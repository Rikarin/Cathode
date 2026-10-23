namespace Cathode.Dsp.Native;

public static class NativeBlocks
{
    /// <summary>Registers blocks backed by native libraries. The libraries are loaded lazily, on first use.</summary>
    public static BlockRegistry AddNativeBlocks(this BlockRegistry registry)
    {
        registry.Register(NamAmpBlock.Info, static () => new NamAmpBlock());
        return registry;
    }
}
