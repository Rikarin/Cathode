using Cathode.Contracts;
using Cathode.Engine.Graph;
using Cathode.Server.Stores;

namespace Cathode.Server.Rig;

/// <summary>Turns the working copy plus the active scene into the graph the engine compiles.</summary>
public static class PresetGraph {
    public static GraphDefinition Build(Preset preset, int sceneIndex, AssetStore assets) {
        var scene = preset.Scenes[sceneIndex];
        var graph = new GraphDefinition { Rows = preset.Rows, Columns = preset.Columns };
        foreach (var block in preset.Blocks) {
            var node = new NodeDefinition { Id = block.Id, Type = block.Type, Row = block.Row, Column = block.Column };
            if (!BuiltInTypes.IsBuiltIn(block.Type)) {
                var state = scene.StateOf(block.Id);
                node.Bypassed = state.Bypassed;
                node.Parameters = new(block.Channels.GetValueOrDefault(state.Channel) ?? []);
                foreach (var (slot, reference) in block.Assets) {
                    node.Assets[slot] = assets.PathOf(reference)
                        ?? throw new RigException(
                            409,
                            $"Block '{block.Id}' uses asset {reference}, which is not in the asset store."
                        );
                }
            }

            graph.Nodes.Add(node);
        }

        graph.Cables.AddRange(preset.Cables.Select(static c => new CableDefinition { From = c.From, To = c.To }));
        return graph;
    }
}
