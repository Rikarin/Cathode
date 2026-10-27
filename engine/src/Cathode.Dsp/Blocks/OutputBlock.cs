using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The grid's Output cells (docs/08-blocks-parity.md §2): Output 1, the main output, and Outputs 2-4 (types
///     <c>output2</c>..<c>output4</c>), each played on the device channels the audio settings map it to. Each is a row
///     mixer, then the level and balance. As with the Mixer, the graph compiler hands it each cabled input through
///     <see cref="RowMixerBlock.MixInput" /> with the row it comes from, so every row has its own level and balance;
///     rows below the sixth use the sixth row's. At the defaults (0 dB, centre) the rows sum at unity, as a plain
///     Output did. Row gains glide (full scale in 20 ms), so scene and channel changes never click. The scene level is
///     applied after it, by the rig.
/// </summary>
public sealed class OutputBlock(BlockDescriptor descriptor) : RowMixerBlock(descriptor, Rows) {
    public const int Rows = 6;
    public const int Level = 2 * Rows;
    public const int Balance = Level + 1;

    OutputStage output;

    public OutputBlock() : this(Info) { }

    public static BlockDescriptor Info { get; } = Describe("output", "Output 1", 1);

    /// <summary>Outputs 2-4, by index 1-3 (index 0 is <see cref="Info" />).</summary>
    public static IReadOnlyList<BlockDescriptor> Numbered { get; } = [
        Info,
        Describe("output2", "Output 2", 1),
        Describe("output3", "Output 3", 1),
        Describe("output4", "Output 4", 1)
    ];

    static BlockDescriptor Describe(string typeId, string name, int maxInstances) =>
        new(
            typeId,
            name,
            "I/O",
            [
                ..Enumerable.Range(1, Rows)
                    .Select(static r => ParamDescriptor.Float(
                            $"level{r}",
                            $"Row {r} Level",
                            Decibels.SilenceDb,
                            20f,
                            0f,
                            "dB"
                        )
                    ),
                ..Enumerable.Range(1, Rows)
                    .Select(static r => ParamDescriptor.Float($"balance{r}", $"Row {r} Balance", -100f, 100f, 0f, "%")),
                ParamDescriptor.Float("level", "Main Level", Decibels.SilenceDb, 20f, 0f, "dB"),
                ParamDescriptor.Float("balance", "Main Balance", -100f, 100f, 0f, "%")
            ],
            maxInstances: maxInstances
        );

    public static int RowLevel(int row) => row;

    public static int RowBalance(int row) => Rows + row;

    /// <summary>
    ///     Audio thread, before <see cref="IDspBlock.Process" />, instead of <see cref="RowMixerBlock.MixInput" />
    ///     when the Output has a single input: scales <paramref name="io" /> (from grid row <paramref name="row" />)
    ///     in place.
    /// </summary>
    public void ScaleInput(int row, in StereoBuffer io) => ScaleRow(row, io);

    /// <summary>Row <paramref name="row" />'s left and right gains for its level and balance targets.</summary>
    protected override (float Left, float Right) Target(int row) =>
        OutputStage.Gains(Params.GetTarget(RowLevel(row)), Params.GetTarget(RowBalance(row)));

    protected override void OnReset() {
        ResetRows();

        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        AdvanceRows(io.Length);

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
