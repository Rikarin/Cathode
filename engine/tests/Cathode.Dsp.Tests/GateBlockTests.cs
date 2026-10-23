using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class GateBlockTests {
    /// <summary>Exact dB to gain (Decibels.ToGain treats -80 dB and below as "off").</summary>
    private static float Db(double db) => (float)Math.Pow(10, db / 20);

    private static GateBlock Create(float threshold = -60, float ratio = 4, float holdMs = 20, float releaseMs = 100) {
        var gate = new GateBlock();
        gate.Params.SetTarget(GateBlock.Threshold, threshold);
        gate.Params.SetTarget(GateBlock.Ratio, ratio);
        gate.Params.SetTarget(GateBlock.Hold, holdMs);
        gate.Params.SetTarget(GateBlock.Release, releaseMs);
        gate.Prepare(new ProcessSpec(48_000, 64));
        return gate;
    }

    [Fact]
    public void Signals_above_the_threshold_pass_untouched() {
        using var gate = Create();
        var input = Signals.Sine(220, 48_000, 48_000, Db(-20));

        var output = BlockRender.Mono(gate, input, 64);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Signals_below_the_threshold_are_expanded_by_the_ratio() {
        using var gate = Create(threshold: -60, ratio: 4);
        var input = Signals.Sine(220, 48_000, 48_000, Db(-80));

        var output = BlockRender.Mono(gate, input, 64);

        // 20 dB under the threshold at 4:1 is 60 dB of attenuation once the release has settled.
        var tail = output.AsSpan(24_000);
        var attenuation = Analysis.ToDb(Analysis.Rms(tail)) - Analysis.ToDb(Analysis.Rms(input.AsSpan(24_000)));
        Assert.InRange(attenuation, -63, -57);
    }

    [Fact]
    public void Hold_keeps_the_gate_open_after_the_signal_stops() {
        using var gate = Create(holdMs: 50, releaseMs: 5);
        var note = Signals.Sine(220, 48_000, 4_800, 0.5f);
        BlockRender.Mono(gate, note, 64);
        var silenceStart = Signals.Step(48 * 40, Db(-90)); // 40 ms of very quiet signal

        BlockRender.Mono(gate, silenceStart, 64);

        Assert.True(gate.CurrentGain > 0.99f, $"gain {gate.CurrentGain} should still be open inside the hold time");
    }

    [Fact]
    public void Closing_and_opening_do_not_click() {
        using var gate = Create(threshold: -30, releaseMs: 20);
        var loud = Signals.Step(4_800, 0.5f);
        var quiet = Signals.Step(9_600, Db(-50));
        var input = Signals.Concat(loud, quiet, loud);

        var output = BlockRender.Mono(gate, input, 64);

        // A DC input makes any gain step visible directly as a step in the output.
        var steps = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).ToArray();
        var worstExceptInputEdges = steps.Where((_, i) => i != 4_799 && i != 14_399).Max();
        Assert.True(worstExceptInputEdges < 0.02f, $"largest step {worstExceptInputEdges}");
    }
}
