using Cathode.Audio.Native;

namespace Cathode.EngineHost;

/// <summary>Native callback timing and driver overloads for one measurement window.</summary>
public readonly record struct DeviceWindow(long Overloads, double P999, double Max);

/// <summary>Driver-side health of the audio device, read by the host once per telemetry window.</summary>
public interface IDeviceMonitor {
    /// <summary>Cumulative driver overloads, read without allocating (polled at the telemetry rate).</summary>
    long Overloads { get; }

    /// <summary>Returns the window that just ended (overloads are cumulative) and starts a new one.</summary>
    DeviceWindow CloseWindow();

    /// <summary>
    ///     The device's input and output latency in frames, as the driver reports them (each with one buffer).
    /// </summary>
    (int Input, int Output) Latency { get; }

    /// <summary>Whether the device is still the one the engine was prepared for; read without allocating.</summary>
    AudioDeviceStatus Status => AudioDeviceStatus.Ok;

    /// <summary>Cumulative exceptions thrown by the engine on the audio thread (each silences one callback).</summary>
    long CallbackExceptions => 0;

    /// <summary>The first callback exception since the previous call, or null.</summary>
    Exception? TakeCallbackException() => null;
}

/// <summary>Reads a <see cref="NativeAudioSession" />'s overload counter and native timing histogram.</summary>
public sealed class NativeDeviceMonitor(NativeAudioSession session) : IDeviceMonitor {
    public long Overloads => session.Overloads;

    public long CallbackExceptions => session.CallbackExceptions;

    public AudioDeviceStatus Status => session.DeviceStatus;

    public Exception? TakeCallbackException() => session.TakeCallbackException();

    public (int Input, int Output) Latency {
        get {
            var stats = session.Stats();
            return (stats.InputLatencyFrames, stats.OutputLatencyFrames);
        }
    }

    public DeviceWindow CloseWindow() {
        var timing = session.Timing();
        session.ResetTiming();
        return new((long)session.Stats().Overloads, timing.Percentile(0.999), timing.MaxPercent);
    }
}
