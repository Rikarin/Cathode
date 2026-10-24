namespace Cathode.Dsp.Filters;

/// <summary>
///     A 90° phase-difference network: two chains of four second-order allpasses in z⁻² whose outputs stay a quarter
///     cycle apart (within about a degree) from roughly 15 Hz to 20 kHz at 44.1 kHz and above. The coefficients are Olli
///     Niemitalo's published design; <c>y[n] = a²(x[n] + y[n−2]) − x[n−2]</c> per section, with the in-phase chain
///     one sample later. Together the outputs are the analytic signal (in-phase, quadrature) of the input, as
///     single-sideband modulation and frequency shifting need. Both chains are allpasses, so each output keeps the
///     input's level.
/// </summary>
public struct HilbertPair {
    static readonly double[] InPhase = [0.6923878, 0.9360654322959, 0.9882295226860, 0.9987488452737];
    static readonly double[] Quadrature = [0.4021921162426, 0.8561710882420, 0.9722909545651, 0.9952884791278];

    Chain inPhase;
    Chain quadrature;
    double delayed;

    public void Reset() {
        inPhase = default;
        quadrature = default;
        delayed = 0;
    }

    /// <summary>One sample: the in-phase and quadrature outputs.</summary>
    public (double I, double Q) Tick(double x) {
        var i = delayed;
        delayed = inPhase.Tick(InPhase, x);
        return (i, quadrature.Tick(Quadrature, x));
    }

    /// <summary>Zeroes state that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        inPhase.Flush();
        quadrature.Flush();
        if (Math.Abs(delayed) < 1e-30) {
            delayed = 0;
        }
    }

    /// <summary>Four sections' input and output history (two samples each).</summary>
    struct Chain {
        double x1a, x2a, y1a, y2a;
        double x1b, x2b, y1b, y2b;
        double x1c, x2c, y1c, y2c;
        double x1d, x2d, y1d, y2d;

        public double Tick(double[] a, double x) {
            x = Section(a[0], x, ref x1a, ref x2a, ref y1a, ref y2a);
            x = Section(a[1], x, ref x1b, ref x2b, ref y1b, ref y2b);
            x = Section(a[2], x, ref x1c, ref x2c, ref y1c, ref y2c);
            return Section(a[3], x, ref x1d, ref x2d, ref y1d, ref y2d);
        }

        public void Flush() {
            if (Math.Abs(y1a)
                + Math.Abs(y2a)
                + Math.Abs(y1b)
                + Math.Abs(y2b)
                + Math.Abs(y1c)
                + Math.Abs(y2c)
                + Math.Abs(y1d)
                + Math.Abs(y2d)
                + Math.Abs(x1a)
                + Math.Abs(x2a)
                < 1e-30) {
                this = default;
            }
        }

        static double Section(double a, double x, ref double x1, ref double x2, ref double y1, ref double y2) {
            var y = a * a * (x + y2) - x2;
            (x2, x1) = (x1, x);
            (y2, y1) = (y1, y);
            return y;
        }
    }
}
