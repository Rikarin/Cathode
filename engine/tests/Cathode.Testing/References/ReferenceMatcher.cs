using System.Globalization;

namespace Cathode.Testing.References;

/// <summary>The result of matching a circuit amp against one reference.</summary>
public sealed record MatchReport(
    string Id,
    string Amp,
    string Licence,
    string Architecture,
    IReadOnlyDictionary<string, string>? ReferenceSettings,
    string Circuit,
    double SampleRate,
    double InputDbu,
    bool InputDbuFitted,
    int LatencySamples,
    double OutputGainDb,
    bool PolarityInverted,
    double Esr,
    double EsrPreEmphasis,
    double Mrstft,
    IReadOnlyList<double> HarmonicLevels,
    IReadOnlyList<double[]> ReferenceHarmonics,
    IReadOnlyList<double[]> CircuitHarmonics,
    double HarmonicDistanceDb,
    IReadOnlyList<double> ResponseCenters,
    IReadOnlyList<double> ReferenceResponse,
    IReadOnlyList<double> CircuitResponse,
    double ResponseRmsDb,
    MatchTargets? Targets,
    IReadOnlyList<string> Warnings
) {
    /// <summary>True when there are targets and both ESR and MRSTFT meet them.</summary>
    public bool? Pass => Targets is null ? null : EsrPreEmphasis <= Targets.Esr && Mrstft <= Targets.Mrstft;
}

/// <summary>
///     Matches a circuit amp against a reference (docs/06 §5.4, step 2): the same stimuli in volts go to both, the
///     reference through its input calibration (fitted when the source does not state it); latency and output gain are
///     aligned; then ESR, MRSTFT, harmonic profile and small-signal response are compared.
/// </summary>
public static class ReferenceMatcher {
    /// <summary>Range searched when a capture's calibration is unknown.</summary>
    public const double MinFittedDbu = -20;

    public const double MaxFittedDbu = 28;

    /// <summary>
    ///     Renders the reference at a calibration; with the circuit's render of the same signal, returns the scored
    ///     distance. Reused by <see cref="FitInputDbu" />.
    /// </summary>
    public static double CalibrationScore(IReferenceModel reference, float[] volts, float[] circuit, double dbu) {
        var target = reference.Render(Calibration.ToDigital(volts, dbu));
        var aligned = MatchMetrics.Align(target, circuit).Apply(circuit);
        return MatchMetrics.Mrstft(target, aligned);
    }

    /// <summary>
    ///     The input calibration (dBu) under which the reference best matches the circuit amp, by a coarse grid and a
    ///     golden-section refinement of MRSTFT on a short performance. One value per capture; docs/06 asks for one
    ///     per capture set, which a caller gets by fitting one representative and fixing it for the rest.
    /// </summary>
    public static double FitInputDbu(IReferenceModel reference, CircuitAmpRenderer circuit, CircuitSettings settings) {
        var volts = MatchSignals.Performance(reference.SampleRate, [0.05, 0.3], 0.2);
        var rendered = circuit.Render(volts, settings);
        double Score(double dbu) => CalibrationScore(reference, volts, rendered, dbu);

        var best = MinFittedDbu;
        var bestScore = double.PositiveInfinity;
        for (var dbu = MinFittedDbu; dbu <= MaxFittedDbu; dbu += 4) {
            var score = Score(dbu);
            if (score < bestScore) {
                (best, bestScore) = (dbu, score);
            }
        }

        return GoldenSection(Score, Math.Max(MinFittedDbu, best - 4), Math.Min(MaxFittedDbu, best + 4), 0.25);
    }

    public static MatchReport Match(
        ReferenceEntry entry,
        IReferenceModel reference,
        CircuitSettings settings,
        double? inputDbu = null
    ) {
        var rate = reference.SampleRate;
        using var circuit = new CircuitAmpRenderer(rate);
        var warnings = new List<string>();

        var fitted = false;
        var dbu = inputDbu ?? entry.Rig.InputDbu;
        if (dbu is null) {
            dbu = FitInputDbu(reference, circuit, settings);
            fitted = true;
        }

        var calibration = dbu.Value;

        // Performance: alignment, ESR, MRSTFT.
        var performance = MatchSignals.Performance(rate);
        var target = reference.Render(Calibration.ToDigital(performance, calibration));
        var candidate = circuit.Render(performance, settings);
        var alignment = MatchMetrics.Align(target, candidate);
        var aligned = alignment.Apply(candidate);

        // Harmonic profile, with the same output gain so the fundamentals' levels (compression) compare too.
        var levels = MatchSignals.HarmonicLevels;
        var referenceHarmonics = new List<double[]>();
        var circuitHarmonics = new List<double[]>();
        foreach (var level in levels) {
            var tone = MatchSignals.HarmonicTone(rate, level, out var cycles);
            referenceHarmonics.Add(
                MatchMetrics.HarmonicProfile(reference.Render(Calibration.ToDigital(tone, calibration)), cycles)
            );
            circuitHarmonics.Add(MatchMetrics.HarmonicProfile(alignment.Apply(circuit.Render(tone, settings)), cycles));
        }

        // Small-signal response.
        var sweep = MatchSignals.Sweep(rate);
        var referenceResponse = MatchMetrics.FrequencyResponse(
            sweep,
            reference.Render(Calibration.ToDigital(sweep, calibration)),
            rate
        );
        var circuitResponse = MatchMetrics.FrequencyResponse(sweep, circuit.Render(sweep, settings), rate);
        var (responseRms, _) = MatchMetrics.ResponseDistance(referenceResponse, circuitResponse);

        if (Band(referenceResponse, 6_300) - Band(referenceResponse, 1_000) < -18) {
            warnings.Add("the reference falls by more than 18 dB from 1 kHz to 6.3 kHz: it may include a cab or mic");
        }

        if (fitted && (calibration <= MinFittedDbu + 0.5 || calibration >= MaxFittedDbu - 0.5)) {
            warnings.Add($"the fitted calibration hit the edge of its range ({calibration:0.0} dBu)");
        }

        if (entry.Rig.Output is not "direct") {
            warnings.Add($"rig output is '{entry.Rig.Output}', not a direct amp output");
        }

        return new(
            entry.Id,
            entry.Amp,
            entry.Licence,
            reference.Architecture,
            entry.Settings,
            CircuitAmpRenderer.Describe(settings),
            rate,
            calibration,
            fitted,
            alignment.Lag,
            alignment.GainDb,
            alignment.Gain < 0,
            Analysis.Esr(target, aligned, 0),
            Analysis.Esr(target, aligned),
            MatchMetrics.Mrstft(target, aligned),
            levels,
            referenceHarmonics,
            circuitHarmonics,
            MatchMetrics.HarmonicDistance(referenceHarmonics, circuitHarmonics),
            MatchMetrics.ResponseCenters,
            referenceResponse,
            circuitResponse,
            responseRms,
            entry.Targets,
            warnings
        );
    }

    /// <summary>Reads a measured response: CSV lines <c>frequency,dB</c> (a header line is skipped).</summary>
    public static IReadOnlyList<(double Frequency, double Db)> ReadMeasuredResponse(string path) =>
        File.ReadLines(path)
            .Select(static line => line.Split(',', StringSplitOptions.TrimEntries))
            .Where(static parts => parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            )
            .Select(static parts => (
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture)
                )
            )
            .OrderBy(static p => p.Item1)
            .ToList();

    /// <summary>A measured response interpolated (log frequency) onto <see cref="MatchMetrics.ResponseCenters" />.</summary>
    public static double[] Resample(IReadOnlyList<(double Frequency, double Db)> points) {
        var result = new double[MatchMetrics.ResponseCenters.Length];
        for (var b = 0; b < result.Length; b++) {
            var f = MatchMetrics.ResponseCenters[b];
            var upper = Math.Clamp(points.Count(p => p.Frequency < f), 1, points.Count - 1);
            var (f0, d0) = points[upper - 1];
            var (f1, d1) = points[upper];
            var t = Math.Clamp(Math.Log(f / f0) / Math.Log(f1 / f0), 0, 1);
            result[b] = d0 + t * (d1 - d0);
        }

        return result;
    }

    /// <summary>
    ///     Shape distance (dB RMS, 80 Hz – 8 kHz, level removed) between a measured response file and the circuit amp's
    ///     small-signal response at <paramref name="settings" />.
    /// </summary>
    public static double MeasuredResponseDistance(string path, CircuitSettings settings, double sampleRate = 48_000) {
        using var circuit = new CircuitAmpRenderer(sampleRate);
        var sweep = MatchSignals.Sweep(sampleRate);
        var response = MatchMetrics.FrequencyResponse(sweep, circuit.Render(sweep, settings), sampleRate);
        return MatchMetrics.ResponseDistance(Resample(ReadMeasuredResponse(path)), response).RmsDb;
    }

    static double Band(IReadOnlyList<double> response, double frequency) {
        var index = 0;
        for (var b = 1; b < MatchMetrics.ResponseCenters.Length; b++) {
            if (Math.Abs(MatchMetrics.ResponseCenters[b] - frequency)
                < Math.Abs(MatchMetrics.ResponseCenters[index] - frequency)) {
                index = b;
            }
        }

        return response[index];
    }

    static double GoldenSection(Func<double, double> f, double low, double high, double tolerance) {
        var ratio = (Math.Sqrt(5) - 1) / 2;
        var a = high - ratio * (high - low);
        var b = low + ratio * (high - low);
        double fa = f(a), fb = f(b);
        while (high - low > tolerance) {
            if (fa < fb) {
                (high, b, fb) = (b, a, fa);
                a = high - ratio * (high - low);
                fa = f(a);
            } else {
                (low, a, fa) = (a, b, fb);
                b = low + ratio * (high - low);
                fb = f(b);
            }
        }

        return (low + high) / 2;
    }
}
