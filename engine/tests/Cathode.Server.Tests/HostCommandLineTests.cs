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
        using var process = new Process();
        process.StartInfo = new("dotnet", ["exec", HostDll(), ..args]) {
            RedirectStandardOutput = true, RedirectStandardError = true
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

    [Theory]
    [InlineData("--rate", "0")]
    [InlineData("--rate", "fast")]
    [InlineData("--buffer", "0")]
    [InlineData("--buffer", "99999999999")]
    [InlineData("--buffer", "64.5")]
    public async Task Serve_rejects_a_rate_or_buffer_out_of_range_with_one_line(string option, string value) {
        // --rate 0 used to divide by zero on the null backend's audio thread, and a buffer too large for an int was an
        // unhandled OverflowException: a stack trace and a crash loop under a supervisor.
        var directory = Directory.CreateTempSubdirectory("cth-cli-");
        try {
            var (exit, output) = await RunHostAsync(
                "--serve",
                "--null-audio",
                "--socket",
                Path.Combine(directory.FullName, "h.sock"),
                "--telemetry",
                Path.Combine(directory.FullName, "t"),
                option,
                value
            );
            Assert.Equal(1, exit);
            Assert.StartsWith($"error: {option} must be", output, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
        } finally {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("--seconds", "0")]
    [InlineData("--seconds", "-1")]
    [InlineData("--tail", "-1")]
    [InlineData("--length", "0")]
    public async Task Ir_capture_rejects_a_duration_that_is_not_positive(string option, string value) {
        var directory = Directory.CreateTempSubdirectory("cth-cli-");
        try {
            var (exit, output) = await RunHostAsync(
                "--ir-capture",
                "--out",
                Path.Combine(directory.FullName, "ir.wav"),
                option,
                value
            );
            Assert.Equal(1, exit);
            Assert.StartsWith($"error: {option} must be", output, StringComparison.Ordinal);
        } finally {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Ir_capture_checks_that_the_file_can_be_written_before_it_plays_the_sweep() {
        var directory = Directory.CreateTempSubdirectory("cth-cli-");
        try {
            var (exit, output) = await RunHostAsync(
                "--ir-capture",
                "--out",
                Path.Combine(directory.FullName, "no-such-folder", "ir.wav")
            );
            Assert.Equal(1, exit);
            Assert.StartsWith("error: ", output, StringComparison.Ordinal);
            Assert.Contains("no-such-folder", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Playing", output, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
        } finally {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task A_graph_file_that_is_missing_or_not_a_graph_is_a_one_line_error() {
        var directory = Directory.CreateTempSubdirectory("cth-cli-");
        try {
            var missing = Path.Combine(directory.FullName, "missing.json");
            var (exit, output) = await RunHostAsync("--bench-nam", "--graph", missing);
            Assert.Equal(1, exit);
            Assert.StartsWith("error: ", output, StringComparison.Ordinal);
            Assert.Contains("missing.json", output, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);

            var broken = Path.Combine(directory.FullName, "broken.json");
            await File.WriteAllTextAsync(broken, "{ \"nodes\": [", Token);
            (exit, output) = await RunHostAsync("--bench-nam", "--graph", broken);
            Assert.Equal(1, exit);
            Assert.StartsWith("error: ", output, StringComparison.Ordinal);
            Assert.Contains("broken.json is not a graph", output, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
        } finally {
            directory.Delete(true);
        }
    }
}
