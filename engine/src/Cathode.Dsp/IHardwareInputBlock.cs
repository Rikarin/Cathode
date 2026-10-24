using Cathode.Dsp.Routing;

namespace Cathode.Dsp;

/// <summary>
///     A block that can listen to the audio interface's inputs directly (docs/08-blocks-parity.md §2): the Input cell's
///     Source, the Multiplexer's Input Select, a sidechain, the Pitch detector's and the Vocoder's analysis source. The
///     graph compiler hands it its graph's <see cref="InputPort" /> before <see cref="IDspBlock.Prepare" />.
/// </summary>
public interface IHardwareInputBlock {
    /// <summary>The graph's view of the hardware inputs; null without one (tests, or a host without inputs).</summary>
    InputPort? Inputs { get; set; }
}
