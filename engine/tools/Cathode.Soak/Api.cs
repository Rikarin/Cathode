using Cathode.Contracts;
using System.Net.Http.Json;
using System.Net.WebSockets;

namespace Cathode.Soak;

/// <summary>A thin API client that counts requests and errors and tracks the worst engine figures seen.</summary>
sealed class Api(HttpClient http) {
    readonly List<string> errorMessages = [];
    long requests;
    long errors;
    long telemetryMessages;

    public long Requests => Interlocked.Read(ref requests);

    public long Errors => Interlocked.Read(ref errors);

    public long TelemetryMessages => Interlocked.Read(ref telemetryMessages);

    public IReadOnlyList<string> ErrorMessages {
        get {
            lock (errorMessages) {
                return [..errorMessages];
            }
        }
    }

    public double WorstNativeMax { get; private set; }

    public double WorstNativeP999 { get; private set; }

    public double WorstCpuMax { get; private set; }

    public void Observe(EngineStatus engine) {
        WorstNativeMax = Math.Max(WorstNativeMax, engine.NativeMax);
        WorstNativeP999 = Math.Max(WorstNativeP999, engine.NativeP999);
        WorstCpuMax = Math.Max(WorstCpuMax, engine.CpuMax);
    }

    public async Task WaitForEngineAsync(CancellationToken cancellationToken) {
        for (var i = 0; i < 600; i++) {
            try {
                if ((await SystemAsync(cancellationToken)).Engine.Connected) {
                    return;
                }
            } catch (HttpRequestException) {
                // The server is not listening yet: keep polling until it is.
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("The server or engine did not come up.");
    }

    public async Task<SystemInfo> SystemAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<SystemInfo>("/api/v1/system", cancellationToken))!;

    public async Task<AssetInfo> UploadAsync(string path, string name, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(path)
            ? throw new ArgumentException(
                $"--{(name.EndsWith(".nam", StringComparison.Ordinal) ? "nam" : "ir")} is required."
            )
            : await UploadBytesAsync(await File.ReadAllBytesAsync(path, cancellationToken), name, cancellationToken);

    public async Task<AssetInfo> UploadBytesAsync(byte[] content, string name, CancellationToken cancellationToken) {
        using var response = await http.PostAsync(
            $"/api/v1/assets?name={Uri.EscapeDataString(name)}",
            new ByteArrayContent(content),
            cancellationToken
        );
        await Check(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AssetInfo>(cancellationToken))!;
    }

    /// <summary>Builds a one-row rig through the API: blocks on the shunts, the model and IR, three distinct scenes.</summary>
    public async Task<string> BuildRigAsync(
        string name,
        string model,
        string ir,
        (string Type, int Column)[] blocks,
        CancellationToken cancellationToken
    ) {
        var token = cancellationToken;
        await PostAsync("/api/v1/live/new", new { }, token);
        foreach (var (type, column) in blocks) {
            var id = type == "nam" ? "amp1" : type == "cab" ? "cab1" : null;
            await PostAsync("/api/v1/live/blocks", new AddBlockRequest(type, 0, column, id), token);
        }

        await PutAsync("/api/v1/live/blocks/amp1/assets/model", new AssetRequest(model), token);
        await PutAsync("/api/v1/live/blocks/cab1/assets/slot1", new AssetRequest(ir), token);
        await PatchAsync("/api/v1/live/blocks/cab1/params", new Dictionary<string, float> { ["m1source"] = 0 }, token);
        await PatchAsync(
            "/api/v1/live/blocks/amp1/params?channel=B",
            new Dictionary<string, float> { ["input"] = 6 },
            token
        );

        // Scene 2: amp on channel B and the last block bypassed; scene 3: the next-to-last block bypassed.
        var last = blocks[^1].Type + "1";
        var nextToLast = blocks[^2].Type + "1";
        await PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest(1), token);
        await PutAsync("/api/v1/live/blocks/amp1/channel", new ChannelRequest("B"), token);
        await PutAsync($"/api/v1/live/blocks/{last}/bypass", new BypassRequest(true), token);
        await PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest(2), token);
        await PutAsync($"/api/v1/live/blocks/{nextToLast}/bypass", new BypassRequest(true), token);
        await PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest(0), token);

        using var saved = await http.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(name, true), token);
        await Check(saved, token);
        return (await saved.Content.ReadFromJsonAsync<LiveState>(token))!.Preset.Id;
    }

    public Task PostAsync(string url, object body, CancellationToken cancellationToken = default) =>
        Send(() => http.PostAsJsonAsync(url, body, cancellationToken), cancellationToken);

    public Task PutAsync(string url, object body, CancellationToken cancellationToken = default) =>
        Send(() => http.PutAsJsonAsync(url, body, cancellationToken), cancellationToken);

    public Task PatchAsync(string url, object body, CancellationToken cancellationToken = default) =>
        Send(() => http.PatchAsJsonAsync(url, body, cancellationToken), cancellationToken);

    public Task GetAsync(string url) => Send(() => http.GetAsync(url), CancellationToken.None);

    /// <summary>
    ///     Runs <paramref name="action" /> every <paramref name="interval" /> until cancelled; failures are counted, not
    ///     thrown.
    /// </summary>
    public async Task Every(TimeSpan interval, Func<long, Task> action, CancellationToken cancellationToken) {
        using var timer = new PeriodicTimer(interval);
        long i = 0;
        try {
            while (await timer.WaitForNextTickAsync(cancellationToken)) {
                try {
                    await action(i++);
                } catch (Exception e) when (e is HttpRequestException
                                                or InvalidOperationException
                                                or TaskCanceledException) {
                    Fail(e.Message);
                }
            }
        } catch (OperationCanceledException) {
            // The soak's duration is up.
        }
    }

    public async Task WatchTelemetryAsync(CancellationToken cancellationToken) {
        using var socket = new ClientWebSocket();
        var uri = new UriBuilder(http.BaseAddress!) {
            Scheme = http.BaseAddress!.Scheme == "https" ? "wss" : "ws", Path = "/ws"
        }.Uri;
        await socket.ConnectAsync(uri, cancellationToken);
        var buffer = new byte[64 * 1024];
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) {
                    Fail("The server closed the WebSocket.");
                    return;
                }

                Interlocked.Increment(ref telemetryMessages);
            }
        } catch (OperationCanceledException) {
            // The soak's duration is up.
        } catch (WebSocketException e) {
            Fail("WebSocket: " + e.Message);
        }
    }

    async Task Send(Func<Task<HttpResponseMessage>> request, CancellationToken cancellationToken) {
        Interlocked.Increment(ref requests);
        using var response = await request();
        await Check(response, cancellationToken);
    }

    async Task Check(HttpResponseMessage response, CancellationToken cancellationToken) {
        if (!response.IsSuccessStatusCode) {
            Fail(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: "
                + $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}"
            );
        }
    }

    void Fail(string message) {
        Interlocked.Increment(ref errors);
        lock (errorMessages) {
            if (errorMessages.Count < 20) {
                errorMessages.Add(message);
            }
        }
    }
}

sealed record Sample(double Minute, EngineStatus Engine, long Requests, long Errors);
