using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Algorithmic reverb. Room, Hall and Plate are an 8-line feedback delay network (Jot and Chaigne 1991) with a
///     Householder feedback matrix; Dattorro is Dattorro's plate (<see cref="DattorroPlate" />) and Spring a dispersive
///     two-spring tank (<see cref="SpringTank" />). Pre-delay, low cut, mix and level are shared.
/// </summary>
/// <remarks>
///     Pre-delay and four Schroeder allpass diffusers feed the network. Each line's gain is set from the decay time so
///     the T60 is analytic, a one-pole damping filter in each line shortens the high-frequency decay, and gentle
///     modulation on two lines breaks up metallic ringing. Room, Hall and Plate are line-length and diffusion
///     presets of the same network. Left and right take decorrelated sign patterns of the lines.
/// </remarks>
public sealed class ReverbBlock : DspBlock {
    public const int Type = 0;
    public const int Decay = 1;
    public const int Size = 2;
    public const int PreDelay = 3;
    public const int Damping = 4;
    public const int LowCut = 5;
    public const int Mix = 6;
    public const int Level = 7;
    public const int Balance = 8;
    public const int InputGain = 9;
    public const int InputSelect = 10;
    public const int Spread = 11;
    public const int DuckAttenuation = 12;
    public const int DuckThreshold = 13;
    public const int DuckRelease = 14;
    public const int Hold = 15;
    public const int GlobalMix = 16;

    public const int Dattorro = 3;
    public const int Spring = 4;

    public const float LowCutOff = 20f;
    const int Lines = 8;
    const float MaxPreDelayMs = 250f;

    /// <summary>The decay time Hold uses: the tail is held, yet every loop stays below unity gain.</summary>
    const float HoldDecaySeconds = 600f;
    const float MaxSizeScale = 1.5f;
    const float ModulationMs = 0.35f;

    /// <summary>Line lengths in ms at size 50 % (scale 1), per type. Mutually prime-ish to spread the modes.</summary>
    static readonly float[][] LineMs = [
        [9.1f, 11.3f, 13.7f, 15.9f, 18.1f, 21.7f, 24.3f, 27.1f], // Room
        [29.7f, 37.1f, 41.1f, 43.7f, 53.3f, 59.9f, 67.1f, 71.3f], // Hall
        [17.3f, 19.1f, 23.3f, 27.7f, 31.1f, 35.9f, 39.7f, 43.1f] // Plate
    ];

    /// <summary>Input diffuser lengths in ms (after Dattorro's plate) and gains per type.</summary>
    static readonly float[] DiffuserMs = [4.77f, 3.59f, 12.73f, 9.30f];

    static readonly float[] DiffusionGain = [0.6f, 0.65f, 0.75f];

    static readonly float[] LeftSigns = [1, -1, 1, -1, 1, -1, 1, -1];
    static readonly float[] RightSigns = [1, 1, -1, -1, 1, 1, -1, -1];

    readonly DelayBuffer preDelay = new();
    readonly Allpass[] diffusers = [new(), new(), new(), new()];
    readonly DelayBuffer[] lines = [..Enumerable.Range(0, Lines).Select(static _ => new DelayBuffer())];
    readonly float[] lengths = new float[Lines];
    readonly float[] gains = new float[Lines];
    readonly float[] damping = new float[Lines];
    readonly float[] outputs = new float[Lines];
    readonly DattorroPlate plate = new();
    readonly SpringTank spring = new();
    int activeType = -1;
    float diffusion;
    float damp;
    float modDepth;
    double modStep;
    SvfState lowCutLeft, lowCutRight;
    double modPhase;
    float mix;
    Ducker ducker;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "reverb",
        "Reverb",
        "Reverb",
        [
            ParamDescriptor.Choice("type", "Type", ["Room", "Hall", "Plate", "Dattorro Plate", "Spring"], 1),
            ParamDescriptor.Float("decay", "Decay Time", 0.1f, 20f, 2f, "s", ParamTaper.Log),
            new("size", "Size", ParamKind.Float, 0f, 100f, 50f, "%", Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("predelay", "Pre-Delay", 0f, MaxPreDelayMs, 0f, "ms"),
            ParamDescriptor.Float("damping", "High Cut", 1_000f, 20_000f, 6_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 1_000f, 80f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 20f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            Stereo.SpreadParam("spread"),
            ParamDescriptor.Float("duckAttenuation", "Ducker Atten", 0f, 40f, 0f, "dB"),
            ParamDescriptor.Float("duckThreshold", "Ducker Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckRelease", "Ducker Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Switch("hold", "Hold"),
            EffectsMix.Param
        ],
        maxInstances: 2
    );

    public ReverbBlock()
        : base(Info) { }

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        preDelay.Allocate((int)(MaxPreDelayMs * 0.001 * rate) + 4);
        for (var i = 0; i < diffusers.Length; i++) {
            diffusers[i].Allocate((int)(DiffuserMs[i] * 0.001 * rate) + 1);
        }

        var longest = LineMs.Max(static l => l.Max());
        foreach (var line in lines) {
            line.Allocate((int)((longest * MaxSizeScale + ModulationMs * 2) * 0.001 * rate) + 4);
        }

        plate.Allocate(rate, MaxSizeScale);
        spring.Allocate(rate);
    }

    protected override void OnReset() {
        preDelay.Clear();
        ducker.Reset();
        foreach (var diffuser in diffusers) {
            diffuser.Clear();
        }

        foreach (var line in lines) {
            line.Clear();
        }

        Array.Clear(damping);
        plate.Clear();
        spring.Clear();
        lowCutLeft.Reset();
        lowCutRight.Reset();
        modPhase = 0;
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            preDelay.Dispose();
            foreach (var diffuser in diffusers) {
                diffuser.Dispose();
            }

            foreach (var line in lines) {
                line.Dispose();
            }

            plate.Dispose();
            spring.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = (float)Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var scale = 0.5f + Params.Current(Size) / 100f;
        var hold = Params.Current(Hold) >= 0.5f;
        var decay = Params.Advance(Decay, io.Length);
        if (hold) {
            decay = HoldDecaySeconds; // an "infinite" tail: the loops lose only what their damping takes
        }

        var inputGain = hold ? 0f : Params.Advance(InputGain, io.Length) / 100f;
        var select = (int)Params.Current(InputSelect);
        var spread = Params.Advance(Spread, io.Length) / 100f;
        ducker.Configure(
            rate,
            Params.Advance(DuckAttenuation, io.Length),
            Params.Advance(DuckThreshold, io.Length),
            Params.Advance(DuckRelease, io.Length)
        );
        var preDelay = Math.Max(1f, Params.Advance(PreDelay, io.Length) * 0.001f * rate);
        var mix = EffectsMix.Apply(Params.Advance(Mix, io.Length), Params.Current(GlobalMix), context) / 100f;
        var dampingHz = Params.Advance(Damping, io.Length);

        if (type != activeType) {
            // The tank that takes over starts empty rather than replaying whatever it held when last used.
            if (activeType >= 0) {
                ClearTank(type);
            }

            activeType = type;
        }

        switch (type) {
            case Dattorro:
                plate.Configure(decay, scale, dampingHz);
                break;
            case Spring:
                spring.Configure(decay, scale);
                break;
            default:
                ConfigureNetwork(type, decay, scale, dampingHz, rate);
                break;
        }

        var lowCut = Params.Advance(LowCut, io.Length);
        var useLowCut = lowCut > LowCutOff;
        var lowCutCoefficients =
            useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, rate, lowCut, 0.7071) : default;

        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var n = 0; n < io.Length; n++) {
            var dryLeft = io.Left[n];
            var dryRight = io.Right[n];
            var x = this.preDelay.ReadWrite(Stereo.Select(select, dryLeft, dryRight) * inputGain, preDelay);
            var (wetLeft, wetRight) = type switch {
                Dattorro => plate.Tick(x),
                Spring => spring.Tick(x),
                _ => TickNetwork(x)
            };

            if (useLowCut) {
                wetLeft = (float)lowCutLeft.Tick(lowCutCoefficients, wetLeft);
                wetRight = (float)lowCutRight.Tick(lowCutCoefficients, wetRight);
            }

            if (ducker.Active) {
                var duck = ducker.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
                wetLeft *= duck;
                wetRight *= duck;
            }

            Stereo.Spread(ref wetLeft, ref wetRight, spread);

            currentMix += mixStep;
            io.Left[n] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[n] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        this.mix = mix;
        ducker.FlushDenormals();
        for (var i = 0; i < Lines; i++) {
            if (MathF.Abs(damping[i]) < 1e-20f) {
                damping[i] = 0;
            }
        }

        lowCutLeft.FlushDenormals();
        lowCutRight.FlushDenormals();

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    void ClearTank(int type) {
        switch (type) {
            case Dattorro:
                plate.Clear();
                break;
            case Spring:
                spring.Clear();
                break;
            default:
                foreach (var diffuser in diffusers) {
                    diffuser.Clear();
                }

                foreach (var line in lines) {
                    line.Clear();
                }

                Array.Clear(damping);
                break;
        }
    }

    void ConfigureNetwork(int type, float decay, float scale, float dampingHz, float rate) {
        diffusion = DiffusionGain[type];
        for (var i = 0; i < Lines; i++) {
            lengths[i] = LineMs[type][i] * scale * 0.001f * rate;
            gains[i] = MathF.Pow(10f, -3f * lengths[i] / (decay * rate)); // -60 dB after `decay` seconds
        }

        // One-pole damping in each line: the coefficient that puts its -3 dB point near the chosen frequency.
        damp = MathF.Exp(-2f * MathF.PI * dampingHz / rate);
        modDepth = ModulationMs * 0.001f * rate;
        modStep = 2 * Math.PI * 0.7 / rate;
    }

    (float Left, float Right) TickNetwork(float x) {
        foreach (var diffuser in diffusers) {
            x = diffuser.Process(x, diffusion);
        }

        var modulation = modDepth * (float)Math.Sin(modPhase);
        modPhase += modStep;
        if (modPhase >= 2 * Math.PI) {
            modPhase -= 2 * Math.PI;
        }

        // Read every line (two of them modulated), damp, and apply the decay gain.
        float sum = 0;
        for (var i = 0; i < Lines; i++) {
            var length = lengths[i] + (i == 1 ? modulation + modDepth : i == 6 ? modDepth - modulation : 0f);
            var y = lines[i].Read(length);
            damping[i] = y + (damping[i] - y) * damp;
            outputs[i] = damping[i] * gains[i];
            sum += outputs[i];
        }

        // Householder feedback (I - 2/N * 11^T) plus the diffused input, written back into the lines.
        var reflection = sum * (2f / Lines);
        float wetLeft = 0, wetRight = 0;
        for (var i = 0; i < Lines; i++) {
            lines[i].Write(outputs[i] - reflection + x * 0.35f);
            wetLeft += outputs[i] * LeftSigns[i];
            wetRight += outputs[i] * RightSigns[i];
        }

        return (wetLeft * 0.5f, wetRight * 0.5f);
    }

    /// <summary>Circular buffer with a fractional (linear) read behind the most recent write.</summary>
    sealed class DelayBuffer : IDisposable {
        NativeBuffer buffer = NativeBuffer.Empty;
        int write;

        public void Allocate(int length) {
            buffer.Dispose();
            buffer = new(length);
            write = 0;
        }

        public void Clear() {
            buffer.Clear();
            write = 0;
        }

        public void Dispose() => buffer.Dispose();

        public float Read(float delay) {
            var position = write - delay;
            var index = (int)MathF.Floor(position);
            var frac = position - index;
            var a = At(index);
            var b = At(index + 1);
            return a + (b - a) * frac;
        }

        public void Write(float value) {
            buffer[write] = MathF.Abs(value) < 1e-20f ? 0f : value;
            if (++write == buffer.Length) {
                write = 0;
            }
        }

        public float ReadWrite(float value, float delay) {
            Write(value);
            return Read(delay);
        }

        float At(int index) {
            index %= buffer.Length;
            return buffer[index < 0 ? index + buffer.Length : index];
        }
    }

    /// <summary>Schroeder allpass with a fixed length.</summary>
    sealed class Allpass : IDisposable {
        NativeBuffer buffer = NativeBuffer.Empty;
        int position;

        public void Allocate(int length) {
            buffer.Dispose();
            buffer = new(length);
            position = 0;
        }

        public void Dispose() => buffer.Dispose();

        public void Clear() {
            buffer.Clear();
            position = 0;
        }

        public float Process(float x, float gain) {
            var delayed = buffer[position];
            var v = x + gain * delayed;
            buffer[position] = MathF.Abs(v) < 1e-20f ? 0f : v;
            if (++position == buffer.Length) {
                position = 0;
            }

            return delayed - gain * v;
        }
    }
}
