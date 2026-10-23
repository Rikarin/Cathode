using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>Maps block type ids to their descriptor and a factory.</summary>
public sealed class BlockRegistry {
    readonly Dictionary<string, (BlockDescriptor Descriptor, Func<IDspBlock> Factory)> types =
        new(StringComparer.Ordinal);

    public IEnumerable<BlockDescriptor> Descriptors => types.Values.Select(static t => t.Descriptor);

    /// <summary>A registry pre-populated with every built-in block.</summary>
    public static BlockRegistry CreateDefault() {
        var registry = new BlockRegistry();
        registry.Register(VolumePanBlock.Info, static () => new VolumePanBlock());
        registry.Register(FilterBlock.Info, static () => new FilterBlock());
        registry.Register(CabBlock.Info, static () => new CabBlock());
        registry.Register(GateBlock.Info, static () => new GateBlock());
        registry.Register(DelayBlock.Info, static () => new DelayBlock());
        registry.Register(ReverbBlock.Info, static () => new ReverbBlock());
        registry.Register(DriveBlock.Info, static () => new DriveBlock());
        registry.Register(CompressorBlock.Info, static () => new CompressorBlock());
        registry.Register(ParametricEqBlock.Info, static () => new ParametricEqBlock());
        registry.Register(GraphicEqBlock.Info, static () => new GraphicEqBlock());
        registry.Register(ChorusBlock.Info, static () => new ChorusBlock());
        registry.Register(FlangerBlock.Info, static () => new FlangerBlock());
        registry.Register(PhaserBlock.Info, static () => new PhaserBlock());
        registry.Register(TremoloBlock.Info, static () => new TremoloBlock());
        registry.Register(WahBlock.Info, static () => new WahBlock());
        registry.Register(CaptureAmpBlock.Info, static () => new CaptureAmpBlock());
        return registry;
    }

    public void Register(BlockDescriptor descriptor, Func<IDspBlock> factory) {
        if (!types.TryAdd(descriptor.TypeId, (descriptor, factory))) {
            throw new ArgumentException($"Block type '{descriptor.TypeId}' is already registered.", nameof(descriptor));
        }
    }

    /// <summary>Registers a block type, replacing any registered under the same id (e.g. a native-backed variant).</summary>
    public void Replace(BlockDescriptor descriptor, Func<IDspBlock> factory) =>
        types[descriptor.TypeId] = (descriptor, factory);

    public bool Contains(string typeId) => types.ContainsKey(typeId);

    public BlockDescriptor GetDescriptor(string typeId) => Lookup(typeId).Descriptor;

    public IDspBlock Create(string typeId) => Lookup(typeId).Factory();

    (BlockDescriptor Descriptor, Func<IDspBlock> Factory) Lookup(string typeId) =>
        types.TryGetValue(typeId, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Unknown block type '{typeId}'.");
}
