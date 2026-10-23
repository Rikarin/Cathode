using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Reverb;

/// <summary>
///     A two-spring tank after Välimäki, Parker and Abel, "Parametric spring reverberation effect" (JAES 2010): in each
///     spring a cascade of stretched first-order allpasses, (a + z^-K) / (1 + a z^-K), disperses a click into the
///     characteristic falling chirp, a low-pass keeps the band below the spring's cut-off, and a feedback delay makes
///     the chirps repeat and decay.
/// </summary>
/// <remarks>
///     With a negative a, the group delay of each stage falls from K(1 - a)/(1 + a) samples at DC to K(1 + a)/(1 - a) at
///     the cut-off fs/2K, so low frequencies arrive last. The two springs differ in length and dispersion, which
///     decorrelates left and right. The feedback gain comes from the decay time and the loop length, including the
///     cascade's mean group delay. The paper's separate high-chirp branch is not modelled.
/// </remarks>
sealed class SpringTank : IDisposable {
    const float CutoffHz = 4_300f;
    const int Stages = 80;
    const float MaxSizeScale = 1.5f;

    static readonly float[] LoopMs = [33.1f, 39.7f];
    static readonly float[] Dispersion = [-0.62f, -0.57f];

    readonly Spring[] springs = [new(), new()];
    double rate;
    SvfCoefficients lowPass;

    public void Allocate(double sampleRate) {
        rate = sampleRate;
        var stretch = Math.Max(1, (int)Math.Round(sampleRate / (2 * CutoffHz)));
        lowPass = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, CutoffHz * 0.95, 0.7071);
        for (var s = 0; s < springs.Length; s++) {
            springs[s].Allocate(stretch, (int)(LoopMs[s] * MaxSizeScale * 0.001 * sampleRate) + 4);
        }
    }

    public void Clear() {
        foreach (var spring in springs) {
            spring.Clear();
        }
    }

    public void Dispose() {
        foreach (var spring in springs) {
            spring.Dispose();
        }
    }

    /// <summary>Once per block: decay time in seconds and the spring length scale.</summary>
    public void Configure(float decaySeconds, float sizeScale) {
        for (var s = 0; s < springs.Length; s++) {
            springs[s].Configure(Dispersion[s], (int)(LoopMs[s] * sizeScale * 0.001 * rate), decaySeconds, rate);
        }
    }

    public (float Left, float Right) Tick(float x) => (springs[0].Tick(x, lowPass), springs[1].Tick(x, lowPass));

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
