using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Cab block's Axe-Fx parity features: stereo input routing, the extended preamp and its input meter, per-slot
///     proximity and smoothing, mic distance and Auto-Align, Motor Drive, FullRes IRs, the Flat source and the global
///     cab modelling switch.
/// </summary>
public sealed class CabParityTests : IDisposable {
    const double Rate = 48_000;
    static readonly ProcessSpec Spec = new(Rate, 64);
    readonly string folder = Directory.CreateTempSubdirectory("cathode-cabparity-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void A_stereo_input_feeds_slots_one_and_three_from_the_left_and_two_and_four_from_the_right() {
        float[][] irs = [CabLikeIr(600, 1), CabLikeIr(700, 2), CabLikeIr(800, 3), CabLikeIr(900, 4)];
        using var cab = Create(
            null,
            ("slot1", WriteIr("a.wav", irs[0])),
            ("slot2", WriteIr("b.wav", irs[1])),
            ("slot3", WriteIr("c.wav", irs[2])),
            ("slot4", WriteIr("d.wav", irs[3]))
        );
        Assert.Equal(IrFeed.Cross, cab.Feed);

        // A left-only input reaches only slots 1 and 3, whose levels are relative between themselves.
        var silence = new float[1_200];
        var (left, right) = BlockRender.Stereo(cab, Signals.Impulse(1_200), silence, _ => 64);
        var expected = Blend(irs[0], irs[2]);
        for (var i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i], left[i], 1e-5f);
            Assert.Equal(expected[i], right[i], 1e-5f); // centred
        }

        cab.Reset();
        (left, _) = BlockRender.Stereo(cab, silence, Signals.Impulse(1_200), _ => 64);
        expected = Blend(irs[1], irs[3]);
        for (var i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i], left[i], 1e-5f);
        }

        static float[] Blend(float[] a, float[] b) {
            var ea = MathF.Sqrt(a.Sum(static v => v * v));
            var eb = MathF.Sqrt(b.Sum(static v => v * v));
            return [..Enumerable.Range(0, b.Length).Select(i => 0.5f * ((i < a.Length ? a[i] / ea : 0f) + b[i] / eb))];
        }
    }

    [Fact]
    public void Two_convolutions_serve_one_side_or_a_mono_input() {
        var path = WriteIr("a.wav", CabLikeIr(600, 5));
        using var cab = Create(null, ("slot1", path), ("slot3", path));
        Assert.Equal(IrFeed.Left, cab.Feed);

        // Only the left input is heard: the right one does not leak in.
        var (left, _) = BlockRender.Stereo(cab, new float[2_000], Signals.Noise(2_000, 6), _ => 64);
        Assert.All(left, static v => Assert.Equal(0f, v));

        cab.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f); // slot 2: a DynaCab, on the right
        cab.Refresh();
        Assert.Equal(IrFeed.Cross, cab.Feed);
        foreach (var mode in (int[])[CabBlock.InputLeft, CabBlock.InputRight, CabBlock.InputSum]) {
            cab.Params.SetTarget(CabBlock.Input, mode);
            cab.Refresh();
            Assert.Equal(IrFeed.Both, cab.Feed);
        }
    }

    [Fact]
    public void A_flat_slot_and_a_cab_slot_split_a_stereo_input_into_a_di_and_a_cab() {
        // The split-output trick: slot 1 (left input) a DynaCab hard left, slot 2 (right input) Flat hard right.
        using var split = Prepared(c => {
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Pan), -100f);
                c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Source), CabBlock.SourceFlat);
                c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f);
                c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Pan), 100f);
            }
        );
        using var cabOnly = Prepared(static c => c.Params.SetTarget(CabBlock.Input, CabBlock.InputLeft));
        var guitar = Signals.Noise(4_800, 7);
        var bass = Signals.Noise(4_800, 8);

        var (left, right) = BlockRender.Stereo(split, guitar, bass, _ => 64);
        var cabbed = BlockRender.Stereo(cabOnly, guitar, guitar, _ => 64).Left;

        Assert.Equal(bass, right); // the DI, untouched
        Assert.True(Analysis.NullResidualDb(cabbed, left) < -120, "the left output is the cab alone");
    }

    [Fact]
    public void Preamp_types_keep_their_curves_and_their_onsets_mark_one_db_of_compression() {
        Assert.Equal(12, MicPreamp.Types.Count);
        Assert.Equal(new[] { "FET 1", "FET 2" }, MicPreamp.Types.Skip(3).Take(2));
        Assert.Equal("Exciter", MicPreamp.Types[^1]);

        // The existing curves, renumbered: Tube, Bipolar, FET (now FET 1) and Transformer are as they were.
        Assert.Equal(Math.Tanh(0.7), MicPreampTypes.Shape(MicPreampTypes.Fet1, 0.7), 12);
        Assert.Equal(0.7 / 1.7, MicPreampTypes.Shape(MicPreampTypes.Bipolar, 0.7), 12);
        Assert.Equal(Math.Tanh(1.25 * -0.7) / 1.25, MicPreampTypes.Shape(MicPreampTypes.Tube, -0.7), 12);
        Assert.Equal(0.7 - 4 * 0.343 / 27, MicPreampTypes.Shape(MicPreampTypes.Transformer, 0.7), 12);

        for (var type = 1; type < MicPreamp.Types.Count; type++) {
            var onset = MicPreampTypes.ClipOnset(type);
            var deviation = Math.Max(
                Math.Abs(Analysis.ToDb(MicPreampTypes.Shape(type, onset) / onset)),
                Math.Abs(Analysis.ToDb(MicPreampTypes.Shape(type, -onset) / -onset))
            );
            Assert.Equal(1, deviation, 0.01);
            Assert.InRange(onset, 0.05, 2);
        }
    }

    [Fact]
    public void Tape_emphasis_saturates_the_treble_first_and_vintage_the_lows() {
        double Compression(int type, double hz) {
            using var cab = Flat(c => {
                    c.Params.SetTarget(CabBlock.Preamp, type);
                    c.Params.SetTarget(CabBlock.Drive, 12f);
                }
            );
            var tone = Signals.Sine(hz, Rate, 9_600, 0.5f);
            var output = BlockRender.Mono(cab, tone, 64);
            return Analysis.ToDb(Analysis.Rms(output.AsSpan(4_800)) / Analysis.Rms(tone.AsSpan(4_800)));
        }

        var tapeLow = Compression(MicPreampTypes.Tape35, 200);
        var tapeHigh = Compression(MicPreampTypes.Tape35, 8_000);
        Assert.True(tapeHigh < tapeLow - 3, $"tape: {tapeLow:0.0} dB at 200 Hz, {tapeHigh:0.0} dB at 8 kHz");
        var vintageLow = Compression(MicPreampTypes.Vintage, 40);
        var vintageHigh = Compression(MicPreampTypes.Vintage, 2_000);
        Assert.True(vintageLow < vintageHigh - 2, $"vintage: {vintageLow:0.0} dB at 40 Hz, {vintageHigh:0.0} at 2 kHz");

        // A longer time constant puts the corner lower, so more of the treble is emphasised.
        Assert.True(Compression(MicPreampTypes.Tape70, 3_000) < Compression(MicPreampTypes.Tape35, 3_000) - 1);
    }

    [Fact]
    public void The_exciter_adds_harmonics_to_the_treble_and_leaves_the_bass_clean() {
        double Harmonic(double hz) {
            using var cab = Flat(c => {
                    c.Params.SetTarget(CabBlock.Preamp, MicPreampTypes.Exciter);
                    c.Params.SetTarget(CabBlock.Drive, 18f);
                    c.Params.SetTarget(CabBlock.Saturation, 50f);
                }
            );
            var output = BlockRender.Mono(cab, Signals.Sine(hz, Rate, 19_200, 0.5f), 64)[9_600..];
            var harmonics = Analysis.MagnitudeAt(output, 2 * hz, Rate) + Analysis.MagnitudeAt(output, 3 * hz, Rate);
            return harmonics / Analysis.MagnitudeAt(output, hz, Rate);
        }

        Assert.True(Harmonic(100) < 0.005, $"bass harmonics {Harmonic(100):0.0000}");
        Assert.True(Harmonic(6_000) > 0.05, $"treble harmonics {Harmonic(6_000):0.0000}");
    }

    [Fact]
    public void The_preamp_input_meter_reads_zero_at_the_onset_of_clipping() {
        var meters = new float[2];
        foreach (var type in (int[])[MicPreampTypes.Fet1, MicPreampTypes.Transformer, MicPreampTypes.Modern]) {
            using var cab = Flat(c => c.Params.SetTarget(CabBlock.Preamp, type));
            var amplitude = (float)MicPreampTypes.ClipOnset(type);
            BlockRender.Mono(cab, Signals.Sine(997, Rate, 4_800, amplitude), 64);
            cab.ReadMeters(meters);
            Assert.Equal(new[] { "preampIn", "motorReduction" }, cab.MeterIds);
            Assert.Equal(0, meters[0], 0.3);
            Assert.Equal(0, meters[1]);

            // Drive raises what reaches the curve, 1 dB for 1 dB.
            cab.Params.SetTarget(CabBlock.Drive, 6f);
            BlockRender.Mono(cab, Signals.Sine(997, Rate, 9_600, amplitude), 64);
            cab.ReadMeters(meters);
            Assert.Equal(6, meters[0], 0.3);
        }
    }

    [Fact]
    public void Proximity_lifts_the_lows_of_an_ir_slot_but_not_of_a_dynacab() {
        var path = WriteIr("a.wav", CabLikeIr(4_000, 9));
        using var cab = Create(null, ("slot1", path));
        var (plain, _) = cab.ImpulseResponse();
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Proximity), 100f);
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.ProximityFreq), 200f);
        var (near, _) = cab.ImpulseResponse();

        // (s + ω_f + ω_c) / (s + ω_f): +3 dB at the corner, unity in the treble, about +6 dB/oct below.
        double Gain(double hz) => Analysis.ToDb(Analysis.MagnitudeAt(near, hz, Rate) / Analysis.MagnitudeAt(plain, hz, Rate));
        Assert.Equal(0, Gain(8_000), 0.2);
        Assert.Equal(Analysis.ToDb(Math.Sqrt(1 + Math.Pow(212 / 200.0, 2))), Gain(200), 0.5);
        Assert.InRange(Gain(50) - Gain(100), 4, 6.5);

        using var dynaCab = Prepared();
        var (before, _) = dynaCab.ImpulseResponse();
        dynaCab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Proximity), 200f);
        var (after, _) = dynaCab.ImpulseResponse();
        Assert.Equal(before, after); // a DynaCab's Distance sets its proximity
    }

    [Fact]
    public void Smoothing_works_per_slot_on_a_dynacab_too() {
        using var cab = Prepared(static c => c.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f));
        var (before, _) = cab.ImpulseResponse();
        cab.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Smoothing), 100f);
        var (smoothed, _) = cab.ImpulseResponse();

        Assert.NotEqual(before, smoothed);
        Assert.InRange(Analysis.PeakIndex(smoothed), 0, 8); // the onset stays
    }

    [Fact]
    public void Mic_distance_delays_a_slot_by_up_to_three_point_four_metres() {
        Assert.Equal(3.4 / CabAcoustics.SpeedOfSound * 1_000, CabBlock.MaxDelayMs, 1e-4);
        var delay = CabBlock.Info.Params[CabBlock.SlotParam(0, CabBlock.Delay)];
        Assert.Equal(CabBlock.MaxDelayMs, delay.Max);
        Assert.Equal(1f, CabBlock.Info.Params[CabBlock.SlotParam(0, CabBlock.AutoAlign)].Default);

        using var cab = Flat(null);
        var (flat, _) = cab.ImpulseResponse();
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Delay), CabBlock.MaxDelayMs);
        var (delayed, _) = cab.ImpulseResponse();
        Assert.Equal(0, Analysis.PeakIndex(flat));
        Assert.Equal(CabBlock.MaxDelayMs / 1_000 * Rate, Analysis.PeakIndex(delayed), 1.0);
    }

    [Fact]
    public void Motor_drive_compresses_as_the_voice_coil_heats_and_is_off_by_default() {
        Assert.Equal(0f, CabBlock.Info.Params[CabBlock.MotorDrive].Default);
        Assert.Equal(0.5f, CabBlock.Info.Params[CabBlock.MotorTime].Default);
        var tone = Signals.Sine(1_000, Rate, 48_000);

        float[] Render(float drive, float tau, out float meter) {
            using var cab = Flat(c => {
                    c.Params.SetTarget(CabBlock.MotorDrive, drive);
                    c.Params.SetTarget(CabBlock.MotorTime, tau);
                }
            );
            var output = BlockRender.Mono(cab, tone, 64);
            Span<float> meters = stackalloc float[2];
            cab.ReadMeters(meters);
            meter = meters[1];
            return output;
        }

        Assert.Equal(tone, Render(0, 0.5f, out var none));
        Assert.Equal(0, none);

        // Steady state: θ (1 + θ) = k A² / 2, with k = 8 at 100 %, and the gain is 1 / (1 + θ).
        var output = Render(100, 0.05f, out var meter);
        var theta = (-1 + Math.Sqrt(1 + 4 * VoiceCoil.FullHeating / 2)) / 2;
        var expected = -Analysis.ToDb(1 + theta);
        var measured = Analysis.ToDb(Analysis.Rms(output.AsSpan(24_000)) / Analysis.Rms(tone.AsSpan(24_000)));
        Assert.Equal(expected, measured, 0.3);
        Assert.Equal(-expected, meter, 0.5);

        // A slower coil has hardly warmed up after 0.1 s.
        var slow = Render(100, 1f, out _);
        var fast = Render(100, 0.05f, out _);
        Assert.True(Analysis.Rms(slow.AsSpan(4_000, 1_000)) > 1.3 * Analysis.Rms(fast.AsSpan(4_000, 1_000)));
    }

    [Fact]
    public void Full_res_irs_play_to_about_one_point_four_seconds() {
        var ir = Signals.Noise(65_000, 10).Select(static (v, i) => v * MathF.Exp(-i / 20_000f)).ToArray();
        using var cab = Create(null, ("slot1", WriteIr("long.wav", ir)));
        var output = BlockRender.Mono(cab, Signals.Impulse(66_000), 64);

        var energy = MathF.Sqrt(ir.Sum(static v => v * v));
        Assert.Equal(ir[64_000] / energy, output[64_000], 1e-5f);
        Assert.Equal(ir[100] / energy, output[100], 1e-5f);
        Assert.Equal(1_366f, CabBlock.FullLengthMs);
        Assert.Equal(CabBlock.MaxIrLength, IrPlayerBlock.MaxIrLength);
    }

    [Fact]
    public void Cab_modelling_off_passes_audio_through_and_fades_back_in() {
        using var cab = Prepared();
        var input = Signals.Noise(640, 11);
        var left = (float[])input.Clone();
        var right = (float[])input.Clone();
        var off = new BlockContext(Rate, 120, 0, CabsOff: true);
        for (var offset = 0; offset < input.Length; offset += 64) {
            cab.Process(new(left.AsSpan(offset, 64), right.AsSpan(offset, 64)), off);
        }

        Assert.Equal(input[64..], left[64..]); // after the first callback's fade, untouched
        Assert.Equal(input[64..], right[64..]);

        // Back on: the cab again, from a clean state.
        using var fresh = Prepared();
        var on = BlockRender.Mono(fresh, input, 64);
        var back = (float[])input.Clone();
        var backRight = (float[])input.Clone();
        for (var offset = 0; offset < input.Length; offset += 64) {
            cab.Process(new(back.AsSpan(offset, 64), backRight.AsSpan(offset, 64)), new(Rate, 120, 0));
        }

        Assert.Equal(on[64..], back[64..]);
    }

    [Fact]
    public void Export_bakes_the_master_cuts_into_the_mixed_ir() {
        using var cab = Prepared();
        var (plain, _) = cab.ExportImpulseResponse();
        cab.Params.SetTarget(CabBlock.LowCut, 300f);
        var (cut, cutRight) = cab.ExportImpulseResponse();

        Assert.True(plain.Length is > 1_000 and < 4_000, $"{plain.Length} samples: trimmed after the DynaCab");
        Assert.Equal(cut, cutRight);
        Assert.True(
            Analysis.MagnitudeAt(cut, 60, Rate) < 0.2 * Analysis.MagnitudeAt(plain, 60, Rate),
            "the low cut is in the export"
        );
    }

    /// <summary>A default cab (slot 1 DynaCab).</summary>
    static CabBlock Prepared(Action<CabBlock>? configure = null) {
        var cab = new CabBlock();
        configure?.Invoke(cab);
        cab.Prepare(Spec);
        return cab;
    }

    /// <summary>A cab whose slot 1 is Flat, so what it does around the IR is heard alone.</summary>
    static CabBlock Flat(Action<CabBlock>? configure) =>
        Prepared(c => {
                c.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceFlat);
                configure?.Invoke(c);
            }
        );

    /// <summary>A cab whose loaded slots play IR files, unmuted.</summary>
    static CabBlock Create(Action<CabBlock>? configure, params (string Slot, string Path)[] irs) {
        var cab = new CabBlock();
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

    string WriteIr(string name, float[] ir) {
        var path = Path.Combine(folder, name);
        WavFile.Write(path, new((int)Rate, [ir]));
        return path;
    }

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
