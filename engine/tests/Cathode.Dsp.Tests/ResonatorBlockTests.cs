using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class ResonatorBlockTests {
    const double Rate = 48_000;

    [Theory]
    [InlineData(200f, 0f)]
    [InlineData(311f, 0f)]
    [InlineData(200f, 60f)]
    public void Single_voice_peaks_sit_on_the_harmonics(float frequency, float damping) {
        using var resonator = SingleVoice(frequency, 0.5f, damping);

        var response = BlockRender.Mono(resonator, Signals.Impulse(32_768), 64);

        for (var harmonic = 1; harmonic <= 4; harmonic++) {
            var expected = harmonic * frequency;
            var peak = PeakNear(response, expected, expected * 0.02);

            // Damping disperses the upper harmonics slightly.
            Assert.InRange(peak, expected * 0.999 - 0.3, expected * 1.001 + 0.3);
            var trough = Analysis.MagnitudeAt(response, expected + frequency / 2, Rate);
            Assert.True(
                Analysis.ToDb(Analysis.MagnitudeAt(response, peak, Rate) / trough) > 20,
                $"harmonic {harmonic} is not a peak"
            );
        }
    }

    [Fact]
    public void Chord_mode_tunes_the_voices_to_the_chord_on_the_root() {
        // A2 major: A2, C#3, E3, A3.
        using var resonator = Create((ResonatorBlock.Root, 45f), (ResonatorBlock.Chord, 3f), (ResonatorBlock.Mix, 100f));

        var response = BlockRender.Mono(resonator, Signals.Impulse(65_536), 64);

        foreach (var note in new[] { 45, 49, 52, 57 }) {
            var expected = 440 * Math.Pow(2, (note - 69) / 12.0);
            Assert.InRange(PeakNear(response, expected, expected * 0.02), expected - 0.3, expected + 0.3);
        }
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void Ringing_decays_60_db_per_decay_time(float decay) {
        using var resonator = SingleVoice(200, decay, 0);
        var response = BlockRender.Mono(resonator, Signals.Impulse(48_000), 64);

        // The fundamental's level in two short windows 300 ms apart.
        double Level(int start) {
            var window = response.AsSpan(start, 4_800).ToArray();
            for (var i = 0; i < window.Length; i++) {
                window[i] *= (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / window.Length)));
            }

            return Analysis.ToDb(Analysis.MagnitudeAt(window, 200, Rate));
        }

        var drop = Level(4_800) - Level(4_800 + 14_400);
        Assert.Equal(60 * 0.3 / decay, drop, 60 * 0.3 / decay * 0.05 + 0.3);
    }

    [Fact]
    public void Longest_decay_at_the_highest_pitch_stays_bounded() {
        using var resonator = Create(
            (ResonatorBlock.Tuning, 1f),
            (ResonatorBlock.Frequency1, ResonatorBlock.MaxFrequency),
            (ResonatorBlock.Frequency1 + 1, ResonatorBlock.MinFrequency),
            (ResonatorBlock.Decay, 10f),
            (ResonatorBlock.Damping, 0f),
            (ResonatorBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(resonator, Signals.Noise(10 * 48_000, 2), 64);

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Peak(output.AsSpan(9 * 48_000)) < 4 * Analysis.Peak(output.AsSpan(48_000, 48_000)) + 1);
    }

    static double PeakNear(float[] response, double frequency, double span) {
        var best = 0.0;
        var at = frequency;
        for (var f = frequency - span; f <= frequency + span; f += 0.1) {
            var magnitude = Analysis.MagnitudeAt(response, f, Rate);
            if (magnitude > best) {
                best = magnitude;
                at = f;
            }
        }

        return at;
    }

    static ResonatorBlock SingleVoice(float frequency, float decay, float damping) =>
        Create(
            (ResonatorBlock.Tuning, 1f),
            (ResonatorBlock.Frequency1, frequency),
            (ResonatorBlock.Level1 + 1, -80f),
            (ResonatorBlock.Level1 + 2, -80f),
            (ResonatorBlock.Level1 + 3, -80f),
            (ResonatorBlock.Decay, decay),
            (ResonatorBlock.Damping, damping),
            (ResonatorBlock.Mix, 100f)
        );

    static ResonatorBlock Create(params (int Index, float Value)[] settings) {
        var block = new ResonatorBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
