using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A tremolo that lives in a preamp stage (<see cref="NetworkStageDesign.Tremolo" />): the stage the design builds
///     is wrapped in its runtime. The Circuit Amp's <c>tremrate</c> (hertz) and <c>tremdepth</c> reach it through
///     <see cref="AmpControls.TremoloRate" /> and <see cref="AmpControls.TremoloDepth" />; at depth 0 the wrapper
///     passes the stage through untouched.
/// </summary>
public abstract record StageTremoloDesign {
    /// <summary>
    ///     Wraps <paramref name="stage" />, built from <paramref name="design" />, in this tremolo's runtime.
    /// </summary>
    internal abstract AmpStage Wrap(NetworkStageDesign design, AmpStage stage, double sampleRate);
}

/// <summary>
///     A cadmium-sulphide photocell (LDR) lit by a lamp: its resistance falls with the light as a power law,
///     <c>R = Lit × (I / ReferenceCurrent)^−Exponent</c> in parallel with <paramref name="Dark" />, and it follows the
///     light with an <paramref name="Attack" /> time constant as it brightens and a slower <paramref name="Release" />
///     as it darkens (the cell's conductance, not the lamp, is slow). The defaults are typical of the neon-lamp cells
///     in Fender's "roach" (a guess: no data sheet for it is published): about 5M dark, a few kilohms with the lamp at
///     1 mA, 5 ms and 35 ms.
/// </summary>
public sealed record LdrDesign(
    double Dark = 5e6,
    double Lit = 4e3,
    double ReferenceCurrent = 1e-3,
    double Exponent = 0.8,
    double Attack = 0.005,
    double Release = 0.035
) {
    /// <summary>The cell's resistance with a steady lamp current of <paramref name="amperes" />.</summary>
    public double Resistance(double amperes) {
        var light = amperes > 0 ? Math.Pow(amperes / ReferenceCurrent, Exponent) : 0;
        return 1 / (1 / Dark + light / Lit);
    }
}

/// <summary>
///     Fender's optical vibrato (the AB763 and AA763 amps, the Vibro-King): the oscillator's second ladder node drives
///     a 12AX7 half whose plate current lights a neon lamp (from <paramref name="DriverSupply" /> through
///     <paramref name="LampResistor" />, <paramref name="LampShunt" /> across it), and the lamp's photocell, from the
///     <paramref name="Intensity" /> pot's wiper to ground, shunts the signal at the node the pot's top hangs on
///     (<paramref name="NodeResistance" />: that node's Thévenin resistance without the pot; 0 to take the stage's own
///     output resistance, less the pot's track, in parallel with <paramref name="DownstreamLoad" />). The stage's
///     netlist must carry the pot's track (<c>Intensity.Ohms</c>) from that node to ground.
///     <paramref name="Channel" />, when set, is the switch under which the signal passes that node (the Vibrato
///     channel).
/// </summary>
/// <remarks>
///     <para>
///         <b>The lamp.</b> The driver's cathode (<paramref name="DriverCathode" /> with a large cap) charges up to
///         near the positive peaks of its grid, so it conducts in pulses near each peak (class C). Its bias is solved
///         where the pulses' mean current holds the cathode, each point of the pulse on the tube's Koren curves with
///         the lamp lit (its plate at the supply less <paramref name="LampVoltage" /> and the resistor's drop); the
///         drawings' 17 V at the AB763 driver's cathode is that balance. The lamp's current is the plate current less
///         what the shunt carries. All per Speed rotation, on the oscillator's table.
///     </para>
///     <para>
///         <b>The shunt.</b> With the photocell at R, the node sees <c>R_top + R_bottom ‖ R</c> to ground (the pot's
///         halves). The stage's netlist carries the pot's whole track from the node to ground (what it is with the
///         intensity at 0, the wiper at ground, or with the cell dark, to within the dark cell's 1 %), so the runtime
///         scales the node's signal by the shunt it actually sees against that track:
///         <c>(1 + Z / R_track) / (1 + Z / R_shunt)</c>, Z the node's resistance without the pot (exact for a resistive
///         node; the coupling caps around it are short at audio). At intensity 0 it is exactly 1 and the stage plays
///         untouched. The cell runs at the control rate (every <see cref="OpticalTremoloStage.ControlPeriod" />
///         samples), the gain ramped between.
///     </para>
/// </remarks>
public sealed record OpticalTremoloDesign(
    PhaseShiftOscillatorDesign Oscillator,
    double DriverSupply,
    double DriverCathode,
    double LampResistor,
    Pot Intensity,
    double NodeResistance,
    SwitchRole? Channel = null,
    double LampVoltage = 60,
    double LampShunt = 10e6,
    LdrDesign? Ldr = null,
    TriodeModel? DriverTube = null,
    double DownstreamLoad = double.PositiveInfinity
) : StageTremoloDesign {
    static readonly ConditionalWeakTable<OpticalTremoloDesign, LampTable> Tables = new();

    static readonly LdrDesign Roach = new();

    /// <summary>The photocell.</summary>
    public LdrDesign Cell => Ldr ?? Roach;

    /// <summary>
    ///     The lamp's current over a cycle and the driver's bias, per Speed rotation (built on first use).
    /// </summary>
    internal LampTable Lamp => Tables.GetValue(this, static d => new(d));

    /// <summary>The driver's cathode voltage (its class-C bias) at Speed rotation <paramref name="speed" />.</summary>
    public double DriverBias(double speed) =>
        OscillatorTable.At(Lamp.Bias, speed * (PhaseShiftOscillatorDesign.Points - 1));

    /// <summary>The lamp's peak current at Speed rotation <paramref name="speed" />, amperes.</summary>
    public double PeakLampCurrent(double speed) {
        var lamp = Lamp;
        var p = (int)Math.Round(speed * (PhaseShiftOscillatorDesign.Points - 1));
        var peak = 0.0;
        for (var n = 0; n < PhaseShiftOscillatorDesign.Samples; n++) {
            peak = Math.Max(peak, lamp.Current[p * PhaseShiftOscillatorDesign.Samples + n]);
        }

        return peak;
    }

    /// <summary>
    ///     The signal's gain at the shunted node with the photocell at <paramref name="cell" /> ohms and the Intensity
    ///     pot at <paramref name="intensity" />, against the netlist's pot track (see the remarks);
    ///     <paramref name="node" /> is the node's resistance without the pot.
    /// </summary>
    public double Gain(double cell, double intensity, double node) {
        var track = Intensity.Ohms;
        var fraction = Intensity.Fraction(intensity);
        var upper = (1 - fraction) * track;
        var lower = fraction * track;
        var shunt = upper + lower * cell / (lower + cell);
        return (1 + node / track) / (1 + node / shunt);
    }

    internal override AmpStage Wrap(NetworkStageDesign design, AmpStage stage, double sampleRate) =>
        new OpticalTremoloStage(this, stage, sampleRate);
}

/// <summary>
///     An <see cref="OpticalTremoloDesign" />'s lamp current over one cycle and its driver's bias, per Speed
///     rotation.
/// </summary>
sealed class LampTable {
    /// <summary><c>[rotation * Samples + n]</c>, amperes.</summary>
    public ReadOnlySpan<double> Current => current;

    /// <summary>The cell's light drive, <c>(I / I_ref)^γ</c>, laid out as <see cref="Current" />.</summary>
    public ReadOnlySpan<double> Light => light;

    /// <summary>The driver's grid bias per rotation, volts.</summary>
    public ReadOnlySpan<double> Bias => bias;

    readonly double[] current = new double[PhaseShiftOscillatorDesign.Points * PhaseShiftOscillatorDesign.Samples];
    readonly double[] light = new double[PhaseShiftOscillatorDesign.Points * PhaseShiftOscillatorDesign.Samples];
    readonly double[] bias = new double[PhaseShiftOscillatorDesign.Points];

    public LampTable(OpticalTremoloDesign design) {
        var oscillator = design.Oscillator.Table;
        var triode = new Triode(design.DriverTube ?? TriodeModel.Ecc83);
        var cell = design.Cell;
        const int samples = PhaseShiftOscillatorDesign.Samples;
        Span<double> pulse = stackalloc double[samples];
        for (var p = 0; p < PhaseShiftOscillatorDesign.Points; p++) {
            var swing = oscillator.Amplitudes[p] * oscillator.Second[p].Magnitude;

            // The cathode where the pulses' mean current holds it.
            double low = 0, high = swing + 10;
            for (var i = 0; i < 40; i++) {
                var middle = 0.5 * (low + high);
                if (Pulse(triode, design, swing, middle, pulse) * design.DriverCathode > middle) {
                    low = middle;
                } else {
                    high = middle;
                }
            }

            bias[p] = 0.5 * (low + high);
            Pulse(triode, design, swing, bias[p], pulse);
            for (var n = 0; n < samples; n++) {
                // What the shunt across the lamp carries (the lamp's voltage plus the resistor's drop) stays dark.
                var lamp = Math.Max(
                    pulse[n] - (design.LampVoltage + pulse[n] * design.LampResistor) / design.LampShunt,
                    0
                );
                current[p * samples + n] = lamp;
                light[p * samples + n] = lamp > 0 ? Math.Pow(lamp / cell.ReferenceCurrent, cell.Exponent) : 0;
            }
        }
    }

    /// <summary>
    ///     The driver's plate current over a cycle (into <paramref name="current" />) at cathode voltage
    ///     <paramref name="cathode" />; returns its mean.
    /// </summary>
    static double Pulse(
        Triode triode,
        OpticalTremoloDesign design,
        double swing,
        double cathode,
        Span<double> current
    ) {
        var sum = 0.0;
        var most = (design.DriverSupply - design.LampVoltage - cathode) / design.LampResistor;
        for (var n = 0; n < current.Length; n++) {
            var grid = swing * Math.Sin(2 * Math.PI * n / current.Length) - cathode;
            double low = 0, high = Math.Max(most, 0);
            for (var i = 0; i < 40; i++) {
                var middle = 0.5 * (low + high);
                var vpk = design.DriverSupply - design.LampVoltage - middle * design.LampResistor - cathode;
                if (OperatingPoint.PlateCurrent(triode, grid, vpk) > middle) {
                    low = middle;
                } else {
                    high = middle;
                }
            }

            current[n] = 0.5 * (low + high);
            sum += current[n];
        }

        return sum / current.Length;
    }
}

/// <summary>
///     A stage with an <see cref="OpticalTremoloDesign" /> on its output: every <see cref="ControlPeriod" /> samples
///     the lamp's light (from the table, at the oscillator's phase) moves the photocell's conductance with its attack
///     or release, and the shunt's gain on the stage's signal (its output less its rest) is ramped there. Passes the
///     stage through while the depth is 0 or its channel is not playing.
/// </summary>
public sealed class OpticalTremoloStage : AmpStage {
    /// <summary>Samples per photocell update.</summary>
    public const int ControlPeriod = 32;

    readonly AmpStage inner;
    readonly LampTable lamp;
    readonly OscillatorTable table;
    readonly double[] light = new double[PhaseShiftOscillatorDesign.Samples];
    double sampleRate;
    double phase; // cycles
    double phaseStep; // cycles per control period
    double attack;
    double release;
    double cell; // the light the cell has adapted to
    double gain = 1;
    double step;
    double intensity;
    double rate = double.NaN;
    double depth;
    int countdown;
    bool channelOn = true;
    bool engaged;

    public OpticalTremoloDesign Design { get; }

    /// <summary>The oscillator's rate, hertz.</summary>
    public double Frequency { get; private set; }

    /// <summary>The photocell's resistance at the last update, ohms.</summary>
    public double CellResistance => 1 / (1 / Design.Cell.Dark + cell / Design.Cell.Lit);

    /// <summary>The gain on the signal at the last sample.</summary>
    public double Gain => gain;

    /// <summary>The stage the tremolo sits on.</summary>
    public AmpStage Inner => inner;

    public override double OutputResistance => inner.OutputResistance;

    public override double RestOutput => inner.RestOutput;

    public override NewtonSolver? Newton => inner.Newton;

    public override bool IsGainStage => inner.IsGainStage;

    internal OpticalTremoloStage(OpticalTremoloDesign design, AmpStage inner, double sampleRate) {
        Design = design;
        this.inner = inner;
        this.sampleRate = sampleRate;
        lamp = design.Lamp;
        table = design.Oscillator.Table;
        Times();
    }

    public override void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        inner.SetSampleRate(sampleRate);
        Times();
    }

    public override void SetSourceResistance(double ohms) => inner.SetSourceResistance(ohms);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) => inner.SetSupply(scale);

    public override void Update(in AmpControls controls) {
        inner.Update(controls);
        channelOn = Design.Channel is not { } role || controls.IsOn(role);
        depth = Math.Clamp(controls.TremoloDepth, 0, 1);
        intensity = depth;
        if (depth > 0 && channelOn && !engaged) {
            engaged = true;
            countdown = 0;
        }

        if (controls.TremoloRate != rate) {
            rate = controls.TremoloRate;
            var position = table.Position(rate);
            Frequency = OscillatorTable.At(table.Rates, position);
            var i = Math.Min((int)position, PhaseShiftOscillatorDesign.Points - 2);
            var t = position - i;
            const int samples = PhaseShiftOscillatorDesign.Samples;
            for (var n = 0; n < samples; n++) {
                light[n] = lamp.Light[i * samples + n]
                    + t * (lamp.Light[(i + 1) * samples + n] - lamp.Light[i * samples + n]);
            }

            Times();
        }
    }

    public override void Apply(in PreampSettings settings) => inner.Apply(settings);

    public override void Retune(double restInput) => inner.Retune(restInput);

    public override void Reset(double restInput) {
        inner.Reset(restInput);
        phase = 0;
        cell = 0;
        gain = 1;
        step = 0;
        countdown = 0;
        engaged = depth > 0 && channelOn;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        var y = inner.Tick(input);
        if (!engaged) {
            return y;
        }

        if (--countdown < 0) {
            Control();
            if (!engaged) {
                return y;
            }
        }

        gain += step;
        var rest = inner.RestOutput;
        return rest + gain * (y - rest);
    }

    public override void FlushDenormals() => inner.FlushDenormals();

    /// <summary>One photocell update: the light at the phase, the cell's response, the gain's next ramp.</summary>
    void Control() {
        countdown = ControlPeriod - 1;
        var from = gain; // the ramp has reached its target (to rounding): restart from where it is
        var samples = light.Length;
        var position = phase * samples;
        var n = (int)position % samples;
        var t = position - Math.Floor(position);
        var drive = depth > 0 ? light[n] + t * (light[(n + 1) % samples] - light[n]) : 0;
        cell += (drive - cell) * (drive > cell ? attack : release);
        phase += phaseStep;
        if (phase >= 1) {
            phase -= 1;
        }

        double node;
        if (Design.NodeResistance > 0) {
            node = Design.NodeResistance;
        } else {
            // The stage's output resistance includes the pot's track: take it out, and add what loads the node beyond.
            var without = 1 / Math.Max(inner.OutputResistance, 1) - 1 / Design.Intensity.Ohms;
            node = 1 / (Math.Max(without, 1e-12) + 1 / Design.DownstreamLoad);
        }

        var to = channelOn ? Design.Gain(CellResistance, intensity, node) : 1;
        if ((depth == 0 || !channelOn) && to == 1 && Math.Abs(from - 1) < 1e-9) {
            // Off (or its channel idle) and settled: hand the stage back untouched.
            engaged = false;
            gain = 1;
            step = 0;
            cell = 0;
            phase = 0;
            return;
        }

        gain = from;
        step = (to - from) / ControlPeriod;
    }

    void Times() {
        var period = ControlPeriod / sampleRate;
        attack = 1 - Math.Exp(-period / Design.Cell.Attack);
        release = 1 - Math.Exp(-period / Design.Cell.Release);
        phaseStep = Frequency * period;
    }
}

/// <summary>
///     A tremolo whose oscillator is a tube of the stage itself (the Marshall 18 W's V3B, which shares its plate supply
///     and cathode with the Tremolo channel's V3A): <paramref name="Modulated" /> is the stage's netlist with that
///     tube's grid on a source (supply element <paramref name="Source" />) instead of grounded, and the source swings
///     with the oscillator's grid (<paramref name="Oscillator" />, its swing scaled by the depth, its positive peaks
///     clamped by grid current), so the network itself works out what the oscillating tube's current does to the
///     channel.
/// </summary>
/// <remarks>
///     At depth 0 the stage the design builds plays (the footswitch's grounded grid), bit for bit. Engaging runs the
///     modulated netlist, reset at the same operating point, crossfaded in over <see cref="CrossfadeTime" />; the swing
///     builds up over <see cref="SmoothingTime" /> as the oscillation does. Returning to depth 0, the swing dies away
///     and the plain netlist, reset, is crossfaded back.
/// </remarks>
public sealed record GridDriveTremoloDesign(
    NetworkStageDesign Modulated,
    int Source,
    PhaseShiftOscillatorDesign Oscillator
) : StageTremoloDesign {
    public const double CrossfadeTime = 0.01;
    public const double SmoothingTime = 0.03;

    internal override AmpStage Wrap(NetworkStageDesign design, AmpStage stage, double sampleRate) =>
        new GridDriveTremoloStage(this, stage, Modulated.Create(sampleRate), sampleRate);
}

/// <summary>The runtime of a <see cref="GridDriveTremoloDesign" />.</summary>
public sealed class GridDriveTremoloStage : PairedAmpStage {
    readonly AmpStage plain;
    readonly AmpStage modulated;
    readonly IRailModulation rails;
    readonly OscillatorTable table;
    double sampleRate;
    double restInput;
    double smoothing;
    double fadeStep;
    double fade; // 0: plain, 1: modulated
    double swing; // the grid's smoothed peak swing
    double target;
    double sine;
    double cosine = 1;
    double stepSine;
    double stepCosine = 1;
    double rate = double.NaN;
    double depth;
    int direction; // +1 fading to the modulated netlist, -1 back, 0 settled

    public GridDriveTremoloDesign Design { get; }

    /// <summary>The oscillator's rate, hertz.</summary>
    public double Frequency { get; private set; }

    /// <summary>Whether the modulated netlist is playing (or fading).</summary>
    public bool Modulating => fade > 0 || direction > 0;

    /// <summary>The oscillating grid's swing at the last sample, peak volts.</summary>
    public double Swing => swing;

    public override double OutputResistance => plain.OutputResistance;

    public override double RestOutput => plain.RestOutput;

    public override NewtonSolver? Newton => Modulating ? modulated.Newton : plain.Newton;

    public override bool IsGainStage => plain.IsGainStage;

    internal GridDriveTremoloStage(
        GridDriveTremoloDesign design,
        AmpStage plain,
        AmpStage modulated,
        double sampleRate
    ) : base(plain, modulated) {
        Design = design;
        this.plain = plain;
        this.modulated = modulated;
        rails = (IRailModulation)modulated;
        table = design.Oscillator.Table;
        this.sampleRate = sampleRate;
        Step();
    }

    protected override void OnSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        Step();
    }

    protected override void OnUpdate(in AmpControls controls) {
        depth = Math.Clamp(controls.TremoloDepth, 0, 1);
        var position = table.Position(controls.TremoloRate);
        if (controls.TremoloRate != rate) {
            rate = controls.TremoloRate;
            Frequency = OscillatorTable.At(table.Rates, position);
            Step();
        }

        target = depth * OscillatorTable.At(table.Amplitudes, position);
        if (depth > 0 && direction < 0) {
            direction = 1; // re-engaged while fading out: the modulated netlist is still running
        } else if (depth > 0 && fade == 0 && direction == 0) {
            // Engage: the modulated netlist at the operating point, faded in.
            modulated.Reset(restInput);
            direction = 1;
        }
    }

    public override void Retune(double restInput) {
        this.restInput = restInput;
        plain.Retune(restInput);
        modulated.Retune(restInput);
    }

    public override void Reset(double restInput) {
        this.restInput = restInput;
        plain.Reset(restInput);
        modulated.Reset(restInput);
        sine = 0;
        cosine = 1;
        swing = target;
        fade = depth > 0 ? 1 : 0;
        direction = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        if (fade == 0 && direction == 0) {
            return plain.Tick(input);
        }

        // The oscillating grid: its swing, clamped at 0 V grid-to-cathode by grid current beyond the bias.
        swing += smoothing * (target - swing);
        var s = sine * stepCosine + cosine * stepSine;
        var c = cosine * stepCosine - sine * stepSine;
        var norm = 1.5 - 0.5 * (s * s + c * c);
        sine = s * norm;
        cosine = c * norm;
        rails.OffsetRail(Design.Source, swing * sine - Math.Max(swing - table.Bias, 0));
        var wet = modulated.Tick(input);
        if (direction == 0) {
            if (depth == 0 && swing < 1e-4) {
                // Died away: back to the plain netlist.
                plain.Reset(restInput);
                direction = -1;
            }

            return wet;
        }

        var dry = plain.Tick(input);
        fade = Math.Clamp(fade + direction * fadeStep, 0, 1);
        if (fade is 0 or 1) {
            direction = 0;
            if (fade == 0) {
                swing = 0;
                sine = 0;
                cosine = 1;
            }
        }

        return dry + fade * (wet - dry);
    }

    public override void FlushDenormals() {
        plain.FlushDenormals();
        modulated.FlushDenormals();
    }

    void Step() {
        (stepSine, stepCosine) = Math.SinCos(2 * Math.PI * Frequency / sampleRate);
        smoothing = 1 - Math.Exp(-1 / (GridDriveTremoloDesign.SmoothingTime * sampleRate));
        fadeStep = 1 / (GridDriveTremoloDesign.CrossfadeTime * sampleRate);
    }
}
