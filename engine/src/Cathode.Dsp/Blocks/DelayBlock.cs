using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Delay with voicings sharing one core:
///     <list type="bullet">
///         <item>Digital: clean.</item>
///         <item>Analog: dark BBD-style feedback with soft saturation.</item>
///         <item>Tape: gentler filtering, saturation, wow and flutter.</item>
///         <item>Ping-Pong: the mono input enters the left line and every repeat crosses to the other side.</item>
///         <item>Vintage Digital: 12-bit repeats darkened at 7 kHz, like an early digital rack delay.</item>
///     </list>
///     Up to 4 s, tempo sync, a right time as a ratio of the left, feedback-path low and high cut, drive and bit
///     reduction, modulation, a ducker on the wet signal, input gain (what the lines hear, not the dry) and Hold, which
///     closes the input and loops the repeats at unity. The mix follows the Axe-Fx delay law: the wet reaches full level at
///     50 % while the dry stays at full level, then the dry fades out. Reads use cubic Hermite interpolation, so integer
///     delays are exact and time changes glide instead of clicking.
/// </summary>
public sealed class DelayBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Time = 1;
    public const int Tempo = 2;
    public const int Feedback = 3;
    public const int Mix = 4;
    public const int LowCut = 5;
    public const int HighCut = 6;
    public const int ModRate = 7;
    public const int ModDepth = 8;
    public const int Level = 9;
    public const int Balance = 10;
    public const int InputGain = 11;
    public const int Ratio = 12;
    public const int Drive = 13;
    public const int BitReduction = 14;
    public const int Spread = 15;
    public const int DuckAttenuation = 16;
    public const int DuckThreshold = 17;
    public const int DuckRelease = 18;
    public const int Hold = 19;
    public const int PhaseReverse = 20;
    public const int GlobalMix = 21;

    public const int Digital = 0;
    public const int Analog = 1;
    public const int Tape = 2;
    public const int PingPong = 3;
    public const int VintageDigital = 4;

    public const float MaxTimeMs = 4_000f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    const int VintageBits = 12;

    readonly Line left = new();
    readonly Line right = new();
    double lfoPhase;
    double wowPhase;
    double flutterPhase;
    float delaySamples;
    float mix;
    Ducker ducker;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "delay",
        "Delay",
        "Delay",
        [
            ParamDescriptor.Choice("type", "Type", ["Digital", "Analog", "Tape", "Ping-Pong", "Vintage Digital"]),
            new("time", "Time", ParamKind.Float, 1f, MaxTimeMs, 375f, "ms", ParamTaper.Log, ParamSmoothing.Linear, 60f),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("feedback", "Feedback", 0f, 100f, 30f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("modrate", "Mod Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("moddepth", "Mod Depth", 0f, 10f, 0f, "ms"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("ratio", "L/R Time Ratio", 1f, 100f, 100f, "%"),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 0f),
            ParamDescriptor.Float("bitReduction", "Bit Reduction", 0f, 20f, 0f, "bits"),
            ParamDescriptor.Float("spread", "Spread", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("duckAttenuation", "Ducker Atten", 0f, 40f, 0f, "dB"),
            ParamDescriptor.Float("duckThreshold", "Ducker Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckRelease", "Ducker Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Switch("hold", "Repeat Hold"),
            ParamDescriptor.Choice("phaseReverse", "Phase Reverse", ChorusBlock.PhaseOptions),
            EffectsMix.Param
        ],
        maxInstances: 4
    );

    /// <summary>Dry and wet gains for a mix of 0..1 under the delay law: each is at full level up to 50 %.</summary>
    public static (float Dry, float Wet) MixGains(float mix) => (Math.Min(1f, 2f * (1f - mix)), Math.Min(1f, 2f * mix));

    float TargetDelaySamples(float timeMs, double tempo) {
        var division = TempoDivisions.Milliseconds((int)Params.Current(Tempo), tempo);
        var ms = division > 0 ? Math.Min(MaxTimeMs, (float)division) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;

    protected override void OnPrepare(in ProcessSpec spec) {
        // Room for the longest time plus modulation and tape wow.
        var capacity = (int)(spec.SampleRate * (MaxTimeMs + 20) / 1000) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        lfoPhase = 0;
        wowPhase = 0;
        flutterPhase = 0;
        ducker.Reset();
        delaySamples = TargetDelaySamples(Params.Current(Time), 120);
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
        var sampleRate = (float)Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var hold = Params.Current(Hold) >= 0.5f;
        var feedback = hold ? 1f : Params.Advance(Feedback, n) / 100f;
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var targetDelay = TargetDelaySamples(Params.Advance(Time, n), context.Tempo);
        var ratio = Params.Advance(Ratio, n) / 100f;
        var modDepth = Params.Advance(ModDepth, n) * 0.001f * sampleRate;
        var lfoStep = 2 * Math.PI * Params.Advance(ModRate, n) / sampleRate;
        var inputGain = hold ? 0f : Params.Advance(InputGain, n) / 100f;
        var spread = Params.Advance(Spread, n) / 100f;
        var (polarityLeft, polarityRight) = ChorusBlock.Polarity((int)Params.Current(PhaseReverse));

        // Feedback-path filters. Analog, Tape and Vintage Digital add their own fixed darkening to the user's high cut.
        var lowCut = Params.Advance(LowCut, n);
        var highCut = Math.Min(
            Params.Advance(HighCut, n),
            type switch {
                Analog => 2_800f,
                Tape => 6_500f,
                VintageDigital => 7_000f,
                _ => HighCutOff
            }
        );
        var loop = new LoopSettings(
            lowCut > LowCutOff,
            lowCut > LowCutOff ? SvfCoefficients.Design(BiquadType.HighPass, sampleRate, lowCut, 0.7071) : default,
            highCut < HighCutOff,
            highCut < HighCutOff ? SvfCoefficients.Design(BiquadType.LowPass, sampleRate, highCut, 0.7071) : default,
            type is Analog or Tape && !hold,
            hold ? 0f : Params.Advance(Drive, n),
            Quantum(type == VintageDigital ? VintageBits : 24 - Params.Advance(BitReduction, n))
        );
        if (hold) {
            Params.Advance(Drive, n);
            Params.Advance(BitReduction, n);
        }

        ducker.Configure(
            sampleRate,
            Params.Advance(DuckAttenuation, n),
            Params.Advance(DuckThreshold, n),
            Params.Advance(DuckRelease, n)
        );

        // Tape transport: slow wow plus faster flutter, a few tenths of a millisecond deep.
        var wowDepth = type == Tape ? 0.0006f * sampleRate : 0f;
        var flutterDepth = type == Tape ? 0.00012f * sampleRate : 0f;
        var wowStep = 2 * Math.PI * 0.55 / sampleRate;
        var flutterStep = 2 * Math.PI * 6.3 / sampleRate;

        var delayStep = (targetDelay - delaySamples) / n;
        var mixStep = (mix - this.mix) / n;
        var delay = delaySamples;
        var currentMix = this.mix;
        var pingPong = type == PingPong;
        for (var i = 0; i < n; i++) {
            delay += delayStep;
            currentMix += mixStep;
            var modulation = modDepth * (float)Math.Sin(lfoPhase)
                + wowDepth * (float)Math.Sin(wowPhase)
                + flutterDepth * (float)Math.Sin(flutterPhase);
            lfoPhase = Wrap(lfoPhase + lfoStep);
            wowPhase = Wrap(wowPhase + wowStep);
            flutterPhase = Wrap(flutterPhase + flutterStep);
            var offset = modulation + modDepth + wowDepth + flutterDepth; // keeps modulation positive
            var dl = Math.Max(3f, delay + offset);
            var dr = ratio == 1f ? dl : Math.Max(3f, delay * ratio + offset);

            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var wetLeft = left.Read(dl, loop);
            var wetRight = right.Read(dr, loop);
            var fedLeft = left.Feedback(wetLeft, loop);
            var fedRight = right.Feedback(wetRight, loop);
            if (pingPong) {
                left.Write(0.5f * (dryLeft + dryRight) * inputGain + fedRight * feedback, loop);
                right.Write(fedLeft * feedback, loop);
            } else {
                left.Write(dryLeft * inputGain + fedLeft * feedback, loop);
                right.Write(dryRight * inputGain + fedRight * feedback, loop);
            }

            if (ducker.Active) {
                var duck = ducker.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
                wetLeft *= duck;
                wetRight *= duck;
            }

            Stereo.Spread(ref wetLeft, ref wetRight, spread);

            var (dryGain, wetGain) = MixGains(currentMix);
            io.Left[i] = dryLeft * dryGain + polarityLeft * wetLeft * wetGain;
            io.Right[i] = dryRight * dryGain + polarityRight * wetRight * wetGain;
        }

        delaySamples = targetDelay;
        this.mix = mix;
        ducker.FlushDenormals();
        left.FlushDenormals();
        right.FlushDenormals();

        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }

    /// <summary>The quantisation step for a word length of <paramref name="bits" /> (0 when 24 bits or more).</summary>
    static float Quantum(float bits) => bits >= 23.99f ? 0f : MathF.Pow(2f, 1f - MathF.Max(1f, bits));

    /// <summary>What the lines' feedback paths do this block.</summary>
    readonly record struct LoopSettings(
        bool UseLowCut,
        SvfCoefficients LowCut,
        bool UseHighCut,
        SvfCoefficients HighCut,
        bool Saturate,
        float Drive,
        float Quantum
    );

    sealed class Line : IDisposable {
        readonly FractionalDelayLine delay = new();
        SvfState lowCut;
        SvfState highCut;

        public void Allocate(int capacity) => delay.Allocate(capacity);

        public void Dispose() => delay.Dispose();

        public void Reset() {
            delay.Reset();
            lowCut.Reset();
            highCut.Reset();
        }

        /// <summary>The repeat <paramref name="samples" /> back, bit-reduced if asked.</summary>
        public float Read(float samples, in LoopSettings loop) {
            var wet = delay.Read(samples);
            return loop.Quantum > 0 ? MathF.Round(wet / loop.Quantum) * loop.Quantum : wet;
        }

        /// <summary>The repeat through the feedback path's low and high cut.</summary>
        public float Feedback(float wet, in LoopSettings loop) {
            var fed = wet;
            if (loop.UseLowCut) {
                fed = (float)lowCut.Tick(loop.LowCut, fed);
            }

            if (loop.UseHighCut) {
                fed = (float)highCut.Tick(loop.HighCut, fed);
            }

            return fed;
        }

        public void Write(float value, in LoopSettings loop) {
            if (loop.Drive > 0) {
                var gain = 1f + loop.Drive;
                value = MathF.Tanh(value * gain) / gain;
            }

            if (loop.Saturate) {
                value = MathF.Tanh(value * 0.8f) / 0.8f; // BBD/tape headroom: repeats compress instead of building up
            }

            delay.Write(value);
        }

        public void FlushDenormals() {
            lowCut.FlushDenormals();
            highCut.FlushDenormals();
        }
    }
}
