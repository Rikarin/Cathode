using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     10-band octave graphic EQ (31 Hz to 16 kHz) of peaking bands, one octave wide (Q = sqrt 2) times Master Q. In
///     Variable Q mode a band narrows as it is pushed harder: its Q runs from 40 % of the constant one near 0 dB to the full
///     value at ±12 dB, so gentle settings are broad, as on an analogue graphic.
/// </summary>
public sealed class GraphicEqBlock : DspBlock {
    public const int Bands = 10;
    public const int Balance = 11;
    public const int MasterQ = 12;
    public const int QMode = 13;
    public const int Level = Bands;
    public const float BandQ = 1.4142f;

    public static readonly float[] Centers = [31.25f, 62.5f, 125f, 250f, 500f, 1_000f, 2_000f, 4_000f, 8_000f, 16_000f];
    static readonly string[] Labels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    readonly EqBand[] bands = [..Enumerable.Range(0, Bands).Select(static _ => new EqBand())];
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "geq",
        "Graphic EQ",
        "EQ",
        [
            ..Labels.Select(static l => ParamDescriptor.Float($"g{l}", $"{l} Hz", -12f, 12f, 0f, "dB")),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("masterQ", "Master Q", 0.25f, 4f, 1f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Choice("qMode", "Q Mode", ["Constant Q", "Variable Q"])
        ],
        maxInstances: 4
    );

    public GraphicEqBlock()
        : base(Info) { }

    protected override void OnReset() {
        foreach (var band in bands) {
            band.Reset();
        }

        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var q = BandQ * Params.Advance(MasterQ, io.Length);
        var variable = (int)Params.Current(QMode) == 1;
        for (var b = 0; b < Bands; b++) {
            var moving = Params.IsRamping(b);
            var gain = Params.Advance(b, io.Length);
            var bandQ = variable ? q * (0.4f + 0.6f * Math.Min(1f, MathF.Abs(gain) / 12f)) : q;
            bands[b].Process(io, BiquadType.Peak, Spec.SampleRate, Centers[b], bandQ, gain, moving);
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
