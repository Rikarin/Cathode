using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Filter block's ladder types and its own envelope follower.</summary>
public class FilterBlockTests {
    const double Rate = 48_000;

    /// <summary>
    ///     Small enough to stay under the ladder's saturation, so the response measured is the linear one.
    /// </summary>
    const float Small = 0.001f;

    [Theory]
    [InlineData(FilterBlock.Ladder24, -12.04, 24.0)] // four poles: 3 dB each at the cut-off
    [InlineData(FilterBlock.Ladder12, -6.02, 12.0)] // taken after the second
    public void Ladder_is_flat_below_its_cut_off_and_falls_at_its_slope(int type, double atCutOff, double perOctave) {
        using var block = TestBlocks.Create<FilterBlock>((FilterBlock.Type, type), (FilterBlock.Frequency, 200f));

        var response = Response(block);

        Assert.Equal(0, Db(response, 20), 0.2);
        Assert.Equal(atCutOff, Db(response, 200), 0.1);
        Assert.Equal(perOctave, Db(response, 1_600) - Db(response, 3_200), 0.5);
    }

    [Theory]
    [InlineData(FilterBlock.Ladder24)]
    [InlineData(FilterBlock.Ladder12)]
    public void Ladder_resonance_peaks_at_the_cut_off_and_leaves_the_pass_band_at_unity(int type) {
        using var block = TestBlocks.Create<FilterBlock>(
            (FilterBlock.Type, type),
            (FilterBlock.Frequency, 1_000f),
            (FilterBlock.Q, 8f)
        );

        var response = Response(block);

        Assert.Equal(0, Db(response, 30), 0.1);
        var peak = Db(response, 1_000);
        Assert.True(peak > 12, $"peak {peak:F1} dB");
        Assert.True(peak > Db(response, 900) && peak > Db(response, 1_100));
    }

    [Fact]
    public void Ladder_drive_saturates_a_loud_signal_and_keeps_a_quiet_one_at_its_level() {
        const int length = 16_384;
        const int cycles = 64; // 187.5 Hz, far below the cut-off
        var loud = Signals.CoherentSine(2 * cycles, 2 * length, 0.5f);
        var quiet = Signals.CoherentSine(2 * cycles, 2 * length, 0.0005f);

        float[] Render(float drive, float[] input) {
            using var block = TestBlocks.Create<FilterBlock>(
                (FilterBlock.Type, FilterBlock.Ladder24),
                (FilterBlock.Frequency, 10_000f),
                (FilterBlock.Drive, drive)
            );
            return BlockRender.Mono(block, input, 64)[length..];
        }

        Assert.True(Analysis.Thd(Render(0f, loud), cycles) < 0.005);
        var driven = Render(30f, loud);
        Assert.True(Analysis.Thd(driven, cycles) > 0.1);
        Assert.True(Analysis.Peak(driven) < 0.5);
        Assert.Equal(0, Analysis.ToDb(Analysis.Rms(Render(30f, quiet)) / Analysis.Rms(quiet.AsSpan(length))), 0.1);
    }

    [Fact]
    public void Ladder_at_full_resonance_stays_bounded_under_a_sweep() {
        using var block = TestBlocks.Create<FilterBlock>(
            (FilterBlock.Type, FilterBlock.Ladder24),
            (FilterBlock.Q, 18f),
            (FilterBlock.Frequency, 100f),
            (FilterBlock.ModFrequency, 15_000f),
            (FilterBlock.LfoOn, 1f),
            (FilterBlock.LfoRate, 10f),
            (FilterBlock.Drive, 36f)
        );

        var output = BlockRender.Mono(block, Signals.Noise(48_000, 7), 64);

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Peak(output) < 4);
    }

    [Fact]
    public void Envelope_opens_the_filter_for_a_loud_signal_and_not_for_a_quiet_one() {
        // A low-pass at 200 Hz that the envelope may open by four octaves, to 3.2 kHz; the tone is at 2 kHz.
        static double GainDb(float amplitude, float range) {
            using var block = TestBlocks.Create<FilterBlock>(
                (FilterBlock.Frequency, 200f),
                (FilterBlock.EnvRange, range),
                (FilterBlock.EnvSensitivity, 0f)
            );
            var input = Signals.Sine(2_000, Rate, 24_000, amplitude);
            var output = BlockRender.Mono(block, input, 64);
            return Analysis.ToDb(Analysis.Rms(output.AsSpan(12_000)) / Analysis.Rms(input.AsSpan(12_000)));
        }

        Assert.True(GainDb(1f, 4f) > -3);
        Assert.True(GainDb(0.01f, 4f) < -35);
        Assert.Equal(GainDb(0.01f, 0f), GainDb(1f, 0f), 0.01); // no range, no envelope
    }

    [Fact]
    public void Envelope_sensitivity_makes_a_quiet_signal_open_it_and_a_negative_range_closes_it() {
        static double GainDb(float sensitivity, float frequency, float range) {
            using var block = TestBlocks.Create<FilterBlock>(
                (FilterBlock.Frequency, frequency),
                (FilterBlock.EnvRange, range),
                (FilterBlock.EnvSensitivity, sensitivity)
            );
            var input = Signals.Sine(2_000, Rate, 24_000, 0.01f);
            var output = BlockRender.Mono(block, input, 64);
            return Analysis.ToDb(Analysis.Rms(output.AsSpan(12_000)) / Analysis.Rms(input.AsSpan(12_000)));
        }

        Assert.True(GainDb(40f, 200f, 4f) > -3); // 40 dB brings −40 dBFS to full scale
        Assert.True(GainDb(40f, 3_200f, -4f) < -35);
    }

    [Fact]
    public void Envelope_falls_back_over_its_release_when_the_signal_stops() {
        // A loud burst opens the filter; the quiet tone after it is let through at first and shut out once released.
        using var block = TestBlocks.Create<FilterBlock>(
            (FilterBlock.Frequency, 200f),
            (FilterBlock.EnvRange, 4f),
            (FilterBlock.EnvSensitivity, 0f),
            (FilterBlock.EnvRelease, 100f)
        );
        var tail = Signals.Sine(2_000, Rate, 48_000, 0.001f);
        var input = Signals.Concat(Signals.Sine(2_000, Rate, 9_600), tail);

        var output = BlockRender.Mono(block, input, 64);

        double TailDb(int from) =>
            Analysis.ToDb(Analysis.Rms(output.AsSpan(9_600 + from, 480)) / Analysis.Rms(tail.AsSpan(from, 480)));

        Assert.True(TailDb(480) > -10, $"just after: {TailDb(480):F1} dB");
        Assert.True(TailDb(40_000) < -35, $"released: {TailDb(40_000):F1} dB");
    }

    [Fact]
    public void Envelope_and_ladder_do_not_depend_on_the_callback_size() {
        static float[] Render(int size) {
            using var block = TestBlocks.Create<FilterBlock>(
                (FilterBlock.Type, FilterBlock.Ladder24),
                (FilterBlock.Frequency, 300f),
                (FilterBlock.Q, 4f),
                (FilterBlock.EnvRange, 4f)
            );
            return BlockRender.Mono(block, Signals.Noise(9_600, 3, 0.25f), size);
        }

        Assert.True(Analysis.NullResidualDb(Render(64), Render(37)) < -100);
    }

    [Fact]
    public void The_new_types_and_knobs_follow_the_old_ones() {
        // Stored presets address types by index and parameters by id: both lists only grow at the end.
        var info = FilterBlock.Info;
        Assert.Equal("Peaking 2", info.Params[FilterBlock.Type].Options![FilterBlock.Peaking2]);
        Assert.Equal(17, info.Params[FilterBlock.Type].Options!.Count);
        Assert.Equal("lfoQuantize", info.Params[FilterBlock.LfoQuantize].Id);
        Assert.Equal("envRelease", info.Params[FilterBlock.EnvRelease].Id);
        Assert.Equal(0f, info.Params[FilterBlock.EnvRange].Default);
    }

    static float[] Response(FilterBlock block) => BlockRender.Mono(block, Signals.Impulse(32_768, Small), 64);

    static double Db(float[] response, double frequency) =>
        Analysis.ToDb(Analysis.MagnitudeAt(response, frequency, Rate) / Small);
}
