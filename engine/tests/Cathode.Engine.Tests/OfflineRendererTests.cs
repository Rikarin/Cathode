using Cathode.Audio;

namespace Cathode.Engine.Tests;

/// <summary>The offline renderer's arguments.</summary>
public sealed class OfflineRendererTests {
    /// <summary>#607: a block of no frames is refused rather than rendering 0 frames forever.</summary>
    [Fact]
    public void A_block_size_of_zero_is_refused() {
        var input = new float[64];
        (float[] Left, float[] Right)?[] sources = [(input, input)];

        Assert.Throws<ArgumentOutOfRangeException>(() => OfflineRenderer.Render(new Silent(), sources, 0));
    }

    sealed class Silent : IAudioProcessor {
        public void Process(in AudioIO io) { }
    }
}
