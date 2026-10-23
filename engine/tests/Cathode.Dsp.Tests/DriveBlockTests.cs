using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class DriveBlockTests(ITestOutputHelper output)
{
    private const int Length = 65_536;
    private const int FundamentalBin = 3_413; // prime, about 2.5 kHz at 48 kHz

    private static DriveBlock Create(float type, float drive, bool antialias = true)
    {
        var block = new DriveBlock { Antialias = antialias };
        block.Params.SetTarget(DriveBlock.Type, type);
        block.Params.SetTarget(DriveBlock.Drive, drive);
        block.Params.SetTarget(DriveBlock.Tone, 10f);
        block.Prepare(new ProcessSpec(48_000, 64));
        return block;
    }

    [Fact]
    public void Adaa_matches_the_clipping_function_for_slow_signals()
    {
        foreach (var shape in Enum.GetValues<ClipperShape>())
        {
            var clipper = default(AdaaClipper);
            var x = Signals.Sine(5, 48_000, 48_000, 3f); // 5 Hz: consecutive samples are almost equal
            for (var i = 0; i < x.Length; i++)
            {
                var y = clipper.Process(shape, x[i]);
                if (i > 0)
                {
                    var expected = AdaaClipper.Function(shape, 0.5 * (x[i] + x[i - 1]));
                    Assert.Equal(expected, y, 1e-3);
                }
            }
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Antialiasing_reduces_aliasing_at_full_drive(float type)
    {
        var input = Signals.CoherentSine(FundamentalBin * 2, Length * 2, 0.5f);

        using var adaa = Create(type, 10f);
        using var naive = Create(type, 10f, antialias: false);
        var withAdaa = Analysis.SignalToAliasingDb(BlockRender.Mono(adaa, input, 64).AsSpan(Length), FundamentalBin);
        var without = Analysis.SignalToAliasingDb(BlockRender.Mono(naive, input, 64).AsSpan(Length), FundamentalBin);

        output.WriteLine($"type {type}: signal-to-aliasing {without:0.0} dB naive, {withAdaa:0.0} dB with ADAA");
        Assert.True(withAdaa - without >= 15, $"ADAA gained only {withAdaa - without:0.0} dB ({without:0.0} -> {withAdaa:0.0})");

        // Measured: 38-41 dB with ADAA alone (Oct 2026). The 60 dB target in docs/03 needs 4x oversampling as well,
        // which is still to come; this floor keeps the current level from regressing.
        Assert.True(withAdaa >= 35, $"signal-to-aliasing {withAdaa:0.0} dB");
    }

    [Fact]
    public void More_drive_means_more_distortion()
    {
        var input = Signals.CoherentSine(cycles: 64, length: 8_192, amplitude: 0.3f);
        double Thd(float drive)
        {
            using var block = Create(1f, drive);
            var rendered = BlockRender.Mono(block, Signals.Concat(input, input), 64);
            return Analysis.Thd(rendered.AsSpan(8_192), 64);
        }

        Assert.True(Thd(2f) < Thd(5f));
        Assert.True(Thd(5f) < Thd(9f));
    }

    [Fact]
    public void Fuzz_adds_even_harmonics_and_no_dc()
    {
        using var fuzz = Create(2f, 6f);
        var input = Signals.CoherentSine(cycles: 32, length: 8_192, amplitude: 0.3f);

        var rendered = BlockRender.Mono(fuzz, Signals.Concat(input, input, input), 64).AsSpan(16_384);
        var magnitudes = Fft.Magnitudes(rendered);

        Assert.True(magnitudes[64] > magnitudes[32] * 0.01, "second harmonic present");
        Assert.True(Math.Abs(rendered.ToArray().Average()) < 1e-3, "no DC offset");
    }
}
