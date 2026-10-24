namespace Cathode.Dsp;

/// <summary>
///     A block with meters of its own (the Cab's preamp input level and Motor Drive gain reduction). The host reads them
///     on the control thread with its telemetry and the editor shows them with the block. The audio thread writes plain
///     fields; a reader may see one callback's value or the next one's, never a torn one.
/// </summary>
public interface IBlockMeters {
    /// <summary>Meter ids, in the order <see cref="ReadMeters" /> writes them.</summary>
    IReadOnlyList<string> MeterIds { get; }

    /// <summary>Control thread: writes the current values (dB unless the meter's doc says otherwise).</summary>
    void ReadMeters(Span<float> values);
}
