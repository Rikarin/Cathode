using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Graphic EQ on the shared <see cref="GraphicEqualizer" />. Type picks the band layout: ten octave bands (31 Hz to
///     16 kHz, the default and the global output EQ's), ten at 2/3 octave, eight, seven or five bands, or one of the
///     passive-style layouts (Mark, 5-, 4- and 3-band, console). The band parameters keep their ids on every layout and
///     are renamed and hidden per type (<see cref="ParamDescriptor.TypeLabels" />). Bands are peaks one octave wide
///     (Q = sqrt 2) times Master Q, or the layout's own width; Outer Shelves turns the outermost two into shelves. In
///     Variable Q mode a band narrows as it is pushed harder, as on an analogue graphic. The passive types run their
///     component-level networks (<see cref="PassiveGraphicEq" />): the band gains are slider and pot positions there,
///     and Master Q, Q Mode and Outer Shelves do not apply.
/// </summary>
public sealed class GraphicEqBlock : DspBlock {
    public const int Bands = 10;
    public const int Balance = 11;
    public const int MasterQ = 12;
    public const int QMode = 13;
    public const int Type = 14;
    public const int Shelves = 15;
    public const int Level = Bands;
    public const float BandQ = GraphicEqualizer.OctaveQ;

    public static readonly float[] Centers = GraphicEqualizer.Layouts[0].Centers;

    readonly GraphicEqualizer equalizer = new(networks: true);
    readonly float[] gains = new float[Bands];
    readonly bool[] moving = new bool[Bands];
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "geq",
        "Graphic EQ",
        "EQ",
        [
            ..GraphicEqualizer.Layouts[0]
                .Labels.Select(static (l, b) => ParamDescriptor.Float($"g{l}", $"{l} Hz", -12f, 12f, 0f, "dB") with {
                        TypeLabels = GraphicEqualizer.BandLabels(b)
                    }
                ),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("masterQ", "Master Q", 0.25f, 4f, 1f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Choice("qMode", "Q Mode", ["Constant Q", "Variable Q"]),
            ParamDescriptor.Choice("type", "Type", GraphicEqualizer.LayoutNames),
            ParamDescriptor.Switch("shelves", "Outer Shelves")
        ],
        maxInstances: 4
    );

    public GraphicEqBlock()
        : base(Info) { }

    protected override void OnPrepare(in ProcessSpec spec) => equalizer.Prepare(spec.SampleRate);

    protected override void OnReset() {
        equalizer.Reset();
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        for (var b = 0; b < Bands; b++) {
            moving[b] = Params.IsRamping(b);
            gains[b] = Params.Advance(b, io.Length);
        }

        equalizer.Process(
            io,
            Spec.SampleRate,
            (int)Params.Current(Type),
            gains,
            moving,
            Params.Advance(MasterQ, io.Length),
            (int)Params.Current(QMode) == 1,
            Params.Current(Shelves) >= 0.5f
        );
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
