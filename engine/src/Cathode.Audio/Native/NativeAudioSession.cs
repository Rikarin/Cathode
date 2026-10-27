using Cathode.Engine;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    /// <summary>
    ///     Parses 1-based lists such as <c>3,4</c>; null or empty means none. A channel below 1 is a
    ///     <see cref="FormatException" />: it would index before the driver's channel-pointer array on the audio thread.
    /// </summary>
    public static LoopChannels Parse(string? returns, string? sends) =>
        new(List(returns, "--loop-in"), List(sends, "--loop-out"));

    internal static int[] List(string? text, string option) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => Channel(c, option))
                .Take(2)
                .ToArray();

    /// <summary>A 1-based device channel as a 0-based index; anything below 1 is rejected.</summary>
    static int Channel(string text, string option) {
        var channel = int.Parse(text, CultureInfo.InvariantCulture);
        return channel >= 1
            ? channel - 1
            : throw new FormatException(
                $"{option}: device channels are numbered from 1, so '{text}' is not a channel."
            );
    }
}

/// <summary>
///     Device channels (0-based) for each hardware input source (<see cref="Dsp.Routing.HardwareInputs" />: Input 1–4,
///     USB 5/6, USB 7/8): one channel feeds both sides of a source (a guitar DI is mono), two make a stereo pair, none
///     leaves the source silent. The default maps only Input 1, to the device's first channel.
/// </summary>
public sealed record InputMap(int[][] Sources) {
    public static InputMap Default { get; } = new([[0]]);

    /// <summary>One bit per source with device channels (bit 0 = Input 1).</summary>
    public int Mask {
        get {
            var mask = 0;
            for (var s = 0; s < Sources.Length; s++) {
                mask |= Sources[s].Length > 0 ? 1 << s : 0;
            }

            return mask;
        }
    }

    /// <summary>How many device input channels the map needs opened.</summary>
    public int Channels => Sources.Select(static s => s.Length == 0 ? 0 : s.Max() + 1).DefaultIfEmpty(0).Max();

    /// <summary>Whether a source other than Input 1 is mapped, so callbacks must carry every source.</summary>
    public bool HasExtraSources => Sources.Skip(1).Any(static s => s.Length > 0);

    /// <summary>
    ///     Parses 1-based channel lists, one per source in order, separated by semicolons: <c>1;3,4;;5,6</c> maps Input
    ///     1 to channel 1, Input 2 to channels 3 and 4, leaves Input 3 empty and puts Input 4 on 5 and 6. Null or empty
    ///     text, or an empty Input 1, keeps Input 1 on channel 1.
    /// </summary>
    public static InputMap Parse(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return Default;
        }

        var sources = text.Split(';')
            .Take(Dsp.Routing.HardwareInputs.Count)
            .Select(static s => LoopChannels.List(s, "--input-map"))
            .ToArray();
        if (sources[0].Length == 0) {
            sources[0] = [0];
        }

        return new(sources);
    }
}

/// <summary>
///     A running device stream that calls an <see cref="IAudioProcessor" /> on the driver's real-time thread.
///     The <see cref="InputMap" />'s Input 1 is the engine's main input (by default channel 1 on both sides: a guitar
///     DI is mono), and its other sources travel in <see cref="AudioIO.Sources" />. An <see cref="OutputBus" /> maps
///     Output 1 (by default to channels 1 and 2), Outputs 2-4 and the DI out to device channels; an output mapped past
///     the device's last channel is dropped. Optional <see cref="LoopChannels" /> carry an external effects loop.
/// </summary>
public sealed unsafe class NativeAudioSession : IDisposable {
    const int MaxFrames = 4096; // CTH_MAX_FRAMES

    readonly IAudioProcessor processor;
    readonly float[] silence = GC.AllocateArray<float>(MaxFrames, true);
    readonly float[] scratchLeft = GC.AllocateArray<float>(MaxFrames, true);
    readonly float[] scratchRight = GC.AllocateArray<float>(MaxFrames, true);
    readonly LoopChannels loop;
    readonly OutputBus outputs;
    readonly InputMap inputs;
    readonly float[] sources;
    readonly int sourceMask;
    GCHandle self;
    nint stream;
    long exceptions;
    readonly Lock watchdogLock = new();
    bool running;
    ulong lastCallbacks;
    long lastCallbackAt; // Stopwatch timestamp when the callback count last moved (or the stream started)
    Exception? callbackException; // the first exception not yet taken by TakeCallbackException

    public double SampleRate { get; private set; }

    public int BufferFrames { get; private set; }

    public int InputChannels { get; private set; }

    public int OutputChannels { get; private set; }

    /// <summary>Linear gain on the device output. 0 mutes it (the default for unattended tests).</summary>
    public float OutputGain { get; set; } = 1f;

    /// <summary>A started stream whose callback has not run for this long is reported as lost.</summary>
    public static readonly TimeSpan CallbackStall = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Control side, allocation-free: what the backend reports about the device (<c>cth_audio_status</c>), or
    ///     <see cref="AudioDeviceStatus.Lost" /> when a started stream's callback has stalled for
    ///     <see cref="CallbackStall" /> (the backstop for a backend that cannot tell).
    /// </summary>
    public AudioDeviceStatus DeviceStatus {
        get {
            var status = (AudioDeviceStatus)NativeMethods.Status(stream);
            if (status != AudioDeviceStatus.Ok) {
                return status;
            }

            lock (watchdogLock) {
                if (!running) {
                    return status;
                }

                NativeMethods.Stats(stream, out var s);
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (s.Callbacks != lastCallbacks) {
                    lastCallbacks = s.Callbacks;
                    lastCallbackAt = now;
                    return status;
                }

                return System.Diagnostics.Stopwatch.GetElapsedTime(lastCallbackAt, now) >= CallbackStall
                    ? AudioDeviceStatus.Lost
                    : status;
            }
        }
    }

    /// <summary>Driver overloads so far. Unlike <see cref="Stats" /> it does not allocate, so it can be polled often.</summary>
    public long Overloads {
        get {
            NativeMethods.Stats(stream, out var s);
            return (long)s.Overloads;
        }
    }

    NativeAudioSession(IAudioProcessor processor, LoopChannels? loop, InputMap? inputs, OutputBus? outputs) {
        this.processor = processor;
        this.loop = loop ?? new([], []);
        this.outputs = outputs ?? new();
        this.inputs = inputs ?? InputMap.Default;
        sourceMask = this.inputs.Mask;
        sources = this.inputs.HasExtraSources
            ? GC.AllocateArray<float>(Dsp.Routing.HardwareInputs.Count * 2 * MaxFrames, true)
            : [];
    }

    /// <summary>
    ///     Exceptions the processor threw on the audio thread so far. Each one leaves that callback's output silent
    ///     (the backends clear it before the call), so a persistent one sounds like a dead interface.
    /// </summary>
    public long CallbackExceptions => Interlocked.Read(ref exceptions);

    /// <summary>
    ///     Control side: the first callback exception since the previous call, or null. Later ones are only counted, so
    ///     the audio thread never allocates for them.
    /// </summary>
    public Exception? TakeCallbackException() => Interlocked.Exchange(ref callbackException, null);

    /// <summary>The input map the session was opened with.</summary>
    public InputMap Inputs => inputs;

    /// <summary>Opens <paramref name="deviceUid" /> (null for the default output device) without starting it.</summary>
    public static NativeAudioSession Open(
        IAudioProcessor processor,
        string? deviceUid,
        double sampleRate,
        int bufferFrames,
        int inputChannels,
        LoopChannels? loop = null,
        InputMap? inputs = null,
        OutputBus? outputs = null
    ) {
        var stream = new NativeAudioSession(processor, loop, inputs, outputs);
        if (loop is { Returns.Length: > 0 }) {
            inputChannels = Math.Max(inputChannels, loop.Returns.Max() + 1);
        }

        inputChannels = Math.Max(inputChannels, stream.inputs.Channels);

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

        lock (watchdogLock) {
            NativeMethods.Stats(stream, out var s);
            lastCallbacks = s.Callbacks;
            lastCallbackAt = System.Diagnostics.Stopwatch.GetTimestamp();
            running = true;
        }
    }

    public void Stop() {
        lock (watchdogLock) {
            running = false;
        }

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
        catch (Exception e)
#pragma warning restore CA1031
        {
            Interlocked.Increment(ref self.exceptions);
            Interlocked.CompareExchange(ref self.callbackException, e, null);
        }
    }

    void Process(float** inputs, int inputChannels, float** outputs, int outputChannels, int frames) {
        var source = this.inputs.Sources[0];
        var inputLeft = Input(inputs, inputChannels, source, 0, frames);
        var inputRight = Input(inputs, inputChannels, source, 1, frames);
        var main = this.outputs.Pair(0);
        var left = main >= 0 && main < outputChannels ? new(outputs[main], frames) : scratchLeft.AsSpan(0, frames);
        var right = main >= 0 && main + 1 < outputChannels
            ? new(outputs[main + 1], frames)
            : scratchRight.AsSpan(0, frames);

        if (sources.Length > 0) {
            FillSources(inputs, inputChannels, frames);
        }

        if (loop.Returns.Length == 0 && loop.Sends.Length == 0 && sources.Length == 0) {
            processor.Process(new(inputLeft, inputRight, left, right) { Outputs = this.outputs });
        } else {
            var returnLeft = Channel(inputs, inputChannels, loop.Returns, 0, frames);
            var returnRight = Channel(inputs, inputChannels, loop.Returns, 1, frames);
            var sendLeft = Channel(outputs, outputChannels, loop.Sends, 0, frames);
            var sendRight = Channel(outputs, outputChannels, loop.Sends, 1, frames);
            processor.Process(
                new(
                    inputLeft,
                    inputRight,
                    left,
                    right,
                    returnLeft,
                    returnRight,
                    sendLeft,
                    sendRight,
                    sources.AsSpan(0, sources.Length == 0 ? 0 : Dsp.Routing.HardwareInputs.Count * 2 * frames),
                    sourceMask
                ) { Outputs = this.outputs }
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
        if ((uint)channel >= (uint)count) {
            return;
        }

        var destination = new Span<float>(outputs[channel], samples.Length);
        samples.CopyTo(destination);
        Scale(destination, gain);
    }

    /// <summary>Side <paramref name="side" /> of an input source: its second channel, or its first again when mono.</summary>
    ReadOnlySpan<float> Input(float** channels, int count, int[] map, int side, int frames) {
        var channel = map.Length == 0 ? -1 : map[Math.Min(side, map.Length - 1)];
        return channel >= 0 && channel < count
            ? new ReadOnlySpan<float>(channels[channel], frames)
            : silence.AsSpan(0, frames);
    }

    /// <summary>Copies every mapped source into <see cref="sources" />, planar, as <see cref="AudioIO.Sources" /> wants.</summary>
    void FillSources(float** channels, int count, int frames) {
        var map = inputs.Sources;
        for (var s = 0; s < Dsp.Routing.HardwareInputs.Count; s++) {
            var source = s < map.Length ? map[s] : [];
            Input(channels, count, source, 0, frames).CopyTo(sources.AsSpan(2 * s * frames, frames));
            Input(channels, count, source, 1, frames).CopyTo(sources.AsSpan((2 * s + 1) * frames, frames));
        }
    }

    /// <summary>Loop channel <paramref name="index" /> of <paramref name="map" />, the first one again for a mono loop.</summary>
    static Span<float> Channel(float** channels, int count, int[] map, int index, int frames) {
        if (map.Length == 0) {
            return [];
        }

        var channel = map[Math.Min(index, map.Length - 1)];
        return (uint)channel < (uint)count ? new(channels[channel], frames) : [];
    }
}
