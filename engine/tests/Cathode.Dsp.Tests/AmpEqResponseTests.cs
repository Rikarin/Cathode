using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Tests;

/// <summary>The Amp's Output EQ as a measured frequency response (<see cref="AmpEqResponse" />).</summary>
public class AmpEqResponseTests {
    static readonly float[] Frequencies = EqResponse.Frequencies();

    [Fact]
    public void At_its_defaults_the_output_eq_is_off_flat_and_without_centres() {
        var values = Bank([]);

        Assert.All(AmpEqResponse.Of(values, Frequencies), static d => Assert.Equal(0f, d));
        Assert.Empty(AmpEqResponse.Centers(values));
    }

    [Fact]
    public void Every_layout_reads_0_db_while_its_bands_are_flat_with_its_centres() {
        for (var layout = 0; layout < GraphicEqualizer.Layouts.Count; layout++) {
            var values = Bank(new() { ["outeq"] = layout + 1 });

            Assert.All(AmpEqResponse.Of(values, Frequencies), static d => Assert.Equal(0f, d, 1e-4f));
            Assert.Equal(GraphicEqualizer.Layouts[layout].Centers, AmpEqResponse.Centers(values));
        }
    }

    [Fact]
    public void Off_reads_flat_whatever_the_bands_say() {
        var db = AmpEqResponse.Of(Bank(new() { ["outeq"] = 0, ["outeq6"] = 12 }), Frequencies);

        Assert.All(db, static d => Assert.Equal(0f, d));
    }

    [Fact]
    public void A_10_band_boost_at_1_khz_reads_its_gain_there_and_nothing_far_away() {
        Dictionary<string, float> values = new() { ["outeq"] = 1, ["outeq6"] = 6 }; // 10 Band's 1 kHz band

        Assert.Equal(6f, At(values, 1_000), 0.05f);
        Assert.Equal(0f, At(values, 40), 0.05f);
        Assert.Equal(0f, At(values, 18_000), 0.1f);
    }

    [Fact]
    public void It_is_measured_at_the_amps_oversampled_rate() {
        Dictionary<string, float> standard = new() { ["outeq"] = 1, ["outeq9"] = 12 }; // 8 kHz
        Dictionary<string, float> high = new(standard) { ["oversampling"] = 1 };

        Assert.Equal(96_000, AmpEqResponse.SampleRate(Bank(standard)));
        Assert.Equal(192_000, AmpEqResponse.SampleRate(Bank(high)));
        Assert.Equal(12f, At(standard, 8_000), 0.1f);
        Assert.Equal(12f, At(high, 8_000), 0.1f);
    }

    [Fact]
    public void Its_5_band_mark_is_its_own_approximation_not_the_graphic_eqs_network() {
        var mark = GraphicEqualizer.LayoutNames.ToList().IndexOf("5 Band Mark");
        var amp = AmpEqResponse.Of(Bank(new() { ["outeq"] = mark + 1, ["outeq3"] = -12 }), Frequencies);
        var graphic = new ParamBank(GraphicEqBlock.Info.Params);
        graphic.SetTarget(GraphicEqBlock.Type, mark);
        graphic.SetTarget(2, -12); // the third slider, 750 Hz
        graphic.SnapToTargets();
        var network = GraphicEqResponse.Of(graphic, Frequencies);

        var difference = amp.Zip(network, static (a, n) => Math.Abs(a - n)).Max();
        Assert.True(difference > 1f, $"at most {difference:0.00} dB apart");
        Assert.True(At(new() { ["outeq"] = mark + 1, ["outeq3"] = -12 }, 750) < -6f);
    }

    [Fact]
    public void Every_layout_gives_a_finite_response_at_the_extremes() {
        for (var layout = 0; layout < GraphicEqualizer.Layouts.Count; layout++) {
            foreach (var gain in new[] { -12f, 12f }) {
                var values = new Dictionary<string, float> { ["outeq"] = layout + 1 };
                for (var b = 1; b <= GraphicEqualizer.MaxBands; b++) {
                    values[$"outeq{b}"] = gain;
                }

                Assert.All(
                    AmpEqResponse.Of(Bank(values), Frequencies),
                    d => Assert.True(
                        float.IsFinite(d) && d > EqResponse.FloorDb && d < 80f,
                        $"layout {layout} at {gain}: {d} dB"
                    )
                );
            }
        }
    }

    static float At(Dictionary<string, float> values, float hz) => AmpEqResponse.Of(Bank(values), [hz])[0];

    static ParamBank Bank(Dictionary<string, float> values) {
        var bank = new ParamBank(CircuitAmpBlock.Info.Params);
        foreach (var (id, value) in values) {
            bank.SetTarget(CircuitAmpBlock.Info.IndexOf(id), value);
        }

        bank.SnapToTargets();
        return bank;
    }
}
