using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Mixes up to six stereo inputs, one per grid row, each with its own gain (0-100 %) and balance, into a stereo or
///     summed-mono output with a master level and balance. Like the Multiplexer, the graph compiler hands it each
///     cabled input through <see cref="RowMixerBlock.MixInput" /> instead of summing them; inputs from rows below the
///     sixth use the sixth row's controls. Row gains glide to new settings at a fixed rate (full scale in 20 ms), so
///     scene changes never click.
/// </summary>
public sealed class MixerBlock() : RowMixerBlock(Info, Rows) {
    public const int Rows = 6;
    public const int Mode = 2 * Rows;
    public const int Level = Mode + 1;
    public const int Balance = Level + 1;

    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "mixer",
        "Mixer",
        "Routing",
        [
            ..Enumerable.Range(1, Rows)
                .Select(static r => ParamDescriptor.Float($"gain{r}", $"Row {r} Gain", 0f, 100f, 100f, "%")),
            ..Enumerable.Range(1, Rows)
                .Select(static r => ParamDescriptor.Float($"balance{r}", $"Row {r} Balance", -100f, 100f, 0f, "%")),
            ParamDescriptor.Choice("mode", "Mode", ["Stereo", "Mono"]),
            ParamDescriptor.Float("level", "Output Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Output Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 4
    );

    public static int Gain(int row) => row;

    public static int RowBalance(int row) => Rows + row;

    /// <summary>Row <paramref name="row" />'s left and right gains for its current gain and balance targets.</summary>
    protected override (float Left, float Right) Target(int row) {
        var gain = Params.GetTarget(Gain(row)) / 100f;
        var (left, right) = OutputStage.Gains(0f, Params.GetTarget(RowBalance(row)));
        return (gain * left, gain * right);
    }

    protected override void OnReset() {
        ResetRows();

        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        AdvanceRows(io.Length);

        if ((int)Params.Current(Mode) == 1) {
            for (var i = 0; i < io.Length; i++) {
                var mono = 0.5f * (io.Left[i] + io.Right[i]);
                io.Left[i] = mono;
                io.Right[i] = mono;
            }
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
