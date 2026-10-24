using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Mesa, Bogner, Diezel and Engl models (the second high-gain stream) on the Circuit Amp block: their panels,
///     knob sweeps at full drive in every channel without instability, gain and master raising the level monotonically,
///     every control audible, and the levels the channels play at.
/// </summary>
public class MesaBognerEnglTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>Each model with the switch settings that select each of its channels or modes.</summary>
    public static TheoryData<string, float, float, float> Channels => new() {
        { "Lone Star", 0, 0, 0 },
        { "Lone Star", 1, 0, 0 },
        { "Lone Star", 1, 1, 0 },
        { "Mark V", 0, 0, 0 },
        { "Mark V", 1, 0, 1 },
        { "Mark V", 0, 1, 0 },
        { "Rect-o-Verb", 0, 0, 0 },
        { "Triple Rectifier", 1, 1, 1 },
        { "Powerball", 0, 0, 0 },
        { "Powerball", 1, 1, 0 },
        { "Savage 120", 0, 0, 0 },
        { "Savage 120", 1, 1, 0 }
    };

    static readonly int[] Knobs = [
        CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
        CircuitAmpBlock.Presence, CircuitAmpBlock.Depth, CircuitAmpBlock.Master, CircuitAmpBlock.Control1,
        CircuitAmpBlock.Control2
    ];

    /// <summary>
    ///     A signal small enough to stay below clipping at the knob settings these tests use: the Engl cascades have
    ///     four fully bypassed stages before the stack.
    /// </summary>
    static float SmallSignal(string type) => type is "Powerball" or "Savage 120" ? 1e-6f : 1e-4f;

    /// <summary>Whether a control plays in this channel (some only work in one mode).</summary>
    static bool Plays(string type, int knob, float s1, float s2) =>
        (type, knob) switch {
            ("Lone Star", CircuitAmpBlock.Control1) => s2 > 0,
            ("Powerball", CircuitAmpBlock.Control1) => s1 > 0,
            ("Powerball", CircuitAmpBlock.Middle) => s1 == 0,
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

    [Fact]
    public void Panels_name_the_real_controls() {
        Assert.Equal(["Channel 2", "Drive Mode", "Tube Rectifier"], new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, "Lone Star")));
        Assert.Equal("Drive", Label("control1", "Lone Star"));
        Assert.Equal("Presence", Label("control2", "Lone Star"));
        Assert.Equal("Thick", Label("bright", "Lone Star"));
        Assert.Empty(Label("presence", "Lone Star")!);
    }

    /// <summary>Every knob swept end to end in each channel while a loud chord plays: finite, bounded, Newton converging.</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type, float s1, float s2, float s3) {
        using var block = Create(type, [..Switches(s1, s2, s3), (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f), (CircuitAmpBlock.Control1, 10f)]);
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
            using var block = Create(type, [..Switches(s1, s2, s3), (CircuitAmpBlock.Gain, gain), (CircuitAmpBlock.Master, master)]);
            return Level(block, 1_000, SmallSignal(type));
        }

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => At(knob, v)).ToArray();
            output.WriteLine($"{type} {s1}{s2}{s3} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Length; i++) {
                Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
            }

            Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    /// <summary>Every knob on the model's panel changes the sound in each channel (small signal, 80 Hz to 6 kHz).</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Every_panel_knob_is_audible(string type, float s1, float s2, float s3) {
        var design = AmpDesign.All[TypeOf(type)];
        var ids = new Dictionary<int, string> {
            [CircuitAmpBlock.Gain] = "gain", [CircuitAmpBlock.Bass] = "bass", [CircuitAmpBlock.Middle] = "middle",
            [CircuitAmpBlock.Treble] = "treble", [CircuitAmpBlock.Presence] = "presence", [CircuitAmpBlock.Depth] = "depth",
            [CircuitAmpBlock.Master] = "master", [CircuitAmpBlock.Control1] = "control1", [CircuitAmpBlock.Control2] = "control2"
        };
        foreach (var (knob, id) in ids) {
            if (design.Panel.Label(id, id) is null) {
                continue;
            }

            if (!Plays(type, knob, s1, s2)) {
                continue;
            }

            var change = 0.0;
            foreach (var frequency in new[] { 80.0, 200.0, 3_000.0, 6_000.0 }) {
                double At(float value) {
                    using var block = Create(type, [..Switches(s1, s2, s3), (CircuitAmpBlock.Gain, 2f), (CircuitAmpBlock.Master, 3f), (knob, value)]);
                    return Level(block, frequency, 3 * SmallSignal(type));
                }

                change = Math.Max(change, Math.Abs(At(10f) - At(0f)));
            }

            output.WriteLine($"{type} {s1}{s2}{s3} {id}: {change:0.0} dB");
            Assert.True(change > 1, $"{type}: {id} changes the sound by only {change:0.00} dB");
        }
    }
}
