using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

public class SampleRingTests {
    [Fact]
    public void Delivers_samples_in_order_and_drops_when_full() {
        var ring = new SampleRing(8);
        ring.Write([1, 2, 3, 4, 5]);
        ring.Write([6, 7, 8, 9, 10]); // only 6..8 fit
        var destination = new float[16];

        var read = ring.Read(destination);

        Assert.Equal(8, read);
        Assert.Equal([1f, 2, 3, 4, 5, 6, 7, 8], destination[..8]);
        Assert.Equal(2, ring.Dropped);
    }

    [Fact]
    public void Engine_feeds_the_input_tap_only_when_enabled() {
        var spec = new ProcessSpec(48_000, 64);
        var engine = new CathodeEngine(spec);
        engine.Publish(
            new GraphCompiler(BlockRegistry.CreateDefault()).Compile(GraphTemplates.Basic(), spec, generation: 1)
        );
        var input = Signals.Noise(256, seed: 1);
        var destination = new float[1_024];

        OfflineRenderer.Render(engine, input, input, 64);
        Assert.Equal(0, engine.InputTap.Read(destination));

        engine.InputTapEnabled = true;
        OfflineRenderer.Render(engine, input, input, 64);

        Assert.Equal(256, engine.InputTap.Read(destination));
        Assert.Equal(input, destination[..256]);
    }
}
