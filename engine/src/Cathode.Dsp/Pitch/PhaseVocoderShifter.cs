using Cathode.Dsp.Convolution;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Polyphonic pitch shifter: a phase vocoder that moves every partial of a chord by the same ratio, where the
///     time-domain shifters (which follow one period) fall apart.
/// </summary>
/// <remarks>
///     <para>
///         Short-time Fourier analysis with a Hann window of <see cref="FrameSize" /> samples (scaled with the sample
///         rate, about 43 ms) and four-fold overlap. Each bin's true frequency comes from its phase advance between
///         frames. The spectrum's peaks each own a region of bins; every region moves rigidly to where its peak's
///         frequency times the ratio falls, the peak's phase advancing at its new frequency and the other bins keeping
///         their phase against the peak (Laroche and Dolson's peak-locked pitch shifting, 1999), so a partial's whole
///         window lobe moves intact and keeps its level. The frames are resynthesised with the same window and
///         overlap-added with the Hann-squared normalisation.
///     </para>
///     <para>
///         Formant correction (<see cref="Process(float, double, double)" />): each frame's all-pole envelope
///         (<see cref="SpectralEnvelope" />, order 24 at 48 kHz) is fitted to its power spectrum, and every bin that
///         moves from k to j is scaled by the envelope at j (or at j / r for a formant shift of ratio r) over the envelope
///         at k, at most 52 dB either way: the partials take the input's formants at their new frequencies. Two more
///         FFTs per hop; the envelope cannot raise what the input lacks, so shifting down an octave leaves the top of
///         the output as thin as the input's top octave was.
///     </para>
///     <para>
///         The output lags the input by one frame (the wet path delay). Transients smear by up to a frame, and with
///         large shifts the partials of a chord can phase against each other ("phasiness"), the known costs of the
///         method. Buffers live in native memory; analysis runs once per hop, driven by a sample counter, so the output
///         does not depend on how the host slices the audio.
///     </para>
/// </remarks>
public sealed class PhaseVocoderShifter : IDisposable {
    const int Overlap = 4;

    /// <summary>Peaks below this, relative to the frame size (a full-scale sine is a quarter of it), are noise floor.</summary>
    const float PeakFloor = 1e-7f;

    /// <summary>The most a formant correction raises or cuts a bin, in nepers (about 52 dB).</summary>
    const float MaxCorrection = 6f;

    static readonly Lock FftLock = new();
    static readonly Dictionary<int, Fft> Ffts = [];

    Fft fft = SharedFft(2);
    NativeBuffer input = NativeBuffer.Empty;
    NativeBuffer output = NativeBuffer.Empty;
    NativeBuffer window = NativeBuffer.Empty;
    NativeBuffer re = NativeBuffer.Empty;
    NativeBuffer im = NativeBuffer.Empty;
    NativeBuffer lastPhase = NativeBuffer.Empty;
    NativeBuffer sumPhase = NativeBuffer.Empty;
    NativeBuffer magnitude = NativeBuffer.Empty;
    NativeBuffer frequency = NativeBuffer.Empty;
    NativeBuffer synthesisPhase = NativeBuffer.Empty;
    NativeBuffer peaks = NativeBuffer.Empty; // bin indices, exact in float
    NativeBuffer envelopeRe = NativeBuffer.Empty;
    NativeBuffer envelopeIm = NativeBuffer.Empty;
    NativeBuffer envelope = NativeBuffer.Empty; // the all-pole envelope's natural-log magnitude per bin
    double[] lag = [];
    double[] autocorrelation = [];
    double[] poly = [];
    double[] scratch = [];
    int mask;
    int hop;
    long count;
    float scale;

    /// <summary>The analysis frame length in samples, and the wet path delay.</summary>
    public int FrameSize { get; private set; }

    /// <summary>Control thread: sizes the frames for <paramref name="sampleRate" /> (2048 samples up to 50 kHz, then doubled).</summary>
    public void Prepare(double sampleRate) {
        Dispose();
        FrameSize = sampleRate > 100_000 ? 8_192 : sampleRate > 50_000 ? 4_096 : 2_048;
        hop = FrameSize / Overlap;
        fft = SharedFft(FrameSize);
        input = new(FrameSize);
        output = new(FrameSize);
        window = new(FrameSize);
        re = new(FrameSize);
        im = new(FrameSize);
        var bins = FrameSize / 2 + 1;
        lastPhase = new(bins);
        sumPhase = new(bins);
        magnitude = new(bins);
        frequency = new(bins);
        synthesisPhase = new(bins);
        peaks = new(bins);
        envelopeRe = new(FrameSize);
        envelopeIm = new(FrameSize);
        envelope = new(bins);
        var order = SpectralEnvelope.Order(sampleRate);
        lag = new double[order + 1];
        SpectralEnvelope.LagWindow(lag, sampleRate);
        autocorrelation = new double[order + 1];
        poly = new double[order + 1];
        scratch = new double[order + 1];
        mask = FrameSize - 1;
        for (var i = 0; i < FrameSize; i++) {
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FrameSize));
        }

        // Hann analysis and synthesis at 4x overlap sum to 1.5: undo it.
        scale = 1f / 1.5f;
        Reset();
    }

    public void Reset() {
        input.Clear();
        output.Clear();
        lastPhase.Clear();
        sumPhase.Clear();
        count = 0;
    }

    /// <summary>One sample in at shift <paramref name="ratio" />, one sample out (a frame later).</summary>
    public float Process(float sample, double ratio) => Process(sample, ratio, 0);

    /// <summary>
    ///     One sample in at shift <paramref name="ratio" /> with formant correction: the spectral envelope stays put,
    ///     moved by <paramref name="formantRatio" /> (1 = kept where it was); 0 turns the correction off.
    /// </summary>
    public float Process(float sample, double ratio, double formantRatio) {
        var at = (int)(count & mask);
        input[at] = sample;
        var result = output[at];
        output[at] = 0;
        count++;
        if (count % hop == 0) {
            Frame(ratio, formantRatio);
        }

        return result;
    }

    public void Dispose() {
        foreach (var buffer in (ReadOnlySpan<NativeBuffer>) [
                     input,
                     output,
                     window,
                     re,
                     im,
                     lastPhase,
                     sumPhase,
                     magnitude,
                     frequency,
                     synthesisPhase,
                     peaks,
                     envelopeRe,
                     envelopeIm,
                     envelope
                 ]) {
            buffer.Dispose();
        }

        input = output = window = re = im = lastPhase = sumPhase = magnitude = frequency = synthesisPhase = peaks =
            envelopeRe = envelopeIm = envelope = NativeBuffer.Empty;
    }

    /// <summary>
    ///     One FFT per size for every shifter in the process: its tables are read-only after construction, so the
    ///     instances share them, and a block's Prepare does not allocate them again.
    /// </summary>
    internal static Fft SharedFft(int size) {
        lock (FftLock) {
            if (!Ffts.TryGetValue(size, out var fft)) {
                fft = new(size);
                Ffts[size] = fft;
            }

            return fft;
        }
    }

    /// <summary>
    ///     Analyses the last frame, moves its partials by <paramref name="ratio" /> and overlap-adds the result; with a
    ///     <paramref name="formantRatio" /> above 0 every moved bin takes the level of the (shifted) envelope where it lands.
    /// </summary>
    void Frame(double ratio, double formantRatio) {
        var n = FrameSize;
        var bins = n / 2 + 1;
        var start = count; // the frame is the n samples before count
        var reals = re.Span;
        var imaginaries = im.Span;
        for (var i = 0; i < n; i++) {
            reals[i] = input[(int)((start + i) & mask)] * window[i];
            imaginaries[i] = 0;
        }

        fft.Forward(reals, imaginaries);

        // Analysis: magnitude and true frequency (in bins) of every bin.
        var expected = 2 * Math.PI * hop / n; // phase advance per hop of bin 1
        for (var k = 0; k < bins; k++) {
            var phase = Math.Atan2(imaginaries[k], reals[k]);
            var delta = phase - lastPhase[k] - k * expected;
            lastPhase[k] = (float)phase;
            delta -= 2 * Math.PI * Math.Round(delta / (2 * Math.PI));
            magnitude[k] = MathF.Sqrt(reals[k] * reals[k] + imaginaries[k] * imaginaries[k]);
            frequency[k] = (float)(k + delta / expected);
        }

        var correct = formantRatio > 0;
        if (correct) {
            Envelope();
        }

        // Peaks: the local maxima of the magnitude, each owning the bins halfway to its neighbours.
        var floor = PeakFloor * n;
        var peakCount = 0;
        for (var k = 1; k < bins - 1; k++) {
            if (magnitude[k] > floor && magnitude[k] > magnitude[k - 1] && magnitude[k] >= magnitude[k + 1]) {
                peaks[peakCount++] = k;
            }
        }

        // Synthesis: every region moves rigidly to where its peak's shifted frequency falls; the peak's phase advances
        // at that frequency and the region's other bins keep their phase against it (identity phase locking).
        reals.Clear();
        imaginaries.Clear();
        synthesisPhase.Clear();
        for (var q = 0; q < peakCount; q++) {
            var p = (int)peaks[q];
            var low = q == 0 ? 1 : ((int)peaks[q - 1] + p) / 2 + 1;
            var high = q == peakCount - 1 ? bins - 1 : (p + (int)peaks[q + 1]) / 2;
            var shifted = frequency[p] * ratio;
            var target = (int)Math.Round(shifted);
            if (target <= 0 || target >= bins) {
                continue;
            }

            var shift = target - p;
            var peakPhase = sumPhase[target] + shifted * expected;
            for (var k = low; k <= high; k++) {
                var j = k + shift;
                if (j <= 0 || j >= bins) {
                    continue;
                }

                var phase = peakPhase + (lastPhase[k] - lastPhase[p]);
                var level = correct ? magnitude[k] * Correction(k, j, formantRatio, bins) : magnitude[k];
                reals[j] += (float)(level * Math.Cos(phase));
                imaginaries[j] += (float)(level * Math.Sin(phase));
                synthesisPhase[j] = (float)(phase - 2 * Math.PI * Math.Floor(phase / (2 * Math.PI)));
            }
        }

        synthesisPhase.Span.CopyTo(sumPhase.Span);

        // Conjugate-symmetric upper half, so the inverse is real.
        for (var k = 1; k < n / 2; k++) {
            reals[n - k] = reals[k];
            imaginaries[n - k] = -imaginaries[k];
        }

        imaginaries[0] = 0;
        imaginaries[n / 2] = 0;
        fft.Inverse(reals, imaginaries);

        // Overlap-add one frame ahead: the slot of sample i of the frame is read again n samples after it came in.
        for (var i = 0; i < n; i++) {
            ref var slot = ref output[(int)((start + i) & mask)];
            slot += reals[i] * window[i] * scale;
        }
    }

    /// <summary>
    ///     The frame's spectral envelope (<see cref="SpectralEnvelope" />): the autocorrelation from the power spectrum,
    ///     the all-pole fit, and its log magnitude 0.5 ln(E / |A|²) at every bin from one more FFT of the polynomial.
    /// </summary>
    void Envelope() {
        var n = FrameSize;
        var bins = n / 2 + 1;
        var reals = envelopeRe.Span;
        var imaginaries = envelopeIm.Span;
        for (var k = 0; k < bins; k++) {
            reals[k] = magnitude[k] * magnitude[k];
        }

        for (var k = 1; k < n / 2; k++) {
            reals[n - k] = reals[k];
        }

        imaginaries.Clear();
        fft.Inverse(reals, imaginaries);
        for (var l = 0; l < autocorrelation.Length; l++) {
            autocorrelation[l] = reals[l] * lag[l];
        }

        autocorrelation[0] *= 1 + SpectralEnvelope.NoiseFloor;
        if (autocorrelation[0] < SpectralEnvelope.SilencePower) {
            envelope.Clear(); // silence: no correction
            return;
        }

        var error = SpectralEnvelope.Levinson(autocorrelation, poly, scratch, []);
        reals.Clear();
        imaginaries.Clear();
        for (var i = 0; i < poly.Length; i++) {
            reals[i] = (float)poly[i];
        }

        fft.Forward(reals, imaginaries);
        var level = 0.5f * MathF.Log((float)error);
        for (var k = 0; k < bins; k++) {
            envelope[k] = level - 0.5f * MathF.Log(reals[k] * reals[k] + imaginaries[k] * imaginaries[k] + 1e-30f);
        }
    }

    /// <summary>The gain that gives bin <paramref name="from" />, moved to <paramref name="to" />, the envelope there (shifted by <paramref name="formantRatio" />).</summary>
    float Correction(int from, int to, double formantRatio, int bins) {
        var source = Math.Min(to / formantRatio, bins - 1);
        var index = (int)source;
        var frac = (float)(source - index);
        var target = index + 1 < bins
            ? envelope[index] + (envelope[index + 1] - envelope[index]) * frac
            : envelope[index];
        return MathF.Exp(Math.Clamp(target - envelope[from], -MaxCorrection, MaxCorrection));
    }
}
