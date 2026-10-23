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

    private static (double K, double Q) TransitionParameters(double transition) {
        var k = Math.Pow(Math.Tan((1 - transition * 2) * Math.PI / 4), 2);
        var root = Math.Pow(1 - k * k, 0.25);
        var e = 0.5 * (1 - root) / (1 + root);
        var e4 = Math.Pow(e, 4);
        var q = e * (1 + e4 * (2 + e4 * (15 + 150 * e4)));
        return (k, q);
    }

    private static double Coefficient(int index, double k, double q, int order) {
        var c = index + 1;
        var numerator = SeriesNumerator(q, order, c) * Math.Pow(q, 0.25);
        var denominator = SeriesDenominator(q, order, c) + 0.5;
        var ww = numerator / denominator;
        var wwsq = ww * ww;
        var x = Math.Sqrt((1 - wwsq * k) * (1 - wwsq / k)) / (1 + wwsq);
        return (1 - x) / (1 + x);
    }

    private static double SeriesNumerator(double q, int order, int c) {
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

    private static double SeriesDenominator(double q, int order, int c) {
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
internal sealed class AllpassChain(float[] coefficients) {
    private readonly float[] _x = new float[coefficients.Length];
    private readonly float[] _y = new float[coefficients.Length];

    public float Process(float input) {
        for (var i = 0; i < coefficients.Length; i++) {
            var output = coefficients[i] * (input - _y[i]) + _x[i];
            _x[i] = input;
            _y[i] = output;
            input = output;
        }

        return input;
    }

    public void Reset() {
        Array.Clear(_x);
        Array.Clear(_y);
    }

    public void FlushDenormals() {
        for (var i = 0; i < _x.Length; i++) {
            if (MathF.Abs(_x[i]) < 1e-20f) {
                _x[i] = 0;
            }

            if (MathF.Abs(_y[i]) < 1e-20f) {
                _y[i] = 0;
            }
        }
    }
}

/// <summary>2x upsampler and downsampler pair built from one half-band design.</summary>
public sealed class HalfBandStage {
    private readonly AllpassChain _upEven;
    private readonly AllpassChain _upOdd;
    private readonly AllpassChain _downEven;
    private readonly AllpassChain _downOdd;

    public HalfBandStage(double[] coefficients) {
        var even = coefficients.Where((_, i) => i % 2 == 0).Select(static c => (float)c).ToArray();
        var odd = coefficients.Where((_, i) => i % 2 == 1).Select(static c => (float)c).ToArray();
        _upEven = new AllpassChain(even);
        _upOdd = new AllpassChain(odd);
        _downEven = new AllpassChain(even);
        _downOdd = new AllpassChain(odd);
    }

    /// <summary>Writes 2 x <paramref name="input" />.Length samples to <paramref name="output" />.</summary>
    public void Up(ReadOnlySpan<float> input, Span<float> output) {
        for (var i = 0; i < input.Length; i++) {
            output[2 * i] = _upEven.Process(input[i]);
            output[2 * i + 1] = _upOdd.Process(input[i]);
        }
    }

    /// <summary>Reads 2 x <paramref name="output" />.Length samples from <paramref name="input" />.</summary>
    public void Down(ReadOnlySpan<float> input, Span<float> output) {
        for (var i = 0; i < output.Length; i++) {
            output[i] = 0.5f * (_downEven.Process(input[2 * i + 1]) + _downOdd.Process(input[2 * i]));
        }
    }

    public void Reset() {
        _upEven.Reset();
        _upOdd.Reset();
        _downEven.Reset();
        _downOdd.Reset();
    }

    public void FlushDenormals() {
        _upEven.FlushDenormals();
        _upOdd.FlushDenormals();
        _downEven.FlushDenormals();
        _downOdd.FlushDenormals();
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
    private static readonly double[] FirstStage = HalfBand.Design(AttenuationDb, 0.04);
    private static readonly double[] SecondStage = HalfBand.Design(AttenuationDb, 0.26);

    private readonly HalfBandStage _first = new(FirstStage);
    private readonly HalfBandStage _second = new(SecondStage);
    private readonly float[] _twice;
    private readonly float[] _four;

    public Oversampler4x(int maxBlockSize) {
        _twice = new float[maxBlockSize * 2];
        _four = new float[maxBlockSize * 4];
    }

    public static int CoefficientCount => FirstStage.Length + SecondStage.Length;

    /// <summary>Upsamples <paramref name="input" /> and returns the 4x buffer to process in place.</summary>
    public Span<float> Up(ReadOnlySpan<float> input) {
        var twice = _twice.AsSpan(0, input.Length * 2);
        var four = _four.AsSpan(0, input.Length * 4);
        _first.Up(input, twice);
        _second.Up(twice, four);
        return four;
    }

    /// <summary>Downsamples the 4x buffer returned by <see cref="Up" /> into <paramref name="output" />.</summary>
    public void Down(Span<float> output) {
        var twice = _twice.AsSpan(0, output.Length * 2);
        _second.Down(_four.AsSpan(0, output.Length * 4), twice);
        _first.Down(twice, output);
    }

    public void Reset() {
        _first.Reset();
        _second.Reset();
    }

    public void FlushDenormals() {
        _first.FlushDenormals();
        _second.FlushDenormals();
    }
}
