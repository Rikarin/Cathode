using Cathode.Dsp;
using Cathode.Dsp.Parameters;

namespace Cathode.Engine.Tests;

/// <summary>A pure delay that reports its latency, for testing compensation.</summary>
sealed class LatencyBlock(int samples) : DspBlock(Info) {
    readonly float[] left = new float[samples];
    readonly float[] right = new float[samples];
    int position;

    public static BlockDescriptor Info { get; } =
        new("test-latency", "Latency", "Test", [ParamDescriptor.Float("unused", "Unused", 0, 1, 0)]);

    public override int LatencySamples => samples;

    protected override void OnReset() {
        Array.Clear(left);
        Array.Clear(right);
        position = 0;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        for (var i = 0; i < io.Length; i++) {
            (io.Left[i], left[position]) = (left[position], io.Left[i]);
            (io.Right[i], right[position]) = (right[position], io.Right[i]);
            position = (position + 1) % samples;
        }
    }
}

static class TestRegistry {
    public static BlockRegistry Create() {
        var registry = BlockRegistry.CreateDefault();
        registry.Register(LatencyBlock.Info, static () => new LatencyBlock(10));
        return registry;
    }
}
