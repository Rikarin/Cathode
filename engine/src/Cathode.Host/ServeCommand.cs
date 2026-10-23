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
    public static int Run(CommandLine options) {
        var socketPath = options.Text("socket") ?? throw new InvalidOperationException("--socket <path> is required.");
        var rate = options.Double("rate", 48_000);
        var buffer = options.Int("buffer", 64);

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
            using var service = new EngineHostService(spec, "null");
            using var backend = new NullBackend((int)rate, buffer);
            backend.Start(service.Engine);
            return Serve(service, socketPath, stop.Token);
        }

        if (!NativeAudio.IsAvailable) {
            throw new InvalidOperationException($"No native audio backend on this platform yet (backend: {NativeAudio.Backend}). Use --null-audio.");
        }

        var forwarder = new ForwardingProcessor();
        using var session = NativeAudioSession.Open(forwarder, options.Text("device"), rate, buffer, 1);
        using var deviceService = new EngineHostService(new(session.SampleRate, session.BufferFrames), NativeAudio.Backend);
        forwarder.Target = deviceService.Engine;
        session.Start();
        try {
            return Serve(deviceService, socketPath, stop.Token);
        } finally {
            session.Stop();
        }
    }

    static int Serve(EngineHostService service, string socketPath, CancellationToken cancellationToken) {
        var info = service.Info;
        Console.WriteLine($"cathode-host {info.Version} serving on {socketPath} ({info.Backend}, {info.SampleRate / 1000:0.#} kHz, {info.BufferFrames} frames)");
        new EngineHostServer(service, socketPath).RunAsync(cancellationToken).GetAwaiter().GetResult();
        Console.WriteLine("cathode-host stopped");
        return 0;
    }
}
