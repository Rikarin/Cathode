using System.Security.Cryptography;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Every Circuit Amp model, Drive type and Dynamic Distortion type at its defaults (at two gains) renders
///     bit-identical to master before the advanced-page and Drive parity work (stream PB): the SHA-256 of each render
///     must match <c>tests/assets/defaults/master.sha256</c>, which was generated from master (commit 49fe687 with its
///     static-initialisation fix). New controls must default to stock, so this only changes when a model itself does;
///     regenerate then with <c>CATHODE_UPDATE_DEFAULTS=1 dotnet test</c> on the commit that changes it, and review.
/// </summary>
public class AmpDefaultsTests {
    static readonly string[] Blocks = ["circuitamp", "drive", "dyndist"];
    static readonly float[] Gains = [2f, 8f];
    static readonly string Path = System.IO.Path.Combine(EngineRoot(), "tests", "assets", "defaults", "master.sha256");

    [Fact]
    public void Every_model_and_type_renders_bit_identical_to_master_at_defaults() {
        var renders = Render();
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_DEFAULTS") == "1") {
            File.WriteAllLines(Path, renders);
            return;
        }

        var reference = File.ReadAllLines(Path);
        Assert.Equal(81 * Gains.Length, renders.Count(static l => l.StartsWith("circuitamp ", StringComparison.Ordinal)));
        var mismatched = reference.Except(renders).ToArray();
        // Types added since (more Dynamic Distortion curves) have no reference; every one that has must match.
        Assert.True(mismatched.Length == 0, $"Not bit-identical to master: {string.Join("; ", mismatched.Take(10))}");
    }

    /// <summary>One line per block type, type and gain: "id type gain SHA-256" of the stereo render.</summary>
    static List<string> Render() {
        var registry = BlockRegistry.CreateDefault();
        var lines = new List<string>();
        foreach (var typeId in Blocks) {
            var descriptor = registry.Descriptors.First(d => d.TypeId == typeId);
            var types = descriptor.Params[0].Options!.Count;
            for (var t = 0; t < types; t++) {
                foreach (var gain in Gains) {
                    using var block = registry.Create(typeId);
                    block.Params.SetTarget(0, t);
                    block.Params.SetTarget(1, gain);
                    block.Prepare(new(48_000, 64));
                    var input = Signals.Concat(
                        Signals.Noise(2_000, 3, 0.3f),
                        Signals.Sine(110, 48_000, 6_000, 0.5f),
                        new float[2_000]
                    );
                    // Uneven host blocks: the render must not depend on them either.
                    var (left, right) = BlockRender.Stereo(block, input, input, static i => 17 + i % 47);
                    var bytes = new byte[left.Length * 8];
                    Buffer.BlockCopy(left, 0, bytes, 0, left.Length * 4);
                    Buffer.BlockCopy(right, 0, bytes, left.Length * 4, left.Length * 4);
                    lines.Add($"{typeId} {t} {gain} {Convert.ToHexString(SHA256.HashData(bytes))}");
                }
            }
        }

        return lines;
    }

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cathode.slnx not found above the test binaries.");
    }
}
