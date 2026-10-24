using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Uni-Vibe-style photocell phaser. Four first-order allpass stages with the Shin-ei network's unequal capacitors
///     (15 nF, 220 nF, 470 pF, 4.7 nF) share one lamp: every stage's light-dependent resistor sees the same light, so
///     the four break frequencies move together but sit far apart, giving the Vibe's lopsided, throaty notches instead
///     of a phaser's evenly spaced ones. A sine LFO drives the lamp; the lamp's power law skews the drive towards short
///     bright peaks, and the lamp and photocell respond faster to rising light than to falling light, which rounds the
///     sweep into its characteristic lurching shape. Chorus mode mixes the chain with the dry signal; vibrato mode is
///     the chain alone (pure phase modulation, heard as pitch wobble).
/// </summary>
public sealed class VibeBlock() : DspBlock(Info) {
    public const int Mode = 0;
    public const int Rate = 1;
    public const int Intensity = 2;
    public const int Mix = 3;
    public const int Spread = 4;
    public const int Level = 5;
    public const int Balance = 6;

    public const int StageCount = 4;

    /// <summary>Break frequency of the first (15 nF) stage with the lamp at its average brightness.</summary>
    public const float RestFrequency = 250f;

    /// <summary>Sweep range of the first stage at 100 % intensity, in octaves.</summary>
    public const float SweepOctaves = 4f;

    /// <summary>Mean of the lamp drive <c>u^2</c> over a sine cycle, where the sweep is centred.</summary>
    const float MeanLight = 0.375f;

    /// <summary>Lamp and photocell time constant for rising light, in seconds.</summary>
    const float AttackSeconds = 0.006f;

    /// <summary>Time constant for falling light: the photocell is slow to recover its dark resistance.</summary>
    const float DecaySeconds = 0.04f;

    readonly float[] stateLeft = new float[StageCount];
    readonly float[] stateRight = new float[StageCount];
    Lfo lfo;
    float lightLeft;
    float lightRight;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "vibe",
        "Vibe",
        "Modulation",
        [
            ParamDescriptor.Choice("mode", "Mode", ["Chorus", "Vibrato"]),
            ParamDescriptor.Float("rate", "Rate", 0.1f, 12f, 3f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("intensity", "Intensity", 0f, 100f, 75f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "Spread", 0f, 180f, 0f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 2
    );

    /// <summary>
    ///     Each stage's break frequency relative to the first: inversely proportional to its capacitor, as all four
    ///     photocells have the same resistance.
    /// </summary>
    public static ReadOnlySpan<float> StageRatios => [1f, 15f / 220f, 15f / 0.47f, 15f / 4.7f];

    /// <summary>The asymmetric lamp: incandescent power rises with the square of the drive, then lags the light.</summary>
    static float Lamp(float light, float lfo, float attack, float decay) {
        var u = 0.5f * (1 + lfo);
        var drive = u * u;
        return light + (drive - light) * (drive > light ? attack : decay);
    }

    /// <summary>The four TPT first-order allpasses (allpass = 2 * lowpass - input), all set by the same photocell.</summary>
    static float Chain(float[] state, float x, float firstStage, float nyquistGuard, double sampleRate) {
        var ratios = StageRatios;
        for (var s = 0; s < StageCount; s++) {
            var frequency = Math.Min(nyquistGuard, firstStage * ratios[s]);
            var g = MathF.Tan(MathF.PI * frequency / (float)sampleRate);
            var gain = g / (1 + g);
            var v = (x - state[s]) * gain;
            var lowpass = v + state[s];
            state[s] = lowpass + v;
            x = 2 * lowpass - x;
        }

        return x;
    }

    static void Flush(float[] state) {
        for (var s = 0; s < state.Length; s++) {
            if (MathF.Abs(state[s]) < 1e-20f) {
                state[s] = 0;
            }
        }
    }

    protected override void OnReset() {
        Array.Clear(stateLeft);
        Array.Clear(stateRight);
        lfo.Reset();
        lightLeft = MeanLight;
        lightRight = MeanLight;
        mix = TargetMix();
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var n = io.Length;
        var lfoRate = Params.Advance(Rate, n);
        var octaves = Params.Advance(Intensity, n) / 100f * SweepOctaves;
        var spread = Params.Advance(Spread, n) / 360.0;
        Params.Advance(Mix, n);
        var mix = TargetMix();
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        var attack = 1 - MathF.Exp(-1 / (AttackSeconds * (float)rate));
        var decay = 1 - MathF.Exp(-1 / (DecaySeconds * (float)rate));
        var nyquistGuard = (float)(rate * 0.45);

        for (var i = 0; i < n; i++) {
            lightLeft = Lamp(lightLeft, lfo.Value(LfoShape.Sine), attack, decay);
            lightRight = Lamp(lightRight, lfo.Value(LfoShape.Sine, spread), attack, decay);
            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;

            // More light, lower photocell resistance, higher break frequencies.
            var firstLeft = RestFrequency * MathF.Pow(2f, octaves * (lightLeft - MeanLight));
            var firstRight = RestFrequency * MathF.Pow(2f, octaves * (lightRight - MeanLight));
            var left = io.Left[i];
            var right = io.Right[i];
            var wetLeft = Chain(stateLeft, left, firstLeft, nyquistGuard, rate);
            var wetRight = Chain(stateRight, right, firstRight, nyquistGuard, rate);
            io.Left[i] = left + (wetLeft - left) * currentMix;
            io.Right[i] = right + (wetRight - right) * currentMix;
        }

        Flush(stateLeft);
        Flush(stateRight);
        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }

    /// <summary>Vibrato is the chain alone; chorus blends it with the dry signal.</summary>
    float TargetMix() => Params.Current(Mode) >= 0.5f ? 1f : Params.Current(Mix) / 100f;
}
