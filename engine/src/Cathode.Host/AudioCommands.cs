using System.Text.Json;
using Cathode.Audio;
using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Engine;
using Cathode.Engine.Graph;

namespace Cathode.Host;

/// <summary>Device diagnostics for Phase 0 spike S1 (native audio shim).</summary>
static class AudioCommands {
    public static int ListDevices(CommandLine options) {
        RequireBackend();
        if (options.Flag("json")) {
            // For the desktop app's audio setup page; hand-written so the AOT host needs no serializer metadata.
            using var json = new Utf8JsonWriter(Console.OpenStandardOutput(), new() { Indented = true });
            json.WriteStartObject();
            json.WriteString("backend", NativeAudio.Backend);
            json.WriteStartArray("devices");
            foreach (var d in NativeAudio.Devices()) {
                json.WriteStartObject();
                json.WriteString("uid", d.Uid);
                json.WriteString("name", d.Name);
                json.WriteNumber("inputChannels", d.InputChannels);
                json.WriteNumber("outputChannels", d.OutputChannels);
                json.WriteNumber("sampleRate", d.SampleRate);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            return 0;
        }

        Console.WriteLine($"backend: {NativeAudio.Backend}");
        foreach (var d in NativeAudio.Devices()) {
            Console.WriteLine(
                $"  {d.Name,-40} in {d.InputChannels,2}  out {d.OutputChannels,2}  {d.SampleRate / 1000:0.#} kHz  uid: {d.Uid}"
            );
        }

        return 0;
    }

    public static int AudioTest(CommandLine options) {
        RequireBackend();
        var seconds = options.Int("seconds", 10);
        var engine = default(CathodeEngine);
        using var session = OpenSession(
            options,
            spec => {
                engine = new(spec);
                var graphPath = options.Text("graph");
                var definition = graphPath is not null
                    ? JsonSerializer.Deserialize(
                        File.ReadAllText(graphPath),
                        GraphJsonContext.Default.GraphDefinition
                    )!
                    : options.Text("nam") is { } model
                        ? Graphs.Nam(Path.GetFullPath(model))
                        : GraphTemplates.Basic();
                var assetRoot = graphPath is not null ? Path.GetDirectoryName(Path.GetFullPath(graphPath)) : null;
                engine.Publish(
                    new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks(), assetRoot).Compile(
                        definition,
                        spec,
                        1
                    )
                );
                return engine;
            },
            options.Flag("input") ? 1 : 0
        );
        session.OutputGain = options.Flag("monitor") ? 1f : 0f;

        session.Start();
        for (var s = 1; s <= seconds; s++) {
            Thread.Sleep(1000);
            var stats = session.Stats();
            Console.WriteLine(
                $"  {s,3}s  callbacks {stats.Callbacks,7}  overloads {stats.Overloads}  engine p99.9 <= {engine!.Stats.Percentile(0.999):0}%  max {engine.Stats.MaxPercent:0.0}%"
            );
        }

        session.Stop();
        var final = session.Stats();
        Console.WriteLine();
        Console.WriteLine(
            $"engine callback: p50 <= {engine!.Stats.Percentile(0.5):0}%  p99.9 <= {engine.Stats.Percentile(0.999):0}%  max {engine.Stats.MaxPercent:0.0}% of budget"
        );
        Console.WriteLine($"driver overloads: {final.Overloads}   managed exceptions: {final.CallbackExceptions}");
        PrintReportedLatency(session, final);
        var ok = final.Overloads == 0 && final.CallbackExceptions == 0 && final.Callbacks > 0;
        Console.WriteLine(ok ? "audio-test: PASS" : "audio-test: FAIL");
        return ok ? 0 : 1;
    }

    public static int LatencyTest(CommandLine options) {
        RequireBackend();
        var probe = default(LatencyProbe);
        using var session = OpenSession(options, spec => probe = new(spec.SampleRate), 1);
        if (session.InputChannels == 0) {
            throw new InvalidOperationException(
                "The device has no inputs. Pick a duplex interface with --device and cable output 1 to input 1."
            );
        }

        Console.WriteLine("Playing clicks on output 1 and listening on input 1...");
        session.Start();
        Thread.Sleep(options.Int("seconds", 5) * 1000);
        session.Stop();

        var lags = probe!.Measurements();
        var stats = session.Stats();
        PrintReportedLatency(session, stats);
        var median = LatencyProbe.Median(lags);
        if (median is null) {
            Console.WriteLine($"No clicks detected ({probe.Missed} missed). Check the loopback cable and input gain.");
            return 1;
        }

        Console.WriteLine(
            $"measured round trip: {median} frames = {median * 1000.0 / session.SampleRate:0.00} ms (median of {lags.Length}, range {lags.Min()}..{lags.Max()}, missed {probe.Missed})"
        );
        return 0;
    }

    static NativeAudioSession OpenSession(
        CommandLine options,
        Func<ProcessSpec, IAudioProcessor> createProcessor,
        int inputChannels
    ) {
        var rate = options.Double("rate", 48_000);
        var buffer = options.Int("buffer", 64);

        // The processor needs the actual buffer size, which is only known once the device is open, so it is
        // created lazily on first use behind a forwarding wrapper.
        var forwarder = new ForwardingProcessor();
        var session = NativeAudioSession.Open(forwarder, options.Text("device"), rate, buffer, inputChannels);
        forwarder.Target = createProcessor(new(session.SampleRate, Math.Max(session.BufferFrames, 1)));
        Console.WriteLine(
            $"device: {options.Text("device") ?? "(default output)"}  {session.SampleRate / 1000:0.#} kHz  buffer {session.BufferFrames}  in {session.InputChannels}  out {session.OutputChannels}"
        );
        return session;
    }

    static void PrintReportedLatency(NativeAudioSession session, AudioSessionStats stats) {
        var total = stats.InputLatencyFrames + stats.OutputLatencyFrames;
        Console.WriteLine(
            $"driver-reported latency: in {stats.InputLatencyFrames} + out {stats.OutputLatencyFrames} = {total} frames = {total * 1000.0 / session.SampleRate:0.00} ms"
        );
    }

    static void RequireBackend() {
        if (!NativeAudio.IsLibraryPresent) {
            throw new InvalidOperationException(
                "Native audio library not found. Build it with engine/native/build.sh."
            );
        }

        if (!NativeAudio.IsAvailable) {
            throw new InvalidOperationException(
                $"No native audio backend on this platform yet (backend: {NativeAudio.Backend})."
            );
        }
    }
}
