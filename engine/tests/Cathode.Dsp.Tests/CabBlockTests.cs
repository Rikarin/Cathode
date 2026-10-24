using Cathode.Dsp.Blocks;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Cab block's IR source, how its slots mix, and what it does with nothing to play.</summary>
public sealed class CabBlockTests : IDisposable {
    static readonly ProcessSpec Spec = new(48_000, 64);
    readonly string folder = Directory.CreateTempSubdirectory("cathode-cab-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void Default_is_slot_one_dynacab_and_the_rest_muted() {
        var info = CabBlock.Info;

        Assert.Equal(CabBlock.SourceDynaCab, info.Params[CabBlock.SlotParam(0, CabBlock.Source)].Default);
        Assert.Equal(0f, info.Params[CabBlock.SlotParam(0, CabBlock.Mute)].Default);
        for (var s = 1; s < CabBlock.Slots; s++) {
            Assert.Equal(1f, info.Params[CabBlock.SlotParam(s, CabBlock.Mute)].Default);
        }

        string Choice(int offset) => info.Params[CabBlock.SlotParam(0, offset)].Options![(int)info.Params[CabBlock.SlotParam(0, offset)].Default];
        Assert.Equal("4x12 Closed Back", Choice(CabBlock.Cab));
        Assert.Equal("Celestion Vintage 30", Choice(CabBlock.Speaker));
        Assert.Equal("Dynamic 57 (SM57)", Choice(CabBlock.Mic));
        Assert.Equal(25f, info.Params[CabBlock.SlotParam(0, CabBlock.Position)].Default);
        Assert.Equal(1f, info.Params[CabBlock.SlotParam(0, CabBlock.Distance)].Default);
        Assert.Equal("m1source", info.Params[CabBlock.SlotParam(0, CabBlock.Source)].Id);
        Assert.Equal("m4solo", info.Params[CabBlock.SlotParam(3, CabBlock.Solo)].Id);
        Assert.Equal(CabBlock.SlotBase + CabBlock.Slots * CabBlock.SlotStride, info.Params.Count);
    }

    [Fact]
    public void Impulse_response_is_the_normalised_ir_with_zero_latency() {
        var ir = CabLikeIr(1_500, 1);
        using var cab = Create(null, ("slot1", WriteIr("a.wav", ir, 48_000)));

        var output = BlockRender.Mono(cab, Signals.Impulse(2_000), 64);

        var energy = MathF.Sqrt(ir.Sum(static v => v * v));
        for (var i = 0; i < ir.Length; i++) {
            Assert.Equal(ir[i] / energy, output[i], 1e-5f);
        }

        Assert.Equal(1.0, output.Sum(static v => (double)v * v), 1e-4); // unit energy
    }

    [Fact]
    public void Two_irs_are_blended_by_their_levels() {
        var a = CabLikeIr(800, 2);
        var b = CabLikeIr(1_200, 3);
        using var cab = Create(
            c => c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.SlotLevel), -6.0206f),
            ("slot1", WriteIr("a.wav", a, 48_000)),
            ("slot2", WriteIr("b.wav", b, 48_000))
        );

        var output = BlockRender.Mono(cab, Signals.Impulse(1_500), 64);

        var ea = MathF.Sqrt(a.Sum(static v => v * v));
        var eb = MathF.Sqrt(b.Sum(static v => v * v));
        for (var i = 0; i < 1_200; i++) {
            var expected = ((i < a.Length ? a[i] / ea : 0f) + 0.5f * b[i] / eb) / 1.5f; // relative levels sum to 1
            Assert.Equal(expected, output[i], 1e-4f);
        }
    }

    [Fact]
    public void Irs_at_other_sample_rates_are_resampled() {
        var ir = CabLikeIr(1_000, 4);
        using var cab = Create(null, ("slot1", WriteIr("a.wav", ir, 44_100)));

        var prepared = cab.PreparedImpulseResponse(0)!;

        Assert.Equal((int)Math.Ceiling(1_000 * 48_000 / 44_100.0), prepared.Length);
        var expected = Resampler.Resample(ir, 44_100, 48_000);
        var norm = 1f / MathF.Sqrt(expected.Sum(static v => v * v));
        Assert.True(Analysis.NullResidualDb(prepared, expected.Select(v => v * norm).ToArray()) < -120);
    }

    [Fact]
    public void Long_irs_are_truncated_with_a_fade() {
        using var cab = Create(null, ("slot1", WriteIr("long.wav", Signals.Noise(20_000, 5), 48_000)));

        var prepared = cab.PreparedImpulseResponse(0)!;

        Assert.Equal(CabBlock.MaxIrLength, prepared.Length);
        Assert.Equal(0f, prepared[^1], 1e-6f);
    }

    [Fact]
    public void Stereo_irs_process_each_channel_separately() {
        var left = CabLikeIr(400, 6);
        var right = CabLikeIr(400, 7);
        var path = Path.Combine(folder, "stereo.wav");
        WavFile.Write(path, new(48_000, [left, right]));
        using var cab = Create(null, ("slot1", path));

        var (outLeft, outRight) = BlockRender.Stereo(cab, Signals.Impulse(500), Signals.Impulse(500), _ => 64);

        Assert.True(Analysis.NullResidualDb(outLeft, outRight) > -40, "left and right should differ");
        Assert.Equal(cab.PreparedImpulseResponse(0, 1)![10], outRight[10], 1e-5f);
    }

    [Fact]
    public void Output_does_not_depend_on_block_size_and_does_not_allocate() {
        var ir = CabLikeIr(3_000, 8);
        using var a = Create(static c => c.Params.SetTarget(CabBlock.LowCut, 80), ("slot1", WriteIr("a.wav", ir, 48_000)));
        using var b = Create(static c => c.Params.SetTarget(CabBlock.LowCut, 80), ("slot1", WriteIr("b.wav", ir, 48_000)));
        var input = Signals.Noise(9_600, 9);
        var random = new Random(1);

        var fixedSize = BlockRender.Mono(a, input, 64);
        var varied = BlockRender.Stereo(b, input, input, _ => random.Next(1, 65)).Left;
        var left = Signals.Noise(64, 1);
        var right = Signals.Noise(64, 2);
        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 1_000; i++) {
                    a.Process(new(left, right), default);
                }
            }
        );

        Assert.True(Analysis.NullResidualDb(fixedSize, varied) < -120);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Irs_are_read_once_per_file_version() {
        var path = WriteIr("cached.wav", CabLikeIr(500, 10), 48_000);
        using var first = Create(null, ("slot1", path));
        using var second = Create(null, ("slot1", path));
        Assert.Same(first.PreparedImpulseResponse(0), second.PreparedImpulseResponse(0)); // a preset reload: cached

        WriteIr("cached.wav", CabLikeIr(700, 11), 48_000);
        using var third = Create(null, ("slot1", path));

        Assert.Equal(700, third.PreparedImpulseResponse(0)!.Length); // the file changed: read again
    }

    [Fact]
    public void Unknown_slots_and_unreadable_files_throw() {
        using var cab = new CabBlock();
        var garbage = Path.Combine(folder, "garbage.wav");
        File.WriteAllText(garbage, "not a wav file");

        Assert.Equal(["slot1", "slot2", "slot3", "slot4"], cab.AssetSlots);
        Assert.Throws<ArgumentException>(() => cab.LoadAsset("ir1", garbage));
        Assert.Throws<ArgumentException>(() => cab.LoadAsset("slot5", garbage));
        Assert.Throws<InvalidDataException>(() => cab.LoadAsset("slot1", garbage));
        Assert.Throws<FileNotFoundException>(() => cab.LoadAsset("slot1", Path.Combine(folder, "missing.wav")));
    }

    [Fact]
    public void An_ir_slot_with_no_file_passes_audio_through_untouched() {
        // Slot 1 set to IR with nothing loaded, the rest muted: the cab with no IR, transparent to the last bit.
        using var cab = Create(null);
        var left = Signals.Noise(4_000, 12);
        var right = Signals.Noise(4_000, 13);

        var (outLeft, outRight) = BlockRender.Stereo(cab, left, right, _ => 64);

        Assert.True(cab.IsPassThrough);
        Assert.Equal(left, outLeft);
        Assert.Equal(right, outRight);
    }

    [Fact]
    public void An_empty_ir_slot_contributes_nothing_to_the_others() {
        using var alone = Prepared();
        using var withEmpty = Prepared(
            c => {
                c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Source), CabBlock.SourceIr);
                c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f);
            }
        );

        Assert.False(withEmpty.IsPassThrough);
        Assert.Equal(alone.ImpulseResponse().Left, withEmpty.ImpulseResponse().Left);
    }

    [Fact]
    public void Muting_every_slot_silences_the_cab() {
        using var cab = Prepared(static c => c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mute), 1f));

        var output = BlockRender.Mono(cab, Signals.Noise(2_000, 14), 64);

        Assert.False(cab.IsPassThrough);
        Assert.All(output, static v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Ir_and_dynacab_slots_mix_linearly_with_pan_and_delay() {
        var path = WriteIr("a.wav", CabLikeIr(1_000, 15), 48_000);
        using var dynaCab = Prepared();
        using var ir = Create(
            static c => {
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Pan), 50f);
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Delay), 1f);
            },
            ("slot1", path)
        );
        using var both = new CabBlock();
        both.LoadAsset("slot2", path);
        both.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Source), CabBlock.SourceIr);
        both.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f);
        both.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Pan), 50f);
        both.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Delay), 1f);
        both.Prepare(Spec);

        var (dynaLeft, dynaRight) = dynaCab.ImpulseResponse();
        var (irLeft, irRight) = ir.ImpulseResponse();
        var (left, right) = both.ImpulseResponse();

        // Panned right by half: the left side at half level; delayed 1 ms (48 samples).
        Assert.Equal(48, Array.FindIndex(irRight, static v => v != 0f));
        Assert.Equal(0.5f * irRight[100], irLeft[100], 1e-6f);
        for (var i = 0; i < left.Length; i++) {
            Assert.Equal(0.5f * (dynaLeft[i] + irLeft[i]), left[i], 1e-6f); // two slots at 0 dB: each at half
            Assert.Equal(0.5f * (dynaRight[i] + irRight[i]), right[i], 1e-6f);
        }
    }

    [Fact]
    public void A_wav_in_an_ir_pack_slot_plays_as_a_one_point_pack() {
        var path = WriteIr("a.wav", CabLikeIr(1_500, 16), 48_000);
        using var pack = Create(static c => c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourcePack), ("slot1", path));
        using var file = Create(null, ("slot1", path));

        var (packIr, _) = pack.ImpulseResponse();
        var (fileIr, _) = file.ImpulseResponse();

        Assert.Single(pack.Grid(0)!.Positions);
        Assert.False(pack.IsPassThrough);
        Assert.True(Analysis.AllFinite(packIr));
        Assert.NotEqual(fileIr, packIr); // rebuilt minimum phase at the pack length, not the file itself
    }

    [Fact]
    public void Changing_a_slot_source_crossfades_from_the_control_thread() {
        var path = WriteIr("a.wav", CabLikeIr(1_000, 17), 48_000);
        using var cab = new CabBlock();
        cab.LoadAsset("slot1", path);
        cab.Prepare(Spec);
        BlockRender.Mono(cab, Signals.Noise(512, 18), 64);

        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceIr);
        cab.Refresh();
        BlockRender.Mono(cab, Signals.Noise(2_048, 19), 64);

        Assert.Equal(1, cab.IrSwaps);
        Assert.False(cab.IsFading);
        Assert.Equal(cab.PreparedImpulseResponse(0)![..1_000], cab.ImpulseResponse().Left[..1_000]);
    }

    [Fact]
    public void Slot_levels_are_relative_and_only_set_the_blend() {
        var a = WriteIr("a.wav", CabLikeIr(800, 20), 48_000);
        var b = WriteIr("b.wav", CabLikeIr(800, 21), 48_000);
        using var quiet = Create(static c => c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLevel), -12f), ("slot1", a));
        using var loud = Create(static c => c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLevel), 6f), ("slot1", a));
        using var blend = Create(
            static c => {
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLevel), 6f);
                c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.SlotLevel), 6f);
            },
            ("slot1", a),
            ("slot2", b)
        );

        Assert.Equal(quiet.ImpulseResponse().Left, loud.ImpulseResponse().Left); // one slot: its level does nothing
        var one = quiet.ImpulseResponse().Left;
        var two = blend.ImpulseResponse().Left;
        var ib = blend.PreparedImpulseResponse(1)!;
        for (var i = 0; i < ib.Length; i++) {
            Assert.Equal(0.5f * (one[i] + ib[i]), two[i], 1e-6f); // equal levels: half each, whatever the level
        }
    }

    [Fact]
    public void A_slot_low_cut_and_ir_length_shape_only_that_slot() {
        var path = WriteIr("a.wav", CabLikeIr(4_000, 22), 48_000);
        using var plain = Create(null, ("slot1", path));
        using var cut = Create(
            static c => {
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLowCut), 200f);
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLowSlope), 3f); // 24 dB/oct
            },
            ("slot1", path)
        );
        using var shortened = Create(static c => c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Length), 20f), ("slot1", path));

        var (before, _) = plain.ImpulseResponse();
        var (after, _) = cut.ImpulseResponse();
        var (truncated, _) = shortened.ImpulseResponse();

        var low = Analysis.MagnitudeAt(before, 60, 48_000) / Analysis.MagnitudeAt(after, 60, 48_000);
        Assert.True(low > 15, $"60 Hz, 1.7 octaves under a 24 dB/oct cut, should be well down (x{low:0.0})");
        var high = Analysis.MagnitudeAt(after, 3_000, 48_000) / Analysis.MagnitudeAt(before, 3_000, 48_000);
        Assert.InRange(high, 0.95, 1.05);
        Assert.True(truncated.Skip(960).All(static v => v == 0f), "cut to 20 ms");
        Assert.Equal(before[..896], truncated[..896]); // untouched up to the 64-sample fade
    }

    [Fact]
    public void Input_mode_feeds_the_cab_left_right_or_their_sum() {
        var left = Signals.Noise(1_000, 23);
        var right = Signals.Noise(1_000, 24);
        float[] Render(int mode) {
            using var cab = Create(c => c.Params.SetTarget(CabBlock.Input, mode));
            return BlockRender.Stereo(cab, left, right, _ => 64).Right;
        }

        Assert.Equal(left, Render(CabBlock.InputLeft));
        Assert.Equal(right, Render(CabBlock.InputRight));
        Assert.Equal(left.Select((v, i) => 0.5f * (v + right[i])).ToArray(), Render(CabBlock.InputSum));
        Assert.Equal(right, Render(CabBlock.InputStereo));
    }

    [Fact]
    public void Air_adds_the_dry_treble_above_its_frequency() {
        using var dry = Create(null);
        using var airy = Create(static c => c.Params.SetTarget(CabBlock.Air, 100f));
        var input = Signals.Noise(4_800, 25);

        var plain = BlockRender.Mono(dry, input, 64);
        var withAir = BlockRender.Mono(airy, input, 64);

        var added = withAir.Select((v, i) => v - plain[i]).ToArray();
        Assert.True(Analysis.MagnitudeAt(added, 12_000, 48_000) > 10 * Analysis.MagnitudeAt(added, 500, 48_000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_mic_preamp_keeps_quiet_signals_and_saturates_loud_ones(int type) {
        using var quiet = Create(c => {
                c.Params.SetTarget(CabBlock.Preamp, type);
                c.Params.SetTarget(CabBlock.Drive, 12f);
                c.Params.SetTarget(CabBlock.Saturation, 50f);
            }
        );
        using var loud = Create(c => {
                c.Params.SetTarget(CabBlock.Preamp, type);
                c.Params.SetTarget(CabBlock.Drive, CabBlock.Info.Params[CabBlock.Drive].Max);
            }
        );
        var tone = Signals.Sine(220, 48_000, 9_600, 0.002f);

        var small = BlockRender.Mono(quiet, tone, 64);
        var big = BlockRender.Mono(loud, Signals.Sine(220, 48_000, 9_600, 1f), 64);

        // Unity small-signal gain (the treble roll-off barely touches 220 Hz), and a bounded, finite output.
        Assert.Equal(Analysis.Rms(tone.AsSpan(4_800)), Analysis.Rms(small.AsSpan(4_800)), 0.0002);
        Assert.True(Analysis.AllFinite(big));
        Assert.True(Analysis.Peak(big) < 1.5);
    }

    static CabBlock Prepared(Action<CabBlock>? configure = null) {
        var cab = new CabBlock();
        configure?.Invoke(cab);
        cab.Prepare(Spec);
        return cab;
    }

    /// <summary>A cab whose slot 1 plays an IR file; each loaded slot is set to IR and unmuted.</summary>
    CabBlock Create(Action<CabBlock>? configure, params (string Slot, string Path)[] irs) {
        var cab = new CabBlock();
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceIr);
        foreach (var (slot, path) in irs) {
            var index = slot[^1] - '1';
            cab.LoadAsset(slot, path);
            cab.Params.SetTarget(CabBlock.SlotParam(index, CabBlock.Source), CabBlock.SourceIr);
            cab.Params.SetTarget(CabBlock.SlotParam(index, CabBlock.Mute), 0f);
        }

        configure?.Invoke(cab);
        cab.Prepare(Spec);
        return cab;
    }

    string WriteIr(string name, float[] ir, int sampleRate) {
        var path = Path.Combine(folder, name);
        WavFile.Write(path, new(sampleRate, [ir]));
        return path;
    }

    /// <summary>A decaying, low-passed noise burst: roughly the shape of a close-miked cabinet IR.</summary>
    static float[] CabLikeIr(int length, int seed) {
        var noise = Signals.Noise(length, seed);
        var ir = new float[length];
        float state = 0;
        for (var i = 0; i < length; i++) {
            state += 0.3f * (noise[i] - state);
            ir[i] = state * MathF.Exp(-i / (length / 5f));
        }

        return ir;
    }
}
