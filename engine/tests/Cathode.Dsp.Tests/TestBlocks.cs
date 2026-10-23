namespace Cathode.Dsp.Tests;

static class TestBlocks {
    /// <summary>A block of type <typeparamref name="T" /> with the given parameter values, prepared at 48 kHz / 64.</summary>
    public static T Create<T>(params (int Index, float Value)[] settings)
        where T : IDspBlock, new() {
        var block = new T();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(48_000, 64));
        return block;
    }
}
