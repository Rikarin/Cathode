using System.Runtime.InteropServices;
using Cathode.Audio;
using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.EngineHost;

namespace Cathode.Host;

/// <summary>
///     The production mode: run the engine on a device (or the null backend) and serve the Server over a Unix domain
///     socket until SIGINT or SIGTERM (docs/adr/0001-process-model.md, docs/adr/0004-state-ownership.md).
/// </summary>
static class ServeCommand {
    /// <summary>
    ///     The GC budget that keeps the host collection-free for hours (docs/adr/0001-process-model.md). NativeAOT reads
    ///     it from the environment only, as bare hex, so whoever launches the host must set it.
    /// </summary>
    public const string Gen0Budget = "10000000"; // 256 MB

    public static int Run(CommandLine options) {
        var socketPath = options.Text("socket") ?? throw new InvalidOperationException("--socket <path> is required.");
        var rate = options.Double("rate", 48_000);
        var buffer = options.Int("buffer", 64);
        // Per process, so a restarted host never truncates a file a Server still has mapped (Windows forbids that).
        var telemetry = options.Text("telemetry") ?? $"{socketPath}.{Environment.ProcessId}.telemetry";

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            stop.Cancel();
        };
        using var terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context => {
                context.Cancel = true;
                stop.Cancel();
            }
        );

        if (options.Flag("null-audio")) {
            var spec = new ProcessSpec(rate, buffer);
            using var service = new EngineHostService(spec, "null", telemetryPath: telemetry);
            using var backend = new NullBackend((int)rate, buffer);
            backend.Start(service.Engine);
            return Serve(service, socketPath, stop.Token);
        }

        if (!NativeAudio.IsAvailable) {
            throw new InvalidOperationException(
                $"No native audio backend on this platform yet (backend: {NativeAudio.Backend}). Use --null-audio."
            );
        }

        var forwarder = new ForwardingProcessor();
        using var session = NativeAudioSession.Open(
            forwarder,
            options.Text("device"),
            rate,
            buffer,
            1,
            LoopChannels.Parse(options.Text("loop-in"), options.Text("loop-out")),
            InputMap.Parse(options.Text("input-map"))
        );
        using var deviceService = new EngineHostService(
            new(session.SampleRate, session.BufferFrames),
            NativeAudio.Backend,
            device: new NativeDeviceMonitor(session),
            telemetryPath: telemetry,
            inputSources: session.Inputs.Mask
        );
        forwarder.Target = deviceService.Engine;
        if (options.Flag("mute")) {
            session.OutputGain = 0f; // unattended runs (soak tests): timing is identical, nothing is heard
        }

        session.Start();
        try {
            return Serve(deviceService, socketPath, stop.Token);
        } finally {
            session.Stop();
        }
    }

    static int Serve(EngineHostService service, string socketPath, CancellationToken cancellationToken) {
        if (Environment.GetEnvironmentVariable("DOTNET_GCgen0size") is null) {
            Console.Error.WriteLine(
                $"warning: DOTNET_GCgen0size is not set; start the host with DOTNET_GCgen0size={Gen0Budget} so garbage collections cannot interrupt playing"
            );
        }

        var info = service.Info;
        Console.WriteLine(
            $"cathode-host {info.Version} serving on {socketPath} ({info.Backend}, {info.SampleRate / 1000:0.#} kHz, {info.BufferFrames} frames)"
        );
        new EngineHostServer(
                service,
                socketPath,
                static message => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}")
            )
            .RunAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();
        Console.WriteLine("cathode-host stopped");
        return 0;
    }
}
