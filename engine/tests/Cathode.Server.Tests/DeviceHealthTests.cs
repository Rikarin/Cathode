using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.EngineHost;
using Cathode.Ipc;

namespace Cathode.Server.Tests;

/// <summary>What the host reports about the audio device and the audio thread beyond meters and CPU.</summary>
public sealed class DeviceHealthTests {
    [Fact]
    public void Callback_exceptions_are_published_and_the_first_is_logged_with_its_stack_trace() {
        var device = new FakeDevice();
        var log = new List<string>();
        using var service = new EngineHostService(ProcessSpec.Default, "test", device: device);
        service.Log = log.Add;
        var telemetry = new HostTelemetry();

        service.Telemetry(telemetry);
        Assert.Equal(0, telemetry.CallbackExceptions);
        Assert.Empty(log);

        device.Throw(new ArgumentOutOfRangeException("slot", "routing index"), 3);
        service.Telemetry(telemetry);
        Assert.Equal(3, telemetry.CallbackExceptions);
        var line = Assert.Single(log);
        Assert.Contains("ArgumentOutOfRangeException", line, StringComparison.Ordinal);
        Assert.Contains("routing index", line, StringComparison.Ordinal);
        Assert.Contains(nameof(FakeDevice), line, StringComparison.Ordinal); // the stack trace

        // More of the same within 10 s are counted, not logged again.
        device.Throw(new ArgumentOutOfRangeException("slot", "routing index"), 50);
        service.Telemetry(telemetry);
        Assert.Equal(53, telemetry.CallbackExceptions);
        Assert.Single(log);
    }

    [Fact]
    public void A_lost_or_changed_device_is_published_in_telemetry_without_allocating() {
        var device = new FakeDevice();
        using var service = new EngineHostService(ProcessSpec.Default, "test", device: device);
        var telemetry = new HostTelemetry();

        service.Telemetry(telemetry);
        Assert.Equal("ok", telemetry.DeviceStatus);
        Assert.Equal(AudioDeviceStatus.Ok, service.DeviceStatus);

        device.Status = AudioDeviceStatus.Changed;
        service.Telemetry(telemetry);
        Assert.Equal("changed", telemetry.DeviceStatus);

        device.Status = AudioDeviceStatus.Lost;
        Assert.Equal(0, Testing.AllocationProbe.Measure(() => service.Telemetry(telemetry)));
        Assert.Equal("lost", telemetry.DeviceStatus);
        Assert.Equal(AudioDeviceStatus.Lost, service.DeviceStatus);
    }

    [Fact]
    public void Without_a_device_the_status_is_ok() {
        using var service = new EngineHostService(ProcessSpec.Default, "null");
        Assert.Equal(AudioDeviceStatus.Ok, service.DeviceStatus);
        Assert.Equal("ok", service.Telemetry(new HostTelemetry()).DeviceStatus);
    }

    sealed class FakeDevice : IDeviceMonitor {
        long exceptions;
        Exception? first;

        public AudioDeviceStatus Status { get; set; }

        public long Overloads => 0;

        public (int Input, int Output) Latency => (0, 0);

        public long CallbackExceptions => exceptions;

        public DeviceWindow CloseWindow() => new(0, 0, 0);

        public Exception? TakeCallbackException() => Interlocked.Exchange(ref first, null);

        public void Throw(Exception exception, int times) {
            try {
                throw exception; // gives it a stack trace, as one thrown on the audio thread has
            } catch (Exception e) {
                first ??= e;
            }

            exceptions += times;
        }
    }
}
