namespace Cathode.Dsp;

/// <summary>
///     A block with meters of its own (the Cab's preamp input level and Motor Drive gain reduction). Every block has
///     the standard <c>peak</c> meter (its output peak, measured by the graph); these come after it in the block's
///     meters, on the same path: the engine reads them into the graph's meters once per callback, the telemetry region
///     keeps the highest value of each over its meter slots, and the server reports them with the block's peak by block
///     id.
/// </summary>
public interface IBlockMeters {
    /// <summary>
    ///     Meter ids, in the order <see cref="ReadMeters" /> writes them. Never <c>peak</c>: that one is every block's,
    ///     and it marks where a slot's meters start in the host's meter list.
    /// </summary>
    IReadOnlyList<string> MeterIds { get; }

    /// <summary>
    ///     Audio thread, after each callback: writes the current values (dB unless the meter's doc says otherwise).
    ///     Must not allocate or block. Readers see the highest value since their previous read.
    /// </summary>
    void ReadMeters(Span<float> values);
}
