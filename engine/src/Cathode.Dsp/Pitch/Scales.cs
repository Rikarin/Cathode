namespace Cathode.Dsp.Pitch;

/// <summary>Keys, scales and diatonic intervals for the intelligent harmonizer.</summary>
public static class Scales {
    /// <summary>Semitones of each scale degree above the root.</summary>
    static readonly int[][] Intervals = [
        [0, 2, 4, 5, 7, 9, 11],
        [0, 2, 3, 5, 7, 8, 10],
        [0, 2, 3, 5, 7, 9, 10],
        [0, 1, 3, 5, 7, 8, 10],
        [0, 2, 4, 6, 7, 9, 11],
        [0, 2, 4, 5, 7, 9, 10],
        [0, 1, 3, 5, 6, 8, 10],
        [0, 2, 3, 5, 7, 8, 11],
        [0, 2, 3, 5, 7, 9, 11],
        [0, 2, 4, 7, 9],
        [0, 3, 5, 7, 10],
        [0, 3, 5, 6, 7, 10]
    ];

    /// <summary>Each scale's pitch classes above its root as a 12-bit mask, bit 0 = the root.</summary>
    static readonly int[] Masks =
        [..Intervals.Select(static degrees => degrees.Aggregate(0, static (mask, d) => mask | (1 << d)))];

    /// <summary>Five-limit just intonation: the ratio of each semitone above the key's root.</summary>
    static readonly double[] Just =
        [1, 16 / 15.0, 9 / 8.0, 6 / 5.0, 5 / 4.0, 4 / 3.0, 45 / 32.0, 3 / 2.0, 8 / 5.0, 5 / 3.0, 9 / 5.0, 15 / 8.0];

    /// <summary>Key names, C first. A key is the pitch class of the scale's root.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Scale names, in the order of <see cref="Intervals" />.</summary>
    public static IReadOnlyList<string> Names { get; } = [
        "Major",
        "Minor",
        "Dorian",
        "Phrygian",
        "Lydian",
        "Mixolydian",
        "Locrian",
        "Harmonic Minor",
        "Melodic Minor",
        "Major Pentatonic",
        "Minor Pentatonic",
        "Blues"
    ];

    /// <summary>
    ///     The pitch ratio that moves <paramref name="note" /> by <paramref name="semitones" /> in just intonation on
    ///     <paramref name="key" />: both notes are placed on the key's five-limit scale and the ratio is theirs, so a
    ///     harmony a major third over the root is exactly 5/4 rather than the equal-tempered 1.2599. A fractional part
    ///     of <paramref name="semitones" /> stays equal-tempered.
    /// </summary>
    public static double JustRatio(int note, int key, double semitones) {
        var whole = (int)Math.Round(semitones);
        var from = note - key;
        return JustPosition(from + whole) / JustPosition(from) * Math.Pow(2, (semitones - whole) / 12);
    }

    /// <summary>Whether <paramref name="scale" /> is a minor one: a minor third above its root and no major third.</summary>
    public static bool IsMinor(int scale) {
        var mask = Masks[Math.Clamp(scale, 0, Masks.Length - 1)];
        return (mask & (1 << 3)) != 0 && (mask & (1 << 4)) == 0;
    }

    /// <summary>Pitch class (0 = C .. 11 = B) of a MIDI note.</summary>
    public static int PitchClass(int note) => (note % 12 + 12) % 12;

    /// <summary>
    ///     The scale to harmonise in over a chord: <paramref name="preferred" /> when it contains every tone of
    ///     <paramref name="chord" /> (a 12-bit pitch-class mask, bit 0 = C) from <paramref name="root" />, else the first
    ///     scale in <see cref="Names" /> that does (chord-scale theory: Am picks Minor, G7 Mixolydian), else
    ///     <paramref name="preferred" />.
    /// </summary>
    public static int ChordScale(int root, int chord, int preferred) {
        root = PitchClass(root);
        var relative = ((chord >> root) | (chord << (12 - root))) & 0xFFF;
        preferred = Math.Clamp(preferred, 0, Masks.Length - 1);
        if ((relative & ~Masks[preferred]) == 0) {
            return preferred;
        }

        for (var scale = 0; scale < Masks.Length; scale++) {
            if ((relative & ~Masks[scale]) == 0) {
                return scale;
            }
        }

        return preferred;
    }

    /// <summary>MIDI note number (A4 = 69) of <paramref name="frequency" />, fractional.</summary>
    public static double MidiNote(double frequency) => 69 + 12 * Math.Log2(frequency / 440);

    /// <summary>
    ///     Semitones from <paramref name="note" /> to the note <paramref name="steps" /> scale degrees away in
    ///     <paramref name="key" /> and <paramref name="scale" />. A note outside the scale harmonises as the scale note
    ///     just below it, so the interval stays diatonic; e.g. in C major a third above A is C (+3), above C is E (+4).
    /// </summary>
    public static int HarmonySemitones(int note, int key, int scale, int steps) {
        var degrees = Intervals[Math.Clamp(scale, 0, Intervals.Length - 1)];
        var relative = note - key;
        var octave = (int)Math.Floor(relative / 12.0);
        var pitchClass = relative - 12 * octave;
        var degree = degrees.Length - 1;
        while (degrees[degree] > pitchClass) {
            degree--;
        }

        var target = degree + steps;
        var targetOctave = (int)Math.Floor(target / (double)degrees.Length);
        var targetDegree = target - targetOctave * degrees.Length;
        return 12 * targetOctave + degrees[targetDegree] - degrees[degree];
    }

    /// <summary>
    ///     <see cref="HarmonySemitones(int, int, int, int)" /> snapped to the tones of a chord: when the diatonic note
    ///     is not in <paramref name="chord" /> (a 12-bit pitch-class mask, bit 0 = C; 0 means no chord), the nearest
    ///     chord tone on the same side of <paramref name="note" /> replaces it, the one nearer the played note on a tie.
    ///     Unison and octaves stay as they are, and the snapped voice keeps at least a minor third from the played note
    ///     (or the diatonic interval's own size, if smaller), so it never rubs a semitone against the melody.
    /// </summary>
    public static int HarmonySemitones(int note, int key, int scale, int steps, int chord) {
        var semitones = HarmonySemitones(note, key, scale, steps);
        var length = Intervals[Math.Clamp(scale, 0, Intervals.Length - 1)].Length;
        var target = note + semitones;
        if (chord == 0 || steps % length == 0 || ((chord >> PitchClass(target)) & 1) != 0) {
            return semitones;
        }

        var direction = Math.Sign(semitones);
        var minimum = Math.Min(3, Math.Abs(semitones));
        for (var distance = 1; distance < 12; distance++) {
            foreach (var candidate in (ReadOnlySpan<int>)
                     [target - direction * distance, target + direction * distance]) {
                var interval = candidate - note;
                if (((chord >> PitchClass(candidate)) & 1) != 0
                    && Math.Sign(interval) == direction
                    && Math.Abs(interval) >= minimum) {
                    return interval;
                }
            }
        }

        return semitones;
    }

    static double JustPosition(int relative) {
        var octave = (int)Math.Floor(relative / 12.0);
        return Math.Pow(2, octave) * Just[relative - 12 * octave];
    }
}
