using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class PlexBlockTests {
    const double Rate = 48_000;

    static readonly (int Index, float Value)[] Clean = [
        (PlexBlock.Mix, 100f),
        (PlexBlock.Diffusion, 0f),
        (PlexBlock.LowCut, 20f),
        (PlexBlock.HighCut, 20_000f)
    ];

    [Fact]
    public void First_echoes_arrive_at_the_line_times_with_their_pans() {
        using var plex = Create([..Clean, (PlexBlock.Delays, 4f), (PlexBlock.PreDelay, 10f)]);
        var impulse = Signals.Impulse(20_000);

        var (left, right) = BlockRender.Stereo(plex, impulse, impulse, _ => 64);

        // Four lines (143, 197, 263, 331 ms) after a 10 ms pre-delay, each carrying 1/√4 of the input; line 1 is hard left,
        // line 2 hard right, line 3 at -75 % (right side 0.25), line 4 at +75 %.
        Assert.Equal(0.5f, left[(int)(0.153 * Rate)], 1e-5f);
        Assert.Equal(0f, right[(int)(0.153 * Rate)], 1e-6f);
        Assert.Equal(0.5f, right[(int)(0.207 * Rate)], 1e-5f);
        Assert.Equal(0.125f, right[(int)(0.273 * Rate)], 1e-5f);
        Assert.Equal(0f, left.AsSpan(0, (int)(0.153 * Rate)).ToArray().Max(MathF.Abs), 1e-6f);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void The_network_decays_by_60_dB_in_the_decay_time(float decay) {
        using var plex = Create([..Clean, (PlexBlock.Decay, decay), (PlexBlock.Diffusion, 60f)]);
        var input = Signals.Concat(Signals.Noise(4_800, 2, 0.5f), new float[(int)(Rate * (decay * 1.5 + 1))]);

        var output = BlockRender.Mono(plex, input, 64);

        // Schroeder's backward-integrated energy: the time from -5 to -25 dB is a third of the T60.
        var energy = new double[output.Length + 1];
        for (var i = output.Length - 1; i >= 0; i--) {
            energy[i] = energy[i + 1] + (double)output[i] * output[i];
        }

        int Below(double db) => Array.FindIndex(energy, e => 10 * Math.Log10(e / energy[0]) < db);
        var measured = 3.0 * (Below(-25) - Below(-5)) / Rate;
        Assert.Equal(decay, measured, decay * 0.15);
    }

    [Theory]
    [InlineData(PlexBlock.PlexDetune, 1.0594631)] // +50 cents at 200 % master detune: a semitone
    [InlineData(PlexBlock.PlexShifter, 2.0)] // shift 1 is an octave up
    public void Shifters_move_each_repeat_by_their_interval(int type, double ratio) {
        using var plex = Create(
            [
                ..Clean,
                (PlexBlock.Type, type),
                (PlexBlock.Delays, 1f),
                (PlexBlock.Decay, 0.1f),
                (PlexBlock.Detune1, 50f),
                (PlexBlock.MasterDetune, 200f),
                (PlexBlock.Granule, 40f)
            ]
        );

        // 300 Hz repeats a whole number of times in half a 40 ms grain, so the shifter's two grains add in phase (at
        // other frequencies they partly cancel and the grain-rate sidebands can outgrow the shifted tone).
        var output = BlockRender.Mono(plex, Signals.Sine(300, Rate, 48_000, 0.5f), 64);

        Assert.Equal(300 * ratio, Frequency(output.AsSpan(24_000, 16_384)), 300 * ratio * 0.01);
    }

    [Fact]
    public void Shimmer_adds_the_octave_above_by_its_intensity() {
        double Octave(float intensity) {
            using var plex = Create(
                [
                    ..Clean, (PlexBlock.Type, PlexBlock.ShimmerVerb), (PlexBlock.ShimmerIntensity, intensity),
                    (PlexBlock.Decay, 2f)
                ]
            );
            var output = BlockRender.Mono(plex, Signals.Sine(330, Rate, 48_000, 0.5f), 64)
                .AsSpan(16_384, 16_384)
                .ToArray();
            var magnitudes = Fft.Magnitudes(output);
            return magnitudes[(int)Math.Round(660 * 16_384 / Rate)] / magnitudes[(int)Math.Round(330 * 16_384 / Rate)];
        }

        Assert.True(Octave(80f) > 10 * Octave(0f), $"{Octave(80f):0.000} vs {Octave(0f):0.000}");
    }

    [Fact]
    public void Hold_sustains_the_network_and_closes_the_input() {
        using var plex = Create([..Clean, (PlexBlock.Decay, 0.5f), (PlexBlock.Diffusion, 60f)]);
        var burst = Signals.Concat(Signals.Noise(9_600, 4, 0.3f), new float[4_800]);
        BlockRender.Mono(plex, burst, 64);
        plex.Params.SetTarget(PlexBlock.Hold, PlexBlock.HoldOn);

        var held = BlockRender.Mono(plex, Signals.Noise(48_000 * 3, 5, 0.3f), 64);

        // The input is closed, so the tail neither grows from the new noise nor dies away.
        var early = Analysis.Rms(held.AsSpan(48_000, 24_000));
        var late = Analysis.Rms(held.AsSpan(48_000 * 2, 24_000));
        Assert.Equal(0, Analysis.ToDb(late / early), 1.0);
    }

    [Fact]
    public void Plex_verb_size_scales_its_lines() {
        float First(float size) {
            using var plex = Create([..Clean, (PlexBlock.Type, PlexBlock.PlexVerb), (PlexBlock.VerbSize, size)]);
            var output = BlockRender.Mono(plex, Signals.Impulse(10_000), 64);
            return Array.FindIndex(output, static s => MathF.Abs(s) > 1e-6f);
        }

        // The shortest line is 31.1 ms × (0.3 + 1.4 × size).
        Assert.InRange(First(50f), (int)(0.0311 * Rate) - 1, (int)(0.0311 * Rate) + 1);
        Assert.InRange(First(100f), (int)(0.0311 * 1.7 * Rate) - 1, (int)(0.0311 * 1.7 * Rate) + 1);
    }

    /// <summary>
    ///     The frequency of the strongest partial, from the spectral peak refined by a parabola through the log magnitudes
    ///     (zero crossings miscount where a granular shifter's two grains cancel).
    /// </summary>
    static double Frequency(ReadOnlySpan<float> signal) {
        var windowed = new float[signal.Length];
        for (var i = 0; i < signal.Length; i++) {
            windowed[i] = signal[i] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / signal.Length));
        }

        var magnitudes = Fft.Magnitudes(windowed);
        var peak = 1;
        for (var k = 2; k < magnitudes.Length - 1; k++) {
            if (magnitudes[k] > magnitudes[peak]) {
                peak = k;
            }
        }

        var (a, b, c) = (Math.Log(magnitudes[peak - 1]), Math.Log(magnitudes[peak]), Math.Log(magnitudes[peak + 1]));
        return (peak + 0.5 * (a - c) / (a - 2 * b + c)) * Rate / signal.Length;
    }

    static PlexBlock Create(params (int Index, float Value)[] settings) => TestBlocks.Create<PlexBlock>(settings);
}
