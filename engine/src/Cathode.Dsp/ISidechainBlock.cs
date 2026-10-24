using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>
///     A block whose detector can listen to another signal than its input (docs/08-blocks-parity.md §6.2, §6.11): the
///     grid input, or the signal on another grid row. The graph compiler offers it, once per sub-block before
///     <see cref="IDspBlock.Process" />, the grid input and the output of the last live block on each row to its left;
///     the block keeps the one its Sidechain parameter selects (<see cref="SidechainSource" />).
/// </summary>
public interface ISidechainBlock {
    /// <summary>
    ///     Audio thread: <paramref name="source" /> is the grid input (<paramref name="row" /> −1) or the signal on grid
    ///     row <paramref name="row" /> (0-based). No allocation.
    /// </summary>
    void SidechainInput(int row, in StereoBuffer source);
}

/// <summary>
///     The sidechain a <see cref="ISidechainBlock" /> keeps: the selected source's samples for the coming
///     <see cref="IDspBlock.Process" />, or nothing, in which case the block listens to its own input. A selected row
///     with no block to the left of this one (or nothing at all on it) also falls back to the block's own input.
/// </summary>
public sealed class SidechainSource {
    /// <summary>The Sidechain parameter's options: the block's own input, the grid input, or a grid row.</summary>
    public static readonly string[] Options = ["Block", "Input", "Row 1", "Row 2", "Row 3", "Row 4", "Row 5", "Row 6"];

    float[] left = [];
    float[] right = [];
    int length;

    public static ParamDescriptor Param(string id = "scSource") => ParamDescriptor.Choice(id, "Sidechain", Options);

    /// <summary>Whether a source was kept for this sub-block.</summary>
    public bool Active => length > 0;

    public ReadOnlySpan<float> Left => left.AsSpan(0, length);

    public ReadOnlySpan<float> Right => right.AsSpan(0, length);

    /// <summary>Control thread: room for one sub-block.</summary>
    public void Prepare(int maxBlockSize) {
        left = new float[maxBlockSize];
        right = new float[maxBlockSize];
        length = 0;
    }

    /// <summary>Keeps <paramref name="source" /> if it is the one <paramref name="selected" /> (the parameter's value) names.</summary>
    public void Offer(int row, in StereoBuffer source, int selected) {
        if (selected == 0 || selected != row + 2 || source.Length > left.Length) {
            return;
        }

        source.Left.CopyTo(left);
        source.Right.CopyTo(right);
        length = source.Length;
    }

    /// <summary>Forgets the kept source; call at the end of every <see cref="IDspBlock.Process" />.</summary>
    public void Clear() => length = 0;
}
