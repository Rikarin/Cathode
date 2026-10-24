using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The modern US high-gain stream on the Circuit Amp block: the Carvin Legacy VL100 (lead and clean channels) and the
///     SLO-100's normal channel. Panels, knob sweeps at full drive on every channel, the knobs that must move the level
///     monotonically, and each model-specific switch and control doing what the circuit says.
/// </summary>
public class ModernHighGainAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>Each model with Switch 1 off and on (its two channels).</summary>
    public static TheoryData<string, bool> Channels =>
        new() { { "Legacy VL100", false }, { "Legacy VL100", true }, { "SLO-100", true } };

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

    static double Level(string type, double frequency, double amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(type, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, (float)amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Drive", Label("gain", "Legacy VL100"));
        Assert.Equal("Volume 1", Label("master", "Legacy VL100"));
        Assert.Equal("Presence", Label("control1", "Legacy VL100"));
        Assert.Equal("Volume 2", Label("control2", "Legacy VL100"));
        Assert.Equal("Clean Ch", Label("switch1", "Legacy VL100"));
        Assert.Equal("Clean Presence", Label("switch2", "Legacy VL100"));
        Assert.Empty(Label("presence", "Legacy VL100"));
        Assert.Empty(Label("depth", "Legacy VL100"));
        Assert.Empty(Label("bright", "Legacy VL100"));
        Assert.Equal("Normal Ch", Label("switch1", "SLO-100"));
        Assert.Equal("Crunch", Label("switch2", "SLO-100"));
        Assert.Equal("Bright", Label("bright", "SLO-100"));
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, and Newton converging.</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type, bool switched) {
        using var block = Create(
            type,
            (CircuitAmpBlock.Gain, 10f),
            (CircuitAmpBlock.Master, 10f),
            (CircuitAmpBlock.Control2, 10f),
            (CircuitAmpBlock.Switch1, switched ? 1f : 0f)
        );
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Depth, CircuitAmpBlock.Master, CircuitAmpBlock.Control1,
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

        foreach (var toggle in new[] { CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3, CircuitAmpBlock.Bright }) {
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

        output.WriteLine($"{type} switch 1 {switched}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>
    ///     Below clipping, each channel's volume knobs raise the level monotonically, as their pots do: the Legacy's
    ///     Drive and Volume 1 on the lead channel, Volume 2 on the clean; the SLO's normal volume (Gain) and Master.
    /// </summary>
    [Theory]
    [InlineData("Legacy VL100", false, CircuitAmpBlock.Gain)]
    [InlineData("Legacy VL100", false, CircuitAmpBlock.Master)]
    [InlineData("Legacy VL100", true, CircuitAmpBlock.Control2)]
    [InlineData("SLO-100", true, CircuitAmpBlock.Gain)]
    [InlineData("SLO-100", true, CircuitAmpBlock.Master)]
    public void Volumes_raise_the_level_monotonically(string type, bool switched, int knob) {
        var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }
            .Select(v => {
                    return Level(
                        type,
                        1_000,
                        1e-4,
                        (CircuitAmpBlock.Gain, Knob(CircuitAmpBlock.Gain)),
                        (CircuitAmpBlock.Master, Knob(CircuitAmpBlock.Master)),
                        (CircuitAmpBlock.Control2, Knob(CircuitAmpBlock.Control2)),
                        (CircuitAmpBlock.Switch1, switched ? 1f : 0f)
                    );
                    float Knob(int index) => index == knob ? v : 3f;
                }
            )
            .ToArray();
        output.WriteLine(
            $"{type} switch 1 {switched} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
        );
        for (var i = 1; i < levels.Length; i++) {
            Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
        }

        Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
    }

    /// <summary>
    ///     The Legacy's clean Presence switch puts 330 pF across a 470k: the schematic notes 9 dB at 10 kHz; at 1 kHz it
    ///     barely moves.
    /// </summary>
    [Fact]
    public void Legacy_clean_presence_switch_lifts_the_top() {
        double Lift(double frequency) {
            (int, float)[] clean = [(CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Control2, 3f)];
            return Level("Legacy VL100", frequency, 1e-3, [..clean, (CircuitAmpBlock.Switch2, 1f)])
                - Level("Legacy VL100", frequency, 1e-3, clean);
        }

        var (top, middle) = (Lift(10_000), Lift(1_000));
        output.WriteLine($"Presence switch: {top:0.0} dB at 10 kHz, {middle:0.0} dB at 1 kHz");
        Assert.InRange(top, 6, 12);
        Assert.InRange(middle, 0, 3);
    }

    /// <summary>The Legacy's lead Presence (a rheostat into 2.2 nF on Volume 1's wiper) cuts less treble as it turns up.</summary>
    [Fact]
    public void Legacy_lead_presence_turns_the_treble_up() {
        var levels = new[] { 0f, 5f, 10f }
            .Select(v => Level(
                    "Legacy VL100",
                    5_000,
                    1e-4,
                    (CircuitAmpBlock.Gain, 3f),
                    (CircuitAmpBlock.Master, 3f),
                    (CircuitAmpBlock.Control1, v)
                )
                - Level(
                    "Legacy VL100",
                    500,
                    1e-4,
                    (CircuitAmpBlock.Gain, 3f),
                    (CircuitAmpBlock.Master, 3f),
                    (CircuitAmpBlock.Control1, v)
                )
            )
            .ToArray();
        output.WriteLine(
            $"5 kHz relative to 500 Hz at Presence 0/5/10: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
        );
        Assert.True(levels[1] > levels[0] + 1 && levels[2] > levels[1] + 1);
    }

    /// <summary>
    ///     The SLO's Crunch switch drops the Clean divider (470k in series, 39k across the volume): about 23 dB more
    ///     signal into the normal channel's second stage (52 % against 4 % at full volume, per Robinette).
    /// </summary>
    [Fact]
    public void Slo_crunch_raises_the_normal_channel_gain() {
        double Normal(bool crunch) =>
            Level(
                "SLO-100",
                1_000,
                1e-4,
                (CircuitAmpBlock.Switch1, 1f),
                (CircuitAmpBlock.Switch2, crunch ? 1f : 0f),
                (CircuitAmpBlock.Gain, 10f),
                (CircuitAmpBlock.Master, 3f)
            );

        var lift = Normal(true) - Normal(false);
        output.WriteLine($"Crunch: {lift:0.0} dB");
        Assert.InRange(lift, 15, 26);
    }
}
