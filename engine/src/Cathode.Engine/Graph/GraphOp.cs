namespace Cathode.Engine.Graph;

public enum GraphOpKind {
    /// <summary>Copy the engine input into <c>Buffer</c>.</summary>
    ReadInput,

    /// <summary>Copy buffer <c>Source</c> into <c>Buffer</c>.</summary>
    Copy,

    /// <summary>Add buffer <c>Source</c> into <c>Buffer</c>.</summary>
    Add,

    /// <summary>Delay <c>Buffer</c> in place using delay line <c>Index</c> (latency compensation).</summary>
    Delay,

    /// <summary>Run block slot <c>Index</c> in place on <c>Buffer</c>.</summary>
    Process,

    /// <summary>Add <c>Buffer</c> into engine output <c>Index</c> (0 = Output 1, 1-3 = Outputs 2-4).</summary>
    WriteOutput,

    /// <summary>Silence <c>Buffer</c> (a node whose inputs are all pruned, or a Feedback Return with none).</summary>
    Clear,

    /// <summary>
    ///     Hand side-input block slot <c>Index</c> (<see cref="Cathode.Dsp.ISideInputBlock" />) the signal on grid row
    ///     <c>Row</c>, buffer <c>Source</c>. With <c>Buffer</c> ≥ 0 it is a cabled input, which the block adds into
    ///     <c>Buffer</c>, its own (a Multiplexer, Mixer or Output weighs it, Pitch and Vocoder may keep it aside); with
    ///     <c>Buffer</c> −1 it is a tap, only offered (a sidechain), and <c>Source</c> −1 (<c>Row</c> −1) is the grid
    ///     input.
    /// </summary>
    SideInput,

    /// <summary>
    ///     Let Output block slot <c>Index</c> scale <c>Buffer</c>, its only input, by the level and balance of grid row
    ///     <c>Row</c>, in place (the single-input case of <see cref="SideInput" />, without a second buffer).
    /// </summary>
    ScaleRow
}

/// <summary>One step of a compiled execution plan. A plan is a flat array walked once per callback.</summary>
public readonly record struct GraphOp(GraphOpKind Kind, int Buffer, int Source = -1, int Index = -1, int Row = -1);
