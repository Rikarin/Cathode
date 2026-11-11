using Cathode.Ipc;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Cathode.Server.Engine;

/// <summary>
///     Runs offline renders (reamps) for <see cref="Reamp" />. The Server's own is <see cref="HostOfflineRenderer" />;
///     the tests replace it with a render in the same process.
/// </summary>
public interface IOfflineRenderer {
    /// <summary>
    ///     Renders <paramref name="job" /> and returns once its output file is written. Throws a
    ///     <see cref="RigException" /> saying why when it is not.
    /// </summary>
    Task RenderAsync(RenderJob job, CancellationToken cancellationToken);
}

/// <summary>
///     Production: a second cathode-host process per render (<c>--render --job</c>), so the host that plays keeps its
///     audio thread and its collection-free heap to itself (docs/adr/0001-process-model.md). One render runs at a time;
///     a cancelled request kills its process.
/// </summary>
public sealed partial class HostOfflineRenderer(IOptions<ServerOptions> options, ILogger<HostOfflineRenderer> logger)
    : IOfflineRenderer, IDisposable {
    /// <summary>
    ///     A render that takes longer than this is stopped: a stuck process must not hold the queue for ever.
    /// </summary>
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(30);

    readonly SemaphoreSlim gate = new(1, 1);

    public void Dispose() => gate.Dispose();

    public async Task RenderAsync(RenderJob job, CancellationToken cancellationToken) {
        var host = HostPath();
        if (!File.Exists(host)) {
            HostMissing(logger, host);
            throw new RigException(
                503,
                "Offline rendering needs cathode-host, which was not found beside the Server. "
                + "Set Cathode:Engine:HostPath to it."
            );
        }

        var jobPath = Path.Combine(Path.GetTempPath(), $"cathode-render-{Guid.NewGuid():n}.json");
        await gate.WaitAsync(cancellationToken);
        try {
            await File.WriteAllTextAsync(
                jobPath,
                JsonSerializer.Serialize(job, IpcJsonContext.Default.RenderJob),
                cancellationToken
            );
            var (exit, errors) = await RunAsync(host, jobPath, cancellationToken);
            if (exit != 0) {
                throw new RigException(
                    422,
                    "The preset could not be rendered.",
                    errors.Count > 0 ? errors : [$"cathode-host exited with code {exit}."]
                );
            }
        } finally {
            gate.Release();
            File.Delete(jobPath);
        }
    }

    /// <summary>The configured host, or the one a package puts beside the Server.</summary>
    string HostPath() {
        if (options.Value.Engine.HostPath is { Length: > 0 } configured) {
            return configured;
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "cathode-host.exe" : "cathode-host"
        );
    }

    /// <summary>Runs the host on the job; returns its exit code and the reasons it wrote to standard error.</summary>
    static async Task<(int Exit, List<string> Errors)> RunAsync(
        string host,
        string jobPath,
        CancellationToken cancellationToken
    ) {
        // An extension test, not path identity: a development build's host is a .dll for `dotnet`.
#pragma warning disable SK2152
        var managed = host.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
#pragma warning restore SK2152
        using var process = new Process();
        process.StartInfo = new(
            managed ? "dotnet" : host,
            [..managed ? ["exec", host] : Array.Empty<string>(), "--render", "--job", jobPath]
        ) {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        try {
            process.Start();
        } catch (Win32Exception e) {
            throw new RigException(503, $"cathode-host could not be started for the render: {e.Message}");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Limit);
        try {
            // Both pipes are drained while it runs, or a host that writes more than a pipe holds would block.
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = await process.StandardError.ReadToEndAsync(limit.Token);
            await output;
            await process.WaitForExitAsync(limit.Token);
            const string prefix = "error: ";
            return (
                process.ExitCode,
                [
                    ..errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(static line => line.StartsWith(prefix, StringComparison.Ordinal)
                                ? line[prefix.Length..]
                                : line
                        )
                ]
            );
        } catch (OperationCanceledException) {
            process.Kill(true);
            if (cancellationToken.IsCancellationRequested) {
                throw;
            }

            throw new RigException(504, $"The render was stopped after {Limit.TotalMinutes:0} minutes.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No cathode-host for offline renders at {Path}")]
    static partial void HostMissing(ILogger logger, string path);
}
