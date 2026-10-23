using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class DriveBlockTests(ITestOutputHelper output) {
    const int Length = 65_536;
    const int FundamentalBin = 3_413; // prime, about 2.5 kHz at 48 kHz

    [Fact]
    public void Adaa_matches_the_clipping_function_for_slow_signals() {
        foreach (var shape in Enum.GetValues<ClipperShape>()) {
            var clipper = default(AdaaClipper);
            var x = Signals.Sine(5, 48_000, 48_000, 3f); // 5 Hz: consecutive samples are almost equal
            for (var i = 0; i < x.Length; i++) {
                var y = clipper.Process(shape, x[i]);
                if (i > 0) {
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
    public void Oversampling_and_adaa_keep_aliasing_60_db_down_at_full_drive(float type) {
        var input = Signals.CoherentSine(FundamentalBin * 2, Length * 2, 0.5f);

        double Measure(bool antialias, bool oversample) {
            using var block = Create(type, 10f, antialias, oversample);
            return Analysis.SignalToAliasingDb(BlockRender.Mono(block, input, 64).AsSpan(Length), FundamentalBin);
        }

        var naive = Measure(false, false);
        var adaa = Measure(true, false);
        var both = Measure(true, true);

        output.WriteLine(
            $"type {type}: signal-to-aliasing {naive:0.0} dB naive, {adaa:0.0} dB ADAA, {both:0.0} dB ADAA + 4x"
        );
        Assert.True(adaa - naive >= 15, $"ADAA gained only {adaa - naive:0.0} dB");
        Assert.True(
            both >= 60,
            $"signal-to-aliasing {both:0.0} dB with ADAA + 4x oversampling (target 60 dB, docs/03 §3)"
        );
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Partial_mix_does_not_comb(float type) {
        // Drive at its minimum is close to linear, so 50 % mix should be close to the processed signal
        // in level everywhere; a phase mismatch between paths would cut deep notches.
        using var block = Create(type, 0f);
        block.Params.SetTarget(DriveBlock.Mix, 50f);
        block.Reset();
        var impulse = BlockRender.Mono(block, Signals.Impulse(8_192, 0.01f), 64);

        foreach (var f in new[] { 200.0, 1_000, 3_000, 6_000 }) {
            Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(impulse, f, 48_000) / 0.01) > -20, $"notch at {f} Hz");
        }
    }

    [Fact]
    public void More_drive_means_more_distortion() {
        var input = Signals.CoherentSine(64, 8_192, 0.3f);

        double Thd(float drive) {
            using var block = Create(1f, drive);
            var rendered = BlockRender.Mono(block, Signals.Concat(input, input), 64);
            return Analysis.Thd(rendered.AsSpan(8_192), 64);
        }

        Assert.True(Thd(2f) < Thd(5f));
        Assert.True(Thd(5f) < Thd(9f));
    }

    [Fact]
    public void Fuzz_adds_even_harmonics_and_no_dc() {
        using var fuzz = Create(2f, 6f);
        var input = Signals.CoherentSine(32, 8_192, 0.3f);

        var rendered = BlockRender.Mono(fuzz, Signals.Concat(input, input, input), 64).AsSpan(16_384);
        var magnitudes = Fft.Magnitudes(rendered);

        Assert.True(magnitudes[64] > magnitudes[32] * 0.01, "second harmonic present");
        Assert.True(Math.Abs(rendered.ToArray().Average()) < 1e-3, "no DC offset");
    }

    static DriveBlock Create(float type, float drive, bool antialias = true, bool oversample = true) {
        var block = new DriveBlock { Antialias = antialias, Oversample = oversample };
        block.Params.SetTarget(DriveBlock.Type, type);
        block.Params.SetTarget(DriveBlock.Drive, drive);
        block.Params.SetTarget(DriveBlock.Tone, 10f);
        block.Prepare(new(48_000, 64));
        return block;
    }
}
