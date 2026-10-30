using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Graphic EQ's measured frequency response (<see cref="GraphicEqResponse" />, on <see cref="EqResponse" />),
///     which the Server hands the editor's chart.
/// </summary>
public class GraphicEqResponseTests {
    static readonly float[] Frequencies = EqResponse.Frequencies();

    [Fact]
    public void The_frequencies_run_log_spaced_from_20_hz_to_20_khz() {
        Assert.Equal(256, Frequencies.Length);
        Assert.Equal(20f, Frequencies[0], 1e-3f);
        Assert.Equal(20_000f, Frequencies[^1], 0.1f);
        Assert.Equal(Frequencies[1] / Frequencies[0], Frequencies[^1] / Frequencies[^2], 1e-4f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_flat_eq_reads_0_db_everywhere(int type) {
        var db = Response(new() { ["type"] = type, ["masterQ"] = 2, ["qMode"] = 1, ["shelves"] = 1 });

        Assert.All(db, static d => Assert.Equal(0f, d, 1e-4f));
    }

    [Fact]
    public void A_10_band_boost_at_1_khz_reads_its_gain_there_and_nothing_far_away() {
        Dictionary<string, float> values = new() { ["g1k"] = 6 };

        Assert.Equal(6f, At(values, 1_000), 0.05f);
        Assert.Equal(0f, At(values, 40), 0.05f);
        Assert.Equal(0f, At(values, 18_000), 0.1f);
    }

    [Fact]
    public void The_lowest_band_settles_within_the_impulse() {
        foreach (var gain in new[] { -12f, 12f }) {
            Assert.Equal(gain, At(new() { ["g31"] = gain, ["masterQ"] = 4 }, 31.25f), 0.1f);
        }
    }

    [Fact]
    public void Level_moves_the_whole_curve() {
        var db = Response(new() { ["level"] = -6 });

        Assert.All(db, static d => Assert.Equal(-6f, d, 1e-3f));
    }

    [Fact]
    public void A_passive_layout_with_a_band_moved_has_a_shaped_response() {
        var mark = GraphicEqualizer.LayoutNames.ToList().IndexOf("5 Band Mark");
        Dictionary<string, float> cut = new() { ["type"] = mark, ["g125"] = -12 }; // the third slider, 750 Hz

        Assert.All(Response(new() { ["type"] = mark }), static d => Assert.Equal(0f, d, 0.05f)); // centred: flat
        Assert.All(Response(cut), static d => Assert.True(float.IsFinite(d)));
        Assert.True(At(cut, 750) < -6f, $"{At(cut, 750):0.00} dB at 750 Hz");
        Assert.True(Math.Abs(At(cut, 20)) < 1f, $"{At(cut, 20):0.00} dB at 20 Hz");
    }

    [Fact]
    public void Every_layout_gives_a_finite_response() {
        for (var type = 0; type < GraphicEqualizer.Layouts.Count; type++) {
            foreach (var gain in new[] { -12f, 0f, 12f }) {
                var values = new Dictionary<string, float> { ["type"] = type };
                foreach (var label in GraphicEqualizer.Layouts[0].Labels) {
                    values[$"g{label}"] = gain;
                }

                // Every pot of a tone stack at zero all but silences it, as the circuit does: finite, not a floor.
                var db = Response(values);
                Assert.All(
                    db,
                    d => Assert.True(
                        float.IsFinite(d) && d > EqResponse.FloorDb && d < 40f,
                        $"type {type}: {d} dB"
                    )
                );
            }
        }
    }

    static float[] Response(Dictionary<string, float> values) =>
        GraphicEqResponse.Of(Bank(values), Frequencies);

    static float At(Dictionary<string, float> values, float hz) => GraphicEqResponse.Of(Bank(values), [hz])[0];

    static ParamBank Bank(Dictionary<string, float> values) {
        var bank = new ParamBank(GraphicEqBlock.Info.Params);
        foreach (var (id, value) in values) {
            bank.SetTarget(GraphicEqBlock.Info.IndexOf(id), value);
        }

        bank.SnapToTargets();
        return bank;
    }
}
