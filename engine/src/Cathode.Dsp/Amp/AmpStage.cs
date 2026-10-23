using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>Which front-panel knob turns a stage's pot.</summary>
public enum PotRole {
    /// <summary>The channel volume (gain).</summary>
    Gain,

    /// <summary>The master volume, just ahead of the phase inverter.</summary>
    Master
}

/// <summary>The design of one preamp stage; <see cref="Create" /> builds it.</summary>
public abstract record AmpStageDesign(string Name) {
    /// <summary>A new stage for this design at <paramref name="sampleRate" />, at its operating point.</summary>
    public abstract AmpStage Create(double sampleRate);
}

/// <summary>The preamp knobs, as rotations 0..1, and the bright switch.</summary>
public readonly record struct AmpControls(
    double Gain,
    double Bass,
    double Middle,
    double Treble,
    double Master,
    bool Bright
) {
    public double Rotation(PotRole role) => role == PotRole.Gain ? Gain : Master;
}

/// <summary>
///     One stage of a preamp chain, run at the oversampled rate. A stage reads the previous stage's output as an
///     ideal voltage (a plate's open-circuit voltage, or a divider's output) behind that stage's
///     <see cref="OutputResistance" />, which it includes in its own circuit: the next stage's load, coupling cap and
///     grid current then act on the previous plate through its small-signal output impedance (a Thévenin cut, which
///     linearises only the previous tube's plate resistance).
/// </summary>
public abstract class AmpStage {
    /// <summary>Small-signal output (Thévenin) resistance at the operating point, in ohms.</summary>
    public abstract double OutputResistance { get; }

    /// <summary>The output voltage at rest (the operating point), which the next stage precharges against.</summary>
    public abstract double RestOutput { get; }

    /// <summary>The Newton statistics of the stage's nonlinear root, or null for a linear stage.</summary>
    public virtual NewtonSolver? Newton => null;

    public abstract void SetSampleRate(double sampleRate);

    /// <summary>The previous stage's output resistance, in series with this stage's own input resistance.</summary>
    public abstract void SetSourceResistance(double ohms);

    /// <summary>Supply voltage as a fraction of the nominal one (power-supply sag), per sample.</summary>
    public abstract void SetSupply(double scale);

    /// <summary>Applies the knobs; recomputes the circuit only when a pot this stage owns has moved.</summary>
    public abstract void Update(in AmpControls controls);

    /// <summary>
    ///     Puts the stage at its DC operating point for a source resting at <paramref name="restInput" /> volts:
    ///     capacitors precharged and Newton started there, so a reset amp is silent instead of powering up.
    /// </summary>
    public abstract void Reset(double restInput);

    /// <summary>One sample at the oversampled rate.</summary>
    public abstract double Tick(double input);

    public abstract void FlushDenormals();
}

/// <summary>DC operating points of tube stages, by bisection on the tube equations (control thread or reset).</summary>
public static class OperatingPoint {
    /// <summary>
    ///     A common-cathode triode: grid at <paramref name="grid" /> volts (no grid current), plate resistor
    ///     <paramref name="plateResistor" /> from <paramref name="supply" />, cathode resistor
    ///     <paramref name="cathodeResistor" /> to ground. Returns the plate current and the plate and cathode voltages.
    /// </summary>
    public static (double Current, double Plate, double Cathode) CommonCathode(
        TriodeModel model,
        double grid,
        double supply,
        double plateResistor,
        double cathodeResistor
    ) {
        var triode = new Triode(model);
        var low = 0.0;
        var high = supply / (plateResistor + cathodeResistor);
        for (var i = 0; i < 100; i++) {
            var current = 0.5 * (low + high);
            var cathode = current * cathodeResistor;
            var plate = supply - current * plateResistor;
            if (PlateCurrent(triode, grid - cathode, plate - cathode) > current) {
                low = current;
            } else {
                high = current;
            }
        }

        var ia = 0.5 * (low + high);
        return (ia, supply - ia * plateResistor, ia * cathodeResistor);
    }

    /// <summary>
    ///     A cathode follower: plate at <paramref name="supply" />, cathode resistor to ground. Returns the current and
    ///     the cathode voltage.
    /// </summary>
    public static (double Current, double Cathode) CathodeFollower(
        TriodeModel model,
        double grid,
        double supply,
        double cathodeResistor
    ) {
        var triode = new Triode(model);
        var low = 0.0;
        var high = supply;
        for (var i = 0; i < 100; i++) {
            var cathode = 0.5 * (low + high);
            if (PlateCurrent(triode, grid - cathode, supply - cathode) * cathodeResistor > cathode) {
                low = cathode;
            } else {
                high = cathode;
            }
        }

        var vk = 0.5 * (low + high);
        return (vk / cathodeResistor, vk);
    }

    /// <summary>
    ///     The small-signal plate resistance r_p and amplification factor μ of a triode at (V_gk, V_pk), from the
    ///     model's Jacobian.
    /// </summary>
    public static (double PlateResistance, double Mu) SmallSignal(TriodeModel model, double vgk, double vpk) {
        Span<double> v = [vgk, vpk];
        Span<double> i = stackalloc double[2];
        Span<double> j = stackalloc double[4];
        new Triode(model).Evaluate(v, i, j, 2);
        var rp = 1 / Math.Max(j[3], 1e-12);
        return (rp, j[2] * rp);
    }

    /// <summary>Koren plate current of <paramref name="triode" />.</summary>
    public static double PlateCurrent(Triode triode, double vgk, double vpk) {
        Span<double> v = [vgk, vpk];
        Span<double> i = stackalloc double[2];
        Span<double> j = stackalloc double[4];
        triode.Evaluate(v, i, j, 2);
        return i[1];
    }
}
