using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Numerics;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Peavey 5150 II (6505+), lead and rhythm channels (#362): the panels, the rails against the drawing's
///     dropping chain (the drawing prints no voltages to check them against), the lead chain against the reference
///     simulator, the gain structure, what each control and switch does, the feedback network's presence and
///     resonance, and stability under sweeps.
/// </summary>
public class Peavey6505PlusTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const string Lead = "6505+";
    const string Rhythm = "6505+ Rhythm";

    /// <summary>
    ///     The two types follow the models that were there before them (a type's index is its saved value), the rhythm
    ///     channel straight after the lead, and the 5150 keeps its place.
    /// </summary>
    [Fact]
    public void Both_types_are_appended() {
        Assert.Equal(22, TypeOf("5150"));
        Assert.True(TypeOf(Lead) > TypeOf("Dual Rectifier 3-Ch Orange"));
        Assert.Equal(TypeOf(Lead) + 1, TypeOf(Rhythm));
        Assert.Same(Peavey6505Plus.Design.Power, Peavey6505Plus.Rhythm.Power);
    }

    [Fact]
    public void Panels_name_the_amps_controls() {
        foreach (var type in new[] { Lead, Rhythm }) {
            Assert.Equal(
                ["Pre Gain", "Low", "Mid", "High", "Post Gain", "Presence", "Resonance"],
                new[] { "gain", "bass", "middle", "treble", "master", "presence", "depth" }.Select(id => Label(id, type)
                )
            );
            foreach (var id in new[] { "cut", "switch2", "switch3", "control1", "control2", "eq1" }) {
                Assert.Empty(Label(id, type));
            }
        }

        Assert.Empty(Label("bright", Lead));
        Assert.Empty(Label("switch1", Lead));
        Assert.Equal("Bright", Label("bright", Rhythm));
        Assert.Equal("Crunch", Label("switch1", Rhythm));
    }

    /// <summary>
    ///     The rails the stages assume are the drawing's chain at rest (4k to V3, 11k to V2, 10k to V1) with the
    ///     currents the model's tubes draw there.
    /// </summary>
    [Theory]
    [InlineData(Lead)]
    [InlineData(Rhythm)]
    public void Rails_are_the_drawn_chains_rest_voltages(string type) {
        new PreampSupplyTests().Assumed_rails_are_the_chains_rest_voltages(type);
        var design = AmpDesign.All[TypeOf(type)];
        var supply = design.Power.PowerSupply;
        Assert.Equal((400, 4e3), (supply.ChokeResistance, supply.PreampResistance));
        Assert.Equal([11e3, 10e3], design.PreampSupply!.Nodes.Select(static n => n.Resistance));
        Assert.Equal([0, 1], design.PreampSupply.Nodes.Select(static n => n.Parent));
    }

    /// <summary>
    ///     The lead channel's first four stages (V1A and V1B in one circuit, the V2A cold clipper, V2B, V5B) against
    ///     the reference simulator, as the other high-gain front ends are checked. Measured -36.5 dB at 2 mV and -30.9
    ///     dB at 50 mV (-23 and -20 dB with V1A cut from the networks on its coupling cap).
    /// </summary>
    [Theory]
    [InlineData(0.002, -32)]
    [InlineData(0.05, -26)]
    public void Lead_chain_matches_the_reference(double amplitude, double limitDb) =>
        new AmpStageTests(output).Cold_clipper_chains_match_the_reference(Lead, false, amplitude, limitDb);

    [Theory]
    [InlineData(Lead)]
    [InlineData(Rhythm)]
    public void Sweeps_stay_stable_and_the_gains_rise_monotonically(string type) {
        var shared = new HighGainAmpTests(output);
        shared.Knob_sweeps_at_full_drive_stay_finite_and_converge(type);
        shared.Gain_and_master_raise_the_level_monotonically(type);
    }

    /// <summary>
    ///     Below clipping at 1 kHz with the pre gains at 2: Crunch takes the rhythm channel's pad out (2.2 nF in place
    ///     of 470k into 22k), and the lead channel's four stages ahead of the stack have more gain still.
    /// </summary>
    [Fact]
    public void Gain_rises_from_rhythm_to_crunch_to_lead() {
        var clean = Level(Rhythm, 1_000);
        var crunch = Level(Rhythm, 1_000, (CircuitAmpBlock.Switch1, 1f));
        var lead = Level(Lead, 1_000);
        output.WriteLine($"1 kHz, 10 µV: rhythm {clean:0.0}, crunch {crunch:0.0}, lead {lead:0.0} dB");
        Assert.InRange(crunch - clean, 20, 45);
        Assert.True(lead > crunch + 6);
    }

    /// <summary>
    ///     Each tone control lifts its own band, on both channels, and Bright lifts the rhythm channel's top.
    /// </summary>
    [Theory]
    [InlineData(Lead)]
    [InlineData(Rhythm)]
    public void Tone_controls_lift_their_bands(string type) {
        (int Knob, double Frequency, double Other)[] bands = [
            (CircuitAmpBlock.Bass, 80, 2_000), (CircuitAmpBlock.Middle, 500, 5_000),
            (CircuitAmpBlock.Treble, 5_000, 200)
        ];
        foreach (var (knob, frequency, other) in bands) {
            var own = Level(type, frequency, (knob, 10f)) - Level(type, frequency, (knob, 0f));
            var far = Level(type, other, (knob, 10f)) - Level(type, other, (knob, 0f));
            output.WriteLine($"{type} knob {knob}: {own:0.0} dB at {frequency} Hz, {far:0.0} dB at {other} Hz");
            Assert.True(own > 3, $"{type}: knob {knob} lifts {frequency} Hz by {own:0.0} dB");
            Assert.True(own > far + 1, $"{type}: knob {knob} lifts {other} Hz as much ({far:0.0} dB)");
        }

        if (type == Rhythm) {
            double Tilt(float bright) =>
                Level(type, 5_000, (CircuitAmpBlock.Bright, bright))
                - Level(type, 300, (CircuitAmpBlock.Bright, bright));

            output.WriteLine($"Bright: 5 kHz over 300 Hz {Tilt(0):0.0} dB out, {Tilt(1):0.0} dB in");
            Assert.True(Tilt(1) > Tilt(0) + 2);
        }
    }

    /// <summary>
    ///     The presence network (the pot's track in series with 0.1 µF across the 4.7k, 33 nF from the wiper to
    ///     ground): the two sections the power amp runs are the network's own response, the mid-band feedback is 4.7k
    ///     over 43.7k, and turning the knob up takes feedback out of the top.
    /// </summary>
    [Fact]
    public void Presence_sections_are_the_tapped_network() {
        var feedback = Peavey6505Plus.Design.Power.Feedback;
        Assert.Equal(
            (0.1e-6, 33e-9, 39e3, 4.7e3),
            (
                feedback.PresenceCap, feedback.PresenceTapCap, feedback.FeedbackResistor, feedback.ShuntResistor
            )
        );
        foreach (var presence in new[] { 0, 0.25, 0.5, 0.75, 1 }) {
            var (gain, zero1, pole1, zero2, pole2) = feedback.TapTimes(presence);
            foreach (var frequency in new[] { 20.0, 200, 1_000, 5_000, 20_000 }) {
                var s = new Complex(0, 2 * Math.PI * frequency);
                var sections = gain * (1 + s * zero1) * (1 + s * zero2) / ((1 + s * pole1) * (1 + s * pole2));
                var network = feedback.Response(frequency, presence);
                Assert.True((sections - network).Magnitude < 1e-9 * network.Magnitude, $"{presence} at {frequency} Hz");
            }
        }

        Assert.Equal(4.7 / 43.7, feedback.Response(0.01, 0.5).Magnitude, 1e-6);

        // At 10 the 33 nF lies across the shunt: half the feedback is gone by 1/(2π (4.7k ∥ 39k ∥ 10k) 33 nF), 1.6 kHz.
        var top = new[] { 0, 0.5, 1 }.Select(p => feedback.Response(8_000, p).Magnitude).ToArray();
        Assert.True(top[0] > top[1] && top[1] > top[2]);
        Assert.True(top[2] < 0.3 * feedback.Beta);

        // At 0 the whole 10k track stays in series with both caps: the shunt falls to 4.7k ∥ 10k and no further.
        Assert.Equal(3.197 / 42.197, feedback.Response(100e3, 0).Magnitude, 1e-3);
    }

    /// <summary>
    ///     The resonance (1M audio rheostat with 6.8 nF across it, in series with the 39k): flat at 0; at 10 the low
    ///     feedback is 43.7k over 1043.7k of the mid band's with the pole at 1 / (2π (1M ∥ 43.7k) 6.8 nF), 559 Hz. The
    ///     5150 keeps the generic shelf, a tenth at its depth frequency.
    /// </summary>
    [Fact]
    public void Resonance_is_the_rheostat_and_cap_in_the_feedback_arm() {
        var feedback = Peavey6505Plus.Design.Power.Feedback;
        Assert.Equal(new SeriesResonance(new(1e6, PotTaper.Audio), 6.8e-9), feedback.Resonance);
        Assert.Equal(1, feedback.DepthShelf(0).Floor);
        var (floor, omega) = feedback.DepthShelf(1);
        Assert.Equal(43.7 / 1043.7, floor, 1e-9);
        Assert.Equal(559, omega / (2 * Math.PI), 1.0);
        var noon = feedback.DepthShelf(0.5);
        Assert.InRange(noon.Floor, floor, 1);
        Assert.True(noon.Omega > omega);
        Assert.Equal(2 * omega, feedback.DepthShelf(1, 2).Omega, 1e-6 * omega);

        var generic = Peavey5150.Design.Power.Feedback;
        Assert.Null(generic.Resonance);
        Assert.Equal(0.1, generic.DepthShelf(1).Floor, 1e-12);
        Assert.Equal(1, generic.DepthShelf(0, 1.5).Floor);
        Assert.Equal(2 * Math.PI * generic.DepthFrequency * 1.5, generic.DepthShelf(0, 1.5).Omega, 1e-9);
    }

    /// <summary>
    ///     Through the whole amp, below clipping: Resonance lifts the bottom and Presence the top, each against 1 kHz.
    /// </summary>
    [Fact]
    public void Resonance_lifts_the_lows_and_presence_the_highs() {
        static double Lift(int knob, double frequency) =>
            Level(Rhythm, frequency, (knob, 10f))
            - Level(Rhythm, frequency, (knob, 0f))
            - (Level(Rhythm, 1_000, (knob, 10f)) - Level(Rhythm, 1_000, (knob, 0f)));

        var resonance = Lift(CircuitAmpBlock.Depth, 90);
        var presence = Lift(CircuitAmpBlock.Presence, 6_000);
        output.WriteLine($"Resonance: {resonance:0.0} dB at 90 Hz; Presence: {presence:0.0} dB at 6 kHz (over 1 kHz)");
        Assert.True(resonance > 2);
        Assert.True(presence > 2);
    }

    /// <summary>The level in dB of a 10 µV sine through the block, pre and post gain at 2 unless set.</summary>
    static double Level(string type, double frequency, params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, TypeOf(type));
        block.Params.SetTarget(CircuitAmpBlock.Gain, 2f);
        block.Params.SetTarget(CircuitAmpBlock.Master, 2f);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, 1e-5f), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }

    static int TypeOf(string name) => AmpDesign.All.ToList().FindIndex(a => a.Name == name);

    static string Label(string id, string type) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] : parameter.Name;
    }
}
