using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>Macro-model parameters of an op-amp; see <see cref="OpAmp" />.</summary>
/// <param name="OpenLoopGain">DC open-loop gain A₀ (V/V).</param>
/// <param name="GainBandwidth">Gain-bandwidth product in Hz: the dominant pole sits at GBW / A₀.</param>
/// <param name="SlewRate">Maximum output slope in V/s.</param>
/// <param name="InputResistance">Differential input resistance in ohms.</param>
/// <param name="OutputResistance">Open-loop output resistance in ohms.</param>
/// <param name="Headroom">How far below each supply rail the output saturates, in volts.</param>
/// <remarks>Typical datasheet values; the LM308 is the RAT's, compensated with 30 pF.</remarks>
public readonly record struct OpAmpModel(
    double OpenLoopGain,
    double GainBandwidth,
    double SlewRate,
    double InputResistance,
    double OutputResistance,
    double Headroom
) {
    /// <summary>TL072 (JFET input): 106 dB, 3 MHz, 13 V/µs, 1.5 V from the rails.</summary>
    public static OpAmpModel TL072 { get; } = new(200e3, 3e6, 13e6, 1e12, 100, 1.5);

    /// <summary>JRC4558 (the Tube Screamer's): 100 dB, 3 MHz, 1 V/µs, 1 V from the rails.</summary>
    public static OpAmpModel JRC4558 { get; } = new(100e3, 3e6, 1e6, 5e6, 75, 1);

    /// <summary>LM308 with 30 pF compensation (the RAT's): 110 dB, 1 MHz, 0.3 V/µs, 1 V from the rails.</summary>
    public static OpAmpModel LM308 { get; } = new(300e3, 1e6, 0.3e6, 40e6, 100, 1);

    /// <summary>LM741 (the Distortion+'s): 106 dB, 1 MHz, 0.5 V/µs, 2 V from the rails.</summary>
    public static OpAmpModel LM741 { get; } = new(200e3, 1e6, 0.5e6, 2e6, 75, 2);
}

/// <summary>
///     An op-amp macro-model as a two-port <see cref="INonlinearDevice" />: port 0 is the differential input
///     (non-inverting, inverting), port 1 the output against the reference its rails are measured from (declare them
///     with
///     <see cref="WdfNetlist.OpAmp(int, int, int, int)" />). It has memory: the dominant pole, so it must sit at the
///     root of an <see cref="RTypeRoot{TPorts,TDevice}" />, which calls <see cref="Commit" /> every sample.
/// </summary>
/// <remarks>
///     <para>
///         The internal output y follows the single-pole open-loop response dy/dt = ω_p (A₀ v_d - y), ω_p = 2π GBW /
///         A₀, discretised with the trapezoidal rule like every reactance of the wave digital filter: y[n] = ((1 - k)
///         y[n-1] + k A₀ (v_d[n] + v_d[n-1])) / (1 + k), k = ω_p T / 2. Its change per sample is then limited smoothly
///         to the slew rate, Δ → S Δ / √(S² + Δ²) with S = SR T, and the output saturates between the rails, v_o = m +
///         h s((y - m) / h) (m and h the centre and half-span of the swing) with s(x) = x / (1 + x⁸)^(1/8): unity gain
///         within about 80 % of the swing and a firm knee, like a real output stage, yet smooth for Newton. The output
///         is a source of resistance R_out behind v_o, and the input a resistance R_in. At audio rates the slew limit
///         only bites on large, fast edges (an LM308 at 192 kHz moves 1.6 V per sample).
///     </para>
///     <para>
///         After every sample y is clamped a little beyond the rails, so that a saturated stage recovers at once (real
///         op-amps take microseconds, not the dominant pole's tens of milliseconds). For a linear circuit (a tone
///         stack's buffer, an active filter) an ideal or finite-gain op-amp is cheaper as a
///         <see cref="WdfNetlist.Vcvs" /> inside the adaptor, with no device at all.
///     </para>
/// </remarks>
public struct OpAmp : INonlinearDevice {
    readonly double openLoopGain;
    readonly double inputConductance;
    readonly double outputConductance;
    readonly double centre;
    readonly double half;
    double slew; // volts per sample
    double gain; // ∂y/∂v_d before slew limiting: k A₀ / (1 + k)
    double hold; // (1 - k) / (1 + k)
    double previousInput;

    public OpAmpModel Model { get; }

    /// <summary>The internal output before the rails, as of the last committed sample.</summary>
    public double InternalOutput { get; private set; }

    public readonly int Ports => 2;

    /// <param name="model">Op-amp parameters.</param>
    /// <param name="negativeRail">Negative supply, relative to the output port's reference node.</param>
    /// <param name="positiveRail">Positive supply, relative to the output port's reference node.</param>
    /// <param name="sampleRate">Rate the circuit runs at.</param>
    public OpAmp(OpAmpModel model, double negativeRail, double positiveRail, double sampleRate) {
        Model = model;
        openLoopGain = model.OpenLoopGain;
        inputConductance = 1 / model.InputResistance;
        outputConductance = 1 / model.OutputResistance;
        var low = negativeRail + model.Headroom;
        var high = Math.Max(positiveRail - model.Headroom, low + 1e-3);
        centre = 0.5 * (low + high);
        half = 0.5 * (high - low);
        Configure(sampleRate);
    }

    /// <summary>Changes the sample rate, keeping the state.</summary>
    public void Configure(double sampleRate) {
        var pole = 2 * Math.PI * Model.GainBandwidth / Model.OpenLoopGain;
        var k = pole / (2 * sampleRate);
        hold = (1 - k) / (1 + k);
        gain = k * openLoopGain / (1 + k);
        slew = Model.SlewRate / sampleRate;
    }

    /// <summary>
    ///     A slew rate in place of the model's (the Drive block's Slew Rate on this op-amp), in V/s at
    ///     <paramref name="sampleRate" />; a later <see cref="Configure" /> restores the model's.
    /// </summary>
    public void SetSlewRate(double voltsPerSecond, double sampleRate) => slew = voltsPerSecond / sampleRate;

    public readonly double Conductance(int port) => port == 0 ? Math.Max(inputConductance, 1e-9) : outputConductance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var vd = voltages[0];
        var output = Output(vd, out var slope);
        currents[0] = vd * inputConductance;
        currents[1] = (voltages[1] - output) * outputConductance;
        jacobian[0] = inputConductance;
        jacobian[stride] = -slope * outputConductance;
        jacobian[stride + 1] = outputConductance;
    }

    /// <summary>
    ///     The output responds to v_d only within a narrow window (a few millivolts wide at high gain): outside it the
    ///     output slews or sits on a rail and is flat. A Newton step that would jump across the window, from one flat
    ///     side to the other, stops at its edge instead, where the slope is informative; otherwise Newton bounces from
    ///     rail to rail.
    /// </summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        var low = Math.Max(centre - half, InternalOutput - slew);
        var high = Math.Min(centre + half, InternalOutput + slew);
        if (low > high) {
            low = high = Math.Clamp(InternalOutput, centre - half, centre + half);
        }

        // target = hold y + gain (v_d + v_d[n-1]), solved for v_d at the window's ends.
        var from = (low - hold * InternalOutput) / gain - previousInput;
        var to = (high - hold * InternalOutput) / gain - previousInput;
        if (proposed[0] > to && previous[0] < to) {
            proposed[0] = to;
        } else if (proposed[0] < from && previous[0] > from) {
            proposed[0] = from;
        }
    }

    public void Commit(ReadOnlySpan<double> voltages) {
        var vd = voltages[0];
        InternalOutput = Math.Clamp(Internal(vd, out _), centre - 1.25 * half, centre + 1.25 * half);
        previousInput = vd;
    }

    public void Reset() {
        InternalOutput = 0;
        previousInput = 0;
    }

    /// <summary>The internal output after the pole and the slew limit, and its slope against v_d.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly double Internal(double vd, out double slope) {
        var target = hold * InternalOutput + gain * (vd + previousInput);
        var step = SoftLimit(target - InternalOutput, slew, out var limiterSlope);
        slope = limiterSlope * gain;
        return InternalOutput + step;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly double Output(double vd, out double slope) {
        var internalOutput = Internal(vd, out var internalSlope);
        var output = centre + Limit(internalOutput - centre, half, out var railSlope);
        slope = railSlope * internalSlope;
        return output;
    }

    /// <summary>The smooth limiter L s(x / L), s(x) = x / (1 + x⁸)^(1/8), and its slope (1 + x⁸)^(-9/8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Limit(double x, double limit, out double slope) {
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

    /// <summary>The gentler limiter L x / √(L² + x²) for the slew rate, and its slope.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double SoftLimit(double x, double limit, out double slope) {
        var u = x / limit;
        var p = 1 + u * u;
        var root = Math.Sqrt(p);
        slope = 1 / (root * p);
        return x / root;
    }
}
