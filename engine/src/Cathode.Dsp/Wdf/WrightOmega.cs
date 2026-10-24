using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     The Wright omega function ω(x), the solution of ω + ln ω = x (equivalently W(e^x) with Lambert's W). It turns the
///     implicit equation of a diode behind a resistor into a closed form.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Fast" /> is a piecewise polynomial: degree-7 Chebyshev interpolants of <see cref="Precise" />,
///         fitted
///         once at start-up and stored in monomial form in t ∈ [-1, 1]. Below <see cref="UniformHigh" /> the segments
///         are uniform in x (half a unit wide), where ω still bends like e^x; above it they are a quarter of an octave
///         wide, indexed straight from the exponent and the top two mantissa bits of x, because ω(x) ≈ x - ln x is
///         nearly linear there and only its logarithmic part needs fitting. Outside the table the asymptotic series take
///         over: ω = E (1 - E) + O(E³) with E = e^x for x below -24, and ω = x - L + L / x + O(L² / x²) with L = ln x
///         above 2^24.
///     </para>
///     <para>
///         The result is within 1e-11 relative of the exact value everywhere, with no logarithm or division in the
///         table range. The previous estimator (the cubic of D'Angelo, Gabrielli and Turchet, "Fast approximation of
///         the Lambert W function for virtual analog modelling", DAFx-19, refined by one step of Fritsch's iteration)
///         needed a library logarithm and a division per call and was only good to about 1e-5; on an Apple M1 the table
///         takes about 19 ns per dependent call against 42 ns, and 3 ns against 8 ns when calls overlap. The same paper
///         family (D'Angelo, Gabrielli and Turchet, "Fast approximations of the Wright omega function") trades accuracy
///         for speed further; a wave digital filter needs the accuracy, because antiderivative antialiasing divides
///         differences of ω-dependent terms by small differences of the input.
///     </para>
/// </remarks>
public static class WrightOmega {
    /// <summary>Lower end of the table; below it ω = e^x (1 - e^x) is exact to double precision.</summary>
    public const double TableLow = -24;

    /// <summary>Boundary between the uniform and the octave segments.</summary>
    public const double UniformHigh = 8;

    /// <summary>Upper end of the table (2^24); above it the asymptotic series is exact to double precision.</summary>
    public const double TableHigh = 16_777_216;

    const double SegmentsPerUnit = 2;
    const int OctaveBits = 2; // four segments per octave
    const int UniformSegments = (int)((UniformHigh - TableLow) * SegmentsPerUnit);
    const int OctaveSegments = (24 - 3) << OctaveBits; // octaves 2^3 (UniformHigh) to 2^24 (TableHigh)
    const long FirstOctave = (1_023L + 3) << OctaveBits; // biased exponent of UniformHigh, with the mantissa bits
    const long MantissaMask = 0x000F_FFFF_FFFF_FFFF;
    const long One = 0x3FF0_0000_0000_0000;
    const int Stride = 8; // coefficients per segment

    // The cubic of D'Angelo, Gabrielli and Turchet (DAFx-19), only a starting point for Precise.
    const double C3 = -1.314293149877800e-3;
    const double C2 = 4.775931364975583e-2;
    const double C1 = 3.631952663804445e-1;
    const double C0 = 6.313183464296682e-1;

    static readonly double[] Coefficients = Fit();

    /// <summary>ω(x) to a relative error below 1e-11.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Fast(double x) {
        int segment;
        double t;
        if (x < UniformHigh) {
            if (!(x >= TableLow)) {
                var e = Math.Exp(x);
                return e * (1 - e);
            }

            var s = (x - TableLow) * SegmentsPerUnit;
            segment = (int)s;
            t = 2 * (s - segment) - 1;
        } else if (x < TableHigh) {
            // x = 2^e (1 + m): the exponent and the top mantissa bits pick the segment, the remaining mantissa bits,
            // shifted up into a double in [1, 2), are the position in it.
            var bits = BitConverter.DoubleToInt64Bits(x);
            segment = UniformSegments + (int)((bits >> (52 - OctaveBits)) - FirstOctave);
            t = 2 * BitConverter.Int64BitsToDouble(((bits << OctaveBits) & MantissaMask) | One) - 3;
        } else {
            return Asymptotic(x);
        }

        // Estrin's scheme: a shorter dependency chain than Horner's for the same eight coefficients.
        ref var c = ref Coefficients[segment * Stride];
        var t2 = t * t;
        var low = c + Unsafe.Add(ref c, 1) * t + t2 * (Unsafe.Add(ref c, 2) + Unsafe.Add(ref c, 3) * t);
        var high = Unsafe.Add(ref c, 4)
            + Unsafe.Add(ref c, 5) * t
            + t2 * (Unsafe.Add(ref c, 6) + Unsafe.Add(ref c, 7) * t);
        return low + t2 * t2 * high;
    }

    /// <summary>
    ///     Iterates to double precision: a rough start, then Fritsch's third-order iteration (Lawrence, Corless and
    ///     Jeffrey, "Algorithm 917: complex double-precision evaluation of the Wright ω function", ACM TOMS 38(3), 2012).
    ///     Reference use and table fitting only.
    /// </summary>
    public static double Precise(double x) {
        if (double.IsNaN(x) || x > 1e300) {
            return x;
        }

        double y;
        if (x < -2) {
            var e = Math.Exp(x);
            y = e * (1 - e);
        } else if (x < UniformHigh) {
            y = C0 + x * (C1 + x * (C2 + x * C3));
        } else {
            y = x - Math.Log(x);
        }

        for (var i = 0; i < 20; i++) {
            var next = Fritsch(x, y);
            if (Math.Abs(next - y) <= 1e-15 * y) {
                return next;
            }

            y = next;
        }

        return y;
    }

    static double Asymptotic(double x) {
        if (x > 1e300) {
            return x; // also +∞, where x - ln x would be ∞ - ∞
        }

        var l = Math.Log(x);
        return x - l + l / x;
    }

    static double Fritsch(double x, double y) {
        if (y <= 0) {
            return 0; // e^x underflowed: ω is below the smallest double too
        }

        var r = x - y - Math.Log(y);
        var z = 1 + y;
        var q = z * (z + 2.0 / 3.0 * r);
        return y * (1 + r * (q - 0.5 * r) / (z * (q - r)));
    }

    /// <summary>Chebyshev interpolation of every segment at the eight Chebyshev nodes, converted to monomials in t.</summary>
    static double[] Fit() {
        var coefficients = new double[(UniformSegments + OctaveSegments) * Stride];
        Span<double> chebyshev = stackalloc double[Stride];
        Span<double> previous = stackalloc double[Stride];
        Span<double> current = stackalloc double[Stride];
        Span<double> next = stackalloc double[Stride];
        Span<double> samples = stackalloc double[Stride];
        for (var segment = 0; segment < UniformSegments + OctaveSegments; segment++) {
            double from, to;
            if (segment < UniformSegments) {
                from = TableLow + segment / SegmentsPerUnit;
                to = from + 1 / SegmentsPerUnit;
            } else {
                var k = segment - UniformSegments;
                var exponent = 3 + (k >> OctaveBits);
                var quarter = k & ((1 << OctaveBits) - 1);
                from = Math.ScaleB(1 + quarter / (double)(1 << OctaveBits), exponent);
                to = Math.ScaleB(1 + (quarter + 1) / (double)(1 << OctaveBits), exponent);
            }

            for (var k = 0; k < Stride; k++) {
                var node = Math.Cos(Math.PI * (k + 0.5) / Stride);
                samples[k] = Precise(0.5 * (from + to) + 0.5 * (to - from) * node);
            }

            for (var j = 0; j < Stride; j++) {
                var sum = 0.0;
                for (var k = 0; k < Stride; k++) {
                    sum += samples[k] * Math.Cos(Math.PI * j * (k + 0.5) / Stride);
                }

                chebyshev[j] = 2.0 / Stride * sum;
            }

            chebyshev[0] *= 0.5;

            // Accumulate Σ c_j T_j(t) in monomials, with T_{j+1} = 2 t T_j - T_{j-1}.
            var monomial = coefficients.AsSpan(segment * Stride, Stride);
            previous.Clear();
            current.Clear();
            previous[0] = 1;
            current[1] = 1;
            monomial[0] = chebyshev[0];
            monomial[1] = chebyshev[1];
            for (var j = 2; j < Stride; j++) {
                for (var i = 0; i < Stride; i++) {
                    next[i] = (i > 0 ? 2 * current[i - 1] : 0) - previous[i];
                }

                for (var i = 0; i < Stride; i++) {
                    monomial[i] += chebyshev[j] * next[i];
                }

                current.CopyTo(previous);
                next.CopyTo(current);
            }
        }

        return coefficients;
    }
}
