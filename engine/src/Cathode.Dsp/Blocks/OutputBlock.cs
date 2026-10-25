using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The grid's Output cells (docs/08-blocks-parity.md §2): Output 1, the main output, and Outputs 2-4 (types
///     <c>output2</c>..<c>output4</c>), each played on the device channels the audio settings map it to. Each is a row
///     mixer, then the level and balance. As with the
///     Mixer, the graph compiler hands it each cabled input through <see cref="MixInput" /> with the row it comes from,
///     so every row has its own level and balance; rows below the sixth use the sixth row's. At the defaults (0 dB,
///     centre) the rows sum at unity, as a plain Output did. Row gains glide (full scale in 20 ms), so scene and channel
///     changes never click. The scene level is applied after it, by the rig.
/// </summary>
public sealed class OutputBlock(BlockDescriptor descriptor) : DspBlock(descriptor), ISideInputBlock {
    public const int Rows = 6;
    public const int Level = 2 * Rows;
    public const int Balance = Level + 1;

    const double GlideMs = 20;

    readonly float[] gainLeft = new float[Rows];
    readonly float[] gainRight = new float[Rows];
    float glideStep = 1f;
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

    static BlockDescriptor Describe(string typeId, string name, int maxInstances) => new(
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

    /// <summary>Its cabled inputs by row (<see cref="MixInput" />).</summary>
    public SideInputs SideInputs => SideInputs.Cables;

    /// <summary>Audio thread: a cabled input, through <see cref="MixInput" />.</summary>
    public void SideInput(SideInputKind kind, int row, in StereoBuffer signal, in StereoBuffer destination) =>
        MixInput(row, signal, destination);

    public void MixInput(int row, in StereoBuffer source, in StereoBuffer destination) {
        var slot = Math.Clamp(row, 0, Rows - 1);
        var (targetLeft, targetRight) = Target(slot);
        var left = gainLeft[slot];
        var right = gainRight[slot];
        if (left == targetLeft && right == targetRight) {
            if (left == 1f && right == 1f) {
                for (var i = 0; i < source.Length; i++) {
                    destination.Left[i] += source.Left[i];
                    destination.Right[i] += source.Right[i];
                }
            } else {
                for (var i = 0; i < source.Length; i++) {
                    destination.Left[i] += source.Left[i] * left;
                    destination.Right[i] += source.Right[i] * right;
                }
            }

            return;
        }

        for (var i = 0; i < source.Length; i++) {
            left = Glide(left, targetLeft);
            right = Glide(right, targetRight);
            destination.Left[i] += source.Left[i] * left;
            destination.Right[i] += source.Right[i] * right;
        }

        gainLeft[slot] = left;
        gainRight[slot] = right;
    }

    /// <summary>
    ///     Audio thread, before <see cref="IDspBlock.Process" />, instead of <see cref="MixInput" /> when the Output has a
    ///     single input: scales <paramref name="io" /> (from grid row <paramref name="row" />) in place.
    /// </summary>
    public void ScaleInput(int row, in StereoBuffer io) {
        var slot = Math.Clamp(row, 0, Rows - 1);
        var (targetLeft, targetRight) = Target(slot);
        var left = gainLeft[slot];
        var right = gainRight[slot];
        if (left == targetLeft && right == targetRight) {
            if (left != 1f || right != 1f) {
                for (var i = 0; i < io.Length; i++) {
                    io.Left[i] *= left;
                    io.Right[i] *= right;
                }
            }

            return;
        }

        for (var i = 0; i < io.Length; i++) {
            left = Glide(left, targetLeft);
            right = Glide(right, targetRight);
            io.Left[i] *= left;
            io.Right[i] *= right;
        }

        gainLeft[slot] = left;
        gainRight[slot] = right;
    }

    /// <summary>Row <paramref name="row" />'s left and right gains for its level and balance targets.</summary>
    (float Left, float Right) Target(int row) =>
        OutputStage.Gains(Params.GetTarget(RowLevel(row)), Params.GetTarget(RowBalance(row)));

    float Glide(float gain, float target) =>
        target > gain ? Math.Min(target, gain + glideStep) : Math.Max(target, gain - glideStep);

    protected override void OnPrepare(in ProcessSpec spec) =>
        glideStep = (float)(1.0 / Math.Max(1.0, GlideMs * 0.001 * spec.SampleRate));

    protected override void OnReset() {
        for (var row = 0; row < Rows; row++) {
            (gainLeft[row], gainRight[row]) = Target(row);
        }

        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        for (var p = 0; p < 2 * Rows; p++) {
            Params.Advance(p, io.Length); // the rows glide in MixInput; keep their smoothers in step
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
