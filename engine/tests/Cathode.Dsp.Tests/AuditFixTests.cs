using Cathode.Dsp.Amp;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The findings of the DSP audit of 1.0.0-rc.6 (#559), each held where it was fixed: the cases are the ones the
///     audit gave, so a return of any of them fails here rather than in a render nobody is looking at.
/// </summary>
public class AuditFixTests {
    /// <summary>#543: the end fade starts inside the result, however short it is.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(200)]
    public void An_ir_mix_shorter_than_the_fade_fades_over_what_there_is(int length) {
        var input = new IrMixInput(Signals.Noise(500, 3, 0.5f), 48_000);

        var mixed = IrMix.Mix([input], 48_000, length, false, false);

        Assert.Equal(length, mixed.Length);
        Assert.All(mixed, static v => Assert.True(float.IsFinite(v)));
        Assert.True(Math.Abs(mixed[^1]) < Math.Abs(input.Samples[length - 1]) + 1e-6f, "the last sample is faded");
    }

    /// <summary>#548: a silent IR normalises to silence, not to NaN that the cache then hands out.</summary>
    [Fact]
    public void A_silent_ir_with_unit_energy_stays_silent() {
        var file = new ImpulseResponseFile("silent.wav", 0, DateTime.UnixEpoch, new(48_000, [new float[512]]));

        var prepared = file.Prepare(48_000, new(512));

        Assert.All(prepared[0], static v => Assert.Equal(0f, v));
    }

    /// <summary>#548: the truncation fade starts inside the IR, however short the length it is cut to.</summary>
    [Fact]
    public void An_ir_cut_shorter_than_the_fade_stays_finite() {
        var samples = Signals.Noise(512, 5, 0.5f);
        var file = new ImpulseResponseFile("short.wav", 0, DateTime.UnixEpoch, new(48_000, [samples]));

        var prepared = file.Prepare(48_000, new(20, false));

        Assert.Equal(20, prepared[0].Length);
        Assert.All(prepared[0], static v => Assert.True(float.IsFinite(v)));
    }

    /// <summary>
    ///     #544: a change of hardness takes the difference of one curve, not of two. Held near a level, where the
    ///     step between samples is small enough for a difference of two antiderivatives to be divided into a spike.
    /// </summary>
    [Fact]
    public void A_clip_shape_change_does_not_spike_the_adaa_clipper() {
        var clipper = new AdaaClipper();
        clipper.Reset();
        for (var i = 0; i < 64; i++) {
            clipper.Process(ClipperShape.Soft, 3f + 1e-5f * i, 0f, true);
        }

        var moved = 0f;
        for (var i = 0; i < 16; i++) {
            moved = clipper.Process(ClipperShape.Soft, 3f + 1e-5f * (64 + i), 0.5f * (i + 1) / 16f, true);
            Assert.True(Math.Abs(moved) < 1.5f, $"{moved} out of the curve's range at step {i}");
        }

        Assert.True(Math.Abs(moved) < 1.5f);
    }

    /// <summary>#550: the pan moves across the block instead of stepping at its edge.</summary>
    [Fact]
    public void A_pan_change_is_interpolated_over_the_block() {
        var (left, right) = (new float[8], new float[8]);
        Array.Fill(left, 1f);
        var io = new StereoBuffer(left, right);

        // From hard left (the pair untouched) to centre: the left input arrives in both sides, a step at a time.
        Stereo.PanRamp(io, -1f, 1f, 0f, 0f);

        Assert.True(right[0] > 0 && right[0] < right[^1], "the right side comes up over the block");
        for (var i = 1; i < right.Length; i++) {
            Assert.True(right[i] > right[i - 1], $"the ramp steps back at {i}");
        }

        Assert.Equal(1f, right[^1], 3);
    }

    /// <summary>
    ///     #540: a model whose presence pot is the inverter tail's shunt counts it in the tail, which
    ///     <see cref="LongTailPairDesign" /> documents as the whole DC resistance from the bias junction to ground.
    /// </summary>
    [Fact]
    public void A_presence_pot_wired_as_the_tail_shunt_is_in_the_tail() {
        var models = AmpDesign.All
            .Where(static a => a.Power.Feedback.PresenceIsShunt && a.Power.PhaseInverter is LongTailPairDesign)
            .ToArray();

        Assert.NotEmpty(models);
        foreach (var design in models) {
            var inverter = (LongTailPairDesign)design.Power.PhaseInverter;
            Assert.True(
                inverter.TailResistor > design.Power.Feedback.ShuntResistor,
                $"{design.Name}: a {inverter.TailResistor} Ω tail over a {design.Power.Feedback.ShuntResistor} Ω shunt"
            );
        }
    }
}
