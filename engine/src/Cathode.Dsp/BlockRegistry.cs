using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>Maps block type ids to their descriptor and a factory.</summary>
public sealed class BlockRegistry
{
    private readonly Dictionary<string, (BlockDescriptor Descriptor, Func<IDspBlock> Factory)> _types = new(StringComparer.Ordinal);

    /// <summary>A registry pre-populated with every built-in block.</summary>
    public static BlockRegistry CreateDefault()
    {
        var registry = new BlockRegistry();
        registry.Register(VolumePanBlock.Info, static () => new VolumePanBlock());
        registry.Register(FilterBlock.Info, static () => new FilterBlock());
        registry.Register(CabBlock.Info, static () => new CabBlock());
        registry.Register(GateBlock.Info, static () => new GateBlock());
        registry.Register(DelayBlock.Info, static () => new DelayBlock());
        registry.Register(ReverbBlock.Info, static () => new ReverbBlock());
        registry.Register(DriveBlock.Info, static () => new DriveBlock());
        return registry;
    }

    public IEnumerable<BlockDescriptor> Descriptors => _types.Values.Select(static t => t.Descriptor);

    public void Register(BlockDescriptor descriptor, Func<IDspBlock> factory)
    {
        if (!_types.TryAdd(descriptor.TypeId, (descriptor, factory)))
        {
            throw new ArgumentException($"Block type '{descriptor.TypeId}' is already registered.", nameof(descriptor));
        }
    }

    public bool Contains(string typeId) => _types.ContainsKey(typeId);

    public BlockDescriptor GetDescriptor(string typeId) => Lookup(typeId).Descriptor;

    public IDspBlock Create(string typeId) => Lookup(typeId).Factory();

    private (BlockDescriptor Descriptor, Func<IDspBlock> Factory) Lookup(string typeId) =>
        _types.TryGetValue(typeId, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Unknown block type '{typeId}'.");
}
