using Cathode.Dsp.Convolution;
using System.Numerics;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Formant correction around a time-domain pitch shifter (<see cref="SpliceShifter" />): linear prediction whitens
///     the input before the shifter, and the input's own spectral envelope is put back on its output, so the formants
///     stay where they were while the pitch moves. An optional formant shift moves the re-imposed envelope.
/// </summary>
/// <remarks>
///     <para>
///         Each hop (half a Hann frame of about 21 ms, scaled with the sample rate) the last frame's power
///         spectrum gives its autocorrelation, and <see cref="SpectralEnvelope" /> turns that into the reflection
///         coefficients of an all-pole envelope (order 24 at 48 kHz, a 120 Hz lag window so it passes over single
///         harmonics, a -50 dB white-noise floor that bounds how hard it whitens). The whitening filter A(z) runs as a
///         lattice FIR on the input (<see cref="Whiten" />), the envelope 1/A(z) as a lattice IIR on the shifted signal
///         (<see cref="Color" />); both ramp their coefficients across each hop, which keeps the all-pole lattice
///         stable throughout.
///     </para>
///     <para>
///         With a formant shift of ratio r the envelope comes instead from the same power spectrum stretched in
///         frequency by r, P(f / r) (one inverse FFT carries both autocorrelations, the plain one in the real part and
///         the stretched one in the imaginary), with a gain that keeps the total power. The envelope put back is the one
///         of the input as it is now, while the shifted signal lags it by the shifter's delay (5-15 ms on average):
///         slow next to how a vowel or a guitar's tone changes, but at a sudden change of spectrum (a pick attack after
///         silence, a held note into noise) the old residual meets the new envelope. A level guard keeps the output
///         within 6 dB of the input's recent peak for those moments. The analysis frame does not delay the audio.
///     </para>
/// </remarks>
public sealed class FormantCorrector : IDisposable {
    const int Overlap = 2;
    const float MaxBoost = 2f; // the level guard: output peaks at most 6 dB over the input's
    const double InputReleaseSeconds = 0.1; // holds the input's peak across the shifter's delay
    const double OutputReleaseSeconds = 0.02;

    Fft fft = PhaseVocoderShifter.SharedFft(2);
    NativeBuffer input = NativeBuffer.Empty;
    NativeBuffer window = NativeBuffer.Empty;
    NativeBuffer re = NativeBuffer.Empty;
    NativeBuffer im = NativeBuffer.Empty;
    NativeBuffer power = NativeBuffer.Empty;
    double[] lag = [];
    double[] plain = [];
    double[] stretched = [];
    double[] poly = [];
    double[] scratch = [];
    float[] analysisK = [];
    float[] analysisStep = [];
    float[] synthesisK = [];
    float[] synthesisStep = [];
    float[] targetK = [];
    float[] analysisState = [];
    float[] synthesisState = [];
    int size;
    int mask;
    int hop;
    long count;
    float gain = 1f;
    float gainStep;
    float inputPeak;
    float outputPeak;
    float inputRelease;
    float outputRelease;
    double shift = 1;

    /// <summary>The all-pole envelope's order.</summary>
    public int Order { get; private set; }

    /// <summary>
    ///     Control thread: sizes the frame (1024 samples up to 50 kHz, then doubled) and the order for
    ///     <paramref name="sampleRate" />.
    /// </summary>
    public void Prepare(double sampleRate) {
        Dispose();
        size = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(256, 0.02 * sampleRate));
        mask = size - 1;
        hop = size / Overlap;
        fft = PhaseVocoderShifter.SharedFft(size);
        input = new(size);
        window = new(size);
        re = new(size);
        im = new(size);
        power = new(size / 2 + 1);
        for (var i = 0; i < size; i++) {
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size));
        }

        Order = SpectralEnvelope.Order(sampleRate);
        lag = new double[Order + 1];
        SpectralEnvelope.LagWindow(lag, sampleRate);
        plain = new double[Order + 1];
        stretched = new double[Order + 1];
        poly = new double[Order + 1];
        scratch = new double[Order + 1];
        analysisK = new float[Order];
        analysisStep = new float[Order];
        synthesisK = new float[Order];
        synthesisStep = new float[Order];
        targetK = new float[Order];
        analysisState = new float[Order];
        synthesisState = new float[Order];
        inputRelease = (float)Math.Exp(-1 / (InputReleaseSeconds * sampleRate));
        outputRelease = (float)Math.Exp(-1 / (OutputReleaseSeconds * sampleRate));
        Reset();
    }

    /// <summary>
    ///     Audio thread: the formant shift as a frequency ratio (1 = keep the formants, 2 = an octave up), from the next
    ///     hop.
    /// </summary>
    public void SetShift(double ratio) => shift = Math.Clamp(ratio, 0.25, 4);

    public void Reset() {
        input.Clear();
        Array.Clear(analysisK);
        Array.Clear(analysisStep);
        Array.Clear(synthesisK);
        Array.Clear(synthesisStep);
        Array.Clear(analysisState);
        Array.Clear(synthesisState);
        gain = 1f;
        gainStep = 0;
        inputPeak = 0;
        outputPeak = 0;
        count = 0;
    }

    /// <summary>
    ///     The next input sample through the whitening filter (the prediction residual), for the shifter. Call once per
    ///     sample, before <see cref="Color" />.
    /// </summary>
    public float Whiten(float sample) {
        input[(int)(count & mask)] = sample;
        count++;
        if (count % hop == 0) {
            Analyse();
        }

        var magnitude = MathF.Abs(sample);
        inputPeak = magnitude > inputPeak ? magnitude : inputPeak * inputRelease;
        var order = Order;
        var forward = sample;
        var backward = sample;
        for (var m = 0; m < order; m++) {
            analysisK[m] += analysisStep[m];
            synthesisK[m] += synthesisStep[m];
            var k = analysisK[m];
            var old = analysisState[m];
            analysisState[m] = backward;
            var next = forward + k * old;
            backward = k * forward + old;
            forward = next;
        }

        gain += gainStep;
        return MathF.Abs(forward) < 1e-20f ? 0f : forward;
    }

    /// <summary>
    ///     The shifter's output through the envelope (the all-pole lattice), with the formant shift's gain and the level
    ///     guard.
    /// </summary>
    public float Color(float sample) {
        var forward = sample * gain;
        for (var m = Order - 1; m >= 0; m--) {
            var k = synthesisK[m];
            forward -= k * synthesisState[m];
            if (m + 1 < Order) {
                synthesisState[m + 1] = k * forward + synthesisState[m];
            }
        }

        synthesisState[0] = forward;
        var magnitude = MathF.Abs(forward);
        outputPeak = magnitude > outputPeak ? magnitude : outputPeak * outputRelease;
        var limit = MaxBoost * inputPeak;
        var result = outputPeak > limit ? forward * (limit / outputPeak) : forward;
        return MathF.Abs(result) < 1e-20f ? 0f : result;
    }

    /// <summary>Zeroes the subnormal state, once per block.</summary>
    public void FlushDenormals() {
        for (var m = 0; m < Order; m++) {
            if (MathF.Abs(analysisState[m]) < 1e-20f) {
                analysisState[m] = 0;
            }

            if (MathF.Abs(synthesisState[m]) < 1e-20f) {
                synthesisState[m] = 0;
            }
        }

        if (inputPeak < 1e-20f) {
            inputPeak = 0;
        }

        if (outputPeak < 1e-20f) {
            outputPeak = 0;
        }
    }

    public void Dispose() {
        foreach (var buffer in (ReadOnlySpan<NativeBuffer>)[input, window, re, im, power]) {
            buffer.Dispose();
        }

        input = window = re = im = power = NativeBuffer.Empty;
    }

    /// <summary>The envelope of the last frame: the whitening filter's and the (shifted) envelope's next coefficients.</summary>
    void Analyse() {
        var n = size;
        var bins = n / 2 + 1;
        var reals = re.Span;
        var imaginaries = im.Span;
        for (var i = 0; i < n; i++) {
            reals[i] = input[(int)((count + i) & mask)] * window[i];
            imaginaries[i] = 0;
        }

        fft.Forward(reals, imaginaries);
        for (var k = 0; k < bins; k++) {
            power[k] = reals[k] * reals[k] + imaginaries[k] * imaginaries[k];
        }

        // Both power spectra (real, even) in one inverse: the plain one gives the real part, the stretched the imaginary.
        var stretch = shift != 1;
        for (var k = 0; k < bins; k++) {
            reals[k] = power[k];
            imaginaries[k] = stretch ? PowerAt(k / shift, bins) : 0f;
        }

        for (var k = 1; k < n / 2; k++) {
            reals[n - k] = reals[k];
            imaginaries[n - k] = imaginaries[k];
        }

        fft.Inverse(reals, imaginaries);
        for (var l = 0; l <= Order; l++) {
            plain[l] = reals[l] * lag[l];
            stretched[l] = (stretch ? imaginaries[l] : reals[l]) * lag[l];
        }

        plain[0] *= 1 + SpectralEnvelope.NoiseFloor;
        stretched[0] *= 1 + SpectralEnvelope.NoiseFloor;
        var target = 1.0;
        if (plain[0] < SpectralEnvelope.SilencePower || stretched[0] < SpectralEnvelope.SilencePower) {
            Array.Clear(targetK); // silence: no filtering
            Ramp(analysisK, analysisStep);
        } else {
            var error = SpectralEnvelope.Levinson(plain, poly, scratch, targetK);
            Ramp(analysisK, analysisStep);
            if (stretch) {
                var stretchedError = SpectralEnvelope.Levinson(stretched, poly, scratch, targetK);
                target = Math.Sqrt(stretchedError / error * (plain[0] / stretched[0]));
            }
        }

        Ramp(synthesisK, synthesisStep);

        gainStep = (float)((target - gain) / hop);
    }

    /// <summary>The power spectrum at fractional bin <paramref name="bin" />, by linear interpolation; nothing past Nyquist.</summary>
    float PowerAt(double bin, int bins) {
        var index = (int)bin;
        if (index >= bins - 1) {
            return 0f;
        }

        var frac = (float)(bin - index);
        return power[index] + (power[index + 1] - power[index]) * frac;
    }

    /// <summary>Steps <paramref name="current" /> towards <see cref="targetK" /> over the next hop.</summary>
    void Ramp(float[] current, float[] step) {
        for (var m = 0; m < Order; m++) {
            step[m] = (targetK[m] - current[m]) / hop;
        }
    }
}
