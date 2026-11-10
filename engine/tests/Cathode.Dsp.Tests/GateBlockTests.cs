using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class GateBlockTests {
    [Fact]
    public void Signals_above_the_threshold_pass_untouched() {
        using var gate = Create();
        var input = Signals.Sine(220, 48_000, 48_000, Db(-20));

        var output = BlockRender.Mono(gate, input, 64);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Signals_below_the_threshold_are_expanded_by_the_ratio() {
        using var gate = Create();
        var input = Signals.Sine(220, 48_000, 48_000, Db(-80));

        var output = BlockRender.Mono(gate, input, 64);

        // 20 dB under the threshold at 4:1 is 60 dB of attenuation once the release has settled.
        var tail = output.AsSpan(24_000);
        var attenuation = Analysis.ToDb(Analysis.Rms(tail)) - Analysis.ToDb(Analysis.Rms(input.AsSpan(24_000)));
        Assert.InRange(attenuation, -63, -57);
    }

    [Fact]
    public void The_gain_reduction_meter_reads_how_far_the_gate_is_closed() {
        using var gate = Create();
        var meter = new float[1];

        BlockRender.Mono(gate, Signals.Sine(220, 48_000, 48_000, Db(-20)), 64);
        gate.ReadMeters(meter);
        Assert.Equal(0f, meter[0]);

        BlockRender.Mono(gate, Signals.Sine(220, 48_000, 48_000, Db(-80)), 64);
        gate.ReadMeters(meter);
        Assert.InRange(meter[0], 50f, 100f); // 20 dB under at 4:1, near 60 dB
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

    [Theory]
    [InlineData(GateBlock.Expander, 0, 0f)] // Hard: open above the threshold
    [InlineData(GateBlock.Expander, 1, -1f)] // Soft, 6 dB: -3 * (1 - 3)^2 / 12
    [InlineData(GateBlock.Expander, 2, -3.125f)] // Softer, 12 dB: -3 * (1 - 6)^2 / 24
    [InlineData(GateBlock.ModernExpander, 0, 0f)]
    [InlineData(GateBlock.ModernExpander, 1, -1f)]
    [InlineData(GateBlock.ModernExpander, 2, -3.125f)]
    public void The_knee_shapes_the_gain_just_above_the_threshold_whatever_the_hold(int type, int knee, float gainDb) {
        // 1 dB over the threshold at 4:1, with the default 20 ms Hold.
        using var gate = Create(-30, type: type, knee: knee);

        BlockRender.Mono(gate, Signals.Step(48_000, Db(-29)), 64);

        Assert.Equal(gainDb, 20 * MathF.Log10(gate.CurrentGain), 0.01f);
    }

    [Theory]
    [InlineData(GateBlock.Expander, -10.125f)] // 3 dB under, still inside the 12 dB knee: -3 * (-3 - 6)^2 / 24
    [InlineData(GateBlock.ModernExpander, -4.5f)] // inside the hysteresis: the gain at the threshold, -3 * 36 / 24
    public void Under_the_threshold_the_modern_expander_keeps_the_knee_gain_it_had_there(int type, float gainDb) {
        using var gate = Create(-30, type: type, knee: 2);
        var input = Signals.Concat(Signals.Step(24_000, Db(-29)), Signals.Step(48_000, Db(-33)));

        BlockRender.Mono(gate, input, 64);

        Assert.Equal(gainDb, 20 * MathF.Log10(gate.CurrentGain), 0.01f);
    }

    [Fact]
    public void Hold_keeps_the_knee_gain_after_the_signal_stops() {
        using var gate = Create(-30, holdMs: 50, releaseMs: 5, knee: 1);
        BlockRender.Mono(gate, Signals.Step(24_000, Db(-29)), 64);

        // The detector falls through the knee's upper half in about a millisecond, then the hold starts.
        BlockRender.Mono(gate, Signals.Step(48 * 10, Db(-90)), 64);
        var held = gate.CurrentGain;
        BlockRender.Mono(gate, Signals.Step(48 * 30, Db(-90)), 64); // 40 ms in all, inside the hold

        Assert.InRange(20 * MathF.Log10(held), -2.25f, -1f); // between the gain 1 dB over and at the threshold
        Assert.Equal(held, gate.CurrentGain);
    }

    [Fact]
    public void Closing_and_opening_do_not_click() {
        using var gate = Create(-30, releaseMs: 20);
        var loud = Signals.Step(4_800, 0.5f);
        var quiet = Signals.Step(9_600, Db(-50));
        var input = Signals.Concat(loud, quiet, loud);

        var output = BlockRender.Mono(gate, input, 64);

        // A DC input makes any gain step visible directly as a step in the output.
        var steps = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).ToArray();
        var worstExceptInputEdges = steps.Where(static (_, i) => i != 4_799 && i != 14_399).Max();
        Assert.True(worstExceptInputEdges < 0.02f, $"largest step {worstExceptInputEdges}");
    }

    /// <summary>Exact dB to gain (Decibels.ToGain treats -80 dB and below as "off").</summary>
    static float Db(double db) => (float)Math.Pow(10, db / 20);

    [Fact]
    public void Ratio_and_knee_show_on_the_expanders_and_attenuation_on_the_gates() {
        static string Label(string id, int type) => GateBlock.Info.Params.Single(p => p.Id == id).TypeLabels![type];

        foreach (var type in (int[])[GateBlock.Expander, GateBlock.ModernExpander]) {
            Assert.Equal("Ratio", Label("ratio", type));
            Assert.Equal("Knee", Label("knee", type));
            Assert.Empty(Label("attenuation", type));
        }

        foreach (var type in (int[])[GateBlock.Gate, GateBlock.ModernGate]) {
            Assert.Empty(Label("ratio", type));
            Assert.Empty(Label("knee", type));
            Assert.Equal("Attenuation", Label("attenuation", type));
        }
    }

    static GateBlock Create(
        float threshold = -60,
        float ratio = 4,
        float holdMs = 20,
        float releaseMs = 100,
        int type = GateBlock.Expander,
        int knee = 0
    ) {
        var gate = new GateBlock();
        gate.Params.SetTarget(GateBlock.Type, type);
        gate.Params.SetTarget(GateBlock.Knee, knee);
        gate.Params.SetTarget(GateBlock.Threshold, threshold);
        gate.Params.SetTarget(GateBlock.Ratio, ratio);
        gate.Params.SetTarget(GateBlock.Hold, holdMs);
        gate.Params.SetTarget(GateBlock.Release, releaseMs);
        gate.Prepare(new(48_000, 64));
        return gate;
    }
}
