using Cathode.Testing.References;

namespace Cathode.References.Tests;

/// <summary>
///     Matches every reference that has circuit settings against the circuit amp, and holds it to the manifest's
///     targets. References not in the local cache are skipped, never failed: CI has no cache, and tests never download.
/// </summary>
public class ReferenceMatchTests(ITestOutputHelper output) {
    public static TheoryData<string> Comparable =>
        new(Manifest.References.Where(static r => r.Circuit is not null).Select(static r => r.Id));

    static ReferenceManifest Manifest { get; } = ReferenceManifest.Load(ReferenceManifest.Locate());

    [Theory]
    [MemberData(nameof(Comparable))]
    public void Circuit_amp_meets_its_target_against_the_reference(string id) {
        var entry = Manifest.Find(id);
        var cache = ReferenceCache.Default;
        Assert.SkipWhen(
            cache.StateOf(entry) == ReferenceState.Missing,
            $"{id} is not in the reference cache ({cache.Root})"
        );
        Assert.SkipWhen(ReferenceModels.Unsupported(entry) is not null, $"{id}: {ReferenceModels.Unsupported(entry)}");
        var path = cache.TryLocate(entry);
        Assert.True(path is not null, $"{id} in the cache does not match its SHA-256");

        using var reference = ReferenceModels.Open(entry, path!);
        var report = ReferenceMatcher.Match(entry, reference, entry.Circuit!);

        output.WriteLine(
            $"{id} vs {report.Circuit}: input {report.InputDbu:0.0} "
            + $"dBu{(report.InputDbuFitted ? " (fitted)" : string.Empty)}, "
            + $"ESR {report.Esr:0.000}, ESR pre-emphasised {report.EsrPreEmphasis:0.000}, MRSTFT "
            + $"{report.Mrstft:0.000}, "
            + $"harmonics {report.HarmonicDistanceDb:0.0} dB, response {report.ResponseRmsDb:0.0} dB"
        );
        Assert.True(
            report.Pass,
            $"{id}: ESR {report.EsrPreEmphasis:0.000} (target {entry.Targets!.Esr}), MRSTFT {report.Mrstft:0.000} "
            + $"(target {entry.Targets.Mrstft})"
        );
    }
}
