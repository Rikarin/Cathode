using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Randall RG100ES against its drawing (PCB 2146B) and owner's manual: the JFET stages' operating points and
///     signal voltages, the two channels, the red channel's clamps, the tone controls' swings.
/// </summary>
public partial class BassAmpTests {
    const string Randall = "RG100ES";

    /// <summary>
    ///     The drawing's DC voltages (it gives them ± 10 %, and its two gain stages are two samples of one part that
    ///     the model draws alike): +15 V and +11.5 V on the gain stages' drains, +1 V and +1.3 V on their sources, +12
    ///     V on the first follower's source, +14 V on the last follower's gate and +15 V on its source.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rg100es_operating_points_match_the_drawing(bool green) {
        var channel = RandallChannel(new(0.5, 0.5, 0.5, 0.5, 0.5, false, Switch1: green));
        var (input, gain, send) = (Fets(channel, 0), Fets(channel, 1), Fets(channel, 3));
        var first = input[0];
        var (second, follower) = (gain[0], gain[1]);
        var last = send[0];
        output.WriteLine(
            $"{(green ? "Green" : "Red")}: first stage drain {first.Drain:0.0} V, source {first.Source:0.00} V; second "
            + $"{second.Drain:0.0} / {second.Source:0.00} V; follower source {follower.Source:0.0} V; last follower "
            + $"gate {last.Gate:0.0} V, source {last.Source:0.0} V"
        );
        Assert.InRange(first.Drain, 11.5 * 0.9, 15 * 1.1);
        Assert.InRange(second.Drain, 11.5 * 0.9, 15 * 1.1);
        Assert.InRange(first.Source, 1 * 0.9, 1.3 * 1.1);
        Assert.InRange(second.Source, 1 * 0.9, 1.3 * 1.1);
        Assert.InRange(follower.Source, 12 * 0.9, 12 * 1.1);
        Assert.InRange(last.Gate, 14 * 0.9, 14 * 1.1);
        Assert.InRange(last.Source, 15 * 0.9, 15 * 1.1);
    }

    /// <summary>
    ///     The drawing's 1 kHz signal voltages along the green channel, everything full up (they are the levels at
    ///     rated output from the manual's minimum input): 6 mV at the input, 40 mV on the Gain pot, 2.1 V after the
    ///     second stage, 400 mV at the last follower and 250 mV at the send (0.625 of it), 20 V at the speaker. The
    ///     tone controls' settings are not printed; full up, the model's stack loses about 2 dB more than the drawing's
    ///     2.1 V to 400 mV (14.4 dB), so it is held to 3 dB and the stages after it are checked by their own gains.
    /// </summary>
    [Fact]
    public void Rg100es_stage_gains_match_the_drawing_signal_voltages() {
        var channel = RandallChannel(new(1, 1, 1, 1, 1, false, Switch1: true, Presence: 1));
        var gainStage = ((ChannelSwitchStage)channel.Stages[1]).Active[0];
        var potTop = Netlist(gainStage).Elements[0].B;
        const int Samples = 48_000;
        const int Settled = Samples / 2;
        var (sums, squares) = (new double[6], new double[6]);
        for (var n = 0; n < Samples; n++) {
            var x = 0.006 * Math.Sqrt(2) * Math.Sin(2 * Math.PI * 1_000 * n / Rate);
            for (var k = 0; k < 6; k++) {
                // The four stages, the speaker, and beside them the Gain pot's top.
                var y = k switch {
                    < 4 => x = channel.Stages[k].Tick(x),
                    4 => channel.SolidState!.Tick(x),
                    _ => Voltage(gainStage, potTop)
                };
                if (n >= Settled) {
                    sums[k] += y;
                    squares[k] += y * y;
                }
            }
        }

        // The stages' outputs rest on their drains' and sources' DC: the signal is what moves about the mean.
        var rms = squares.Select((s, k) => Math.Sqrt(s / Settled - sums[k] / Settled * (sums[k] / Settled))).ToArray();
        output.WriteLine(
            $"Gain pot {rms[5] * 1e3:0.0} mV, second stage {rms[1]:0.00} V, stack {rms[2] * 1e3:0} mV "
            + $"({Db(rms[2] / rms[1]):0.0} dB), send {rms[3] * 1e3:0} mV, speaker {rms[4]:0.0} V"
        );
        Assert.InRange(Db(rms[5] / 0.040), -1.5, 1.5);
        Assert.InRange(Db(rms[1] / rms[5] / (2.1 / 0.040)), -1.5, 1.5);
        Assert.InRange(Db(rms[2] / rms[1] / (0.400 / 2.1)), -3, 3);
        Assert.InRange(Db(rms[3] / rms[2] / (0.250 / 0.400)), -1.5, 1.5);
        Assert.InRange(Db(rms[4] / rms[3] / (20 / 0.250)), -1, 1);

        static double Db(double ratio) => 20 * Math.Log10(ratio);
    }

    /// <summary>
    ///     The red channel (channel 2) has the first stage's source bypassed: more gain than the green from a few
    ///     hundred hertz up. The drawing's values give 14 dB at 3 kHz; the manual's sensitivities there (0.5 mV against
    ///     6.5 mV for rated output) say 22 dB, which no one JFET gives with the drawing's printed stage gains.
    /// </summary>
    [Fact]
    public void Rg100es_red_channel_has_the_bypassed_first_stage() {
        static double At(double frequency, bool green) =>
            Level(
                Randall,
                frequency,
                1e-4f,
                (CircuitAmpBlock.Gain, 10f),
                (CircuitAmpBlock.Master, 3f),
                (CircuitAmpBlock.Switch1, green ? 1f : 0f)
            );

        var (high, low) = (At(3_000, false) - At(3_000, true), At(40, false) - At(40, true));
        output.WriteLine($"Red over green: {high:0.0} dB at 3 kHz, {low:0.0} dB at 40 Hz");
        Assert.InRange(high, 10, 16);
        Assert.True(low < high - 4, $"the 0.47 µF bypass leaves the lows: {low:0.0} dB");
    }

    /// <summary>
    ///     The red channel's clamps, read at the Master's top with a guitar-level chord at full gain: the zeners hold
    ///     it near ± 4.6 V; the Sustain Boost's two diodes hold it near ± 0.6 V (the manual: "the overall volume will
    ///     drop"), and do nothing on the green channel.
    /// </summary>
    [Fact]
    public void Rg100es_sustain_boost_clips_the_red_channel_only() {
        static double Peak(bool green, bool boost) {
            var channel = RandallChannel(new(1, 0.5, 0.5, 0.5, 1, false, Switch1: green, Switch2: boost));
            var tone = ((ChannelSwitchStage)channel.Stages[2]).Active[0];
            var top = Netlist(tone).Elements[0].B;
            var peak = 0.0;
            for (var n = 0; n < 48_000; n++) {
                var x = 0.2 * Math.Sin(2 * Math.PI * 220 * n / Rate);
                for (var k = 0; k < 3; k++) {
                    x = channel.Stages[k].Tick(x);
                }

                if (n >= 24_000) {
                    peak = Math.Max(peak, Math.Abs(Voltage(tone, top)));
                }
            }

            return peak;
        }

        var (red, boosted, green, greenBoosted) = (Peak(false, false), Peak(false, true), Peak(true, false),
            Peak(true, true));
        output.WriteLine(
            $"Master top: red {red:0.00} V, with Sustain Boost {boosted:0.00} V; green {green:0.00} V, with it "
            + $"{greenBoosted:0.00} V"
        );
        Assert.InRange(red, 3.5, 5.5);
        Assert.InRange(boosted, 0.4, 0.9);
        Assert.True(green > red, "the green channel has no zeners");
        Assert.Equal(green, greenBoosted, 6);
    }

    /// <summary>
    ///     The manual's tone control swings: Bass 15 dB at 50 Hz, Middle 10 dB at 600 Hz, Presence 15 dB at 10 kHz
    ///     (the Treble's frequency is misprinted there: read here at 5 kHz).
    /// </summary>
    [Fact]
    public void Rg100es_tone_controls_swing_as_the_manual_says() {
        static double Swing(int knob, double frequency) {
            return At(10) - At(0);

            double At(float value) =>
                Level(
                    Randall,
                    frequency,
                    1e-4f,
                    (CircuitAmpBlock.Switch1, 1f),
                    (CircuitAmpBlock.Gain, 5f),
                    (CircuitAmpBlock.Master, 5f),
                    (knob, value)
                );
        }

        var (bass, middle, treble, presence) = (Swing(CircuitAmpBlock.Bass, 50), Swing(CircuitAmpBlock.Middle, 600),
            Swing(CircuitAmpBlock.Treble, 5_000), Swing(CircuitAmpBlock.Presence, 10_000));
        output.WriteLine(
            $"Bass {bass:0.0} dB at 50 Hz, Middle {middle:0.0} dB at 600 Hz, Treble {treble:0.0} dB at 5 kHz, "
            + $"Presence {presence:0.0} dB at 10 kHz"
        );
        Assert.InRange(bass, 12, 18);
        Assert.InRange(middle, 7, 13);
        Assert.InRange(treble, 12, 20);
        Assert.InRange(presence, 12, 18);
    }

    [Fact]
    public void Rg100es_panel_names_the_real_controls() {
        Assert.Equal(
            ["Gain", "Bass", "Middle", "Treble", "Presence", "Master"],
            new[] { "gain", "bass", "middle", "treble", "presence", "master" }.Select(static id => Label(id, Randall))
        );
        Assert.Equal(
            ["Channel 1", "Sustain Boost", string.Empty],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, Randall))
        );
        foreach (var id in new[] { "depth", "cut", "bright", "bias", "powertube", "pretube", "presag", "presfreq" }) {
            Assert.Empty(Label(id, Randall));
        }
    }

    /// <summary>
    ///     Full drive into the power amp: 80 W into 8 Ω is 35.8 V peak, a little under the ± 40 V rails, and both
    ///     Gain and Master raise the level.
    /// </summary>
    [Fact]
    public void Rg100es_power_amp_clips_under_its_rails_and_the_volumes_are_monotonic() {
        var channel = RandallChannel(new(1, 0.5, 0.5, 0.5, 1, false));
        var peak = 0.0;
        for (var n = 0; n < 48_000; n++) {
            var y = channel.Tick(0.2 * Math.Sin(2 * Math.PI * 220 * n / Rate));
            if (n >= 24_000) {
                peak = Math.Max(peak, Math.Abs(y));
            }
        }

        var watts = peak * peak / 2 / 8;
        output.WriteLine($"Full drive: {peak:0.0} V peak at the speaker ({watts:0} W as a sine)");
        Assert.InRange(peak, 30, 40);

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }) {
            var levels = new[] { 0.5f, 2.5f, 5f, 7.5f, 10f }
                .Select(v => Level(Randall, 1_000, 1e-4f, (CircuitAmpBlock.Switch1, 1f), (knob, v)))
                .ToArray();
            output.WriteLine($"Knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Length; i++) {
                Assert.True(levels[i] > levels[i - 1], $"knob {knob}: not monotonic at step {i}");
            }
        }
    }

    static AmpChannel RandallChannel(AmpControls controls) {
        var channel = new AmpChannel(AmpDesign.All[TypeOf(Randall)], Rate);
        channel.Configure(controls, new(0.5, 0, 0, 0.5, 0.5, 0.5));
        channel.Reset();
        return channel;
    }

    /// <summary>
    ///     The rest voltages of the JFETs of <paramref name="channel" />'s stage <paramref name="index" /> (the playing
    ///     channel's, where it is switched), in the netlist's order.
    /// </summary>
    static (double Drain, double Gate, double Source)[] Fets(AmpChannel channel, int index) {
        var stage = channel.Stages[index] is ChannelSwitchStage switched ? switched.Active[0] : channel.Stages[index];
        return [
            ..Netlist(stage)
                .Elements.Where(static e => e.Kind == ElementKind.Jfet)
                .Select(e => (Rest(stage, e.A), Rest(stage, e.B), Rest(stage, e.C)))
        ];
    }

    static StageNetlist Netlist(AmpStage stage) =>
        stage switch {
            NetworkStage<Jfet> one => one.Design.Netlist,
            NetworkStage<Devices<Jfet, Jfet>> two => two.Design.Netlist,
            NetworkStage<Devices<DiodeClipper, DiodeClipper>> clamps => clamps.Design.Netlist,
            _ => ((NetworkStage<NoDevice>)stage).Design.Netlist
        };

    static double Rest(AmpStage stage, int node) =>
        stage is NetworkStage<Jfet> one
            ? one.RestVoltage(node)
            : ((NetworkStage<Devices<Jfet, Jfet>>)stage).RestVoltage(node);

    static double Voltage(AmpStage stage, int node) =>
        stage switch {
            NetworkStage<Devices<Jfet, Jfet>> two => two.Voltage(node),
            NetworkStage<Devices<DiodeClipper, DiodeClipper>> clamps => clamps.Voltage(node),
            _ => ((NetworkStage<NoDevice>)stage).Voltage(node)
        };
}
