namespace Cathode.Dsp;

/// <summary>
///     A dynamics block with a sidechain (the Compressor and the Gate/Expander): its detector can listen to another
///     signal (<see cref="ISideInputBlock" />), and its own meters (<see cref="IBlockMeters" />) are gain reductions in
///     positive dB, so they rest at 0 while it is bypassed.
/// </summary>
public interface IDynamicsBlock : ISideInputBlock, IBlockMeters {
    void IBlockMeters.ReadRestingMeters(Span<float> values) => values.Clear();
}
