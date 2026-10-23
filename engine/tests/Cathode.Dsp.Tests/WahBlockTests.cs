using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class WahBlockTests {
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 50f)]
    [InlineData(0f, 100f)]
    [InlineData(2f, 25f)]
    public void Resonant_peak_follows_the_pedal_position(float type, float position) {
        using var wah = new WahBlock();
        wah.Params.SetTarget(WahBlock.Type, type);
        wah.Params.SetTarget(WahBlock.Position, position);
        wah.Prepare(new(48_000, 64));

        var response = BlockRender.Mono(wah, Signals.Impulse(16_384), 64);

        var peak = 100.0;
        var best = 0.0;
        for (var f = 100.0; f < 6_000; f *= 1.01) {
            var magnitude = Analysis.MagnitudeAt(response, f, 48_000);
            if (magnitude > best) {
                best = magnitude;
                peak = f;
            }
        }

        var expected = WahBlock.Frequency((int)type, position);
        Assert.InRange(peak, expected * 0.95, expected * 1.05);
        Assert.True(Analysis.ToDb(best) > 4, $"resonance only {Analysis.ToDb(best):0.0} dB");
    }

    [Fact]
    public void Sweeping_the_pedal_does_not_click() {
        using var wah = new WahBlock();
        wah.Prepare(new(48_000, 64));
        var input = Signals.Sine(440, 48_000, 48_000, 0.3f);
        var output = new float[input.Length];
        var right = new float[input.Length];

        for (var offset = 0; offset < input.Length; offset += 64) {
            wah.Params.SetTarget(
                WahBlock.Position,
                50f + 50f * MathF.Sin(offset / 48_000f * 2 * MathF.PI * 2)
            ); // 2 Hz rock
            input.AsSpan(offset, 64).CopyTo(output.AsSpan(offset, 64));
            wah.Process(new(output.AsSpan(offset, 64), right.AsSpan(offset, 64)), default);
        }

        var maxStep = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.15f, $"max step {maxStep}");
    }
}
