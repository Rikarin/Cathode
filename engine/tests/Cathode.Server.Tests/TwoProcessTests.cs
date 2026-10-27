using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Cathode.Server.Tests;

/// <summary>
///     The production layout (docs/adr/0001-process-model.md): cathode-host in its own process, the Server talking to
///     it over a Unix domain socket.
/// </summary>
public sealed class TwoProcessTests : IAsyncDisposable {
    readonly StringBuilder hostOutput = new();

    readonly string socketPath = Path.Combine(
        Path.GetTempPath(),
        $"cth-{Guid.NewGuid().ToString("n")[..8]}.sock"
    ); // Unix socket paths are limited to ~104 bytes on macOS

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
        var connection = server.Services.GetRequiredService<EngineConnection>();
        Assert.True(connection.HasSharedTelemetry, "meters come from the host's shared-memory region");

        await Ok(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2), Token));
        await Ok(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/delay1/params",
                new Dictionary<string, float> { ["time"] = 250 },
                Token
            )
        );
        await ServerFixture.Eventually(async () => (await System(client)).Engine.LastError is null);

        await StopHostAsync();
        await ServerFixture.Eventually(async () => !(await System(client)).Engine.Connected);
        StartHost();
        // Generous: the server retries every second, and slower CI machines take a while to start a host process.
        await ServerFixture.Eventually(async () => (await System(client)).Engine.Connected, 90);

        // The new host knows nothing; the Server must have resent the whole rig. Otherwise these would fail with
        // "stale graph generation" or "no block 'delay1'".
        await Ok(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/delay1/params",
                new Dictionary<string, float> { ["time"] = 300 },
                Token
            )
        );
        await Ok(client.PutAsJsonAsync("/api/v1/live/blocks/delay1/bypass", new BypassRequest(true), Token));
        await Task.Delay(300, Token);
        Assert.Null((await System(client)).Engine.LastError);
        Assert.True(connection.HasSharedTelemetry, "the restarted host's region is mapped too");
    }

    /// <summary>
    ///     End to end over the socket: a factory preset loaded and edited through the Server's API plays in the
    ///     separate host, whose meters reach a WebSocket client through the shared-memory region; after the host is
    ///     killed and started again, the same preset, with the edit, is playing and metered again.
    /// </summary>
    [Fact]
    public async Task A_preset_loaded_through_the_server_plays_in_the_host_and_survives_its_restart() {
        StartHost();
        await using var server = new ServerFixture(socketPath);
        using var client = await server.ConnectedClientAsync();

        var factory = (await client.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token))!
            .First(static p => p.Factory == true);
        await Ok(client.PutAsync($"/api/v1/live/preset/{factory.Id}", null, Token));
        var amp = (await Live(client)).Preset.Blocks.First(static b => b.Type == "circuitamp").Id;
        await Ok(
            client.PatchAsJsonAsync(
                $"/api/v1/live/blocks/{amp}/params",
                new Dictionary<string, float> { ["master"] = 3.5f },
                Token
            )
        );
        await ServerFixture.Eventually(async () => (await System(client)).Engine.LastError is null);

        using var socket = await server.Server.CreateWebSocketClient()
            .ConnectAsync(new(server.Server.BaseAddress, "ws"), Token);
        await ExpectMetered(client, socket, amp);

        await StopHostAsync();
        await ServerFixture.Eventually(async () => !(await System(client)).Engine.Connected);
        StartHost();
        await ServerFixture.Eventually(async () => (await System(client)).Engine.Connected, 90);

        var live = await Live(client);
        Assert.Equal(factory.Id, live.Preset.Id);
        Assert.Contains(
            live.Preset.Blocks.First(b => b.Id == amp).Channels.Values,
            static c => c.TryGetValue("master", out var master) && master == 3.5f
        );
        // The new host plays the resent rig: a block of the preset takes a test tone and meters it.
        await ExpectMetered(client, socket, amp);
        Assert.Null((await System(client)).Engine.LastError);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
    }

    [Fact]
    public async Task Host_watching_its_stdin_stops_cleanly_when_the_launcher_goes() {
        StartHost(["--watch-stdin"]);
        await ServerFixture.Eventually(() => Task.FromResult(Output().Contains("serving on", StringComparison.Ordinal))
        );

        // The launcher crashed or was killed: the OS closes its end of the pipe.
        host!.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await host.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, host.ExitCode);
        Assert.Contains("cathode-host stopped", Output(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(socketPath)));
    }

    [Fact]
    public void Stdin_watch_calls_back_once_its_stream_ends() {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out);
        using var input = new AnonymousPipeClientStream(
            PipeDirection.In,
            pipe.ClientSafePipeHandle
        );
        using var closed = new ManualResetEventSlim();
        StdinWatch.Start(input, closed.Set);

        pipe.Write("anything"u8); // what arrives is ignored
        Assert.False(closed.Wait(200, Token));
        pipe.Dispose();
        Assert.True(closed.Wait(TimeSpan.FromSeconds(10), Token));
    }

    string Output() {
        lock (hostOutput) {
            return hostOutput.ToString();
        }
    }

    void StartHost(string[]? extra = null) {
        var dll = Path.Combine(
            AppContext.BaseDirectory.Replace(
                Path.Combine("tests", "Cathode.Server.Tests"),
                Path.Combine("src", "Cathode.Host"),
                StringComparison.Ordinal
            ),
            "cathode-host.dll"
        );
        Assert.True(File.Exists(dll), $"cathode-host not built at {dll}");
        host = new() {
            StartInfo = new("dotnet", ["exec", dll, "--serve", "--null-audio", "--socket", socketPath, ..extra ?? []]) {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
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

    /// <summary>
    ///     Turns on a test tone in place of <paramref name="block" />'s output, waits for telemetry that carries the
    ///     block's meter and the tone at the output (the preset is silent without it: null audio has no input), all
    ///     rendered by the host and read from its shared memory, then turns it off.
    /// </summary>
    async Task ExpectMetered(HttpClient client, WebSocket socket, string block) {
        await Ok(
            client.PutAsJsonAsync(
                $"/api/v1/live/blocks/{block}/test-tone",
                new TestToneRequest(true, 220, -20),
                Token
            )
        );
        var buffer = new byte[256 * 1024];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); // generous: a busy machine; frames come every 33 ms
        JsonNode? last = null;
        while (true) {
            WebSocketReceiveResult result;
            try {
                result = await socket.ReceiveAsync(buffer, deadline.Token);
            } catch (OperationCanceledException) when (!Token.IsCancellationRequested) {
                Assert.Fail($"No telemetry metering {block}'s test tone within 30 s. Last frame: {last}");
                throw;
            }

            var frame = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
            if (frame["type"]!.GetValue<string>() != "telemetry") {
                continue;
            }

            last = frame;
            // The block's own meter reads what it rendered, before the tone replaces it; downstream, the tone shows.
            if (frame["blocks"]?[block]?["peak"] is not null
                && frame["outputPeakLeft"]!.GetValue<float>() > 0.01f) { // the tone is -20 dBFS (0.1)
                break;
            }
        }

        await Ok(client.PutAsJsonAsync($"/api/v1/live/blocks/{block}/test-tone", new TestToneRequest(false), Token));
    }

    async Task<LiveState> Live(HttpClient client) => (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

    async Task<SystemInfo> System(HttpClient client) =>
        (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!;

    static async Task Ok(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }
}
