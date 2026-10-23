using Cathode.Testing.References;

namespace Cathode.References.Tests;

public class ToleranceFitTests {
    [Fact]
    public void Nelder_Mead_finds_the_minimum_of_a_skewed_bowl() {
        var (x, value, evaluations) = NelderMead.Minimize(
            static p => Math.Pow(p[0] - 0.3, 2) + 4 * Math.Pow(p[1] - 0.7, 2) + (p[0] - 0.3) * (p[1] - 0.7),
            [0.5, 0.5],
            0.25,
            300,
            1e-12
        );

        Assert.Equal(0.3, x[0], 3);
        Assert.Equal(0.7, x[1], 3);
        Assert.True(value < 1e-6);
        Assert.InRange(evaluations, 10, 300);
    }

    [Fact]
    public void Recovers_perturbed_values_of_a_circuit_amp() {
        // A Plexi whose gain pot sits off its marking and whose feedback is weaker than stock plays the reference; the
        // fit starts from the stock values and must land on the perturbation.
        const double Rate = 48_000;
        var settings = new CircuitSettings("Plexi", new Dictionary<string, double> { ["gain"] = 6 });
        var truth = new Dictionary<string, double> { ["gain"] = 6.8, ["feedback"] = 4.2 };
        var tolerances = new[] {
            Tolerance.Absolute("gain", 6, 1.5),
            Tolerance.Absolute("feedback", 5, 2)
        };
        using var circuit = new CircuitAmpRenderer(Rate);
        var volts = MatchSignals.Performance(Rate, [0.3], 0.15);
        var target = circuit.Render(volts, settings, truth);

        var result = ToleranceFit.ForCircuitAmp(circuit, settings, tolerances, volts, target).Run(60);

        Assert.Equal(6.8, result.Values["gain"], 0.2);
        Assert.Equal(4.2, result.Values["feedback"], 0.4);
        Assert.True(result.Score < result.NominalScore / 10, $"score {result.NominalScore} -> {result.Score}");
        Assert.Empty(result.AtBound);
    }
}
