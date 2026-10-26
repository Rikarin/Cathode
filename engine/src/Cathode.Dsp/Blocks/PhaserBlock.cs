using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Phaser: a chain of first-order TPT allpass stages whose break frequency is swept by an LFO, with feedback around
///     the chain. Mixed 50/50 with the dry signal, every 180° of chain phase cuts a notch.
/// </summary>
/// <remarks>
///     <para>
///         Classic: the sweep spans Depth octaves either side of Center, kept within Min and Max Frequency. The VCR Type
///         is the element that sets the stages' frequency from the LFO: Exponential sweeps evenly in octaves (the
///         default), Linear evenly in hertz, JFET follows a square law (slow at the bottom, fast at the top) and
///         Photocell an exponential law on a light level that brightens and darkens with the cell's own attack and
///         release, which lags and lopsides the sweep like an optical phaser. VCR Bias shifts the LFO's position
///         and Exponent bends it. The LFO can be quantised and smoothed (<see cref="LfoConditioner" />); duty skews it
///         (the astable shape's curvature). Feedback is taken after the last stage or the middle one.
///     </para>
///     <para>
///         Barberpole: two chains of second-order allpasses (Filter Q) sweep the whole range on a sawtooth, half a
///         cycle apart, each faded in and out by a sin² window so their sum is always one chain's worth. The notches
///         seem to rise (or fall) forever, a Shepard-tone illusion. Their coefficients are refreshed on a fixed grid of
///         <see cref="BarberGrid" /> samples counted from the stream start.
///     </para>
///     <para>Tone tilts the wet signal around 1 kHz, up to 6 dB either way.</para>
/// </remarks>
public sealed class PhaserBlock() : DspBlock(Info), IEngageAware {
    public const int Rate = 0;
    public const int Depth = 1;
    public const int Center = 2;
    public const int Stages = 3;
    public const int Feedback = 4;
    public const int Mix = 5;
    public const int Spread = 6;
    public const int Level = 7;
    public const int Balance = 8;
    public const int Tempo = 9;
    public const int LfoType = 10;
    public const int LfoDuty = 11;
    public const int LfoHicut = 12;
    public const int LfoQuantize = 13;
    public const int Tone = 14;
    public const int FeedbackPoint = 15;
    public const int MinFrequency = 16;
    public const int MaxFrequency = 17;
    public const int VcrType = 18;
    public const int VcrBias = 19;
    public const int Exponent = 20;
    public const int PhotoAttack = 21;
    public const int PhotoRelease = 22;
    public const int Type = 23;
    public const int Direction = 24;
    public const int FilterQ = 25;
    public const int LfoReset = 26;
    public const int GlobalMix = 27;
    public const int StereoSpread = 28;

    public const int Classic = 0;
    public const int Barberpole = 1;

    public const int VcrLinear = 0;
    public const int VcrExponential = 1;
    public const int VcrJfet = 2;
    public const int VcrPhotocell = 3;

    const int MaxStages = 12;
    const int BarberChains = 2;
    const int BarberGrid = 8;
    const float MaxSweepOctaves = 2f;
    const float ToneHinge = 1_000f;
    const float ToneMaxOctaves = 1f; // ±6 dB
    static readonly int[] StageCounts = [2, 4, 6, 8, 10, 12];

    readonly float[] stateLeft = new float[MaxStages];
    readonly float[] stateRight = new float[MaxStages];
    readonly SvfState[] barber = new SvfState[2 * BarberChains * MaxStages / 2];
    readonly SvfCoefficients[] barberCoefficients = new SvfCoefficients[2 * BarberChains];
    readonly float[] barberLast = new float[2 * BarberChains];
    Lfo lfo;
    LfoConditioner conditioner;
    float lastLeft;
    float lastRight;
    float lightLeft;
    float lightRight;
    float toneLeft;
    float toneRight;
    long sampleCount;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "phaser",
        "Phaser",
        "Modulation",
        [
            ParamDescriptor.Float("rate", "Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 80f, "%"),
            ParamDescriptor.Float("center", "Manual", 100f, 4_000f, 800f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("stages", "Order", ["2", "4", "6", "8", "10", "12"], 1),
            ParamDescriptor.Float("feedback", "Feedback", -95f, 95f, 0f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "LFO Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Choice("lfoType", "LFO Type", Lfo.ShapeNames) with { TypeLabels = ClassicOnly("LFO Type") },
            ParamDescriptor.Float("lfoDuty", "LFO Duty", 0f, 100f, 50f, "%") with {
                TypeLabels = ClassicOnly("LFO Duty")
            },
            LfoConditioner.HicutParam() with { TypeLabels = ClassicOnly("LFO Hicut") },
            LfoConditioner.QuantizeParam() with { TypeLabels = ClassicOnly("LFO Quantize") },
            ParamDescriptor.Float("tone", "Tone", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("feedbackPoint", "Feedback Point", ["Output", "Middle"]) with {
                TypeLabels = ClassicOnly("Feedback Point")
            },
            ParamDescriptor.Float("minFreq", "Min Frequency", 20f, 20_000f, 20f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("maxFreq", "Max Frequency", 20f, 20_000f, 20_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice(
                "vcrType",
                "VCR Type",
                ["Linear", "Exponential", "JFET", "Photocell"],
                VcrExponential
            ) with {
                TypeLabels = ClassicOnly("VCR Type")
            },
            ParamDescriptor.Float("vcrBias", "VCR Bias", -100f, 100f, 0f, "%") with {
                TypeLabels = ClassicOnly("VCR Bias")
            },
            ParamDescriptor.Float("exponent", "Exponent", 0.25f, 4f, 1f, string.Empty, ParamTaper.Log) with {
                TypeLabels = ClassicOnly("Exponent")
            },
            ParamDescriptor.Float("photoAttack", "Photocell Attack", 1f, 200f, 10f, "ms", ParamTaper.Log) with {
                TypeLabels = ClassicOnly("Photocell Attack")
            },
            ParamDescriptor.Float("photoRelease", "Photocell Release", 5f, 1_000f, 100f, "ms", ParamTaper.Log) with {
                TypeLabels = ClassicOnly("Photocell Release")
            },
            ParamDescriptor.Choice("type", "Type", ["Classic", "Barberpole"]),
            ParamDescriptor.Choice("direction", "Sweep Direction", ["Up", "Down"]) with {
                TypeLabels = [string.Empty, "Sweep Direction"]
            },
            ParamDescriptor.Float("filterQ", "Filter Q", 0.3f, 10f, 0.7071f, string.Empty, ParamTaper.Log) with {
                TypeLabels = [string.Empty, "Filter Q"]
            },
            ParamDescriptor.Switch("lfoReset", "LFO Bypass Reset"),
            EffectsMix.Param,
            Stereo.SpreadParam()
        ],
        maxInstances: 2,
        pages: [
            new("Basic", ["rate", "tempo", "depth", "center", "stages", "feedback", "spread", "tone"]),
            new("Modulation", ["lfoType", "lfoDuty", "lfoHicut", "lfoQuantize", "lfoReset", "direction"]),
            new(
                "Advanced",
                [
                    "feedbackPoint", "minFreq", "maxFreq", "filterQ", "vcrType", "vcrBias", "exponent", "photoAttack",
                    "photoRelease", "stereoSpread", "globalMix"
                ]
            )
        ]
    );

    /// <summary>
    ///     The stage frequency for an LFO <paramref name="position" /> in [0, 1] across [<paramref name="low" />,
    ///     <paramref name="high" />] for a VCR type (the photocell's light level is the position it is given).
    /// </summary>
    public static float VcrFrequency(int vcr, float low, float high, float position) =>
        vcr switch {
            VcrLinear => low + (high - low) * position,
            VcrJfet => low + (high - low) * position * position,
            _ => low * MathF.Pow(high / low, position)
        };

    /// <summary>LFO Bypass Reset: engaging the block restarts the sweep from the top, so every engage sounds alike.</summary>
    public void Engage() {
        if (Params.GetTarget(LfoReset) >= 0.5f) {
            lfo.Reset();
        }
    }

    static string[] ClassicOnly(string name) => [name, string.Empty];

    /// <summary>TPT one-pole gain G = g / (1 + g) with g = tan(pi f / fs).</summary>
    static float Gain(float frequency, double sampleRate) {
        var g = MathF.Tan(MathF.PI * frequency / (float)sampleRate);
        return g / (1 + g);
    }

    /// <summary>
    ///     First-order allpasses: low-pass via TPT, then allpass = 2 * lowpass - input. <paramref name="middle" /> is
    ///     the output of the middle stage.
    /// </summary>
    static float Chain(float[] state, int stages, float x, float gain, out float middle) {
        middle = x;
        var half = stages / 2;
        for (var s = 0; s < stages; s++) {
            var v = (x - state[s]) * gain;
            var lowpass = v + state[s];
            state[s] = lowpass + v;
            x = 2 * lowpass - x;
            if (s == half - 1) {
                middle = x;
            }
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

    /// <summary>Per-sample coefficient of a one-pole with time constant <paramref name="ms" />.</summary>
    static float OnePole(float ms, double rate) => (float)(1 - Math.Exp(-1 / (ms * 0.001 * rate)));

    /// <summary>The LFO's 0..1 position, shifted by the VCR bias and bent by the exponent.</summary>
    static float Position(float lfo, float bias, float exponent) {
        var p = Math.Clamp(0.5f * (1 + lfo) + 0.5f * bias, 0f, 1f);
        return exponent == 1f ? p : MathF.Pow(p, exponent);
    }

    static double Wrap(double p) => p - Math.Floor(p);

    /// <summary>The coefficients of each barberpole chain on each side, for the chains' sawtooth positions.</summary>
    void DesignBarber(double position, double positionRight, float low, float high, float q) {
        for (var k = 0; k < BarberChains; k++) {
            var offset = (double)k / BarberChains;
            barberCoefficients[k] = SvfCoefficients.Design(
                BiquadType.AllPass,
                Spec.SampleRate,
                low * Math.Pow(high / low, Wrap(position + offset)),
                q
            );
            barberCoefficients[BarberChains + k] = SvfCoefficients.Design(
                BiquadType.AllPass,
                Spec.SampleRate,
                low * Math.Pow(high / low, Wrap(positionRight + offset)),
                q
            );
        }
    }

    /// <summary>
    ///     One side of the barberpole: each chain (stages / 2 second-order allpasses with its own feedback) weighted by
    ///     its sin² window, the two windows summing to one.
    /// </summary>
    float Barber(int side, float x, double position, float feedback, int stages) {
        var sections = stages / 2;
        float sum = 0;
        for (var k = 0; k < BarberChains; k++) {
            var chain = side * BarberChains + k;
            var c = barberCoefficients[chain];
            double y = x + feedback * barberLast[chain];
            for (var s = 0; s < sections; s++) {
                y = barber[chain * (MaxStages / 2) + s].Tick(c, y);
            }

            barberLast[chain] = (float)y;
            var window = MathF.Sin(MathF.PI * (float)Wrap(position + (double)k / BarberChains));
            sum += window * window * (float)y;
        }

        return sum;
    }

    protected override void OnReset() {
        Array.Clear(stateLeft);
        Array.Clear(stateRight);
        Array.Clear(barber);
        Array.Clear(barberLast);
        lfo.Reset();
        conditioner.Reset();
        lastLeft = 0;
        lastRight = 0;
        lightLeft = 0.5f;
        lightRight = 0.5f;
        toneLeft = 0;
        toneRight = 0;
        sampleCount = 0;
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var rate = Spec.SampleRate;
        var lfoRate = TempoDivisions.Rate((int)Params.Current(Tempo), context.Tempo, Params.Advance(Rate, n));
        var octaves = Params.Advance(Depth, n) / 100f * MaxSweepOctaves;
        var center = Params.Advance(Center, n);
        var stages = StageCounts[(int)Params.Current(Stages)];
        var feedback = Params.Advance(Feedback, n) / 100f;
        var spread = Params.Advance(Spread, n) / 360.0;
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var nyquistGuard = (float)(rate * 0.45);
        var minFrequency = Math.Min(Params.Advance(MinFrequency, n), nyquistGuard);
        var maxFrequency = Math.Clamp(Params.Advance(MaxFrequency, n), minFrequency, nyquistGuard);
        var tone = Params.Advance(Tone, n) / 100f;
        var duty = Params.Advance(LfoDuty, n) / 100.0;
        conditioner.Design(rate, Params.Advance(LfoHicut, n), (int)Params.Current(LfoQuantize));
        var bias = Params.Advance(VcrBias, n) / 100f;
        var exponent = Params.Advance(Exponent, n);
        var attack = OnePole(Params.Advance(PhotoAttack, n), rate);
        var release = OnePole(Params.Advance(PhotoRelease, n), rate);
        var q = Params.Advance(FilterQ, n);

        // The sweep's ends, kept within Min and Max Frequency.
        var lowEnd = center * MathF.Pow(2f, -octaves);
        var highEnd = center * MathF.Pow(2f, octaves);
        var low = Math.Clamp(lowEnd, minFrequency, maxFrequency);
        var high = Math.Clamp(highEnd, minFrequency, maxFrequency);
        var vcr = (int)Params.Current(VcrType);
        var plain = vcr == VcrExponential
            && bias == 0
            && exponent == 1f
            && low == lowEnd // neither end clamped
            && high == highEnd;
        var shape = (LfoShape)(int)Params.Current(LfoType);
        var conditioned = !conditioner.IsOff;
        var middleFeedback = (int)Params.Current(FeedbackPoint) == 1;
        var barberpole = (int)Params.Current(Type) == Barberpole;
        var downward = (int)Params.Current(Direction) == 1;
        var toneSplit = 1f - MathF.Exp(-2f * MathF.PI * ToneHinge / (float)rate);
        var (toneLow, toneHigh) = (MathF.Pow(2f, -ToneMaxOctaves * tone), MathF.Pow(2f, ToneMaxOctaves * tone));
        var gains = new MixRamp(MixLaw.ConstantPower, this.mix, mix, n);
        var stereoSpread = Params.Advance(StereoSpread, n) / 100f;

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            float wetLeft, wetRight;
            if (barberpole) {
                // The sawtooth position of chain 0 (chain 1 is half a cycle on), rising or falling.
                var position = downward ? 1 - lfo.Phase : lfo.Phase;
                var positionRight = position + (downward ? -spread : spread);
                if ((sampleCount & (BarberGrid - 1)) == 0) {
                    DesignBarber(position, positionRight, low, high, q);
                }

                wetLeft = Barber(0, left, position, feedback, stages);
                wetRight = Barber(1, right, positionRight, feedback, stages);
            } else {
                float fl, fr;
                var lfoLeft = lfo.Value(shape, 0, duty);
                var lfoRight = lfo.Value(shape, spread, duty);
                if (conditioned) {
                    conditioner.Tick(ref lfoLeft, ref lfoRight);
                }

                if (plain) {
                    fl = Math.Min(nyquistGuard, center * MathF.Pow(2f, octaves * lfoLeft));
                    fr = Math.Min(nyquistGuard, center * MathF.Pow(2f, octaves * lfoRight));
                } else {
                    var pl = Position(lfoLeft, bias, exponent);
                    var pr = Position(lfoRight, bias, exponent);
                    if (vcr == VcrPhotocell) {
                        lightLeft += (pl > lightLeft ? attack : release) * (pl - lightLeft);
                        lightRight += (pr > lightRight ? attack : release) * (pr - lightRight);
                        (pl, pr) = (lightLeft, lightRight);
                    }

                    fl = VcrFrequency(vcr, low, high, pl);
                    fr = VcrFrequency(vcr, low, high, pr);
                }

                float middleLeft, middleRight;
                var outLeft = Chain(stateLeft, stages, left + feedback * lastLeft, Gain(fl, rate), out middleLeft);
                var outRight = Chain(stateRight, stages, right + feedback * lastRight, Gain(fr, rate), out middleRight);
                (lastLeft, lastRight) = middleFeedback ? (middleLeft, middleRight) : (outLeft, outRight);
                (wetLeft, wetRight) = (outLeft, outRight);
            }

            lfo.Advance(lfoRate, rate);
            sampleCount++;
            if (tone != 0) {
                toneLeft += toneSplit * (wetLeft - toneLeft);
                toneRight += toneSplit * (wetRight - toneRight);
                wetLeft = toneLow * toneLeft + toneHigh * (wetLeft - toneLeft);
                wetRight = toneLow * toneRight + toneHigh * (wetRight - toneRight);
            }

            Stereo.Spread(ref wetLeft, ref wetRight, stereoSpread);
            gains.Advance();
            io.Left[i] = left * gains.Dry + wetLeft * gains.Wet;
            io.Right[i] = right * gains.Dry + wetRight * gains.Wet;
        }

        Flush(stateLeft);
        Flush(stateRight);
        Flush(barberLast);
        for (var s = 0; s < barber.Length; s++) {
            barber[s].FlushDenormals();
        }

        if (MathF.Abs(toneLeft) < 1e-20f) {
            toneLeft = 0;
        }

        if (MathF.Abs(toneRight) < 1e-20f) {
            toneRight = 0;
        }

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
