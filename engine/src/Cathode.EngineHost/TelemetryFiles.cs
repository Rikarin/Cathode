using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Cathode.EngineHost;

/// <summary>
///     Where a serving host keeps its shared-memory telemetry file when it is not told: next to the socket, one per
///     process, so a restarted host never truncates a file a Server still has mapped (Windows forbids that).
/// </summary>
public static class TelemetryFiles {
    const string Extension = ".telemetry";

    /// <summary>The file of the host with process id <paramref name="processId" />.</summary>
    public static string PathFor(string socketPath, int processId) =>
        string.Create(CultureInfo.InvariantCulture, $"{socketPath}.{processId}{Extension}");

    /// <summary>
    ///     Deletes the files that hosts on <paramref name="socketPath" /> left behind when they were killed or crashed
    ///     (a clean stop deletes its own): those whose process is no longer running. Best effort: a file that cannot
    ///     be deleted (a Server on Windows still has it mapped) is left for the next start. Returns how many went.
    /// </summary>
    public static int RemoveStale(string socketPath) {
        var full = Path.GetFullPath(socketPath);
        var directory = Path.GetDirectoryName(full);
        var prefix = Path.GetFileName(full) + ".";
        if (directory is null || !Directory.Exists(directory)) {
            return 0;
        }

        var removed = 0;
        try {
            foreach (var file in Directory.EnumerateFiles(directory, prefix + "*" + Extension)) {
                // The pattern also matches other sockets' files ("a.sock.1.telemetry" for "a.sock" and "a"): only
                // "<socket>.<digits>.telemetry" is ours.
                var name = Path.GetFileName(file);
                if (name.Length <= prefix.Length + Extension.Length
                    || !name.StartsWith(prefix, StringComparison.Ordinal)
                    || !int.TryParse(
                        name.AsSpan(prefix.Length, name.Length - prefix.Length - Extension.Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var processId
                    )
                    || IsRunning(processId)) {
                    continue;
                }

                try {
                    File.Delete(file);
                    removed++;
                } catch (IOException) {
                    // Still mapped by a Server, or gone already.
                } catch (UnauthorizedAccessException) {
                    // Another user's: theirs to clean up.
                }
            }
        } catch (IOException) {
            // The folder cannot be listed: nothing to clean.
        } catch (UnauthorizedAccessException) {
            // As above.
        }

        return removed;
    }

    /// <summary>
    ///     Whether a process with this id exists. A reused id reads as running, which only keeps a stale file until a
    ///     later start.
    /// </summary>
    static bool IsRunning(int processId) {
        if (processId == Environment.ProcessId) {
            return true;
        }

        try {
            using var process = Process.GetProcessById(processId);
            return true;
        } catch (ArgumentException) {
            return false; // no such process
        } catch (InvalidOperationException) {
            return false; // it exited while being looked up
        } catch (Win32Exception) {
            return true; // it exists but may not be inspected
        }
    }
}
