namespace Cathode.Dsp.Resampling;

/// <summary>
///     Polyphase IIR half-band filter design after Laurent de Soras' HIIR (http://ldesoras.free.fr/prod.html): two
///     parallel chains of first-order allpass sections whose average is an elliptic half-band low-pass. Near-zero
///     latency (non-linear phase), and cheap: each coefficient costs one multiply per sample at the low rate.
/// </summary>
public static class HalfBand {
    /// <summary>
    ///     Coefficients for a filter reaching <paramref name="attenuationDb" /> in the stopband with the given
    ///     <paramref name="transition" /> bandwidth (fraction of the high sample rate, 0..0.5).
    /// </summary>
    public static double[] Design(double attenuationDb, double transition) {
        var (k, q) = TransitionParameters(transition);
        var attenuation = Math.Pow(10, -attenuationDb / 10);
        var a = attenuation / (1 - attenuation);
        var order = (int)Math.Ceiling(Math.Log(a * a / 16) / Math.Log(q));
        if ((order & 1) == 0) {
            order++;
        }

        order = Math.Max(order, 3);
        var count = (order - 1) / 2;
        var coefficients = new double[count];
        for (var i = 0; i < count; i++) {
            coefficients[i] = Coefficient(i, k, q, order);
        }

        return coefficients;
    }

    static (double K, double Q) TransitionParameters(double transition) {
        var k = Math.Pow(Math.Tan((1 - transition * 2) * Math.PI / 4), 2);
        var root = Math.Pow(1 - k * k, 0.25);
        var e = 0.5 * (1 - root) / (1 + root);
        var e4 = Math.Pow(e, 4);
        var q = e * (1 + e4 * (2 + e4 * (15 + 150 * e4)));
        return (k, q);
    }

    static double Coefficient(int index, double k, double q, int order) {
        var c = index + 1;
        var numerator = SeriesNumerator(q, order, c) * Math.Pow(q, 0.25);
        var denominator = SeriesDenominator(q, order, c) + 0.5;
        var ww = numerator / denominator;
        var wwsq = ww * ww;
        var x = Math.Sqrt((1 - wwsq * k) * (1 - wwsq / k)) / (1 + wwsq);
        return (1 - x) / (1 + x);
    }

    static double SeriesNumerator(double q, int order, int c) {
        double sum = 0, term;
        var sign = 1;
        var i = 0;
        do {
            term = Math.Pow(q, i * (i + 1)) * Math.Sin((i * 2 + 1) * c * Math.PI / order) * sign;
            sum += term;
            sign = -sign;
            i++;
        } while (Math.Abs(term) > 1e-100);

        return sum;
    }

    static double SeriesDenominator(double q, int order, int c) {
        double sum = 0, term;
        var sign = -1;
        var i = 1;
        do {
            term = Math.Pow(q, i * i) * Math.Cos(i * 2 * c * Math.PI / order) * sign;
            sum += term;
            sign = -sign;
            i++;
        } while (Math.Abs(term) > 1e-100);

        return sum;
    }
}

/// <summary>One allpass chain state: y[n] = a (x[n] - y[n-1]) + x[n-1] per section.</summary>
sealed class AllpassChain(float[] coefficients) {
    readonly float[] x = new float[coefficients.Length];
    readonly float[] y = new float[coefficients.Length];

    public float Process(float input) {
        for (var i = 0; i < coefficients.Length; i++) {
            var output = coefficients[i] * (input - y[i]) + x[i];
            x[i] = input;
            y[i] = output;
            input = output;
        }

        return input;
    }

    public void Reset() {
        Array.Clear(x);
        Array.Clear(y);
    }

    public void FlushDenormals() {
        for (var i = 0; i < x.Length; i++) {
            if (MathF.Abs(x[i]) < 1e-20f) {
                x[i] = 0;
            }

            if (MathF.Abs(y[i]) < 1e-20f) {
                y[i] = 0;
            }
        }
    }
}

/// <summary>2x upsampler and downsampler pair built from one half-band design.</summary>
public sealed class HalfBandStage {
    readonly AllpassChain upEven;
    readonly AllpassChain upOdd;
    readonly AllpassChain downEven;
    readonly AllpassChain downOdd;

    public HalfBandStage(double[] coefficients) : this(Split(coefficients)) { }

    /// <summary>
    ///     From coefficients already split by <see cref="Split" />: the arrays are only read, so stages built from one
    ///     design can share them.
    /// </summary>
    public HalfBandStage((float[] Even, float[] Odd) coefficients) {
        var (even, odd) = coefficients;
        upEven = new(even);
        upOdd = new(odd);
        downEven = new(even);
        downOdd = new(odd);
    }

    /// <summary>The even- and odd-indexed coefficients, which run the two allpass branches.</summary>
    public static (float[] Even, float[] Odd) Split(double[] coefficients) =>
    (
        coefficients.Where((_, i) => i % 2 == 0).Select(static c => (float)c).ToArray(),
        coefficients.Where((_, i) => i % 2 == 1).Select(static c => (float)c).ToArray()
    );

    /// <summary>Writes 2 x <paramref name="input" />.Length samples to <paramref name="output" />.</summary>
    public void Up(ReadOnlySpan<float> input, Span<float> output) {
        for (var i = 0; i < input.Length; i++) {
            output[2 * i] = upEven.Process(input[i]);
            output[2 * i + 1] = upOdd.Process(input[i]);
        }
    }

    /// <summary>Reads 2 x <paramref name="output" />.Length samples from <paramref name="input" />.</summary>
    public void Down(ReadOnlySpan<float> input, Span<float> output) {
        for (var i = 0; i < output.Length; i++) {
            output[i] = 0.5f * (downEven.Process(input[2 * i + 1]) + downOdd.Process(input[2 * i]));
        }
    }

    public void Reset() {
        upEven.Reset();
        upOdd.Reset();
        downEven.Reset();
        downOdd.Reset();
    }

    public void FlushDenormals() {
        upEven.FlushDenormals();
        upOdd.FlushDenormals();
        downEven.FlushDenormals();
        downOdd.FlushDenormals();
    }
}

/// <summary>
///     4x oversampling as two cascaded 2x half-band stages. Process a block with <see cref="Up" />, run the nonlinearity
///     on the returned span, then <see cref="Down" />. Allocation-free after construction.
/// </summary>
public sealed class Oversampler4x {
    /// <summary>Stopband attenuation of each stage, in dB.</summary>
    public const double AttenuationDb = 100;

    // The first stage must keep the full audio band (transition starts near 20 kHz at 48 kHz); the second stage
    // only has to reject images of an already band-limited signal, so it can be much shorter.
    static readonly double[] FirstStage = HalfBand.Design(AttenuationDb, 0.04);
    static readonly double[] SecondStage = HalfBand.Design(AttenuationDb, 0.26);
    static readonly (float[], float[]) FirstBranches = HalfBandStage.Split(FirstStage);
    static readonly (float[], float[]) SecondBranches = HalfBandStage.Split(SecondStage);

    readonly HalfBandStage first = new(FirstBranches);
    readonly HalfBandStage second = new(SecondBranches);
    float[] twice;
    float[] four;

    public static int CoefficientCount => FirstStage.Length + SecondStage.Length;

    public Oversampler4x(int maxBlockSize) {
        twice = new float[maxBlockSize * 2];
        four = new float[maxBlockSize * 4];
    }

    /// <summary>
    ///     Makes room for blocks of <paramref name="maxBlockSize" />, keeping the filters: cheaper than a new instance
    ///     when a block is prepared. Control thread only.
    /// </summary>
    public void Resize(int maxBlockSize) {
        if (four.Length != maxBlockSize * 4) {
            twice = new float[maxBlockSize * 2];
            four = new float[maxBlockSize * 4];
        }
    }

    /// <summary>Upsamples <paramref name="input" /> and returns the 4x buffer to process in place.</summary>
    public Span<float> Up(ReadOnlySpan<float> input) {
        var twice = this.twice.AsSpan(0, input.Length * 2);
        var four = this.four.AsSpan(0, input.Length * 4);
        first.Up(input, twice);
        second.Up(twice, four);
        return four;
    }

    /// <summary>Downsamples the 4x buffer returned by <see cref="Up" /> into <paramref name="output" />.</summary>
    public void Down(Span<float> output) {
        var twice = this.twice.AsSpan(0, output.Length * 2);
        second.Down(four.AsSpan(0, output.Length * 4), twice);
        first.Down(twice, output);
    }

    public void Reset() {
        first.Reset();
        second.Reset();
    }

    public void FlushDenormals() {
        first.FlushDenormals();
        second.FlushDenormals();
    }
}
