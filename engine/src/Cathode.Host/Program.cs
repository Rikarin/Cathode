using System.Reflection;
using Cathode.Engine;
using Cathode.Engine.Graph;
using Cathode.Host;

// Cathode engine host: a NativeAOT executable. --serve runs the engine for Cathode.Server over a Unix domain
// socket (docs/adr/0004-state-ownership.md); the other commands are diagnostics.

var version = typeof(CathodeEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
    ?? "dev";
var options = CommandLine.Parse(args);

try {
    return options.Command switch {
        "version" => PrintVersion(),
        "self-test" => SelfTest.Run(),
        "devices" => AudioCommands.ListDevices(options),
        "audio-test" => AudioCommands.AudioTest(options),
        "latency-test" => AudioCommands.LatencyTest(options),
        "ir-capture" => AudioCommands.IrCapture(options),
        "bench-nam" => NamBenchmark.Run(options),
        "serve" => ServeCommand.Run(options),
        _ => Usage()
    };
} catch (Exception e) when (e is InvalidOperationException
                                or DllNotFoundException
                                or FormatException
                                or GraphValidationException) {
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

int PrintVersion() {
    Console.WriteLine($"cathode-host {version}");
    return 0;
}

int Usage() {
    Console.WriteLine(
        $"""
         cathode-host {version}

           --serve --socket <path> [options]
                                           run the engine for Cathode.Server until SIGINT or SIGTERM
                 --device <uid>  --rate 48000  --buffer 64  --null-audio  --mute
                 --telemetry <path>  (shared-memory telemetry; default <socket>.<pid>.telemetry)
                 --loop-in 3,4  --loop-out 3,4  (device channels for FX Loop blocks' return and send)
                 --input-map "1;3,4;;;5,6"  (device channels for Input 1, Input 2, Input 3, Input 4, USB 5/6, USB 7/8;
                                             default: Input 1 on channel 1)
           --self-test                     offline real-time path check (no device)
           --devices [--json]              list audio devices
           --audio-test [options]          run the engine on a device and report timing and dropouts
                 --device <uid>  --rate 48000  --buffer 64  --seconds 10  --input  --monitor  --nam <file.nam> | --graph <graph.json>
                 (output is muted unless --monitor; with --input, input 1 is processed)
           --latency-test [options]        round-trip latency through a loopback cable (output 1 -> input 1)
                 --device <uid>  --rate 48000  --buffer 64  --seconds 5   (plays audible clicks)
           --ir-capture --out <file.wav> [options]
                                           measure an IR: sweep out of outputs 1-2, record input 1
                 --device <uid>  --seconds 5  --level -18 (dBFS)  --tail 1.5 (s)  --length 500 (ms)
           --bench-nam --model <file.nam|file.aidax> | --graph <graph.json> [options]
                                           CPU cost of a capture model or a whole graph through the engine (offline)
                 --buffer 64  --seconds 10  --size <0..1 for slimmable models>  --exact-tanh  --ftz  --silent-input
           --version
         """
    );
    return options.Command is null ? 0 : 2;
}
