using System.Numerics;

namespace Cathode.Dsp;

/// <summary>
///     The MIDI notes (0-127) held down on the rig's MIDI input, as a 128-bit set: <paramref name="Low" /> holds notes
///     0-63, <paramref name="High" /> notes 64-127. Blocks that follow a keyboard (the Pitch block's MIDI key source)
///     read it from <see cref="BlockContext.Notes" />.
/// </summary>
public readonly record struct HeldNotes(ulong Low, ulong High) {
    public bool IsEmpty => (Low | High) == 0;

    /// <summary>The lowest held note, or -1 when none is held.</summary>
    public int Lowest =>
        Low != 0 ? BitOperations.TrailingZeroCount(Low)
        : High != 0 ? 64 + BitOperations.TrailingZeroCount(High)
        : -1;

    /// <summary>The held pitch classes as a 12-bit mask, bit 0 = C.</summary>
    public int PitchClasses => Classes(Low, 0) | Classes(High, 64);

    /// <summary>A set of the given notes; for tests and tools.</summary>
    public static HeldNotes Of(params ReadOnlySpan<int> notes) {
        var set = default(HeldNotes);
        foreach (var note in notes) {
            set = set.With(note, true);
        }

        return set;
    }

    public bool Contains(int note) => (uint)note < 128 && ((note < 64 ? Low >> note : High >> (note - 64)) & 1) != 0;

    /// <summary>This set with <paramref name="note" /> pressed (<paramref name="held" />) or released.</summary>
    public HeldNotes With(int note, bool held) {
        if ((uint)note >= 128) {
            return this;
        }

        var bit = 1UL << (note & 63);
        return note < 64
            ? this with { Low = held ? Low | bit : Low & ~bit }
            : this with { High = held ? High | bit : High & ~bit };
    }

    static int Classes(ulong bits, int offset) {
        var mask = 0;
        while (bits != 0) {
            mask |= 1 << ((offset + BitOperations.TrailingZeroCount(bits)) % 12);
            bits &= bits - 1;
        }

        return mask;
    }
}
