using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Golden renders (docs/03 §12): every registered block, at its defaults, renders a fixed stereo stimulus that must
///     match its committed reference in <c>tests/assets/golden</c>. A refactor that changes the sound fails here; an
///     intended change is accepted by regenerating with <c>CATHODE_UPDATE_GOLDEN=1 dotnet test</c> and reviewing the
///     diff. The tolerance absorbs last-bit differences between platforms' math libraries, not audible changes.
/// </summary>
public class GoldenRenderTests {
    const int Frames = 6_000;
    const double ResidualDb = -90;

    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly BlockRegistry Registry = BlockRegistry.CreateDefault();
    static readonly string Folder = Path.Combine(FindEngineRoot(), "tests", "assets", "golden");

    public static TheoryData<string> BlockTypes => [..Registry.Descriptors.Select(static d => d.TypeId)];

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Default_render_matches_the_committed_reference(string typeId) {
        using var block = Registry.Create(typeId);
        block.Prepare(Spec);
        var (left, right) = BlockRender.Stereo(block, Stimulus(1), Stimulus(2), static _ => Spec.MaxBlockSize);
        var path = Path.Combine(Folder, typeId + ".wav");

        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_GOLDEN") == "1"
            || (!File.Exists(path) && Environment.GetEnvironmentVariable("CI") is null)) {
            Directory.CreateDirectory(Folder);
            WavFile.Write(path, new((int)Spec.SampleRate, [left, right]));
            return;
        }

        Assert.True(File.Exists(path), $"No golden render for {typeId}: run CATHODE_UPDATE_GOLDEN=1 dotnet test.");
        var golden = WavFile.Read(path);
        Assert.True(
            Analysis.NullResidualDb(golden.Channels[0], left) < ResidualDb
            && Analysis.NullResidualDb(golden.Channels[1], right) < ResidualDb,
            $"{typeId} no longer matches its golden render (left "
            + $"{Analysis.NullResidualDb(golden.Channels[0], left):0.0} dB, right "
            + $"{Analysis.NullResidualDb(golden.Channels[1], right):0.0} dB). If the change is intended, regenerate "
            + "with CATHODE_UPDATE_GOLDEN=1."
        );
    }

    /// <summary>A noise burst, a decaying 220 Hz tone, then silence for the tails: different on each side.</summary>
    static float[] Stimulus(int seed) {
        var noise = Signals.Noise(1_500, seed, 0.25f);
        var tone = Signals.Sine(220 * seed, Spec.SampleRate, 1_500, 0.5f)
            .Select(static (v, i) => v * MathF.Exp(-i / 500f))
            .ToArray();
        return Signals.Concat(noise, tone, new float[Frames - 3_000]);
    }

    static string FindEngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Cannot find the engine folder.");
    }
}
