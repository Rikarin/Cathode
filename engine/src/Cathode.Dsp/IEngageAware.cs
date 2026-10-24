namespace Cathode.Dsp;

/// <summary>
///     A block that does something when it is engaged (taken out of bypass): an LFO that restarts from a set phase
///     (Flanger and Phaser LFO Bypass Reset, Tremolo Start Phase). The compiled graph calls <see cref="Engage" /> on
///     the audio thread when a block's bypass target turns from bypassed to active, before the bypass fade starts and
///     before the block's next <see cref="IDspBlock.Process" />. It is not called for a block that starts engaged.
/// </summary>
public interface IEngageAware {
    /// <summary>Audio thread: the block was engaged. No allocation, no locks.</summary>
    void Engage();
}
