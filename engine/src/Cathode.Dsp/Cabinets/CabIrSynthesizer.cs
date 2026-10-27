using System.Numerics;

namespace Cathode.Dsp.Cabinets;

/// <summary>
///     Turns a <see cref="MicPlacement" /> (or a point inside a user <see cref="CabGrid" />) into an impulse response
///     at the engine rate: the magnitude part is made minimum phase by the real cepstrum (Oppenheim and Schafer,
///     Discrete-Time Signal Processing, §12), multiplied by the physically phased paths, normalised for loudness and
///     taken back to time. All scratch memory is native and allocated once; synthesis allocates nothing managed, so it
///     can run in <see cref="IDspBlock.Prepare" /> and on the background refresher. Not thread-safe.
/// </summary>
public sealed class CabIrSynthesizer : IDisposable {
    /// <summary>IR length at <see cref="ReferenceRate" />: the DynaCab length, 42.7 ms.</summary>
    public const int ReferenceTaps = 2_048;

    public const double ReferenceRate = 48_000;

    /// <summary>Magnitude floor, dB: keeps the cepstrum finite where a response reaches zero (DC).</summary>
    const double FloorDb = -140;

    const int FadeLength = 64;
    const double LoudnessLowHz = 40;
    const double LoudnessHighHz = 16_000;

    readonly NativeFft fft;
    readonly NativeBuffer re;
    readonly NativeBuffer im;
    readonly NativeBuffer loudnessWeights;

    public double SampleRate { get; }

    /// <summary>IR length in samples.</summary>
    public int Taps { get; }

    /// <summary>Frequency grid size: four times the IR, so the cepstrum's aliasing and the paths' delays fit.</summary>
    public int FftSize { get; }

    public CabIrSynthesizer(double sampleRate) {
        SampleRate = sampleRate;
        Taps = Math.Max(256, (int)Math.Round(ReferenceTaps * sampleRate / ReferenceRate));
        FftSize = 4 * (int)BitOperations.RoundUpToPowerOf2((uint)Taps);
        fft = new(FftSize);
        re = new(FftSize);
        im = new(FftSize);
        loudnessWeights = new(FftSize / 2 + 1);
        var weights = loudnessWeights.Span;
        var high = Math.Min(LoudnessHighHz, 0.45 * sampleRate);
        for (var k = 1; k < weights.Length; k++) {
            var hz = BinHz(k);
            weights[k] = hz < LoudnessLowHz || hz > high ? 0f : (float)LoudnessWeight(hz);
        }
    }

    /// <summary>
    ///     Loudness weight per Hz for a guitar-like (pink) input heard through ITU-R BS.1770's K filter: its RLB
    ///     high-pass (38 Hz) and +4 dB shelf above about 1.5 kHz, divided by f for the pink spectrum.
    /// </summary>
    public static double LoudnessWeight(double hz) {
        var highPass = Math.Pow(hz / 38, 4) / (1 + Math.Pow(hz / 38, 4));
        var shelf = (1 + Math.Pow(10, 0.4) * (hz / 1_500) * (hz / 1_500)) / (1 + hz / 1_500 * (hz / 1_500));
        return highPass * shelf / hz;
    }

    public double BinHz(int bin) => bin * SampleRate / FftSize;

    /// <summary>
    ///     Writes the miked speaker's IR, loudness-normalised, into <paramref name="ir" /> (<see cref="Taps" /> long).
    ///     Returns the direct sound's travel time in seconds, which the IR itself does not include.
    /// </summary>
    public double Synthesize(in MicPlacement placement, Span<float> ir) {
        var acoustics = new CabAcoustics(placement);
        var half = FftSize / 2;
        var re = this.re.Span;
        for (var k = 0; k <= half; k++) {
            var db = Math.Max(FloorDb, acoustics.MagnitudeDb(Math.Max(BinHz(k), 1)));
            re[k] = (float)(db * Math.Log(10) / 20);
        }

        MinimumPhase();
        var im = this.im.Span;
        for (var k = 1; k <= half; k++) {
            var path = acoustics.Paths(BinHz(k));
            var r = re[k] * path.Real - im[k] * path.Imaginary;
            var i = re[k] * path.Imaginary + im[k] * path.Real;
            re[k] = (float)r;
            im[k] = (float)i;
        }

        Finish(ir);
        return acoustics.DirectDelay;
    }

    /// <summary>
    ///     Writes the IR interpolated from <paramref name="grid" /> at <paramref name="position" /> (0 cap … 1 edge)
    ///     and
    ///     <paramref name="distance" /> (inches), loudness-normalised. Returns its onset delay in seconds.
    /// </summary>
    public double Synthesize(CabGrid grid, double position, double distance, Span<float> ir) {
        var re = this.re.Span;
        var delay = grid.LogMagnitude(position, distance, SampleRate, re[..(FftSize / 2 + 1)]);
        MinimumPhase();
        Finish(ir);
        return delay;
    }

    public void Dispose() {
        fft.Dispose();
        re.Dispose();
        im.Dispose();
        loudnessWeights.Dispose();
    }

    /// <summary>
    ///     <c>re[0..N/2]</c> holds a natural-log magnitude; leaves the minimum-phase spectrum with that magnitude in
    ///     <c>re</c>/<c>im</c>, bins 0 … N/2.
    /// </summary>
    void MinimumPhase() => fft.MinimumPhase(re.Span, im.Span);

    /// <summary>Normalises bins 0 … N/2 to unit loudness, fills the mirror half, and takes the first taps.</summary>
    void Finish(Span<float> ir) {
        var n = FftSize;
        var half = n / 2;
        var re = this.re.Span;
        var im = this.im.Span;
        var weights = loudnessWeights.Span;
        re[0] = 0f;
        im[0] = 0f;
        im[half] = 0f;
        double loudness = 0, total = 0;
        for (var k = 1; k <= half; k++) {
            loudness += weights[k] * ((double)re[k] * re[k] + (double)im[k] * im[k]);
            total += weights[k];
        }

        var scale = loudness > 1e-30 ? (float)Math.Sqrt(total / loudness) : 0f;
        for (var k = 1; k < half; k++) {
            re[n - k] = re[k];
            im[n - k] = -im[k];
        }

        fft.Inverse(re, im);
        var taps = Math.Min(Taps, ir.Length);
        for (var i = 0; i < taps; i++) {
            ir[i] = re[i] * scale;
        }

        for (var i = 0; i < FadeLength; i++) {
            ir[taps - FadeLength + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / FadeLength));
        }

        ir[taps..].Clear();
    }
}
