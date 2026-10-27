using Cathode.Dsp.Blocks;
using Cathode.Testing;
using static Cathode.Dsp.Tests.PitchBlockTests;

namespace Cathode.Dsp.Tests;

public class SynthBlockTests {
    const double Rate = 48_000;

    [Theory]
    [InlineData(110.0, 0f)]
    [InlineData(196.0, 12f)]
    [InlineData(440.0, -12f)]
    [InlineData(330.0, 7f)]
    public void Oscillator_follows_the_input_pitch_plus_its_shift(double frequency, float shift) {
        using var synth = Create(
            (SynthBlock.VoiceParam(0, SynthBlock.Shape), 0f), // sine, so the peak is the oscillator's own frequency
            (SynthBlock.VoiceParam(0, SynthBlock.Shift), shift),
            (SynthBlock.VoiceParam(0, SynthBlock.Cutoff), 20_000f)
        );

        var output = BlockRender.Mono(synth, PitchTrackerTests.HarmonicTone(frequency, 48_000), 64)[12_000..];

        var expected = frequency * Math.Pow(2, shift / 12.0);
        Assert.Equal(expected, PeakFrequency(output), expected * 0.003); // 5 cents
    }

    [Fact]
    public void Saw_voice_has_a_full_harmonic_series_at_the_tracked_pitch() {
        using var synth = Create(
            (SynthBlock.VoiceParam(0, SynthBlock.Shape), 2f),
            (SynthBlock.VoiceParam(0, SynthBlock.Cutoff), 20_000f),
            (SynthBlock.EnvAmount, 0f)
        );

        var output = BlockRender.Mono(synth, Signals.Sine(110, Rate, 48_000, 0.5f), 64)[12_000..];

        // A saw's harmonics fall as 1/k.
        var fundamental = Level(output, 110);
        Assert.True(fundamental > 0.1, $"fundamental {fundamental:0.000}");
        Assert.Equal(0.5, Level(output, 220) / fundamental, 0.05);
        Assert.Equal(1 / 3.0, Level(output, 330) / fundamental, 0.05);
    }

    [Fact]
    public void Silence_in_gives_exact_silence_out() {
        using var synth = Create();

        var output = BlockRender.Mono(synth, new float[48_000], 64);

        Assert.All(output, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Noise_does_not_open_the_gate() {
        using var synth = Create();

        var output = BlockRender.Mono(synth, Signals.Noise(48_000, 8, 0.3f), 64);

        Assert.True(Analysis.Peak(output) < 0.01, $"noise played the synth at {Analysis.Peak(output):0.000}");
    }

    [Fact]
    public void Amplitude_follows_the_input_and_closes_after_the_note() {
        using var synth = Create(
            (SynthBlock.VoiceParam(0, SynthBlock.Shape), 0f),
            (SynthBlock.VoiceParam(0, SynthBlock.Cutoff), 20_000f),
            (SynthBlock.Release, 20f)
        );
        var input = Signals.Concat(
            Signals.Sine(220, Rate, 24_000, 0.4f),
            Signals.Sine(220, Rate, 24_000, 0.1f),
            new float[48_000]
        );

        var output = BlockRender.Mono(synth, input, 64);

        var loud = Analysis.Peak(output.AsSpan(12_000, 12_000));
        var quiet = Analysis.Peak(output.AsSpan(36_000, 12_000));
        Assert.Equal(0.4, loud, 0.06);
        Assert.Equal(0.1, quiet, 0.02);
        Assert.All(output[72_000..], static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void A_drone_filter_rests_at_its_cutoff_in_silence_and_opens_as_you_play() {
        static SynthBlock Drone() =>
            Create(
                (SynthBlock.VoiceParam(0, SynthBlock.Shape), 2f), // saw
                (SynthBlock.VoiceParam(0, SynthBlock.Track), SynthBlock.TrackOff),
                (SynthBlock.VoiceParam(0, SynthBlock.Frequency), 220f),
                (SynthBlock.VoiceParam(0, SynthBlock.Cutoff), 200f),
                (SynthBlock.VoiceParam(0, SynthBlock.Resonance), 0f),
                (SynthBlock.VoiceParam(1, SynthBlock.VoiceLevel), Decibels.SilenceDb),
                (SynthBlock.VoiceParam(2, SynthBlock.VoiceLevel), Decibels.SilenceDb),
                (SynthBlock.EnvAmount, 100f)
            );

        using var silent = Drone();
        using var played = Drone();

        var resting = BlockRender.Mono(silent, new float[48_000], 64)[12_000..];
        var opened = BlockRender.Mono(played, Signals.Sine(110, Rate, 48_000, 0.5f), 64)[12_000..];

        // The eighth harmonic, three octaves over the 200 Hz cutoff, comes up once the playing envelope opens it.
        Assert.True(Level(resting, 220) > 0.1, "the drone sounds in silence");
        Assert.True(
            Level(opened, 1_760) > 10 * Level(resting, 1_760),
            $"opened {Level(opened, 1_760):0.0000}, resting {Level(resting, 1_760):0.0000}"
        );
    }

    [Fact]
    public void Mix_blends_the_dry_signal_back_in() {
        using var synth = Create((SynthBlock.Mix, 0f));
        var input = PitchTrackerTests.HarmonicTone(147, 9_600);

        Assert.Equal(input, BlockRender.Mono(synth, input, 64));
    }

    static SynthBlock Create(params (int Index, float Value)[] settings) {
        var block = new SynthBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
