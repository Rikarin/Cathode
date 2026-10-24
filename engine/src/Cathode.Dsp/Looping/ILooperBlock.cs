namespace Cathode.Dsp.Looping;

/// <summary>
///     A grid block that holds a <see cref="Looper" />. While a graph with one is running, the engine's looper
///     transport, telemetry and export use it instead of the rig looper.
/// </summary>
public interface ILooperBlock {
    Looper? Looper { get; }
}
