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

    /// <summary>The device's input and output latency in frames, as the driver reports them (each with one buffer).</summary>
    (int Input, int Output) Latency { get; }
}

/// <summary>Reads a <see cref="NativeAudioSession" />'s overload counter and native timing histogram.</summary>
public sealed class NativeDeviceMonitor(NativeAudioSession session) : IDeviceMonitor {
    public long Overloads => session.Overloads;

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
