using System.Net.Http.Json;
using System.Net.WebSockets;
using Cathode.Contracts;

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

    public async Task WaitForEngineAsync() {
        for (var i = 0; i < 600; i++) {
            try {
                if ((await SystemAsync()).Engine.Connected) {
                    return;
                }
            } catch (HttpRequestException) { }

            await Task.Delay(500);
        }

        throw new TimeoutException("The server or engine did not come up.");
    }

    public async Task<SystemInfo> SystemAsync() => (await http.GetFromJsonAsync<SystemInfo>("/api/v1/system"))!;

    public async Task<AssetInfo> UploadAsync(string path, string name) =>
        string.IsNullOrEmpty(path)
            ? throw new ArgumentException(
                $"--{(name.EndsWith(".nam", StringComparison.Ordinal) ? "nam" : "ir")} is required."
            )
            : await UploadBytesAsync(await File.ReadAllBytesAsync(path), name);

    public async Task<AssetInfo> UploadBytesAsync(byte[] content, string name) {
        using var response = await http.PostAsync(
            $"/api/v1/assets?name={Uri.EscapeDataString(name)}",
            new ByteArrayContent(content)
        );
        await Check(response);
        return (await response.Content.ReadFromJsonAsync<AssetInfo>())!;
    }

    /// <summary>Builds a one-row rig through the API: blocks on the shunts, the model and IR, three distinct scenes.</summary>
    public async Task<string> BuildRigAsync(string name, string model, string ir, (string Type, int Column)[] blocks) {
        await PostAsync("/api/v1/live/new", new { });
        foreach (var (type, column) in blocks) {
            var id = type == "nam" ? "amp1" : type == "cab" ? "cab1" : null;
            await PostAsync("/api/v1/live/blocks", new AddBlockRequest(type, 0, column, id));
        }

        await PutAsync("/api/v1/live/blocks/amp1/assets/model", new AssetRequest(model));
        await PutAsync("/api/v1/live/blocks/cab1/assets/slot1", new AssetRequest(ir));
        await PatchAsync("/api/v1/live/blocks/cab1/params", new Dictionary<string, float> { ["m1source"] = 0 });
        await PatchAsync("/api/v1/live/blocks/amp1/params?channel=B", new Dictionary<string, float> { ["input"] = 6 });

        // Scene 2: amp on channel B and the last block bypassed; scene 3: the next-to-last block bypassed.
        var last = blocks[^1].Type + "1";
        var nextToLast = blocks[^2].Type + "1";
        await PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest(1));
        await PutAsync("/api/v1/live/blocks/amp1/channel", new ChannelRequest("B"));
        await PutAsync($"/api/v1/live/blocks/{last}/bypass", new BypassRequest(true));
        await PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest(2));
        await PutAsync($"/api/v1/live/blocks/{nextToLast}/bypass", new BypassRequest(true));
        await PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest(0));

        using var saved = await http.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(name, true));
        await Check(saved);
        return (await saved.Content.ReadFromJsonAsync<LiveState>())!.Preset.Id;
    }

    public Task PostAsync(string url, object body) => Send(() => http.PostAsJsonAsync(url, body));

    public Task PutAsync(string url, object body) => Send(() => http.PutAsJsonAsync(url, body));

    public Task PatchAsync(string url, object body) => Send(() => http.PatchAsJsonAsync(url, body));

    public Task GetAsync(string url) => Send(() => http.GetAsync(url));

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
        } catch (OperationCanceledException) { }
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
        } catch (OperationCanceledException) { } catch (WebSocketException e) {
            Fail("WebSocket: " + e.Message);
        }
    }

    async Task Send(Func<Task<HttpResponseMessage>> request) {
        Interlocked.Increment(ref requests);
        using var response = await request();
        await Check(response);
    }

    async Task Check(HttpResponseMessage response) {
        if (!response.IsSuccessStatusCode) {
            Fail(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"
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
