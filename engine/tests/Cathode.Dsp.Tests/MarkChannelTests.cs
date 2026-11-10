using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Mark rhythm channels' preamps against the reference MNA simulator as whole chains, the way
///     <see cref="Mesa_and_boutique_chains_match_the_reference" /> checks the other Mesa models: V1A with its stack,
///     V1B and the return stage (three stages), and with the recovery stage behind the master (four).
/// </summary>
/// <remarks>
///     Limits are the measured errors with a few dB of margin: -46 dB on the clean channels at either depth (there is
///     no plate-bypassed stage in a rhythm chain for the Thévenin cuts to miss), -43 dB on the Mark IV's Rhythm 2
///     through the return stage and -25 dB with the recovery stage behind it at 50 mV, where V1B drives the return
///     stage's grid into conduction.
/// </remarks>
public partial class AmpStageTests {
    public static TheoryData<string, int, double, double> MarkRhythmChains =>
        new() {
            { "Mark IIC+ Rhythm", 3, 0.002, -42 },
            { "Mark IIC+ Rhythm", 4, 0.05, -42 },
            { "Mark IV Rhythm 1", 3, 0.002, -42 },
            { "Mark IV Rhythm 1", 4, 0.05, -42 },
            { "Mark IV Rhythm 2", 3, 0.002, -38 },
            { "Mark IV Rhythm 2", 4, 0.05, -21 }
        };

    [Theory]
    [MemberData(nameof(MarkRhythmChains))]
    public void Mark_rhythm_chains_match_the_reference(string type, int count, double amplitude, double limitDb) =>
        Mesa_and_boutique_chains_match_the_reference(type, 0, count, amplitude, limitDb);
}

/// <summary>
///     The Mark IIC+ and Mark IV rhythm channels on the Circuit Amp block: their panels, knob sweeps without
///     instability, gain and master monotonic, and what tells each channel from the lead and from the other.
/// </summary>
public class MarkChannelTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = ["Mark IIC+ Rhythm", "Mark IV Rhythm 1", "Mark IV Rhythm 2"];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Volume", Label("gain", "Mark IIC+ Rhythm"));
        Assert.Equal(["Treble Shift", "Deep", "Bass Shift"], Switches("Mark IIC+ Rhythm"));
        Assert.Equal("Bright", Label("bright", "Mark IIC+ Rhythm"));
        Assert.Empty(Label("control1", "Mark IIC+ Rhythm"));
        Assert.Empty(Label("control2", "Mark IIC+ Rhythm"));

        Assert.Equal("R1 Master", Label("control1", "Mark IV Rhythm 1"));
        Assert.Equal("Bright", Label("bright", "Mark IV Rhythm 1"));
        Assert.Equal([string.Empty, string.Empty, string.Empty], Switches("Mark IV Rhythm 1"));
        Assert.Equal("R2 Master", Label("control1", "Mark IV Rhythm 2"));
        Assert.Empty(Label("bright", "Mark IV Rhythm 2"));
        Assert.Equal(["Fat", string.Empty, string.Empty], Switches("Mark IV Rhythm 2"));
        foreach (var type in Models) {
            Assert.Empty(Label("cut", type));
            Assert.Empty(Label("depth", type));
            Assert.Empty(Label("control2", type));
            Assert.Equal(
                ["80", "240", "750", "2200", "6600"],
                Enumerable.Range(1, 5).Select(b => Label($"eq{b}", type))
            );
        }
    }

    /// <summary>
    ///     A rhythm type is its lead type without the lead circuit: the same V1B, power amp (but for the presence
    ///     rheostat's shift cap, which each Mark IV channel has its own of) and EQ, two triodes fewer.
    /// </summary>
    [Theory]
    [InlineData("Mark IIC+ Rhythm", "Mark IIC+")]
    [InlineData("Mark IV Rhythm 1", "Mark IV")]
    [InlineData("Mark IV Rhythm 2", "Mark IV")]
    public void Rhythm_types_share_the_lead_types_amp_and_skip_the_lead_circuit(string rhythm, string lead) {
        var (r, l) = (Design(rhythm), Design(lead));
        Assert.Equal(l.Power with { Feedback = r.Power.Feedback }, r.Power);
        var series = l.Power.Feedback.Series!.Value;
        Assert.Equal(series with { ShiftCap = 0 }, r.Power.Feedback.Series!.Value with { ShiftCap = 0 });
        Assert.Equal(l.Preamp[1], r.Preamp[1]);
        Assert.Equal(l.Preamp.Count - 2, r.Preamp.Count);
        Assert.Equal(Triodes(l) - 2, Triodes(r));

        static int Triodes(AmpDesign design) =>
            design.Preamp.Sum(static s => s switch {
                    TriodeStageDesign => 1,
                    NetworkStageDesign n => n.Netlist.Elements.Count(static e => e.Kind == ElementKind.Triode),
                    _ => 0
                }
            );
    }

    /// <summary>
    ///     The parts that make each rhythm channel, as read from the factory scans: the 3.3M ‖ 20 pF feed into 680k ‖
    ///     47 pF on the clean channels and its absence on the Mark IV's Rhythm 2; the 4.7k pad missing only on the Mark
    ///     IV's Rhythm 1; Rhythm 1's 120 pF bright cap and 470k; Rhythm 2's second treble pot's 68k and its Fat 750 pF.
    /// </summary>
    [Fact]
    public void Rhythm_channels_have_the_scans_parts() {
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Resistor, 3.3e6));
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Capacitor, 20e-12));
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Resistor, 680e3));
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Capacitor, 47e-12));
        Assert.DoesNotContain(Elements("Mark IIC+ Rhythm", "V4A"), static e => e.Part != PotPart.None);

        Assert.True(Has("Mark IV Rhythm 1", "V3A", ElementKind.Resistor, 3.3e6));
        Assert.True(Has("Mark IV Rhythm 1", "V3A", ElementKind.Resistor, 680e3));
        Assert.True(Has("Mark IV Rhythm 1", "V3A", ElementKind.Resistor, 22e3));
        Assert.False(Has("Mark IV Rhythm 2", "V3A", ElementKind.Resistor, 3.3e6));
        Assert.False(Has("Mark IV Rhythm 2", "V3A", ElementKind.Resistor, 680e3));
        Assert.False(Has("Mark IV Rhythm 2", "V3A", ElementKind.Resistor, 22e3));

        Assert.False(Has("Mark IV Rhythm 1", "V4A", ElementKind.Resistor, 4.7e3));
        Assert.True(Has("Mark IV Rhythm 2", "V4A", ElementKind.Resistor, 4.7e3));
        Assert.True(Has("Mark IV", "V4A", ElementKind.Resistor, 4.7e3));

        Assert.True(Has("Mark IV Rhythm 1", "V1A", ElementKind.Resistor, 470e3));
        Assert.Contains(
            Elements("Mark IV Rhythm 1", "V1A"),
            static e => e is { Value: 120e-12, Switch: SwitchRole.Bright }
        );
        Assert.True(Has("Mark IV Rhythm 2", "V1A", ElementKind.Resistor, 68e3));
        Assert.Contains(
            Elements("Mark IV Rhythm 2", "V1A"),
            static e => e is { Value: 750e-12, Switch: SwitchRole.Switch1 }
        );

        static IReadOnlyList<StageElement> Elements(string type, string stage) =>
            ((NetworkStageDesign)Design(type).Preamp.Single(s => s.Name == stage)).Netlist.Elements;

        static bool Has(string type, string stage, ElementKind kind, double value) =>
            Elements(type, stage).Any(e => e.Kind == kind && e.Value == value);
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, and Newton converging.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Master, CircuitAmpBlock.Control1, CircuitAmpBlock.Control2
        ];
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var knob in knobs) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(knob, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: knob {knob} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        int[] toggles =
            [CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3, CircuitAmpBlock.Bright];
        foreach (var toggle in toggles) {
            block.Params.SetTarget(toggle, 1f);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type}: switch {toggle}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        var solvers = channel.Stages
            .Where(static s => s.Newton is not null)
            .Select(static s => s.Newton!)
            .Append(channel.Power.Newton);
        foreach (var newton in solvers) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type}: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"{type}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>Below clipping, the gain, the channel master and the master raise the level monotonically.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Gain_and_masters_raise_the_level_monotonically(string type) {
        int[] knobs = type == "Mark IIC+ Rhythm"
            ? [CircuitAmpBlock.Gain, CircuitAmpBlock.Master]
            : [CircuitAmpBlock.Gain, CircuitAmpBlock.Master, CircuitAmpBlock.Control1];
        foreach (var knob in knobs) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Small(type, 1_000, 3f, (knob, v))).ToArray();
            output.WriteLine($"{type} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Length; i++) {
                Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
            }

            Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    /// <summary>
    ///     A rhythm channel has less gain than its lead (two stages fewer), small-signal at 1 kHz with every gain and
    ///     master alike. The Mark IV's two rhythm channels differ less in gain than in where it sits: Rhythm 2 drives
    ///     the return stage straight from V1B and pads it afterwards, so at a guitar's level with the gain up it
    ///     distorts where Rhythm 1, fed through the 3.3M and left unpadded, stays clean.
    /// </summary>
    [Fact]
    public void Rhythm_channels_have_less_gain_than_the_lead_and_the_crunch_distorts_where_the_clean_does_not() {
        static double Level(string type) =>
            Small(type, 1_000, 5f, (CircuitAmpBlock.Control1, 5f), (CircuitAmpBlock.Control2, 5f));

        var (lead2, rhythm2) = (Level("Mark IIC+"), Level("Mark IIC+ Rhythm"));
        var (lead4, clean4, crunch4) = (Level("Mark IV"), Level("Mark IV Rhythm 1"), Level("Mark IV Rhythm 2"));
        output.WriteLine($"Mark IIC+: lead {lead2:0.0} dB, rhythm {rhythm2:0.0} dB");
        output.WriteLine($"Mark IV: lead {lead4:0.0} dB, Rhythm 1 {clean4:0.0} dB, Rhythm 2 {crunch4:0.0} dB");
        Assert.True(lead2 - rhythm2 > 10, $"the IIC+ lead is only {lead2 - rhythm2:0.0} dB over its rhythm channel");
        Assert.True(lead4 - crunch4 > 5, $"the Mark IV lead is only {lead4 - crunch4:0.0} dB over Rhythm 2");
        Assert.True(crunch4 > clean4, "Rhythm 2 has less gain than Rhythm 1");

        var (clean, crunch) = (Distortion("Mark IV Rhythm 1"), Distortion("Mark IV Rhythm 2"));
        output.WriteLine($"THD at 0.1 V, gain 8: Rhythm 1 {100 * clean:0.0} %, Rhythm 2 {100 * crunch:0.0} %");
        Assert.True(crunch > 3 * clean, $"Rhythm 2 distorts {100 * crunch:0.0} %, Rhythm 1 {100 * clean:0.0} %");

        // A coherent sine (bin 37 of 8192: 217 Hz) with the masters low, so that the power amp stays clean.
        static double Distortion(string type) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 8f),
                (CircuitAmpBlock.Control1, 3f),
                (CircuitAmpBlock.Master, 2f)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(37 * Rate / 8_192, Rate, 32_768, 0.1f), 64);
            return Analysis.Thd(rendered.AsSpan(rendered.Length - 8_192), 37);
        }
    }

    /// <summary>
    ///     The Mark IV's rhythm pulls: Rhythm 1's Bright (120 pF across the gain's upper half) lifts the treble at a low
    ///     gain setting and leaves the lows; Rhythm 2's Fat (750 pF more on the treble cap) lifts the mids.
    /// </summary>
    [Fact]
    public void Mark_IV_rhythm_pulls_are_audible() {
        static double Bright(double frequency, float on) =>
            Small("Mark IV Rhythm 1", frequency, 3f, (CircuitAmpBlock.Bright, on));

        static double Fat(double frequency, float on) =>
            Small("Mark IV Rhythm 2", frequency, 3f, (CircuitAmpBlock.Switch1, on));

        var (brightHigh, brightLow) = (Bright(5_000, 1) - Bright(5_000, 0), Bright(100, 1) - Bright(100, 0));
        var (fatMid, fatLow) = (Fat(700, 1) - Fat(700, 0), Fat(80, 1) - Fat(80, 0));
        output.WriteLine($"Rhythm 1 Bright: {brightHigh:+0.0;-0.0} dB at 5 kHz, {brightLow:+0.0;-0.0} dB at 100 Hz");
        output.WriteLine($"Rhythm 2 Fat: {fatMid:+0.0;-0.0} dB at 700 Hz, {fatLow:+0.0;-0.0} dB at 80 Hz");
        Assert.True(brightHigh > 3, "Bright does not lift the treble");
        Assert.InRange(brightLow, -0.5, 0.5);
        Assert.True(fatMid > 1, "Fat does not lift the mids");
        Assert.True(fatMid > fatLow + 1, "Fat lifts the lows as much as the mids");
    }

    /// <summary>
    ///     The Mark IV's presence pull shift, a cap in series with the lead and Rhythm 2 rheostats (1 nF, 680 pF; none
    ///     on Rhythm 1): the feedback filter's three sections equal the divider written out from the parts at every
    ///     setting and frequency. Shifted, there is no feedback at DC, and far above the audio band, where the cap is
    ///     a short, the feedback is the unshifted network's.
    /// </summary>
    [Theory]
    [InlineData("Mark IV", 1e-9)]
    [InlineData("Mark IV Rhythm 1", 0)]
    [InlineData("Mark IV Rhythm 2", 680e-12)]
    public void Mark_IV_presence_shift_matches_its_circuit(string type, double shiftCap) {
        var design = Design(type).Power.Feedback;
        Assert.Equal(new SeriesPresence(56e3, 3.3e3, 47e-9, shiftCap), design.Series);
        if (shiftCap == 0) {
            Assert.Equal(design.Response(1_000, 0.5), design.Response(1_000, 0.5, shift: true));
            return;
        }

        var (rf, rx, rl, cl, rm, cm) = (3.3e3, 1.5e3, 56e3, 5e-9, 3.3e3, 47e-9);
        foreach (var presence in new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 1.0 }) {
            var rp = 250e3 * presence;
            var (rise, pole0, zero1, pole1, zero2, pole2) = design.ShiftedTimes(presence);
            Assert.True(pole0 >= pole1 && pole1 >= pole2 && pole2 > 0, $"poles {pole0}, {pole1}, {pole2}");
            foreach (var frequency in new[] { 20.0, 100, 500, 1_000, 3_000, 10_000, 20_000, 100_000 }) {
                var s = new System.Numerics.Complex(0, 2 * Math.PI * frequency);
                var arm = rp + rf + rl / (1 + s * rl * cl) + 1 / (s * shiftCap);
                var foot = rx + rm / (1 + s * rm * cm);
                var circuit = foot / (arm + foot);
                var model = rise
                    * s
                    * (1 + s * zero1)
                    * (1 + s * zero2)
                    / ((1 + s * pole0) * (1 + s * pole1) * (1 + s * pole2));
                Assert.True(
                    (circuit - model).Magnitude < 1e-9 * circuit.Magnitude,
                    $"presence {presence}, {frequency} Hz: {model.Magnitude:0.000000} against {circuit.Magnitude:0.000000}"
                );
                Assert.True((design.Response(frequency, presence, shift: true) - circuit).Magnitude < 1e-12);
            }

            Assert.True(design.Response(0.01, presence, shift: true).Magnitude < 1e-5);
            Assert.Equal(
                design.Response(100e6, presence).Magnitude,
                design.Response(100e6, presence, shift: true).Magnitude,
                1e-3
            );
            // Shifted, the feedback is less at every audio frequency, and most where the cap blocks it.
            Assert.True(
                design.Response(200, presence, shift: true).Magnitude < 0.5 * design.Response(200, presence).Magnitude
            );
            Assert.True(
                design.Response(10_000, presence, shift: true).Magnitude < design.Response(10_000, presence).Magnitude
            );
        }
    }

    /// <summary>
    ///     On the block the Presence Shift switch is that pull on the Mark IV's lead and Rhythm 2: with the feedback
    ///     gone below the cap's reach the amp is louder at 200 Hz (the loop no longer holds it down), which the generic
    ///     switch on Rhythm 1 (the presence frequency an octave down) does not do. Flat out with the shift on, at both
    ///     ends of the Presence knob, a loud chord renders finite and the amp is silent two seconds after it stops.
    /// </summary>
    [Fact]
    public void Mark_IV_presence_shift_is_the_amps_pull_on_the_lead_and_rhythm_2() {
        static double Lift(string type) =>
            Small(type, 200, 3f, (CircuitAmpBlock.PresenceShift, 1f), (CircuitAmpBlock.Presence, 0f))
            - Small(type, 200, 3f, (CircuitAmpBlock.Presence, 0f));

        var (lead, clean, crunch) = (Lift("Mark IV"), Lift("Mark IV Rhythm 1"), Lift("Mark IV Rhythm 2"));
        output.WriteLine(
            $"Presence Shift at 200 Hz, presence 0: lead {lead:+0.0;-0.0} dB, Rhythm 1 {clean:+0.0;-0.0} dB, "
            + $"Rhythm 2 {crunch:+0.0;-0.0} dB"
        );
        Assert.True(lead > 0.5, $"the lead's shift lifts 200 Hz by {lead:0.00} dB");
        Assert.True(crunch > 0.5, $"Rhythm 2's shift lifts 200 Hz by {crunch:0.00} dB");
        Assert.True(clean < 0.3, $"Rhythm 1's generic shift lifts 200 Hz by {clean:0.00} dB");

        var chord = new float[(int)(2.5 * Rate)];
        for (var n = 0; n < (int)(0.5 * Rate); n++) {
            var t = n / Rate;
            chord[n] = (float)(0.4
                * (Math.Sin(2 * Math.PI * 82.4 * t)
                    + Math.Sin(2 * Math.PI * 123.5 * t)
                    + Math.Sin(2 * Math.PI * 164.8 * t))
                / 3);
        }

        foreach (var type in new[] { "Mark IV", "Mark IV Rhythm 2" }) {
            foreach (var presence in new[] { 0f, 10f }) {
                using var block = Create(
                    type,
                    (CircuitAmpBlock.Gain, 10f),
                    (CircuitAmpBlock.Control1, 10f),
                    (CircuitAmpBlock.Control2, 10f),
                    (CircuitAmpBlock.Master, 10f),
                    (CircuitAmpBlock.Presence, presence),
                    (CircuitAmpBlock.PresenceShift, 1f)
                );
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type} at presence {presence}: not finite");
                var tail = Analysis.ToDb(Analysis.Rms(rendered.AsSpan(rendered.Length - (int)(0.1 * Rate))));
                output.WriteLine($"{type}, shifted, presence {presence}: tail {tail:0.0} dB");
                Assert.True(tail < -100, $"{type} at presence {presence}: {tail:0.0} dB two seconds after the chord");
            }
        }
    }

    /// <summary>The level in dB of a 0.1 mV sine through the block, at gain <paramref name="gain" />.</summary>
    static double Small(string type, double frequency, float gain, params (int Index, float Value)[] settings) {
        using var block = Create(type, [(CircuitAmpBlock.Gain, gain), (CircuitAmpBlock.Master, 3f), ..settings]);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, 1e-4f), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }

    static AmpDesign Design(string name) => AmpDesign.All.Single(a => a.Name == name);

    static int TypeOf(string name) => AmpDesign.All.ToList().FindIndex(a => a.Name == name);

    static CircuitAmpBlock Create(string type, params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, TypeOf(type));
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }

    static string Label(string id, string type) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] : parameter.Name;
    }

    static IEnumerable<string> Switches(string type) =>
        new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, type));
}
