using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Ipc;
using System.Diagnostics;
using System.Text.Json;

namespace Cathode.Host;

/// <summary>
///     <c>--render --job job.json</c>: an offline render (reamp) of a WAV file through a graph, which the Server
///     starts as a process of its own for <c>POST /live/render</c> (<see cref="RenderJob" />). No device, no socket.
/// </summary>
static class RenderCommand {
    /// <summary>
    ///     0 when the output file was written; 1, with each reason on a line of standard error, when not.
    /// </summary>
    public static int Run(CommandLine options) {
        var path = options.Text("job") ?? throw new InvalidOperationException("--job <job.json> is required.");
        try {
            var job = JsonSerializer.Deserialize(File.ReadAllText(path), IpcJsonContext.Default.RenderJob)
                ?? throw new InvalidDataException("The job file is empty.");
            var started = Stopwatch.GetTimestamp();
            var (frames, rate) = OfflineReamp.Run(job);
            Console.WriteLine(
                $"Rendered {(double)frames / rate:0.0} s at {rate} Hz in "
                + $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s -> {job.Output}"
            );
            return 0;
        } catch (GraphValidationException e) {
            foreach (var error in e.Errors) {
                Console.Error.WriteLine($"error: {error}");
            }

            return 1;
        } catch (Exception e) when (e is InvalidDataException
                                        or EndOfStreamException
                                        or IOException
                                        or UnauthorizedAccessException
                                        or JsonException
                                        or NotSupportedException) {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }
}
