using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class BiquadTests
{
    private const double SampleRate = 48_000;

    public static TheoryData<BiquadType, double, double, double> Designs => new()
    {
        { BiquadType.LowPass, 1000, 0.7071, 0 },
        { BiquadType.HighPass, 80, 0.7071, 0 },
        { BiquadType.BandPass, 2000, 2, 0 },
        { BiquadType.Notch, 60, 10, 0 },
        { BiquadType.Peak, 800, 1.5, 6 },
        { BiquadType.LowShelf, 200, 0.7071, -9 },
        { BiquadType.HighShelf, 5000, 0.7071, 4 },
        { BiquadType.LowPass, 20, 0.7071, 0 }, // low frequency: needs double-precision state
    };

    [Theory]
    [MemberData(nameof(Designs))]
    public void Filter_implementation_matches_its_transfer_function(BiquadType type, double frequency, double q, double gainDb)
    {
        var c = BiquadCoefficients.Design(type, SampleRate, frequency, q, gainDb);
        var impulseResponse = ImpulseResponse(c, length: frequency < 100 ? 48_000 : 8192);

        foreach (var f in LogFrequencies())
        {
            var measured = Analysis.ToDb(Analysis.MagnitudeAt(impulseResponse, f, SampleRate));
            var expected = Analysis.ToDb(c.MagnitudeAt(f, SampleRate));
            if (expected > -60)
            {
                Assert.True(Math.Abs(measured - expected) < 0.05, $"{type} at {f:0} Hz: measured {measured:0.000} dB, expected {expected:0.000} dB");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void State_variable_filter_matches_the_rbj_response(BiquadType type, double frequency, double q, double gainDb)
    {
        var svf = SvfCoefficients.Design(type, SampleRate, frequency, q, gainDb);
        var samples = Signals.Impulse(frequency < 100 ? 48_000 : 8192);
        var state = default(SvfState);
        state.Process(svf, samples);
        var reference = BiquadCoefficients.Design(type, SampleRate, frequency, q, gainDb);

        foreach (var f in LogFrequencies())
        {
            var expected = Analysis.ToDb(reference.MagnitudeAt(f, SampleRate));
            if (expected > -60)
            {
                Assert.Equal(expected, Analysis.ToDb(Analysis.MagnitudeAt(samples, f, SampleRate)), 0.05);
            }
        }
    }

    [Fact]
    public void Butterworth_low_pass_is_unity_at_dc_and_minus_3db_at_cutoff()
    {
        var c = BiquadCoefficients.Design(BiquadType.LowPass, SampleRate, 1000, 1 / Math.Sqrt(2));

        Assert.Equal(0, Analysis.ToDb(c.MagnitudeAt(1e-3, SampleRate)), 1e-6);
        Assert.Equal(-3.0103, Analysis.ToDb(c.MagnitudeAt(1000, SampleRate)), 1e-3);
        Assert.True(Analysis.ToDb(c.MagnitudeAt(10_000, SampleRate)) < -38);
    }

    [Fact]
    public void Peaking_filter_hits_its_gain_at_centre_and_is_flat_far_away()
    {
        var c = BiquadCoefficients.Design(BiquadType.Peak, SampleRate, 800, 1.5, 6);

        Assert.Equal(6, Analysis.ToDb(c.MagnitudeAt(800, SampleRate)), 1e-6);
        Assert.Equal(0, Analysis.ToDb(c.MagnitudeAt(20, SampleRate)), 0.05);
        Assert.Equal(0, Analysis.ToDb(c.MagnitudeAt(20_000, SampleRate)), 0.05);
    }

    [Fact]
    public void Shelves_reach_their_gain_on_the_shelf_side()
    {
        var low = BiquadCoefficients.Design(BiquadType.LowShelf, SampleRate, 200, 0.7071, -9);
        var high = BiquadCoefficients.Design(BiquadType.HighShelf, SampleRate, 5000, 0.7071, 4);

        Assert.Equal(-9, Analysis.ToDb(low.MagnitudeAt(10, SampleRate)), 0.05);
        Assert.Equal(0, Analysis.ToDb(low.MagnitudeAt(15_000, SampleRate)), 0.05);
        Assert.Equal(4, Analysis.ToDb(high.MagnitudeAt(23_000, SampleRate)), 0.1);
        Assert.Equal(0, Analysis.ToDb(high.MagnitudeAt(50, SampleRate)), 0.05);
    }

    [Theory]
    [InlineData(BiquadType.LowShelf, -24)]
    [InlineData(BiquadType.LowShelf, 24)]
    [InlineData(BiquadType.HighShelf, -24)]
    [InlineData(BiquadType.HighShelf, 24)]
    public void Shelves_never_overshoot_their_gain_by_more_than_one_and_a_half_decibels(BiquadType type, double gainDb)
    {
        // Regression: the bounded-output property found a 20 Hz high shelf at Q 15.5 ringing at +65 dBFS.
        var c = BiquadCoefficients.Design(type, SampleRate, 200, q: 18, gainDb);
        var limit = Math.Max(0, gainDb) + 1.5; // Q = 1 allows a deliberate ~1.1 dB bump

        Assert.All(LogFrequencies(), f => Assert.True(Analysis.ToDb(c.MagnitudeAt(f, SampleRate)) < limit, $"{f:0} Hz"));
    }

    [Fact]
    public void Filter_block_renders_the_designed_response()
    {
        using var block = new FilterBlock();
        block.Params.SetTarget(FilterBlock.Type, (float)BiquadType.HighPass);
        block.Params.SetTarget(FilterBlock.Frequency, 80);
        block.Prepare(new ProcessSpec(SampleRate, 64));
        var impulseResponse = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        var expected = BiquadCoefficients.Design(BiquadType.HighPass, SampleRate, 80, FilterBlock.Info.Params[FilterBlock.Q].Default);
        foreach (var f in new[] { 40.0, 80, 160, 1000 })
        {
            Assert.Equal(Analysis.ToDb(expected.MagnitudeAt(f, SampleRate)), Analysis.ToDb(Analysis.MagnitudeAt(impulseResponse, f, SampleRate)), 0.05);
        }
    }

    private static float[] ImpulseResponse(BiquadCoefficients c, int length)
    {
        var samples = Signals.Impulse(length);
        var state = default(BiquadState);
        state.Process(c, samples);
        return samples;
    }

    private static IEnumerable<double> LogFrequencies()
    {
        for (var f = 20.0; f < 22_000; f *= 1.25)
        {
            yield return f;
        }
    }
}
