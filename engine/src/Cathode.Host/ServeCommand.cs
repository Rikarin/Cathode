using Cathode.Audio;
using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Engine;
using Cathode.EngineHost;
using System.Runtime.InteropServices;

namespace Cathode.Host;

/// <summary>
///     The production mode: run the engine on a device (or the null backend) and serve the Server over a Unix domain
///     socket until SIGINT or SIGTERM, or with <c>--watch-stdin</c> until standard input closes
///     (docs/adr/0001-process-model.md, docs/adr/0004-state-ownership.md).
/// </summary>
static class ServeCommand {
    /// <summary>
    ///     The GC budget that keeps the host collection-free for hours (docs/adr/0001-process-model.md). NativeAOT
    ///     reads it from the environment only, as bare hex, so whoever launches the host must set it.
    /// </summary>
    public const string Gen0Budget = "10000000"; // 256 MB

    public static async Task<int> RunAsync(CommandLine options) {
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

        if (options.Flag("watch-stdin")) {
            StdinWatch.Start(Console.OpenStandardInput(), stop.Cancel); // the launcher is gone: stop as on SIGTERM
        }

        if (options.Flag("null-audio")) {
            using var nullOutputs = OutputBus.Parse(options.Text("outputs"), options.Text("di-out"));
            var spec = new ProcessSpec(rate, buffer);
            using var service = new EngineHostService(spec, "null", telemetryPath: telemetry);
            using var backend = new NullBackend((int)rate, buffer, nullOutputs);
            backend.Start(service.Engine);
            return await ServeAsync(service, socketPath, stop.Token).ConfigureAwait(false);
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
            InputMap.Parse(options.Text("input-map")),
            OutputBus.Parse(options.Text("outputs"), options.Text("di-out"))
        );
        using var deviceService = new EngineHostService(
            new(session.SampleRate, session.BufferFrames),
            NativeAudio.Backend,
            device: new NativeDeviceMonitor(session),
            telemetryPath: telemetry,
            inputSources: session.Inputs.Mask
        ) { Log = static message => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}") };
        forwarder.Target = deviceService.Engine;
        if (options.Flag("mute")) {
            session.OutputGain = 0f; // unattended runs (soak tests): timing is identical, nothing is heard
        }

        session.Start();
        using var serving = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var watch = WatchDeviceAsync(deviceService, serving);
        try {
            var exit = await ServeAsync(deviceService, socketPath, serving.Token).ConfigureAwait(false);
            await serving.CancelAsync().ConfigureAwait(false);
            return await watch.ConfigureAwait(false) ?? exit;
        } finally {
            session.Stop();
        }
    }

    /// <summary>Exit code when the audio device was lost or changed under the host (docs/adr/0002).</summary>
    public const int DeviceExitCode = 3;

    /// <summary>
    ///     Polls the device status; when the device is lost or its settings changed under the stream, logs why and
    ///     stops serving, so the host exits with <see cref="DeviceExitCode" /> and its supervisor (systemd, the desktop
    ///     app) starts it again on the device as it now is. The Server then resends the rig. Returns the exit code, or
    ///     null when serving ended for another reason.
    /// </summary>
    static async Task<int?> WatchDeviceAsync(EngineHostService service, CancellationTokenSource serving) {
        try {
            while (true) {
                await Task.Delay(250, serving.Token).ConfigureAwait(false);
                var status = service.DeviceStatus;
                if (status == AudioDeviceStatus.Ok) {
                    continue;
                }

                Console.Error.WriteLine(
                    status == AudioDeviceStatus.Lost
                        ? $"{DateTime.Now:HH:mm:ss.fff} error: the audio device was lost (unplugged, taken away or no "
                        + "longer calling back); exiting so the host is restarted"
                        : $"{DateTime.Now:HH:mm:ss.fff} error: the audio device's sample rate, buffer size or default "
                        + "changed under the host; exiting so it is restarted with the new settings"
                );
                // Long enough for a telemetry push (every 100 ms) to tell the Server why before the connection drops.
                await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);
                await serving.CancelAsync().ConfigureAwait(false);
                return DeviceExitCode;
            }
        } catch (OperationCanceledException) {
            return null;
        }
    }

    static async Task<int> ServeAsync(
        EngineHostService service,
        string socketPath,
        CancellationToken cancellationToken
    ) {
        if (Environment.GetEnvironmentVariable("DOTNET_GCgen0size") is null) {
            Console.Error.WriteLine(
                $"warning: DOTNET_GCgen0size is not set; start the host with DOTNET_GCgen0size={Gen0Budget} so garbage "
                + "collections cannot interrupt playing"
            );
        }

        var info = service.Info;
        Console.WriteLine(
            $"cathode-host {info.Version} serving on {socketPath} ({info.Backend}, {info.SampleRate / 1000:0.#} kHz, "
            + $"{info.BufferFrames} frames)"
        );
        await new EngineHostServer(
                service,
                socketPath,
                static message => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}")
            )
            .RunAsync(cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine("cathode-host stopped");
        return 0;
    }
}
