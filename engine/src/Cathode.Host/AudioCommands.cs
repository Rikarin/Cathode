using Cathode.Audio;
using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Dsp.Native;
using Cathode.Engine;
using Cathode.Engine.Graph;
using System.Text.Json;

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
                json.WriteStartArray("inputNames");
                foreach (var name in d.InputNames) {
                    json.WriteStringValue(name);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("midiPorts");
            foreach (var port in NativeMidi.Ports()) {
                json.WriteStartObject();
                json.WriteString("uid", port.Uid);
                json.WriteString("name", port.Name);
                json.WriteBoolean("isInput", port.IsInput);
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

        Console.WriteLine("midi:");
        foreach (var port in NativeMidi.Ports()) {
            Console.WriteLine($"  {port.Name,-40} {(port.IsInput ? "in " : "out")}  uid: {port.Uid}");
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

    /// <summary>
    ///     Measures an impulse response (a cab and mic, a room, an amp's power section into a load): plays an exponential
    ///     sweep on outputs 1 and 2, records input 1 and writes the trimmed IR as a WAV (docs/04 Phase 6 item 8).
    /// </summary>
    public static int IrCapture(CommandLine options) {
        RequireBackend();
        var path = options.Text("out") ?? throw new InvalidOperationException("--out <file.wav> is required.");
        var seconds = options.Double("seconds", 5);
        var level = (float)Math.Pow(10, options.Double("level", -18) / 20);
        var tail = options.Double("tail", 1.5);
        var recorder = default(SweepRecorder);
        using var session = OpenSession(
            options,
            spec => recorder = new(new(spec.SampleRate, seconds), level, tail),
            1
        );
        if (session.InputChannels == 0) {
            throw new InvalidOperationException("The device has no inputs. Pick a duplex interface with --device.");
        }

        Console.WriteLine(
            $"Playing a {seconds:0.#} s sweep at {20 * Math.Log10(level):0} dBFS on outputs 1-2 and recording input 1..."
        );
        session.Start();
        while (!recorder!.Done) {
            Thread.Sleep(50);
        }

        session.Stop();
        var peak = recorder.Recorded.Max(static v => MathF.Abs(v));
        if (peak < 1e-4f) {
            Console.WriteLine("Nothing came back on input 1. Check the cabling and the input gain.");
            return 1;
        }

        if (peak > 0.99f) {
            Console.WriteLine("warning: the recording clipped; lower --level or the input gain and capture again.");
        }

        var measurement = recorder.Measurement;
        var ir = measurement.Trim(measurement.Deconvolve(recorder.Recorded), options.Double("length", 500));
        WavFile.Write(path, new((int)session.SampleRate, [ir]));
        Console.WriteLine(
            $"recorded peak {20 * Math.Log10(peak):0.0} dBFS; wrote {ir.Length} samples ({ir.Length * 1000.0 / session.SampleRate:0} ms) to {path}"
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

    /// <summary>Audio thread: plays the sweep then silence for the tail, recording input 1 into a preallocated buffer.</summary>
    sealed class SweepRecorder(SweepMeasurement measurement, float level, double tailSeconds) : IAudioProcessor {
        readonly float[] sweep = measurement.Sweep;
        int position;

        public SweepMeasurement Measurement => measurement;

        public float[] Recorded { get; } =
            new float[measurement.Sweep.Length + (int)(tailSeconds * measurement.SampleRate)];

        public bool Done => Volatile.Read(ref position) >= Recorded.Length;

        public void Process(in AudioIO io) {
            for (var i = 0; i < io.Frames; i++) {
                var n = position + i;
                var output = n < sweep.Length ? sweep[n] * level : 0f;
                io.OutputLeft[i] = output;
                io.OutputRight[i] = output;
                if (n < Recorded.Length) {
                    Recorded[n] = io.InputLeft[i];
                }
            }

            Volatile.Write(ref position, position + io.Frames);
        }
    }
}
