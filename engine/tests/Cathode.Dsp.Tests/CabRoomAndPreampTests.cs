using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Cab block's stereo room mics (Room Mic Spacing) and its Preamp Mode (docs/08-blocks-parity.md §5).
/// </summary>
public sealed class CabRoomAndPreampTests {
    const double Rate = 48_000;
    static readonly ProcessSpec Spec = new(Rate, 64);

    [Fact]
    public void Without_spacing_the_room_is_the_same_on_both_sides() {
        using var block = Prepared((CabBlock.Room, 100f));
        var (left, right) = block.ImpulseResponse();

        Assert.Equal(left, right);
    }

    [Fact]
    public void Spacing_without_a_room_changes_nothing() {
        using var plain = Prepared();
        using var spaced = Prepared((CabBlock.MicSpacing, 200f));

        Assert.Equal(plain.ImpulseResponse().Left, spaced.ImpulseResponse().Left);
        Assert.Equal(plain.ImpulseResponse().Left, spaced.ImpulseResponse().Right);
    }

    [Fact]
    public void Spaced_room_mics_differ_in_the_reflections_and_more_so_the_wider_they_are() {
        var narrow = SideDifference(30f);
        var wide = SideDifference(200f);

        Assert.True(narrow > 0, "spaced mics should hear different reflections");
        Assert.True(wide > narrow, $"wide {wide} should differ more than narrow {narrow}");
    }

    [Fact]
    public void Spaced_room_mics_keep_the_direct_sound_and_the_loudness() {
        using var block = Prepared((CabBlock.Room, 100f), (CabBlock.MicSpacing, 200f));
        var (left, right) = block.ImpulseResponse();

        // The direct sound is the close mic's on both sides: the onset and the first half millisecond agree closely.
        var head = (int)(0.0005 * Rate);
        Assert.Equal(Analysis.PeakIndex(left), Analysis.PeakIndex(right));
        Assert.True(Analysis.NullResidualDb(left.AsSpan(0, head), right.AsSpan(0, head)) < -20);
        var levelDb = Analysis.ToDb(Analysis.Rms(left) / Analysis.Rms(right));
        Assert.InRange(levelDb, -1.5, 1.5);
    }

    [Fact]
    public void The_room_mic_offset_moves_every_wall_image() {
        var mic = CabCatalogue.Mics[10];
        var placement = new MicPlacement(
            CabCatalogue.Speakers[3],
            CabCatalogue.Enclosures[5],
            mic,
            0.25,
            0.05,
            0,
            true,
            1,
            RoomShape: CabRoom.Default
        );
        var centred = new CabAcoustics(placement);
        var offset = new CabAcoustics(placement with { RoomShape = CabRoom.Default with { MicOffset = 0.5 } });
        var anechoic = new CabAcoustics(placement with { Room = 0 });

        // The direct path is shared; only the reflections move.
        Assert.Equal(centred.DirectDelay, offset.DirectDelay);
        var moved = 0;
        for (var hz = 200.0; hz < 4_000; hz *= 1.1) {
            var direct = anechoic.Paths(hz);
            var a = centred.Paths(hz) - direct;
            var b = offset.Paths(hz) - direct;
            moved += (a - b).Magnitude > 1e-3 * a.Magnitude ? 1 : 0;
        }

        Assert.True(moved > 20, $"only {moved} frequencies changed");
    }

    [Fact]
    public void High_quality_preamp_folds_back_less_than_economy() {
        // 9 kHz driven 12 dB: its 5th harmonic (45 kHz) folds to 3 kHz at the engine rate.
        var economy = AliasDb(CabBlock.PreampEconomy);
        var high = AliasDb(CabBlock.PreampHighQuality);

        Assert.True(high < economy - 12, $"high quality {high:0.0} dB vs economy {economy:0.0} dB");
    }

    [Fact]
    public void High_quality_preamp_keeps_the_level_of_a_clean_signal() {
        var sine = Signals.Sine(1_000, Rate, 9_600, 0.01f);
        var economy = Preamp(CabBlock.PreampEconomy, sine, 0f);
        var high = Preamp(CabBlock.PreampHighQuality, sine, 0f);

        Assert.Equal(Analysis.Rms(economy.AsSpan(4_800)), Analysis.Rms(high.AsSpan(4_800)), 1e-4);
    }

    /// <summary>The energy of the left minus the right IR relative to the left's.</summary>
    static double SideDifference(float spacing) {
        using var block = Prepared((CabBlock.Room, 100f), (CabBlock.MicSpacing, spacing));
        var (left, right) = block.ImpulseResponse();
        var difference = left.Zip(right, static (l, r) => l - r).ToArray();
        return Analysis.Rms(difference) / Analysis.Rms(left);
    }

    /// <summary>The 3 kHz alias of a hard-driven 9 kHz sine, relative to the 9 kHz fundamental, in dB.</summary>
    static double AliasDb(int mode) {
        var output = Preamp(mode, Signals.Sine(9_000, Rate, 19_200, 0.5f), 12f);
        var steady = output.AsSpan(9_600);
        return Analysis.ToDb(Analysis.MagnitudeAt(steady, 3_000, Rate) / Analysis.MagnitudeAt(steady, 9_000, Rate));
    }

    /// <summary>The block as a plain mic preamp (slot 1 an IR slot with no file passes audio through).</summary>
    static float[] Preamp(int mode, float[] input, float driveDb) {
        using var block = Prepared(
            (CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceIr),
            (CabBlock.Preamp, 3f), // FET
            (CabBlock.PreampMode, mode),
            (CabBlock.Drive, driveDb)
        );
        var left = (float[])input.Clone();
        var right = (float[])input.Clone();
        for (var offset = 0; offset < left.Length; offset += 64) {
            block.Process(new(left.AsSpan(offset, 64), right.AsSpan(offset, 64)), new(Rate, 120, offset));
        }

        return left;
    }

    static CabBlock Prepared(params (int Param, float Value)[] settings) {
        var block = new CabBlock();
        foreach (var (param, value) in settings) {
            block.Params.SetTarget(param, value);
        }

        block.Prepare(Spec);
        block.Reset();
        return block;
    }
}
