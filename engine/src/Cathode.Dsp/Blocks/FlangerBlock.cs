using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Flanger: a very short swept delay with positive or negative feedback, mixed with the dry signal, giving
///     a moving comb filter. The right channel's sweep is offset by the spread angle.
/// </summary>
public sealed class FlangerBlock : DspBlock {
    public const int Rate = 0;
    public const int Depth = 1;
    public const int Delay = 2;
    public const int Feedback = 3;
    public const int Mix = 4;
    public const int Spread = 5;
    public const int Level = 6;

    const float MaxSweepMs = 7f;
    const float MaxDelayMs = 10f;

    readonly FractionalDelayLine left = new();
    readonly FractionalDelayLine right = new();
    Lfo lfo;
    float mix;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "flanger",
        "Flanger",
        "Modulation",
        [
            ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.3f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 70f, "%"),
            ParamDescriptor.Float("delay", "Delay", 0.1f, MaxDelayMs, 1f, "ms"),
            ParamDescriptor.Float("feedback", "Feedback", -95f, 95f, 50f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "Spread", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public FlangerBlock()
        : base(Info) { }

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)((MaxDelayMs + MaxSweepMs) * 0.001 * spec.SampleRate) + 8;
        this.left.Allocate(capacity);
        this.right.Allocate(capacity);
    }

    protected override void OnReset() {
        this.left.Reset();
        this.right.Reset();
        lfo.Reset();
        this.mix = Params.Current(Mix) / 100f;
        this.outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var lfoRate = Params.Advance(Rate, io.Length);
        var sweep = Params.Advance(Depth, io.Length) / 100f * MaxSweepMs * 0.001f * (float)rate;
        var baseDelay = Params.Advance(Delay, io.Length) * 0.001f * (float)rate;
        var feedback = Params.Advance(Feedback, io.Length) / 100f;
        var spread = Params.Advance(Spread, io.Length) / 360.0;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var i = 0; i < io.Length; i++) {
            var dl = baseDelay + sweep * 0.5f * (1 + lfo.Value(LfoShape.Triangle));
            var dr = baseDelay + sweep * 0.5f * (1 + lfo.Value(LfoShape.Triangle, spread));
            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;

            var left = io.Left[i];
            var right = io.Right[i];
            var wetLeft = this.left.Read(dl); // read before writing, so a delay of N is exactly N samples
            var wetRight = this.right.Read(dr);
            this.left.Write(left + wetLeft * feedback);
            this.right.Write(right + wetRight * feedback);
            io.Left[i] = left * (1 - currentMix) + wetLeft * currentMix;
            io.Right[i] = right * (1 - currentMix) + wetRight * currentMix;
        }

        this.mix = mix;
        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }
}
