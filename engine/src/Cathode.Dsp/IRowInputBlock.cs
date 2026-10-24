namespace Cathode.Dsp;

/// <summary>
///     A block that takes each cabled input separately, by grid row, instead of their sum: the Multiplexer and the
///     Mixer. The graph compiler then hands it every input through <see cref="MixInput" /> rather than adding them.
/// </summary>
public interface IRowInputBlock {
    /// <summary>
    ///     Audio thread, once per sub-block for each cabled input, before <see cref="IDspBlock.Process" />: adds
    ///     <paramref name="source" /> (from grid row <paramref name="row" />, 0-based) into <paramref name="destination" />,
    ///     scaled as the block sees fit.
    /// </summary>
    void MixInput(int row, in StereoBuffer source, in StereoBuffer destination);
}
