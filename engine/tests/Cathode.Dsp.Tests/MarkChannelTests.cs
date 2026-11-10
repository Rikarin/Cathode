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
///     Limits are the measured errors with a few dB of margin: -46 dB at either depth (there is no plate-bypassed
///     stage in a rhythm chain for the Thévenin cuts to miss).
/// </remarks>
public partial class AmpStageTests {
    public static TheoryData<string, int, double, double> MarkRhythmChains =>
        new() { { "Mark IIC+ Rhythm", 3, 0.002, -42 }, { "Mark IIC+ Rhythm", 4, 0.05, -42 } };

    [Theory]
    [MemberData(nameof(MarkRhythmChains))]
    public void Mark_rhythm_chains_match_the_reference(string type, int count, double amplitude, double limitDb) =>
        Mesa_and_boutique_chains_match_the_reference(type, 0, count, amplitude, limitDb);
}

/// <summary>
///     The Mark IIC+ rhythm channel on the Circuit Amp block: its panel, knob sweeps without instability, gain and
///     master monotonic, and what tells it from the lead.
/// </summary>
public class MarkChannelTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = ["Mark IIC+ Rhythm"];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Volume", Label("gain", "Mark IIC+ Rhythm"));
        Assert.Equal(["Treble Shift", "Deep", "Bass Shift"], Switches("Mark IIC+ Rhythm"));
        Assert.Equal("Bright", Label("bright", "Mark IIC+ Rhythm"));
        Assert.Empty(Label("control1", "Mark IIC+ Rhythm"));
        Assert.Empty(Label("control2", "Mark IIC+ Rhythm"));
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
    ///     A rhythm type is its lead type without the lead circuit: the same V1B, power amp and EQ, two triodes fewer.
    /// </summary>
    [Theory]
    [InlineData("Mark IIC+ Rhythm", "Mark IIC+")]
    public void Rhythm_types_share_the_lead_types_amp_and_skip_the_lead_circuit(string rhythm, string lead) {
        var (r, l) = (Design(rhythm), Design(lead));
        Assert.Same(l.Power, r.Power);
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
    ///     The parts that make the rhythm channel, as read from the factory scan: the 3.3M ‖ 20 pF feed into 680k ‖
    ///     47 pF, and no lead master on the recovery stage.
    /// </summary>
    [Fact]
    public void Rhythm_channels_have_the_scans_parts() {
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Resistor, 3.3e6));
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Capacitor, 20e-12));
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Resistor, 680e3));
        Assert.True(Has("Mark IIC+ Rhythm", "V2B", ElementKind.Capacitor, 47e-12));
        Assert.DoesNotContain(Elements("Mark IIC+ Rhythm", "V4A"), static e => e.Part != PotPart.None);

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
    ///     The rhythm channel has less gain than the lead (two stages fewer): small-signal at 1 kHz with every gain and
    ///     master alike.
    /// </summary>
    [Fact]
    public void Rhythm_channels_have_less_gain_than_the_lead() {
        static double Level(string type) =>
            Small(type, 1_000, 5f, (CircuitAmpBlock.Control1, 5f), (CircuitAmpBlock.Control2, 5f));

        var (lead2, rhythm2) = (Level("Mark IIC+"), Level("Mark IIC+ Rhythm"));
        output.WriteLine($"Mark IIC+: lead {lead2:0.0} dB, rhythm {rhythm2:0.0} dB");
        Assert.True(lead2 - rhythm2 > 10, $"the IIC+ lead is only {lead2 - rhythm2:0.0} dB over its rhythm channel");
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
