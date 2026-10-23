using Cathode.Engine.Rig;

namespace Cathode.Engine;

public enum EngineCommandKind {
    SetBypass,
    TapTempo,
    Looper
}

/// <summary>
///     A discrete event for the audio thread. Commands carry the <see cref="Generation" /> of the graph they were
///     addressed to; the audio thread drops graph commands aimed at a graph that has since been replaced. Tap tempo and
///     looper commands belong to the rig and apply whatever graph is running. <see cref="Time" /> is a sample time
///     (the engine's clock), for events that must be placed exactly, such as taps.
/// </summary>
public readonly record struct EngineCommand(EngineCommandKind Kind, int Generation, int Slot, float Value, long Time = 0) {
    public static EngineCommand SetBypass(int generation, int slot, bool bypassed) =>
        new(EngineCommandKind.SetBypass, generation, slot, bypassed ? 1f : 0f);

    /// <summary>A tap at engine sample time <paramref name="sampleTime" /> (from a MIDI timestamp, or "now").</summary>
    public static EngineCommand TapTempo(long sampleTime) => new(EngineCommandKind.TapTempo, 0, 0, 0f, sampleTime);

    public static EngineCommand Looper(LooperAction action) => new(EngineCommandKind.Looper, 0, (int)action, 0f);
}
