using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Chorus: a short delay swept by an LFO, mixed with the dry signal. The right channel's LFO is offset by
///     the spread angle for width.
/// </summary>
public sealed class ChorusBlock() : DspBlock(Info) {
    public const int Rate = 0;
    public const int Depth = 1;
    public const int Delay = 2;
    public const int Mix = 3;
    public const int Spread = 4;
    public const int Level = 5;

    const float MaxDepthMs = 5f;
    const float MaxDelayMs = 30f;

    readonly FractionalDelayLine left = new();
    readonly FractionalDelayLine right = new();
    Lfo lfo;
    float mix;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "chorus",
        "Chorus",
        "Modulation",
        [
            ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.8f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("delay", "Delay", 1f, MaxDelayMs, 8f, "ms"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "Spread", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)((MaxDelayMs + MaxDepthMs) * 0.001 * spec.SampleRate) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        lfo.Reset();
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var lfoRate = Params.Advance(Rate, io.Length);
        var depth = Params.Advance(Depth, io.Length) / 100f * MaxDepthMs * 0.001f * (float)rate;
        var baseDelay = Params.Advance(Delay, io.Length) * 0.001f * (float)rate;
        var spread = Params.Advance(Spread, io.Length) / 360.0;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var i = 0; i < io.Length; i++) {
            var dl = baseDelay + depth * 0.5f * (1 + lfo.Value(LfoShape.Sine));
            var dr = baseDelay + depth * 0.5f * (1 + lfo.Value(LfoShape.Sine, spread));
            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;

            var left = io.Left[i];
            var right = io.Right[i];
            var wetLeft = this.left.Read(dl); // read before writing, so a delay of N is exactly N samples
            var wetRight = this.right.Read(dr);
            this.left.Write(left);
            this.right.Write(right);
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
