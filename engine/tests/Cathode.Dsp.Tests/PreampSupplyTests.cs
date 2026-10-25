using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Testing.References;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The preamp supply's decoupling chain (<see cref="DecouplingChain" />, <see cref="PreampSupplyDesign" />): its
///     operating point and filtering, the stages' nodes, the expert parameters that act through it, and the
///     motorboating it cures (nine high-gain models self-oscillated at 35-70 Hz after a loud input stopped, while every
///     stage ran on the one preamp node).
/// </summary>
public class PreampSupplyTests {
    const double Rate = 48_000;

    /// <summary>
    ///     The models whose tail is slow for reasons of their own, with the level they reach: the SVT's preamp settles
    ///     with a decaying wobble of about 1 Hz that the supply does not cause (Preamp Sag 0, Sag 0 and the power amp
    ///     off leave it as it is).
    /// </summary>
    static readonly Dictionary<string, double> SlowTails = new() { ["SVT"] = -100 };

    /// <summary>The user's 5150 settings, which motorboated with every stage on the one preamp node.</summary>
    static readonly Dictionary<string, double> User5150 = new() {
        ["gain"] = 7.15625, ["middle"] = 2.94375, ["treble"] = 9.0375, ["bass"] = 7.8875, ["presence"] = 6.1375,
        ["depth"] = 7.175, ["bright"] = 1, ["spkimp"] = 95.926414, ["rectifier"] = 2, ["speaker"] = 5, ["pretube"] = 4,
        ["prebias"] = 41.875, ["brightcap"] = 89.65774, ["supply"] = 1, ["spkpeak"] = 111.728714, ["control1"] = 4.7,
        ["depthfreq"] = 124.83305, ["stackfreq"] = 101.39595, ["master"] = 8.25
    };

    public static TheoryData<string> Types => [..AmpDesign.All.Select(static a => a.Name)];

    public static TheoryData<string> Motorboating => [
        "5150", "SLO-100", "Dual Rectifier", "Dual Rectifier 3-Ch", "Triple Rectifier", "Rect-o-Verb", "TriAxis Rhythm",
        "Savage 120", "Powerball"
    ];

    /// <summary>
    ///     A loud E power chord (82.4, 123.5 and 164.8 Hz, 0.1 or 0.4 V), stopped at three points of its waveform, at
    ///     gains 5, 8 and 10: two seconds after the stop the output above 10 Hz (what a speaker plays: the slowest
    ///     models leave a microvolt of DC settling there) is below -120 dB.
    /// </summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Every_model_goes_silent_after_a_loud_input_stops(string type) {
        (double Gain, double Volts, double Stop)[] cases = [
            ..from gain in new[] { 5.0, 8, 10 }
            from volts in new[] { 0.1, 0.4 }
            from stop in new[] { 0.5, 0.503, 0.507 }
            select (gain, volts, stop)
        ];
        var levels = new double[cases.Length];
        Parallel.For(0, cases.Length, i => {
            var (gain, volts, stop) = cases[i];
            levels[i] = TailDb(type, new() { ["gain"] = gain }, volts, stop);
        });

        var worst = Array.IndexOf(levels, levels.Max());
        Assert.True(
            levels[worst] < SlowTails.GetValueOrDefault(type, -120),
            $"{type}: {levels[worst]:0.0} dB two seconds after the stop (gain {cases[worst].Gain}, "
            + $"{cases[worst].Volts} V, stopped at {cases[worst].Stop} s)"
        );
    }

    /// <summary>
    ///     The nine at the settings that showed it (gain 10, master 8, 0.1 V), and the user's 5150 settings; the
    ///     Powerball also at gain 1 and master 5 after a 0.4 V chord, where its drawn supply latched.
    /// </summary>
    [Theory]
    [MemberData(nameof(Motorboating))]
    public void The_motorboating_models_stay_silent(string type) {
        var level = TailDb(type, new() { ["gain"] = 10, ["master"] = 8 }, 0.1, 0.5);
        Assert.True(level < -120, $"{type}: {level:0.0} dB two seconds after the stop");
        if (type == "Powerball") {
            var low = TailDb(type, new() { ["gain"] = 1, ["master"] = 5 }, 0.4, 0.5);
            Assert.True(low < -120, $"Powerball at gain 1, master 5: {low:0.0} dB two seconds after the stop");
        }

        if (type == "5150") {
            var user = new[] { 0.5, 0.503, 0.507 }.Max(stop => TailDb(type, User5150, 0.1, stop));
            Assert.True(user < -120, $"5150 at the user's settings: {user:0.0} dB two seconds after the stop");
        }
    }

    /// <summary>
    ///     At rest each node sits below its parent by its resistor's drop, which carries its own and its children's
    ///     draw.
    /// </summary>
    [Fact]
    public void A_chain_rests_below_the_preamp_node_by_each_resistors_drop() {
        var chain = new DecouplingChain([new(10e3, 22e-6, 1e-3), new(22e3, 10e-6, 0.5e-3, 1), new(15e3, 10e-6, 2e-3)]);
        chain.Calibrate(400);

        Assert.Equal(3.5e-3, chain.Current, 12);
        Assert.Equal(400 - 10e3 * 1.5e-3, chain.Voltage(1), 9);
        Assert.Equal(chain.Voltage(1) - 22e3 * 0.5e-3, chain.Voltage(2), 9);
        Assert.Equal(400 - 15e3 * 2e-3, chain.Voltage(3), 9);
        Assert.All(chain.Supplies, static s => Assert.Equal(1, s, 12));

        var drawn = chain.Tick(400, 1 / Rate);
        Assert.Equal(chain.Current, drawn, 12); // at rest the chain draws its stages' current from node 0
    }

    /// <summary>
    ///     A ripple on node 0 reaches a node one RC further down filtered once (first order at 1 / 2πRC), and one two
    ///     nodes down twice.
    /// </summary>
    [Fact]
    public void Each_node_filters_the_supply_once_more() {
        var chain = new DecouplingChain([new(10e3, 22e-6, 1e-3), new(10e3, 22e-6, 0.5e-3, 1)]);
        chain.Calibrate(400);
        const double Hz = 50;
        var (peak1, peak2) = (0.0, 0.0);
        for (var n = 0; n < (int)(8 * Rate); n++) {
            chain.Tick(400 + Math.Sin(2 * Math.PI * Hz * n / Rate), 1 / Rate);
            if (n > 7 * Rate) { // after the slowest mode (about 0.6 s) has settled
                peak1 = Math.Max(peak1, Math.Abs(chain.Voltage(1) - 385));
                peak2 = Math.Max(peak2, Math.Abs(chain.Voltage(2) - 380));
            }
        }

        // One section: 1 / |1 + jωRC| (the second loads the first a little); two: 1 / |1 - (ωRC)² + 3jωRC|.
        var wrc = 2 * Math.PI * Hz * 10e3 * 22e-6;
        var one = 1 / Math.Sqrt(1 + wrc * wrc);
        var two = 1 / Math.Sqrt(Math.Pow(1 - wrc * wrc, 2) + 9 * wrc * wrc);
        Assert.InRange(peak1, 0.9 * one, 1.1 * one);
        Assert.InRange(peak2, 0.95 * two, 1.05 * two);
    }

    [Fact]
    public void A_node_must_hang_from_an_earlier_one() =>
        Assert.Throws<ArgumentException>(static () => new DecouplingChain([new(10e3, 22e-6, 1e-3, 1)]));

    /// <summary>
    ///     The stages sit on the nodes their designs name, inside a channel switch too (the SLO-100's channels).
    /// </summary>
    [Fact]
    public void Stages_run_on_their_designs_nodes() {
        var channel = new AmpChannel(Slo100.Design, 2 * Rate);
        Assert.Equal(2, channel.Power.Decoupling.Count);
        Assert.Equal(1, channel.Stages[0].SupplyNode); // V1b on B4
        var switched = Assert.IsType<ChannelSwitchStage>(channel.Stages[1]);
        Assert.Equal([2, 2, 0], switched.Active.Select(static s => s.SupplyNode)); // V2a, V2b on B3; V3b on B2
        Assert.All(channel.Stages.Skip(2), static s => Assert.Equal(0, s.SupplyNode));

        channel.Configure(Controls(switch1: true), Power);
        Assert.Equal([1, 0], switched.Active.Select(static s => s.SupplyNode)); // the normal channel's V1a on B4
    }

    /// <summary>
    ///     A model that draws no chain gets the generic one: its gain stages in pairs from the input, the last pair on
    ///     the preamp node, each earlier pair a node further down, each node 10k into 22 µF.
    /// </summary>
    [Fact]
    public void A_model_without_a_drawn_chain_gets_the_generic_pairs() {
        foreach (var design in AmpDesign.All.Where(static a => a.PreampSupply is null && a.SolidState is null)) {
            var channel = new AmpChannel(design, 2 * Rate);
            var gains = channel.Stages
                .Select(static s => s is ChannelSwitchStage c ? c.GainStages : s.IsGainStage ? 1 : 0)
                .ToArray();
            var pairs = (gains.Sum() + 1) / 2;
            Assert.Equal(Math.Max(pairs - 1, 0), channel.Power.Decoupling.Count);
            var first = Array.FindIndex(gains, static g => g > 0);
            if (first >= 0) {
                Assert.Equal(Math.Max(pairs - 1, 0), channel.Stages[first].SupplyNode);
            }

            // Down the signal path the nodes climb back towards the preamp node, never further away.
            var nodes = channel.Stages.Select(static s => s.SupplyNode).ToArray();
            Assert.True(
                nodes.Zip(nodes.Skip(1)).All(static p => p.First >= p.Second),
                $"{design.Name}: {string.Join(", ", nodes)}"
            );

            Assert.True(
                channel.Power.Decoupling.Current < design.Power.PowerSupply.PreampCurrent,
                $"{design.Name}: the chain draws more than the preamp"
            );
        }
    }

    /// <summary>
    ///     Preamp Sag scales what reaches every node (0: a stiff preamp supply), the Variac moves every node, and a
    ///     rack preamp's own supply (the TriAxis) never moves.
    /// </summary>
    [Fact]
    public void Preamp_sag_and_variac_act_on_every_node() {
        double[] Run(AmpDesign design, double sag, double variac) {
            var channel = new AmpChannel(design, 2 * Rate);
            channel.Apply(
                PreampSettings.Stock,
                PowerSettings.Stock with { Variac = variac },
                ChannelSettings.Stock with { PreampSag = sag }
            );
            channel.Configure(Controls(), Power);
            channel.Reset();
            var low = Enumerable.Repeat(double.MaxValue, channel.Supplies.Count).ToArray();
            for (var n = 0; n < (int)(0.5 * 2 * Rate); n++) {
                channel.Tick(0.4 * Math.Sin(2 * Math.PI * 82.4 * n / (2 * Rate)));
                for (var k = 0; k < low.Length; k++) {
                    low[k] = Math.Min(low[k], channel.Supplies[k]);
                }
            }

            return low;
        }

        var full = Run(DualRectifier.Design, 1, 1);
        var half = Run(DualRectifier.Design, 0.5, 1);
        var stiff = Run(DualRectifier.Design, 0, 1);
        Assert.Equal(3, full.Length);
        for (var k = 0; k < full.Length; k++) {
            Assert.True(full[k] < 0.999, $"node {k} sags under a loud chord ({full[k]:0.0000})");
            Assert.Equal(1 - 0.5 * (1 - full[k]), half[k], 2);
            Assert.Equal(1, stiff[k], 12);
        }

        Assert.True(full[0] < full[2], "the preamp node sags more than V1A's, two RCs further down");
        Assert.All(Run(DualRectifier.Design, 1, 0.9), static s => Assert.InRange(s, 0.85, 0.92));
        Assert.All(Run(TriAxis.Rhythm, 1, 0.9), static s => Assert.Equal(1, s));
    }

    static readonly PowerControls Power = new(0.5, 0, 0, 0.5, 0.5, 0.5);

    static AmpControls Controls(bool switch1 = false) => new(1, 0.5, 0.5, 0.5, 0.8, false, Switch1: switch1);

    /// <summary>
    ///     The output's level above 10 Hz (a second-order high-pass) over the 100 ms ending two seconds after a chord
    ///     of <paramref name="volts" /> stops at <paramref name="stop" /> seconds.
    /// </summary>
    static double TailDb(string type, Dictionary<string, double> settings, double volts, double stop) {
        var length = (int)((stop + 2) * Rate);
        var input = new float[length];
        for (var n = 0; n < (int)(stop * Rate); n++) {
            var t = n / Rate;
            input[n] = (float)(volts
                * (Math.Sin(2 * Math.PI * 82.4 * t) + Math.Sin(2 * Math.PI * 123.5 * t)
                    + Math.Sin(2 * Math.PI * 164.8 * t))
                / 3);
        }

        using var renderer = new CircuitAmpRenderer(Rate);
        var output = renderer.Render(input, new(type, settings));

        // RBJ high-pass at 10 Hz, Q 0.707.
        var w = 2 * Math.PI * 10 / Rate;
        var alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
        var a0 = 1 + alpha;
        var (b0, b1, b2) = ((1 + Math.Cos(w)) / 2 / a0, -(1 + Math.Cos(w)) / a0, (1 + Math.Cos(w)) / 2 / a0);
        var (a1, a2) = (-2 * Math.Cos(w) / a0, (1 - alpha) / a0);
        var (x1, x2, y1, y2) = (0.0, 0.0, 0.0, 0.0);
        var sum = 0.0;
        var from = length - (int)(0.1 * Rate);
        for (var n = 0; n < length; n++) {
            double x = output[n];
            var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            (x2, x1, y2, y1) = (x1, x, y1, y);
            if (n >= from) {
                sum += y * y;
            }
        }

        return 10 * Math.Log10(sum / (length - from) + 1e-30);
    }
}
