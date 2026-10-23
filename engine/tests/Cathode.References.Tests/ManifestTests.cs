using Cathode.Dsp.Blocks;
using Cathode.Testing.References;

namespace Cathode.References.Tests;

public class ManifestTests {
    static readonly string[] GplCompatible = ["GPL-3.0", "GPL-3.0-only", "GPL-3.0-or-later", "MIT", "BSD-3-Clause", "CC0-1.0", "CC-BY-4.0", "CC-BY-SA-4.0"];

    /// <summary>Captures of modellers (docs/06 §4.2): matching them would clone someone else's model.</summary>
    static readonly string[] Modellers = ["axe", "fractal", "fm3", "fm9", "kemper", "helix", "line 6", "quad cortex", "tonex"];

    static ReferenceManifest Manifest { get; } = ReferenceManifest.Load(ReferenceManifest.Locate());

    [Fact]
    public void Ids_are_unique_and_formats_known() {
        Assert.NotEmpty(Manifest.References);
        Assert.Equal(Manifest.References.Count, Manifest.References.Select(static r => r.Id).Distinct().Count());
        Assert.All(Manifest.References, static r => Assert.NotNull(r.FileName));
    }

    [Fact]
    public void Only_GPL_compatible_files_are_marked_redistributable_and_they_carry_a_hash() {
        Assert.All(Manifest.References.Where(static r => r.Redistributable), static r => {
            Assert.Contains(r.Licence, GplCompatible);
            Assert.Matches("^[0-9a-f]{64}$", r.Sha256);
            Assert.NotNull(r.Size);
        });
        Assert.All(
            Manifest.References.Where(static r => !GplCompatible.Contains(r.Licence)),
            static r => Assert.False(r.Redistributable, $"{r.Id} ({r.Licence}) must stay local-only")
        );
    }

    [Fact]
    public void No_reference_is_a_capture_of_a_modeller() =>
        Assert.All(Manifest.References, static r => {
            var text = $"{r.Amp} {r.Url} {r.SourceName}".ToLowerInvariant();
            Assert.DoesNotContain(Modellers, m => text.Contains(m, StringComparison.Ordinal));
        });

    [Fact]
    public void No_reference_file_is_committed() {
        var folder = Path.GetDirectoryName(ReferenceManifest.Locate())!;
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(static f => f is not ("manifest.json" or "guitarml_golden.py") && !f!.EndsWith(".golden.txt", StringComparison.Ordinal));
        Assert.Empty(files);
    }

    [Fact]
    public void Circuit_settings_name_real_types_and_parameters() {
        using var renderer = new CircuitAmpRenderer(48_000);
        var ids = CircuitAmpRenderer.ParameterIds.ToHashSet(StringComparer.Ordinal);
        Assert.All(Manifest.References.Where(static r => r.Circuit is not null), r => {
            Assert.Contains(r.Circuit!.Type, renderer.Types);
            Assert.All(r.Circuit.Params?.Keys ?? [], id => Assert.Contains(id, ids));
            Assert.NotNull(r.Targets);
        });
        Assert.Contains("type", ids);
        Assert.Equal(CircuitAmpBlock.Info.Params.Count, ids.Count);
    }

    [Fact]
    public void Conditioned_models_list_their_knob_values() =>
        Assert.All(
            Manifest.References.Where(static r => r.Conditioning is not null),
            static r => Assert.All(r.Conditioning!, static v => Assert.InRange(v, 0, 1))
        );
}
