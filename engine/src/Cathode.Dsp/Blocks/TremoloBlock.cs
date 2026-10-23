using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Tremolo (amplitude modulation) or auto-panner. The panner places the mono sum with a constant-power law, scaled so
///     the centre position is unity on each side.
/// </summary>
public sealed class TremoloBlock : DspBlock {
    public const int Type = 0;
    public const int Rate = 1;
    public const int Depth = 2;
    public const int Shape = 3;
    public const int Level = 4;

    public static BlockDescriptor Info { get; } = new(
        "tremolo",
        "Tremolo/Panner",
        "Modulation",
        [
            ParamDescriptor.Choice("type", "Type", ["Tremolo", "Panner"]),
            ParamDescriptor.Float("rate", "Rate", 0.1f, 20f, 4f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 50f, "%"),
            ParamDescriptor.Choice("shape", "Shape", ["Sine", "Triangle", "Square"]),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    Lfo _lfo;
    float _outputGain = 1f;

    public TremoloBlock()
        : base(Info) { }

    protected override void OnReset() {
        _lfo.Reset();
        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var panner = Params.Current(Type) >= 0.5f;
        var shape = (LfoShape)(int)Params.Current(Shape);
        var lfoRate = Params.Advance(Rate, io.Length);
        var depth = Params.Advance(Depth, io.Length) / 100f;

        for (var i = 0; i < io.Length; i++) {
            var lfo = _lfo.Value(shape);
            _lfo.Advance(lfoRate, rate);
            if (panner) {
                var angle = (depth * lfo + 1) * MathF.PI / 4; // 0 = hard left, pi/2 = hard right
                var mono = 0.5f * (io.Left[i] + io.Right[i]);
                io.Left[i] = mono * MathF.Sqrt(2) * MathF.Cos(angle);
                io.Right[i] = mono * MathF.Sqrt(2) * MathF.Sin(angle);
            } else {
                var gain = 1 - depth * 0.5f * (1 - lfo);
                io.Left[i] *= gain;
                io.Right[i] *= gain;
            }
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }
}
