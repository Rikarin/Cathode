using System.Diagnostics;

namespace Cathode.Server.Tests;

/// <summary>The cathode-host command line, run as the separate process the desktop app and systemd start.</summary>
public sealed class HostCommandLineTests {
    static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static string HostDll() {
        var dll = Path.Combine(
            AppContext.BaseDirectory.Replace(
                Path.Combine("tests", "Cathode.Server.Tests"),
                Path.Combine("src", "Cathode.Host"),
                StringComparison.Ordinal
            ),
            "cathode-host.dll"
        );
        Assert.True(File.Exists(dll), $"cathode-host not built at {dll}");
        return dll;
    }

    internal static async Task<(int Exit, string Output)> RunHostAsync(params string[] args) {
        using var process = new Process {
            StartInfo = new("dotnet", ["exec", HostDll(), ..args]) {
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(Token);
        var stderr = process.StandardError.ReadToEndAsync(Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try {
            await process.WaitForExitAsync(timeout.Token);
        } catch (OperationCanceledException) {
            process.Kill(true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    [Fact]
    public async Task Ir_capture_is_a_command_and_validates_its_options() {
        // Before the fix --ir-capture was parsed as an option: usage was printed and the exit code was 0, so the
        // desktop app took a capture that never ran for a success.
        var (exit, output) = await RunHostAsync("--ir-capture", "--seconds", "1");
        Assert.Equal(1, exit);
        Assert.Contains("--out <file.wav> is required", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_command_is_an_error_but_plain_usage_is_not() {
        var (unknown, output) = await RunHostAsync("--no-such-command");
        Assert.Equal(2, unknown);
        Assert.Contains("--ir-capture", output, StringComparison.Ordinal);

        Assert.Equal(0, (await RunHostAsync()).Exit);
        Assert.Equal(0, (await RunHostAsync("--help")).Exit);
    }

    [Fact]
    public async Task Serve_rejects_a_device_channel_below_one_before_it_starts() {
        var directory = Directory.CreateTempSubdirectory("cth-cli-");
        try {
            var socket = Path.Combine(directory.FullName, "h.sock");
            var (exit, output) = await RunHostAsync(
                "--serve",
                "--null-audio",
                "--socket",
                socket,
                "--telemetry",
                Path.Combine(directory.FullName, "t"),
                "--di-out",
                "0"
            );
            Assert.Equal(1, exit);
            Assert.Contains("--di-out", output, StringComparison.Ordinal);
            Assert.DoesNotContain("serving on", output, StringComparison.Ordinal);
        } finally {
            directory.Delete(true);
        }
    }
}
