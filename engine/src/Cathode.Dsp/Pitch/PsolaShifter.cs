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
///         grains, a granular shifter. Monophonic; formant shifting and pitch-mark alignment to waveform peaks are not
///         modelled.
///     </para>
/// </remarks>
public sealed class PsolaShifter(int voiceCount) : IDisposable {
    const double FallbackPeriodSeconds = 0.005;

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
        foreach (var voice in voices) {
            voice.Allocate((int)BitOperations.RoundUpToPowerOf2((uint)(2 * maxPeriod + 8)));
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
    public float Read(int index, double ratio, double period) {
        var voice = voices[index];
        var t = period > 0 ? Math.Clamp(period, minPeriod, maxPeriod) : fallbackPeriod;
        ratio = Math.Clamp(ratio, 0.25, 4);
        if (!voice.Started || voice.NextMark < now - t) {
            voice.Start(now + t); // first use, or resuming after the voice sat idle
        }

        while (voice.NextMark - t <= now) {
            PlaceGrain(voice, voice.NextMark, t, ratio);
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
