using System.Diagnostics;
using System.Net.Http.Json;
using Cathode.Contracts;

namespace Cathode.Server.Tests;

/// <summary>
///     The production layout (docs/adr/0001-process-model.md): cathode-host in its own process, the Server talking to
///     it over a Unix domain socket.
/// </summary>
public sealed class TwoProcessTests : IAsyncDisposable {
    readonly System.Text.StringBuilder hostOutput = new();
    readonly string socketPath = Path.Combine(Path.GetTempPath(), $"cth-{Guid.NewGuid().ToString("n")[..8]}.sock"); // Unix socket paths are limited to ~104 bytes on macOS
    Process? host;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() {
        await StopHostAsync();
        File.Delete(socketPath);
        lock (hostOutput) {
            TestContext.Current.TestOutputHelper?.WriteLine("cathode-host output:\n" + hostOutput);
        }
    }

    [Fact]
    public async Task Server_drives_a_separate_host_and_resyncs_after_the_host_restarts() {
        StartHost();
        await using var server = new ServerFixture(socketPath);
        using var client = await server.ConnectedClientAsync();

        var system = await System(client);
        Assert.Equal("ipc", system.Engine.Mode);
        Assert.Equal("null", system.Engine.Backend);

        await Ok(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2), Token));
        await Ok(client.PatchAsJsonAsync("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["time"] = 250 }, Token));
        await ServerFixture.Eventually(async () => (await System(client)).Engine.LastError is null);

        await StopHostAsync();
        await ServerFixture.Eventually(async () => !(await System(client)).Engine.Connected);
        StartHost();
        // Generous: the server retries every second, and slower CI machines take a while to start a host process.
        await ServerFixture.Eventually(async () => (await System(client)).Engine.Connected, 90);

        // The new host knows nothing; the Server must have resent the whole rig. Otherwise these would fail with
        // "stale graph generation" or "no block 'delay1'".
        await Ok(client.PatchAsJsonAsync("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["time"] = 300 }, Token));
        await Ok(client.PutAsJsonAsync("/api/v1/live/blocks/delay1/bypass", new BypassRequest(true), Token));
        await Task.Delay(300, Token);
        Assert.Null((await System(client)).Engine.LastError);
    }

    void StartHost() {
        var dll = Path.Combine(AppContext.BaseDirectory.Replace(Path.Combine("tests", "Cathode.Server.Tests"), Path.Combine("src", "Cathode.Host"), StringComparison.Ordinal), "cathode-host.dll");
        Assert.True(File.Exists(dll), $"cathode-host not built at {dll}");
        host = new Process {
            StartInfo = new("dotnet", ["exec", dll, "--serve", "--null-audio", "--socket", socketPath]) {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        host.OutputDataReceived += (_, e) => Capture(e.Data);
        host.ErrorDataReceived += (_, e) => Capture(e.Data);
        host.Start();
        host.BeginOutputReadLine();
        host.BeginErrorReadLine();
    }

    void Capture(string? line) {
        if (line is not null) {
            lock (hostOutput) {
                hostOutput.AppendLine(line);
            }
        }
    }

    async Task StopHostAsync() {
        if (host is { HasExited: false }) {
            host.Kill(true);
            await host.WaitForExitAsync(Token);
        }

        host?.Dispose();
        host = null;
    }

    async Task<SystemInfo> System(HttpClient client) => (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!;

    static async Task Ok(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
