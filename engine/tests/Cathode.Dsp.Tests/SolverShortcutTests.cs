using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Newton shortcuts that keep the dearest Circuit Amp models in their CPU budgets (chord steps on a reused
///     factorisation, looser step tolerances, the one-port bracket safeguard) against the same models with every
///     solver set back to plain Newton at the default tolerance: the sound may move by no more than -60 dB.
/// </summary>
public class SolverShortcutTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    public static TheoryData<string, int[]> Settings =>
        new() {
            { "SVT", [] },
            { "SVT", [CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3] },
            { "Silver Jubilee", [] },
            { "Silver Jubilee", [CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2] },
            { "Hiwatt DR103", [] },
            { "Hiwatt DR103", [CircuitAmpBlock.Switch1] },
            { "18 W 1974X", [CircuitAmpBlock.Switch1] },
            { "Plexi", [] }
        };

    [Theory]
    [MemberData(nameof(Settings))]
    public void Shortcuts_stay_within_60_dB_of_plain_Newton(string type, int[] switches) {
        var noise = Signals.Noise(9_600, 3, 0.25f);
        var chord = CircuitAmpBlockTests.Chord(9_600, 0.5f);
        foreach (var gain in new[] { 5f, 10f }) {
            foreach (var (name, input) in new[] { ("noise", noise), ("chord", chord) }) {
                var fast = Render(type, switches, gain, input, false);
                var exact = Render(type, switches, gain, input, true);
                double energy = 0, residual = 0;
                for (var i = 0; i < exact.Length; i++) {
                    energy += (double)exact[i] * exact[i];
                    residual += ((double)fast[i] - exact[i]) * ((double)fast[i] - exact[i]);
                }

                var db = residual == 0 ? double.NegativeInfinity : 10 * Math.Log10(residual / energy);
                output.WriteLine($"{type} [{string.Join(", ", switches)}] gain {gain} {name}: {db:0.0} dB");
                Assert.True(db <= -60, $"{type} gain {gain} {name}: {db:0.0} dB");
            }
        }
    }

    static float[] Render(string type, int[] switches, float gain, float[] input, bool exact) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == type));
        block.Params.SetTarget(CircuitAmpBlock.Gain, gain);
        foreach (var s in switches) {
            block.Params.SetTarget(s, 1);
        }

        block.Prepare(new(Rate, 64));
        BlockRender.Mono(block, new float[64], 64); // the type and switches take effect
        if (exact) {
            foreach (var newton in Solvers(block.Channel)) {
                newton.ReuseFactors = false;
                newton.AbsoluteTolerance = 1e-6;
                newton.RelativeTolerance = 1e-6;
                newton.SafeguardAfter = int.MaxValue;
            }
        }

        block.Channel.Reset();
        return BlockRender.Mono(block, input, 64);
    }

    static IEnumerable<NewtonSolver> Solvers(AmpChannel channel) {
        foreach (var stage in channel.Stages) {
            var stages = stage is ChannelSwitchStage switched ? switched.Active : [stage];
            foreach (var newton in stages.Select(static s => s.Newton).OfType<NewtonSolver>()) {
                yield return newton;
            }
        }

        yield return channel.Power.Newton;
    }
}
