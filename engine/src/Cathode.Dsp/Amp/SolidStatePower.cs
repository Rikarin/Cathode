using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A solid-state power amp: a transistor (or op-amp) voltage amplifier with global feedback driving a class-AB
///     complementary emitter-follower output stage on an unregulated split supply. The amp's closed-loop gain is set by
///     the feedback divider; its open-loop gain and dominant pole set how hard the loop holds the output; its rails sag
///     under load and the output clips a little below them.
/// </summary>
/// <param name="Name">A label, e.g. "2N3773 class AB".</param>
/// <param name="InputCap">The input coupling cap.</param>
/// <param name="InputResistor">The input's bias resistor to ground, after the cap.</param>
/// <param name="FeedbackResistor">From the output to the inverting input.</param>
/// <param name="ShuntResistor">From the inverting input to ground, through <paramref name="ShuntCap" />.</param>
/// <param name="ShuntCap">In series with the shunt resistor: the loop's gain falls to one at DC (0 for none).</param>
/// <param name="OpenLoopGain">The voltage amplifier's DC gain.</param>
/// <param name="GainBandwidth">Its gain-bandwidth product in hertz (the dominant pole is at GBW / A₀).</param>
/// <param name="Rail">Each rail's voltage at idle (the supply is symmetric).</param>
/// <param name="RailResistance">The transformer and rectifier as one resistance per rail (the supply's sag).</param>
/// <param name="Reservoir">Each rail's reservoir cap.</param>
/// <param name="Headroom">
///     How far below the rail the voltage amplifier's swing ends (its saturation and the output stage's
///     V_be).
/// </param>
/// <param name="Junctions">Base-emitter junctions per side of the output stage (2 for Darlingtons).</param>
/// <param name="EmitterResistor">Each output transistor's emitter resistor.</param>
/// <param name="IdleCurrent">The output stage's quiescent current (class AB bias).</param>
/// <param name="Load">The nominal speaker load, in ohms.</param>
/// <param name="RatedPower">Into <paramref name="Load" />, at the onset of clipping.</param>
public sealed record SolidStatePowerDesign(
    string Name,
    double InputCap,
    double InputResistor,
    double FeedbackResistor,
    double ShuntResistor,
    double ShuntCap,
    double OpenLoopGain,
    double GainBandwidth,
    double Rail,
    double RailResistance,
    double Reservoir,
    double Headroom,
    int Junctions,
    double EmitterResistor,
    double IdleCurrent,
    double Load,
    double RatedPower
) {
    /// <summary>The closed-loop gain in the midband, 1 + R_f / R_s.</summary>
    public double Gain => 1 + FeedbackResistor / ShuntResistor;

    /// <summary>The swing at which the output clips at idle rails: the block's full scale.</summary>
    public double Swing => Rail - Headroom;
}

/// <summary>
///     A <see cref="SolidStatePowerDesign" /> run sample by sample: the output voltage is the one unknown, solved by a
///     scalar Newton iteration with bracketing (the residual falls monotonically with it), with the feedback and the
///     voltage amplifier's pole in the loop, so the loop has no delay.
/// </summary>
/// <remarks>
///     <para>
///         The voltage amplifier is a one-pole integrator, y[n] = h y[n-1] + g (e[n] + e[n-1]) (trapezoidal), whose output
///         saturates smoothly between the rails less the headroom (the op-amp macro-model's limiter, unity within about
///         80 % of the swing). Each side of the output stage is a junction stack (Ebers-Moll's base-emitter diode,
///         Shockley
///         with series resistance: the emitter resistor) biased by the class-AB bias voltage, so the crossover region and
///         the stage's current into the load are physical; the feedback reduces their distortion by the loop gain, as in
///         the amp. The load is the nominal resistance: with the loop's gain the output impedance is a few milliohms, so
///         the speaker's impedance curve barely moves the voltage (unlike a tube amp's).
///     </para>
///     <para>
///         The rails are reservoir caps charged from the idle voltage through the rail resistance and drained by each
///         side's current, integrated explicitly after each sample (their time constant is milliseconds).
///     </para>
/// </remarks>
public sealed class SolidStatePowerAmp {
    const int MaxIterations = 64;

    readonly SolidStatePowerDesign design;
    readonly Diode junction;
    readonly double bias; // the class-AB bias voltage per side (half the bias spreader's)
    readonly double load;
    FirstOrder input;
    FirstOrder feedback; // β(s): the fraction of the output at the inverting input
    double sampleRate;
    double hold;
    double gain;
    double y;
    double previousError;
    double output;
    double previousOutput;
    double positive;
    double negative;
    double sagScale = 1;
    double variac = 1;
    double reservoir;
    long iterations;
    long samples;
    long unconverged;
    long nonFinite;
    int mostIterations;

    public SolidStatePowerDesign Design => design;

    /// <summary>The rails at the last sample.</summary>
    public (double Positive, double Negative) Rails => (positive, negative);

    public long Samples => samples;

    public long Unconverged => unconverged;

    public long NonFinite => nonFinite;

    public int MostIterations => mostIterations;

    public double MeanIterations => samples > 0 ? (double)iterations / samples : 0;

    public SolidStatePowerAmp(SolidStatePowerDesign design, double sampleRate) {
        this.design = design;
        load = design.Load;
        reservoir = design.Reservoir;
        var model = new DiodeModel(1e-12, 1);
        junction = new(model, design.Junctions, design.EmitterResistor);
        // The bias that sets the idle current through each side's junctions and emitter resistor.
        bias = design.Junctions * DiodeModel.ThermalVoltage * Math.Log(design.IdleCurrent / model.SaturationCurrent + 1)
            + design.IdleCurrent * design.EmitterResistor;
        SetSampleRate(sampleRate);
        Reset();
    }

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        input.Design(0, design.InputCap * design.InputResistor, 1, design.InputCap * design.InputResistor, sampleRate);
        var (rf, rs, c) = (design.FeedbackResistor, design.ShuntResistor, design.ShuntCap);
        if (c > 0) {
            feedback.Design(1, rs * c, 1, (rs + rf) * c, sampleRate);
        } else {
            feedback.Design(rs / (rs + rf), 0, 1, 0, sampleRate);
        }

        var pole = 2 * Math.PI * design.GainBandwidth / design.OpenLoopGain;
        var k = pole / (2 * sampleRate);
        hold = (1 - k) / (1 + k);
        gain = k * design.OpenLoopGain / (1 + k);
    }

    /// <summary>The knobs that reach a solid-state power amp: sag (0.5 stock) scales the rails' source resistance.</summary>
    public void Configure(in PowerControls controls) => sagScale = Math.Pow(4, 2 * controls.Sag - 1);

    /// <summary>
    ///     The settings that reach it: the mains (the rails follow the variac) and the reservoirs (the B+ time constant).
    ///     The tube settings do nothing here.
    /// </summary>
    public void Apply(in PowerSettings settings) {
        variac = settings.Variac;
        reservoir = design.Reservoir * settings.TimeConstant;
    }

    public void Reset() {
        input.Reset();
        feedback.Reset();
        y = 0;
        previousError = 0;
        output = 0;
        previousOutput = 0;
        positive = design.Rail * variac;
        negative = design.Rail * variac;
    }

    /// <summary>One sample: the voltage amplifier's input volts in, the speaker volts out.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        var drive = input.Process(x);
        var b0 = feedback.B0;
        var fed0 = feedback.Peek(0);
        var high = positive - design.Headroom;
        var low = -(negative - design.Headroom);
        var centre = 0.5 * (high + low);
        var half = Math.Max(0.5 * (high - low), 1e-3);

        // The output can neither pass the rails nor the far side's: bracket it there.
        var (lo, hi) = (-negative, positive);
        var v = Math.Clamp(2 * output - previousOutput, lo, hi); // the last two samples extrapolated
        var converged = false;
        var n = 0;
        Span<double> port = stackalloc double[1];
        Span<double> current = stackalloc double[1];
        Span<double> slope = stackalloc double[1];
        for (; n < MaxIterations; n++) {
            var error = drive - (fed0 + b0 * v);
            var internalOutput = hold * y + gain * (error + previousError);
            var vd = centre + Limit(internalOutput - centre, half, out var limiterSlope);
            var dvd = -limiterSlope * gain * b0;
            port[0] = vd + bias - v;
            junction.Evaluate(port, current, slope, 1);
            var (iUp, gUp) = (current[0], slope[0]);
            port[0] = v - vd + bias;
            junction.Evaluate(port, current, slope, 1);
            var (iDown, gDown) = (current[0], slope[0]);
            var residual = iUp - iDown - v / load;
            var derivative = gUp * (dvd - 1) - gDown * (1 - dvd) - 1 / load;
            if (residual > 0) {
                lo = v;
            } else {
                hi = v;
            }

            // Newton, inside the bracket; past a dozen iterations (cycling across the limiter's or the crossover's knee,
            // one bracket end never moving) plain bisection, which closes the bracket within the iteration limit.
            var next = v - residual / derivative;
            if (!(next > lo && next < hi) || n >= 12) {
                next = 0.5 * (lo + hi);
            }

            // A tenth of a microvolt: the junctions' closed form (Wright ω) is no more exact than that.
            if (Math.Abs(next - v) <= 1e-7 + 1e-9 * Math.Abs(v) || hi - lo <= 1e-7) {
                v = next;
                converged = true;
                break;
            }

            v = next;
        }

        samples++;
        iterations += n + 1;
        mostIterations = Math.Max(mostIterations, n + 1);
        if (!converged) {
            unconverged++;
        }

        if (!double.IsFinite(v)) {
            nonFinite++;
            v = 0;
        }

        // Commit: the loop's states, then the rails from each side's current. After a sample on a rail the
        // integrator's state is held a little beyond it, so that it comes off the rail at once (as in the op-amp).
        var e = drive - feedback.Process(v);
        var settled = hold * y + gain * (e + previousError);
        var driven = centre + Limit(settled - centre, half, out _);
        y = Math.Clamp(settled, centre - 1.25 * half, centre + 1.25 * half);
        previousError = e;
        previousOutput = output;
        output = v;
        port[0] = driven + bias - v;
        junction.Evaluate(port, current, slope, 1);
        var up = current[0];
        port[0] = v - driven + bias;
        junction.Evaluate(port, current, slope, 1);
        var down = current[0];
        var resistance = design.RailResistance * sagScale;
        var idle = design.Rail * variac;
        var step = 1 / (sampleRate * reservoir);
        positive += step * ((idle - positive) / resistance - up);
        negative += step * ((idle - negative) / resistance - down);
        return v;
    }

    public void FlushDenormals() {
        input.FlushDenormals();
        feedback.FlushDenormals();
        y = Flush.Value(y);
        previousError = Flush.Value(previousError);
    }

    /// <summary>The smooth limiter L s(x / L), s(x) = x / (1 + x⁸)^(1/8), and its slope (as the op-amp macro-model's).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Limit(double x, double limit, out double slope) {
        var u = x / limit;
        if (!(Math.Abs(u) < 1e30)) {
            slope = 0;
            return double.IsNaN(u) ? u : Math.CopySign(limit, u);
        }

        var u2 = u * u;
        var u4 = u2 * u2;
        var p = 1 + u4 * u4;
        var root = Math.Sqrt(Math.Sqrt(Math.Sqrt(p)));
        slope = 1 / (root * p);
        return x / root;
    }
}
