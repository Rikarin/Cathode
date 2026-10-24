using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Low-latency time-domain pitch shifter: a delay line read by a tap whose delay drifts at <c>1 - speed</c> samples
///     per sample, so the tap plays the input back at <c>speed</c> times its rate. When the tap reaches the end of its
///     range it splices: a second tap starts one splice length away and the two crossfade.
/// </summary>
/// <remarks>
///     <para>
///         With a period hint from <see cref="PitchTracker" /> the splice length is a whole number of input periods
///         (at least 8 ms), so the two taps read the same point of the waveform and the splice is phase-coherent: no
///         comb filtering or warble on a single note. Without a hint (chords, noise) it falls back to a 25 ms splice and
///         a longer crossfade, the classic dual-tap shifter. The tap stays between a few samples and two splice lengths
///         behind the input: shifting up it sweeps one splice (8-25 ms), shifting down up to two, so the wet path delay
///         averages 5-15 ms and peaks near 50 ms for unvoiced downward shifts. At unison it holds still at ~13 ms.
///     </para>
///     <para>
///         <see cref="SetSplice" /> lengthens the minimum splice (the Pitch block's splice length): longer splices
///         warble less on sustained notes and smear more on fast passages; the unvoiced fallback stays at least 2.5
///         times the minimum.
///     </para>
///     <para>
///         A resampler, so the formants move with the pitch; <see cref="FormantCorrector" /> around it keeps them. Not
///         modelled: splice-point search by cross-correlation.
///     </para>
/// </remarks>
public sealed class SpliceShifter : IDisposable {
    /// <summary>The shortest minimum splice <see cref="SetSplice" /> takes.</summary>
    public const double MinSpliceSeconds = 0.008;

    /// <summary>The longest minimum splice <see cref="SetSplice" /> takes.</summary>
    public const double MaxSpliceSeconds = 0.04;

    const double FallbackSpliceSeconds = 0.025;
    const double FallbackRatio = 2.5;
    const double VoicedFadeSeconds = 0.005;
    const double FallbackFadeSeconds = 0.015;

    readonly FractionalDelayLine line = new();
    double sampleRate;
    double minSplice;
    double fallbackSplice;
    int voicedFade;
    int fallbackFade;
    double maxDelay;
    double delay;
    double fadingDelay;
    int fadeLength;
    int fadeLeft;

    /// <summary>Control thread: sizes the delay line for <paramref name="sampleRate" />.</summary>
    public void Prepare(double sampleRate) {
        this.sampleRate = sampleRate;
        minSplice = MinSpliceSeconds * sampleRate;
        fallbackSplice = FallbackSpliceSeconds * sampleRate;
        voicedFade = (int)(VoicedFadeSeconds * sampleRate);
        fallbackFade = (int)(FallbackFadeSeconds * sampleRate);

        // The longest splice is one period of the lowest tracked note, two minimum splices, or the fallback, at the
        // longest splice setting.
        var maxSplice = MaxSpliceSeconds * sampleRate;
        var longest = Math.Max(
            Math.Max(sampleRate / PitchTracker.MinFrequency, 2 * maxSplice),
            Math.Max(fallbackSplice, FallbackRatio * maxSplice)
        );
        maxDelay = 2 * longest + 16;
        line.Allocate((int)maxDelay + 8);
        Reset();
    }

    /// <summary>Audio thread: the minimum splice, from <see cref="MinSpliceSeconds" /> to <see cref="MaxSpliceSeconds" />.</summary>
    public void SetSplice(double seconds) {
        var splice = Math.Clamp(seconds, MinSpliceSeconds, MaxSpliceSeconds) * sampleRate;
        minSplice = splice;
        fallbackSplice = Math.Max(FallbackSpliceSeconds * sampleRate, FallbackRatio * splice);
    }

    public void Reset() {
        line.Reset();
        delay = 4 + fallbackSplice / 2;
        fadingDelay = delay;
        fadeLength = 0;
        fadeLeft = 0;
    }

    /// <summary>
    ///     Processes one sample at playback rate <paramref name="speed" /> (2 = an octave up, 0.5 = an octave down).
    ///     <paramref name="period" /> is the input period in samples, or 0 when unknown.
    /// </summary>
    public float Process(float input, double speed, double period) {
        line.Write(input);
        var drift = 1 - speed;
        delay += drift;
        if (fadeLeft > 0) {
            fadingDelay += drift;
        } else if (drift != 0) {
            var voiced = period > 0;
            var splice = voiced ? period * Math.Ceiling(minSplice / period) : fallbackSplice;
            var fade = (int)Math.Min(voiced ? voicedFade : fallbackFade, splice / (2 * Math.Abs(drift)));
            fade = Math.Max(fade, 1);

            // The outgoing tap keeps drifting through the crossfade, so the floor leaves it room to stay readable.
            var floor = FractionalDelayLine.MinDelay + 1 + Math.Abs(drift) * (fade + 1);
            if (drift < 0 && delay < floor) {
                StartSplice(delay + splice, fade);
            } else if (drift > 0 && delay > floor + splice) {
                StartSplice(delay - splice, fade);
            }
        }

        delay = Math.Clamp(delay, FractionalDelayLine.MinDelay, maxDelay);
        var output = line.Read((float)delay);
        if (fadeLeft == 0) {
            return output;
        }

        // Raised-cosine crossfade from the outgoing tap to the new one.
        var outgoing = 0.5f - 0.5f * MathF.Cos(MathF.PI * fadeLeft / fadeLength);
        fadeLeft--;
        fadingDelay = Math.Clamp(fadingDelay, FractionalDelayLine.MinDelay, maxDelay);
        return output + (line.Read((float)fadingDelay) - output) * outgoing;
    }

    public void Dispose() => line.Dispose();

    void StartSplice(double to, int fade) {
        fadingDelay = delay;
        delay = to;
        fadeLength = fade;
        fadeLeft = fade;
    }
}
