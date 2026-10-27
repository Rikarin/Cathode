namespace Cathode.Dsp;

/// <summary>
///     A block whose parameters drive expensive derived state (a synthesised impulse response) that cannot be rebuilt
///     on the audio thread. The host calls <see cref="Refresh" /> on the control thread after it sets parameters, and
///     periodically for changes made by modifiers; the block rebuilds what changed and hands it to the audio thread
///     lock-free.
/// </summary>
public interface IControlRefresh {
    /// <summary>
    ///     Control thread (any, serialised by the block): brings derived state up to date with the targets.
    /// </summary>
    void Refresh();
}
