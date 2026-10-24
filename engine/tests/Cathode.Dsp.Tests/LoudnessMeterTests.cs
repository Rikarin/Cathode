using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The BS.1770 meter against the standard's reference points.</summary>
public class LoudnessMeterTests {
    const double Rate = 48_000;

    [Fact]
    public void A_full_scale_1_khz_sine_in_one_channel_reads_minus_3_lufs() {
        var meter = Run(Signals.Sine(997, Rate, 192_000), new float[192_000]);

        Assert.Equal(-3.01, meter.Momentary, 0.1);
        Assert.Equal(-3.01, meter.ShortTerm, 0.1);
    }

    [Fact]
    public void Both_channels_add_their_power_and_level_follows_in_db() {
        var tone = Signals.Sine(997, Rate, 192_000, 0.1f);
        var meter = Run(tone, tone);

        Assert.Equal(-20, meter.Momentary, 0.1); // -20 dBFS twice is -23 + 3 LUFS
    }

    [Fact]
    public void K_weighting_lifts_the_top_and_cuts_the_deep_bass() {
        var treble = Run(Signals.Sine(8_000, Rate, 96_000), new float[96_000]).Momentary;
        var bass = Run(Signals.Sine(20, Rate, 96_000), new float[96_000]).Momentary;

        Assert.InRange(treble, -3.01 + 3, -3.01 + 4.2); // the shelf is about +3.4 dB at 8 kHz
        Assert.InRange(bass, -3.01 - 16, -3.01 - 8);
    }

    [Fact]
    public void Momentary_answers_in_400_ms_and_short_term_takes_3_s() {
        var meter = new LoudnessMeter(Rate);
        var tone = Signals.Sine(997, Rate, 24_000);
        meter.Process(tone, new float[tone.Length]); // 500 ms

        Assert.Equal(-3.01, meter.Momentary, 0.1);
        Assert.InRange(meter.ShortTerm, -16, -10); // a sixth of the window, the rest counting as silence
        meter.Process(new float[48_000], new float[48_000]);
        Assert.Equal(LoudnessMeter.FloorLufs, meter.Momentary);
        meter.Reset();
        Assert.Equal(LoudnessMeter.FloorLufs, meter.ShortTerm);
    }

    static LoudnessMeter Run(float[] left, float[] right) {
        var meter = new LoudnessMeter(Rate);
        for (var i = 0; i < left.Length; i += 64) {
            meter.Process(left.AsSpan(i, 64), right.AsSpan(i, 64));
        }

        return meter;
    }
}
