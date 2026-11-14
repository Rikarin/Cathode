using Cathode.Engine.Rig;

namespace Cathode.Engine.Tests;

/// <summary>The rig metronome: where its clicks land, and what a tempo change does to the beats around it.</summary>
public sealed class MetronomeTests {
    const double Rate = 48_000;
    const int Block = 64;
    const int ClickSamples = (int)(0.025 * Rate);

    [Fact]
    public void Clicks_land_on_the_beat_with_every_fourth_one_accented() {
        var clicks = Render(new(Rate), 4 * (int)Rate, static _ => 120);

        Assert.Equal(8, clicks.Count); // 120 BPM for four seconds
        Assert.All(Gaps(clicks), static gap => Assert.Equal(24_000, gap));
        Assert.Equal([0, 4], Accents(clicks));
    }

    [Fact]
    public void A_tempo_change_moves_only_the_beats_after_it() {
        // The grid used to be re-divided from sample 0 by the new beat length, so a change after a minute of playing
        // fired a click 794 samples after the one before it and moved the accent to another beat of the bar.
        var change = 58 * (int)Rate; // a whole 116 beats at 120 BPM, so the accent is due at the change

        var clicks = Render(new(Rate), change + 8 * (int)Rate, t => t < change ? 120 : 121);

        var before = clicks.Where(c => c.Start <= change).ToList();
        var after = clicks.Where(c => c.Start >= change).ToList();
        Assert.All(Gaps(before), static gap => Assert.Equal(24_000, gap));
        // 121 BPM is 23,801.65 samples a beat, so a gap is one or the other side of it; it used to stutter to 794.
        Assert.All(Gaps(after), static gap => Assert.InRange(gap, 23_801L, 23_802L));
        var accents = Accents(clicks);
        Assert.All(accents.Zip(accents.Skip(1), static (a, b) => b - a), static gap => Assert.Equal(4, gap));
    }

    static IEnumerable<long> Gaps(List<(long Start, bool Accent)> clicks) =>
        clicks.Zip(clicks.Skip(1), static (a, b) => b.Start - a.Start);

    /// <summary>Which of the clicks are the accent, by index.</summary>
    static int[] Accents(List<(long Start, bool Accent)> clicks) =>
        [..clicks.Index().Where(static c => c.Item.Accent).Select(static c => c.Index)];

    /// <summary>
    ///     Renders <paramref name="samples" /> samples one block at a time at the tempo <paramref name="tempo" />
    ///     gives for each, and returns every click: where it starts, and whether it is the accented one (1.5 kHz
    ///     rather than 1 kHz, told apart by how often it crosses zero).
    /// </summary>
    static List<(long Start, bool Accent)> Render(Metronome metronome, int samples, Func<long, double> tempo) {
        var signal = new float[samples];
        var left = new float[Block];
        var right = new float[Block];
        for (var t = 0; t + Block <= samples; t += Block) {
            Array.Clear(left);
            Array.Clear(right);
            metronome.Process(left, right, t, tempo(t), 1f);
            left.CopyTo(signal, t);
        }

        var clicks = new List<(long Start, bool Accent)>();
        for (var i = 1; i < signal.Length; i++) {
            // A click's own first sample is sin(0) = 0, so a burst begins at the first sample that is not silent.
            if (signal[i] == 0f || signal[i - 1] != 0f) {
                continue;
            }

            var crossings = 0;
            for (var j = i + 1; j < Math.Min(signal.Length, i + ClickSamples) && signal[j] != 0f; j++) {
                if (signal[j] * signal[j - 1] < 0f) {
                    crossings++;
                }
            }

            clicks.Add((i - 1, crossings > 62)); // 1.5 kHz crosses ~75 times in 25 ms, 1 kHz ~50
        }

        return clicks;
    }
}
