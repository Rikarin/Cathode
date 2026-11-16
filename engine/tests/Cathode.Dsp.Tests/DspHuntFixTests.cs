using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Spectrum;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The findings of the DSP bug hunts at edd1d039 (#625), each held where it was fixed, with the cases the reports
///     gave.
/// </summary>
public class DspHuntFixTests {
    const double Rate = 48_000;

    /// <summary>
    ///     #612: after a reset, a small sample through the Asymmetric curve comes out small, not near full scale.
    /// </summary>
    [Fact]
    public void The_asymmetric_clipper_has_no_spike_after_a_reset() {
        var clipper = new AdaaClipper();
        clipper.Process(ClipperShape.Asymmetric, 0.7f);
        clipper.Reset();

        var y = clipper.Process(ClipperShape.Asymmetric, 0.01f);

        Assert.InRange(y, -0.05f, 0.05f);
    }

    /// <summary>#614: a comb type switched back to does not play what its lines held from the last time.</summary>
    [Fact]
    public void A_comb_type_switched_back_to_starts_silent() {
        using var block = TestBlocks.Create<FilterBlock>(
            (FilterBlock.Type, FilterBlock.FeedbackComb),
            (FilterBlock.Depth, 90f)
        );
        BlockRender.Mono(block, Signals.Noise(4_800, 1, 0.5f), 64);
        block.Params.SetTarget(FilterBlock.Type, 0);
        BlockRender.Mono(block, new float[4_800], 64);
        block.Params.SetTarget(FilterBlock.Type, FilterBlock.FeedbackComb);

        var output = BlockRender.Mono(block, new float[4_800], 64);

        Assert.All(output, static x => Assert.Equal(0f, x));
    }

    /// <summary>#615: with the only modelled slot muted, the Align page reports finite delays.</summary>
    [Fact]
    public void The_align_page_reports_finite_delays_with_no_audible_modelled_slot() {
        using var block = TestBlocks.Create<CabBlock>(
            (CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceDynaCab),
            (CabBlock.SlotParam(0, CabBlock.Mute), 1f),
            (CabBlock.SlotParam(0, CabBlock.AutoAlign), 0f)
        );

        var slots = block.Alignment();

        Assert.All(slots, static s => Assert.True(double.IsFinite(s.AutoDelayMs), $"slot {s.Slot}: {s.AutoDelayMs}"));
    }

    /// <summary>
    ///     #617: on a Brilliance design, a sample-rate change keeps the cut the knobs set: the power amp renders as one
    ///     configured at that rate from the start.
    /// </summary>
    [Fact]
    public void A_brilliance_power_amp_keeps_its_cut_through_a_sample_rate_change() {
        var knobs = new PowerControls(0.9, 0.3, 0.4, 0.5, 0.5, 0.5, 0.9);
        var moved = new PowerAmp(TinyTerror.Design.Power, 96_000);
        moved.Configure(knobs);
        moved.SetSampleRate(Rate);
        var fresh = new PowerAmp(TinyTerror.Design.Power, Rate);
        fresh.Configure(knobs);
        moved.Reset();
        fresh.Reset();

        for (var n = 0; n < 4_800; n++) {
            var x = 2 * Math.Sin(2 * Math.PI * 1_000 * n / Rate);
            Assert.Equal(fresh.Tick(x), moved.Tick(x));
        }
    }

    /// <summary>#618: the RC low-pass is -3 dB at the corner asked for, high ones included.</summary>
    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(20_000)]
    public void The_rc_low_pass_is_3_db_down_at_its_corner(double corner) {
        var filter = new RcLowPass();
        filter.SetCutoff(corner, Rate);
        double inputPower = 0, outputPower = 0; // power, not peaks: 20 kHz has 2.4 samples a cycle
        for (var n = 0; n < 96_000; n++) {
            var x = Math.Sin(2 * Math.PI * corner * n / Rate);
            var y = filter.Tick(x);
            if (n >= 48_000) {
                inputPower += x * x;
                outputPower += y * y;
            }
        }

        Assert.Equal(-3.01, 10 * Math.Log10(outputPower / inputPower), 0.05);
    }

    /// <summary>
    ///     #623: Stop part-way through the closing crossfade gives the loop start back as recorded rather than leaving
    ///     a step where the blend stopped.
    /// </summary>
    [Fact]
    public void Stop_during_the_closing_crossfade_leaves_the_loop_start_as_recorded() {
        using var looper = new Looper(Rate, 2);
        looper.Configure(LooperSettings.Default with { CrossfadeSamples = 480 });
        looper.Apply(LooperAction.Record);
        for (var n = 0; n < 4_800; n++) {
            looper.Tick(0.5f, 0.5f); // the recording: a steady 0.5
        }

        looper.Apply(LooperAction.Play);
        for (var n = 0; n < 100; n++) {
            looper.Tick(-0.5f, -0.5f); // the playing after it, unlike the loop
        }

        looper.Apply(LooperAction.Stop);
        for (var n = 0; n < 4_800; n++) {
            looper.Tick(0f, 0f);
        }

        looper.Apply(LooperAction.Play);
        var played = new float[1_000];
        for (var n = 0; n < played.Length; n++) {
            played[n] = looper.Tick(0f, 0f).Left;
        }

        // Past the 2 ms fade-in (96 samples), the loop start is the steady recording; the blend had reached about
        // sample 200 when the stop fade ended, so a half-done one shows from here to there.
        Assert.All(played[100..], static x => Assert.Equal(0.5f, x, 1e-4f));
    }

    /// <summary>#624: fewer than 64 taps fade over what there is instead of throwing.</summary>
    [Fact]
    public void A_minimum_phase_filter_of_fewer_than_64_taps_is_designed() {
        var filter = MinimumPhase.Design(new double[257], 32);

        Assert.Equal(32, filter.Length);
        Assert.All(filter, static x => Assert.True(float.IsFinite(x)));
    }
}
