using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>Which front-panel knob turns a stage's pot.</summary>
public enum PotRole {
    /// <summary>The channel volume (gain).</summary>
    Gain,

    /// <summary>The master volume, just ahead of the phase inverter.</summary>
    Master,

    /// <summary>The bass knob (a <see cref="NetworkStageDesign" /> pot; the TMB stacks wire their own).</summary>
    Bass,

    /// <summary>The middle knob.</summary>
    Middle,

    /// <summary>The treble knob.</summary>
    Treble,

    /// <summary>The model's first extra pot (<see cref="AmpPanel.Control1" />): a lead drive, a second volume.</summary>
    Control1,

    /// <summary>The model's second extra pot (<see cref="AmpPanel.Control2" />).</summary>
    Control2
}

/// <summary>Which front-panel switch engages a switched element of a stage.</summary>
public enum SwitchRole {
    /// <summary>The bright switch.</summary>
    Bright,

    /// <summary>The model's first extra switch (<see cref="AmpPanel.Switch1" />).</summary>
    Switch1,

    /// <summary>The model's second extra switch.</summary>
    Switch2,

    /// <summary>The model's third extra switch.</summary>
    Switch3
}

/// <summary>The design of one preamp stage; <see cref="Create" /> builds it.</summary>
public abstract record AmpStageDesign(string Name) {
    /// <summary>A new stage for this design at <paramref name="sampleRate" />, at its operating point.</summary>
    public abstract AmpStage Create(double sampleRate);
}

/// <summary>The gains of a graphic equaliser's five bands, in decibels.</summary>
public readonly record struct EqualizerGains(double Band1, double Band2, double Band3, double Band4, double Band5) {
    /// <summary>Band <paramref name="index" /> (0..4).</summary>
    public double this[int index] =>
        index switch {
            0 => Band1,
            1 => Band2,
            2 => Band3,
            3 => Band4,
            _ => Band5
        };
}

/// <summary>
///     The preamp knobs, as rotations 0..1, and the switches: the bright switch, the model's extra pots
///     (<paramref name="Control1" />, <paramref name="Control2" />) and switches (<paramref name="Switch1" /> to
///     <paramref name="Switch3" />), and a graphic equaliser's band gains.
/// </summary>
public readonly record struct AmpControls(
    double Gain,
    double Bass,
    double Middle,
    double Treble,
    double Master,
    bool Bright,
    double Control1 = 0.5,
    double Control2 = 0.5,
    bool Switch1 = false,
    bool Switch2 = false,
    bool Switch3 = false,
    EqualizerGains Equalizer = default
) {
    public double Rotation(PotRole role) =>
        role switch {
            PotRole.Gain => Gain,
            PotRole.Master => Master,
            PotRole.Bass => Bass,
            PotRole.Middle => Middle,
            PotRole.Treble => Treble,
            PotRole.Control1 => Control1,
            _ => Control2
        };

    public bool IsOn(SwitchRole role) =>
        role switch {
            SwitchRole.Bright => Bright,
            SwitchRole.Switch1 => Switch1,
            SwitchRole.Switch2 => Switch2,
            _ => Switch3
        };
}

/// <summary>The preamp's expert settings, as the Circuit Amp's expert parameters set them; the defaults are stock.</summary>
/// <param name="Tube">
///     A triode type replacing every preamp triode of the stock 12AX7 type (<see cref="TriodeModel.Ecc83" />), or null
///     for the stock tubes.
/// </param>
/// <param name="BrightCap">The bright caps' values as a multiple of stock.</param>
/// <param name="StackFrequency">The tone stacks' frequencies as a multiple of stock (their capacitors divided by it).</param>
/// <param name="CathodeResistor">
///     The cathode resistor as a multiple of stock: the channel applies it to its last gain stage only (the Axe-Fx's
///     Preamp Bias), 1 elsewhere.
/// </param>
/// <param name="Hardness">Every preamp triode's cut-off knee (<see cref="TriodeModel.Hardened" />): 1 stock.</param>
/// <param name="GridCurrent">
///     Every preamp triode's grid current as a multiple of stock: how far a driven stage charges its coupling cap and
///     shifts its bias (the Axe-Fx's Preamp Bias Excursion); cathode followers take <paramref name="FollowerGrid" />.
/// </param>
/// <param name="PlateFrequency">
///     A plate-bypass cap's corner as a multiple of stock (the cap divided by it); the channel applies it per stage
///     (Triode 1 and 2 Plate Freq on the last two stages with such a cap), 1 elsewhere.
/// </param>
/// <param name="MasterCap">The bright cap across a master pot, as a multiple of stock.</param>
/// <param name="FollowerGrid">
///     A cathode follower's grid current as a multiple of stock: its compression when the previous plate drives its
///     grid above its cathode.
/// </param>
/// <param name="FollowerBias">
///     A cathode follower's cathode resistor as a multiple of stock: its idle current, so how early and how
///     asymmetrically it clips (its harmonics).
/// </param>
/// <param name="Stack">A tone stack in place of a <see cref="ToneStackStage" />'s own; null for stock.</param>
public readonly record struct PreampSettings(
    TriodeModel? Tube = null,
    double BrightCap = 1,
    double StackFrequency = 1,
    double CathodeResistor = 1,
    double Hardness = 1,
    double GridCurrent = 1,
    double PlateFrequency = 1,
    double MasterCap = 1,
    double FollowerGrid = 1,
    double FollowerBias = 1,
    ToneStackDesign? Stack = null
) {
    public static PreampSettings Stock => new(null);

    /// <summary>The tabulated device for a triode built with <paramref name="stock" />, with these settings.</summary>
    public FastTriode Triode(TriodeModel stock) => new(TubeFor(stock), Hardness, GridCurrent);

    /// <summary>The exact model of that device, for operating points.</summary>
    public TriodeModel Law(TriodeModel stock) => TubeFor(stock).Hardened(Hardness);

    /// <summary>Whether a change from <paramref name="previous" /> moves an operating point (the channel resets).</summary>
    public bool MovesOperatingPoint(in PreampSettings previous) =>
        Tube != previous.Tube
        || CathodeResistor != previous.CathodeResistor
        || Hardness != previous.Hardness
        || FollowerBias != previous.FollowerBias;

    /// <summary>Whether a stage must rebuild its devices for a change from <paramref name="previous" />.</summary>
    public bool ChangesDevices(in PreampSettings previous) =>
        Tube != previous.Tube || Hardness != previous.Hardness || GridCurrent != previous.GridCurrent;

    /// <summary>
    ///     The triode a stage built with <paramref name="stock" /> runs: <see cref="Tube" /> in place of a 12AX7, the
    ///     stock tube otherwise (a 12AT7 or a 12AU7 stays what it is).
    /// </summary>
    public TriodeModel TubeFor(TriodeModel stock) => Tube is { } tube && stock == TriodeModel.Ecc83 ? tube : stock;
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

    /// <summary>Whether this is a common-cathode gain stage, which <see cref="PreampSettings.CathodeResistor" /> may bias.</summary>
    public virtual bool IsGainStage => false;

    public abstract void SetSampleRate(double sampleRate);

    /// <summary>The previous stage's output resistance, in series with this stage's own input resistance.</summary>
    public abstract void SetSourceResistance(double ohms);

    /// <summary>Supply voltage as a fraction of the nominal one (power-supply sag), per sample.</summary>
    public abstract void SetSupply(double scale);

    /// <summary>Applies the knobs; recomputes the circuit only when a pot this stage owns has moved.</summary>
    public abstract void Update(in AmpControls controls);

    /// <summary>
    ///     Applies the expert settings (component values; allocation-free). A new tube or cathode resistor moves the
    ///     operating point, so the channel resets afterwards (<see cref="Reset" />).
    /// </summary>
    public virtual void Apply(in PreampSettings settings) { }

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
