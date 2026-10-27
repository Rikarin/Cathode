using Cathode.Dsp.Wdf;
using System.Numerics;

namespace Cathode.Dsp.Tests.Circuits;

/// <summary>
///     The scattering matrix of a passive <see cref="WdfNetlist" /> (ports, resistors and probes) solved in exact rational
///     arithmetic from the same component values <see cref="RTypeScattering" /> holds, as the high-precision reference
///     for its double-precision LU solve; and the ∞-norm condition number of its nodal matrix.
/// </summary>
static class ExactScattering {
    /// <summary>
    ///     The largest difference between <paramref name="scattering" />'s matrix and the exact one, relative to the
    ///     largest exact entry, and cond∞(X) = ‖X‖∞ ‖X⁻¹‖∞ of the nodal matrix it was solved from.
    /// </summary>
    public static (double Error, double Condition) Compare(WdfNetlist netlist, RTypeScattering scattering) {
        var n = netlist.Nodes;
        var columns = scattering.Columns;
        var x = new Fraction[n, n];
        var rhs = new Fraction[n, columns];
        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                x[i, j] = Fraction.Zero;
            }

            for (var j = 0; j < columns; j++) {
                rhs[i, j] = Fraction.Zero;
            }

            x[i, i] = Fraction.From(1e-15); // RTypeScattering's safety conductance to ground
        }

        var (port, resistor) = (0, 0);
        foreach (var e in netlist.Elements) {
            switch (e.Kind) {
                case NetlistElementKind.Port: {
                    var g = Fraction.One / Fraction.From(scattering.PortResistances[port]);
                    Conductance(x, e.Plus, e.Minus, g);
                    Inject(rhs, e.Plus, e.Minus, port++, g);
                    break;
                }

                case NetlistElementKind.Resistor:
                    Conductance(x, e.Plus, e.Minus, Fraction.One / Fraction.From(scattering.GetResistance(resistor++)));
                    break;
                case NetlistElementKind.Probe:
                    break;
                default:
                    throw new NotSupportedException($"{e.Kind} is not in the exact reference.");
            }
        }

        // Gauss-Jordan on [X | B | I]: the node voltages per input column, and X⁻¹ for the condition number.
        var width = columns + n;
        var augmented = new Fraction[n, n + width];
        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                augmented[i, j] = x[i, j];
            }

            for (var j = 0; j < columns; j++) {
                augmented[i, n + j] = rhs[i, j];
            }

            for (var j = 0; j < n; j++) {
                augmented[i, n + columns + j] = i == j ? Fraction.One : Fraction.Zero;
            }
        }

        for (var k = 0; k < n; k++) {
            var pivot = k;
            while (augmented[pivot, k].IsZero) {
                pivot++;
            }

            for (var j = 0; j < n + width; j++) {
                (augmented[k, j], augmented[pivot, j]) = (augmented[pivot, j], augmented[k, j]);
            }

            var inverse = Fraction.One / augmented[k, k];
            for (var j = 0; j < n + width; j++) {
                augmented[k, j] *= inverse;
            }

            for (var i = 0; i < n; i++) {
                if (i == k || augmented[i, k].IsZero) {
                    continue;
                }

                var factor = augmented[i, k];
                for (var j = 0; j < n + width; j++) {
                    augmented[i, j] -= factor * augmented[k, j];
                }
            }
        }

        var (error, largest) = (0.0, 0.0);
        var (row, probe) = (0, 0);
        foreach (var e in netlist.Elements) {
            int target;
            bool wave;
            if (e.Kind == NetlistElementKind.Port) {
                (target, wave) = (row++, true);
            } else if (e.Kind == NetlistElementKind.Probe) {
                (target, wave) = (scattering.ProbeRow + probe++, false);
            } else {
                continue;
            }

            var actual = scattering.Row(target);
            for (var c = 0; c < columns; c++) {
                var v = Voltage(augmented, n + c, e.Plus) - Voltage(augmented, n + c, e.Minus);
                var exact = wave ? Fraction.From(2) * v - (c == target ? Fraction.One : Fraction.Zero) : v;
                largest = Math.Max(largest, Math.Abs(exact.ToDouble()));
                error = Math.Max(error, Math.Abs((Fraction.From(actual[c]) - exact).ToDouble()));
            }
        }

        var (norm, inverseNorm) = (0.0, 0.0);
        for (var i = 0; i < n; i++) {
            var (sum, inverseSum) = (Fraction.Zero, Fraction.Zero);
            for (var j = 0; j < n; j++) {
                sum += x[i, j].Abs();
                inverseSum += augmented[i, n + columns + j].Abs();
            }

            norm = Math.Max(norm, sum.ToDouble());
            inverseNorm = Math.Max(inverseNorm, inverseSum.ToDouble());
        }

        return (error / largest, norm * inverseNorm);
    }

    static Fraction Voltage(Fraction[,] solved, int column, int node) =>
        node > 0 ? solved[node - 1, column] : Fraction.Zero;

    static void Conductance(Fraction[,] x, int p, int m, Fraction g) {
        if (p > 0) {
            x[p - 1, p - 1] += g;
        }

        if (m > 0) {
            x[m - 1, m - 1] += g;
        }

        if (p > 0 && m > 0) {
            x[p - 1, m - 1] -= g;
            x[m - 1, p - 1] -= g;
        }
    }

    static void Inject(Fraction[,] rhs, int p, int m, int column, Fraction amount) {
        if (p > 0) {
            rhs[p - 1, column] += amount;
        }

        if (m > 0) {
            rhs[m - 1, column] -= amount;
        }
    }

    /// <summary>An exact rational, numerator over a positive denominator, kept in lowest terms.</summary>
    readonly record struct Fraction(BigInteger Numerator, BigInteger Denominator) {
        public static Fraction Zero => new(BigInteger.Zero, BigInteger.One);

        public static Fraction One => new(BigInteger.One, BigInteger.One);

        public bool IsZero => Numerator.IsZero;

        /// <summary>The double's exact value: its mantissa times a power of two.</summary>
        public static Fraction From(double value) {
            if (!double.IsFinite(value)) {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == 0) {
                return Zero;
            }

            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & ((1L << 52) - 1);
            if (exponent == 0) {
                exponent = 1;
            } else {
                mantissa |= 1L << 52;
            }

            exponent -= 1075;
            var numerator = new BigInteger(value < 0 ? -mantissa : mantissa);
            return exponent >= 0
                ? new(numerator << exponent, BigInteger.One)
                : Reduce(numerator, BigInteger.One << -exponent);
        }

        public Fraction Abs() => new(BigInteger.Abs(Numerator), Denominator);

        /// <summary>Rounded to a double, scaled so that a tiny or huge value keeps its digits.</summary>
        public double ToDouble() {
            if (Numerator.IsZero) {
                return 0;
            }

            var shift = (int)(Denominator.GetBitLength() - BigInteger.Abs(Numerator).GetBitLength()) + 64;
            var scaled = shift >= 0 ? (Numerator << shift) / Denominator : Numerator / (Denominator << -shift);
            return Math.ScaleB((double)scaled, -shift);
        }

        static Fraction Reduce(BigInteger numerator, BigInteger denominator) {
            if (denominator.Sign < 0) {
                (numerator, denominator) = (-numerator, -denominator);
            }

            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            return divisor.IsOne || divisor.IsZero
                ? new(numerator, denominator)
                : new(numerator / divisor, denominator / divisor);
        }

        public static Fraction operator +(Fraction a, Fraction b) =>
            Reduce(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

        public static Fraction operator -(Fraction a, Fraction b) =>
            Reduce(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

        public static Fraction operator *(Fraction a, Fraction b) =>
            Reduce(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

        public static Fraction operator /(Fraction a, Fraction b) =>
            Reduce(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }
}
