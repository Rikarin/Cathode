using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A noise gate built into a preamp stage (<see cref="NetworkStageDesign.Gate" />), as Engl's amps have on their
///     lead channels: a detector on the stage's input, a comparator against a threshold pot, and a JFET that shunts
///     the stage's output to ground through a resistor while the signal is below the threshold. The stage the design
///     builds is wrapped in <see cref="NoiseGateStage" />. The threshold pot is the Circuit Amp's Cut knob
///     (<see cref="AmpControls.Cut" />), which a model with a gate names on its panel.
/// </summary>
/// <param name="DetectorGain">
///     The rectified detector voltage per volt of the stage's input (its AC part): the divider into the detector times
///     the detector amplifier's gain.
/// </param>
/// <param name="DetectorCorner">The detector's coupling, as a high-pass corner in hertz.</param>
/// <param name="DiodeDrop">The rectifier diode's drop, volts.</param>
/// <param name="Ceiling">The most the rectified voltage reaches (the detector amplifier's output swing), volts.</param>
/// <param name="Release">The time constant with which the held detector voltage falls, seconds.</param>
/// <param name="Supply">The supply the threshold divider stands on, volts.</param>
/// <param name="Reference">The detector's reference rail, which the comparator's other input rests at, volts.</param>
/// <param name="Threshold">The threshold pot, a rheostat from the supply that is all in at rotation 0.</param>
/// <param name="Series">The resistor between the pot and the comparator's input, ohms.</param>
/// <param name="Lower">The resistor from the comparator's input to ground, ohms.</param>
/// <param name="Shunt">The resistor in series with the JFET, from the stage's output to ground, ohms.</param>
/// <param name="OnResistance">The JFET's channel resistance with its gate at its source, ohms.</param>
/// <param name="PinchOff">The gate voltage at which the JFET's channel closes, volts.</param>
/// <param name="GateHigh">The JFET's gate voltage while the comparator holds the gate open, volts.</param>
/// <param name="GateTime">The time constant of the JFET's gate network, seconds.</param>
public sealed record NoiseGateDesign(
    double DetectorGain,
    double DetectorCorner,
    double DiodeDrop,
    double Ceiling,
    double Release,
    double Supply,
    double Reference,
    Pot Threshold,
    double Series,
    double Lower,
    double Shunt,
    double OnResistance,
    double PinchOff,
    double GateHigh,
    double GateTime
) {
    /// <summary>
    ///     The rectified detector voltage the signal must pass for the gate to open with the threshold pot at
    ///     <paramref name="rotation" />: the divider's voltage above the reference. Zero or less where the comparator
    ///     is high whatever the signal, so the gate never closes (the pot's low end).
    /// </summary>
    public double ThresholdAt(double rotation) {
        var pot = Threshold.Ohms * (1 - Threshold.Fraction(Math.Clamp(rotation, 0, 1)));
        return Supply * Lower / (Lower + Series + pot) - Reference;
    }

    /// <summary>
    ///     The gain on a signal at a node of <paramref name="sourceResistance" /> ohms with the JFET's gate at
    ///     <paramref name="gate" /> volts: 1 with the channel closed, the shunt's divider with it open.
    /// </summary>
    public double GainAt(double gate, double sourceResistance) {
        if (gate >= PinchOff) {
            return 1;
        }

        var shunt = Shunt + OnResistance / (1 - Math.Max(gate, 0) / PinchOff);
        return shunt / (shunt + sourceResistance);
    }
}

/// <summary>
///     A stage with a noise gate on it (<see cref="NoiseGateDesign" />). The detector follows the stage's input: its AC
///     part, full-wave rectified, less a diode drop, held on a capacitor that the detector charges at once and that
///     falls with the release time. While the held voltage is above the threshold the comparator holds the JFET's gate
///     high and the stage passes untouched; below it the gate falls with its network's time constant and the JFET
///     shunts the stage's output, modelled as a gain on its signal (the output less its rest). With the threshold pot
///     at its low end the comparator never falls and the wrapper passes the stage through.
/// </summary>
public sealed class NoiseGateStage : AmpStage {
    readonly AmpStage inner;
    double threshold;
    double coupling; // per-sample coefficient of the detector's high-pass
    double release; // per-sample factor of the held voltage
    double gateStep; // per-sample coefficient of the JFET's gate network
    double restInput;
    double held;
    double gate;
    bool idle = true;

    public NoiseGateDesign Design { get; }

    /// <summary>The stage the gate sits on.</summary>
    public AmpStage Inner => inner;

    /// <summary>
    ///     The JFET's gate voltage at the last sample: at <see cref="NoiseGateDesign.GateHigh" /> while open.
    /// </summary>
    public double Gate => gate;

    public override double OutputResistance => inner.OutputResistance;

    public override double RestOutput => inner.RestOutput;

    public override NewtonSolver? Newton => inner.Newton;

    public override SolverFaults Faults => inner.Faults;

    public override bool IsGainStage => inner.IsGainStage;

    public override long OperatingPointSteps => inner.OperatingPointSteps;

    internal NoiseGateStage(NoiseGateDesign design, AmpStage inner, double sampleRate) {
        Design = design;
        this.inner = inner;
        gate = design.GateHigh;
        Times(sampleRate);
    }

    public override void SetSampleRate(double sampleRate) {
        inner.SetSampleRate(sampleRate);
        Times(sampleRate);
    }

    public override void SetSourceResistance(double ohms) => inner.SetSourceResistance(ohms);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) => inner.SetSupply(scale);

    public override void Update(in AmpControls controls) {
        inner.Update(controls);
        threshold = Design.ThresholdAt(controls.Cut);
        if (threshold > 0) {
            idle = false;
        }
    }

    public override void Apply(in PreampSettings settings) => inner.Apply(settings);

    public override void Retune(double restInput) => inner.Retune(restInput);

    public override void Reset(double restInput) {
        inner.Reset(restInput);
        this.restInput = restInput;
        held = 0;
        // A reset amp is silent: the gate rests where silence leaves it.
        gate = threshold > 0 ? 0 : Design.GateHigh;
        idle = threshold <= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        var y = inner.Tick(input);
        if (idle) {
            return y;
        }

        restInput += coupling * (input - restInput);
        var rectified = Math.Min(Math.Abs(input - restInput) * Design.DetectorGain - Design.DiodeDrop, Design.Ceiling);
        held = Math.Max(held * release, rectified);
        gate += gateStep * ((held > threshold ? Design.GateHigh : 0) - gate);
        if (gate >= Design.PinchOff) {
            // Open, and staying so with the threshold at its low end: nothing left to follow.
            idle = threshold <= 0 && gate >= 0.99 * Design.GateHigh;
            return y;
        }

        var rest = inner.RestOutput;
        return rest + Design.GainAt(gate, inner.OutputResistance) * (y - rest);
    }

    public override void FlushDenormals() => inner.FlushDenormals();

    void Times(double sampleRate) {
        coupling = 1 - Math.Exp(-2 * Math.PI * Design.DetectorCorner / sampleRate);
        release = Math.Exp(-1 / (Design.Release * sampleRate));
        gateStep = 1 - Math.Exp(-1 / (Design.GateTime * sampleRate));
    }
}
