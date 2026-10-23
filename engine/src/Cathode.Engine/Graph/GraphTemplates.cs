namespace Cathode.Engine.Graph;

/// <summary>Ready-made graphs for smoke tests and new presets.</summary>
public static class GraphTemplates
{
    /// <summary>Input -> Filter (high pass 80 Hz) -> Volume/Pan -> Output across the first row.</summary>
    public static GraphDefinition Basic() => new()
    {
        Nodes =
        [
            new() { Id = "in1", Type = NodeTypes.Input, Row = 0, Column = 0 },
            new() { Id = "filter1", Type = "filter", Row = 0, Column = 1, Parameters = { ["type"] = 1, ["freq"] = 80 } },
            new() { Id = "volpan1", Type = "volpan", Row = 0, Column = 2 },
            new() { Id = "out1", Type = NodeTypes.Output, Row = 0, Column = 3 },
        ],
        Cables =
        [
            new() { From = "in1", To = "filter1" },
            new() { From = "filter1", To = "volpan1" },
            new() { From = "volpan1", To = "out1" },
        ],
    };
}
