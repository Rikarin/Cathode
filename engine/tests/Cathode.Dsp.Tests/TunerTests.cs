using Cathode.Dsp.Tuning;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class TunerTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    [Theory]
    [InlineData(41.20, "E", 1)] // bass low E
    [InlineData(46.25, "F#", 1)] // 8-string low F#
    [InlineData(61.74, "B", 1)] // 7-string low B
    [InlineData(82.41, "E", 2)]
    [InlineData(110.00, "A", 2)]
    [InlineData(329.63, "E", 4)]
    [InlineData(440.00, "A", 4)]
    [InlineData(1_318.51, "E", 6)]
    public void Sines_read_the_right_note_within_half_a_cent(double frequency, string note, int octave) {
        var reading = Detect(Signals.Sine(frequency, Rate, 24_000, 0.3f));

        Assert.NotNull(reading);
        Assert.Equal(note, reading.Value.Note);
        Assert.Equal(octave, reading.Value.Octave);
        var expectedCents = 1_200
            * Math.Log2(frequency / (440 * Math.Pow(2, Math.Round(12 * Math.Log2(frequency / 440)) / 12)));
        output.WriteLine(
            $"{frequency} Hz: {reading.Value.Frequency:0.000} Hz, {reading.Value.Cents - expectedCents:+0.000;-0.000} "
            + $"cents error, clarity {reading.Value.Clarity:0.000}"
        );
        Assert.Equal(expectedCents, reading.Value.Cents, 0.5);
    }

    [Theory]
    [InlineData(82.41)]
    [InlineData(110.0)]
    public void Harmonic_rich_signals_do_not_read_an_octave_high(double frequency) {
        // Band-limited sawtooth: every harmonic present, with the 2nd at half the fundamental's level.
        var saw = new float[24_000];
        for (var h = 1; h * frequency < 20_000; h++) {
            for (var i = 0; i < saw.Length; i++) {
                saw[i] += (float)(0.3 / h * Math.Sin(2 * Math.PI * h * frequency * i / Rate));
            }
        }

        var reading = Detect(saw);

        Assert.NotNull(reading);
        Assert.Equal(frequency, reading.Value.Frequency, frequency * 0.001);
    }

    [Fact]
    public void A_plucked_string_reads_its_pitch() {
        // Karplus-Strong averaging each sample with the one after it: y[n] = (y[n-N] + y[n-N+1]) / 2, period N - 0.5.
        const int n = 436;
        var expected = Rate / (n - 0.5);
        var random = new Random(3);
        var line = Enumerable.Range(0, n).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        var pluck = new float[24_000];
        for (var i = 0; i < pluck.Length; i++) {
            var k = i % n;
            pluck[i] = 0.4f * line[k];
            line[k] = 0.996f * 0.5f * (line[k] + line[(k + 1) % n]);
        }

        var reading = Detect(pluck);

        Assert.NotNull(reading);
        Assert.Equal(0, 1_200 * Math.Log2(reading.Value.Frequency / expected), 1.0);
    }

    [Fact]
    public void Noise_and_silence_give_no_reading() {
        Assert.Null(Detect(Signals.Noise(24_000, 1, 0.3f)));
        Assert.Null(Detect(new float[24_000]));
    }

    [Fact]
    public void Calibration_moves_the_reference() {
        var reading = Detect(Signals.Sine(432, Rate, 24_000, 0.3f), 432);

        Assert.Equal("A", reading!.Value.Note);
        Assert.Equal(0, reading.Value.Cents, 0.5);
    }

    static TunerReading? Detect(float[] signal, double reference = 440) {
        var tuner = new Tuner(Rate) { Reference = reference };
        for (var offset = 0; offset < signal.Length; offset += 64) {
            tuner.Push(signal.AsSpan(offset, Math.Min(64, signal.Length - offset)));
        }

        return tuner.Detect();
    }
}
