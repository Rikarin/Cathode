using Cathode.Dsp.Pitch;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class PitchTrackerTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>Guitar-like: a weak fundamental under a strong second harmonic, and harmonics falling off as 1/k.</summary>
    public static float[] HarmonicTone(double frequency, int length, float amplitude = 0.3f) {
        var tone = new float[length];
        for (var k = 1; k * frequency < 12_000; k++) {
            var level = k == 1 ? 0.6 : 1.0 / (k - 1);
            var phase = 0.7 * k * k; // scattered phases, as a real string has
            for (var i = 0; i < length; i++) {
                tone[i] += (float)(amplitude * level * Math.Sin(2 * Math.PI * k * frequency * i / Rate + phase));
            }
        }

        return tone;
    }

    [Theory]
    [InlineData(70.0)]
    [InlineData(82.41)]
    [InlineData(110.0)]
    [InlineData(146.83)]
    [InlineData(196.0)]
    [InlineData(246.94)]
    [InlineData(329.63)]
    [InlineData(440.0)]
    [InlineData(659.26)]
    [InlineData(987.77)]
    [InlineData(1_318.51)]
    public void Sines_and_harmonic_tones_track_within_one_cent(double frequency) {
        foreach (var (name, signal) in new[] {
                     ("sine", Signals.Sine(frequency, Rate, 24_000, 0.3f)),
                     ("harmonic", HarmonicTone(frequency, 24_000))
                 }) {
            var (estimate, voiced) = Track(signal);
            var cents = 1_200 * Math.Log2(estimate / frequency);
            output.WriteLine($"{frequency} Hz {name}: {estimate:0.000} Hz, {cents:+0.00;-0.00} cents");

            Assert.True(voiced, $"{name} at {frequency} Hz read as unvoiced");
            Assert.InRange(cents, -1, 1);
        }
    }

    [Fact]
    public void Silence_and_noise_are_unvoiced() {
        Assert.False(Track(new float[24_000]).Voiced);
        Assert.False(Track(Signals.Noise(24_000, 4, 0.3f)).Voiced);
    }

    [Fact]
    public void A_new_note_is_tracked_within_40_ms() {
        using var tracker = new PitchTracker();
        tracker.Prepare(Rate);
        var signal = Signals.Concat(HarmonicTone(110, 9_600), HarmonicTone(220, 9_600));

        var lockedAt = -1;
        for (var i = 0; i < signal.Length; i++) {
            if (tracker.Push(signal[i]) && i >= 9_600 && lockedAt < 0 && Math.Abs(tracker.Frequency - 220) < 1) {
                lockedAt = i - 9_600;
            }
        }

        output.WriteLine($"locked after {lockedAt / Rate * 1_000:0.0} ms");
        Assert.InRange(lockedAt, 0, (int)(0.04 * Rate));
    }

    [Fact]
    public void Reset_restores_a_fresh_state() {
        using var tracker = new PitchTracker();
        tracker.Prepare(Rate);
        foreach (var s in Signals.Sine(220, Rate, 9_600, 0.3f)) {
            tracker.Push(s);
        }

        tracker.Reset();

        Assert.Equal(0, tracker.Frequency);
        Assert.False(tracker.Voiced);
        Assert.Equal(0, tracker.Period);
    }

    static (double Frequency, bool Voiced) Track(float[] signal) {
        using var tracker = new PitchTracker();
        tracker.Prepare(Rate);
        foreach (var s in signal) {
            tracker.Push(s);
        }

        return (tracker.Frequency, tracker.Voiced);
    }
}
