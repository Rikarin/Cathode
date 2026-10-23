namespace Cathode.Engine;

public enum EngineCommandKind {
    SetBypass
}

/// <summary>
///     A discrete event for the audio thread. Commands carry the <see cref="Generation" /> of the graph they were
///     addressed to; the audio thread drops commands aimed at a graph that has since been replaced.
/// </summary>
public readonly record struct EngineCommand(EngineCommandKind Kind, int Generation, int Slot, float Value) {
    public static EngineCommand SetBypass(int generation, int slot, bool bypassed) =>
        new(EngineCommandKind.SetBypass, generation, slot, bypassed ? 1f : 0f);
}
