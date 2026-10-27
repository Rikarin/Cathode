using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class RotaryBlockTests {
    const double Rate = 48_000;
    const double Tone = 2_000;

    [Theory]
    [InlineData(1f)]
    [InlineData(3f)]
    public void Horn_reaches_the_fast_speed_only_after_its_acceleration_time(float accel) {
        // Horn only, amplitude modulation only: the envelope of a 2 kHz tone rises once per horn revolution.
        using var rotary = Create(
            (RotaryBlock.HornSlow, 1f),
            (RotaryBlock.HornFast, 6f),
            (RotaryBlock.HornAccel, accel),
            (RotaryBlock.HornDepth, 0f),
            (RotaryBlock.AmDepth, 100f),
            (RotaryBlock.DrumLevel, -80f),
            (RotaryBlock.Crossover, 200f),
            (RotaryBlock.Spread, 0f)
        );
        const int switchAt = 3 * 48_000;
        var input = Signals.Sine(Tone, Rate, switchAt + 6 * 48_000, 0.5f);
        var slow = BlockRender.Mono(rotary, input[..switchAt], 64);
        rotary.Params.SetTarget(RotaryBlock.Speed, 1f);
        var fast = BlockRender.Mono(rotary, input[switchAt..], 64);

        var revolutions = Revolutions(Signals.Concat(slow, fast));
        var switchTime = switchAt / Rate;
        var before = revolutions.Where(r => r.Time > 0.3 && r.Time < switchTime - 0.5).ToArray();
        var settled = revolutions.Where(r => r.Time > switchTime + 1.5 * accel).ToArray();
        var reached = revolutions.First(static r => r.Rate > 5.5).Time - switchTime;

        // Exponential approach settling within 5 % in the accel time: 90 % of the change after accel ln(10) / 3.
        Assert.NotEmpty(before);
        Assert.NotEmpty(settled);
        Assert.All(before, static r => Assert.Equal(1, r.Rate, 0.03));
        Assert.InRange(reached, accel * Math.Log(10) / 3 - 0.2, accel * Math.Log(10) / 3 + 0.2);
        Assert.All(settled, static r => Assert.Equal(6, r.Rate, 0.12));
    }

    [Theory]
    [InlineData(1f, 4.0)]
    [InlineData(0f, 4.0 / 6)]
    public void With_tempo_the_fast_horn_turns_once_per_division_and_slow_keeps_its_ratio(
        float speed,
        double expected
    ) {
        // 1/8 at the renderer's 120 bpm is 4 Hz for Horn Fast (6 Hz); Horn Slow (1 Hz) keeps its sixth of it.
        using var rotary = Create(
            (RotaryBlock.Speed, speed),
            (RotaryBlock.Tempo, 6f),
            (RotaryBlock.HornSlow, 1f),
            (RotaryBlock.HornFast, 6f),
            (RotaryBlock.HornDepth, 0f),
            (RotaryBlock.AmDepth, 100f),
            (RotaryBlock.DrumLevel, -80f),
            (RotaryBlock.Crossover, 200f),
            (RotaryBlock.Spread, 0f)
        );

        var output = BlockRender.Mono(rotary, Signals.Sine(Tone, Rate, 8 * 48_000, 0.5f), 64);

        var settled = Revolutions(output).Where(static r => r.Time > 2).ToArray();
        Assert.NotEmpty(settled);
        Assert.All(settled, r => Assert.Equal(expected, r.Rate, expected * 0.05));
    }

    [Fact]
    public void Doppler_puts_bessel_sidebands_at_the_rotor_rate() {
        // Horn delay = c - e cos(2 pi fr t): phase modulation with index beta = 2 pi f e, so the sidebands sit at
        // multiples of the rotor rate with amplitudes J_k(beta).
        const float depth = 20f;
        const double hornRate = 6;
        using var rotary = Create(
            (RotaryBlock.Speed, 1f),
            (RotaryBlock.HornFast, (float)hornRate),
            (RotaryBlock.HornDepth, depth),
            (RotaryBlock.AmDepth, 0f),
            (RotaryBlock.DrumLevel, -80f),
            (RotaryBlock.Crossover, 200f)
        );

        var output = BlockRender.Mono(rotary, Signals.Sine(Tone, Rate, 4_800 + 131_072, 0.5f), 64)[4_800..];
        var windowed = Hann(output);
        double Level(double frequency) => Analysis.ToDb(Analysis.MagnitudeAt(windowed, frequency, Rate));

        var beta = 2 * Math.PI * Tone * depth / 100 * RotaryBlock.MaxHornExcursionMs * 0.001;
        var carrier = Level(Tone);
        Assert.Equal(Analysis.ToDb(BesselJ(1, beta) / BesselJ(0, beta)), Level(Tone + hornRate) - carrier, 0.5);
        Assert.Equal(Analysis.ToDb(BesselJ(1, beta) / BesselJ(0, beta)), Level(Tone - hornRate) - carrier, 0.5);
        Assert.Equal(Analysis.ToDb(BesselJ(2, beta) / BesselJ(0, beta)), Level(Tone + 2 * hornRate) - carrier, 0.5);
        Assert.True(Level(Tone + hornRate / 2) < carrier - 50, "energy between the sidebands");
    }

    [Theory]
    [InlineData(1f, 2.0)]
    [InlineData(2f, 4.0)]
    [InlineData(0.5f, 1.0)]
    public void Low_rate_multiplier_scales_the_drum_speed(float multiplier, double expected) {
        // Drum only, amplitude modulation only: a 400 Hz tone (under the crossover) rises once per drum revolution.
        using var rotary = Create(
            (RotaryBlock.Speed, 1f),
            (RotaryBlock.DrumFast, 2f),
            (RotaryBlock.LowRateMultiplier, multiplier),
            (RotaryBlock.DrumDepth, 0f),
            (RotaryBlock.AmDepth, 100f),
            (RotaryBlock.HornLevel, -80f),
            (RotaryBlock.Crossover, 2_000f),
            (RotaryBlock.Spread, 0f)
        );

        var output = BlockRender.Mono(rotary, Signals.Sine(400, Rate, 6 * 48_000, 0.5f), 64);

        var settled = Revolutions(output).Where(static r => r.Time > 1.5).ToArray();
        Assert.NotEmpty(settled);
        Assert.All(settled, r => Assert.Equal(expected, r.Rate, expected * 0.05));
    }

    [Fact]
    public void Mics_hear_the_rotor_at_different_angles() {
        using var rotary = Create();
        var input = Signals.Sine(Tone, Rate, 48_000, 0.5f);

        var (left, right) = BlockRender.Stereo(rotary, input, input, static _ => 64);

        Assert.True(Analysis.NullResidualDb(left, right) > -20, "left and right mics should differ");
    }

    /// <summary>
    ///     Upward crossings of the mid level of the tone's envelope, one per revolution, with the rate implied by the
    ///     interval since the previous crossing.
    /// </summary>
    static List<(double Time, double Rate)> Revolutions(float[] signal) {
        const int window = 240; // ten periods of the tone, so the envelope has no carrier ripple
        var envelope = new double[signal.Length - window];
        double sum = 0;
        for (var i = 0; i < signal.Length; i++) {
            sum += Math.Abs(signal[i]);
            if (i >= window) {
                sum -= Math.Abs(signal[i - window]);
                envelope[i - window] = sum / window;
            }
        }

        var settled = envelope.AsSpan(4_800);
        var threshold = 0.5 * (settled.ToArray().Min() + settled.ToArray().Max());
        var result = new List<(double, double)>();
        var previous = -1.0;
        for (var i = 4_801; i < envelope.Length; i++) {
            if (envelope[i - 1] < threshold && envelope[i] >= threshold) {
                var time = i / Rate;
                if (previous >= 0) {
                    result.Add((0.5 * (time + previous), 1 / (time - previous)));
                }

                previous = time;
            }
        }

        return result;
    }

    static float[] Hann(float[] signal) {
        var result = new float[signal.Length];
        for (var i = 0; i < signal.Length; i++) {
            result[i] = (float)(signal[i] * 0.5 * (1 - Math.Cos(2 * Math.PI * i / signal.Length)));
        }

        return result;
    }

    /// <summary>Bessel function of the first kind by its power series (fine for small arguments).</summary>
    static double BesselJ(int order, double x) {
        double sum = 0;
        var term = Math.Pow(x / 2, order);
        for (var k = 1; k <= order; k++) {
            term /= k;
        }

        for (var m = 0; m < 30; m++) {
            sum += term;
            term *= -(x / 2) * (x / 2) / ((m + 1) * (m + 1 + order));
        }

        return sum;
    }

    static RotaryBlock Create(params (int Index, float Value)[] settings) {
        var block = new RotaryBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
