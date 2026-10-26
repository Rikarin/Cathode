using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Reverb;

/// <summary>
///     The first-generation digital reverb: Schroeder's parallel feedback combs and series allpasses ("Natural sounding
///     artificial reverberation", JAES 1962) with Moorer's low-pass in every comb loop ("About this reverberation
///     business", CMJ 1979), heard through the narrow audio band of the first commercial units (the EMT 250 of 1976 and
///     its contemporaries ran at about a 10 kHz bandwidth).
/// </summary>
/// <remarks>
///     <para>
///         Six combs per side at Moorer's lengths (50 to 78 ms at size 50 %), the right side's 0.53 ms longer (the
///         spread Freeverb uses) so the sides decorrelate, then two allpasses per side at Schroeder's 5.0 and 1.7 ms.
///         Each comb's gain is <c>10^(-3 d / (T60 fs))</c> for its own length d, so every comb, and so the sum, decays at
///         the set T60; High Cut is the comb low-pass and the Low/High Freq Time splits sit in the same loop. The input
///         passes a 10 kHz second-order low-pass first. Input Diffusion scales the allpass gains (0.7 at 100 %) and its
///         Time their lengths.
///     </para>
///     <para>
///         This is the topology, not a model of one box: the EMT 250's own program is not published in enough detail to
///         copy. Its sound, a sparse, grainy build with a metallic edge at short sizes, is what parallel combs give.
///     </para>
/// </remarks>
sealed class CombReverb : IDisposable {
    const int Combs = 6;
    const int Allpasses = 2;
    const float SpreadMs = 0.53f;
    const float AllpassGain = 0.7f;
    const float BandwidthHz = 10_000f;
    const float InputScale = 0.2f;
    const float OutputScale = 0.5f;

    /// <summary>Moorer's comb lengths (ms) and Schroeder's allpass lengths (ms).</summary>
    static readonly float[] CombMs = [50f, 56f, 61f, 68f, 72f, 78f];

    static readonly float[] AllpassMs = [5.0f, 1.7f];

    // Lines: left combs, right combs, left allpasses, right allpasses.
    readonly LinePool lines = new(2 * (Combs + Allpasses));
    Twelve<TankLoss> filters;
    Twelve<int> combLengths;
    Four<float> allpassLengths;
    double rate;
    SvfCoefficients bandwidth;
    SvfState band;
    DecayShape shape;
    float allpassGain;
    bool stack;

    public void Allocate(double sampleRate, float maxScale, float maxInputTime) {
        rate = sampleRate;
        var ms = 0.001 * sampleRate;
        Span<int> sizes = stackalloc int[2 * (Combs + Allpasses)];
        for (var i = 0; i < 2 * Combs; i++) {
            sizes[i] = (int)((CombMs[i % Combs] + SpreadMs) * maxScale * ms) + 4;
        }

        for (var i = 0; i < 2 * Allpasses; i++) {
            sizes[2 * Combs + i] = (int)((AllpassMs[i % Allpasses] + SpreadMs) * maxInputTime * ms) + 4;
        }

        lines.Allocate(sizes);
        bandwidth = SvfCoefficients.Design(
            BiquadType.LowPass,
            sampleRate,
            Math.Min(BandwidthHz, 0.45 * sampleRate),
            0.7071
        );
        Clear();
    }

    public void Clear() {
        lines.Clear();
        band.Reset();
        for (var i = 0; i < 2 * Combs; i++) {
            filters[i].Clear();
        }
    }

    public void Dispose() => lines.Dispose();

    public void FlushDenormals() {
        band.FlushDenormals();
        for (var i = 0; i < 2 * Combs; i++) {
            filters[i].FlushDenormals();
        }
    }

    /// <summary>Once per block; <paramref name="diffusion" /> is a fraction (1 = 0.7 allpass gain).</summary>
    public void Configure(in DecayShape decay, float scale, float diffusion, float allpassTime, bool stack) {
        shape = decay;
        this.stack = stack;
        var ms = (float)(0.001 * rate);
        for (var i = 0; i < 2 * Combs; i++) {
            var length = (CombMs[i % Combs] + (i >= Combs ? SpreadMs : 0f)) * scale * ms;
            combLengths[i] = Math.Max(1, (int)MathF.Round(length));
            filters[i].Configure(combLengths[i], shape);
        }

        for (var i = 0; i < 2 * Allpasses; i++) {
            var length = (AllpassMs[i % Allpasses] + (i >= Allpasses ? SpreadMs : 0f)) * allpassTime * ms;
            allpassLengths[i] = Math.Clamp(MathF.Round(length), 1f, lines.Length(2 * Combs + i) - 2);
        }

        allpassGain = Math.Min(0.9f, AllpassGain * diffusion);
    }

    static float Limit(float x) => 4f * MathF.Tanh(0.25f * x);

    public (float Left, float Right) Tick(float x) {
        x = InputScale * (float)band.Tick(bandwidth, x);
        float left = 0, right = 0;
        for (var i = 0; i < 2 * Combs; i++) {
            var y = lines.Tap(i, combLengths[i]);
            var feedback = filters[i].Tick(y, shape);
            var value = x + feedback;
            lines.Write(i, stack ? Limit(value) : value);
            if (i < Combs) {
                left += y;
            } else {
                right += y;
            }
        }

        for (var i = 0; i < Allpasses; i++) {
            left = lines.Allpass(2 * Combs + i, left, allpassLengths[i], allpassGain);
            right = lines.Allpass(2 * Combs + Allpasses + i, right, allpassLengths[Allpasses + i], allpassGain);
        }

        return (left * OutputScale, right * OutputScale);
    }
}
