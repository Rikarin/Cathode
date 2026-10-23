namespace Cathode.Dsp.Pitch;

/// <summary>Keys, scales and diatonic intervals for the intelligent harmonizer.</summary>
public static class Scales {
    /// <summary>Key names, C first. A key is the pitch class of the scale's root.</summary>
    public static IReadOnlyList<string> Keys { get; } = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

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
}
