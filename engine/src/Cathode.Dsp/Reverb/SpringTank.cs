using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Reverb;

/// <summary>
///     A tank of one to six springs after Välimäki, Parker and Abel, "Parametric spring reverberation effect" (JAES
///     2010): in each spring a cascade of stretched first-order allpasses, (a + z^-K) / (1 + a z^-K), disperses a click
///     into the characteristic falling chirp, a low-pass keeps the band below the spring's cut-off, and a feedback delay
///     makes the chirps repeat and decay.
/// </summary>
/// <remarks>
///     <para>
///         With a negative a, the group delay of each stage falls from K(1 - a)/(1 + a) samples at DC to K(1 + a)/(1 - a)
///         at the cut-off fs/2K, so low frequencies arrive last. The springs differ in length and dispersion; the even
///         ones feed the left output and the odd ones the right (one spring feeds both), each side normalised by the root
///         of its spring count. The feedback gain comes from the decay time and the loop length, including the cascade's
///         mean group delay. The paper's separate high-chirp branch is not modelled.
///     </para>
///     <para>
///         Tone moves the springs' low-pass an octave either way of its natural 4.1 kHz. Drive saturates what enters
///         the springs (tanh, unity for small signals). Boing makes the tank react to attacks as a kicked spring does: a
///         fast peak envelope against a slow average detects transients and drives the springs up to 5× harder while they
///         last, so picked notes "boing" and sustained ones do not.
///     </para>
/// </remarks>
sealed class SpringTank : IDisposable {
    public const int MaxSprings = 6;

    const float CutoffHz = 4_300f;
    const int Stages = 80;
    const float MaxSizeScale = 1.5f;
    const float MaxBoingGain = 4f;

    static readonly float[] LoopMs = [33.1f, 39.7f, 36.7f, 43.9f, 30.7f, 41.3f];
    static readonly float[] Dispersion = [-0.62f, -0.57f, -0.6f, -0.55f, -0.64f, -0.59f];

    readonly Spring[] springs = [new(), new(), new(), new(), new(), new()];
    double rate;
    SvfCoefficients lowPass;
    double tone = double.NaN;
    int count = 2;
    float leftNorm = 1f;
    float rightNorm = 1f;
    float drive;
    float boing;
    float fastEnvelope;
    float slowEnvelope;
    float fastRelease;
    float slowCoefficient;

    public void Allocate(double sampleRate) {
        rate = sampleRate;
        var stretch = Math.Max(1, (int)Math.Round(sampleRate / (2 * CutoffHz)));
        tone = double.NaN;
        Voice(2, 1, 0, 0);
        fastRelease = MathF.Exp(-1f / (0.002f * (float)sampleRate));
        slowCoefficient = 1f - MathF.Exp(-1f / (0.1f * (float)sampleRate));
        for (var s = 0; s < springs.Length; s++) {
            springs[s].Allocate(stretch, (int)(LoopMs[s] * MaxSizeScale * 0.001 * sampleRate) + 4);
        }
    }

    public void Clear() {
        foreach (var spring in springs) {
            spring.Clear();
        }

        fastEnvelope = 0;
        slowEnvelope = 0;
    }

    public void Dispose() {
        foreach (var spring in springs) {
            spring.Dispose();
        }
    }

    /// <summary>Once per block: decay time in seconds and the spring length scale.</summary>
    public void Configure(float decaySeconds, float sizeScale) {
        for (var s = 0; s < count; s++) {
            springs[s].Configure(Dispersion[s], (int)(LoopMs[s] * sizeScale * 0.001 * rate), decaySeconds, rate);
        }
    }

    /// <summary>
    ///     Once per block: <paramref name="springCount" /> springs (1..6), the low-pass at <paramref name="toneScale" />
    ///     times its natural cut-off, input <paramref name="drive" /> (0 = clean) and <paramref name="boingAmount" /> (0..1).
    /// </summary>
    public void Voice(int springCount, double toneScale, float drive, float boingAmount) {
        springCount = Math.Clamp(springCount, 1, MaxSprings);
        if (springCount != count) {
            // Springs that join start silent rather than replaying what they held when last used.
            for (var s = count; s < springCount; s++) {
                springs[s].Clear();
            }

            count = springCount;
            leftNorm = 1f / MathF.Sqrt((count + 1) / 2);
            rightNorm = count == 1 ? leftNorm : 1f / MathF.Sqrt(count / 2);
        }

        if (toneScale != tone) {
            tone = toneScale;
            lowPass = SvfCoefficients.Design(BiquadType.LowPass, rate, CutoffHz * 0.95 * toneScale, 0.7071);
        }

        this.drive = drive;
        boing = boingAmount;
    }

    public (float Left, float Right) Tick(float x) {
        if (drive > 0) {
            var gain = 1f + drive;
            x = MathF.Tanh(x * gain) / gain;
        }

        if (boing > 0) {
            var level = MathF.Abs(x);
            fastEnvelope = level > fastEnvelope ? level : fastEnvelope * fastRelease;
            slowEnvelope += (level - slowEnvelope) * slowCoefficient;
            var transient = Math.Max(0f, fastEnvelope - 2f * slowEnvelope) / (slowEnvelope + 1e-3f);
            x *= 1f + boing * Math.Min(MaxBoingGain, transient);
        }

        if (count == 2) {
            return (springs[0].Tick(x, lowPass) * leftNorm, springs[1].Tick(x, lowPass) * rightNorm);
        }

        float left = 0, right = 0;
        for (var s = 0; s < count; s++) {
            var y = springs[s].Tick(x, lowPass);
            if (s % 2 == 0) {
                left += y;
            } else {
                right += y;
            }
        }

        return count == 1 ? (left * leftNorm, left * leftNorm) : (left * leftNorm, right * rightNorm);
    }

    /// <summary>Once per block, after the ticks.</summary>
    public void FlushDenormals() {
        if (fastEnvelope < 1e-20f) {
            fastEnvelope = 0;
        }

        if (slowEnvelope < 1e-20f) {
            slowEnvelope = 0;
        }
    }

    sealed class Spring : IDisposable {
        readonly ReverbLine loop = new();
        NativeBuffer inputs = NativeBuffer.Empty;
        NativeBuffer outputs = NativeBuffer.Empty;
        SvfState filter;
        int stretch;
        int position;
        int loopLength = 1;
        float a;
        float feedback;

        public void Allocate(int stretch, int maxLoop) {
            this.stretch = stretch;
            inputs.Dispose();
            outputs.Dispose();
            inputs = new(Stages * stretch);
            outputs = new(Stages * stretch);
            loop.Allocate(maxLoop);
            position = 0;
        }

        public void Clear() {
            inputs.Clear();
            outputs.Clear();
            loop.Clear();
            filter.Reset();
            position = 0;
        }

        public void Dispose() {
            inputs.Dispose();
            outputs.Dispose();
            loop.Dispose();
        }

        public void Configure(float dispersion, int length, float decaySeconds, double rate) {
            filter.FlushDenormals();
            a = dispersion;
            loopLength = Math.Clamp(length, 1, loop.Length - 1);

            // Mean group delay of the cascade across the passband, between its DC and cut-off values.
            var dc = stretch * (1 - a) / (1 + a);
            var top = stretch * (1 + a) / (1 - a);
            var round = loopLength + Stages * 0.5 * (dc + top);
            feedback = (float)Math.Pow(10, -3 * round / (decaySeconds * rate));
        }

        public float Tick(float x, in SvfCoefficients lowPass) {
            var v = x + feedback * loop.Tap(loopLength);

            // Every stage delays by the same K, so one cursor walks all their histories.
            var inputs = this.inputs.Span;
            var outputs = this.outputs.Span;
            for (int stage = 0, i = position; stage < Stages; stage++, i += stretch) {
                var y = a * v + inputs[i] - a * outputs[i];
                inputs[i] = v;
                outputs[i] = MathF.Abs(y) < 1e-20f ? 0f : y;
                v = y;
            }

            if (++position == stretch) {
                position = 0;
            }

            var output = (float)filter.Tick(lowPass, v);
            loop.Write(output);
            return output;
        }
    }
}
