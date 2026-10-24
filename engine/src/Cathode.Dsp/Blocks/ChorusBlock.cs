using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Chorus: short delays swept by an LFO, mixed with the dry signal. Each side reads its line with 1 to 4 voices whose
///     LFOs are spread evenly around the cycle (summed at equal power), and the right channel's LFO is offset by the spread
///     angle for width. The wet signal has its own low and high cut and can be phase-inverted per side.
/// </summary>
public sealed class ChorusBlock() : DspBlock(Info) {
    public const int Rate = 0;
    public const int Depth = 1;
    public const int Delay = 2;
    public const int Mix = 3;
    public const int Spread = 4;
    public const int Level = 5;
    public const int Balance = 6;
    public const int Tempo = 7;
    public const int LfoType = 8;
    public const int Voices = 9;
    public const int LowCut = 10;
    public const int HighCut = 11;
    public const int PhaseReverse = 12;
    public const int GlobalMix = 13;
    public const int StereoSpread = 14;

    public const int MaxVoices = 4;

    const float MaxDepthMs = 5f;
    const float MaxDelayMs = 30f;

    /// <summary>Wet polarity per side for each Phase Reverse option (None, Left, Right, Both).</summary>
    public static readonly string[] PhaseOptions = ["None", "Left", "Right", "Both"];

    readonly FractionalDelayLine left = new();
    readonly FractionalDelayLine right = new();
    Lfo lfo;
    WetCuts cuts;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "chorus",
        "Chorus",
        "Modulation",
        [
            ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.8f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("delay", "Delay", 1f, MaxDelayMs, 8f, "ms"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "LFO Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Choice("lfoType", "LFO Type", Lfo.ShapeNames),
            ParamDescriptor.Choice("voices", "Voices", ["1", "2", "3", "4"]),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            ParamDescriptor.Choice("phaseReverse", "Phase Reverse", PhaseOptions),
            EffectsMix.Param,
            Stereo.SpreadParam()
        ],
        maxInstances: 2
    );

    /// <summary>Wet polarity of the left and right side for a Phase Reverse setting.</summary>
    public static (float Left, float Right) Polarity(int phaseReverse) =>
        (phaseReverse is 1 or 3 ? -1f : 1f, phaseReverse is 2 or 3 ? -1f : 1f);

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)((MaxDelayMs + MaxDepthMs) * 0.001 * spec.SampleRate) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
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
        var depth = Params.Advance(Depth, io.Length) / 100f * MaxDepthMs * 0.001f * (float)rate;
        var baseDelay = Params.Advance(Delay, io.Length) * 0.001f * (float)rate;
        var spread = Params.Advance(Spread, io.Length) / 360.0;
        var mix = EffectsMix.Apply(Params.Advance(Mix, io.Length), Params.Current(GlobalMix), context) / 100f;
        var shape = (LfoShape)(int)Params.Current(LfoType);
        var voices = (int)Params.Current(Voices) + 1;
        var voiceGain = 1f / MathF.Sqrt(voices);
        var (polarityLeft, polarityRight) = Polarity((int)Params.Current(PhaseReverse));
        cuts.Design(rate, Params.Advance(LowCut, io.Length), Params.Advance(HighCut, io.Length));
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;
        var stereoSpread = Params.Advance(StereoSpread, io.Length) / 100f;

        for (var i = 0; i < io.Length; i++) {
            float wetLeft;
            float wetRight;
            if (voices == 1) {
                // Read before writing, so a delay of N is exactly N samples.
                wetLeft = left.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape)));
                wetRight = right.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape, spread)));
            } else {
                wetLeft = 0;
                wetRight = 0;
                for (var v = 0; v < voices; v++) {
                    var offset = (double)v / voices;
                    wetLeft += left.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape, offset)));
                    wetRight += right.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape, offset + spread)));
                }

                wetLeft *= voiceGain;
                wetRight *= voiceGain;
            }

            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;
            cuts.Tick(ref wetLeft, ref wetRight);
            Stereo.Spread(ref wetLeft, ref wetRight, stereoSpread);

            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            left.Write(dryLeft);
            right.Write(dryRight);
            io.Left[i] = dryLeft * (1 - currentMix) + polarityLeft * wetLeft * currentMix;
            io.Right[i] = dryRight * (1 - currentMix) + polarityRight * wetRight * currentMix;
        }

        cuts.FlushDenormals();
        this.mix = mix;
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
