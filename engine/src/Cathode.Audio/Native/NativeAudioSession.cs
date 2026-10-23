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
///     A running device stream that calls an <see cref="IAudioProcessor" /> on the driver's real-time thread.
///     Input channel 1 feeds both engine channels (a guitar DI is mono); engine L/R go to output channels 1 and 2.
/// </summary>
public sealed unsafe class NativeAudioSession : IDisposable {
    const int MaxFrames = 4096; // CTH_MAX_FRAMES

    readonly IAudioProcessor _processor;
    readonly float[] _silence = GC.AllocateArray<float>(MaxFrames, true);
    readonly float[] _scratchLeft = GC.AllocateArray<float>(MaxFrames, true);
    readonly float[] _scratchRight = GC.AllocateArray<float>(MaxFrames, true);
    GCHandle _self;
    nint _stream;
    long _exceptions;

    public double SampleRate { get; private set; }

    public int BufferFrames { get; private set; }

    public int InputChannels { get; private set; }

    public int OutputChannels { get; private set; }

    /// <summary>Linear gain on the device output. 0 mutes it (the default for unattended tests).</summary>
    public float OutputGain { get; set; } = 1f;

    NativeAudioSession(IAudioProcessor processor) {
        _processor = processor;
    }

    /// <summary>Opens <paramref name="deviceUid" /> (null for the default output device) without starting it.</summary>
    public static NativeAudioSession Open(
        IAudioProcessor processor,
        string? deviceUid,
        double sampleRate,
        int bufferFrames,
        int inputChannels
    ) {
        var stream = new NativeAudioSession(processor);
        stream._self = GCHandle.Alloc(stream);
        var config = new StreamConfigNative {
            SampleRate = sampleRate, BufferFrames = bufferFrames, InputChannels = inputChannels
        };
        if (NativeMethods.Open(
                deviceUid,
                ref config,
                &Callback,
                (void*)GCHandle.ToIntPtr(stream._self),
                out stream._stream
            )
            != 0) {
            stream._self.Free();
            throw new InvalidOperationException($"Could not open audio device: {LastErrorMessage()}");
        }

        stream.SampleRate = config.SampleRate;
        stream.BufferFrames = config.BufferFrames;
        stream.InputChannels = config.InputChannels;
        stream.OutputChannels = config.OutputChannels;
        return stream;
    }

    public void Start() {
        if (NativeMethods.Start(_stream) != 0) {
            throw new InvalidOperationException($"Could not start audio device: {LastErrorMessage()}");
        }
    }

    public void Stop() {
        if (NativeMethods.Stop(_stream) != 0) {
            throw new InvalidOperationException($"Could not stop audio device: {LastErrorMessage()}");
        }
    }

    public AudioSessionStats Stats() {
        NativeMethods.Stats(_stream, out var s);
        return new(
            s.Callbacks,
            s.Overloads,
            s.InputLatencyFrames,
            s.OutputLatencyFrames,
            Interlocked.Read(ref _exceptions)
        );
    }

    /// <summary>Snapshot of the native callback timing histogram.</summary>
    public CallbackTiming Timing() {
        TimingNative raw;
        NativeMethods.Timing(_stream, &raw);
        var bins = new ulong[TimingBins];
        for (var i = 0; i < TimingBins; i++) {
            bins[i] = raw.Bins[i];
        }

        return new(bins, raw.OverBudget, raw.MaxPercent);
    }

    /// <summary>Clears the timing histogram (applied by the audio thread at its next callback).</summary>
    public void ResetTiming() => NativeMethods.ResetTiming(_stream);

    public void Dispose() {
        if (_stream != 0) {
            Close(_stream);
            _stream = 0;
        }

        if (_self.IsAllocated) {
            _self.Free();
        }
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
            Interlocked.Increment(ref self._exceptions);
        }
    }

    void Process(float** inputs, int inputChannels, float** outputs, int outputChannels, int frames) {
        var input =
            inputChannels > 0 ? new ReadOnlySpan<float>(inputs[0], frames) : _silence.AsSpan(0, frames);
        var left = outputChannels > 0 ? new(outputs[0], frames) : _scratchLeft.AsSpan(0, frames);
        var right = outputChannels > 1 ? new(outputs[1], frames) : _scratchRight.AsSpan(0, frames);

        _processor.Process(new(input, input, left, right));

        var gain = OutputGain;
        if (gain != 1f) {
            for (var i = 0; i < frames; i++) {
                left[i] *= gain;
                right[i] *= gain;
            }
        }
    }
}
