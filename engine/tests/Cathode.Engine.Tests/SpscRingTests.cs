namespace Cathode.Engine.Tests;

public class SpscRingTests {
    [Fact]
    public void Is_fifo_and_reports_full_and_empty() {
        var ring = new SpscRing<int>(4);

        Assert.False(ring.TryDequeue(out _));
        for (var i = 0; i < 4; i++) {
            Assert.True(ring.TryEnqueue(i));
        }

        Assert.False(ring.TryEnqueue(99));
        for (var i = 0; i < 4; i++) {
            Assert.True(ring.TryDequeue(out var value));
            Assert.Equal(i, value);
        }

        Assert.False(ring.TryDequeue(out _));
    }

    [Fact]
    public void Rejects_non_power_of_two_capacity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpscRing<int>(6));

    [Fact]
    public async Task Preserves_order_across_threads() {
        const int count = 1_000_000;
        var ring = new SpscRing<long>(256);
        var producer = Task.Run(
            () => {
                for (long i = 0; i < count; i++) {
                    while (!ring.TryEnqueue(i)) {
                        Thread.SpinWait(1);
                    }
                }
            },
            TestContext.Current.CancellationToken
        );

        long expected = 0;
        while (expected < count) {
            if (ring.TryDequeue(out var value)) {
                Assert.Equal(expected++, value);
            }
        }

        await producer;
    }
}
