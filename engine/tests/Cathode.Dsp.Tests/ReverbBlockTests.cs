using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class ReverbBlockTests {
    [Theory]
    [InlineData(0f, 0.8f)]
    [InlineData(1f, 2f)]
    [InlineData(1f, 5f)]
    [InlineData(2f, 3f)]
    [InlineData(3f, 1.5f)]
    [InlineData(3f, 4f)]
    [InlineData(4f, 1f)]
    [InlineData(4f, 3f)]
    public void Measured_decay_time_matches_the_setting(float type, float decaySeconds) {
        // Damping fully open and no low cut, so the broadband decay is set by the line gains alone.
        using var reverb = Create(
            (ReverbBlock.Type, type),
            (ReverbBlock.Decay, decaySeconds),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        );
        var length = (int)(48_000 * decaySeconds * 1.2f);

        var response = BlockRender.Mono(reverb, Signals.Impulse(length), 64);

        var t60 = SchroederT60(response, 48_000);
        Assert.InRange(t60, decaySeconds * 0.8, decaySeconds * 1.2);
    }

    [Fact]
    public void Spring_disperses_a_click_into_a_falling_chirp() {
        // A short decay, so the first pass through the springs dominates the start of the response.
        using var reverb = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.Decay, 0.3f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        );

        var response = BlockRender.Mono(reverb, Signals.Impulse(4_800), 64);

        var low = ArrivalMs(response, 300);
        var high = ArrivalMs(response, 3_000);
        Assert.True(low > high + 10, $"300 Hz arrives at {low:0.0} ms, 3 kHz at {high:0.0} ms");
    }

    [Theory]
    [InlineData(ReverbBlock.Dattorro)]
    [InlineData(ReverbBlock.Spring)]
    public void New_tanks_are_stereo_and_switch_types_without_replaying_old_tails(int type) {
        using var reverb = Create((ReverbBlock.Type, type), (ReverbBlock.Mix, 100f));
        var (left, right) = BlockRender.Stereo(reverb, Signals.Impulse(48_000), Signals.Impulse(48_000), _ => 64);

        double dot = 0, ll = 0, rr = 0;
        for (var i = 2_400; i < left.Length; i++) {
            dot += left[i] * right[i];
            ll += left[i] * left[i];
            rr += right[i] * right[i];
        }

        Assert.True(Math.Abs(dot / Math.Sqrt(ll * rr)) < 0.5, $"correlation {dot / Math.Sqrt(ll * rr):0.00}");

        // Switch to the FDN hall and back: the tank must restart empty, not resume the old tail.
        reverb.Params.SetTarget(ReverbBlock.Type, 1);
        BlockRender.Mono(reverb, new float[4_800], 64);
        reverb.Params.SetTarget(ReverbBlock.Type, type);
        var after = BlockRender.Mono(reverb, new float[4_800], 64);
        Assert.True(Analysis.Peak(after) < 1e-6, $"stale tail: {Analysis.Peak(after):E2}");
    }

    [Fact]
    public void Damping_shortens_the_high_frequency_decay() {
        using var bright = Create((ReverbBlock.Damping, 20_000f), (ReverbBlock.Mix, 100f));
        using var dark = Create((ReverbBlock.Damping, 2_000f), (ReverbBlock.Mix, 100f));
        var impulse = Signals.Impulse(48_000);

        var brightTail = BlockRender.Mono(bright, impulse, 64)[24_000..];
        var darkTail = BlockRender.Mono(dark, impulse, 64)[24_000..];

        var hf = Analysis.MagnitudeAt(brightTail, 8_000, 48_000);
        var hfDark = Analysis.MagnitudeAt(darkTail, 8_000, 48_000);
        Assert.True(hfDark < hf * 0.1, $"8 kHz tail: bright {hf:E2}, dark {hfDark:E2}");
    }

    [Fact]
    public void Left_and_right_are_decorrelated() {
        using var reverb = Create((ReverbBlock.Mix, 100f));

        var (left, right) = BlockRender.Stereo(reverb, Signals.Impulse(48_000), Signals.Impulse(48_000), _ => 64);

        double dot = 0, ll = 0, rr = 0;
        for (var i = 2_400; i < left.Length; i++) {
            dot += left[i] * right[i];
            ll += left[i] * left[i];
            rr += right[i] * right[i];
        }

        var correlation = dot / Math.Sqrt(ll * rr);
        Assert.True(Math.Abs(correlation) < 0.3, $"correlation {correlation:0.00}");
    }

    [Fact]
    public void Pre_delay_holds_back_the_wet_signal_and_zero_mix_is_dry() {
        using var delayed = Create((ReverbBlock.PreDelay, 50f), (ReverbBlock.Mix, 100f));
        using var dry = Create((ReverbBlock.Mix, 0f));
        var input = Signals.Noise(9_600, 1);

        var wet = BlockRender.Mono(delayed, Signals.Impulse(9_600), 64);
        var unchanged = BlockRender.Mono(dry, input, 64);

        Assert.True(Analysis.Peak(wet.AsSpan(0, 2_400)) < 1e-6, "nothing before the 50 ms pre-delay");
        Assert.True(Analysis.Peak(wet.AsSpan(2_400)) > 1e-4, "reverb after the pre-delay");
        Assert.Equal(input, unchanged);
    }

    /// <summary>When the response's energy in a narrow band around <paramref name="frequency" /> peaks, in ms.</summary>
    static double ArrivalMs(float[] response, double frequency) {
        var band = response.ToArray();
        var c = SvfCoefficients.Design(BiquadType.BandPass, 48_000, frequency, 4);
        var state = new SvfState();
        state.Process(c, band);
        var envelope = new double[band.Length];
        double smooth = 0;
        for (var i = 0; i < band.Length; i++) {
            smooth += (band[i] * band[i] - smooth) * 0.02;
            envelope[i] = smooth;
        }

        return Array.IndexOf(envelope, envelope.Max()) / 48.0;
    }

    static ReverbBlock Create(params (int Index, float Value)[] settings) {
        var reverb = new ReverbBlock();
        foreach (var (index, value) in settings) {
            reverb.Params.SetTarget(index, value);
        }

        reverb.Prepare(new(48_000, 64));
        return reverb;
    }

    /// <summary>T60 from the Schroeder backward-integrated energy decay, fitted between -5 and -25 dB (T20 x 3).</summary>
    static double SchroederT60(float[] response, double sampleRate) {
        var energy = new double[response.Length];
        double running = 0;
        for (var i = response.Length - 1; i >= 0; i--) {
            running += (double)response[i] * response[i];
            energy[i] = running;
        }

        var start = Array.FindIndex(energy, e => 10 * Math.Log10(e / energy[0]) <= -5);
        var end = Array.FindIndex(energy, e => 10 * Math.Log10(e / energy[0]) <= -25);
        return 3 * (end - start) / sampleRate;
    }
}
