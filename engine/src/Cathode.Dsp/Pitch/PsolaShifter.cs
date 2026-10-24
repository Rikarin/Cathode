using System.Numerics;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Real-time TD-PSOLA (pitch-synchronous overlap-add; Moulines and Charpentier, 1990) with several synthesis voices
///     sharing one analysis buffer.
/// </summary>
/// <remarks>
///     <para>
///         Analysis marks sit one input period T apart. For each voice, synthesis marks sit T / ratio apart; at each
///         synthesis mark the analysis mark just before it (less a fixed delay) supplies a two-period Hann grain, which
///         is overlap-added into the voice's output. Raising the pitch repeats grains, lowering it skips them. Because
///         every grain is a whole local waveform, the spectral envelope (the formants) is preserved, which is why
///         PSOLA sounds natural on harmonically rich guitar tones; a pure sine has no harmonics to resample and loses
///         level on large shifts.
///     </para>
///     <para>
///         A grain reaches one period ahead of its centre, and its source must be fully in the past when its first
///         sample is due, so the fixed analysis delay is two periods of the lowest tracked note: about 29 ms at
///         70 Hz. That is the voices' wet path delay. Without a period (unvoiced input, chords) it falls back to 5 ms
///         grains, a granular shifter. Monophonic; pitch-mark alignment to waveform peaks is not modelled.
///     </para>
///     <para>
///         Formant shift: <see cref="Read(int, double, double, double)" /> resamples each grain by the formant ratio r
///         (the grain's waveform played r times faster, over 2T / r samples), which moves the spectral envelope by r and
///         leaves the pitch, set by the grains' spacing, alone (the classic PSOLA formant shift). The source still spans
///         two periods, so the analysis delay does not change; the grains' overlap, and with it the gain, does.
///     </para>
/// </remarks>
public sealed class PsolaShifter(int voiceCount) : IDisposable {
    const double FallbackPeriodSeconds = 0.005;

    /// <summary>The lowest formant ratio <see cref="Read(int, double, double, double)" /> takes (an octave down).</summary>
    public const double MinFormantRatio = 0.5;

    /// <summary>The highest formant ratio (an octave up).</summary>
    public const double MaxFormantRatio = 2;

    readonly Voice[] voices = [..Enumerable.Range(0, voiceCount).Select(static _ => new Voice())];
    NativeBuffer input = NativeBuffer.Empty;
    int inputMask;
    long now = -1;
    double minPeriod;
    double maxPeriod;
    double fallbackPeriod;

    /// <summary>Delay between a grain's source and its synthesis position, in samples.</summary>
    public double AnalysisDelay { get; private set; }

    /// <summary>Control thread: sizes the buffers for <paramref name="sampleRate" />.</summary>
    public void Prepare(double sampleRate) {
        minPeriod = sampleRate / PitchTracker.MaxFrequency;
        maxPeriod = Math.Ceiling(sampleRate / PitchTracker.MinFrequency);
        fallbackPeriod = FallbackPeriodSeconds * sampleRate;
        AnalysisDelay = 2 * maxPeriod + 2;

        // Sources reach back to the delay plus two periods; grains reach two periods ahead.
        input.Dispose();
        input = new((int)BitOperations.RoundUpToPowerOf2((uint)(AnalysisDelay + 4 * maxPeriod + 8)));
        inputMask = input.Length - 1;
        // A grain reaches two half-lengths ahead: two periods, or four at the lowest formant ratio.
        foreach (var voice in voices) {
            voice.Allocate((int)BitOperations.RoundUpToPowerOf2((uint)(2 / MinFormantRatio * maxPeriod + 8)));
        }

        Reset();
    }

    public void Reset() {
        input.Clear();
        now = -1;
        foreach (var voice in voices) {
            voice.Reset();
        }
    }

    /// <summary>Silences voice <paramref name="index" /> and forgets its marks, for a voice that resumes after being skipped.</summary>
    public void ResetVoice(int index) => voices[index].Reset();

    /// <summary>Writes the next input sample. Call once per sample, before reading the voices.</summary>
    public void Write(float sample) {
        now++;
        input[(int)(now & inputMask)] = MathF.Abs(sample) < 1e-20f ? 0f : sample;
    }

    /// <summary>
    ///     Returns voice <paramref name="index" />'s output for the sample just written, shifted by
    ///     <paramref name="ratio" /> (2 = an octave up). <paramref name="period" /> is the input period in samples, or 0
    ///     when unknown.
    /// </summary>
    public float Read(int index, double ratio, double period) => Read(index, ratio, period, 1);

    /// <summary>
    ///     As <see cref="Read(int, double, double)" />, with the spectral envelope moved by
    ///     <paramref name="formantRatio" /> (1 = kept, 2 = an octave up; clamped to an octave either way).
    /// </summary>
    public float Read(int index, double ratio, double period, double formantRatio) {
        var voice = voices[index];
        formantRatio = Math.Clamp(formantRatio, MinFormantRatio, MaxFormantRatio);
        var t = period > 0 ? Math.Clamp(period, minPeriod, maxPeriod) : fallbackPeriod;
        ratio = Math.Clamp(ratio, 0.25, 4);
        if (!voice.Started || voice.NextMark < now - t) {
            voice.Start(now + t); // first use, or resuming after the voice sat idle
        }

        // A grain is placed when its first sample is due: one period ahead, or its longer half-length when lowering
        // the formants.
        var lead = formantRatio < 1 ? t / formantRatio : t;
        while (voice.NextMark - lead <= now) {
            if (formantRatio == 1) {
                PlaceGrain(voice, voice.NextMark, t, ratio);
            } else {
                PlaceGrain(voice, voice.NextMark, t, ratio, formantRatio);
            }

            voice.NextMark += t / ratio;
        }

        return voice.Take(now);
    }

    public void Dispose() {
        input.Dispose();
        input = NativeBuffer.Empty;
        foreach (var voice in voices) {
            voice.Dispose();
        }
    }

    void PlaceGrain(Voice voice, double centre, double t, double ratio) {
        // Analysis marks advance in whole periods, so consecutive grains are phase-coherent with each other.
        var target = centre - AnalysisDelay;
        if (voice.AnalysisMark > target || target - voice.AnalysisMark > 4 * maxPeriod) {
            voice.AnalysisMark = target;
        }

        while (voice.AnalysisMark + t <= target) {
            voice.AnalysisMark += t;
        }

        // Overlapping Hann windows sum to the ratio; spread-out grains (lowering) are boosted towards equal power.
        var gain = (float)(ratio >= 1 ? 1 / ratio : 1 / Math.Sqrt(ratio));
        var first = Math.Max((long)Math.Ceiling(centre - t), now);
        var last = (long)Math.Floor(centre + t);
        var scale = Math.PI / t;
        for (var j = first; j <= last; j++) {
            var offset = j - centre;
            var window = (float)(0.5 + 0.5 * Math.Cos(offset * scale));
            voice.Add(j, gain * window * Interpolate(voice.AnalysisMark + offset));
        }
    }

    /// <summary>
    ///     A grain resampled by <paramref name="formant" />: its source spans the same two periods, played over
    ///     2T / formant samples, with the gain for the overlap of the shorter or longer grains.
    /// </summary>
    void PlaceGrain(Voice voice, double centre, double t, double ratio, double formant) {
        // A longer grain is placed earlier, so its source sits further back by the extra half-length.
        var half = t / formant;
        var target = centre - AnalysisDelay - Math.Max(0, half - t);
        if (voice.AnalysisMark > target || target - voice.AnalysisMark > 4 * maxPeriod) {
            voice.AnalysisMark = target;
        }

        while (voice.AnalysisMark + t <= target) {
            voice.AnalysisMark += t;
        }

        var overlap = ratio / formant;
        var gain = (float)(overlap >= 1 ? 1 / overlap : 1 / Math.Sqrt(overlap));
        var first = Math.Max((long)Math.Ceiling(centre - half), now);
        var last = (long)Math.Floor(centre + half);
        var scale = Math.PI / half;
        for (var j = first; j <= last; j++) {
            var offset = j - centre;
            var window = (float)(0.5 + 0.5 * Math.Cos(offset * scale));
            voice.Add(j, gain * window * Interpolate(voice.AnalysisMark + offset * formant));
        }
    }

    float Interpolate(double position) {
        var index = (long)Math.Floor(position);
        var frac = (float)(position - index);
        var a = input[(int)(index & inputMask)];
        var b = input[(int)((index + 1) & inputMask)];
        return a + (b - a) * frac;
    }

    sealed class Voice : IDisposable {
        NativeBuffer output = NativeBuffer.Empty;
        int mask;

        public bool Started { get; private set; }

        public double NextMark { get; set; }

        public double AnalysisMark { get; set; }

        public void Allocate(int length) {
            output.Dispose();
            output = new(length);
            mask = length - 1;
        }

        public void Reset() {
            output.Clear();
            Started = false;
            NextMark = 0;
            AnalysisMark = double.MaxValue;
        }

        public void Start(double firstMark) {
            Started = true;
            NextMark = firstMark;
        }

        public void Add(long time, float value) => output[(int)(time & mask)] += value;

        public float Take(long time) {
            ref var slot = ref output[(int)(time & mask)];
            var value = slot;
            slot = 0;
            return MathF.Abs(value) < 1e-20f ? 0f : value;
        }

        public void Dispose() {
            output.Dispose();
            output = NativeBuffer.Empty;
        }
    }
}
