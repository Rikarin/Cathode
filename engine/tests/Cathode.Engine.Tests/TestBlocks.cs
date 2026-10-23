using Cathode.Dsp;
using Cathode.Dsp.Parameters;

namespace Cathode.Engine.Tests;

/// <summary>A pure delay that reports its latency, for testing compensation.</summary>
internal sealed class LatencyBlock(int samples) : DspBlock(Info)
{
    public static BlockDescriptor Info { get; } = new("test-latency", "Latency", "Test", [ParamDescriptor.Float("unused", "Unused", 0, 1, 0)]);

    private readonly float[] _left = new float[samples];
    private readonly float[] _right = new float[samples];
    private int _position;

    public override int LatencySamples => samples;

    protected override void OnReset()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        _position = 0;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context)
    {
        for (var i = 0; i < io.Length; i++)
        {
            (io.Left[i], _left[_position]) = (_left[_position], io.Left[i]);
            (io.Right[i], _right[_position]) = (_right[_position], io.Right[i]);
            _position = (_position + 1) % samples;
        }
    }
}

internal static class TestRegistry
{
    public static BlockRegistry Create()
    {
        var registry = BlockRegistry.CreateDefault();
        registry.Register(LatencyBlock.Info, static () => new LatencyBlock(10));
        return registry;
    }
}
