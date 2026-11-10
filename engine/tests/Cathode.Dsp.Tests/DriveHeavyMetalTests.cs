using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Boss HM-2 (Heavy Metal): its place in the catalogue, its gyrator EQ and its gain.</summary>
public class DriveHeavyMetalTests {
    const int Rate = 48_000;

    static readonly int Type = DriveBlock.Info.Params[DriveBlock.Type].Options!.ToList().IndexOf("Heavy Metal");

    static DriveCircuit Circuit => DriveCircuits.All[Type - DriveBlock.CircuitTypes];

    [Fact]
    public void It_is_appended_after_every_earlier_type() {
        // Presets store the type by index: the 64 types before it keep theirs.
        Assert.Equal(64, Type);
        Assert.Equal(Type, DriveBlock.Info.Params[DriveBlock.Type].Options!.Count - 1);
    }

    [Fact]
    public void Its_knobs_carry_the_pedals_names() {
        var info = DriveBlock.Info;

        Assert.Equal("Color Mix H", info.Params[info.IndexOf("tone")].TypeLabels![Type]);
        Assert.Equal("Color Mix L", info.Params[info.IndexOf("stackBass")].TypeLabels![Type]);
        Assert.Equal(string.Empty, info.Params[info.IndexOf("stackMid")].TypeLabels![Type]);
    }

    [Fact]
    public void The_gyrators_resonate_where_the_drawings_parts_put_them() {
        var shunts = Circuit.Tone.Shunts;

        Assert.Equal(ToneStage.GyratorShunts, shunts.Count);
        Assert.Equal(86.7, shunts[0].Hz, 0.1); // 1.5 µF with 330 Ω x 100k x 68 nF
        Assert.Equal(1_043, shunts[1].Hz, 1.0); // 150 nF with 330 Ω x 100k x 4.7 nF
        Assert.Equal(1_173, shunts[2].Hz, 1.0); // 100 nF with 330 Ω x 82k x 6.8 nF
    }

    [Theory]
    [InlineData(0.5, 0.5, 87, 0)] // both at noon: flat
    [InlineData(0.5, 0.5, 1_100, 0)]
    [InlineData(1, 0.5, 87, 20)] // Color Mix L: about 1 + 3.3k / 330 at its resonance
    [InlineData(0, 0.5, 87, -20)]
    [InlineData(1, 0.5, 1_100, 0)] // and nothing at the other knob's
    [InlineData(0.5, 1, 1_100, 23)] // Color Mix H: two shunts side by side
    [InlineData(0.5, 0, 1_100, -23)]
    [InlineData(0.5, 1, 87, 0)]
    [InlineData(0.5, 1, 6_000, 3)] // only the skirt reaches the treble
    public void The_eq_boosts_and_cuts_at_the_gyrators(double low, double high, double hz, double db) {
        var tone = new ToneStage();
        tone.Configure(Circuit.Tone, high, Rate, low);
        var response = new float[32_768];
        for (var i = 0; i < response.Length; i++) {
            response[i] = (float)tone.Tick(i == 0 ? 1 : 0);
        }

        Assert.Equal(db, Analysis.ToDb(Analysis.MagnitudeAt(response, hz, Rate)), 3.0);
    }

    [Fact]
    public void Drive_raises_the_gain_into_the_clipping() {
        // A quiet 1 kHz sine: at Drive 0 the op-amp's leg is 47k + 250k (1.7x), at 10 it is 47k (5.7x), after the
        // two transistor stages' 21x each.
        var input = Signals.Sine(1_000, Rate, Rate / 2, 0.0005f);

        double Level(float drive) {
            using var block = new DriveBlock();
            block.Params.SetTarget(DriveBlock.Type, Type);
            block.Params.SetTarget(DriveBlock.Info.IndexOf("drive"), drive);
            block.Prepare(new(Rate, 64));
            return Analysis.Rms(BlockRender.Mono(block, input, 64).AsSpan(Rate / 4));
        }

        var (low, full) = (Level(0f), Level(10f));

        Assert.True(low > 0, "it passes signal at Drive 0");
        Assert.InRange(Analysis.ToDb(full / low), 3, 12);
    }

    [Fact]
    public void A_guitar_level_signal_is_clipped_hard_at_any_drive() {
        var input = Signals.Sine(220, Rate, Rate / 2, 0.1f);
        using var block = new DriveBlock();
        block.Params.SetTarget(DriveBlock.Type, Type);
        block.Params.SetTarget(DriveBlock.Info.IndexOf("drive"), 0f);
        block.Prepare(new(Rate, 64));

        var rendered = BlockRender.Mono(block, input, 64).AsSpan(Rate / 4).ToArray();
        var third = Analysis.ToDb(Analysis.MagnitudeAt(rendered, 660, Rate))
            - Analysis.ToDb(Analysis.MagnitudeAt(rendered, 220, Rate));

        Assert.True(third > -20, $"H3 at {third:0.0} dBc");
    }
}
