using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class EqBlockTests {
    const double Rate = 48_000;

    [Fact]
    public void Parametric_response_is_the_product_of_its_bands() {
        using var peq = new ParametricEqBlock();
        var bands = new (int Band, float Freq, float Gain, float Q)[] {
            (1, 120, 4, 0.7f), (2, 400, -6, 2f), (3, 1_200, 3, 1f), (4, 3_500, -9, 4f), (5, 9_000, 5, 0.7f)
        };
        foreach (var (band, freq, gain, q) in bands) {
            peq.Params.SetTarget(ParametricEqBlock.Frequency(band), freq);
            peq.Params.SetTarget(ParametricEqBlock.Gain(band), gain);
            peq.Params.SetTarget(ParametricEqBlock.Q(band), q);
        }

        peq.Prepare(new(Rate, 64));
        var response = BlockRender.Mono(peq, Signals.Impulse(16_384), 64);

        for (var f = 30.0; f < 20_000; f *= 1.3) {
            var expected = bands.Aggregate(
                1.0,
                (acc, b) => acc
                    * BiquadCoefficients.Design(
                            b.Band == 1 ? BiquadType.LowShelf : b.Band == 5 ? BiquadType.HighShelf : BiquadType.Peak,
                            Rate,
                            b.Freq,
                            b.Q,
                            b.Gain
                        )
                        .MagnitudeAt(f, Rate)
            );
            Assert.Equal(Analysis.ToDb(expected), Analysis.ToDb(Analysis.MagnitudeAt(response, f, Rate)), 0.05);
        }
    }

    [Fact]
    public void Flat_equalisers_are_bit_transparent() {
        using var peq = new ParametricEqBlock();
        using var geq = new GraphicEqBlock();
        peq.Prepare(new(Rate, 64));
        geq.Prepare(new(Rate, 64));
        var input = Signals.Noise(4_800, 1);

        Assert.Equal(input, BlockRender.Mono(peq, input, 64));
        Assert.Equal(input, BlockRender.Mono(geq, input, 64));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public void Graphic_band_hits_its_gain_at_its_centre(int band) {
        using var geq = new GraphicEqBlock();
        geq.Params.SetTarget(band, 12f);
        geq.Prepare(new(Rate, 64));

        var response = BlockRender.Mono(geq, Signals.Impulse(48_000), 64);

        Assert.Equal(12.0, Analysis.ToDb(Analysis.MagnitudeAt(response, GraphicEqBlock.Centers[band], Rate)), 0.1);
        Assert.True(
            Analysis.ToDb(Analysis.MagnitudeAt(response, GraphicEqBlock.Centers[band] / 8, Rate)) < 1,
            "three octaves away is nearly flat"
        );
    }

    [Fact]
    public void Every_graphic_layout_puts_each_band_at_its_centre() {
        for (var type = 0; type < GraphicEqualizer.Layouts.Count; type++) {
            var layout = GraphicEqualizer.Layouts[type];
            for (var band = 0; band < layout.Count; band++) {
                using var geq = TestBlocks.Create<GraphicEqBlock>((GraphicEqBlock.Type, type), (band, 9f));
                var response = BlockRender.Mono(geq, Signals.Impulse(48_000), 64);
                var shape = GraphicEqualizer.BandType(layout, band, false);
                var probe = shape switch {
                    BiquadType.LowShelf => layout.Centers[band] / 4,
                    BiquadType.HighShelf => Math.Min(20_000, layout.Centers[band] * 2.5),
                    _ => layout.Centers[band]
                };
                Assert.Equal(9.0, Analysis.ToDb(Analysis.MagnitudeAt(response, probe, Rate)), 0.6);
            }
        }
    }

    [Fact]
    public void Graphic_bands_a_layout_lacks_are_hidden_and_silent() {
        var fiveBand = GraphicEqualizer.LayoutNames.ToList().IndexOf("5 Band");
        Assert.Equal(string.Empty, GraphicEqBlock.Info.Params[7].TypeLabels![fiveBand]);
        Assert.Equal("1k Hz", GraphicEqBlock.Info.Params[2].TypeLabels![fiveBand]);
        Assert.Equal("31 Hz", GraphicEqBlock.Info.Params[0].TypeLabels![0]);

        using var geq = TestBlocks.Create<GraphicEqBlock>((GraphicEqBlock.Type, fiveBand), (7, 12f), (9, -12f));
        var input = Signals.Noise(4_800, 2);
        Assert.Equal(input, BlockRender.Mono(geq, input, 64));
    }

    [Fact]
    public void Outer_shelves_lift_everything_below_the_lowest_band() {
        using var peak = TestBlocks.Create<GraphicEqBlock>((0, 12f));
        using var shelf = TestBlocks.Create<GraphicEqBlock>((0, 12f), (GraphicEqBlock.Shelves, 1f));
        var peaked = BlockRender.Mono(peak, Signals.Impulse(65_536), 64);
        var shelved = BlockRender.Mono(shelf, Signals.Impulse(65_536), 64);

        Assert.True(
            Analysis.ToDb(Analysis.MagnitudeAt(peaked, 5, Rate)) < 3,
            "a peak falls back to flat below its band"
        );
        Assert.Equal(12.0, Analysis.ToDb(Analysis.MagnitudeAt(shelved, 5, Rate)), 0.5);
    }
}
