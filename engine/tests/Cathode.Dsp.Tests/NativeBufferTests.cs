using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class NativeBufferTests {
    [Fact]
    public unsafe void Memory_is_zeroed_aligned_and_writable() {
        using var buffer = new NativeBuffer(1_000);

        Assert.All(buffer.Span.ToArray(), static v => Assert.Equal(0f, v));
        fixed (float* first = buffer.Span) {
            Assert.Equal(0, (long)first % 64);
        }

        buffer[999] = 3.5f;
        Assert.Equal(3.5f, buffer.Span[^1]);
        buffer.Clear();
        Assert.Equal(0f, buffer[999]);
    }

    [Fact]
    public void Allocating_does_not_touch_the_managed_heap_beyond_the_handle() {
        var allocated = AllocationProbe.Measure(static () => new NativeBuffer(1_000_000).Dispose());

        Assert.True(allocated < 200, $"{allocated} managed bytes for a 4 MB buffer");
    }

    [Fact]
    public void Dispose_is_idempotent_and_empty_buffers_are_safe() {
        var buffer = new NativeBuffer(16);
        buffer.Dispose();
        buffer.Dispose();
        NativeBuffer.Empty.Dispose();

        Assert.Equal(0, NativeBuffer.Empty.Length);
        Assert.Equal(0, NativeBuffer.Empty.Span.Length);
    }
}
