using Cathode.Dsp.Modulation;

namespace Cathode.Dsp.Reverb;

/// <summary>
///     The Diffuser page of the delays: four Schroeder allpasses per side, their lengths set by a Diffusion Time and
///     slowly modulated by a sine LFO, blended with the signal by a Diffusion Mix. It smears each echo into a short wash
///     without changing its level (an allpass chain is lossless), so a mix of 100 % is a "diffused delay".
/// </summary>
/// <remarks>
///     The stage lengths are fixed, mutually prime-ish fractions of the time; the right side's are 7 % longer, so the two
///     sides decorrelate. The LFO moves every length by up to <see cref="MaxModulation" /> of itself, with the stages in
///     quadrature so the modulation never lines up.
/// </remarks>
public sealed class Diffuser : IDisposable {
    public const float MaxModulation = 0.08f;
    const float Gain = 0.62f;
    const float RightStretch = 1.07f;
    const int Stages = 4;

    static readonly float[] Ratios = [1f, 0.753f, 0.531f, 0.367f];

    readonly ReverbLine[] left = [new(), new(), new(), new()];
    readonly ReverbLine[] right = [new(), new(), new(), new()];
    readonly float[] lengths = new float[Stages];
    readonly float[] rightLengths = new float[Stages];
    double sampleRate;
    float maxLength;
    float mix;
    float rate;
    float depth;
    Lfo lfo;

    /// <summary>False at a mix of zero: <see cref="Tick" /> then leaves the signal alone.</summary>
    public bool Active => mix > 0;

    /// <summary>Control thread: room for a Diffusion Time of up to <paramref name="maxTimeMs" />.</summary>
    public void Allocate(double sampleRate, float maxTimeMs) {
        this.sampleRate = sampleRate;
        maxLength = (float)(maxTimeMs * 0.001 * sampleRate);
        var capacity = (int)(maxLength * RightStretch * (1 + MaxModulation)) + 4;
        for (var i = 0; i < Stages; i++) {
            left[i].Allocate(capacity);
            right[i].Allocate(capacity);
        }
    }

    public void Clear() {
        for (var i = 0; i < Stages; i++) {
            left[i].Clear();
            right[i].Clear();
        }

        lfo.Reset();
    }

    public void Dispose() {
        for (var i = 0; i < Stages; i++) {
            left[i].Dispose();
            right[i].Dispose();
        }
    }

    /// <summary>Once per block: <paramref name="mix" /> and <paramref name="depth" /> are 0..1.</summary>
    public void Configure(float mix, float timeMs, float rateHz, float depth) {
        this.mix = mix;
        rate = rateHz;
        this.depth = depth * MaxModulation;
        var longest = Math.Clamp((float)(timeMs * 0.001 * sampleRate), 2f, maxLength);
        for (var i = 0; i < Stages; i++) {
            // Whole samples, so an unmodulated diffuser reads without interpolation and stays exactly lossless.
            lengths[i] = MathF.Round(Math.Max(2f, longest * Ratios[i]));
            rightLengths[i] = MathF.Round(lengths[i] * RightStretch);
        }
    }

    public void Tick(ref float l, ref float r) {
        if (!Active) {
            return;
        }

        lfo.Advance(rate, sampleRate);
        float dl = l, dr = r;
        for (var i = 0; i < Stages; i++) {
            var swing = depth == 0 ? 0f : depth * lfo.Value(LfoShape.Sine, 0.25 * i);
            dl = left[i].Allpass(dl, lengths[i] * (1 + swing), Gain);
            dr = right[i].Allpass(dr, rightLengths[i] * (1 + swing), Gain);
        }

        l += mix * (dl - l);
        r += mix * (dr - r);
    }
}
