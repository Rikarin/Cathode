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
        registry.Register(InputBlock.Info, static () => new InputBlock());
        registry.Register(OutputBlock.Info, static () => new OutputBlock());
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
        registry.Register(CircuitAmpBlock.Info, static () => new CircuitAmpBlock());
        registry.Register(ToneStackBlock.Info, static () => new ToneStackBlock());
        registry.Register(IrPlayerBlock.Info, static () => new IrPlayerBlock());
        registry.Register(ToneMatchBlock.Info, static () => new ToneMatchBlock());
        registry.Register(MultiplexerBlock.Info, static () => new MultiplexerBlock());
        registry.Register(MixerBlock.Info, static () => new MixerBlock());
        registry.Register(FeedbackSendBlock.Info, static () => new FeedbackSendBlock());
        registry.Register(FeedbackReturnBlock.Info, static () => new FeedbackReturnBlock());
        registry.Register(FxLoopBlock.Info, static () => new FxLoopBlock());
        registry.Register(MultiDelayBlock.Info, static () => new MultiDelayBlock());
        registry.Register(MegatapBlock.Info, static () => new MegatapBlock());
        registry.Register(TenTapBlock.Info, static () => new TenTapBlock());
        registry.Register(PlexBlock.Info, static () => new PlexBlock());
        registry.Register(FormantBlock.Info, static () => new FormantBlock());
        registry.Register(RingModulatorBlock.Info, static () => new RingModulatorBlock());
        registry.Register(EnhancerBlock.Info, static () => new EnhancerBlock());
        registry.Register(CrossoverBlock.Info, static () => new CrossoverBlock());
        registry.Register(MultibandCompressorBlock.Info, static () => new MultibandCompressorBlock());
        registry.Register(DynamicDistortionBlock.Info, static () => new DynamicDistortionBlock());
        registry.Register(RotaryBlock.Info, static () => new RotaryBlock());
        registry.Register(VibeBlock.Info, static () => new VibeBlock());
        registry.Register(VocoderBlock.Info, static () => new VocoderBlock());
        registry.Register(ResonatorBlock.Info, static () => new ResonatorBlock());
        registry.Register(PitchBlock.Info, static () => new PitchBlock());
        registry.Register(SynthBlock.Info, static () => new SynthBlock());
        registry.Register(RtaBlock.Info, static () => new RtaBlock());
        registry.Register(LooperBlock.Info, static () => new LooperBlock());
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
