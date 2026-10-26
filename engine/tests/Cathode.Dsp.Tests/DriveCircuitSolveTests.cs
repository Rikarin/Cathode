using Cathode.Dsp.Blocks;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The drive catalogue against committed renders (<c>tests/assets/golden/drive-circuits.wav</c>, one channel per
///     circuit type, antialiased then exact), a guard on the solvers: a faster closed form, a different Newton start or a
///     changed tolerance may move the last bits, never the sound. Circuits appended after the render was made are
///     skipped until it is regenerated. Regenerate with <c>CATHODE_UPDATE_GOLDEN=1</c> only for
///     an intended change to the catalogue (last: the component-level tone networks, op-amp stages and transistor
///     fuzzes, and the guitar source).
/// </summary>
public class DriveCircuitSolveTests(ITestOutputHelper output) {
    const int Frames = 2_048;
    const double NullDb = -100;

    static readonly string Path =
        System.IO.Path.Combine(FindEngineRoot(), "tests", "assets", "golden", "drive-circuits.wav");

    [Fact]
    public void Circuit_types_null_against_their_committed_renders() {
        var renders = new List<float[]>();
        foreach (var antialias in new[] { true, false }) {
            for (var i = 0; i < DriveCircuits.All.Count; i++) {
                renders.Add(Render(DriveBlock.CircuitTypes + i, antialias));
            }
        }

        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_GOLDEN") == "1"
            || (!File.Exists(Path) && Environment.GetEnvironmentVariable("CI") is null)) {
            WavFile.Write(Path, new(48_000, [..renders]));
            return;
        }

        // Circuits appended since the render was committed have no channel: every circuit that has one must match.
        var golden = WavFile.Read(Path);
        var committed = golden.Channels.Length / 2;
        Assert.Equal(0, golden.Channels.Length % 2);
        Assert.InRange(committed, 1, DriveCircuits.All.Count);
        var worst = double.NegativeInfinity;
        for (var c = 0; c < golden.Channels.Length; c++) {
            var reference = golden.Channels[c];
            var (half, type) = (c / committed, c % committed);
            var render = renders[half * DriveCircuits.All.Count + type];
            var residual = Analysis.NullResidualDb(reference, render) - Analysis.ToDb(Analysis.Rms(reference));
            var name = DriveCircuits.All[type].Name;
            output.WriteLine($"{name,-16} {(half == 0 ? "antialiased" : "exact"),-11} null {residual:0.0} dB");
            worst = Math.Max(worst, residual);
            Assert.True(residual < NullDb, $"{name}: null {residual:0.0} dB against the committed render");
        }

        output.WriteLine($"worst null {worst:0.0} dB");
    }

    /// <summary>A hot noise burst, then a loud decaying chord: every circuit clips hard for part of it.</summary>
    static float[] Render(int type, bool antialias) {
        using var block = new DriveBlock();
        block.Antialias = antialias;
        block.Params.SetTarget(DriveBlock.Type, type);
        block.Params.SetTarget(DriveBlock.Drive, 7f);
        block.Params.SetTarget(DriveBlock.Tone, 5f);
        block.Prepare(new(48_000, 64));
        var noise = Signals.Noise(512, 11, 0.3f);
        var low = Signals.Sine(110, 48_000, Frames - 512, 0.4f);
        var high = Signals.Sine(330, 48_000, Frames - 512, 0.2f);
        var chord = low.Select((v, i) => (v + high[i]) * MathF.Exp(-i / 1_000f)).ToArray();
        return BlockRender.Mono(block, Signals.Concat(noise, chord), 64);
    }

    static string FindEngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Cannot find the engine folder.");
    }
}
