using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cathode.Engine;
using static Cathode.Audio.Native.NativeMethods;

namespace Cathode.Audio.Native;

/// <summary>
///     Driver-thread callback durations measured natively around the managed call, as a percentage of the buffer
///     period. Unlike <see cref="CallbackStats" />, this includes time spent waiting to enter the runtime (GC suspension).
/// </summary>
public sealed class CallbackTiming(ulong[] bins, ulong overBudget, double maxPercent) {
    public ulong Count { get; } = bins.Aggregate(0UL, static (a, b) => a + b);

    public ulong OverBudget { get; } = overBudget;

    public double MaxPercent { get; } = maxPercent;

    /// <summary>
    ///     Upper bound (1 % resolution) of the budget percentage that <paramref name="quantile" /> of callbacks stayed
    ///     under.
    /// </summary>
    public double Percentile(double quantile) {
        if (Count == 0) {
            return 0;
        }

        var threshold = (ulong)Math.Ceiling(quantile * Count);
        ulong running = 0;
        for (var i = 0; i < bins.Length; i++) {
            running += bins[i];
            if (running >= threshold) {
                return i + 1;
            }
        }

        return bins.Length;
    }

    /// <summary>Callbacks that took at least <paramref name="percent" /> of their budget.</summary>
    public ulong AtLeast(int percent) =>
        bins.Skip(Math.Clamp(percent, 0, bins.Length - 1)).Aggregate(0UL, static (a, b) => a + b);
}

public sealed record AudioSessionStats(
    ulong Callbacks,
    ulong Overloads,
    int InputLatencyFrames,
    int OutputLatencyFrames,
    long CallbackExceptions
);

/// <summary>
///     Device channels (0-based) for an external effects loop: what the FX Loop blocks send out of and hear back on.
///     A single channel is used for both sides of a stereo pair.
/// </summary>
public sealed record LoopChannels(int[] Returns, int[] Sends) {
    /// <summary>Parses 1-based lists such as <c>3,4</c>; null or empty means none.</summary>
    public static LoopChannels Parse(string? returns, string? sends) => new(List(returns), List(sends));

    static int[] List(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static c => int.Parse(c, CultureInfo.InvariantCulture) - 1)
                .Take(2)
                .ToArray();
}

/// <summary>
///     A running device stream that calls an <see cref="IAudioProcessor" /> on the driver's real-time thread.
///     Input channel 1 feeds both engine channels (a guitar DI is mono). An <see cref="OutputBus" /> maps Output 1
///     (by default to channels 1 and 2), Outputs 2-4 and the DI out to device channels; an output mapped past the
///     device's last channel is dropped. Optional <see cref="LoopChannels" /> carry an external effects loop.
/// </summary>
public sealed unsafe class NativeAudioSession : IDisposable {
    const int MaxFrames = 4096; // CTH_MAX_FRAMES

    readonly IAudioProcessor processor;
    readonly float[] silence = GC.AllocateArray<float>(MaxFrames, true);
    readonly float[] scratchLeft = GC.AllocateArray<float>(MaxFrames, true);
    readonly float[] scratchRight = GC.AllocateArray<float>(MaxFrames, true);
    readonly LoopChannels loop;
    readonly OutputBus outputs;
    GCHandle self;
    nint stream;
    long exceptions;

    public double SampleRate { get; private set; }

    public int BufferFrames { get; private set; }

    public int InputChannels { get; private set; }

    public int OutputChannels { get; private set; }

    /// <summary>Linear gain on the device output. 0 mutes it (the default for unattended tests).</summary>
    public float OutputGain { get; set; } = 1f;

    /// <summary>Driver overloads so far. Unlike <see cref="Stats" /> it does not allocate, so it can be polled often.</summary>
    public long Overloads {
        get {
            NativeMethods.Stats(stream, out var s);
            return (long)s.Overloads;
        }
    }

    NativeAudioSession(IAudioProcessor processor, LoopChannels? loop, OutputBus? outputs) {
        this.processor = processor;
        this.loop = loop ?? new([], []);
        this.outputs = outputs ?? new();
    }

    /// <summary>Opens <paramref name="deviceUid" /> (null for the default output device) without starting it.</summary>
    public static NativeAudioSession Open(
        IAudioProcessor processor,
        string? deviceUid,
        double sampleRate,
        int bufferFrames,
        int inputChannels,
        LoopChannels? loop = null,
        OutputBus? outputs = null
    ) {
        var stream = new NativeAudioSession(processor, loop, outputs);
        if (loop is { Returns.Length: > 0 }) {
            inputChannels = Math.Max(inputChannels, loop.Returns.Max() + 1);
        }

        stream.self = GCHandle.Alloc(stream);
        var config = new StreamConfigNative {
            SampleRate = sampleRate, BufferFrames = bufferFrames, InputChannels = inputChannels
        };
        if (NativeMethods.Open(
                deviceUid,
                ref config,
                &Callback,
                (void*)GCHandle.ToIntPtr(stream.self),
                out stream.stream
            )
            != 0) {
            stream.self.Free();
            throw new InvalidOperationException($"Could not open audio device: {LastErrorMessage()}");
        }

        stream.SampleRate = config.SampleRate;
        stream.BufferFrames = config.BufferFrames;
        stream.InputChannels = config.InputChannels;
        stream.OutputChannels = config.OutputChannels;
        return stream;
    }

    public void Start() {
        if (NativeMethods.Start(stream) != 0) {
            throw new InvalidOperationException($"Could not start audio device: {LastErrorMessage()}");
        }
    }

    public void Stop() {
        if (NativeMethods.Stop(stream) != 0) {
            throw new InvalidOperationException($"Could not stop audio device: {LastErrorMessage()}");
        }
    }

    public AudioSessionStats Stats() {
        NativeMethods.Stats(stream, out var s);
        return new(
            s.Callbacks,
            s.Overloads,
            s.InputLatencyFrames,
            s.OutputLatencyFrames,
            Interlocked.Read(ref exceptions)
        );
    }

    /// <summary>Snapshot of the native callback timing histogram.</summary>
    public CallbackTiming Timing() {
        TimingNative raw;
        NativeMethods.Timing(stream, &raw);
        var bins = new ulong[TimingBins];
        for (var i = 0; i < TimingBins; i++) {
            bins[i] = raw.Bins[i];
        }

        return new(bins, raw.OverBudget, raw.MaxPercent);
    }

    /// <summary>Clears the timing histogram (applied by the audio thread at its next callback).</summary>
    public void ResetTiming() => NativeMethods.ResetTiming(stream);

    public void Dispose() {
        if (stream != 0) {
            Close(stream);
            stream = 0;
        }

        if (self.IsAllocated) {
            self.Free();
        }

        outputs.Dispose();
    }

    [UnmanagedCallersOnly]
    static void Callback(
        void* user,
        float** inputs,
        int inputChannels,
        float** outputs,
        int outputChannels,
        int frames
    ) {
        var self = Unsafe.As<NativeAudioSession>(GCHandle.FromIntPtr((nint)user).Target!);
        try {
            self.Process(inputs, inputChannels, outputs, outputChannels, frames);
        }
#pragma warning disable CA1031 // An exception must never unwind into the driver's thread.
        catch (Exception)
#pragma warning restore CA1031
        {
            Interlocked.Increment(ref self.exceptions);
        }
    }

    void Process(float** inputs, int inputChannels, float** outputs, int outputChannels, int frames) {
        var input =
            inputChannels > 0 ? new ReadOnlySpan<float>(inputs[0], frames) : silence.AsSpan(0, frames);
        var main = this.outputs.Pair(0);
        var left = main >= 0 && main < outputChannels ? new(outputs[main], frames) : scratchLeft.AsSpan(0, frames);
        var right = main >= 0 && main + 1 < outputChannels
            ? new(outputs[main + 1], frames)
            : scratchRight.AsSpan(0, frames);

        if (loop.Returns.Length == 0 && loop.Sends.Length == 0) {
            processor.Process(new(input, input, left, right) { Outputs = this.outputs });
        } else {
            var returnLeft = Channel(inputs, inputChannels, loop.Returns, 0, frames);
            var returnRight = Channel(inputs, inputChannels, loop.Returns, 1, frames);
            var sendLeft = Channel(outputs, outputChannels, loop.Sends, 0, frames);
            var sendRight = Channel(outputs, outputChannels, loop.Sends, 1, frames);
            processor.Process(
                new(input, input, left, right, returnLeft, returnRight, sendLeft, sendRight) { Outputs = this.outputs }
            );
        }

        // the loop's send channels stay live: muting is for the speakers
        var gain = OutputGain;
        Scale(left, gain);
        Scale(right, gain);
        for (var k = 1; k < OutputBus.Count; k++) {
            var pair = this.outputs.Pair(k);
            if (pair >= 0) {
                Write(outputs, outputChannels, pair, this.outputs.Left(k)[..frames], gain);
                Write(outputs, outputChannels, pair + 1, this.outputs.Right(k)[..frames], gain);
            }
        }

        var di = this.outputs.DiChannels;
        if (di.Length > 0) {
            Write(outputs, outputChannels, di[0], this.outputs.DiLeft[..frames], gain);
            if (di.Length > 1) {
                Write(outputs, outputChannels, di[1], this.outputs.DiRight[..frames], gain);
            }
        }
    }

    static void Scale(Span<float> samples, float gain) {
        if (gain == 1f) {
            return;
        }

        for (var i = 0; i < samples.Length; i++) {
            samples[i] *= gain;
        }
    }

    /// <summary>Copies <paramref name="samples" /> to device channel <paramref name="channel" />, if the device has it.</summary>
    static void Write(float** outputs, int count, int channel, ReadOnlySpan<float> samples, float gain) {
        if (channel >= count) {
            return;
        }

        var destination = new Span<float>(outputs[channel], samples.Length);
        samples.CopyTo(destination);
        Scale(destination, gain);
    }

    /// <summary>Loop channel <paramref name="index" /> of <paramref name="map" />, the first one again for a mono loop.</summary>
    static Span<float> Channel(float** channels, int count, int[] map, int index, int frames) {
        if (map.Length == 0) {
            return [];
        }

        var channel = map[Math.Min(index, map.Length - 1)];
        return channel < count ? new(channels[channel], frames) : [];
    }
}
