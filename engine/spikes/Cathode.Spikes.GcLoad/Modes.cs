using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Engine;
using Cathode.Engine.Graph;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cathode.Spikes.GcLoad;

static class AudioMode {
    public static async Task<int> Run(Options o, CancellationToken cancellationToken) {
        if (o.Flag("sustained-low-latency")) {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }

        NamModel.SetFastTanh(true);
        var forwarder = new Forwarder();
        using var session = NativeAudioSession.Open(
            forwarder,
            o.Text("device"),
            48_000,
            o.Int("buffer", 64),
            0
        );
        session.OutputGain = 0f; // silent: this measures timing, nobody needs to hear it
        var spec = new ProcessSpec(session.SampleRate, session.BufferFrames);
        var engine = new CathodeEngine(spec);
        var graph = new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks()).Compile(
            GraphTemplates.Nam(Path.GetFullPath(o.Text("nam")!)),
            spec,
            1
        );
        engine.Publish(graph);
        forwarder.Target = engine;
        var ampSlot = graph.SlotOf("amp1");

        WebApplication? server = null;
        if (o.Flag("server")) {
            server = Api.Build(
                o.Int("port", 5199),
                (paramIndex, value) => graph.SetParameter(ampSlot, paramIndex, value)
            );
            await server.StartAsync(cancellationToken);
        }

        using var storm = o.Text("alloc-storm") is { } rate
            ? new AllocationStorm(double.Parse(rate, CultureInfo.InvariantCulture))
            : null;

        session.Start();
        await Task.Delay(TimeSpan.FromSeconds(o.Int("warmup", 5)), cancellationToken);
        session.ResetTiming();
        await Task.Delay(50, cancellationToken);
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration();
        var overloads = session.Stats().Overloads;
        var allocated = GC.GetTotalAllocatedBytes();
        var measured = o.Int("seconds", 20);
        await Task.Delay(TimeSpan.FromSeconds(measured), cancellationToken);

        var timing = session.Timing();
        var stats = session.Stats();
        var result = new RunResult(
            o.Text("label") ?? "unnamed",
            session.BufferFrames,
            GCSettings.IsServerGC ? "server" : "workstation",
            ConcurrentGc(),
            GCSettings.LatencyMode.ToString(),
            server is not null,
            measured,
            (long)timing.Count,
            timing.Percentile(0.5),
            timing.Percentile(0.999),
            timing.Percentile(0.9999),
            timing.MaxPercent,
            (long)timing.AtLeast(50),
            (long)timing.OverBudget,
            (long)(stats.Overloads - overloads),
            GC.CollectionCount(0) - gc0,
            GC.CollectionCount(1) - gc1,
            GC.CollectionCount(2) - gc2,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
            (GC.GetTotalAllocatedBytes() - allocated) / 1e6 / measured,
            Api.Requests
        );

        session.Stop();
        if (server is not null) {
            await server.StopAsync(CancellationToken.None); // the run is over: stop gracefully, whatever happens
        }

        Console.WriteLine(result.ToConsole());
        if (o.Text("results") is { } path) {
            await File.AppendAllTextAsync(
                path,
                JsonSerializer.Serialize(result) + Environment.NewLine,
                cancellationToken
            );
        }

        return 0;
    }

    /// <summary>DOTNET_gcConcurrent overrides the runtimeconfig value (System.GC.Concurrent, default on).</summary>
    static bool ConcurrentGc() =>
        Environment.GetEnvironmentVariable("DOTNET_gcConcurrent") is { } env
            ? env != "0"
            : AppContext.GetData("System.GC.Concurrent") is not bool configured || configured;

    sealed class Forwarder : IAudioProcessor {
        IAudioProcessor? target;

        public IAudioProcessor? Target {
            get => Volatile.Read(ref target);
            set => Volatile.Write(ref target, value);
        }

        public void Process(in AudioIO io) {
            if (Target is { } t) {
                t.Process(io);
            } else {
                io.OutputLeft.Clear();
                io.OutputRight.Clear();
            }
        }
    }
}

sealed record RunResult(
    string Label,
    int Buffer,
    string Gc,
    bool Concurrent,
    string LatencyMode,
    bool InProcessServer,
    int Seconds,
    long Callbacks,
    double P50,
    double P999,
    double P9999,
    double Max,
    long Over50,
    long OverBudget,
    long DriverOverloads,
    int Gen0,
    int Gen1,
    int Gen2,
    double GcPauseMs,
    double AllocMBps,
    long RequestsInProcess
) {
    public string ToConsole() =>
        $"{Label,-32} buf {Buffer,3}  gc {Gc}/{(Concurrent ? "conc" : "nonconc")}/{LatencyMode}  "
        + $"callbacks {Callbacks}  "
        + $"p50 {P50:0}%  p99.9 {P999:0}%  p99.99 {P9999:0}%  max {Max:0.0}%  >=50% {Over50}  over "
        + $"budget {OverBudget}  "
        + $"driver overloads {DriverOverloads}  GCs {Gen0}/{Gen1}/{Gen2}  pause {GcPauseMs:0.0} ms  alloc "
        + $"{AllocMBps:0.0} MB/s  in-proc requests {RequestsInProcess}";
}

/// <summary>
///     A realistic slice of the planned REST API (docs/02-architecture.md §6), allocation profile included.
/// </summary>
static class Api {
    static long requestCount;

    public static long Requests => Interlocked.Read(ref requestCount);

    public static WebApplication Build(int port, Action<int, float>? onParameter) {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        var preset = Preset.Create();

        app.MapGet(
            "/api/v1/live",
            () => {
                Interlocked.Increment(ref requestCount);
                return Results.Text(JsonSerializer.Serialize(preset), "application/json");
            }
        );

        app.MapPatch(
            "/api/v1/live/blocks/{id}/params",
            async (string id, HttpRequest request) => {
                Interlocked.Increment(ref requestCount);
                var values = await JsonSerializer.DeserializeAsync<Dictionary<string, float>>(request.Body);
                if (values is not null && values.TryGetValue("input", out var input)) {
                    onParameter?.Invoke(0, input);
                }

                return Results.Json(new { id, revision = Environment.TickCount64, applied = values?.Count ?? 0 });
            }
        );

        app.MapPost(
            "/api/v1/irs",
            async (HttpRequest request) => {
                Interlocked.Increment(ref requestCount);
                await using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer);
                var bytes = buffer.ToArray(); // large object heap, like a real IR or capture upload
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                return Results.Json(new { hash, length = bytes.Length });
            }
        );

        return app;
    }
}

static class Preset {
    /// <summary>
    ///     About 20 blocks x 4 channels x 30 parameters plus 8 scenes, roughly the size of a full Axe-Fx-style preset.
    /// </summary>
    public static object Create() {
        var random = new Random(1);
        var blocks = Enumerable.Range(0, 20)
            .Select(b => new {
                    id = $"block{b}",
                    type = "delay",
                    row = b % 6,
                    column = b % 14,
                    channels = Enumerable.Range(0, 4)
                        .ToDictionary(
                            static c => ((char)('A' + c)).ToString(),
                            _ => Enumerable.Range(0, 30).ToDictionary(static p => $"param{p}", _ => random.NextSingle())
                        )
                }
            )
            .ToList();
        var scenes = Enumerable.Range(0, 8)
            .Select(s => new {
                    name = $"Scene {s + 1}",
                    state = blocks.ToDictionary(
                        static b => b.id,
                        _ => new { bypassed = random.Next(2) == 0, channel = "A" }
                    )
                }
            )
            .ToList();
        return new {
            id = "preset-1",
            name = "Spike preset",
            schemaVersion = 1,
            blocks,
            scenes
        };
    }
}

/// <summary>Background garbage at a target rate, with some survivors so gen1/gen2 collections happen too.</summary>
sealed class AllocationStorm : IDisposable {
    readonly CancellationTokenSource stop = new();
    readonly Thread thread;

    public AllocationStorm(double megabytesPerSecond) {
        thread = new(() => Run(megabytesPerSecond, stop.Token)) { IsBackground = true, Name = "alloc-storm" };
        thread.Start();
    }

    public void Dispose() {
        stop.Cancel();
        thread.Join();
        stop.Dispose();
    }

    static void Run(double megabytesPerSecond, CancellationToken stop) {
        var random = new Random(3);
        var survivors = new object[2_048];
        var bytesPerTick = megabytesPerSecond * 1e6 / 1000; // 1 ms ticks
        var watch = Stopwatch.StartNew();
        double owed = 0;
        long tick = 0;
        while (!stop.IsCancellationRequested) {
            owed += bytesPerTick;
            while (owed > 0) {
                var size = random.Next(64, 32_768);
                var garbage = new byte[size];
                owed -= size;
                if (random.Next(64) == 0) {
                    survivors[random.Next(survivors.Length)] = garbage; // some objects live long enough to be promoted
                }
            }

            tick++;
            var wait = tick - watch.ElapsedMilliseconds;
            if (wait > 0) {
                Thread.Sleep((int)wait);
            }
        }
    }
}

static class ServerMode {
    public static async Task<int> Run(Options o, CancellationToken cancellationToken) {
        var app = Api.Build(o.Int("port", 5199), null);
        await app.StartAsync(cancellationToken);
        Console.WriteLine(
            $"server listening on {o.Int("port", 5199)} (gc {(GCSettings.IsServerGC ? "server" : "workstation")})"
        );
        await Task.Delay(TimeSpan.FromSeconds(o.Int("seconds", 60)), cancellationToken);
        await app.StopAsync(cancellationToken);
        return 0;
    }
}

static class LoadMode {
    public static async Task<int> Run(Options o, CancellationToken cancellationToken) {
        var url = o.Text("url") ?? "http://127.0.0.1:5199";
        var concurrency = o.Int("concurrency", 8);
        var rate = o.Int("rate", 0);
        var seconds = o.Int("seconds", 25);
        using var client =
            new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = concurrency });
        client.BaseAddress = new(url);
        var upload = new byte[2 * 1024 * 1024];
        new Random(5).NextBytes(upload);
        long ok = 0, failed = 0;
        // Ctrl+C ends the load early; the counts so far are still reported.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(seconds));

        // Wait for the server to come up.
        for (var i = 0; i < 100; i++) {
            try {
                using var probe = await client.GetAsync("/api/v1/live", cancellationToken);
                break;
            } catch (HttpRequestException) {
                await Task.Delay(100, cancellationToken);
            }
        }

        var started = Stopwatch.GetTimestamp();
        var workers = Enumerable.Range(0, concurrency)
            .Select(w => Task.Run(
                    async () => {
                        var random = new Random(w);
                        var interval = rate > 0 ? TimeSpan.FromSeconds((double)concurrency / rate) : TimeSpan.Zero;
                        var next = Stopwatch.GetTimestamp();
                        while (!stop.IsCancellationRequested) {
                            try {
                                var pick = random.Next(100);
                                using var response = pick switch {
                                    < 70 => await client.PatchAsJsonAsync(
                                        "/api/v1/live/blocks/amp1/params",
                                        new Dictionary<string, float> { ["input"] = random.NextSingle() * 6 - 3 },
                                        stop.Token
                                    ),
                                    < 97 => await client.GetAsync("/api/v1/live", stop.Token),
                                    _ => await client.PostAsync(
                                        "/api/v1/irs",
                                        new ByteArrayContent(upload),
                                        stop.Token
                                    )
                                };
                                await response.Content.ReadAsByteArrayAsync(stop.Token);
                                if (response.IsSuccessStatusCode) {
                                    Interlocked.Increment(ref ok);
                                } else {
                                    Interlocked.Increment(ref failed);
                                }
                            } catch (OperationCanceledException) {
                                break;
                            } catch (HttpRequestException) {
                                Interlocked.Increment(ref failed);
                            }

                            if (interval > TimeSpan.Zero) {
                                next += (long)(interval.TotalSeconds * Stopwatch.Frequency);
                                var delay = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), next);
                                if (delay > TimeSpan.Zero) {
                                    try {
                                        await Task.Delay(delay, stop.Token);
                                    } catch (OperationCanceledException) {
                                        break;
                                    }
                                }
                            }
                        }
                    },
                    // Each worker ends on stop.Token itself; a cancelled start would skip the counts' report.
                    CancellationToken.None
                )
            )
            .ToArray();

        await Task.WhenAll(workers);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
        Console.WriteLine(
            $"load: {ok} ok, {failed} failed, {ok / elapsed:0} req/s over {elapsed:0.0} s (concurrency {concurrency}, "
            + $"rate {(rate > 0 ? rate.ToString(CultureInfo.InvariantCulture) : "unthrottled")})"
        );
        return 0;
    }
}

sealed class Options {
    readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);

    public static Options Parse(IEnumerable<string> args) {
        var options = new Options();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++) {
            if (!list[i].StartsWith("--", StringComparison.Ordinal)) {
                continue;
            }

            var hasValue = i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal);
            options.values[list[i][2..]] = hasValue ? list[++i] : null;
        }

        return options;
    }

    public bool Flag(string name) => values.ContainsKey(name);

    public string? Text(string name) => values.GetValueOrDefault(name);

    public int Int(string name, int fallback) =>
        values.TryGetValue(name, out var v) && v is not null ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
