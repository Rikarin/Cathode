using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;
using Cathode.Engine.Rig;

namespace Cathode.Server.Rig;

/// <summary>
///     Block types the editor can place, built from the same descriptors the engine uses, plus the grid's built-in
///     Input, Output and Shunt cells.
/// </summary>
public sealed class BlockCatalog {
    readonly Dictionary<string, BlockDescriptor> descriptors = new(StringComparer.Ordinal);
    readonly Dictionary<string, BlockTypeInfo> infos = new(StringComparer.Ordinal);

    public IReadOnlyList<BlockTypeInfo> All => [..infos.Values];

    /// <summary>The rig parameters (controllers, scene level, tempo, sources), for the editor's controller pages.</summary>
    public BlockTypeInfo Rig { get; } = new(
        RigControls.BlockId,
        RigControls.Descriptor.Name,
        RigControls.Descriptor.Category,
        1,
        1,
        [],
        [..RigControls.Descriptor.Params.Select(ToInfo)]
    );

    public BlockCatalog(BlockRegistry registry) {
        foreach (var builtIn in new[] {
                     new BlockTypeInfo(BuiltInTypes.Input, "Input", "I/O", HardwareInputs.Names.Length, 0, [], []),
                     new BlockTypeInfo(BuiltInTypes.Output, "Output 1", "I/O", 1, 0, [], []),
                     new BlockTypeInfo(BuiltInTypes.Shunt, "Shunt", "Routing", 84, 0, [], [])
                 }) {
            infos[builtIn.TypeId] = builtIn;
        }

        foreach (var descriptor in registry.Descriptors) {
            using var block = registry.Create(descriptor.TypeId);
            var slots = block is IAssetConsumer consumer ? consumer.AssetSlots : [];
            descriptors[descriptor.TypeId] = descriptor;
            infos[descriptor.TypeId] = new(
                descriptor.TypeId,
                descriptor.Name,
                descriptor.Category,
                descriptor.MaxInstances,
                descriptor.Channels,
                [..slots],
                [..descriptor.Params.Select(ToInfo)],
                BlockCosts.Of(descriptor.TypeId) ?? 0,
                descriptor.Pages
            );
        }
    }

    public BlockTypeInfo? Info(string typeId) => infos.GetValueOrDefault(typeId);

    /// <summary>The DSP descriptor, or null for built-in cells and unknown types.</summary>
    public BlockDescriptor? Descriptor(string typeId) => descriptors.GetValueOrDefault(typeId);

    static ParameterInfo ToInfo(ParamDescriptor p) =>
        new(
            p.Id,
            p.Name,
            p.Kind.ToString().ToLowerInvariant(),
            p.Min,
            p.Max,
            p.Default,
            p.Unit,
            p.Taper.ToString().ToLowerInvariant(),
            p.Options,
            p.TypeLabels,
            p.Page
        );
}
