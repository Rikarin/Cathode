using Cathode.Contracts;
using Cathode.Dsp.IO;
using Cathode.Soak;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

// cathode-soak: run against a live cathode-server (and cathode-host on a device) for --minutes, doing what an editor
// and a gig would: knob drags, scene switches, preset loads that recompile the graph, uploads, library reads and a
// WebSocket telemetry client. Samples /system every 5 s. Passes with zero driver overloads and zero API errors.
//
//   cathode-soak --server http://127.0.0.1:5180 --minutes 60 --nam <model.nam> --ir <cab.wav> --out soak.jsonl
//   [--max-host-gcs 0]

var options = Options.Parse(args);
var server = new Uri(options.Text("server", "http://127.0.0.1:5180"));
var duration = TimeSpan.FromMinutes(options.Double("minutes", 60));
var output = options.Text("out", "soak.jsonl");
using var http = new HttpClient();
http.BaseAddress = server;
http.Timeout = TimeSpan.FromSeconds(60);
var api = new Api(http);

// Ctrl+C cancels the setup's requests, or ends the soak early with its report; a second Ctrl+C ends the process.
using var abort = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = !abort.IsCancellationRequested;
    abort.Cancel();
};
var aborted = abort.Token;

await api.WaitForEngineAsync(aborted);
Console.WriteLine($"engine: {(await api.SystemAsync(aborted)).Engine}");
var model = await api.UploadAsync(options.Text("nam", string.Empty), "amp.nam", aborted);
var ir = await api.UploadAsync(options.Text("ir", string.Empty), "cab.wav", aborted);
var rigA = await api.BuildRigAsync(
    "Soak A: high gain",
    model.Ref,
    ir.Ref,
    [("gate", 1), ("drive", 2), ("nam", 3), ("cab", 4), ("delay", 5), ("reverb", 6)],
    aborted
);
var rigB = await api.BuildRigAsync(
    "Soak B: clean modulation",
    model.Ref,
    ir.Ref,
    [("compressor", 1), ("nam", 2), ("cab", 3), ("chorus", 4), ("phaser", 5), ("delay", 6)],
    aborted
);
Console.WriteLine($"rigs: {rigA}, {rigB}; running {duration.TotalMinutes:0} min, sampling to {output}");

using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted);
stop.CancelAfter(duration);
var token = stop.Token;
var started = Stopwatch.GetTimestamp();
var first = (await api.SystemAsync(aborted)).Engine;
await using var log = File.CreateText(output);

var activities = new[] {
    api.Every(
        TimeSpan.FromMilliseconds(33),
        i => api.PatchAsync(
            "/api/v1/live/blocks/amp1/params",
            new Dictionary<string, float> { ["input"] = (float)(3 * Math.Sin(i / 30.0)) }
        ),
        token
    ),
    api.Every(
        TimeSpan.FromSeconds(2),
        i => api.PutAsync("/api/v1/live/scenes/active", new SceneSelectRequest((int)(i % 3))),
        token
    ),
    api.Every(
        TimeSpan.FromSeconds(30),
        i => api.PutAsync($"/api/v1/live/preset/{(i % 2 == 0 ? rigB : rigA)}", new { }),
        token
    ),
    api.Every(
        TimeSpan.FromMinutes(5),
        // Not cancelled: an upload under way when the soak ends completes, as before (a cancelled one would count
        // as an error).
        i => api.UploadBytesAsync(SyntheticIr((int)i), $"upload-{i}.wav", CancellationToken.None),
        token
    ),
    api.Every(TimeSpan.FromSeconds(10), _ => api.GetAsync("/api/v1/presets"), token), api.WatchTelemetryAsync(token),
    Sample()
};

try {
    await Task.WhenAll(activities);
} catch (OperationCanceledException) {
    // The soak's duration is up: every activity stops on the same token.
}

// Not cancelled: the report needs the final figures even after Ctrl+C (a second Ctrl+C ends the process).
var last = (await api.SystemAsync(CancellationToken.None)).Engine;
var report = Report.Build(first, last, api, Stopwatch.GetElapsedTime(started), (int)options.Double("max-host-gcs", -1));
Console.WriteLine(report.Text);
return report.Passed ? 0 : 1;

async Task Sample() {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    try {
        while (await timer.WaitForNextTickAsync(token)) {
            var engine = (await api.SystemAsync(CancellationToken.None)).Engine; // a sample under way is logged
            api.Observe(engine);
            var minute = Stopwatch.GetElapsedTime(started).TotalMinutes;
            await log.WriteLineAsync(JsonSerializer.Serialize(new Sample(minute, engine, api.Requests, api.Errors)));
            await log.FlushAsync(token);
            if ((int)(minute * 12) % 12 == 0) {
                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{minute,5:0.0} min  native p99.9 {engine.NativeP999,3:0}% max {engine.NativeMax,5:0.0}%  driver overloads {engine.DriverOverloads - first.DriverOverloads}  host GCs {engine.HostGen0 - first.HostGen0}/{engine.HostGen1 - first.HostGen1}/{engine.HostGen2 - first.HostGen2}  requests {api.Requests}  errors {api.Errors}"
                    )
                );
            }
        }
    } catch (OperationCanceledException) {
        // The soak's duration is up.
    }
}

static byte[] SyntheticIr(int seed) {
    var random = new Random(seed);
    var ir = new float[1_024];
    for (var i = 0; i < ir.Length; i++) {
        ir[i] = (float)((random.NextDouble() * 2 - 1) * Math.Exp(-i / 150.0));
    }

    using var stream = new MemoryStream();
    WavFile.Write(stream, new(48_000, [ir]));
    return stream.ToArray();
}
