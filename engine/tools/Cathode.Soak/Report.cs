using Cathode.Contracts;
using System.Globalization;
using System.Text;

namespace Cathode.Soak;

sealed record Report(bool Passed, string Text) {
    /// <summary>
    ///     Summarises a run. <c>maxHostGcs</c> is the highest acceptable number of host collections (any generation);
    ///     negative disables that gate.
    /// </summary>
    public static Report Build(EngineStatus first, EngineStatus last, Api api, TimeSpan elapsed, int maxHostGcs) {
        var overloads = last.DriverOverloads - first.DriverOverloads;
        var hostGcs = last.HostGen0 - first.HostGen0; // every collection counts as gen0 too
        var gcGate = maxHostGcs < 0 || hostGcs <= maxHostGcs;
        var passed = overloads == 0 && api.Errors == 0 && last.Connected && gcGate;
        var text = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        text.AppendLine();
        text.AppendLine(
            c,
            $"soak: {elapsed.TotalMinutes:0.0} min on {last.Backend}, {last.SampleRate / 1000:0.#} kHz / {last.BufferFrames} frames"
        );
        text.AppendLine(c, $"  driver overloads:     {overloads}");
        text.AppendLine(
            c,
            $"  native callback:      worst window p99.9 {api.WorstNativeP999:0}%, worst max {api.WorstNativeMax:0.0}% of budget"
        );
        text.AppendLine(c, $"  engine (managed):     worst max {api.WorstCpuMax:0.0}% of budget");
        text.AppendLine(
            c,
            $"  host GCs:             gen0 {last.HostGen0 - first.HostGen0}, gen1 {last.HostGen1 - first.HostGen1}, gen2 {last.HostGen2 - first.HostGen2}, pause {last.HostGcPauseMs - first.HostGcPauseMs:0.0} ms"
        );
        text.AppendLine(
            c,
            $"  host allocations:     {(last.HostAllocatedBytes - first.HostAllocatedBytes) / 1e6 / elapsed.TotalMinutes:0.00} MB/min"
        );
        text.AppendLine(
            c,
            $"  API:                  {api.Requests} requests, {api.Errors} errors; {api.TelemetryMessages} WebSocket messages"
        );
        foreach (var message in api.ErrorMessages) {
            text.AppendLine(c, $"    error: {message}");
        }

        if (maxHostGcs >= 0) {
            text.AppendLine(
                c,
                $"  host GC gate:         {hostGcs} collection(s), at most {maxHostGcs} allowed: {(gcGate ? "ok" : "FAILED")}"
            );
        }

        text.AppendLine(passed ? "soak: PASS" : "soak: FAIL");
        return new(passed, text.ToString());
    }
}
