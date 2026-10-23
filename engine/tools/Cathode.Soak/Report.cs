using System.Globalization;
using System.Text;
using Cathode.Contracts;

namespace Cathode.Soak;

sealed record Report(bool Passed, string Text) {
    public static Report Build(EngineStatus first, EngineStatus last, Api api, TimeSpan elapsed) {
        var overloads = last.DriverOverloads - first.DriverOverloads;
        var passed = overloads == 0 && api.Errors == 0 && last.Connected;
        var text = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        text.AppendLine();
        text.AppendLine(c, $"soak: {elapsed.TotalMinutes:0.0} min on {last.Backend}, {last.SampleRate / 1000:0.#} kHz / {last.BufferFrames} frames");
        text.AppendLine(c, $"  driver overloads:     {overloads}");
        text.AppendLine(c, $"  native callback:      worst window p99.9 {api.WorstNativeP999:0}%, worst max {api.WorstNativeMax:0.0}% of budget");
        text.AppendLine(c, $"  engine (managed):     worst max {api.WorstCpuMax:0.0}% of budget");
        text.AppendLine(c, $"  host GCs:             gen0 {last.HostGen0 - first.HostGen0}, gen1 {last.HostGen1 - first.HostGen1}, gen2 {last.HostGen2 - first.HostGen2}, pause {last.HostGcPauseMs - first.HostGcPauseMs:0.0} ms");
        text.AppendLine(c, $"  host allocations:     {(last.HostAllocatedBytes - first.HostAllocatedBytes) / 1e6 / elapsed.TotalMinutes:0.00} MB/min");
        text.AppendLine(c, $"  API:                  {api.Requests} requests, {api.Errors} errors; {api.TelemetryMessages} WebSocket messages");
        foreach (var message in api.ErrorMessages) {
            text.AppendLine(c, $"    error: {message}");
        }

        text.AppendLine(passed ? "soak: PASS" : "soak: FAIL");
        return new(passed, text.ToString());
    }
}
