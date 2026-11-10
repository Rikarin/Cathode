using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The second high-gain stream's front ends against the reference MNA simulator as whole chains: the first four
///     preamp stages joined in one circuit with no Thévenin cut (see the high-gain chains for what such limits record).
/// </summary>
/// <remarks>
///     Measured, with a few dB of margin: these cascades already clip at 2 mV, so even the small-signal rows test the
///     cuts under grid current. The Savage's four network stages agree to -87 dB at 2 mV; driven hard, its V2B (330k
///     plate, unbypassed 3.3k) into V3B's grid current is the cuts' known weak spot (-8 dB), as the Rect-o-Verb's cold
///     clipper with a plate bypass cap is (-13 dB). The Savage's Smooth path, through its stack and V3A (five stages),
///     agrees to -42 dB at 2 mV and -32 dB at 50 mV: its pad keeps V3B's grid current small.
/// </remarks>
public partial class AmpStageTests {
    /// <summary>
    ///     Model, its other channel or voice (Switch 1 on the Rect-o-Verb, Switch 3 on the Engls), the number of stages
    ///     from the input, the drive in volts, the limit in dB.
    /// </summary>
    public static TheoryData<string, bool, int, double, double> MesaEnglChains =>
        new() {
            { "Mark V", false, 4, 0.002, -16 },
            { "Mark V", false, 4, 0.05, -16 },
            { "Rect-o-Verb", false, 4, 0.002, -15 },
            { "Rect-o-Verb", false, 4, 0.05, -9 },
            { "Rect-o-Verb", true, 3, 0.002, -45 },
            { "Rect-o-Verb", true, 3, 0.05, -45 },
            { "Powerball", false, 4, 0.0005, -25 },
            { "Powerball", false, 4, 0.05, -10 },
            { "Powerball", true, 4, 0.002, -38 },
            { "Powerball", true, 4, 0.05, -15 },
            { "Savage 120", false, 4, 0.002, -80 },
            { "Savage 120", false, 4, 0.05, -5 },
            { "Savage 120", true, 5, 0.002, -35 },
            { "Savage 120", true, 5, 0.05, -25 }
        };

    /// <summary>
    ///     The first <paramref name="count" /> stages of the path Switch 3 selects (a channel switch's stages are those
    ///     of the channel that plays).
    /// </summary>
    [Theory]
    [MemberData(nameof(MesaEnglChains))]
    public void Mesa_and_Engl_chains_match_the_reference(
        string type,
        bool other,
        int count,
        double amplitude,
        double limitDb
    ) {
        // The model's other channel or voice: Switch 1 on the Rect-o-Verb, Switch 3 on the Engls.
        var onSwitch1 = type == "Rect-o-Verb";
        var controls = new AmpControls(
            0.5,
            0.5,
            0.5,
            0.5,
            1,
            false,
            Switch1: other && onSwitch1,
            Switch3: other && !onSwitch1
        );
        var designs = AmpDesign.All.Single(a => a.Name == type)
            .Preamp
            .SelectMany(s => s is ChannelSwitchStageDesign c ? controls.IsOn(c.Switch) ? c.On : c.Off : [s])
            .Take(count)
            .ToArray();

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
            stages[^1].Newton,
            circuit,
            node,
            amplitude
        );
        Assert.True(errorDb < limitDb, $"{type}: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The Mesa, Bogner, Diezel and Engl models (the second high-gain stream) on the Circuit Amp block: their panels,
///     knob sweeps at full drive in every channel without instability, gain and master raising the level monotonically,
///     every control audible, and the levels the channels play at.
/// </summary>
public class MesaBognerEnglTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly int[] Knobs = [
        CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
        CircuitAmpBlock.Presence, CircuitAmpBlock.Depth, CircuitAmpBlock.Master, CircuitAmpBlock.Control1,
        CircuitAmpBlock.Control2
    ];

    /// <summary>Each model with the switch settings that select each of its channels or modes.</summary>
    public static TheoryData<string, float, float, float> Channels =>
        new() {
            { "Lone Star", 0, 0, 0 },
            { "Lone Star", 1, 0, 0 },
            { "Lone Star", 1, 1, 0 },
            { "Mark V", 0, 0, 0 },
            { "Mark V", 1, 0, 1 },
            { "Mark V", 0, 1, 0 },
            { "Rect-o-Verb", 0, 0, 0 },
            { "Rect-o-Verb", 1, 0, 0 },
            { "Rect-o-Verb", 1, 1, 0 },
            { "Triple Rectifier", 1, 1, 1 },
            { "Powerball", 0, 0, 0 },
            { "Powerball", 1, 1, 0 },
            { "Powerball", 0, 0, 1 },
            { "Powerball", 1, 1, 1 },
            { "Savage 120", 0, 0, 0 },
            { "Savage 120", 1, 1, 0 },
            { "Savage 120", 0, 0, 1 },
            { "Savage 120", 1, 1, 1 }
        };

    /// <summary>
    ///     The Rect-o-Verb's V3A and follower are the Dual Rectifier's netlists with its own drawing's wiring: the
    ///     same V3A at 415 V; the middle a rheostat (one track element) where the Dual Rectifier's is a pot with the
    ///     cap on its wiper (two); a 250k presence rheostat; and 220k ahead of the lead master, which the Dual
    ///     Rectifier does not have.
    /// </summary>
    [Fact]
    public void Rect_o_Verb_shares_the_Dual_Rectifiers_stages_with_its_own_stack_wiring() {
        static IReadOnlyList<StageElement> Stage(string type, string name) =>
            ((NetworkStageDesign)AmpDesign.All.Single(a => a.Name == type)
                .Preamp.SelectMany(static s => s is ChannelSwitchStageDesign c ? [..c.Off, ..c.On] : new[] { s })
                .Single(s => s.Name == name))
            .Netlist.Elements;

        static int Tracks(IReadOnlyList<StageElement> stage, PotRole role) =>
            stage.Count(e => e.Pot.Ohms > 0 && e.Role == role);

        var (verb, dual) = (Stage("Rect-o-Verb", "V3B"), Stage("Dual Rectifier", "V3B"));
        Assert.Equal(1, Tracks(verb, PotRole.Middle));
        Assert.Equal(2, Tracks(dual, PotRole.Middle));
        Assert.Equal(250e3, verb.Single(static e => e.Pot.Ohms > 0 && e.Role == PotRole.Control2).Pot.Ohms);
        Assert.Contains(verb, static e => e is { Kind: ElementKind.Capacitor, Value: 500e-12 });
        Assert.Contains(verb, static e => e is { Kind: ElementKind.Resistor, Value: 220e3 } && e.Pot.Ohms == 0);
        Assert.DoesNotContain(dual, static e => e is { Kind: ElementKind.Resistor, Value: 220e3 } && e.Pot.Ohms == 0);

        var (fourth, dualFourth) = (Stage("Rect-o-Verb", "V3A"), Stage("Dual Rectifier", "V3A"));
        Assert.Equal(dualFourth.Count, fourth.Count);
        Assert.Equal(415, fourth.Single(static e => e.Kind == ElementKind.Supply).Value);
        foreach (var (mine, theirs) in fourth.Zip(dualFourth)) {
            Assert.Equal(theirs with { Value = mine.Value }, mine);
            Assert.True(mine.Kind == ElementKind.Supply || mine.Value == theirs.Value);
        }
    }

    [Fact]
    public void Panels_name_the_real_controls() {

        Assert.Equal(
            ["Channel 2", "Drive Mode", "Tube Rectifier"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "Lone Star"))
        );
        Assert.Equal("Drive", Label("control1", "Lone Star"));
        Assert.Equal("Presence", Label("control2", "Lone Star"));
        Assert.Equal("Thick", Label("bright", "Lone Star"));
        Assert.Empty(Label("presence", "Lone Star")!);
        Assert.Equal(
            ["IIC+ Mode", "Mark IV Mode", "Bright"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "Mark V"))
        );
        Assert.Equal("Output", Label("master", "Mark V"));
        Assert.Equal("80", Label("eq1", "Mark V"));
        Assert.Equal("Presence", Label("control2", "Rect-o-Verb"));
        Assert.Equal(
            ["Rhythm", "Pushed"],
            new[] { "switch1", "switch2" }.Select(static id => Label(id, "Rect-o-Verb"))
        );
        Assert.Equal(
            ["Modern", "Tube Rectifier", "Spongy"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "Triple Rectifier"))
        );
        Assert.Equal(
            ["Focused", "Gain Hi", "Channel 1"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "Powerball"))
        );
        Assert.Equal("Bright", Label("bright", "Powerball"));
        Assert.Equal("Channel Volume", Label("control2", "Powerball"));
        Assert.Equal("Mid Open", Label("middle", "Powerball"));
        Assert.Equal("Mid Focused", Label("control1", "Powerball"));
        Assert.Equal(
            ["Contour", "Lead Boost", "Smooth"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "Savage 120"))
        );
        Assert.Equal("Lead", Label("control1", "Savage 120"));
        Assert.Equal(6, AmpDesign.All.Single(static a => a.Name == "Triple Rectifier").Power.TubesPerSide * 2);
    }

    /// <summary>
    ///     The Lone Star's rectifier switch puts a 5U4G in place of silicon: B+ sags further under drive.
    /// </summary>
    [Fact]
    public void Lone_Star_tube_rectifier_lowers_the_supply() {
        static double Supply(bool tube) {
            using var block = Create(
                "Lone Star",
                (CircuitAmpBlock.Gain, 8f),
                (CircuitAmpBlock.Master, 10f),
                (CircuitAmpBlock.Switch3, tube ? 1f : 0f)
            );
            BlockRender.Mono(block, Signals.Sine(110, Rate, 24_000, 0.5f), 64);
            return block.Channel.Power.Supply;
        }

        var (silicon, tube) = (Supply(false), Supply(true));
        output.WriteLine($"B+ under drive: silicon {silicon:0.0} V, 5U4G {tube:0.0} V");
        Assert.True(tube < silicon - 10);
    }

    /// <summary>
    ///     The Rect-o-Verb's rhythm channel (Switch 1) is the clean one: at the same knobs its three stages amplify a
    ///     small signal far less than the lead channel's five. Pushed (Switch 2) bridges the 3.3M ahead of its last
    ///     stage, so it is louder, and it does nothing on the lead channel.
    /// </summary>
    [Fact]
    public void Rect_o_Verb_rhythm_channel_is_cleaner_and_Pushed_drives_it() {
        double At(float rhythm, float pushed) {
            using var block = Create(
                "Rect-o-Verb",
                [..Switches(rhythm, pushed, 0), (CircuitAmpBlock.Gain, 5f), (CircuitAmpBlock.Master, 3f)]
            );
            return Level(block, 1_000, 1e-5f);
        }

        var (lead, leadPushed, clean, pushed) = (At(0, 0), At(0, 1), At(1, 0), At(1, 1));
        output.WriteLine(
            $"1 kHz: lead {lead:0.0} dB (Pushed {leadPushed:0.0}), rhythm {clean:0.0} dB, Pushed {pushed:0.0} dB"
        );
        Assert.Equal(lead, leadPushed);
        Assert.True(clean < lead - 15);
        Assert.True(pushed > clean + 6);
    }

    /// <summary>
    ///     The Powerball's channel 1 (Switch 3) runs three of the lead's four stages behind a divider: far less gain
    ///     than the lead, Crunch (Switch 2) above Clean by its hotter divider, and a Bright switch that lifts the highs
    ///     at a low gain setting and does nothing in the lead.
    /// </summary>
    [Fact]
    public void Powerball_channel_one_is_the_clean_and_crunch_of_three_stages() {
        static double At(float channel, float crunch, float bright, double frequency) {
            using var block = Create(
                "Powerball",
                (CircuitAmpBlock.Switch3, channel),
                (CircuitAmpBlock.Switch2, crunch),
                (CircuitAmpBlock.Bright, bright),
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Control2, 10f),
                (CircuitAmpBlock.Master, 3f)
            );
            return Level(block, frequency, 1e-6f);
        }

        // With the channel volume full: at noon the Crunch volume's audio taper puts Crunch below Clean.
        var (lead, clean, crunch) = (At(0, 0, 0, 1_000), At(1, 0, 0, 1_000), At(1, 1, 0, 1_000));
        var bright = At(1, 0, 1, 5_000) - At(1, 0, 0, 5_000);
        var leadBright = At(0, 0, 1, 5_000) - At(0, 0, 0, 5_000);
        output.WriteLine($"Powerball at 1 kHz: lead {lead:0.0} dB, clean {clean:0.0} dB, crunch {crunch:0.0} dB");
        output.WriteLine($"Bright at 5 kHz: channel 1 {bright:0.0} dB, lead {leadBright:0.00} dB");
        Assert.True(clean < lead - 20, $"clean {clean:0.0} dB against the lead's {lead:0.0} dB");
        Assert.InRange(crunch - clean, 1, 8);
        Assert.True(bright > 3, $"Bright lifts 5 kHz by {bright:0.0} dB");
        Assert.Equal(0, leadBright, 6);
    }

    /// <summary>
    ///     The Savage's Smooth (Switch 3) pads V3B's grid and makes the level up after the stack in V3A: far less gain
    ///     below clipping (measured -27.5 dB at gain 3), within a few dB driven hard (-8.6 dB), and, as Engl's manual
    ///     says of Smooth, tone knobs that do noticeably less, because V3A clips what the stack shaped (the treble
    ///     knob moves a driven note's highs by 6.9 dB in Rough, 3.7 dB in Smooth).
    /// </summary>
    [Fact]
    public void Savage_Smooth_pads_the_cascade_and_clips_after_the_stack() {
        static CircuitAmpBlock Amp(bool smooth, float gain, float treble = 5f) =>
            Create(
                "Savage 120",
                (CircuitAmpBlock.Switch3, smooth ? 1f : 0f),
                (CircuitAmpBlock.Gain, gain),
                (CircuitAmpBlock.Treble, treble),
                (CircuitAmpBlock.Master, 3f)
            );

        static double Small(bool smooth) {
            using var block = Amp(smooth, 3f);
            return Level(block, 1_000, 1e-6f);
        }

        static double Driven(bool smooth) {
            using var block = Amp(smooth, 8f);
            return Level(block, 220, 0.1f);
        }

        // The treble knob's reach on a driven note's upper harmonics (2.2 to 6.6 kHz against the fundamental).
        static double TrebleReach(bool smooth) {
            double Tilt(float treble) {
                using var block = Amp(smooth, 8f, treble);
                var rendered = BlockRender.Mono(block, Signals.Sine(220, Rate, 48_000, 0.1f), 64).AsSpan(24_000);
                var high = 0.0;
                foreach (var harmonic in new[] { 11, 15, 21, 25, 29 }) {
                    high += Math.Pow(Analysis.MagnitudeAt(rendered, 220.0 * harmonic, Rate), 2);
                }

                return Analysis.ToDb(Math.Sqrt(high)) - Analysis.ToDb(Analysis.MagnitudeAt(rendered, 220, Rate));
            }

            return Math.Abs(Tilt(10f) - Tilt(0f));
        }

        var (small, driven) = (Small(true) - Small(false), Driven(true) - Driven(false));
        var (rough, smoothed) = (TrebleReach(false), TrebleReach(true));
        output.WriteLine($"Smooth against Rough: {small:0.0} dB below clipping, {driven:0.0} dB driven");
        output.WriteLine($"Treble 0 to 10 on a driven note's highs: Rough {rough:0.0} dB, Smooth {smoothed:0.0} dB");
        Assert.InRange(small, -60, -20);
        Assert.InRange(driven, -12, 3);
        Assert.True(smoothed < rough - 2,$"Smooth's treble reaches {smoothed:0.0} dB, Rough's {rough:0.0} dB");
    }

    /// <summary>
    ///     Every knob swept end to end in each channel while a loud chord plays: finite, bounded, Newton converging.
    /// </summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type, float s1, float s2, float s3) {
        using var block = Create(
            type,
            [
                ..Switches(s1, s2, s3), (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f),
                (CircuitAmpBlock.Control1, 10f)
            ]
        );
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var knob in Knobs) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(knob, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: knob {knob} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        block.Params.SetTarget(CircuitAmpBlock.Bright, 1f);
        var bright = BlockRender.Mono(block, chord, 64);
        Assert.True(Analysis.AllFinite(bright), $"{type}: bright");
        peak = Math.Max(peak, Analysis.Peak(bright));

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

        output.WriteLine($"{type} {s1}{s2}{s3}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>Below clipping, the gain and master knobs raise the level monotonically, as their pots do.</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Gain_and_master_raise_the_level_monotonically(string type, float s1, float s2, float s3) {
        double At(int knob, float value) {
            var gain = knob == CircuitAmpBlock.Gain ? value : 3f;
            var master = knob == CircuitAmpBlock.Master ? value : 3f;
            using var block = Create(
                type,
                [..Switches(s1, s2, s3), (CircuitAmpBlock.Gain, gain), (CircuitAmpBlock.Master, master)]
            );
            return Level(block, 1_000, SmallSignal(type));
        }

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => At(knob, v)).ToArray();
            output.WriteLine(
                $"{type} {s1}{s2}{s3} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
            );
            for (var i = 1; i < levels.Length; i++) {
                Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
            }

            Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    /// <summary>
    ///     Every knob on the model's panel changes the sound in each channel (small signal, 80 Hz to 6 kHz).
    /// </summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Every_panel_knob_is_audible(string type, float s1, float s2, float s3) {
        var design = AmpDesign.All[TypeOf(type)];
        var ids = new Dictionary<int, string> {
            [CircuitAmpBlock.Gain] = "gain",
            [CircuitAmpBlock.Bass] = "bass",
            [CircuitAmpBlock.Middle] = "middle",
            [CircuitAmpBlock.Treble] = "treble",
            [CircuitAmpBlock.Presence] = "presence",
            [CircuitAmpBlock.Depth] = "depth",
            [CircuitAmpBlock.Master] = "master",
            [CircuitAmpBlock.Control1] = "control1",
            [CircuitAmpBlock.Control2] = "control2"
        };
        foreach (var (knob, id) in ids) {
            if (design.Panel.Label(id, id) is null) {
                continue;
            }

            if (!Plays(type, knob, s1, s2, s3)) {
                continue;
            }

            var change = 0.0;
            foreach (var frequency in new[] { 80.0, 200.0, 3_000.0, 6_000.0 }) {
                double At(float value) {
                    using var block = Create(
                        type,
                        [
                            ..Switches(s1, s2, s3), (CircuitAmpBlock.Gain, 2f), (CircuitAmpBlock.Master, 3f),
                            (knob, value)
                        ]
                    );
                    return Level(block, frequency, 3 * SmallSignal(type));
                }

                change = Math.Max(change, Math.Abs(At(10f) - At(0f)));
            }

            output.WriteLine($"{type} {s1}{s2}{s3} {id}: {change:0.0} dB");
            Assert.True(change > 1, $"{type}: {id} changes the sound by only {change:0.00} dB");
        }
    }

    /// <summary>
    ///     A signal small enough to stay below clipping at the knob settings these tests use: the Engl cascades have
    ///     four fully bypassed stages before the stack.
    /// </summary>
    static float SmallSignal(string type) => type is "Powerball" or "Savage 120" ? 1e-6f : 1e-4f;

    /// <summary>Whether a control plays in this channel (some only work in one mode).</summary>
    static bool Plays(string type, int knob, float s1, float s2, float s3) =>
        (type, knob) switch {
            ("Lone Star", CircuitAmpBlock.Control1) => s2 > 0,
            // The Powerball's lead has a middle pot for each Style; its channel 1 (Switch 3) has one.
            ("Powerball", CircuitAmpBlock.Control1) => s1 > 0 && s3 == 0,
            ("Powerball", CircuitAmpBlock.Middle) => s1 == 0 || s3 > 0,
            _ => true
        };

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

    static (int, float)[] Switches(float s1, float s2, float s3) =>
        [(CircuitAmpBlock.Switch1, s1), (CircuitAmpBlock.Switch2, s2), (CircuitAmpBlock.Switch3, s3)];

    static string? Label(string id, string type) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] : parameter.Name;
    }

    static double Level(CircuitAmpBlock block, double frequency = 1_000, float amplitude = 1e-4f) {
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }
}
