using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class DelayBlockTests
{
    private static DelayBlock Create(params (int Index, float Value)[] settings)
    {
        var delay = new DelayBlock();
        foreach (var (index, value) in settings)
        {
            delay.Params.SetTarget(index, value);
        }

        delay.Prepare(new ProcessSpec(48_000, 64));
        return delay;
    }

    [Fact]
    public void Integer_delay_times_are_exact()
    {
        using var delay = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, 100f));

        var output = BlockRender.Mono(delay, Signals.Impulse(1_000), 64);

        Assert.Equal(480, Analysis.PeakIndex(output));
        Assert.Equal(1f, output[480], 1e-6f);
        Assert.Equal(0.0, Analysis.Rms(output) * Math.Sqrt(output.Length), 1.0 + 1e-6); // nothing else
    }

    [Fact]
    public void Repeats_decay_by_the_feedback_amount()
    {
        using var delay = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 50f), (DelayBlock.Mix, 100f));

        var output = BlockRender.Mono(delay, Signals.Impulse(3_000), 64);

        Assert.Equal(1f, output[480], 1e-5f);
        Assert.Equal(0.5f, output[960], 1e-5f);
        Assert.Equal(0.25f, output[1_440], 1e-5f);
    }

    [Fact]
    public void Mix_follows_the_inverse_linear_law()
    {
        using var delay = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, 50f));

        var output = BlockRender.Mono(delay, Signals.Impulse(1_000), 64);

        Assert.Equal(0.5f, output[0], 1e-6f);   // dry at -6 dB
        Assert.Equal(0.5f, output[480], 1e-6f); // wet at -6 dB
    }

    [Fact]
    public void Tempo_sync_overrides_the_time()
    {
        using var delay = Create((DelayBlock.Tempo, 3f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, 100f)); // 1/4
        var output = (float[])Signals.Impulse(30_016).Clone();
        var right = new float[output.Length];

        for (var offset = 0; offset < output.Length; offset += 64)
        {
            delay.Process(new StereoBuffer(output.AsSpan(offset, 64), right.AsSpan(offset, 64)), new BlockContext(48_000, 120, offset));
        }

        Assert.Equal(24_000, Analysis.PeakIndex(output)); // a quarter note at 120 BPM is 500 ms
    }

    [Fact]
    public void Changing_the_time_glides_without_clicks()
    {
        using var delay = Create((DelayBlock.Time, 300f), (DelayBlock.Feedback, 40f), (DelayBlock.Mix, 50f));
        var input = Signals.Sine(330, 48_000, 48_000, 0.5f);
        var first = BlockRender.Mono(delay, input[..24_000], 64);
        delay.Params.SetTarget(DelayBlock.Time, 120f);

        var second = BlockRender.Mono(delay, input[24_000..], 64);

        var output = first.Concat(second).ToArray();
        var maxStep = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.2f, $"max step {maxStep}");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Analog_and_tape_repeats_stay_bounded_at_full_feedback(float type)
    {
        using var delay = Create((DelayBlock.Type, type), (DelayBlock.Time, 50f), (DelayBlock.Feedback, 100f), (DelayBlock.Mix, 100f));

        var output = BlockRender.Mono(delay, Signals.Noise(48_000 * 3, seed: 1, amplitude: 0.9f), 64);

        Assert.True(Analysis.Peak(output) < 3f, $"peak {Analysis.Peak(output)}");
    }
}
