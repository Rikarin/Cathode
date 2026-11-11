using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using System.Collections.Immutable;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Chorus: short delays swept by an LFO, mixed with the dry signal. Each side reads its line with 1 to 8 voices
///     whose LFOs are spread evenly around the cycle (summed at equal power), and the right channel's LFO is offset by
///     the LFO Phase angle for width.
///     <list type="bullet">
///         <item>
///             Dual: the right side's rate, depth and delay time are ratios of the left's (all 1 by default), and Width
///             moves the right side's delay toward 1 ms.
///         </item>
///         <item>
///             LFO 2 wobbles the main LFO's rate, so the sweep is less regular; Auto Depth tames depth at fast rates.
///         </item>
///         <item>Drive saturates what goes into the lines, as an overdriven bucket brigade does.</item>
///         <item>
///             Dimension (Low, Medium, High): one voice per line, the lines swept in antiphase, and each side's wet
///             takes the other side's wet inverted at 30, 50 or 70 %. The static part of the two wets cancels, so the
///             sound is wide and still with very little audible pitch movement.
///         </item>
///     </list>
///     The wet signal has its own low and high cut, Stereo Spread, and can be phase-inverted per side.
///     <para>
///         Type JC-120 is the chorus of the Roland Jazz Chorus, from the drawing the JC-120 amp type is built from
///         (<see cref="JazzChorusPath" />): one bucket-brigade line fed with both sides' sum through the amp's filter
///         and pre-emphasis, read by one voice and put through its output filter and de-emphasis; the left side stays
///         dry and the right side is the chorus alone at Mix 100 %, as the amp's two speakers are (Mix lets the right
///         side's dry signal back in). Its defaults are the amp's sweep: a triangle from 1.46 to 6.4 ms. The controls
///         that shape the other type's two lines and voices do nothing on it and are hidden.
///     </para>
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
    public const int RightRate = 13;
    public const int RightDepth = 14;
    public const int RightTime = 15;
    public const int Width = 16;
    public const int AutoDepth = 17;
    public const int Drive = 18;
    public const int Lfo2Rate = 19;
    public const int Lfo2Depth = 20;
    public const int StereoSpread = 21;
    public const int Dimension = 22;
    public const int GlobalMix = 23;
    public const int Type = 24;

    /// <summary>The <see cref="Type" /> value of the Jazz Chorus's chorus.</summary>
    public const int JazzChorus = 1;

    public const int MaxVoices = 8;

    const float MaxDepthMs = 5f;
    const float MaxDelayMs = 30f;
    const float MaxRightRatio = 2f;
    const float WidthTargetMs = 1f;

    static readonly string[] TypeNames = ["Chorus", "JC-120"];

    /// <summary>The LFO Type value of the triangle.</summary>
    const float Triangle = 1;

    /// <summary>
    ///     The Depth that sweeps the JC-120 type from its shortest delay to its longest, in percent of
    ///     <see cref="MaxDepthMs" />.
    /// </summary>
    static readonly float JazzChorusDepth =
        (float)((JazzChorusPath.LongestDelayMs - JazzChorusPath.ShortestDelayMs) / MaxDepthMs * 100);

    /// <summary>Wet polarity per side for each Phase Reverse option (None, Left, Right, Both).</summary>
    public static readonly ImmutableArray<string> PhaseOptions = ["None", "Left", "Right", "Both"];

    /// <summary>The inverted cross-mix of each Dimension mode (Off, Low, Medium, High).</summary>
    static readonly float[] DimensionCross = [0f, 0.3f, 0.5f, 0.7f];

    readonly FractionalDelayLine left = new();
    readonly FractionalDelayLine right = new();
    Lfo lfo;
    Lfo lfoRight;
    Lfo lfo2;
    WetCuts cuts;
    float mix;
    SampleGlide leftDelay;
    SampleGlide rightDelayGlide;
    OutputStage output;
    JazzChorusPath? jazzChorus; // the JC-120 type's filters, built for the sample rate

    public static BlockDescriptor Info { get; } = new(
        "chorus",
        "Chorus",
        "Modulation",
        [
            OnJazzChorus(ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.8f, "Hz", ParamTaper.Log), 1f),
            OnJazzChorus(ParamDescriptor.Float("depth", "Depth", 0f, 100f, 50f, "%"), JazzChorusDepth),
            OnJazzChorus(
                ParamDescriptor.Float("delay", "Delay", 1f, MaxDelayMs, 8f, "ms"),
                (float)JazzChorusPath.ShortestDelayMs
            ),
            OnJazzChorus(BlockParams.Mix(50f), 100f),
            TwoLinesOnly(ParamDescriptor.Float("spread", "LFO Phase", 0f, 180f, 90f, "°")),
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            OnJazzChorus(ParamDescriptor.Choice("lfoType", "LFO Type", Lfo.ShapeNames), Triangle),
            TwoLinesOnly(ParamDescriptor.Choice("voices", "Voices", ["1", "2", "3", "4", "5", "6", "7", "8"])),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            TwoLinesOnly(ParamDescriptor.Choice("phaseReverse", "Phase Reverse", PhaseOptions)),
            TwoLinesOnly(ParamDescriptor.Float("rightRate", "Right Rate Ratio", 0.25f, 4f, 1f, "x", ParamTaper.Log)),
            TwoLinesOnly(ParamDescriptor.Float("rightDepth", "Right Depth Ratio", 0f, 200f, 100f, "%")),
            TwoLinesOnly(
                ParamDescriptor.Float("rightTime", "Right Time Ratio", 0.5f, MaxRightRatio, 1f, "x", ParamTaper.Log)
            ),
            TwoLinesOnly(ParamDescriptor.Float("width", "Width", 0f, 100f, 0f, "%")),
            LfoConditioner.AutoDepthParam(),
            ParamDescriptor.Float("drive", "Drive", 0f, 100f, 0f, "%"),
            TwoLinesOnly(ParamDescriptor.Float("lfo2Rate", "LFO2 Rate", 0.05f, 10f, 0.2f, "Hz", ParamTaper.Log)),
            TwoLinesOnly(ParamDescriptor.Float("lfo2Depth", "LFO2 Depth", 0f, 100f, 0f, "%")),
            TwoLinesOnly(Stereo.SpreadParam()),
            TwoLinesOnly(ParamDescriptor.Choice("dimension", "Dimension", ["Off", "Low", "Medium", "High"])),
            EffectsMix.Param,
            ParamDescriptor.Choice("type", "Type", TypeNames)
        ],
        maxInstances: 2,
        // The settings a type ships with: a change of type puts them at the new type's defaults.
        typeResets: ["rate", "depth", "delay", "mix", "lfoType"],
        pages: [
            new("Basic", ["type", "voices", "rate", "tempo", "depth", "delay", "width", "spread", "dimension"]),
            new(
                "Advanced",
                [
                    "lfoType", "lfo2Rate", "lfo2Depth", "autoDepth", "rightRate", "rightDepth", "rightTime", "lowCut",
                    "highCut", "drive", "phaseReverse", "stereoSpread", "globalMix"
                ]
            )
        ]
    );

    /// <summary>
    ///     <paramref name="parameter" /> with <paramref name="value" /> as its default on the JC-120 type.
    /// </summary>
    static ParamDescriptor OnJazzChorus(ParamDescriptor parameter, float value) =>
        parameter with { TypeDefaults = [parameter.Default, value] };

    /// <summary>
    ///     <paramref name="parameter" />, which shapes the Chorus type's two lines and voices, hidden on the JC-120
    ///     type: that type has one line and one voice.
    /// </summary>
    static ParamDescriptor TwoLinesOnly(ParamDescriptor parameter) =>
        parameter.HiddenOn(TypeNames.Length, [JazzChorus]);

    /// <summary>Wet polarity of the left and right side for a Phase Reverse setting.</summary>
    public static (float Left, float Right) Polarity(int phaseReverse) =>
        (phaseReverse is 1 or 3 ? -1f : 1f, phaseReverse is 2 or 3 ? -1f : 1f);

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)(MaxRightRatio * (MaxDelayMs + MaxDepthMs) * 0.001 * spec.SampleRate) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
        jazzChorus = new(spec.SampleRate);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        lfo.Reset();
        lfoRight.Reset();
        lfo2.Reset();
        cuts.Reset();
        leftDelay.Reset();
        rightDelayGlide.Reset();
        jazzChorus?.Reset();
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
        var n = io.Length;
        var rate = Spec.SampleRate;
        var lfoRate = TempoDivisions.Rate((int)Params.Current(Tempo), context.Tempo, Params.Advance(Rate, n));
        var rightRate = Params.Advance(RightRate, n);
        var lfo2Rate = Params.Advance(Lfo2Rate, n);
        var lfo2Depth = Params.Advance(Lfo2Depth, n) / 100.0;
        var samplesPerMs = 0.001f * (float)rate;
        var depth = Params.Advance(Depth, n)
            / 100f
            * MaxDepthMs
            * samplesPerMs
            * LfoConditioner.AutoDepth((int)Params.Current(AutoDepth), lfoRate);
        var depthRight = depth * Params.Advance(RightDepth, n) / 100f;
        // The delay times glide across the block (the LFO rides on them), so moving Delay never steps the read.
        var blockDelay = Params.Advance(Delay, n) * samplesPerMs;
        var blockRight = blockDelay * Params.Advance(RightTime, n);
        blockRight += (WidthTargetMs * samplesPerMs - blockRight) * Params.Advance(Width, n) / 100f;
        leftDelay.Begin(blockDelay, n);
        rightDelayGlide.Begin(blockRight, n);
        var spread = Params.Advance(Spread, n) / 360.0;
        var stereoSpread = Params.Advance(StereoSpread, n) / 100f;
        var drive = Params.Advance(Drive, n) / 100f;
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var shape = (LfoShape)(int)Params.Current(LfoType);
        var cross = DimensionCross[(int)Params.Current(Dimension)];
        var voices = cross > 0 ? 1 : (int)Params.Current(Voices) + 1;
        var voiceGain = 1f / MathF.Sqrt(voices);
        var (polarityLeft, polarityRight) = Polarity((int)Params.Current(PhaseReverse));
        cuts.Design(rate, Params.Advance(LowCut, n), Params.Advance(HighCut, n));
        var gains = new MixRamp(MixLaw.ConstantPower, this.mix, mix, n);
        if (jazzChorus is { } path && (int)Params.Current(Type) == JazzChorus) {
            // One line, one voice: the left side stays dry, the right takes the chorus (and, below Mix 100 %, its dry).
            for (var i = 0; i < n; i++) {
                var wet = (float)path.Recover(left.Read(leftDelay.At(i) + depth * 0.5f * (1 + lfo.Value(shape))));
                var unused = 0f;
                cuts.Tick(ref wet, ref unused);
                lfo.Advance(lfoRate, rate);
                gains.Advance();
                var dryRight = io.Right[i];
                left.Write((float)path.Drive(SoftDrive.Apply(0.5f * (io.Left[i] + dryRight), drive)));
                io.Right[i] = dryRight * gains.Dry + wet * gains.Wet;
            }

            path.FlushDenormals();
            Finish(io, mix, n);
            return;
        }

        for (var i = 0; i < n; i++) {
            var baseDelay = leftDelay.At(i);
            var rightDelay = rightDelayGlide.At(i);
            float wetLeft;
            float wetRight;
            if (cross > 0) {
                // Dimension: the lines sweep in antiphase and each side's wet subtracts part of the other's.
                var a = left.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape)));
                var b = right.Read(rightDelay + depthRight * 0.5f * (1 + lfoRight.Value(shape, 0.5)));
                wetLeft = a - cross * b;
                wetRight = b - cross * a;
            } else if (voices == 1) {
                // Read before writing, so a delay of N is exactly N samples.
                wetLeft = left.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape)));
                wetRight = right.Read(rightDelay + depthRight * 0.5f * (1 + lfoRight.Value(shape, spread)));
            } else {
                wetLeft = 0;
                wetRight = 0;
                for (var v = 0; v < voices; v++) {
                    var offset = (double)v / voices;
                    wetLeft += left.Read(baseDelay + depth * 0.5f * (1 + lfo.Value(shape, offset)));
                    wetRight += right.Read(
                        rightDelay + depthRight * 0.5f * (1 + lfoRight.Value(shape, offset + spread))
                    );
                }

                wetLeft *= voiceGain;
                wetRight *= voiceGain;
            }

            // LFO 2 (a sine) wobbles the main rate; at no depth the rate is exactly the knob's.
            var wobble = lfo2Depth > 0 ? Math.Max(0, 1 + lfo2Depth * lfo2.Value(LfoShape.Sine)) : 1;
            lfo.Advance(lfoRate * wobble, rate);
            lfoRight.Advance(lfoRate * rightRate * wobble, rate);
            lfo2.Advance(lfo2Rate, rate);
            gains.Advance();
            cuts.Tick(ref wetLeft, ref wetRight);
            Stereo.Spread(ref wetLeft, ref wetRight, stereoSpread);

            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            if (cross > 0) {
                var mono = SoftDrive.Apply(0.5f * (dryLeft + dryRight), drive);
                left.Write(mono);
                right.Write(mono);
            } else {
                left.Write(SoftDrive.Apply(dryLeft, drive));
                right.Write(SoftDrive.Apply(dryRight, drive));
            }

            io.Left[i] = dryLeft * gains.Dry + polarityLeft * wetLeft * gains.Wet;
            io.Right[i] = dryRight * gains.Dry + polarityRight * wetRight * gains.Wet;
        }

        Finish(io, mix, n);
    }

    /// <summary>
    ///     The end of a block on either type: the wet filters' housekeeping, the mix reached, and the output.
    /// </summary>
    void Finish(in StereoBuffer io, float mix, int n) {
        cuts.FlushDenormals();
        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
