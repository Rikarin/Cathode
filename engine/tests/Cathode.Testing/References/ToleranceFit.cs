namespace Cathode.Testing.References;

/// <summary>
///     A physical quantity allowed to vary within its real-world tolerance while matching (docs/06 §5.4): a resistor
///     ±10 %, B+ ±5 %, tube gain spread, a pot's taper offset. Never an EQ band: a fit only moves component values.
/// </summary>
/// <param name="Name">For the circuit amp, a parameter id (e.g. <c>bias</c>); otherwise any name the render reads.</param>
/// <param name="Nominal">The schematic value.</param>
/// <param name="Min">Lower bound.</param>
/// <param name="Max">Upper bound.</param>
public sealed record Tolerance(string Name, double Nominal, double Min, double Max) {
    /// <summary><paramref name="nominal" /> ± <paramref name="fraction" /> (0.1 for ±10 %).</summary>
    public static Tolerance Relative(string name, double nominal, double fraction) =>
        new(name, nominal, nominal * (1 - fraction), nominal * (1 + fraction));

    /// <summary><paramref name="nominal" /> ± <paramref name="spread" /> in the parameter's own units.</summary>
    public static Tolerance Absolute(string name, double nominal, double spread) =>
        new(name, nominal, nominal - spread, nominal + spread);
}

public enum FitObjective {
    /// <summary>Pre-emphasised ESR.</summary>
    Esr,

    /// <summary>Multi-resolution STFT distance.</summary>
    Mrstft,

    /// <summary>Their sum: ESR keeps the waveform honest, MRSTFT the spectrum where phase drifts.</summary>
    Combined
}

/// <param name="Values">The fitted value of each tolerance.</param>
/// <param name="Score">Objective at the fitted values.</param>
/// <param name="NominalScore">Objective at the nominal values, for comparison.</param>
/// <param name="Evaluations">Objective evaluations (renders) used.</param>
/// <param name="AtBound">Tolerances that ended within 2 % of a bound: the real cause may lie outside the fit.</param>
public sealed record FitResult(
    IReadOnlyDictionary<string, double> Values,
    double Score,
    double NominalScore,
    int Evaluations,
    IReadOnlyList<string> AtBound
);

/// <summary>
///     Fits a few <see cref="Tolerance" />s so a model matches a target recording, by Nelder–Mead (derivative-free)
///     in coordinates normalised to each tolerance's bounds. Every evaluation renders the model, aligns its latency and
///     output gain to the target, and scores it.
/// </summary>
/// <param name="tolerances">What may move, and how far.</param>
/// <param name="render">Renders the stimulus with the given values (by tolerance name).</param>
/// <param name="target">The reference's output for the same stimulus.</param>
/// <param name="objective">What is minimised.</param>
public sealed class ToleranceFit(
    IReadOnlyList<Tolerance> tolerances,
    Func<IReadOnlyDictionary<string, double>, float[]> render,
    float[] target,
    FitObjective objective = FitObjective.Combined
) {
    public IReadOnlyList<Tolerance> Tolerances { get; } = tolerances;

    /// <summary>
    ///     A fit of the Circuit Amp block's parameters (tolerance names are parameter ids) on top of
    ///     <paramref name="settings" />, driven by <paramref name="volts" />.
    /// </summary>
    public static ToleranceFit ForCircuitAmp(
        CircuitAmpRenderer circuit,
        CircuitSettings settings,
        IReadOnlyList<Tolerance> tolerances,
        float[] volts,
        float[] target,
        FitObjective objective = FitObjective.Combined
    ) =>
        new(tolerances, values => circuit.Render(volts, settings, values), target, objective);

    /// <summary>
    ///     A fit of the circuit amp against <paramref name="reference" /> on a short two-dynamics performance, through
    ///     the capture's input calibration (<paramref name="inputDbu" />; fit it first with
    ///     <see cref="ReferenceMatcher.FitInputDbu" /> when the source does not state it).
    /// </summary>
    public static ToleranceFit AgainstReference(
        IReferenceModel reference,
        CircuitAmpRenderer circuit,
        CircuitSettings settings,
        IReadOnlyList<Tolerance> tolerances,
        double inputDbu,
        FitObjective objective = FitObjective.Combined
    ) {
        var volts = MatchSignals.Performance(reference.SampleRate, [0.05, 0.3], 0.25);
        var target = reference.Render(Calibration.ToDigital(volts, inputDbu));
        return ForCircuitAmp(circuit, settings, tolerances, volts, target, objective);
    }

    public double Score(IReadOnlyDictionary<string, double> values) {
        var prediction = render(values);
        var aligned = MatchMetrics.Align(target, prediction).Apply(prediction);
        return objective switch {
            FitObjective.Esr => Analysis.Esr(target, aligned),
            FitObjective.Mrstft => MatchMetrics.Mrstft(target, aligned),
            _ => Analysis.Esr(target, aligned) + MatchMetrics.Mrstft(target, aligned)
        };
    }

    public FitResult Run(int maxEvaluations = 150, double tolerance = 1e-4) {
        var nominal = Tolerances.Select(static t => (t.Nominal - t.Min) / (t.Max - t.Min)).ToArray();
        var nominalScore = Score(Values(nominal));
        var (best, score, evaluations) = NelderMead.Minimize(
            u => Score(Values(u)),
            nominal,
            0.25,
            maxEvaluations,
            tolerance
        );

        if (nominalScore <= score) {
            best = nominal;
            score = nominalScore;
        }

        var atBound = Tolerances.Where((_, i) => Math.Clamp(best[i], 0, 1) is < 0.02 or > 0.98)
            .Select(static t => t.Name)
            .ToList();
        return new(Values(best), score, nominalScore, evaluations + 1, atBound);
    }

    Dictionary<string, double> Values(double[] unit) {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < Tolerances.Count; i++) {
            var t = Tolerances[i];
            values[t.Name] = t.Min + Math.Clamp(unit[i], 0, 1) * (t.Max - t.Min);
        }

        return values;
    }
}

/// <summary>Nelder–Mead simplex minimisation (standard coefficients), for a handful of parameters.</summary>
public static class NelderMead {
    /// <summary>
    ///     Minimises <paramref name="f" /> from <paramref name="start" />, with an initial simplex of
    ///     <paramref name="step" /> along each axis, until the simplex's values differ by less than
    ///     <paramref name="tolerance" /> or <paramref name="maxEvaluations" /> is reached.
    /// </summary>
    public static (double[] X, double Value, int Evaluations) Minimize(
        Func<double[], double> f,
        double[] start,
        double step,
        int maxEvaluations,
        double tolerance
    ) {
        var n = start.Length;
        var points = new double[n + 1][];
        var values = new double[n + 1];
        var evaluations = 0;

        double Evaluate(double[] x) {
            evaluations++;
            return f(x);
        }

        points[0] = (double[])start.Clone();
        values[0] = Evaluate(points[0]);
        for (var i = 0; i < n; i++) {
            var point = (double[])start.Clone();
            point[i] += point[i] + step <= 1 ? step : -step; // stay inside the normalised box where possible
            points[i + 1] = point;
            values[i + 1] = Evaluate(point);
        }

        while (evaluations < maxEvaluations) {
            var order = Enumerable.Range(0, n + 1).OrderBy(i => values[i]).ToArray();
            points = [..order.Select(i => points[i])];
            values = [..order.Select(i => values[i])];
            if (values[n] - values[0] < tolerance) {
                break;
            }

            var centroid = new double[n];
            for (var i = 0; i < n; i++) {
                for (var d = 0; d < n; d++) {
                    centroid[d] += points[i][d] / n;
                }
            }

            double[] Toward(double coefficient) {
                var result = new double[n];
                for (var d = 0; d < n; d++) {
                    result[d] = centroid[d] + coefficient * (points[n][d] - centroid[d]);
                }

                return result;
            }

            var reflected = Toward(-1);
            var reflectedValue = Evaluate(reflected);
            if (reflectedValue < values[0]) {
                var expanded = Toward(-2);
                var expandedValue = Evaluate(expanded);
                (points[n], values[n]) = expandedValue < reflectedValue
                    ? (expanded, expandedValue)
                    : (reflected, reflectedValue);
                continue;
            }

            if (reflectedValue < values[n - 1]) {
                (points[n], values[n]) = (reflected, reflectedValue);
                continue;
            }

            var contracted = reflectedValue < values[n] ? Toward(-0.5) : Toward(0.5);
            var contractedValue = Evaluate(contracted);
            if (contractedValue < Math.Min(reflectedValue, values[n])) {
                (points[n], values[n]) = (contracted, contractedValue);
                continue;
            }

            for (var i = 1; i <= n; i++) {
                for (var d = 0; d < n; d++) {
                    points[i][d] = points[0][d] + 0.5 * (points[i][d] - points[0][d]);
                }

                values[i] = Evaluate(points[i]);
            }
        }

        var best = 0;
        for (var i = 1; i <= n; i++) {
            if (values[i] < values[best]) {
                best = i;
            }
        }

        return (points[best], values[best], evaluations);
    }
}
