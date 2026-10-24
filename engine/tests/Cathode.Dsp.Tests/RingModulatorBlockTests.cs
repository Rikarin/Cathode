using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class RingModulatorBlockTests {
    const int Length = 16_384;
    const double BinHz = 48_000.0 / Length;

    [Fact]
    public void A_sine_becomes_sum_and_difference_sidebands_without_input_or_carrier() {
        const int InputBin = 341; // ~999 Hz
        const int CarrierBin = 103; // ~302 Hz
        using var block = TestBlocks.Create<RingModulatorBlock>(
            (RingModulatorBlock.Frequency, (float)(CarrierBin * BinHz))
        );
        var input = Signals.Sine(InputBin * BinHz, 48_000, 2 * Length);

        var output = BlockRender.Mono(block, input, 64);
        var spectrum = Fft.Magnitudes(output.AsSpan(Length));

        // sin(a) sin(b) = (cos(a - b) - cos(a + b)) / 2
        Assert.Equal(0.5, spectrum[InputBin - CarrierBin], 0.01);
        Assert.Equal(0.5, spectrum[InputBin + CarrierBin], 0.01);
        Assert.True(
            Analysis.ToDb(spectrum[CarrierBin]) < -100,
            $"carrier at {Analysis.ToDb(spectrum[CarrierBin]):0} dB"
        );
        Assert.True(Analysis.ToDb(spectrum[InputBin]) < -100, $"input at {Analysis.ToDb(spectrum[InputBin]):0} dB");
    }

    [Fact]
    public void High_cut_attenuates_the_upper_sideband() {
        using var block = TestBlocks.Create<RingModulatorBlock>(
            (RingModulatorBlock.Frequency, (float)(1_024 * BinHz)),
            (RingModulatorBlock.HighCut, 1_000f)
        );
        var input = Signals.Sine(512 * BinHz, 48_000, 2 * Length); // sidebands at 1.5 kHz and 4.5 kHz

        var spectrum = Fft.Magnitudes(BlockRender.Mono(block, input, 64).AsSpan(Length));

        // A 2nd-order Butterworth at 1 kHz: -7.9 dB at 1.5 kHz, -26.3 dB at 4.5 kHz (on 0.5 per sideband).
        Assert.Equal(0.5 * 0.405, spectrum[512], 0.005);
        Assert.Equal(0.5 * 0.0466, spectrum[1_536], 0.002);
    }

    [Fact]
    public void Tempo_sync_sets_the_carrier_from_the_song_tempo() {
        // 120 BPM, 1/16: four cycles per beat = 8 Hz. On DC the output is the carrier itself.
        using var block = TestBlocks.Create<RingModulatorBlock>((RingModulatorBlock.Tempo, 3f));

        var output = BlockRender.Mono(block, Signals.Step(48_000), 64);

        var crossings = 0;
        for (var i = 1; i < output.Length; i++) {
            if (output[i - 1] < 0 && output[i] >= 0) {
                crossings++;
            }
        }

        Assert.InRange(crossings, 7, 8);
    }

    [Fact]
    public void Zero_mix_is_dry() {
        using var block = TestBlocks.Create<RingModulatorBlock>((RingModulatorBlock.Mix, 0f));
        var input = Signals.Noise(4_800, 3);

        Assert.Equal(input, BlockRender.Mono(block, input, 64));
    }
}
