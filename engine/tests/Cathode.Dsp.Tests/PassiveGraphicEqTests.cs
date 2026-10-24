using System.Numerics;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Graphic EQ's component-level passive types (docs/08-blocks-parity.md §6.12).</summary>
public class PassiveGraphicEqTests {
    const double Rate = 48_000;
    const int Length = 16_384;

    [Fact]
    public void Centred_sliders_are_flat() {
        var ir = Response(PassiveGraphicEq.Mark, [0.5, 0.5, 0.5, 0.5, 0.5]);

        Assert.True(Analysis.NullResidualDb(ir, Signals.Impulse(Length)) < -100);
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(372.0)]
    [InlineData(1_000.0)]
    public void The_simulation_matches_the_circuit_analysis(double hz) {
        double[] positions = [0.9, 0.2, 0.75, 0.0, 1.0];
        var ir = Response(PassiveGraphicEq.Mark, positions);

        var simulated = Analysis.ToDb(Analysis.MagnitudeAt(ir, hz, Rate));
        var analysed = Analysis.ToDb(Analog(PassiveGraphicEq.Mark, positions, hz).Magnitude);

        Assert.Equal(analysed, simulated, 0.15); // the bilinear transform's warping is small this low
    }

    [Fact]
    public void A_full_slider_boosts_its_resonance_and_the_opposite_end_cuts_it_back_exactly() {
        var boost = Analysis.ToDb(Analysis.MagnitudeAt(Response(PassiveGraphicEq.Mark, [0.5, 0.5, 1, 0.5, 0.5]), 724, Rate));
        var cut = Analysis.ToDb(Analysis.MagnitudeAt(Response(PassiveGraphicEq.Mark, [0.5, 0.5, 0, 0.5, 0.5]), 724, Rate));

        Assert.InRange(boost, 10, 18);
        Assert.Equal(-boost, cut, 0.05); // swapping the slider's ends inverts the response
    }

    [Fact]
    public void Neighbouring_bands_interact_as_the_network_does() {
        // Between the second and third resonances, boosting one and cutting the other is not the sum of the two in dB:
        // they share the op-amp's nodes.
        double At(double[] positions) => Analysis.ToDb(Analysis.MagnitudeAt(Response(PassiveGraphicEq.Mark, positions), 520, Rate));
        var second = At([0.5, 1, 0.5, 0.5, 0.5]);
        var third = At([0.5, 0.5, 1, 0.5, 0.5]);
        var both = At([0.5, 1, 1, 0.5, 0.5]);

        Assert.True(Math.Abs(both - (second + third)) > 0.3, $"both {both:0.00} dB vs {second:0.00} + {third:0.00} dB");
    }

    [Fact]
    public void The_mark_resonances_follow_the_mesa_parts() {
        double[] expected = [87.6, 372, 724, 1_573, 4_824];
        for (var b = 0; b < expected.Length; b++) {
            Assert.Equal(expected[b], PassiveGraphicEq.Mark[b].Frequency, expected[b] * 0.01);
        }
    }

    [Fact]
    public void The_three_band_passive_is_the_marshall_stack_made_up_at_1_khz() {
        var eq = Block(8);
        var ir = Render(eq, Signals.Impulse(Length));
        var stack = new ToneStackCircuit(ToneStackDesign.Marshall, Rate);
        var reference = Signals.Impulse(Length).Select(x => (float)stack.Tick(x)).ToArray();

        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(ir, 1_000, Rate)), 0.05);
        var shape = Analysis.ToDb(Analysis.MagnitudeAt(ir, 100, Rate) / Analysis.MagnitudeAt(ir, 1_000, Rate));
        var stackShape =
            Analysis.ToDb(Analysis.MagnitudeAt(reference, 100, Rate) / Analysis.MagnitudeAt(reference, 1_000, Rate));
        Assert.Equal(stackShape, shape, 0.05);
    }

    [Fact]
    public void The_four_band_presence_lifts_the_treble_and_leaves_the_bass() {
        var flat = Render(Block(7), Signals.Impulse(Length));
        var full = Render(Block(7, (3, 12f)), Signals.Impulse(Length));

        var lift = Analysis.ToDb(Analysis.MagnitudeAt(full, 8_000, Rate) / Analysis.MagnitudeAt(flat, 8_000, Rate));
        var bass = Analysis.ToDb(Analysis.MagnitudeAt(full, 60, Rate) / Analysis.MagnitudeAt(flat, 60, Rate));
        Assert.True(lift > 4, $"presence lift {lift:0.0} dB");
        Assert.Equal(0, bass, 0.5);
    }

    [Theory]
    [InlineData(5)] // Mark
    [InlineData(6)] // 5 Band Passive
    [InlineData(9)] // Console
    public void The_inductor_types_are_flat_at_0_db_and_bounded_when_pushed(int type) {
        var flat = Render(Block(type), Signals.Impulse(Length));
        var pushed = Render(
            Block(type, (0, 12f), (1, -12f), (2, 12f), (3, -12f), (4, 12f)),
            Signals.Noise(Length, 3, 0.5f)
        );

        Assert.True(Analysis.NullResidualDb(flat, Signals.Impulse(Length)) < -100);
        Assert.True(Analysis.AllFinite(pushed) && Analysis.Peak(pushed) < 8);
    }

    static GraphicEqBlock Block(int type, params (int Band, float Gain)[] gains) {
        var block = new GraphicEqBlock();
        block.Params.SetTarget(GraphicEqBlock.Type, type);
        foreach (var (band, gain) in gains) {
            block.Params.SetTarget(band, gain);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }

    static float[] Render(GraphicEqBlock block, float[] input) {
        using (block) {
            return BlockRender.Mono(block, input, 64);
        }
    }

    static float[] Response(IReadOnlyList<LcrBranch> branches, double[] positions) {
        var eq = new LcrGraphicEq(branches, PassiveGraphicEq.InputOhms, PassiveGraphicEq.SliderOhms);
        eq.Prepare(Rate);
        eq.Set(positions);
        var ir = Signals.Impulse(Length);
        eq.Process(0, ir);
        return ir;
    }

    /// <summary>The circuit's own response: H = (1 + R Σ I_fb) / (1 + R Σ I_in), per unit node voltage.</summary>
    static Complex Analog(IReadOnlyList<LcrBranch> branches, double[] positions, double hz) {
        var s = new Complex(0, 2 * Math.PI * hz);
        Complex input = 0, feedback = 0;
        for (var k = 0; k < branches.Count; k++) {
            var b = branches[k];
            var ga = 1 / (positions[k] * PassiveGraphicEq.SliderOhms + 100);
            var gb = 1 / ((1 - positions[k]) * PassiveGraphicEq.SliderOhms + 100);
            var y = 1 / (b.Resistance + s * b.Inductance + 1 / (s * b.Capacitance));
            input += ga * y / (ga + gb + y);
            feedback += gb * y / (ga + gb + y);
        }

        var r = PassiveGraphicEq.InputOhms;
        return (1 + r * feedback) / (1 + r * input);
    }
}
