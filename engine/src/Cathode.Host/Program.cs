using System.Reflection;
using Cathode.Engine;
using Cathode.Host;

// Cathode engine host: a NativeAOT executable. The IPC endpoint for Cathode.Server arrives in Phase 3
// (docs/04-implementation-plan.md); for now it offers diagnostics for the Phase 0 spikes.

var version = typeof(CathodeEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
var options = CommandLine.Parse(args);

try
{
    return options.Command switch
    {
        "version" => PrintVersion(),
        "self-test" => SelfTest.Run(),
        "devices" => AudioCommands.ListDevices(),
        "audio-test" => AudioCommands.AudioTest(options),
        "latency-test" => AudioCommands.LatencyTest(options),
        _ => Usage(),
    };
}
catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or FormatException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

int PrintVersion()
{
    Console.WriteLine($"cathode-host {version}");
    return 0;
}

int Usage()
{
    Console.WriteLine($"""
        cathode-host {version}

          --self-test                     offline real-time path check (no device)
          --devices                       list audio devices
          --audio-test [options]          run the engine on a device and report timing and dropouts
                --device <uid>  --rate 48000  --buffer 64  --seconds 10  --input  --monitor
                (output is muted unless --monitor; with --input, input 1 is processed)
          --latency-test [options]        round-trip latency through a loopback cable (output 1 -> input 1)
                --device <uid>  --rate 48000  --buffer 64  --seconds 5   (plays audible clicks)
          --version
        """);
    return options.Command is null ? 0 : 2;
}
