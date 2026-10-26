using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Splawn Quick Rod against the reference MNA simulator: each channel's playing chain at each setting of the OD
///     and gear switches, as one circuit with no Thévenin cut (V1A, V1B and the paralleled V3 on their shared cathode,
///     and in the overdrive channel V2A too, in the first netlist; the one cut is at V2B's grid).
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<int, double, double> QuickRodChains =>
        new() {
            // Switches as bits: 1 = Switch 1 (Clean), 2 = Switch 2 (2nd gear), 4 = Switch 3 (3rd gear), 8 = OD2.
            { 0, 0.001, -50 },
            { 2, 0.0005, -50 },
            { 6, 0.0003, -50 },
            { 8, 0.0003, -50 },
            { 14, 0.0001, -50 },
            { 1, 0.01, -55 },
            { 9, 0.01, -55 }
        };

    [Theory]
    [MemberData(nameof(QuickRodChains))]
    public void Quick_rod_chains_match_the_reference(int switches, double amplitude, double limitDb) {
        var controls = new AmpControls(
            0.5,
            0.5,
            0.5,
            0.5,
            0.5,
            (switches & 8) != 0,
            Switch1: (switches & 1) != 0,
            Switch2: (switches & 2) != 0,
            Switch3: (switches & 4) != 0,
            Control1: 0.3
        );
        var designs = Playing(QuickRod.Design.Preamp, controls).Cast<NetworkStageDesign>().ToArray();

        var stages = designs.Select(static d => d.Create(Rate)).ToArray();
        var (rest, resistance) = (0.0, 0.0);
        foreach (var stage in stages) {
            stage.Update(controls);
            stage.SetSourceResistance(resistance);
            stage.Reset(rest);
            (rest, resistance) = (stage.RestOutput, stage.OutputResistance);
        }

        var circuit = new ReferenceCircuit();
        var node = circuit.Node();
        circuit.Source(node, 0, t => Drive(amplitude, t));
        foreach (var d in designs) {
            node = AmpReferences.Append(circuit, d, controls, PreampSettings.Stock, node);
        }

        var errorDb = CompareInTime(
            $"Quick Rod [{switches}] {string.Join(" > ", designs.Select(static d => d.Name))}",
            x => {
                foreach (var stage in stages) {
                    x = stage.Tick(x);
                }

                return x;
            },
            stages.Last(static s => s.Newton is not null).Newton,
            circuit,
            node,
            amplitude
        );
        Assert.True(errorDb < limitDb, $"Quick Rod [{switches}]: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The Quick Rod on the Circuit Amp block: its panel, the switches doing what the circuit says (OD2 and each gear
///     raise the gain, the solo boost the level), knob sweeps at full drive in every channel and mode without
///     instability, every control audible, gain and master monotonic, levels below +6 dBFS.
/// </summary>
public class QuickRodTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly int[] Knobs = [
        CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
        CircuitAmpBlock.Presence, CircuitAmpBlock.Master, CircuitAmpBlock.Control1
    ];

    /// <summary>Switch settings as bits: 1 = Clean, 2 = 2nd gear, 4 = 3rd gear, 8 = OD2.</summary>
    public static TheoryData<int> Settings => [0, 2, 6, 8, 14, 1, 9];

    static int Type => AmpDesign.All.ToList().IndexOf(QuickRod.Design);

    [Fact]
    public void Panel_names_the_real_controls_and_hides_the_rest() {
        Assert.Equal(
            ["OD2", "Clean", "2nd Gear", "3rd Gear", "Solo Boost"],
            new[] { "bright", "switch1", "switch2", "switch3", "control1" }.Select(Label)
        );
        Assert.Equal(
            ["Gain", "Bass", "Middle", "Treble", "Master", "Presence"],
            new[] { "gain", "bass", "middle", "treble", "master", "presence" }.Select(Label)
        );
        Assert.Empty(Label("depth"));
        Assert.Empty(Label("cut"));
        Assert.Empty(Label("control2"));
    }

    /// <summary>
    ///     Below clipping, OD2 has more gain than OD1, 2nd gear than 1st and 3rd than 2nd (440 Hz, the Master low); the
    ///     overdrive channel has far more than the clean one.
    /// </summary>
    [Fact]
    public void Od2_and_the_gears_raise_the_gain_in_order() {
        var tone = Signals.Sine(440, Rate, 24_000, 0.0003f);

        double Level(int setting) {
            using var block = Create([(CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Master, 2f), ..Switches(setting)]);
            var rendered = BlockRender.Mono(block, tone, 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), 440, Rate));
        }

        var (first, second, third) = (Level(0), Level(2), Level(6));
        var (od2, clean) = (Level(8), Level(1));
        output.WriteLine($"1st {first:0.0}, 2nd {second:0.0}, 3rd {third:0.0}, OD2 {od2:0.0}, clean {clean:0.0} dB");
        Assert.True(second > first + 2, "2nd gear louder than 1st below clipping");
        Assert.True(third > second + 2, "3rd gear louder than 2nd below clipping");
        Assert.True(od2 > first + 2, "OD2 louder than OD1 below clipping");
        Assert.True(first > clean + 10, "the overdrive channel louder than the clean one");
    }

    /// <summary>The Solo Boost raises the overdrive channel's level at a low Master, and 0 is the boost off.</summary>
    [Fact]
    public void Solo_boost_lifts_the_master() {
        var tone = Signals.Sine(440, Rate, 24_000, 0.0003f);

        double Level(float solo) {
            using var block = Create(
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Control1, solo)
            );
            var rendered = BlockRender.Mono(block, tone, 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), 440, Rate));
        }

        var levels = new[] { 0f, 5f, 10f }.Select(Level).ToArray();
        output.WriteLine($"solo boost 0 / 5 / 10: {string.Join(" / ", levels.Select(static l => $"{l:0.0}"))} dB");
        Assert.True(levels[1] > levels[0] + 2);
        Assert.True(levels[2] > levels[1] + 2);
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, converging.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(int setting) {
        using var block = Create([(CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f), ..Switches(setting)]);
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var control in Knobs) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(control, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"[{setting}]: control {control} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        var channel = block.Channel;
        foreach (var newton in Solvers(channel.Stages).Append(channel.Power.Newton)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"[{setting}]: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"[{setting}]: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>
    ///     Every knob changes the sound in the given channel and mode, but the overdrive channel's own (Middle, Solo
    ///     Boost) on the clean channel, where they are out of circuit.
    /// </summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Every_knob_is_audible(int setting) {
        var tones = Signals.Sine(110, Rate, 24_000, 0.02f)
            .Zip(Signals.Sine(2_500, Rate, 24_000, 0.02f), static (a, b) => a + b)
            .ToArray();

        double[] Bands(int control, float value) {
            using var block = Create(
                [(CircuitAmpBlock.Gain, 4f), (CircuitAmpBlock.Master, 4f), ..Switches(setting), (control, value)]
            );
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return [
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 110, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 2_500, Rate))
            ];
        }

        var clean = (setting & 1) != 0;
        foreach (var control in Knobs) {
            if (clean && control is CircuitAmpBlock.Middle or CircuitAmpBlock.Control1) {
                continue;
            }

            var (a, b) = (Bands(control, 0f), Bands(control, 10f));
            var change = Math.Max(Math.Abs(a[0] - b[0]), Math.Abs(a[1] - b[1]));
            output.WriteLine(
                $"[{setting}] control {control}: 110 Hz {b[0] - a[0]:+0.0;-0.0} dB, 2.5 kHz {b[1] - a[1]:+0.0;-0.0} dB"
            );
            Assert.True(change > 0.2, $"[{setting}]: control {control} moves the sound by only {change:0.00} dB");
        }
    }

    /// <summary>Below clipping, Gain and Master raise the level monotonically, in every channel and mode.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Gain_and_master_raise_the_level_monotonically(int setting) {
        double Level(int knob, float value) {
            var gain = knob == CircuitAmpBlock.Gain ? value : 3f;
            var master = knob == CircuitAmpBlock.Master ? value : 3f;
            using var block = Create(
                [(CircuitAmpBlock.Gain, gain), (CircuitAmpBlock.Master, master), ..Switches(setting)]
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Level(knob, v)).ToArray();
            output.WriteLine($"[{setting}] knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Length; i++) {
                if (levels[i - 1] > -100) {
                    Assert.True(levels[i] > levels[i - 1] - 0.1, $"[{setting}]: knob {knob} not monotonic at {i}");
                }
            }

            Assert.True(levels[^1] - levels[0] > 10, $"knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    static IEnumerable<NewtonSolver> Solvers(IEnumerable<AmpStage> stages) =>
        stages.SelectMany(static s => s is ChannelSwitchStage c ? Solvers(c.Active) : s.Newton is { } n ? [n] : []);

    static (int, float)[] Switches(int setting) => [
        (CircuitAmpBlock.Switch1, setting & 1),
        (CircuitAmpBlock.Switch2, (setting >> 1) & 1),
        (CircuitAmpBlock.Switch3, (setting >> 2) & 1),
        (CircuitAmpBlock.Bright, (setting >> 3) & 1)
    ];

    static CircuitAmpBlock Create(params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Type);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }

    static string Label(string id) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[Type] ?? string.Empty : parameter.Name;
    }
}
