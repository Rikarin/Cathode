using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Phaser: a chain of first-order TPT allpass stages whose break frequency is swept by an LFO (in octaves around the
///     centre), with feedback around the chain. Mixed 50/50 with the dry signal, every 180° of chain phase cuts a notch.
/// </summary>
public sealed class PhaserBlock : DspBlock {
    public const int Rate = 0;
    public const int Depth = 1;
    public const int Center = 2;
    public const int Stages = 3;
    public const int Feedback = 4;
    public const int Mix = 5;
    public const int Spread = 6;
    public const int Level = 7;

    const int MaxStages = 12;
    const float MaxSweepOctaves = 2f;
    static readonly int[] StageCounts = [2, 4, 6, 8, 10, 12];

    readonly float[] stateLeft = new float[MaxStages];
    readonly float[] stateRight = new float[MaxStages];
    Lfo lfo;
    float lastLeft;
    float lastRight;
    float mix;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "phaser",
        "Phaser",
        "Modulation",
        [
            ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 80f, "%"),
            ParamDescriptor.Float("center", "Center", 100f, 4_000f, 800f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("stages", "Stages", ["2", "4", "6", "8", "10", "12"], 1),
            ParamDescriptor.Float("feedback", "Feedback", -95f, 95f, 0f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "Spread", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public PhaserBlock()
        : base(Info) { }

    /// <summary>TPT one-pole gain G = g / (1 + g) with g = tan(pi f / fs).</summary>
    static float Gain(float frequency, double sampleRate) {
        var g = MathF.Tan(MathF.PI * frequency / (float)sampleRate);
        return g / (1 + g);
    }

    /// <summary>First-order allpasses: low-pass via TPT, then allpass = 2 * lowpass - input.</summary>
    static float Chain(float[] state, int stages, float x, float gain) {
        for (var s = 0; s < stages; s++) {
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
        lastLeft = 0;
        lastRight = 0;
        this.mix = Params.Current(Mix) / 100f;
        this.outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var lfoRate = Params.Advance(Rate, io.Length);
        var octaves = Params.Advance(Depth, io.Length) / 100f * MaxSweepOctaves;
        var center = Params.Advance(Center, io.Length);
        var stages = StageCounts[(int)Params.Current(Stages)];
        var feedback = Params.Advance(Feedback, io.Length) / 100f;
        var spread = Params.Advance(Spread, io.Length) / 360.0;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;
        var nyquistGuard = (float)(rate * 0.45);

        for (var i = 0; i < io.Length; i++) {
            var fl = Math.Min(nyquistGuard, center * MathF.Pow(2f, octaves * lfo.Value(LfoShape.Sine)));
            var fr = Math.Min(nyquistGuard, center * MathF.Pow(2f, octaves * lfo.Value(LfoShape.Sine, spread)));
            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;

            var left = io.Left[i];
            var right = io.Right[i];
            lastLeft = Chain(stateLeft, stages, left + feedback * lastLeft, Gain(fl, rate));
            lastRight = Chain(stateRight, stages, right + feedback * lastRight, Gain(fr, rate));
            io.Left[i] = left * (1 - currentMix) + lastLeft * currentMix;
            io.Right[i] = right * (1 - currentMix) + lastRight * currentMix;
        }

        Flush(stateLeft);
        Flush(stateRight);
        this.mix = mix;
        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }
}
