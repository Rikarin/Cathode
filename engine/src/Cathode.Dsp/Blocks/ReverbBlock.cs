using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Algorithmic reverb: an 8-line feedback delay network (Jot and Chaigne 1991) with a Householder feedback matrix.
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

    public const float LowCutOff = 20f;
    const int Lines = 8;
    const float MaxPreDelayMs = 250f;
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

    readonly DelayBuffer _preDelay = new();
    readonly Allpass[] _diffusers = [new(), new(), new(), new()];
    readonly DelayBuffer[] _lines = [..Enumerable.Range(0, Lines).Select(static _ => new DelayBuffer())];
    readonly float[] _lengths = new float[Lines];
    readonly float[] _gains = new float[Lines];
    readonly float[] _damping = new float[Lines];
    readonly float[] _outputs = new float[Lines];
    SvfState _lowCutLeft, _lowCutRight;
    double _modPhase;
    float _mix;
    float _outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "reverb",
        "Reverb",
        "Reverb",
        [
            ParamDescriptor.Choice("type", "Type", ["Room", "Hall", "Plate"], 1),
            ParamDescriptor.Float("decay", "Decay Time", 0.1f, 20f, 2f, "s", ParamTaper.Log),
            new("size", "Size", ParamKind.Float, 0f, 100f, 50f, "%", Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("predelay", "Pre-Delay", 0f, MaxPreDelayMs, 0f, "ms"),
            ParamDescriptor.Float("damping", "High Cut", 1_000f, 20_000f, 6_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 1_000f, 80f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 20f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public ReverbBlock()
        : base(Info) { }

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        _preDelay.Allocate((int)(MaxPreDelayMs * 0.001 * rate) + 4);
        for (var i = 0; i < _diffusers.Length; i++) {
            _diffusers[i].Allocate((int)(DiffuserMs[i] * 0.001 * rate) + 1);
        }

        var longest = LineMs.Max(static l => l.Max());
        foreach (var line in _lines) {
            line.Allocate((int)((longest * MaxSizeScale + ModulationMs * 2) * 0.001 * rate) + 4);
        }
    }

    protected override void OnReset() {
        _preDelay.Clear();
        foreach (var diffuser in _diffusers) {
            diffuser.Clear();
        }

        foreach (var line in _lines) {
            line.Clear();
        }

        Array.Clear(_damping);
        _lowCutLeft.Reset();
        _lowCutRight.Reset();
        _modPhase = 0;
        _mix = Params.Current(Mix) / 100f;
        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = (float)Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var scale = 0.5f + Params.Current(Size) / 100f;
        var decay = Params.Advance(Decay, io.Length);
        var preDelay = Math.Max(1f, Params.Advance(PreDelay, io.Length) * 0.001f * rate);
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var diffusion = DiffusionGain[type];

        for (var i = 0; i < Lines; i++) {
            _lengths[i] = LineMs[type][i] * scale * 0.001f * rate;
            _gains[i] = MathF.Pow(10f, -3f * _lengths[i] / (decay * rate)); // -60 dB after `decay` seconds
        }

        // One-pole damping in each line: the coefficient that puts its -3 dB point near the chosen frequency.
        var dampingHz = Params.Advance(Damping, io.Length);
        var damp = MathF.Exp(-2f * MathF.PI * dampingHz / rate);

        var lowCut = Params.Advance(LowCut, io.Length);
        var useLowCut = lowCut > LowCutOff;
        var lowCutCoefficients =
            useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, rate, lowCut, 0.7071) : default;

        var modDepth = ModulationMs * 0.001f * rate;
        var modStep = 2 * Math.PI * 0.7 / rate;
        var mixStep = (mix - _mix) / io.Length;
        var currentMix = _mix;

        for (var n = 0; n < io.Length; n++) {
            var dryLeft = io.Left[n];
            var dryRight = io.Right[n];
            var x = _preDelay.ReadWrite(0.5f * (dryLeft + dryRight), preDelay);
            foreach (var diffuser in _diffusers) {
                x = diffuser.Process(x, diffusion);
            }

            var modulation = modDepth * (float)Math.Sin(_modPhase);
            _modPhase += modStep;
            if (_modPhase >= 2 * Math.PI) {
                _modPhase -= 2 * Math.PI;
            }

            // Read every line (two of them modulated), damp, and apply the decay gain.
            float sum = 0;
            for (var i = 0; i < Lines; i++) {
                var length = _lengths[i] + (i == 1 ? modulation + modDepth : i == 6 ? modDepth - modulation : 0f);
                var y = _lines[i].Read(length);
                _damping[i] = y + (_damping[i] - y) * damp;
                _outputs[i] = _damping[i] * _gains[i];
                sum += _outputs[i];
            }

            // Householder feedback (I - 2/N * 11^T) plus the diffused input, written back into the lines.
            var reflection = sum * (2f / Lines);
            float wetLeft = 0, wetRight = 0;
            for (var i = 0; i < Lines; i++) {
                _lines[i].Write(_outputs[i] - reflection + x * 0.35f);
                wetLeft += _outputs[i] * LeftSigns[i];
                wetRight += _outputs[i] * RightSigns[i];
            }

            wetLeft *= 0.5f;
            wetRight *= 0.5f;
            if (useLowCut) {
                wetLeft = (float)_lowCutLeft.Tick(lowCutCoefficients, wetLeft);
                wetRight = (float)_lowCutRight.Tick(lowCutCoefficients, wetRight);
            }

            currentMix += mixStep;
            io.Left[n] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[n] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        _mix = mix;
        for (var i = 0; i < Lines; i++) {
            if (MathF.Abs(_damping[i]) < 1e-20f) {
                _damping[i] = 0;
            }
        }

        _lowCutLeft.FlushDenormals();
        _lowCutRight.FlushDenormals();

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    /// <summary>Circular buffer with a fractional (linear) read behind the most recent write.</summary>
    sealed class DelayBuffer {
        float[] _buffer = [];
        int _write;

        public void Allocate(int length) => _buffer = new float[length];

        public void Clear() {
            Array.Clear(_buffer);
            _write = 0;
        }

        public float Read(float delay) {
            var position = _write - delay;
            var index = (int)MathF.Floor(position);
            var frac = position - index;
            var a = At(index);
            var b = At(index + 1);
            return a + (b - a) * frac;
        }

        public void Write(float value) {
            _buffer[_write] = MathF.Abs(value) < 1e-20f ? 0f : value;
            if (++_write == _buffer.Length) {
                _write = 0;
            }
        }

        public float ReadWrite(float value, float delay) {
            Write(value);
            return Read(delay);
        }

        float At(int index) {
            index %= _buffer.Length;
            return _buffer[index < 0 ? index + _buffer.Length : index];
        }
    }

    /// <summary>Schroeder allpass with a fixed length.</summary>
    sealed class Allpass {
        float[] _buffer = [];
        int _position;

        public void Allocate(int length) => _buffer = new float[length];

        public void Clear() {
            Array.Clear(_buffer);
            _position = 0;
        }

        public float Process(float x, float gain) {
            var delayed = _buffer[_position];
            var v = x + gain * delayed;
            _buffer[_position] = MathF.Abs(v) < 1e-20f ? 0f : v;
            if (++_position == _buffer.Length) {
                _position = 0;
            }

            return delayed - gain * v;
        }
    }
}
