using Cathode.Dsp.Routing;

namespace Cathode.Dsp;

/// <summary>
///     The signals besides its summed input that a <see cref="ISideInputBlock" /> takes from elsewhere on the grid. The
///     graph compiler routes each declared kind to the block; the block's own parameters pick among them at run time, so
///     changing the choice never recompiles.
/// </summary>
[Flags]
public enum SideInputs {
    None = 0,

    /// <summary>
    ///     Each cabled input on its own, by the grid row it comes from and latency-aligned, instead of their sum
    ///     (<see cref="SideInputKind.Cable" />): the block adds them into its buffer as it sees fit. The Multiplexer and
    ///     the Mixer weigh the rows, the Output cells too; the Pitch detector and the Vocoder analysis keep a chosen row
    ///     aside and sum the rest.
    /// </summary>
    Cables = 1,

    /// <summary>
    ///     Taps (<see cref="SideInputKind.Tap" />): the grid input, and the output of the last live block on each row to
    ///     the block's left, offered before every sub-block without becoming part of its input and without latency
    ///     alignment. The Compressor and Gate sidechain.
    /// </summary>
    Taps = 2,

    /// <summary>
    ///     The audio interface's input sources (docs/08-blocks-parity.md §2): the compiler hands the block its graph's
    ///     <see cref="InputPort" /> (<see cref="ISideInputBlock.Inputs" />) before <see cref="IDspBlock.Prepare" />,
    ///     and the block reads the source it has chosen during <see cref="IDspBlock.Process" />. When the graph is
    ///     replaced the engine fades that port out.
    /// </summary>
    Hardware = 4
}

/// <summary>How a signal handed to <see cref="ISideInputBlock.SideInput" /> reached the block.</summary>
public enum SideInputKind {
    /// <summary>One of the block's cabled inputs (<see cref="SideInputs.Cables" />), to add into its buffer.</summary>
    Cable,

    /// <summary>A tap (<see cref="SideInputs.Taps" />): offered only; the block keeps it if its choice names it.</summary>
    Tap
}

/// <summary>
///     A block that needs a signal from another grid row, the grid input or a hardware input: the Multiplexer and Mixer
///     (and the Output cells), the Pitch detector and Vocoder analysis source, the Compressor and Gate sidechain, the
///     Input cell's source. It declares what it takes in <see cref="SideInputs" />; the graph compiler routes those
///     signals through one mechanism, <see cref="SideInput" /> ops for grid signals and <see cref="Inputs" /> for the
///     hardware inputs.
/// </summary>
public interface ISideInputBlock {
    /// <summary>What the compiler routes to this block; fixed for the block type.</summary>
    SideInputs SideInputs { get; }

    /// <summary>
    ///     The graph's view of the hardware inputs, set by the compiler before <see cref="IDspBlock.Prepare" /> when
    ///     <see cref="SideInputs" /> has <see cref="Cathode.Dsp.SideInputs.Hardware" />; null without one (tests, or a host
    ///     without inputs). Blocks that do not read hardware inputs keep the default, which holds nothing.
    /// </summary>
    InputPort? Inputs {
        get => null;
        set { }
    }

    /// <summary>
    ///     Audio thread, once per sub-block for each routed signal, before <see cref="IDspBlock.Process" />. No allocation.
    ///     <list type="bullet">
    ///         <item>
    ///             <see cref="SideInputKind.Cable" />: <paramref name="signal" /> is a cabled input from grid row
    ///             <paramref name="row" /> (0-based); add it into <paramref name="destination" />, the block's buffer,
    ///             scaled or kept aside as the block sees fit.
    ///         </item>
    ///         <item>
    ///             <see cref="SideInputKind.Tap" />: <paramref name="signal" /> is the grid input (<paramref name="row" />
    ///             −1) or the signal on grid row <paramref name="row" />; <paramref name="destination" /> is empty.
    ///         </item>
    ///     </list>
    /// </summary>
    void SideInput(SideInputKind kind, int row, in StereoBuffer signal, in StereoBuffer destination);
}
