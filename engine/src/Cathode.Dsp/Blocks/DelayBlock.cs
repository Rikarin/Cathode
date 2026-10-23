using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
/// Delay with three voicings sharing one core: Digital (clean), Analog (dark BBD-style feedback with soft
/// saturation) and Tape (gentler filtering, saturation, wow and flutter). Up to 4 s, tempo sync, feedback-path low
/// and high cut, modulation, and the Axe-Fx mix law (50 % = -6 dB each). Reads use cubic Hermite interpolation, so
/// integer delays are exact and time changes glide instead of clicking.
/// </summary>
public sealed class DelayBlock : DspBlock
{
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

    public const float MaxTimeMs = 4_000f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    /// <summary>Tempo divisions in beats (quarter notes).</summary>
    private static readonly float[] Divisions = [0f, 4f, 2f, 1f, 1.5f, 2f / 3f, 0.5f, 0.75f, 1f / 3f, 0.25f];

    public static BlockDescriptor Info { get; } = new(
        "delay",
        "Delay",
        "Delay",
        [
            ParamDescriptor.Choice("type", "Type", ["Digital", "Analog", "Tape"]),
            new("time", "Time", ParamKind.Float, 1f, MaxTimeMs, 375f, "ms", ParamTaper.Log, ParamSmoothing.Linear, 60f),
            ParamDescriptor.Choice("tempo", "Tempo", ["Off", "1/1", "1/2", "1/4", "1/4.", "1/4T", "1/8", "1/8.", "1/8T", "1/16"]),
            ParamDescriptor.Float("feedback", "Feedback", 0f, 100f, 30f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("modrate", "Mod Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("moddepth", "Mod Depth", 0f, 10f, 0f, "ms"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
        ],
        maxInstances: 4);

    private readonly Line _left = new();
    private readonly Line _right = new();
    private double _lfoPhase;
    private double _wowPhase;
    private double _flutterPhase;
    private float _delaySamples;
    private float _mix;
    private float _outputGain = 1f;

    public DelayBlock()
        : base(Info)
    {
    }

    protected override void OnPrepare(in ProcessSpec spec)
    {
        // Room for the longest time plus modulation and tape wow.
        var capacity = (int)(spec.SampleRate * (MaxTimeMs + 20) / 1000) + 8;
        _left.Allocate(capacity);
        _right.Allocate(capacity);
    }

    protected override void OnReset()
    {
        _left.Reset();
        _right.Reset();
        _lfoPhase = 0;
        _wowPhase = 0;
        _flutterPhase = 0;
        _delaySamples = TargetDelaySamples(Params.Current(Time), 120);
        _mix = Params.Current(Mix) / 100f;
        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context)
    {
        var sampleRate = (float)Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var feedback = Params.Advance(Feedback, io.Length) / 100f;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var targetDelay = TargetDelaySamples(Params.Advance(Time, io.Length), context.Tempo);
        var modDepth = Params.Advance(ModDepth, io.Length) * 0.001f * sampleRate;
        var lfoStep = 2 * Math.PI * Params.Advance(ModRate, io.Length) / sampleRate;

        // Feedback-path filters. Analog and Tape add their own fixed darkening on top of the user's high cut.
        var lowCut = Params.Advance(LowCut, io.Length);
        var highCut = Math.Min(Params.Advance(HighCut, io.Length), type switch { 1 => 2_800f, 2 => 6_500f, _ => HighCutOff });
        var useLowCut = lowCut > LowCutOff;
        var useHighCut = highCut < HighCutOff;
        var lowCutCoefficients = useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, sampleRate, lowCut, 0.7071) : default;
        var highCutCoefficients = useHighCut ? SvfCoefficients.Design(BiquadType.LowPass, sampleRate, highCut, 0.7071) : default;
        var saturate = type != 0;

        // Tape transport: slow wow plus faster flutter, a few tenths of a millisecond deep.
        var wowDepth = type == 2 ? 0.0006f * sampleRate : 0f;
        var flutterDepth = type == 2 ? 0.00012f * sampleRate : 0f;
        var wowStep = 2 * Math.PI * 0.55 / sampleRate;
        var flutterStep = 2 * Math.PI * 6.3 / sampleRate;

        var delayStep = (targetDelay - _delaySamples) / io.Length;
        var mixStep = (mix - _mix) / io.Length;
        var delay = _delaySamples;
        var currentMix = _mix;
        for (var i = 0; i < io.Length; i++)
        {
            delay += delayStep;
            currentMix += mixStep;
            var modulation = modDepth * (float)Math.Sin(_lfoPhase)
                + wowDepth * (float)Math.Sin(_wowPhase)
                + flutterDepth * (float)Math.Sin(_flutterPhase);
            _lfoPhase = Wrap(_lfoPhase + lfoStep);
            _wowPhase = Wrap(_wowPhase + wowStep);
            _flutterPhase = Wrap(_flutterPhase + flutterStep);
            var d = Math.Max(3f, delay + modulation + modDepth + wowDepth + flutterDepth); // offset keeps modulation positive

            io.Left[i] = _left.Tick(io.Left[i], d, feedback, currentMix, useLowCut, lowCutCoefficients, useHighCut, highCutCoefficients, saturate);
            io.Right[i] = _right.Tick(io.Right[i], d, feedback, currentMix, useLowCut, lowCutCoefficients, useHighCut, highCutCoefficients, saturate);
        }

        _delaySamples = targetDelay;
        _mix = mix;
        _left.FlushDenormals();
        _right.FlushDenormals();

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    private float TargetDelaySamples(float timeMs, double tempo)
    {
        var division = Divisions[(int)Params.Current(Tempo)];
        var ms = division > 0 && tempo > 0 ? Math.Min(MaxTimeMs, (float)(60_000.0 / tempo * division)) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    private static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;

    private sealed class Line
    {
        private readonly FractionalDelayLine _delay = new();
        private SvfState _lowCut;
        private SvfState _highCut;

        public void Allocate(int capacity) => _delay.Allocate(capacity);

        public void Reset()
        {
            _delay.Reset();
            _lowCut.Reset();
            _highCut.Reset();
        }

        public float Tick(
            float input,
            float delay,
            float feedback,
            float mix,
            bool useLowCut,
            in SvfCoefficients lowCut,
            bool useHighCut,
            in SvfCoefficients highCut,
            bool saturate)
        {
            var wet = _delay.Read(delay);

            var fed = wet;
            if (useLowCut)
            {
                fed = (float)_lowCut.Tick(lowCut, fed);
            }

            if (useHighCut)
            {
                fed = (float)_highCut.Tick(highCut, fed);
            }

            var write = input + fed * feedback;
            if (saturate)
            {
                write = MathF.Tanh(write * 0.8f) / 0.8f; // BBD/tape headroom: repeats compress instead of building up
            }

            _delay.Write(write);
            return input * (1f - mix) + wet * mix;
        }

        public void FlushDenormals()
        {
            _lowCut.FlushDenormals();
            _highCut.FlushDenormals();
        }
    }
}
