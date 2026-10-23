namespace Cathode.Engine.Graph;

public enum GraphOpKind
{
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

    /// <summary>Add <c>Buffer</c> into the engine output.</summary>
    WriteOutput,
}

/// <summary>One step of a compiled execution plan. A plan is a flat array walked once per callback.</summary>
public readonly record struct GraphOp(GraphOpKind Kind, int Buffer, int Source = -1, int Index = -1);
