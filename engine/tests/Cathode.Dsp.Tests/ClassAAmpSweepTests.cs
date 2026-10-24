using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Knob sweeps of the class-A family through the Circuit Amp block (docs/06 §5.4 step 3): each knob moves the
///     sound the way the circuit does, monotonically, on both channels; the channel switch and full drive stay finite;
///     and each model and channel renders as committed (golden renders, regenerated with <c>CATHODE_UPDATE_GOLDEN=1</c>).
/// </summary>
public class ClassAAmpSweepTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const float Quiet = 0.003f; // well below clipping at any gain, so levels compare as small-signal gains

    public static TheoryData<string, bool> Channels => new() {
        { "AC30", false },
        { "AC30", true },
        { "AC15", false },
        { "AC15", true },
        { "DC-30", false },
        { "DC-30", true }
    };

    [Theory]
    [MemberData(nameof(Channels))]
    public void Gain_raises_the_level_monotonically(string type, bool channel) {
        var levels = Sweep(type, channel, CircuitAmpBlock.Gain, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 1_000);
        Assert.True(Increasing(levels), string.Join(", ", levels.Select(static l => $"{l:0.0}")));
        Assert.True(levels[^1] - levels[0] > 20, $"{levels[^1] - levels[0]:0.0} dB");
    }

    [Theory]
    [InlineData("AC30")]
    [InlineData("DC-30")]
    public void Treble_and_bass_tilt_the_top_boost(string type) {
        double[] knobs = [0, 2, 4, 6, 8, 10];
        var trebleHigh = Sweep(type, false, CircuitAmpBlock.Treble, knobs, 4_000);
        var trebleMid = Sweep(type, false, CircuitAmpBlock.Treble, knobs, 400);
        var bassLow = Sweep(type, false, CircuitAmpBlock.Bass, knobs, 80);
        var bassMid = Sweep(type, false, CircuitAmpBlock.Bass, knobs, 1_000);
        var treble = trebleHigh.Zip(trebleMid, static (h, m) => h - m).ToArray();
        var bass = bassLow.Zip(bassMid, static (l, m) => l - m).ToArray();
        // With the DC-30's middle at zero the treble pot's first turn moves the 400 Hz a little more than the 4 kHz.
        Assert.True(Increasing(treble, 1), "4 kHz over 400 Hz: " + string.Join(", ", treble.Select(static l => $"{l:0.0}")));
        Assert.True(Increasing(bass), "80 Hz over 1 kHz: " + string.Join(", ", bass.Select(static l => $"{l:0.0}")));
        Assert.True(treble[^1] - treble[0] > 6 && bass[^1] - bass[0] > 6);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public void Cut_takes_off_the_top(string type, bool channel) {
        var levels = Sweep(type, channel, CircuitAmpBlock.Cut, [0, 2, 4, 6, 8, 10], 5_000);
        Assert.True(Decreasing(levels), string.Join(", ", levels.Select(static l => $"{l:0.0}")));
        Assert.True(levels[0] - levels[^1] > 6, $"{levels[0] - levels[^1]:0.0} dB");
    }

    [Fact]
    public void Dc30_tone_rotary_steps_through_its_six_caps() {
        double[] positions = [0, 2, 4, 6, 8, 10];
        var low = Sweep("DC-30", true, CircuitAmpBlock.Control1, positions, 100);
        var high = Sweep("DC-30", true, CircuitAmpBlock.Control1, positions, 3_000);
        var tilt = low.Zip(high, static (l, h) => l - h).ToArray();
        Assert.True(Increasing(tilt), "100 Hz over 3 kHz: " + string.Join(", ", tilt.Select(static l => $"{l:0.0}")));
        Assert.True(tilt[^1] - tilt[0] > 8, $"{tilt[^1] - tilt[0]:0.0} dB");
    }

    [Fact]
    public void Dc30_master_across_the_inverter_turns_the_level_down() {
        var levels = Sweep("DC-30", false, CircuitAmpBlock.Master, [1, 3, 5, 7, 10], 1_000);
        Assert.True(Increasing(levels), string.Join(", ", levels.Select(static l => $"{l:0.0}")));
        Assert.True(levels[^1] - levels[0] > 15, $"{levels[^1] - levels[0]:0.0} dB");
    }

    [Fact]
    public void Ac15_brilliance_cuts_the_bass_of_the_ef86_channel() {
        double Tilt(bool on) {
            var settings = new[] { (CircuitAmpBlock.Bright, on ? 1f : 0f) };
            return Level("AC15", false, 100, settings) - Level("AC15", false, 2_000, settings);
        }

        var (off, on) = (Tilt(false), Tilt(true));
        output.WriteLine($"AC15 100 Hz over 2 kHz: Brilliance off {off:0.0} dB, on {on:0.0} dB");
        Assert.True(off - on > 10, $"{off - on:0.0} dB");
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public void Full_drive_stays_finite_and_bounded(string type, bool channel) {
        using var block = Create(type, channel, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Treble, 10f), (CircuitAmpBlock.Bass, 10f));
        var square = Signals.Sine(82, Rate, 24_000).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var rendered = BlockRender.Mono(block, Signals.Concat(square, Signals.Noise(24_000, 3)), 64);
        var peak = Analysis.ToDb(Analysis.Peak(rendered));
        output.WriteLine($"{type} channel {(channel ? 2 : 1)}: peak {peak:0.0} dBFS");
        Assert.True(Analysis.AllFinite(rendered));
        Assert.True(peak < 6, $"{peak:0.0} dBFS");
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public void Default_render_matches_the_committed_reference(string type, bool channel) {
        using var block = Create(type, channel);
        var stimulus = Signals.Concat(
            Signals.Noise(1_500, 1, 0.25f),
            Signals.Sine(220, Rate, 1_500, 0.5f).Select(static (v, i) => v * MathF.Exp(-i / 500f)).ToArray(),
            new float[3_000]
        );
        var rendered = BlockRender.Mono(block, stimulus, 64);
        var name = $"circuitamp-{type.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal)}-ch{(channel ? 2 : 1)}.wav";
        var path = Path.Combine(GoldenFolder(), name);
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_GOLDEN") == "1"
            || (!File.Exists(path) && Environment.GetEnvironmentVariable("CI") is null)) {
            WavFile.Write(path, new((int)Rate, [rendered]));
            return;
        }

        Assert.True(File.Exists(path), $"No golden render {name}: run CATHODE_UPDATE_GOLDEN=1 dotnet test.");
        var residual = Analysis.NullResidualDb(WavFile.Read(path).Channels[0], rendered);
        Assert.True(
            residual < -90,
            $"{name}: {residual:0.0} dB from its golden render; regenerate with CATHODE_UPDATE_GOLDEN=1 if intended."
        );
    }

    /// <summary>The level (dB) of a quiet sine at <paramref name="frequency" /> for each value of a knob.</summary>
    double[] Sweep(string type, bool channel, int knob, double[] values, double frequency) {
        // The gain at 3 keeps the hottest channel (the DC-30's first) clear of clipping at every tone setting.
        var levels = values
            .Select(v => Level(
                    type,
                    channel,
                    frequency,
                    (CircuitAmpBlock.Gain, knob == CircuitAmpBlock.Gain ? (float)v : 3f),
                    (knob, (float)v)
                )
            )
            .ToArray();
        output.WriteLine(
            $"{type} ch{(channel ? 2 : 1)} {CircuitAmpBlock.Info.Params[knob].Id} at {frequency} Hz: " +
            string.Join(", ", values.Zip(levels, static (v, l) => $"{v}: {l:0.0}"))
        );
        return levels;
    }

    static double Level(string type, bool channel, double frequency, params (int Index, float Value)[] settings) {
        using var block = Create(type, channel, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 19_200, Quiet), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(9_600)));
    }

    static CircuitAmpBlock Create(string type, bool channel, params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(d => d.Name == type));
        block.Params.SetTarget(CircuitAmpBlock.Switch1, channel ? 1 : 0);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    static bool Increasing(double[] levels, double slack = 0.05) =>
        levels.Zip(levels.Skip(1), (a, b) => b >= a - slack).All(static x => x);

    // The cut's last step can lift 5 kHz by a few tenths: the shorted cut cap and the power stage's loading interact.
    static bool Decreasing(double[] levels) => levels.Zip(levels.Skip(1), static (a, b) => b <= a + 0.3).All(static x => x);

    static string GoldenFolder() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return Path.Combine(dir.FullName, "tests", "assets", "golden");
            }
        }

        throw new InvalidOperationException("Cannot find the engine folder.");
    }
}
