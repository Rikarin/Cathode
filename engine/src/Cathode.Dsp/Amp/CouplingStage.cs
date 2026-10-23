using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A coupling cap and an optional series resistor into a pot to ground (and an optional shunt resistor across it),
///     whose wiper is the output: a mixing network into a master volume ahead of the phase inverter.
/// </summary>
public sealed record CouplingStageDesign(
    string Name,
    double CouplingCap,
    Pot Pot,
    PotRole Role,
    double SeriesResistor = 0,
    double ShuntResistor = 0
) : AmpStageDesign(Name) {
    public override AmpStage Create(double sampleRate) {
        var stage = new CouplingStage(this);
        stage.SetSampleRate(sampleRate);
        return stage;
    }
}

/// <summary>
///     A <see cref="CouplingStageDesign" />: one capacitor, so it is solved in closed form with the trapezoidal rule
///     (the same discretisation as a wave digital capacitor) instead of through an adaptor. Linear and cheap.
/// </summary>
public sealed class CouplingStage(CouplingStageDesign design) : AmpStage {
    double sourceResistance = 1;
    double rotation = double.NaN;
    double fraction;
    double halfStep; // T / 2C
    double conductance;
    double load;
    double history; // v_c[n-1] + (T / 2C) i[n-1]

    public CouplingStageDesign Design => design;

    public override double OutputResistance {
        get {
            var lower = fraction * design.Pot.Ohms;
            var source = sourceResistance + design.SeriesResistor;
            var node = design.ShuntResistor > 0 ? Parallel(source, design.ShuntResistor) : source;
            return Parallel(lower, design.Pot.Ohms - lower + node);
        }
    }

    /// <summary>The resistance from the network's node to ground: the pot and the shunt resistor.</summary>
    double Load => design.ShuntResistor > 0 ? Parallel(design.Pot.Ohms, design.ShuntResistor) : design.Pot.Ohms;

    public override double RestOutput => 0;

    public override void SetSampleRate(double sampleRate) {
        halfStep = 0.5 / (design.CouplingCap * sampleRate);
        Recalculate();
    }

    public override void SetSourceResistance(double ohms) {
        sourceResistance = Math.Max(ohms, 1);
        Recalculate();
    }

    public override void SetSupply(double scale) { }

    public override void Update(in AmpControls controls) {
        var turn = controls.Rotation(design.Role);
        if (turn == rotation) {
            return;
        }

        rotation = turn;
        fraction = design.Pot.Fraction(turn);
    }

    public override void Reset(double restInput) {
        history = restInput;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        var current = (input - history) * conductance;
        history += 2 * halfStep * current;
        return current * load * fraction;
    }

    public override void FlushDenormals() { }

    void Recalculate() {
        load = Load;
        conductance = 1 / (sourceResistance + design.SeriesResistor + load + halfStep);
    }

    static double Parallel(double a, double b) => a * b / (a + b);
}
