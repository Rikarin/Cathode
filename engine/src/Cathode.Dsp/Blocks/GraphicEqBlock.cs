using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>10-band octave graphic EQ (31 Hz to 16 kHz), constant-Q peaking bands (one octave wide, Q = sqrt 2).</summary>
public sealed class GraphicEqBlock : DspBlock {
    public const int Bands = 10;
    public const int Level = Bands;
    public const float BandQ = 1.4142f;

    public static readonly float[] Centers = [31.25f, 62.5f, 125f, 250f, 500f, 1_000f, 2_000f, 4_000f, 8_000f, 16_000f];
    static readonly string[] Labels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    readonly EqBand[] _bands = [..Enumerable.Range(0, Bands).Select(static _ => new EqBand())];
    float _outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "geq",
        "Graphic EQ",
        "EQ",
        [
            ..Labels.Select(static l => ParamDescriptor.Float($"g{l}", $"{l} Hz", -12f, 12f, 0f, "dB")),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 4
    );

    public GraphicEqBlock()
        : base(Info) { }

    protected override void OnReset() {
        foreach (var band in _bands) {
            band.Reset();
        }

        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        for (var b = 0; b < Bands; b++) {
            var moving = Params.IsRamping(b);
            var gain = Params.Advance(b, io.Length);
            _bands[b].Process(io, BiquadType.Peak, Spec.SampleRate, Centers[b], BandQ, gain, moving);
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }
}
