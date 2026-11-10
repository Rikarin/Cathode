using Cathode.Ipc;

namespace Cathode.Server.Engine;

/// <summary>What the rig needs from the engine. Commands are delivered in the order they are issued.</summary>
public interface IEngineSync {
    bool IsConnected { get; }

    /// <summary>
    ///     Compiles and publishes a graph. Throws <see cref="IpcException" /> if the host rejects it. Completes at
    ///     once when no engine is connected, and as soon as the link drops (or the host stalls) before the host
    ///     answers; the rig resyncs on connect.
    /// </summary>
    Task LoadGraphAsync(GraphDefinition graph, int generation);

    /// <summary>Fire-and-forget, ordered after any earlier command.</summary>
    void SetParams(int generation, IReadOnlyList<ParamValue> values);

    /// <summary>Fire-and-forget, ordered after any earlier command.</summary>
    void SetBypass(int generation, string block, bool bypassed);

    /// <summary>
    ///     Turns a channel-scoped modifier on or off; off puts its parameter back to <paramref name="baseValue" />.
    ///     Fire-and-forget, ordered after any earlier command.
    /// </summary>
    void SetModifier(int generation, string block, string param, bool enabled, float baseValue);

    void SetTuner(bool enabled);

    /// <summary>
    ///     A tap; <paramref name="sampleTime" /> places it exactly (from a MIDI timestamp), null means now.
    /// </summary>
    void Tap(long? sampleTime = null);

    void Looper(string action);

    /// <summary>Writes the current loop to <paramref name="path" /> as a WAV file.</summary>
    Task ExportLooperAsync(string path);

    /// <summary>
    ///     Whether the tempo follows incoming MIDI clock, and whether the host sends MIDI clock at the rig's tempo to
    ///     <paramref name="clockDestination" /> (a MIDI output's uid, or null for every output).
    /// </summary>
    void SetMidi(bool followClock, bool sendClock = false, string? clockDestination = null);

    void MidiOut(string? destination, IReadOnlyList<byte> bytes);
}
