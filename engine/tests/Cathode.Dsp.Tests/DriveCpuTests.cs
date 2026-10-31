using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Diagnostics;

namespace Cathode.Dsp.Tests;

/// <summary>Timing tests run alone in this assembly, so the tests around them do not take their cores.</summary>
[CollectionDefinition(nameof(TimingGroup), DisableParallelization = true)]
public sealed class TimingGroup;

[Collection(nameof(TimingGroup))]
public class DriveCpuTests(ITestOutputHelper output) {
    const int Frames = 64;
    const int Blocks = 48_000 / Frames; // one second of audio
    const int TimedPasses = 3;

    /// <summary>Measurements of the whole catalogue, at most, before a type over a core is believed.</summary>
    const int Attempts = 3;

    /// <summary>
    ///     A rough per-type report, fresh noise in every block as tools/Cathode.Bench feeds it (the bench's numbers, on
    ///     a quiet machine, are the ones that count). The bound only catches a runaway: each type has to run in real
    ///     time. Every 64-frame block is timed on its own and a type's cost is its median block: on a busy machine a
    ///     pass of a whole second is pre-empted many times over, a single block of about a hundred microseconds rarely.
    ///     The other test assemblies still run beside this one on a CI runner and can slow a whole measurement several
    ///     times over, so one that finds a type over a core is repeated, each type keeping its lowest cost: a real
    ///     runaway stays over the bound every time.
    /// </summary>
    [Fact]
    public void Cpu_per_type_is_reported() {
        var types = DriveBlock.CircuitTypes + DriveCircuits.All.Count;
        var load = Enumerable.Repeat(double.MaxValue, types).ToArray();
        for (var attempt = 0; attempt < Attempts && load.Max() >= 1; attempt++) {
            var measured = Measure(types);
            for (var type = 0; type < types; type++) {
                load[type] = Math.Min(load[type], measured[type]);
            }
        }

        for (var type = 0; type < types; type++) {
            var name = DriveCatalogueTests.Name(type);
            output.WriteLine($"{type,2} {name,-16} {load[type]:P2} of one core (stereo, 48 kHz, {Frames} frames)");
        }

        Assert.True(load.Max() < 1, $"worst type uses {load.Max():P1} of a core");
    }

    /// <summary>Each type's median block, as seconds of CPU per second of audio.</summary>
    static double[] Measure(int types) {
        var noise = Signals.Noise(48_000, 1, 0.25f);
        var left = new float[Frames];
        var right = new float[Frames];
        var context = new BlockContext(48_000, 120, 0);
        var ticks = new long[types][];
        for (var type = 0; type < types; type++) {
            ticks[type] = new long[TimedPasses * Blocks];
        }

        // The first pass lets tiered compilation settle on the shared code paths; the other three are timed.
        for (var pass = 0; pass <= TimedPasses; pass++) {
            for (var type = 0; type < types; type++) {
                using var block = DriveCatalogueTests.Create(type, 5f, 5f);
                for (var i = 0; i < Blocks; i++) {
                    noise.AsSpan(i * Frames, Frames).CopyTo(left);
                    left.CopyTo(right);
                    var start = Stopwatch.GetTimestamp();
                    block.Process(new(left, right), context);
                    if (pass > 0) {
                        ticks[type][(pass - 1) * Blocks + i] = Stopwatch.GetTimestamp() - start;
                    }
                }
            }
        }

        var load = new double[types];
        for (var type = 0; type < types; type++) {
            Array.Sort(ticks[type]);
            load[type] = (double)ticks[type][ticks[type].Length / 2] * Blocks / Stopwatch.Frequency;
        }

        return load;
    }
}
