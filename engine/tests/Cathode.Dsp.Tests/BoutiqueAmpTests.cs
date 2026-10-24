using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The boutique front ends (Matchless Chieftain, Bad Cat Hot Cat 30, the 1997 Dumble ODS trace, the Trainwreck
///     Express redraw) against the reference MNA simulator as whole chains, with no Thévenin cut: the Hot Cat's
///     zero-bias first stage (its cathode grounded) and gain network, the ODS's plate-driven skyline stack into V1A, the
///     Chieftain's paralleled stages and bridged-T middle, the Express's plate-driven stack and cold V3.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, int, bool, double, double> BoutiqueChains =>
        new() {
            // Model, stages, overdrive or second channel, amplitude in volts, limit in dB.
            {
                "Chieftain",
                2,
                false,
                0.01,
                -45
            },
            {
                "Chieftain",
                2,
                false,
                0.3,
                -45
            },
            {
                "Hot Cat 30",
                4,
                false,
                0.002,
                -30
            },
            {
                "Hot Cat 30",
                4,
                false,
                0.05,
                -28
            },
            {
                "ODS 97",
                2,
                false,
                0.05,
                -30
            },
            {
                "ODS 97",
                3,
                true,
                0.05,
                -20
            },
            {
                "Express",
                3,
                false,
                0.01,
                -30
            },
            {
                "Express",
                3,
                false,
                0.2,
                -15
            }
        };

    [Theory]
    [MemberData(nameof(BoutiqueChains))]
    public void Boutique_chains_match_the_reference(
        string type,
        int count,
        bool switched,
        double amplitude,
        double limitDb
    ) {
        var designs = BoutiqueAmpTests.Chain(AmpDesign.All.Single(a => a.Name == type), switched).Take(count).ToArray();
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false, Switch1: switched);

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
            var network = d switch {
                TriodeStageDesign triode => triode.ToNetwork(),
                NetworkStageDesign net => net,
                _ => throw new InvalidOperationException(d.Name)
            };
            node = AmpReferences.Append(circuit, network, controls, PreampSettings.Stock, node);
        }

        var errorDb = CompareInTime(
            $"{type} {string.Join(" > ", designs.Select(static d => d.Name))}",
            x => {
                foreach (var stage in stages) {
                    x = stage.Tick(x);
                }

                return x;
            },
            stages.Select(static s => s.Newton).LastOrDefault(static n => n is not null),
            circuit,
            node,
            amplitude
        );
        Assert.True(errorDb < limitDb, $"{type}: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The boutique and class-A models (this stream) on the Circuit Amp block: their panels, their stages' operating
///     points against the voltages measured on the drawings, knob sweeps at full drive without instability, gain and
///     master monotonic, the tone controls and the model-specific knobs doing what their circuits do.
/// </summary>
public class BoutiqueAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = ["Chieftain", "Hot Cat 30", "ODS 97", "Express"];

    public static TheoryData<string, bool> Channels =>
        new() {
            { "Chieftain", false },
            { "Hot Cat 30", false },
            { "Hot Cat 30", true },
            { "ODS 97", false },
            { "ODS 97", true },
            { "Express", false }
        };

    public static TheoryData<string, bool> ToneChannels =>
        new() {
            { "Chieftain", false },
            { "Hot Cat 30", false },
            { "ODS 97", false },
            { "ODS 97", true },
            { "Express", false }
        };

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Volume", Label("gain", "Chieftain"));
        Assert.Equal("Master", Label("master", "Chieftain"));
        Assert.Equal("Cut", Label("cut", "Chieftain"));
        Assert.Empty(Label("presence", "Chieftain"));
        Assert.Empty(Label("switch1", "Chieftain"));

        Assert.Equal("Edge", Label("control1", "Hot Cat 30"));
        Assert.Equal("Level", Label("control2", "Hot Cat 30"));
        Assert.Equal("Clean Input", Label("switch1", "Hot Cat 30"));
        Assert.Equal("Solid State", Label("switch2", "Hot Cat 30"));
        Assert.Empty(Label("middle", "Hot Cat 30"));

        Assert.Equal("Overdrive", Label("switch1", "ODS 97"));
        Assert.Equal("Drive", Label("control1", "ODS 97"));
        Assert.Equal("OD Level", Label("control2", "ODS 97"));
        Assert.Equal("Presence", Label("presence", "ODS 97"));
        Assert.Empty(Label("cut", "ODS 97"));

        Assert.Equal("Clarity", Label("switch1", "Express"));
        Assert.Equal("Bright", Label("bright", "Express"));
        Assert.Empty(Label("master", "Express"));
        Assert.Empty(Label("depth", "Express"));
    }

    /// <summary>
    ///     The stages' rest voltages against the DC voltages measured on the drawings: the Hot Cat's plates (99, 145,
    ///     225, 193 V), the ODS's (V1B 209.6, V1A 206.0, V2A 227.5 V) and the Chieftain's V1 and V2 (220 / 219 V).
    /// </summary>
    [Fact]
    public void Operating_points_agree_with_the_measured_voltages() {
        (string Type, bool On, string Stage, int Node, double Volts)[] points = [
            // The zero-bias V1a sits on the triode's contact potential and grid current: the loosest (88 V).
            ("Hot Cat 30", false, "V1a", 0, 99),
            // V2b's stage ends on V2a's grid: its plate is node 5.
            ("Hot Cat 30", false, "V2b", 5, 145),
            ("Hot Cat 30", false, "V2a", 0, 225),
            ("Hot Cat 30", false, "V3b", 0, 193),
            ("ODS 97", false, "V1B", 0, 209.6),
            ("ODS 97", false, "V1A", 0, 206),
            ("ODS 97", true, "V2A", 0, 227.5),
            // The Chieftain's V2 ends on its volume: its plate is node 9.
            ("Chieftain", false, "V1", 0, 220),
            ("Chieftain", false, "V2", 9, 219)
        ];
        foreach (var (type, on, name, node, volts) in points) {
            // The ODS's V1A: the drawing's 150k plate resistor and its measured 206 V and 1.788 V disagree (they fit 220k
            // from 323 V); the model keeps the drawn 150k and sits at 232 V.
            var tolerance = name is "V1a" or "V1A" ? 0.15 : 0.1;
            var stages = Chain(AmpDesign.All.Single(a => a.Name == type), on)
                .Select(static d => d.Create(96_000))
                .ToArray();
            var (rest, resistance) = (0.0, 0.0);
            var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false, Switch1: on);
            AmpStage? found = null;
            for (var i = 0; i < stages.Length; i++) {
                stages[i].Update(controls);
                stages[i].SetSourceResistance(resistance);
                stages[i].Reset(rest);
                (rest, resistance) = (stages[i].RestOutput, stages[i].OutputResistance);
                found = Chain(AmpDesign.All.Single(a => a.Name == type), on).ElementAt(i).Name == name
                    ? stages[i]
                    : found;
            }

            Assert.NotNull(found);
            var model = node == 0
                ? found.RestOutput
                : found switch {
                    NetworkStage<FastTriode> n => n.RestVoltage(node),
                    NetworkStage<Devices<FastTriode, FastTriode>> n => n.RestVoltage(node),
                    _ => double.NaN
                };
            output.WriteLine($"{type} {name}: {model:0.0} V, drawing {volts:0.0} V");
            Assert.InRange(model, (1 - tolerance) * volts, (1 + tolerance) * volts);
        }
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, and Newton converging.</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type, bool channel) {
        using var block = Create(
            type,
            (CircuitAmpBlock.Gain, 10f),
            (CircuitAmpBlock.Master, 10f),
            (CircuitAmpBlock.Control1, 10f),
            (CircuitAmpBlock.Control2, 10f),
            (CircuitAmpBlock.Switch1, channel ? 1f : 0f)
        );
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Cut, CircuitAmpBlock.Master, CircuitAmpBlock.Control1,
            CircuitAmpBlock.Control2
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

        foreach (var toggle in new[] { CircuitAmpBlock.Switch2, CircuitAmpBlock.Bright }) {
            block.Params.SetTarget(toggle, 1f);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type}: switch {toggle}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var solvers = block.Channel.Stages
            .Where(static s => s.Newton is not null)
            .Select(static s => s.Newton!)
            .Append(block.Channel.Power.Newton);
        foreach (var newton in solvers) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type}: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"{type} channel {(channel ? 2 : 1)}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>Below clipping, the volume (and, where the amp has one, the master) raises the level monotonically.</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Gain_and_master_raise_the_level_monotonically(string type, bool channel) {
        var knobs = new List<int> { CircuitAmpBlock.Gain };
        // The ODS's overdrive returns in the master's place (see the model), the Hot Cat's master is on its Gain
        // channel only, and the Express has no master.
        if (Label("master", type) is { Length: > 0 } && !(type is "ODS 97" or "Hot Cat 30" && channel)) {
            knobs.Add(CircuitAmpBlock.Master);
        }

        foreach (var knob in knobs) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }
                .Select(v => Level(
                        type,
                        1_000,
                        1e-4f,
                        (CircuitAmpBlock.Gain, knob == CircuitAmpBlock.Gain ? v : 3f),
                        (CircuitAmpBlock.Master, knob == CircuitAmpBlock.Master ? v : 5f),
                        (CircuitAmpBlock.Switch1, channel ? 1f : 0f)
                    )
                )
                .ToArray();
            output.WriteLine(
                $"{type} ch{(channel ? 2 : 1)} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
            );
            for (var i = 1; i < levels.Length; i++) {
                Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
            }

            // The Hot Cat's master, as drawn, is a 100k rheostat behind 220k across the treble pot's wiper: about 1 dB.
            var span = type == "Hot Cat 30" && knob == CircuitAmpBlock.Master ? 0.5 : 10;
            Assert.True(
                levels[^1] - levels[0] > span,
                $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB"
            );
        }
    }

    /// <summary>
    ///     Treble up lifts 4 kHz against 400 Hz, bass up lifts 80 Hz against 1 kHz, on each model's tone controls (the
    ///     Hot Cat's Clean input has none).
    /// </summary>
    [Theory]
    [MemberData(nameof(ToneChannels))]
    public void Treble_and_bass_tilt_the_response(string type, bool channel) {
        double Tilt(int knob, float value, double high, double low) {
            var on = (CircuitAmpBlock.Switch1, channel ? 1f : 0f);
            var gain = (CircuitAmpBlock.Gain, 3f);
            return Level(type, high, 3e-4f, gain, on, (knob, value)) - Level(type, low, 3e-4f, gain, on, (knob, value));
        }

        var treble = new[] { 0f, 5f, 10f }.Select(v => Tilt(CircuitAmpBlock.Treble, v, 4_000, 400)).ToArray();
        var bass = new[] { 0f, 5f, 10f }.Select(v => Tilt(CircuitAmpBlock.Bass, v, 80, 1_000)).ToArray();
        output.WriteLine(
            $"{type} ch{(channel ? 2 : 1)}: 4 kHz over 400 Hz {string.Join(" ", treble.Select(static l => $"{l:0.0}"))}; "
            + $"80 Hz over 1 kHz {string.Join(" ", bass.Select(static l => $"{l:0.0}"))}"
        );
        Assert.True(treble[1] > treble[0] - 0.1 && treble[2] > treble[1] - 0.1 && treble[2] - treble[0] > 3);
        Assert.True(bass[1] > bass[0] - 0.1 && bass[2] > bass[1] - 0.1 && bass[2] - bass[0] > 3);
    }

    /// <summary>
    ///     The model-specific knobs: the Hot Cat's Edge brings up the top and its Level raises the level; the ODS's Drive and
    ///     OD Level raise the overdrive's level; the Chieftain's middle fills in its notch; the cut takes off the top.
    /// </summary>
    [Fact]
    public void Model_specific_controls_do_what_their_circuits_do() {
        double[] Sweep(string type, int knob, double frequency, params (int Index, float Value)[] settings) {
            var levels = new[] { 0f, 5f, 10f }
                .Select(v => Level(type, frequency, 1e-4f, [..settings, (knob, v)]))
                .ToArray();
            output.WriteLine(
                $"{type} knob {knob} at {frequency} Hz: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
            );
            return levels;
        }

        static void Rising(double[] levels, double span) =>
            Assert.True(levels[1] > levels[0] - 0.1 && levels[2] > levels[1] - 0.1 && levels[2] - levels[0] > span);

        var edgeHigh = Sweep("Hot Cat 30", CircuitAmpBlock.Control1, 5_000);
        var edgeLow = Sweep("Hot Cat 30", CircuitAmpBlock.Control1, 300);
        // The Edge's 10 nF shunts everything above about 100 Hz when its rheostat is short: the top comes up most.
        Rising(edgeHigh, 10);
        Assert.True(edgeHigh[2] - edgeHigh[0] > edgeLow[2] - edgeLow[0] + 6);
        Rising(Sweep("Hot Cat 30", CircuitAmpBlock.Control2, 1_000), 10);
        Rising(Sweep("ODS 97", CircuitAmpBlock.Control1, 1_000, (CircuitAmpBlock.Switch1, 1f)), 10);
        Rising(Sweep("ODS 97", CircuitAmpBlock.Control2, 1_000, (CircuitAmpBlock.Switch1, 1f)), 10);
        var middle = Sweep("Chieftain", CircuitAmpBlock.Middle, 600);
        var middleLow = Sweep("Chieftain", CircuitAmpBlock.Middle, 80);
        Rising(middle.Zip(middleLow, static (m, l) => m - l).ToArray(), 3);
        foreach (var type in new[] { "Chieftain", "Hot Cat 30" }) {
            var cut = Sweep(type, CircuitAmpBlock.Cut, 5_000);
            Assert.True(cut[0] - cut[2] > 6, $"{type}: cut takes {cut[0] - cut[2]:0.0} dB");
        }
    }

    /// <summary>The Hot Cat's Solid State switch lifts B+ under load, as the silicon bridge in place of the GZ34 does.</summary>
    [Fact]
    public void Hot_cat_solid_state_switch_raises_the_supply() {
        double Supply(bool silicon) {
            using var block = Create(
                "Hot Cat 30",
                (CircuitAmpBlock.Gain, 8f),
                (CircuitAmpBlock.Switch2, silicon ? 1f : 0f)
            );
            BlockRender.Mono(block, Signals.Sine(110, Rate, 24_000, 0.5f), 64);
            return block.Channel.Power.Supply;
        }

        var (tube, silicon) = (Supply(false), Supply(true));
        output.WriteLine($"B+ under drive: GZ34 {tube:0.0} V, silicon {silicon:0.0} V");
        Assert.True(silicon > tube + 5);
    }

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

    /// <summary>The level (dB) of a quiet sine at <paramref name="frequency" /> with the given settings.</summary>
    static double Level(string type, double frequency, float amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(type, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 19_200, amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(9_600)));
    }

    /// <summary>The preamp's stage designs in signal order, with the channel switch's chain for <paramref name="on" />.</summary>
    internal static IEnumerable<AmpStageDesign> Chain(AmpDesign design, bool on) =>
        design.Preamp.SelectMany(s => s is ChannelSwitchStageDesign channel ? on ? channel.On : channel.Off : [s]);
}
