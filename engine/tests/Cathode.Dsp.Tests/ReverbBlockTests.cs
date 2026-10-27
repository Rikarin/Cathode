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
    [InlineData(5f, 2f)]
    [InlineData(6f, 4f)]
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
        var (left, right) = BlockRender.Stereo(
            reverb,
            Signals.Impulse(48_000),
            Signals.Impulse(48_000),
            static _ => 64
        );

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
    public void Spring_damping_shortens_the_high_frequency_decay_but_not_the_first_chirp() {
        using var bright = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.Mix, 100f)
        );
        using var dark = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.Damping, 2_000f),
            (ReverbBlock.Mix, 100f)
        );
        var impulse = Signals.Impulse(48_000);

        var brightOut = BlockRender.Mono(bright, impulse, 64);
        var darkOut = BlockRender.Mono(dark, impulse, 64);

        // The first pass through the springs (the shortest loop is about 50 ms with the cascade) is not damped.
        Assert.Equal(brightOut[..1_200], darkOut[..1_200]);
        var hf = Analysis.MagnitudeAt(brightOut.AsSpan(24_000), 3_000, 48_000);
        var hfDark = Analysis.MagnitudeAt(darkOut.AsSpan(24_000), 3_000, 48_000);
        var lf = Analysis.MagnitudeAt(brightOut.AsSpan(24_000), 300, 48_000);
        var lfDark = Analysis.MagnitudeAt(darkOut.AsSpan(24_000), 300, 48_000);
        Assert.True(hfDark < hf * 0.1, $"3 kHz tail: bright {hf:E2}, dark {hfDark:E2}");
        Assert.True(lfDark > lf * 0.5, $"300 Hz tail: bright {lf:E2}, dark {lfDark:E2}");
    }

    [Fact]
    public void Left_and_right_are_decorrelated() {
        using var reverb = Create((ReverbBlock.Mix, 100f));

        var (left, right) = BlockRender.Stereo(
            reverb,
            Signals.Impulse(48_000),
            Signals.Impulse(48_000),
            static _ => 64
        );

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

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_quality_keeps_the_decay_time_and_the_level(int quality) {
        (int, float)[] settings = [
            (ReverbBlock.Decay, 2f), (ReverbBlock.Damping, 20_000f), (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        ];
        using var normal = Create(settings);
        using var other = Create([..settings, (ReverbBlock.Quality, quality)]);
        var noise = Signals.Concat(Signals.Noise(96_000, 3, 0.5f), new float[48_000 * 2]);

        var reference = BlockRender.Mono(normal, noise, 64);
        var output = BlockRender.Mono(other, noise, 64);

        var levelDb = Analysis.ToDb(
            Analysis.Rms(output.AsSpan(48_000, 48_000)) / Analysis.Rms(reference.AsSpan(48_000, 48_000))
        );
        Assert.InRange(levelDb, -2, 2);
        Assert.InRange(SchroederT60(output[96_000..], 48_000), 2 * 0.8, 2 * 1.2);
    }

    [Fact]
    public void Low_and_high_freq_times_scale_the_decay_below_and_above_the_crossovers() {
        using var reverb = Create(
            (ReverbBlock.Decay, 1f),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.LowCrossover, 250f),
            (ReverbBlock.LowTime, 250f),
            (ReverbBlock.HighCrossover, 3_000f),
            (ReverbBlock.HighTime, 40f),
            (ReverbBlock.Mix, 100f)
        );

        var response = BlockRender.Mono(reverb, Signals.Impulse(48_000 * 4), 64);

        var low = SchroederT60(Band(response, 80), 48_000);
        var mid = SchroederT60(Band(response, 1_000), 48_000);
        var high = SchroederT60(Band(response, 10_000), 48_000);
        Assert.InRange(mid, 0.75, 1.3);
        Assert.InRange(low / mid, 1.7, 3);
        Assert.InRange(high / mid, 0.3, 0.6);
    }

    [Fact]
    public void Early_reflections_land_at_their_tap_times_with_their_sides() {
        // Size 50 % and Early Time 100 % place the taps at their table times; no decay, diffusion, tank or cuts.
        using var reverb = Create(
            (ReverbBlock.Type, ReverbBlock.Room),
            (ReverbBlock.EarlyLevel, 100f),
            (ReverbBlock.LateLevel, 0f),
            (ReverbBlock.EarlyDiffusion, 0f),
            (ReverbBlock.EarlyDecay, 0f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        );

        var (left, right) = BlockRender.Stereo(reverb, Signals.Impulse(4_800), Signals.Impulse(4_800), static _ => 64);

        var gain = 1 / Math.Sqrt(12);
        var total = 0.0;
        for (var k = 0; k < 12; k++) {
            var index = (int)Math.Floor(ReverbBlock.EarlyMs[0][k] * 48.0);
            var near = Math.Abs(left[index]) + Math.Abs(left[index + 1]);
            var far = Math.Abs(right[index]) + Math.Abs(right[index + 1]);
            var (expectedLeft, expectedRight) = k % 2 == 0 ? (gain, 0.35 * gain) : (0.35 * gain, gain);
            Assert.Equal(expectedLeft, near, 1e-4);
            Assert.Equal(expectedRight, far, 1e-4);
            total += near;
        }

        Assert.Equal(total, left.Sum(static s => Math.Abs((double)s)), 1e-4); // nothing but the taps
    }

    [Fact]
    public void Low_mid_band_boosts_its_frequency_only() {
        (int, float)[] settings =
            [(ReverbBlock.Decay, 0.3f), (ReverbBlock.LowCut, ReverbBlock.LowCutOff), (ReverbBlock.Mix, 100f)];
        using var flat = Create(settings);
        using var boosted = Create([..settings, (ReverbBlock.LowMidFrequency, 400f), (ReverbBlock.LowMidGain, 12f)]);

        var reference = BlockRender.Mono(flat, Signals.Impulse(48_000), 64);
        var output = BlockRender.Mono(boosted, Signals.Impulse(48_000), 64);

        var at400 = Analysis.MagnitudeAt(output, 400, 48_000) / Analysis.MagnitudeAt(reference, 400, 48_000);
        var at6K = Analysis.MagnitudeAt(output, 6_000, 48_000) / Analysis.MagnitudeAt(reference, 6_000, 48_000);
        Assert.InRange(Analysis.ToDb(at400), 11, 13);
        Assert.InRange(Analysis.ToDb(at6K), -0.5, 0.5);
    }

    [Fact]
    public void Pre_delay_feedback_repeats_the_input_into_the_tank_every_pre_delay() {
        // Without modulation the tank is linear and time-invariant, so feedback g at delay d adds g^k copies k·d later.
        (int, float)[] settings = [(ReverbBlock.PreDelay, 100f), (ReverbBlock.ModDepth, 0f), (ReverbBlock.Mix, 100f)];
        using var plain = Create(settings);
        using var echoing = Create([..settings, (ReverbBlock.PreDelayFeedback, 50f)]);
        const int Length = 48_000;
        const int Spacing = 4_800;

        var reference = BlockRender.Mono(plain, Signals.Impulse(Length), 64);
        var output = BlockRender.Mono(echoing, Signals.Impulse(Length), 64);

        var expected = new float[Length];
        for (var k = 0; k * Spacing < Length; k++) {
            for (var i = k * Spacing; i < Length; i++) {
                expected[i] += MathF.Pow(0.5f, k) * reference[i - k * Spacing];
            }
        }

        var residual = Analysis.NullResidualDb(expected, output);
        Assert.True(residual < -80, $"residual {residual:0.0} dB");
    }

    [Fact]
    public void Stack_keeps_the_input_open_while_hold_closes_it() {
        using var held = Create(
            (ReverbBlock.Decay, 0.5f),
            (ReverbBlock.Hold, ReverbBlock.HoldOn),
            (ReverbBlock.Mix, 100f)
        );
        using var stacked = Create(
            (ReverbBlock.Decay, 0.5f),
            (ReverbBlock.Hold, ReverbBlock.Stack),
            (ReverbBlock.Mix, 100f)
        );
        var input = Signals.Concat(Signals.Noise(24_000, 9, 0.5f), new float[96_000]);

        var closed = BlockRender.Mono(held, input, 64);
        var open = BlockRender.Mono(stacked, input, 64);

        Assert.All(closed, static s => Assert.Equal(0f, s));
        Assert.True(Analysis.ToDb(Analysis.Rms(open.AsSpan(110_000))) > -40, "the stacked tail holds");
        Assert.True(Analysis.Peak(open) < 4, "the stacked tail stays bounded");
    }

    [Fact]
    public void Mic_spacing_delays_the_right_side_only() {
        using var plain = Create((ReverbBlock.Mix, 100f));
        using var spaced = Create((ReverbBlock.Mix, 100f), (ReverbBlock.MicSpacing, 10f));

        var (left, right) = BlockRender.Stereo(plain, Signals.Impulse(9_600), Signals.Impulse(9_600), static _ => 64);
        var (left2, right2) = BlockRender.Stereo(
            spaced,
            Signals.Impulse(9_600),
            Signals.Impulse(9_600),
            static _ => 64
        );

        Assert.Equal(left, left2);
        for (var i = 480; i < right.Length; i++) {
            Assert.Equal(right[i - 480], right2[i], 1e-6f);
        }
    }

    [Fact]
    public void Wall_diffusion_keeps_the_decay_time() {
        using var reverb = Create(
            (ReverbBlock.Decay, 2f),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.WallDiffusion, 100f),
            (ReverbBlock.EchoDensity, 100f),
            (ReverbBlock.Mix, 100f)
        );

        var response = BlockRender.Mono(reverb, Signals.Impulse(48_000 * 3), 64);

        Assert.InRange(SchroederT60(response, 48_000), 2 * 0.8, 2 * 1.2);
    }

    [Theory]
    [InlineData(ReverbBlock.PitchAtOutput, 0f)]
    [InlineData(ReverbBlock.PitchAtInput, 0f)]
    [InlineData(ReverbBlock.PitchAtOutput, 1f)]
    public void Pitch_section_shifts_the_wet_an_octave_up(int position, float reverse) {
        using var reverb = Create(
            (ReverbBlock.Decay, 0.5f),
            (ReverbBlock.PitchMix, 100f),
            (ReverbBlock.VoiceBalance, 0f),
            (ReverbBlock.Shift1, 12f),
            (ReverbBlock.PitchPosition, position),
            (ReverbBlock.PitchDirection, reverse),
            (ReverbBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(reverb, Signals.Sine(220, 48_000, 48_000, 0.5f), 64)[24_000..];

        var octave = Analysis.MagnitudeAt(output, 440, 48_000);
        var fundamental = Analysis.MagnitudeAt(output, 220, 48_000);
        Assert.True(octave > (reverse > 0 ? 1.5 : 10) * fundamental, $"440 Hz {octave:E2}, 220 Hz {fundamental:E2}");
    }

    [Fact]
    public void Shimmer_in_the_tank_climbs_an_octave_on_every_pass() {
        using var reverb = Create(
            (ReverbBlock.Decay, 3f),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.PitchMix, 0f),
            (ReverbBlock.PitchFeedback, 80f),
            (ReverbBlock.VoiceBalance, 0f),
            (ReverbBlock.Shift1, 12f),
            (ReverbBlock.PitchPosition, ReverbBlock.PitchInTank),
            (ReverbBlock.Mix, 100f)
        );
        using var plain = Create((ReverbBlock.Decay, 3f), (ReverbBlock.Damping, 20_000f), (ReverbBlock.Mix, 100f));
        var input = Signals.Concat(Signals.Sine(220, 48_000, 24_000, 0.5f), new float[48_000]);

        var tail = BlockRender.Mono(reverb, input, 64)[36_000..];
        var reference = BlockRender.Mono(plain, input, 64)[36_000..];

        Assert.True(Analysis.MagnitudeAt(tail, 880, 48_000) > 10 * Analysis.MagnitudeAt(reference, 880, 48_000));
        Assert.True(Analysis.Peak(tail) < 4);
    }

    [Fact]
    public void One_spring_is_mono_and_tone_darkens_the_springs() {
        using var one = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.Springs, 1f),
            (ReverbBlock.Mix, 100f)
        );
        using var dark = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.SpringTone, 0f),
            (ReverbBlock.Mix, 100f)
        );
        using var bright = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.SpringTone, 100f),
            (ReverbBlock.Mix, 100f)
        );

        var (left, right) = BlockRender.Stereo(one, Signals.Impulse(9_600), Signals.Impulse(9_600), static _ => 64);
        var darkOut = BlockRender.Mono(dark, Signals.Impulse(9_600), 64);
        var brightOut = BlockRender.Mono(bright, Signals.Impulse(9_600), 64);

        Assert.Equal(left, right);
        var darkHf = Analysis.MagnitudeAt(darkOut, 6_000, 48_000);
        var brightHf = Analysis.MagnitudeAt(brightOut, 6_000, 48_000);
        Assert.True(darkHf < 0.3 * brightHf, $"6 kHz: dark {darkHf:E2}, bright {brightHf:E2}");
    }

    [Fact]
    public void Boing_drives_the_springs_harder_on_attacks_only() {
        using var plain = Create((ReverbBlock.Type, ReverbBlock.Spring), (ReverbBlock.Mix, 100f));
        using var boing = Create(
            (ReverbBlock.Type, ReverbBlock.Spring),
            (ReverbBlock.Boing, 100f),
            (ReverbBlock.Mix, 100f)
        );
        var pluck = Signals.Concat(new float[4_800], Signals.Sine(196, 48_000, 2_400, 0.3f), new float[4_800]);
        var steady = Signals.Sine(196, 48_000, 96_000, 0.3f);

        var attackGain = Analysis.Rms(BlockRender.Mono(boing, pluck, 64))
            / Analysis.Rms(BlockRender.Mono(plain, pluck, 64));
        boing.Reset();
        plain.Reset();
        var sustainGain = Analysis.Rms(BlockRender.Mono(boing, steady, 64).AsSpan(72_000))
            / Analysis.Rms(BlockRender.Mono(plain, steady, 64).AsSpan(72_000));

        Assert.True(attackGain > 1.5, $"attack gain {attackGain:0.00}");
        Assert.InRange(sustainGain, 0.9, 1.15);
    }

    static float[] Band(float[] response, double frequency) {
        var band = response.ToArray();
        var c = SvfCoefficients.Design(BiquadType.BandPass, 48_000, frequency, 2);
        var state = new SvfState();
        state.Process(c, band);
        return band;
    }

    /// <summary>
    ///     When the response's energy in a narrow band around <paramref name="frequency" /> peaks, in ms.
    /// </summary>
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

    /// <summary>
    ///     T60 from the Schroeder backward-integrated energy decay, fitted between -5 and -25 dB (T20 x 3).
    /// </summary>
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
