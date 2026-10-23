using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class VocoderBlockTests {
    const double Rate = 48_000;
    const int Length = 65_536;
    const int Settle = 9_600;

    [Theory]
    [InlineData(VocoderBlock.CarrierSaw)]
    [InlineData(VocoderBlock.CarrierPulse)]
    [InlineData(VocoderBlock.CarrierNoise)]
    public void Silent_modulator_gives_silence_from_the_internal_carrier(int carrier) {
        using var vocoder = Create((VocoderBlock.Carrier, carrier), (VocoderBlock.Chord, 6f));

        var (left, right) = BlockRender.Stereo(vocoder, new float[48_000], new float[48_000], _ => 64);

        Assert.Equal(0, Analysis.Peak(left));
        Assert.Equal(0, Analysis.Peak(right));
    }

    [Fact]
    public void Right_input_carrier_is_gated_by_the_left_modulator() {
        using var vocoder = Create((VocoderBlock.Carrier, VocoderBlock.CarrierRightInput));
        var noise = Signals.Noise(48_000, 4);

        var (left, right) = BlockRender.Stereo(vocoder, new float[48_000], noise, _ => 64);

        Assert.Equal(0, Analysis.Peak(left));
        Assert.Equal(0, Analysis.Peak(right));
    }

    [Theory]
    [InlineData(VocoderBlock.CarrierNoise)]
    [InlineData(VocoderBlock.CarrierRightInput)]
    public void Output_spectrum_follows_the_modulator_band_envelopes(int carrier) {
        // Tones at the centres of bands 5 and 14, 20 dB apart, on a white carrier. A white carrier has power in
        // proportion to a constant-Q band's width, so band 14 gains 10 log(f14 / f5) back on band 5.
        using var vocoder = Create(
            (VocoderBlock.Carrier, carrier),
            (VocoderBlock.Emphasis, 0f),
            (VocoderBlock.Attack, 20f),
            (VocoderBlock.Release, 20f)
        );
        var low = VocoderBlock.BandFrequency(5);
        var high = VocoderBlock.BandFrequency(14);
        var modulator = Mix(
            Signals.Sine(low, Rate, Settle + Length, 0.5f),
            Signals.Sine(high, Rate, Settle + Length, 0.05f)
        );
        var noise = Signals.Noise(Settle + Length, 9);
        var input = carrier == VocoderBlock.CarrierRightInput ? noise : modulator;

        var output = BlockRender.Stereo(vocoder, modulator, input, _ => 64).Left[Settle..];

        var spectrum = Fft.Magnitudes(output);
        var expected = 20 * Math.Log10(0.5 / 0.05) - 10 * Math.Log10(high / low);
        Assert.Equal(expected, BandDb(spectrum, low) - BandDb(spectrum, high), 2.0);

        // Between the tones the modulator has only the band filters' skirts, and so does the output.
        var between = VocoderBlock.BandFrequency(10);
        Assert.True(BandDb(spectrum, between) < BandDb(spectrum, low) - 15, "carrier leaks through an empty band");
    }

    [Fact]
    public void Output_follows_the_modulator_over_time() {
        using var vocoder = Create((VocoderBlock.Emphasis, 0f));
        var low = VocoderBlock.BandFrequency(4);
        var high = VocoderBlock.BandFrequency(15);
        var modulator = Signals.Concat(Signals.Sine(low, Rate, Length, 0.5f), Signals.Sine(high, Rate, Length, 0.5f));

        var output = BlockRender.Mono(vocoder, modulator, 64);

        var first = Fft.Magnitudes(output.AsSpan(Length - 32_768, 32_768));
        var second = Fft.Magnitudes(output.AsSpan(2 * Length - 32_768, 32_768));
        Assert.True(BandDb(first, low) > BandDb(first, high) + 30, "first half should sit in the low band");
        Assert.True(BandDb(second, high) > BandDb(second, low) + 30, "second half should sit in the high band");
    }

    /// <summary>Energy within half an octave either side of <paramref name="frequency" />, in dB.</summary>
    static double BandDb(double[] spectrum, double frequency) {
        var binHz = Rate / (2.0 * (spectrum.Length - 1));
        double energy = 0;
        for (var k = (int)(frequency / Math.Sqrt(2) / binHz); k <= (int)(frequency * Math.Sqrt(2) / binHz); k++) {
            energy += spectrum[k] * spectrum[k];
        }

        return 10 * Math.Log10(energy + 1e-30);
    }

    static float[] Mix(float[] a, float[] b) => a.Zip(b, static (x, y) => x + y).ToArray();

    static VocoderBlock Create(params (int Index, float Value)[] settings) {
        var block = new VocoderBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
