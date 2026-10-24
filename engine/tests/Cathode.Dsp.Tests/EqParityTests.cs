using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Blocks Guide parity features of the PEQ, Enhancer and IR Player (docs/08 §6.7, §6.13, §6.20).</summary>
public sealed class EqParityTests : IDisposable {
    const double Rate = 48_000;

    readonly string folder = Directory.CreateTempSubdirectory("cathode-irp-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Theory]
    [InlineData(true, 9f)]
    [InlineData(true, -9f)]
    [InlineData(false, 9f)]
    public void Shelving_2_is_a_first_order_shelf_centred_on_its_frequency(bool low, float gain) {
        var band = low ? 1 : 5;
        using var peq = TestBlocks.Create<ParametricEqBlock>(
            (low ? ParametricEqBlock.LowType : ParametricEqBlock.HighType, ParametricEqBlock.Shelf2),
            (ParametricEqBlock.Frequency(band), 1_000f),
            (ParametricEqBlock.Gain(band), gain)
        );
        var response = BlockRender.Mono(peq, Signals.Impulse(65_536), 64);

        // Analogue prototype (s + w G) / (s + w), w = f / sqrt(G): half the gain (in dB) at f, all of it far away.
        Assert.Equal(gain / 2, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.2);
        Assert.Equal(gain, Analysis.ToDb(Analysis.MagnitudeAt(response, low ? 10 : 18_000, Rate)), low ? 0.3 : 1.2);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, low ? 18_000 : 10, Rate)), 0.3);
    }

    [Fact]
    public void Peaking_2_narrows_as_it_is_pushed() {
        float Width(float gain) {
            using var peq = TestBlocks.Create<ParametricEqBlock>(
                (ParametricEqBlock.LowType, ParametricEqBlock.Peak2),
                (ParametricEqBlock.Frequency(1), 1_000f),
                (ParametricEqBlock.Gain(1), gain),
                (ParametricEqBlock.Q(1), 2f)
            );
            var response = BlockRender.Mono(peq, Signals.Impulse(65_536), 64);
            Assert.Equal(gain, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.1);
            return (float)(Analysis.ToDb(Analysis.MagnitudeAt(response, 1_500, Rate)) / gain); // share kept half an octave up
        }

        Assert.True(Width(3f) > Width(12f) + 0.1f, "a small boost is broader");
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(3, 24)]
    [InlineData(4, 30)]
    [InlineData(5, 36)]
    public void Blocking_band_slopes_reach_36_dB_per_octave(int slope, int dbPerOctave) {
        using var peq = TestBlocks.Create<ParametricEqBlock>(
            (ParametricEqBlock.LowType, ParametricEqBlock.Blocking),
            (ParametricEqBlock.LowSlope, slope),
            (ParametricEqBlock.Frequency(1), 1_000f)
        );
        var response = BlockRender.Mono(peq, Signals.Impulse(65_536), 64);

        Assert.Equal(-3.01, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.1);

        // A Butterworth high-pass of order n (bilinear): the asymptote is n x 6 dB/oct.
        var order = dbPerOctave / 6;
        foreach (var f in new[] { 250.0, 500 }) {
            var ratio = Math.Tan(Math.PI * 1_000 / Rate) / Math.Tan(Math.PI * f / Rate);
            var expected = -10 * Math.Log10(1 + Math.Pow(ratio, 2 * order));
            Assert.Equal(expected, Analysis.ToDb(Analysis.MagnitudeAt(response, f, Rate)), 0.1);
        }
    }

    [Fact]
    public void Solo_plays_one_band_region_alone() {
        using var peq = TestBlocks.Create<ParametricEqBlock>(
            (ParametricEqBlock.Solo, 3f),
            (ParametricEqBlock.Gain(1), 12f),
            (ParametricEqBlock.Q(3), 4f)
        );
        var response = BlockRender.Mono(peq, Signals.Impulse(65_536), 64);

        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.1);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 100, Rate)) < -25, "band 1's boost is not heard");
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 8_000, Rate)) < -25);
    }

    [Fact]
    public void Steep_cut_filter_matches_butterworth_magnitudes() {
        for (var slope = 0; slope < CutFilter.SteepSlopes.Count; slope++) {
            var order = slope + 1;
            var c = CutFilter.Design(false, Rate, 2_000, slope);
            var state = new CutState();
            var ir = Signals.Impulse(16_384);
            state.Process(c, ir);
            foreach (var f in new[] { 500.0, 2_000, 4_000 }) {
                var expected = -10 * Math.Log10(1 + Math.Pow(Math.Tan(Math.PI * f / Rate) / Math.Tan(Math.PI * 2_000 / Rate), 2 * order));
                Assert.Equal(expected, Analysis.ToDb(Analysis.MagnitudeAt(ir, f, Rate)), 0.05);
            }
        }
    }

    [Theory]
    [InlineData(EnhancerBlock.Modern)]
    [InlineData(EnhancerBlock.Stereoizer)]
    public void Enhancer_types_keep_the_mono_sum(int type) {
        using var block = TestBlocks.Create<EnhancerBlock>(
            (EnhancerBlock.Type, type),
            (EnhancerBlock.Width, 100f),
            (EnhancerBlock.LowCut, 20f),
            (EnhancerBlock.HighCut, 20_000f)
        );
        var input = Signals.Noise(9_600, 3);

        var (left, right) = BlockRender.Stereo(block, input, input, _ => 64);

        for (var i = 0; i < input.Length; i++) {
            Assert.Equal(2 * input[i], left[i] + right[i], 1e-4f);
        }
    }

    [Fact]
    public void Stereoizer_alternates_many_more_bands_than_modern() {
        int Crossings(int type) {
            using var block = TestBlocks.Create<EnhancerBlock>(
                (EnhancerBlock.Type, type),
                (EnhancerBlock.Width, 100f),
                (EnhancerBlock.LowCut, 20f),
                (EnhancerBlock.HighCut, 20_000f)
            );
            var impulse = Signals.Impulse(16_384);
            var (left, right) = BlockRender.Stereo(block, impulse, impulse, _ => 64);
            var count = 0;
            var previous = 0.0;
            for (var f = 100.0; f < 16_000; f *= 1.01) {
                var difference = Analysis.ToDb(Analysis.MagnitudeAt(left, f, Rate))
                    - Analysis.ToDb(Analysis.MagnitudeAt(right, f, Rate));
                if (difference * previous < 0) {
                    count++;
                }

                previous = difference;
            }

            return count;
        }

        Assert.True(Crossings(EnhancerBlock.Stereoizer) > 2 * Crossings(EnhancerBlock.Modern));
    }

    [Fact]
    public void Enhancer_depth_at_half_is_the_original_modern_voicing() =>
        Assert.Equal(0.6, EnhancerBlock.AllpassQFor(0.5f), 1e-12);

    [Fact]
    public void Ir_player_one_slot_plays_its_ir_exactly() {
        var ir = Signals.Noise(2_000, 4, 0.3f);
        using var block = Create(("ir", Write("a.wav", ir)));

        var output = BlockRender.Mono(block, Signals.Impulse(4_000), 64);

        for (var i = 0; i < ir.Length; i++) {
            Assert.Equal(ir[i], output[i], 1e-5f);
        }
    }

    [Fact]
    public void Ir_player_parallel_sums_two_slots_with_their_levels_and_pans() {
        var a = Signals.Impulse(200, 0.5f, 10);
        var b = Signals.Impulse(200, 0.25f, 50);
        using var block = Create(
            ("ir", Write("a.wav", a)),
            ("ir2", Write("b.wav", b)),
            (IrPlayerBlock.SlotParam(1, IrPlayerBlock.SlotLevel), -6.0206f),
            (IrPlayerBlock.SlotParam(1, IrPlayerBlock.Pan), 100f)
        );

        var (left, right) = BlockRender.Stereo(block, Signals.Impulse(400), Signals.Impulse(400), _ => 64);

        Assert.Equal(0.5f, left[10], 1e-4f);
        Assert.Equal(0f, left[50], 1e-4f); // slot 2 is panned right
        Assert.Equal(0.5f, right[10], 1e-4f);
        Assert.Equal(0.125f, right[50], 1e-4f);
    }

    [Fact]
    public void Ir_player_stereo_mode_puts_ir_1_left_and_ir_2_right() {
        using var block = Create(
            ("ir", Write("a.wav", Signals.Impulse(200, 0.5f, 10))),
            ("ir2", Write("b.wav", Signals.Impulse(200, 0.25f, 50))),
            (IrPlayerBlock.Mode, IrPlayerBlock.StereoMode)
        );

        var (left, right) = BlockRender.Stereo(block, Signals.Impulse(400), Signals.Impulse(400), _ => 64);

        Assert.Equal(0.5f, left[10], 1e-4f);
        Assert.Equal(0f, left[50], 1e-4f);
        Assert.Equal(0f, right[10], 1e-4f);
        Assert.Equal(0.25f, right[50], 1e-4f);
    }

    [Fact]
    public void Ir_player_series_convolves_the_slots() {
        using var block = Create(
            ("ir", Write("a.wav", Signals.Impulse(200, 0.5f, 10))),
            ("ir2", Write("b.wav", Signals.Impulse(200, 0.25f, 50))),
            (IrPlayerBlock.Mode, IrPlayerBlock.Series)
        );

        var output = BlockRender.Mono(block, Signals.Impulse(400), 64);

        Assert.Equal(0.125f, output[60], 1e-4f);
        Assert.True(Math.Abs(output[10]) < 1e-4f && Math.Abs(output[50]) < 1e-4f);
    }

    [Fact]
    public void Ir_player_length_cuts_the_ir() {
        var ir = Signals.Step(4_800, 0.01f);
        using var block = Create(("ir", Write("a.wav", ir)), (IrPlayerBlock.SlotParam(0, IrPlayerBlock.Length), 50f));

        var output = BlockRender.Mono(block, Signals.Impulse(9_600), 64);

        Assert.Equal(0.01f, output[1_000], 1e-5f);
        Assert.Equal(0f, output[2_400], 1e-6f); // 50 ms is 2 400 samples
    }

    [Fact]
    public void Ir_player_input_select_feeds_one_side() {
        using var block = Create(("ir", Write("a.wav", Signals.Impulse(16, 1f))), (IrPlayerBlock.Input, IrPlayerBlock.InputLeft));
        var left = Signals.Impulse(100);
        var right = new float[100];

        var (outLeft, outRight) = BlockRender.Stereo(block, left, right, _ => 64);

        Assert.Equal(1f, outLeft[0], 1e-5f);
        Assert.Equal(1f, outRight[0], 1e-5f);
    }

    [Fact]
    public void Ir_player_changes_crossfade_in_after_a_refresh() {
        using var block = Create(("ir", Write("a.wav", Signals.Impulse(16, 1f))));
        block.Params.SetTarget(IrPlayerBlock.SlotParam(0, IrPlayerBlock.SlotLevel), -6f);
        block.Refresh();
        BlockRender.Mono(block, new float[4_800], 64);

        Assert.Equal(1, block.IrSwaps);
    }

    IrPlayerBlock Create(params object[] settings) {
        var block = new IrPlayerBlock();
        foreach (var setting in settings) {
            switch (setting) {
                case (string slot, string path):
                    block.LoadAsset(slot, path);
                    break;
                case (int index, float value):
                    block.Params.SetTarget(index, value);
                    break;
                case (int index, int value):
                    block.Params.SetTarget(index, value);
                    break;
            }
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    string Write(string name, float[] ir) {
        var path = Path.Combine(folder, name);
        WavFile.Write(path, new(48_000, [ir]));
        return path;
    }
}
