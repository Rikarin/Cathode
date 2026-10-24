using Cathode.Dsp.Blocks;

namespace Cathode.Dsp;

/// <summary>
///     A block that follows the next Cab block downstream of it on the grid (the Circuit Amp's Auto speaker load, which
///     takes the impedance curve of the cabinet and speaker that Cab synthesises). The graph compiler links it at every
///     compile: the first Cab block reached through the block's outputs, or null for none.
/// </summary>
public interface ICabFollower {
    /// <summary>Control thread, at compile time: the Cab block to follow (read on the audio thread), or null.</summary>
    void FollowCab(CabBlock? cab);
}
