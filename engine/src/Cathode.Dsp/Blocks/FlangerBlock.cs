using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Flanger: a very short swept delay with positive or negative feedback, mixed with the dry signal, giving
///     a moving comb filter. The right channel's sweep is offset by the LFO Phase angle.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             The sweep runs from Manual to Manual plus the depth's span, kept within Min and Max Time. VCO Response
///             maps the LFO onto that range linearly (in delay) or exponentially (in pitch, so the notches move evenly
///             in octaves), after Exponent bends it toward the short or long end.
///         </item>
///         <item>
///             Thru-Zero delays the dry signal too, by Dry Delay (Manual) or by the middle of the sweep (Auto), so the
///             wet passes through zero delay relative to it; with Phase Reverse the two cancel there completely.
///         </item>
///         <item>
///             Bass Focus high-passes what enters the line, so the lows stay dry and tight; Drive saturates it. The LFO
///             can be quantised and smoothed (<see cref="LfoConditioner" />), and Auto Depth narrows the sweep at fast
///             rates.
///         </item>
///         <item>The wet signal has first-order low and high cuts, as in the guide, and Stereo Spread.</item>
///     </list>
/// </remarks>
public sealed class FlangerBlock() : DspBlock(Info), IEngageAware {
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
    public const int BassFocus = 13;
    public const int Drive = 14;
    public const int MinTime = 15;
    public const int MaxTime = 16;
    public const int ThruZero = 17;
    public const int DryDelay = 18;
    public const int LfoHicut = 19;
    public const int LfoQuantize = 20;
    public const int AutoDepth = 21;
    public const int VcoResponse = 22;
    public const int Exponent = 23;
    public const int StereoSpread = 24;
    public const int LfoReset = 25;
    public const int GlobalMix = 26;

    public const int ThruZeroOff = 0;
    public const int ThruZeroAuto = 1;
    public const int ThruZeroManual = 2;

    const float MaxSweepMs = 7f;
    const float MaxDelayMs = 10f;
    const float MinDelayMs = 0.1f;

    readonly FractionalDelayLine left = new();
    readonly FractionalDelayLine right = new();
    readonly FractionalDelayLine dryLeft = new();
    readonly FractionalDelayLine dryRight = new();
    Lfo lfo;
    LfoConditioner conditioner;
    FirstOrderCuts cuts;
    FirstOrderCuts focus;
    int thruZeroActive;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "flanger",
        "Flanger",
        "Modulation",
        [
            ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.3f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 70f, "%"),
            ParamDescriptor.Float("delay", "Manual", MinDelayMs, MaxDelayMs, 1f, "ms"),
            ParamDescriptor.Float("feedback", "Feedback", -95f, 95f, 50f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "LFO Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Choice("lfoType", "LFO Type", Lfo.ShapeNames, (int)LfoShape.Triangle),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            ParamDescriptor.Choice("phaseReverse", "Phase Reverse", ChorusBlock.PhaseOptions),
            WetCuts.LowCutParam("bassFocus", "Bass Focus", 1_000f),
            ParamDescriptor.Float("drive", "Drive", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("minTime", "Min Time", MinDelayMs, MaxDelayMs + MaxSweepMs, MinDelayMs, "ms"),
            ParamDescriptor.Float(
                "maxTime",
                "Max Time",
                MinDelayMs,
                MaxDelayMs + MaxSweepMs,
                MaxDelayMs + MaxSweepMs,
                "ms"
            ),
            ParamDescriptor.Choice("thruZero", "Thru-Zero", ["Off", "Auto", "Manual"]),
            ParamDescriptor.Float("dryDelay", "Dry Delay", MinDelayMs, MaxDelayMs, 1f, "ms"),
            LfoConditioner.HicutParam(),
            LfoConditioner.QuantizeParam(),
            LfoConditioner.AutoDepthParam(),
            ParamDescriptor.Choice("vcoResponse", "VCO Response", ["Linear", "Exponential"]),
            ParamDescriptor.Float("exponent", "Exponent", 0.25f, 4f, 1f, string.Empty, ParamTaper.Log),
            Stereo.SpreadParam(),
            ParamDescriptor.Switch("lfoReset", "LFO Bypass Reset"),
            EffectsMix.Param
        ],
        maxInstances: 2
    );

    /// <summary>
    ///     The swept delay, in samples, for an LFO <paramref name="position" /> in [0, 1] across a range starting at
    ///     <paramref name="low" /> and spanning <paramref name="span" />.
    /// </summary>
    public static float SweptDelay(float low, float span, float position, bool exponential, float exponent) {
        if (exponent != 1f) {
            position = MathF.Pow(position, exponent);
        }

        return exponential && low > 0 ? low * MathF.Pow((low + span) / low, position) : low + span * position;
    }

    /// <summary>LFO Bypass Reset: engaging the block restarts the sweep from the top, so every engage sounds alike.</summary>
    public void Engage() {
        if (Params.GetTarget(LfoReset) >= 0.5f) {
            lfo.Reset();
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)((MaxDelayMs + MaxSweepMs) * 0.001 * spec.SampleRate) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
        dryLeft.Allocate(capacity);
        dryRight.Allocate(capacity);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        dryLeft.Reset();
        dryRight.Reset();
        lfo.Reset();
        conditioner.Reset();
        cuts.Reset();
        focus.Reset();
        thruZeroActive = (int)Params.Current(ThruZero);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            left.Dispose();
            right.Dispose();
            dryLeft.Dispose();
            dryRight.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var rate = Spec.SampleRate;
        var samplesPerMs = 0.001f * (float)rate;
        var lfoRate = TempoDivisions.Rate((int)Params.Current(Tempo), context.Tempo, Params.Advance(Rate, n));
        var shape = (LfoShape)(int)Params.Current(LfoType);
        var (polarityLeft, polarityRight) = ChorusBlock.Polarity((int)Params.Current(PhaseReverse));
        cuts.Design(rate, Params.Advance(LowCut, n), Params.Advance(HighCut, n));
        focus.Design(rate, Params.Advance(BassFocus, n), WetCuts.HighOff);
        conditioner.Design(rate, Params.Advance(LfoHicut, n), (int)Params.Current(LfoQuantize));
        var sweep = Params.Advance(Depth, n)
            / 100f
            * MaxSweepMs
            * samplesPerMs
            * LfoConditioner.AutoDepth((int)Params.Current(AutoDepth), lfoRate);
        var low = Params.Advance(Delay, n) * samplesPerMs;

        // Keep the sweep inside Min and Max Time; when neither binds, the range is exactly Manual plus the sweep.
        var minTime = Params.Advance(MinTime, n) * samplesPerMs;
        var maxTime = Math.Max(minTime, Params.Advance(MaxTime, n) * samplesPerMs);
        if (low < minTime || low + sweep > maxTime) {
            var high = Math.Clamp(low + sweep, minTime, maxTime);
            low = Math.Clamp(low, minTime, maxTime);
            sweep = high - low;
        }

        var exponential = (int)Params.Current(VcoResponse) == 1;
        var exponent = Params.Advance(Exponent, n);
        var feedback = Params.Advance(Feedback, n) / 100f;
        var spread = Params.Advance(Spread, n) / 360.0;
        var stereoSpread = Params.Advance(StereoSpread, n) / 100f;
        var drive = Params.Advance(Drive, n) / 100f;
        var thruZero = (int)Params.Current(ThruZero);
        if (thruZero != thruZeroActive) {
            dryLeft.Reset(); // the dry line only runs with Thru-Zero on: never replay what it held before
            dryRight.Reset();
            thruZeroActive = thruZero;
        }

        var manualDry = Params.Advance(DryDelay, n) * samplesPerMs;
        var dryDelay = thruZero == ThruZeroManual
            ? manualDry
            : SweptDelay(low, sweep, 0.5f, exponential, 1f); // Auto: the middle of the sweep
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        var conditioned = !conditioner.IsOff;

        for (var i = 0; i < n; i++) {
            var lfoLeft = lfo.Value(shape);
            var lfoRight = lfo.Value(shape, spread);
            if (conditioned) {
                conditioner.Tick(ref lfoLeft, ref lfoRight);
            }

            float dl, dr;
            if (exponential || exponent != 1f) {
                dl = SweptDelay(low, sweep, 0.5f * (1 + lfoLeft), exponential, exponent);
                dr = SweptDelay(low, sweep, 0.5f * (1 + lfoRight), exponential, exponent);
            } else {
                dl = low + sweep * 0.5f * (1 + lfoLeft);
                dr = low + sweep * 0.5f * (1 + lfoRight);
            }

            lfo.Advance(lfoRate, rate);
            currentMix += mixStep;

            var inLeft = io.Left[i];
            var inRight = io.Right[i];
            var wetLeft = left.Read(dl); // read before writing, so a delay of N is exactly N samples
            var wetRight = right.Read(dr);
            var feedLeft = inLeft;
            var feedRight = inRight;
            focus.Tick(ref feedLeft, ref feedRight);
            left.Write(SoftDrive.Apply(feedLeft + wetLeft * feedback, drive));
            right.Write(SoftDrive.Apply(feedRight + wetRight * feedback, drive));
            cuts.Tick(ref wetLeft, ref wetRight);
            Stereo.Spread(ref wetLeft, ref wetRight, stereoSpread);

            if (thruZero != ThruZeroOff) {
                var delayedLeft = dryLeft.Read(dryDelay);
                var delayedRight = dryRight.Read(dryDelay);
                dryLeft.Write(inLeft);
                dryRight.Write(inRight);
                (inLeft, inRight) = (delayedLeft, delayedRight);
            }

            io.Left[i] = inLeft * (1 - currentMix) + polarityLeft * wetLeft * currentMix;
            io.Right[i] = inRight * (1 - currentMix) + polarityRight * wetRight * currentMix;
        }

        cuts.FlushDenormals();
        focus.FlushDenormals();

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
