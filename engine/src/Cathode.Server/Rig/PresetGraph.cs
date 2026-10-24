using Cathode.Contracts;
using Cathode.Engine.Graph;
using Cathode.Server.Stores;

namespace Cathode.Server.Rig;

/// <summary>Turns the working copy plus the active scene into the graph the engine compiles.</summary>
public static class PresetGraph {
    /// <summary>
    ///     <paramref name="held" /> holds the blocks Scene Ignore kept off the scene's state; a modifier scoped to one
    ///     channel starts enabled only when its block is on that channel.
    /// </summary>
    public static GraphDefinition Build(
        Preset preset,
        int sceneIndex,
        AssetStore assets,
        IReadOnlyDictionary<string, SceneBlockState>? held = null
    ) {
        var scene = preset.Scenes[sceneIndex];
        SceneBlockState StateOf(string id) =>
            held is not null && held.TryGetValue(id, out var state) ? state : scene.StateOf(id);

        var graph = new GraphDefinition { Rows = preset.Rows, Columns = preset.Columns };
        foreach (var block in preset.Blocks) {
            var node = new NodeDefinition {
                Id = block.Id,
                Type = block.Type,
                Row = block.Row,
                Column = block.Column,
                BypassMode = block.BypassMode
            };
            if (block.Type != BuiltInTypes.Shunt) {
                // Input and Output have parameters and channels (the gate, the row mixer) but no bypass.
                var state = StateOf(block.Id);
                node.Bypassed = state.Bypassed && !BuiltInTypes.IsBuiltIn(block.Type);
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
        graph.Modifiers.AddRange(
            preset.Modifiers.Where(m => preset.Blocks.Any(b => b.Id == m.Block))
                .Select(m => new ModifierDefinition {
                        Block = m.Block,
                        Param = m.Param,
                        Source = m.Source,
                        Min = m.Min,
                        Max = m.Max,
                        Curve = m.Curve,
                        AttackMs = m.AttackMs,
                        ReleaseMs = m.ReleaseMs,
                        Enabled = m.Channel is null || m.Channel == StateOf(m.Block).Channel
                    }
                )
        );
        return graph;
    }
}
