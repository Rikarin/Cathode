using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Tests;

/// <summary>The Drive's EQ 2 as a measured frequency response (<see cref="DriveEqResponse" />).</summary>
public class DriveEqResponseTests {
    static readonly float[] Frequencies = EqResponse.Frequencies();

    [Fact]
    public void At_its_defaults_eq_2_is_off_flat_and_without_centres() {
        var values = Bank([]);

        Assert.All(DriveEqResponse.Of(values, Frequencies), static d => Assert.Equal(0f, d));
        Assert.Empty(DriveEqResponse.Centers(values));
    }

    [Theory]
    [InlineData(DriveBlock.EqPre)]
    [InlineData(DriveBlock.EqPost)]
    public void A_flat_eq_2_reads_0_db_with_its_layouts_centres(int location) {
        var values = Bank(new() { ["eq2"] = location });

        Assert.All(DriveEqResponse.Of(values, Frequencies), static d => Assert.Equal(0f, d, 1e-4f));
        Assert.Equal(GraphicEqualizer.Layouts[DriveBlock.Eq2Layout].Centers, DriveEqResponse.Centers(values));
    }

    [Fact]
    public void Off_reads_flat_whatever_the_bands_say() {
        var db = DriveEqResponse.Of(Bank(new() { ["eq2"] = DriveBlock.EqOff, ["geq7"] = 12 }), Frequencies);

        Assert.All(db, static d => Assert.Equal(0f, d));
    }

    [Fact]
    public void A_boost_at_1_khz_reads_its_gain_there_and_nothing_far_away() {
        Dictionary<string, float> values = new() { ["eq2"] = DriveBlock.EqPost, ["geq7"] = 6 }; // the 1 kHz band

        Assert.Equal(6f, At(values, 1_000), 0.05f);
        Assert.Equal(0f, At(values, 40), 0.05f);
        Assert.Equal(0f, At(values, 18_000), 0.1f);
    }

    [Fact]
    public void Its_bands_are_the_2_3_octave_ones_not_the_graphic_eqs_octave_ones() {
        // The 10 Band 2/3 Oct's 630 Hz band, boosted: the Graphic EQ's 10 Band would have no band there.
        var drive = At(new() { ["eq2"] = DriveBlock.EqPost, ["geq6"] = 12 }, 630);

        Assert.Equal(12f, drive, 0.1f);
    }

    [Fact]
    public void Every_band_at_the_extremes_gives_a_finite_response() {
        foreach (var gain in new[] { -12f, 12f }) {
            var values = new Dictionary<string, float> { ["eq2"] = DriveBlock.EqPost };
            for (var b = 1; b <= GraphicEqualizer.MaxBands; b++) {
                values[$"geq{b}"] = gain;
            }

            Assert.All(
                DriveEqResponse.Of(Bank(values), Frequencies),
                d => Assert.True(float.IsFinite(d) && d > EqResponse.FloorDb && d < 60f, $"{gain}: {d} dB")
            );
        }
    }

    static float At(Dictionary<string, float> values, float hz) => DriveEqResponse.Of(Bank(values), [hz])[0];

    static ParamBank Bank(Dictionary<string, float> values) {
        var bank = new ParamBank(DriveBlock.Info.Params);
        foreach (var (id, value) in values) {
            bank.SetTarget(DriveBlock.Info.IndexOf(id), value);
        }

        bank.SnapToTargets();
        return bank;
    }
}
