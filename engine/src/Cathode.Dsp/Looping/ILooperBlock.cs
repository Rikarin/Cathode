namespace Cathode.Dsp.Looping;

/// <summary>
///     A grid block that holds a <see cref="Looper" />. While a graph with one is running, the engine's looper
///     transport, telemetry and export use it instead of the rig looper.
/// </summary>
public interface ILooperBlock {
    Looper? Looper { get; }

    /// <summary>
    ///     Audio thread, at a preset swap: takes <paramref name="outgoing" />'s loop (and its transport state) and gives
    ///     it this block's own, so a running loop carries on into the next preset. Both loopers have the same capacity,
    ///     so nothing is allocated. Returns false if either has no looper.
    /// </summary>
    bool TakeLoop(ILooperBlock outgoing);
}
