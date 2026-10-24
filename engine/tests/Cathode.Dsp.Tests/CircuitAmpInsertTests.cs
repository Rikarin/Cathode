using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp's non-model stages (docs/08 §4): input boost, input filter and Definition, output EQ at its three
///     locations, saturation, input dynamics, output compressor, speaker drive, thump and compression, bias tremolo and
///     balance. Each is neutral at its default.
/// </summary>
public class CircuitAmpInsertTests {
    const double Rate = 48_000;
    const double InnerRate = 96_000;

    static float[] Render(float[] input, params (int Index, float Value)[] settings) {
        using var block = TestBlocks.Create<CircuitAmpBlock>([(CircuitAmpBlock.Gain, 2f), ..settings]);
        return BlockRender.Stereo(block, input, input, static _ => 64).Left;
    }

    static double SteadyRmsDb(float[] signal) => Analysis.ToDb(Analysis.Rms(signal.AsSpan(signal.Length / 2)));

    [Fact]
    public void Inserts_at_their_defaults_leave_the_amp_untouched() {
        var input = Signals.Sine(220, Rate, 9_600, 0.3f);
        var stock = Render(input);
        var parked = Render(
            input,
            (CircuitAmpBlock.SaturationDrive, 10f),
            (CircuitAmpBlock.BoostDrive, 9f),
            (CircuitAmpBlock.BoostLevel, 12f),
            (CircuitAmpBlock.InputFilterGain, 9f),
            (CircuitAmpBlock.OutputEq1 + 3, 9f),
            (CircuitAmpBlock.OutputEqLocation, CircuitAmpBlock.EqBeforePowerAmp),
            (CircuitAmpBlock.CompressorAmount, 10f),
            (CircuitAmpBlock.CompressorThreshold, -50f),
            (CircuitAmpBlock.TremoloRate, 9f),
            (CircuitAmpBlock.CabResonance, 200f)
        );

        Assert.Equal(stock, parked);
    }

    [Fact]
    public void A_clean_boost_raises_the_level_into_the_amp() {
        var input = Signals.Sine(330, Rate, 19_200, 0.02f);
        var stock = SteadyRmsDb(Render(input));
        var boosted = SteadyRmsDb(Render(input, (CircuitAmpBlock.BoostType, 5f), (CircuitAmpBlock.BoostLevel, 12f)));

        Assert.True(boosted > stock + 3, $"boosted {boosted:0.0} dB against {stock:0.0} dB");
    }

    [Theory]
    [InlineData(CircuitAmpBlock.EqAtOutput)]
    [InlineData(CircuitAmpBlock.EqBeforePowerAmp)]
    [InlineData(CircuitAmpBlock.EqAtInput)]
    public void Output_eq_band_lifts_its_centre_at_every_location(int location) {
        // A quiet 1 kHz sine through the clean amp: the 1k band at +9 dB.
        var input = Signals.Sine(1_000, Rate, 19_200, 0.005f);
        var stock = SteadyRmsDb(Render(input));
        var lifted = SteadyRmsDb(
            Render(
                input,
                (CircuitAmpBlock.OutputEq, 1f),
                (CircuitAmpBlock.OutputEqLocation, location),
                (CircuitAmpBlock.OutputEq1 + 5, 9f)
            )
        );

        Assert.Equal(9.0, lifted - stock, 1.0);
    }

    [Fact]
    public void Output_eq_matches_its_layout_analytically() {
        var eq = new AmpEqualizer();
        float[] gains = [0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        eq.Design(0, gains, InnerRate);
        var response = Signals.Impulse(32_768);
        eq.Process(response);

        var expected = BiquadCoefficients.Design(BiquadType.Peak, InnerRate, 125, GraphicEqualizer.OctaveQ, 6);
        foreach (var f in new[] { 60.0, 125, 300, 1_000 }) {
            Assert.Equal(
                Analysis.ToDb(expected.MagnitudeAt(f, InnerRate)),
                Analysis.ToDb(Analysis.MagnitudeAt(response, f, InnerRate)),
                0.05
            );
        }
    }

    [Theory]
    [InlineData(1, 200.0, 6.0)]
    [InlineData(2, 4_000.0, -6.0)]
    [InlineData(3, 1_000.0, 9.0)]
    public void Input_filter_has_its_design_response(int type, double frequency, double gainDb) {
        var filter = new AmpInputFilter();
        filter.Design(type, frequency, 0.7071, gainDb, 0, InnerRate);
        var response = Signals.Impulse(32_768);
        filter.Process(response);

        var shape = type switch {
            1 => BiquadType.LowShelf,
            2 => BiquadType.HighShelf,
            _ => BiquadType.Peak
        };
        var expected = BiquadCoefficients.Design(shape, InnerRate, frequency, 0.7071, gainDb);
        foreach (var f in new[] { 50.0, 200, 1_000, 4_000, 12_000 }) {
            Assert.Equal(
                Analysis.ToDb(expected.MagnitudeAt(f, InnerRate)),
                Analysis.ToDb(Analysis.MagnitudeAt(response, f, InnerRate)),
                0.05
            );
        }
    }

    [Fact]
    public void Definition_tilts_treble_up_and_bass_down() {
        var filter = new AmpInputFilter();
        filter.Design(0, 1_000, 0.7071, 0, 1, InnerRate);
        var response = Signals.Impulse(32_768);
        filter.Process(response);

        Assert.Equal(-4.0, Analysis.ToDb(Analysis.MagnitudeAt(response, 30, InnerRate)), 0.3);
        Assert.Equal(4.0, Analysis.ToDb(Analysis.MagnitudeAt(response, 15_000, InnerRate)), 0.3);
        Assert.Equal(0.0, Analysis.ToDb(Analysis.MagnitudeAt(response, AmpInputFilter.DefinitionHz, InnerRate)), 0.3);
    }

    [Fact]
    public void Saturation_is_transparent_when_quiet_and_clips_when_driven() {
        const int length = 8_192;
        var drive = (float)Decibels.ToGain(AmpSaturation.MaxDriveDb);

        float[] Shape(float amplitude) {
            var saturation = new AmpSaturation();
            var sine = Signals.CoherentSine(100, length, amplitude);
            for (var i = 0; i < sine.Length; i++) {
                sine[i] = (float)saturation.Tick(sine[i], 1, drive);
            }

            return sine;
        }

        Assert.True(Analysis.ToDb(Analysis.Thd(Shape(0.001f), 100)) < -50, "quiet signals pass");
        Assert.True(Analysis.ToDb(Analysis.Thd(Shape(0.5f), 100)) > -15, "loud signals clip");
    }

    [Fact]
    public void Saturation_switch_adds_harmonics_through_the_amp() {
        var input = Signals.CoherentSine(64, 16_384, 0.3f);
        var stock = Render(input, (CircuitAmpBlock.Gain, 6f));
        var saturated = Render(
            input,
            (CircuitAmpBlock.Gain, 6f),
            (CircuitAmpBlock.Saturation, 1f),
            (CircuitAmpBlock.SaturationDrive, 10f)
        );

        Assert.True(
            Analysis.Thd(saturated.AsSpan(8_192), 32) > 2 * Analysis.Thd(stock.AsSpan(8_192), 32),
            $"THD {Analysis.Thd(saturated.AsSpan(8_192), 32):P1} against {Analysis.Thd(stock.AsSpan(8_192), 32):P1}"
        );
    }

    [Fact]
    public void Input_dynamics_expand_and_compress_around_the_reference() {
        static double Out(float amount, float amplitude) {
            var dynamics = new AmpInputDynamics();
            dynamics.Prepare(InnerRate);
            var sine = Signals.Sine(500, InnerRate, 19_200, amplitude);
            dynamics.Process(sine, amount);
            return Analysis.ToDb(Analysis.Rms(sine.AsSpan(9_600))) - Analysis.ToDb(amplitude / Math.Sqrt(2));
        }

        Assert.True(Out(1f, 0.003f) < -6, "expansion turns quiet playing down");
        Assert.True(Out(1f, 1f) > 3, "and hard playing up");
        Assert.True(Out(-1f, 0.003f) > 6, "compression the reverse");
        Assert.Equal(0.0, Out(0f, 0.003f), 1e-6);
    }

    [Fact]
    public void Output_compressor_follows_its_static_curve_above_threshold() {
        var compressor = new AmpCompressor();
        compressor.Prepare(InnerRate);
        compressor.Reset();
        var sine = Signals.Sine(1_000, InnerRate, 96_000);
        compressor.Process(sine, AmpCompressor.Output, 10f, -20f, 0f, InnerRate);

        var expected = CompressorBlock.StaticCurve(0, -20, 10, 6);
        Assert.Equal(expected, Analysis.ToDb(Analysis.Peak(sine.AsSpan(48_000))), 1.0);

        var quiet = Signals.Sine(1_000, InnerRate, 96_000, 0.01f);
        compressor.Reset();
        compressor.Process(quiet, AmpCompressor.Output, 10f, -20f, 0f, InnerRate);
        Assert.Equal(-40.0, Analysis.ToDb(Analysis.Peak(quiet.AsSpan(48_000))), 0.2);
    }

    [Fact]
    public void Speaker_compression_settles_with_its_time_constant() {
        const float timeMs = 100f;
        const float amount = 10f;
        var speaker = new SpeakerStage();
        var sine = Signals.Sine(1_000, InnerRate, (int)(InnerRate * 1.5));
        speaker.Process(sine, 0, 0, 100, amount, timeMs, InnerRate);

        double GainAt(double seconds) {
            var at = (int)(seconds * InnerRate);
            return Analysis.Peak(sine.AsSpan(at - 96, 96));
        }

        var c = 2 * (Math.Pow(10, 0.06 * amount) - 1);
        Assert.Equal(-0.6 * amount, Analysis.ToDb(GainAt(1.4)), 0.2);
        Assert.Equal(1 / Math.Sqrt(1 + c * 0.5 * (1 - Math.Exp(-1))), GainAt(timeMs / 1_000), 0.03);
    }

    [Fact]
    public void Speaker_drive_flattens_the_low_band_only() {
        double Loss(double frequency) {
            var speaker = new SpeakerStage();
            var sine = Signals.Sine(frequency, InnerRate, 48_000);
            speaker.Process(sine, 10, 0, 100, 0, 300, InnerRate);
            return Analysis.ToDb(Analysis.Rms(sine.AsSpan(24_000)) * Math.Sqrt(2));
        }

        Assert.True(Loss(50) < -6, "a loud low note flattens");
        Assert.True(Loss(4_000) > -0.5, "treble passes");
    }

    [Fact]
    public void Thump_peaks_at_the_cab_resonance() {
        var speaker = new SpeakerStage();
        var response = Signals.Impulse(65_536);
        speaker.Process(response, 0, 10, 120, 0, 300, InnerRate);

        Assert.Equal(12.0, Analysis.ToDb(Analysis.MagnitudeAt(response, 120, InnerRate)), 0.2);
        Assert.True(Math.Abs(Analysis.ToDb(Analysis.MagnitudeAt(response, 2_000, InnerRate))) < 0.5);
    }

    [Fact]
    public void Bias_tremolo_modulates_at_its_rate() {
        const double tremolo = 6;
        var input = Signals.Sine(800, Rate, 96_000, 0.3f);
        var output = Render(
            input,
            (CircuitAmpBlock.Gain, 6f),
            (CircuitAmpBlock.TremoloDepth, 100f),
            (CircuitAmpBlock.TremoloRate, (float)tremolo)
        );

        // Envelope in 5 ms windows over the second half, then its spectrum at the rate and off it.
        const int window = 240;
        var envelope = Enumerable.Range(0, 48_000 / window)
            .Select(w => Analysis.Rms(output.AsSpan(48_000 + w * window, window)))
            .ToArray();
        var mean = envelope.Average();

        double Component(double hz) {
            double re = 0, im = 0;
            for (var n = 0; n < envelope.Length; n++) {
                var phase = 2 * Math.PI * hz * n * window / Rate;
                re += (envelope[n] - mean) * Math.Cos(phase);
                im += (envelope[n] - mean) * Math.Sin(phase);
            }

            return Math.Sqrt(re * re + im * im) / envelope.Length;
        }

        Assert.True(Component(tremolo) > 0.05 * mean, $"depth {Component(tremolo) / mean:P1}");
        Assert.True(Component(tremolo) > 5 * Component(tremolo * 1.7), "at its rate");
    }

    [Fact]
    public void Balance_turns_the_far_side_down() {
        var input = Signals.Sine(220, Rate, 4_800, 0.2f);
        using var block = TestBlocks.Create<CircuitAmpBlock>((CircuitAmpBlock.Balance, 100f));
        var (left, right) = BlockRender.Stereo(block, input, input, static _ => 64);

        Assert.All(left, static s => Assert.Equal(0f, s));
        Assert.True(Analysis.Rms(right) > 0);
    }
}
