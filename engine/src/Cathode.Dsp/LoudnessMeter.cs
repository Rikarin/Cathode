namespace Cathode.Dsp;

/// <summary>
///     An ITU-R BS.1770 loudness meter for a stereo signal: K-weighting (the stage 1 high shelf, about +4 dB above 2
///     kHz, and the stage 2 high-pass at 38 Hz), then the mean square of each channel summed with unit weights, in 100
///     ms bins. Momentary loudness is the last 400 ms, short-term the last 3 s, both in LUFS (−0.691 + 10 log10 of the
///     mean square). Allocation-free after construction; values update once per bin.
/// </summary>
public sealed class LoudnessMeter {
    /// <summary>What silence reads, instead of minus infinity.</summary>
    public const float FloorLufs = -120f;

    const int MomentaryBins = 4;
    const int ShortTermBins = 30;

    readonly double[] bins = new double[ShortTermBins];
    readonly int binLength;
    readonly Stage shelf;
    readonly Stage highPass;
    Stage.State shelfLeft;
    Stage.State shelfRight;
    Stage.State highLeft;
    Stage.State highRight;
    double sum;
    int filled;
    int position;
    int written;

    public LoudnessMeter(double sampleRate) {
        binLength = Math.Max(1, (int)Math.Round(sampleRate * 0.1));
        shelf = Stage.Shelf(sampleRate);
        highPass = Stage.HighPass(sampleRate);
        Reset();
    }

    /// <summary>Loudness of the last 400 ms, in LUFS.</summary>
    public float Momentary { get; private set; }

    /// <summary>Loudness of the last 3 s, in LUFS.</summary>
    public float ShortTerm { get; private set; }

    /// <summary>
    ///     Mean square of the last 400 ms (the energy behind <see cref="Momentary" />), for gated averaging.
    /// </summary>
    public double MomentaryPower { get; private set; }

    public void Reset() {
        Array.Clear(bins);
        shelfLeft = default;
        shelfRight = default;
        highLeft = default;
        highRight = default;
        sum = 0;
        filled = 0;
        position = 0;
        written = 0;
        Momentary = FloorLufs;
        ShortTerm = FloorLufs;
        MomentaryPower = 0;
    }

    public void Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right) {
        for (var i = 0; i < left.Length; i++) {
            var l = highPass.Tick(ref highLeft, shelf.Tick(ref shelfLeft, left[i]));
            var r = highPass.Tick(ref highRight, shelf.Tick(ref shelfRight, right[i]));
            sum += l * l + r * r;
            if (++filled == binLength) {
                bins[position] = sum / binLength;
                position = (position + 1) % ShortTermBins;
                written = Math.Min(written + 1, ShortTermBins);
                sum = 0;
                filled = 0;
                Publish();
            }
        }

        // Flush filter state that has decayed to denormal levels in silence.
        Stage.Flush(ref shelfLeft);
        Stage.Flush(ref shelfRight);
        Stage.Flush(ref highLeft);
        Stage.Flush(ref highRight);
    }

    /// <summary>LUFS of a mean square.</summary>
    public static float ToLufs(double power) =>
        power <= 1e-12 ? FloorLufs : Math.Max(FloorLufs, (float)(-0.691 + 10 * Math.Log10(power)));

    void Publish() {
        MomentaryPower = Average(MomentaryBins);
        Momentary = ToLufs(MomentaryPower);
        ShortTerm = ToLufs(Average(ShortTermBins));
    }

    double Average(int count) {
        var n = Math.Min(count, written);
        var total = 0.0;
        for (var k = 1; k <= n; k++) {
            total += bins[(position - k + ShortTermBins) % ShortTermBins];
        }

        // Before a window has filled, the missing time counts as silence, as the standard's sliding window does.
        return total / count;
    }

    /// <summary>One K-weighting biquad (direct form I, double precision).</summary>
    readonly struct Stage(double b0, double b1, double b2, double a1, double a2) {
        public static Stage Shelf(double rate) {
            const double f0 = 1681.974450955533;
            const double gain = 3.999843853973347;
            const double q = 0.7071752369554196;
            var k = Math.Tan(Math.PI * f0 / rate);
            var vh = Math.Pow(10, gain / 20);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1 + k / q + k * k;
            return new(
                (vh + vb * k / q + k * k) / a0,
                2 * (k * k - vh) / a0,
                (vh - vb * k / q + k * k) / a0,
                2 * (k * k - 1) / a0,
                (1 - k / q + k * k) / a0
            );
        }

        public static Stage HighPass(double rate) {
            const double f0 = 38.13547087602444;
            const double q = 0.5003270373238773;
            var k = Math.Tan(Math.PI * f0 / rate);
            var a0 = 1 + k / q + k * k;
            return new(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        }

        public double Tick(ref State s, double x) {
            var y = b0 * x + b1 * s.X1 + b2 * s.X2 - a1 * s.Y1 - a2 * s.Y2;
            s = new(x, s.X1, y, s.Y1);
            return y;
        }

        public static void Flush(ref State s) {
            if (Math.Abs(s.Y1) < 1e-25 && Math.Abs(s.Y2) < 1e-25) {
                s = new(s.X1, s.X2, 0, 0);
            }
        }

        public readonly record struct State(double X1, double X2, double Y1, double Y2);
    }
}
