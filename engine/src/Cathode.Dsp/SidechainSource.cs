using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;
using System.Collections.Immutable;

namespace Cathode.Dsp;

/// <summary>
///     The sidechain a Compressor or Gate keeps (docs/08-blocks-parity.md §6.2, §6.11): its detector can listen to
///     another signal than its input. The block declares <see cref="SideInputs.Taps" /> and
///     <see cref="SideInputs.Hardware" /> (<see cref="ISideInputBlock" />), so the graph compiler offers it, once per
///     sub-block, the grid input and the output of the last live block on each row to its left (<see cref="Offer" />).
///     This holds the selected source's samples for the coming
///     <see cref="IDspBlock.Process" />, or nothing, in which case the block listens to its own input. A selected row
///     with no block to the left of this one (or nothing at all on it) also falls back to the block's own input. The
///     source can also be one of the interface's inputs (<see cref="HardwareInputs" />), which the block offers itself
///     through <see cref="OfferHardware" />; an unmapped one is silence, as on the hardware.
/// </summary>
public sealed class SidechainSource {
    /// <summary>The first hardware input option (Input 1); the others follow in <see cref="HardwareInputs" /> order.</summary>
    public const int FirstInput = 8;

    /// <summary>
    ///     The Sidechain parameter's options: the block's own input, the grid input, a grid row, or a hardware input.
    /// </summary>
    public static readonly ImmutableArray<string> Options = [
        "Block", "Grid Input", "Row 1", "Row 2", "Row 3", "Row 4", "Row 5", "Row 6", ..HardwareInputs.Names
    ];

    float[] left = [];
    float[] right = [];
    int length;

    /// <summary>Whether a source was kept for this sub-block.</summary>
    public bool Active => length > 0;

    public ReadOnlySpan<float> Left => left.AsSpan(0, length);

    public ReadOnlySpan<float> Right => right.AsSpan(0, length);

    public static ParamDescriptor Param(string id = "scSource") => ParamDescriptor.Choice(id, "Sidechain", Options);

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

    /// <summary>
    ///     Audio thread, at the start of the block's <see cref="IDspBlock.Process" />: keeps hardware input
    ///     <paramref name="selected" /> − <see cref="FirstInput" /> when that is the selection.
    /// </summary>
    public void OfferHardware(InputPort? port, int selected, int frames) {
        if (selected < FirstInput || port is null || frames > left.Length) {
            return;
        }

        port.Read(selected - FirstInput, left.AsSpan(0, frames), right.AsSpan(0, frames));
        length = frames;
    }

    /// <summary>Forgets the kept source; call at the end of every <see cref="IDspBlock.Process" />.</summary>
    public void Clear() => length = 0;
}
