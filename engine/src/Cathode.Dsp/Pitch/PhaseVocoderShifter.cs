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
        mask = FrameSize - 1;
        for (var i = 0; i < FrameSize; i++) {
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FrameSize));
        }

        // Hann analysis and synthesis at 4x overlap sum to 1.5: undo it.
        scale = 1f / 1.5f;
        Reset();
    }

    /// <summary>
    ///     One FFT per size for every shifter in the process: its tables are read-only after construction, so the
    ///     instances share them, and a block's Prepare does not allocate them again.
    /// </summary>
    static Fft SharedFft(int size) {
        lock (FftLock) {
            if (!Ffts.TryGetValue(size, out var fft)) {
                fft = new(size);
                Ffts[size] = fft;
            }

            return fft;
        }
    }

    public void Reset() {
        input.Clear();
        output.Clear();
        lastPhase.Clear();
        sumPhase.Clear();
        count = 0;
    }

    /// <summary>One sample in at shift <paramref name="ratio" />, one sample out (a frame later).</summary>
    public float Process(float sample, double ratio) {
        var at = (int)(count & mask);
        input[at] = sample;
        var result = output[at];
        output[at] = 0;
        count++;
        if (count % hop == 0) {
            Frame(ratio);
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
                     peaks
                 ]) {
            buffer.Dispose();
        }

        input = output = window = re = im =
            lastPhase = sumPhase = magnitude = frequency = synthesisPhase = peaks = NativeBuffer.Empty;
    }

    /// <summary>Analyses the last frame, moves its partials by <paramref name="ratio" /> and overlap-adds the result.</summary>
    void Frame(double ratio) {
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
                reals[j] += (float)(magnitude[k] * Math.Cos(phase));
                imaginaries[j] += (float)(magnitude[k] * Math.Sin(phase));
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
}
