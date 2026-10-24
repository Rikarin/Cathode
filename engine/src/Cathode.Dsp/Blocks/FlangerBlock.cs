using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Flanger: a very short swept delay with positive or negative feedback, mixed with the dry signal, giving
///     a moving comb filter. The right channel's sweep is offset by the spread angle.
/// </summary>
public sealed class FlangerBlock : DspBlock, IEngageAware {
    public const int Rate = 0;
    public const int Depth = 1;
    public const int Delay = 2;
    public const int Feedback = 3;
    public const int Mix = 4;
    public const int Spread = 5;
    public const int Level = 6;
    public const int Balance = 7;
    public const int Tempo = 8;
    public const int LfoType = 9;
    public const int LowCut = 10;
    public const int HighCut = 11;
    public const int PhaseReverse = 12;
    public const int LfoReset = 13;
    public const int GlobalMix = 14;
    public const int StereoSpread = 15;

    const float MaxSweepMs = 7f;
    const float MaxDelayMs = 10f;

    readonly FractionalDelayLine left = new();
    readonly FractionalDelayLine right = new();
    Lfo lfo;
    WetCuts cuts;
    float mix;
    OutputStage output;

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
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Choice("lfoType", "LFO Type", Lfo.ShapeNames, (int)LfoShape.Triangle),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            ParamDescriptor.Choice("phaseReverse", "Phase Reverse", ChorusBlock.PhaseOptions),
            ParamDescriptor.Switch("lfoReset", "LFO Bypass Reset"),
            EffectsMix.Param,
            Stereo.SpreadParam()
        ],
        maxInstances: 2
    );

    public FlangerBlock()
        : base(Info) { }

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)((MaxDelayMs + MaxSweepMs) * 0.001 * spec.SampleRate) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
    }

    /// <summary>LFO Bypass Reset: engaging the block restarts the sweep from the top, so every engage sounds alike.</summary>
    public void Engage() {
        if (Params.GetTarget(LfoReset) >= 0.5f) {
            lfo.Reset();
        }
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        lfo.Reset();
        cuts.Reset();
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            left.Dispose();
            right.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var lfoRate = TempoDivisions.Rate((int)Params.Current(Tempo), context.Tempo, Params.Advance(Rate, io.Length));
        var shape = (LfoShape)(int)Params.Current(LfoType);
        var (polarityLeft, polarityRight) = ChorusBlock.Polarity((int)Params.Current(PhaseReverse));
        cuts.Design(rate, Params.Advance(LowCut, io.Length), Params.Advance(HighCut, io.Length));
        var sweep = Params.Advance(Depth, io.Length) / 100f * MaxSweepMs * 0.001f * (float)rate;
        var baseDelay = Params.Advance(Delay, io.Length) * 0.001f * (float)rate;
        var feedback = Params.Advance(Feedback, io.Length) / 100f;
        var spread = Params.Advance(Spread, io.Length) / 360.0;
        var mix = EffectsMix.Apply(Params.Advance(Mix, io.Length), Params.Current(GlobalMix), context) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;
        var stereoSpread = Params.Advance(StereoSpread, io.Length) / 100f;

        for (var i = 0; i < io.Length; i++) {
            var dl = baseDelay + sweep * 0.5f * (1 + lfo.Value(shape));
            var dr = baseDelay + sweep * 0.5f * (1 + lfo.Value(shape, spread));
            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;

            var left = io.Left[i];
            var right = io.Right[i];
            var wetLeft = this.left.Read(dl); // read before writing, so a delay of N is exactly N samples
            var wetRight = this.right.Read(dr);
            this.left.Write(left + wetLeft * feedback);
            this.right.Write(right + wetRight * feedback);
            cuts.Tick(ref wetLeft, ref wetRight);
            Stereo.Spread(ref wetLeft, ref wetRight, stereoSpread);
            io.Left[i] = left * (1 - currentMix) + polarityLeft * wetLeft * currentMix;
            io.Right[i] = right * (1 - currentMix) + polarityRight * wetRight * currentMix;
        }

        cuts.FlushDenormals();

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
