namespace Cathode.Dsp.Filters;

/// <summary>
///     A passive magnetic pickup: coil inductance (H), its DC resistance (Ω) and winding capacitance (F).
/// </summary>
public readonly record struct Pickup(double Inductance, double Resistance, double Capacitance) {
    /// <summary>A vintage-style single coil.</summary>
    public static Pickup SingleCoil { get; } = new(2.5, 6_000, 100e-12);

    /// <summary>A PAF-style humbucker (both coils in series).</summary>
    public static Pickup Humbucker { get; } = new(4.5, 8_500, 150e-12);

    /// <summary>A P-90: a single coil, wide and many-turned.</summary>
    public static Pickup P90 { get; } = new(7.0, 8_000, 110e-12);
}

/// <summary>
///     A pickup loaded by its cable and the input it plays into (docs/08-blocks-parity.md §2): the coil's inductance L
///     and resistance R in series, feeding the parallel capacitance C (winding plus cable) and the load resistance
///     R<sub>L</sub>. The voltage at the input is
///     <code>H(s) = 1 / (L C s² + (R C + L / R_L) s + 1 + R / R_L)</code>
///     a second-order low-pass whose resonance is
///     <code>f₀ = √((1 + R / R_L) / (L C)) / 2π,  Q = √(L C (1 + R / R_L)) / (R C + L / R_L)</code>
///     A lower load damps the peak and drops it a little; more cable capacitance moves it down.
/// </summary>
/// <remarks>
///     A recorded or live DI has already been through the real pickup into the interface's instrument input, a high
///     impedance (<see cref="ReferenceLoad" />) with the same cable. So the Input block applies the change from that
///     reference to the chosen load: H<sub>chosen</sub>(s) / H<sub>reference</sub>(s), one biquad. At Auto (1 MΩ)
///     without the pickup buffer the two are equal and the signal passes untouched. The pickup buffer stands for a
///     buffer at the guitar: the pickup then sees 1 MΩ and none of the cable.
/// </remarks>
public static class PickupLoad {
    /// <summary>The instrument input a DI is assumed to have been recorded through.</summary>
    public const double ReferenceLoad = 1e6;

    /// <summary>The Input block's Input Impedance choices, in ohms (Auto is 1 MΩ).</summary>
    public static IReadOnlyList<double> Loads { get; } = [1e6, 1e6, 470e3, 230e3, 90e3, 70e3, 32e3, 22e3];

    public static IReadOnlyList<string> LoadNames { get; } =
        ["Auto", "1M", "470k", "230k", "90k", "70k", "32k", "22k"];

    /// <summary>
    ///     The coefficients of the denominator a₂ s² + a₁ s + a₀ of <paramref name="pickup" /> into
    ///     <paramref name="load" /> ohms with <paramref name="cable" /> farads of cable.
    /// </summary>
    public static (double A2, double A1, double A0) Denominator(Pickup pickup, double load, double cable) {
        var c = pickup.Capacitance + cable;
        return (pickup.Inductance * c, pickup.Resistance * c + pickup.Inductance / load, 1 + pickup.Resistance / load);
    }

    /// <summary>The loaded pickup's resonance frequency (Hz) and quality factor.</summary>
    public static (double Frequency, double Q) Resonance(Pickup pickup, double load, double cable) {
        var (a2, a1, a0) = Denominator(pickup, load, cable);
        return (Math.Sqrt(a0 / a2) / (2 * Math.PI), Math.Sqrt(a0 * a2) / a1);
    }

    /// <summary>|H(j2πf)| of the loaded pickup (its DC gain is 1 / (1 + R / R_L)).</summary>
    public static double Magnitude(Pickup pickup, double load, double cable, double frequency) {
        var (a2, a1, a0) = Denominator(pickup, load, cable);
        var w = 2 * Math.PI * frequency;
        var real = a0 - a2 * w * w;
        var imaginary = a1 * w;
        return 1 / Math.Sqrt(real * real + imaginary * imaginary);
    }

    /// <summary>
    ///     The digital biquad of H<sub>chosen</sub> / H<sub>reference</sub>: b₀..b₂ over 1, a₁, a₂. The reference's
    ///     poles become its zeros. Each quadratic goes through its own bilinear transform, warped at its own resonance,
    ///     so both the peak and the notch it cancels land on their analog frequencies (the DC gain is kept either way).
    /// </summary>
    public static (double B0, double B1, double B2, double A1, double A2) Design(
        Pickup pickup,
        double load,
        double cable,
        bool buffered,
        double sampleRate
    ) {
        var (n0, n1, n2) = Bilinear(Denominator(pickup, ReferenceLoad, cable), sampleRate);
        var (d0, d1, d2) = Bilinear(
            buffered ? Denominator(pickup, ReferenceLoad, 0) : Denominator(pickup, load, cable),
            sampleRate
        );
        return (n0 / d0, n1 / d0, n2 / d0, d1 / d0, d2 / d0);
    }

    /// <summary>a₂ s² + a₁ s + a₀ in z⁻¹ (coefficients of 1, z⁻¹, z⁻²), warped at its resonance.</summary>
    static (double C0, double C1, double C2) Bilinear((double A2, double A1, double A0) s, double sampleRate) {
        var w0 = Math.Min(Math.Sqrt(s.A0 / s.A2), 0.9 * Math.PI * sampleRate);
        var k = w0 / Math.Tan(w0 / (2 * sampleRate));
        var kk = k * k;
        return (s.A2 * kk + s.A1 * k + s.A0, 2 * s.A0 - 2 * s.A2 * kk, s.A2 * kk - s.A1 * k + s.A0);
    }
}
