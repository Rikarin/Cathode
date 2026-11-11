using Cathode.Testing.References;
using System.Globalization;

namespace Cathode.References.Tests;

/// <summary>
///     A drift guard: matches every reference that has circuit settings against the circuit amp, and fails when a
///     distance grows past the manifest's targets. The targets are what each match measured, plus a margin
///     (<see cref="MatchTargets.Around" />); many admit an ESR as large as the signal (silence scores 1.0), so passing
///     here says the model has not moved away from the capture, not that it matches it. The bar for a match is
///     docs/06 §5.4's (<see cref="ShipTarget" />), which <see cref="ReportVariable" /> lets a run count. A reference
///     whose entry does not guard all four measures fails once it is matched: no run passes on ESR and MRSTFT alone.
/// </summary>
/// <remarks>
///     References not in the local cache are skipped, since tests never download. The nightly workflow fetches the
///     redistributable ones and sets <c>CATHODE_REFERENCES_REQUIRED=1</c>: then a redistributable reference that is
///     missing (or a NAM one without <c>cth_nam</c>) fails instead of skipping; only the local-only (T3K) ones skip.
/// </remarks>
public class ReferenceMatchTests(ITestOutputHelper output) {
    /// <summary>
    ///     A file each match appends a line to, when set: the id, the pre-emphasised ESR, MRSTFT, harmonic and response
    ///     distances and whether the match is within its drift guard, separated by tabs. The nightly job counts from
    ///     it how many captures reach the ship target (<c>cathode-references ship-target</c>).
    /// </summary>
    public const string ReportVariable = "CATHODE_REFERENCES_REPORT";

    public static TheoryData<string> Comparable =>
        new(Manifest.References.Where(static r => r.Circuit is not null).Select(static r => r.Id));

    static ReferenceManifest Manifest { get; } = ReferenceManifest.Load(ReferenceManifest.Locate());

    [Theory]
    [MemberData(nameof(Comparable))]
    public void Circuit_amp_stays_within_its_drift_guard_against_the_reference(string id) {
        var entry = Manifest.Find(id);
        var cache = ReferenceCache.Default;
        var missing = cache.StateOf(entry) == ReferenceState.Missing;
        var unsupported = ReferenceModels.Unsupported(entry);
        if (ReferenceCache.Required && entry.Redistributable) {
            Assert.False(missing, $"{id} is redistributable but not in the reference cache ({cache.Root})");
            Assert.True(unsupported is null, $"{id} is redistributable but cannot run here: {unsupported}");
        }

        Assert.SkipWhen(missing, $"{id} is not in the reference cache ({cache.Root})");
        Assert.SkipWhen(unsupported is not null, $"{id}: {unsupported}");
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
        if (Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } file) {
            File.AppendAllText(
                file,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{id}\t{report.EsrPreEmphasis:0.000}\t{report.Mrstft:0.000}\t{report.HarmonicDistanceDb:0.0}\t"
                    + $"{report.ResponseRmsDb:0.0}\t{(report.Pass == true ? "within" : "drifted")}\n"
                )
            );
        }

        // A capture nobody had matched (a local-only one fetched by hand) has no guard, and ESR and MRSTFT alone
        // guard little: whoever holds the file records all four measures before this can pass.
        Assert.True(
            entry.Targets is { GuardsEveryMeasure: true },
            $"{id} was matched, but its entry does not guard all four measures. Check the match, then record in the "
            + $"manifest: {MatchTargets.Around(report).ToManifest()}"
        );
        Assert.True(report.Pass, $"{id} has drifted from its reference: {string.Join(", ", report.Drifted())}");
    }

    [Fact]
    public void The_guard_to_record_for_a_new_match_sits_a_margin_above_what_it_measured() {
        var guard = MatchTargets.Around(Report(0.171, 0.731, 7.6, 1.4, null));

        Assert.Equal(new(0.2, 0.85, 9, 2), guard);
        Assert.True(guard.GuardsEveryMeasure);
        Assert.False(new MatchTargets(2.0, 8.0).GuardsEveryMeasure);
        Assert.Equal(
            "\"targets\": { \"esr\": 0.2, \"mrstft\": 0.85, \"harmonicsDb\": 9, \"responseDb\": 2 }",
            guard.ToManifest()
        );

        // A value already on a step stays there.
        Assert.Equal(new(1.15, 2.3, 11.5, 3.5), MatchTargets.Around(Report(1.0, 2.0, 10, 3, null)));
    }

    [Fact]
    public void The_drift_guard_holds_every_measure_the_entry_guards() {
        var targets = new MatchTargets(1.2, 2.0, 12, 3);

        Assert.True(Report(1.1, 1.9, 11, 2.9, targets).Pass);
        Assert.False(Report(1.3, 1.9, 11, 2.9, targets).Pass);
        Assert.False(Report(1.1, 2.1, 11, 2.9, targets).Pass);
        Assert.Equal(["harmonics 40.0 dB (target 12)"], Report(1.0, 1.9, 40, 2.9, targets).Drifted());
        Assert.Equal(["response 6.0 dB (target 3)"], Report(1.0, 1.9, 11, 6, targets).Drifted());
        Assert.False(Report(double.NaN, 1.9, 11, 2.9, targets).Pass);

        // Without the harmonic and response guards, ESR and MRSTFT alone decide; without targets, nothing does.
        Assert.True(Report(1.0, 1.9, 40, 6, new(1.2, 2.0)).Pass);
        Assert.Null(Report(1.0, 1.9, 40, 6, null).Pass);
    }

    [Fact]
    public void The_ship_target_counts_matches_by_their_esr() {
        var guard = new MatchTargets(1.2, 2.0);
        MatchReport[] reports = [
            Report(0.04, 0.5, 3, 1, guard), Report(0.12, 0.8, 5, 1, guard), Report(0.9, 1.5, 9, 2, guard),
            Report(1.3, 1.5, 9, 2, guard)
        ];

        Assert.Equal(
            "docs/06 §5.4 ship target: 1 of 4 matched captures at ESR ≤ 0.05 (the clean and crunch bar), 2 at ESR ≤ "
            + "0.15 (the high-gain bar); 3 within their drift guard.",
            ShipTarget.Summary(reports)
        );
    }

    static MatchReport Report(double esr, double mrstft, double harmonics, double response, MatchTargets? targets) =>
        new(
            "id",
            "amp",
            "GPL-3.0",
            "test",
            null,
            "circuit",
            48_000,
            0,
            false,
            0,
            0,
            false,
            esr,
            esr,
            mrstft,
            [],
            [],
            [],
            harmonics,
            [],
            [],
            [],
            response,
            targets,
            []
        );
}
