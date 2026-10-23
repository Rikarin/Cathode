using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Resonator: four tuned Karplus–Strong-style combs fed in parallel from the mono input, tuned to a chord on a root
///     note or to four free frequencies. Each comb rings at its pitch and harmonics; decay sets the time for a comb to
///     fall 60 dB (the feedback is derived per voice from its period, so every voice decays at the same rate) and
///     damping lowers a one-pole low-pass in the loop so upper harmonics die sooner, like a plucked string.
/// </summary>
/// <remarks>
///     The input to each comb is scaled by <c>sqrt(1 - g^2)</c>, which keeps a broadband input at roughly the same power
///     whatever the decay; a sustained input exactly on a voice's pitch still rings up by <c>sqrt((1 + g) / (1 - g))</c>.
///     The loop is stable at any setting (see <see cref="DampedComb" />).
/// </remarks>
public sealed class ResonatorBlock() : DspBlock(Info) {
    public const int Tuning = 0;
    public const int Root = 1;
    public const int Chord = 2;
    public const int Frequency1 = 3;
    public const int Level1 = 7;
    public const int Pan1 = 11;
    public const int Decay = 15;
    public const int Damping = 16;
    public const int Mix = 17;
    public const int Level = 18;

    public const int Voices = 4;
    public const float MinFrequency = 20f;
    public const float MaxFrequency = 8_000f;

    /// <summary>Loop low-pass cutoff at 0 % damping.</summary>
    const double OpenCutoff = 20_000;

    /// <summary>Loop low-pass cutoff at 100 % damping.</summary>
    const double ClosedCutoff = 1_000;

    /// <summary>Semitones above the root for each voice, per chord option.</summary>
    static readonly int[][] ChordIntervals = [
        [0, 12, 24, 36],
        [0, 7, 14, 21],
        [0, 7, 12, 19],
        [0, 4, 7, 12],
        [0, 3, 7, 12],
        [0, 2, 7, 12],
        [0, 5, 7, 12],
        [0, 4, 7, 11],
        [0, 3, 7, 10],
        [0, 4, 7, 10]
    ];

    readonly DampedComb[] combs = [new(), new(), new(), new()];
    readonly float[] gainLeft = new float[Voices];
    readonly float[] gainRight = new float[Voices];
    readonly float[] nextLeft = new float[Voices];
    readonly float[] nextRight = new float[Voices];
    readonly float[] delay = new float[Voices];
    readonly float[] feedback = new float[Voices];
    readonly float[] inputGain = new float[Voices];
    readonly float[] damping = new float[Voices];
    float mix;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "resonator",
        "Resonator",
        "Filter",
        [
            ParamDescriptor.Choice("tuning", "Tuning", ["Chord", "Frequency"]),
            ParamDescriptor.Float("root", "Root", 24f, 84f, 45f, "note"),
            ParamDescriptor.Choice(
                "chord",
                "Chord",
                ["Octaves", "Fifths", "Power", "Major", "Minor", "Sus2", "Sus4", "Major 7", "Minor 7", "Dominant 7"],
                3
            ),
            ParamDescriptor.Float("freq1", "Freq 1", MinFrequency, MaxFrequency, 110f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("freq2", "Freq 2", MinFrequency, MaxFrequency, 165f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("freq3", "Freq 3", MinFrequency, MaxFrequency, 220f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("freq4", "Freq 4", MinFrequency, MaxFrequency, 330f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level1", "Level 1", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("level2", "Level 2", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("level3", "Level 3", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("level4", "Level 4", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("pan1", "Pan 1", -100f, 100f, -75f, "%"),
            ParamDescriptor.Float("pan2", "Pan 2", -100f, 100f, -25f, "%"),
            ParamDescriptor.Float("pan3", "Pan 3", -100f, 100f, 25f, "%"),
            ParamDescriptor.Float("pan4", "Pan 4", -100f, 100f, 75f, "%"),
            ParamDescriptor.Float("decay", "Decay", 0.05f, 10f, 1f, "s", ParamTaper.Log),
            ParamDescriptor.Float("damping", "Damping", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2,
        channels: 2
    );

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)Math.Ceiling(spec.SampleRate / MinFrequency) + 4;
        foreach (var comb in combs) {
            comb.Allocate(capacity);
        }
    }

    protected override void OnReset() {
        foreach (var comb in combs) {
            comb.Reset();
        }

        Update(0);
        Array.Copy(nextLeft, gainLeft, Voices);
        Array.Copy(nextRight, gainRight, Voices);
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var comb in combs) {
                comb.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        Update(n);
        var mix = Params.Advance(Mix, n) / 100f;
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var input = 0.5f * (left + right);
            var position = (i + 1) / (float)n;
            float wetLeft = 0, wetRight = 0;
            for (var v = 0; v < Voices; v++) {
                var ring = combs[v].Tick(input * inputGain[v], delay[v], feedback[v], damping[v]);
                wetLeft += ring * (gainLeft[v] + (nextLeft[v] - gainLeft[v]) * position);
                wetRight += ring * (gainRight[v] + (nextRight[v] - gainRight[v]) * position);
            }

            currentMix += mixStep;
            io.Left[i] = left + (wetLeft - left) * currentMix;
            io.Right[i] = right + (wetRight - right) * currentMix;
        }

        Array.Copy(nextLeft, gainLeft, Voices);
        Array.Copy(nextRight, gainRight, Voices);
        this.mix = mix;
        var outputGain = Decibels.ToGain(Params.Advance(Level, n));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    /// <summary>
    ///     Advances the parameters by <paramref name="frames" /> and derives each voice's loop: delay (the period less
    ///     the loop filter's phase delay), feedback for the decay time, input normalisation, and pan gains.
    /// </summary>
    void Update(int frames) {
        var rate = Spec.SampleRate;
        var chord = Params.Current(Tuning) < 0.5f ? ChordIntervals[(int)Params.Current(Chord)] : null;
        var root = Params.Advance(Root, frames);
        var t60 = Params.Advance(Decay, frames);
        var cutoff = OpenCutoff * Math.Pow(ClosedCutoff / OpenCutoff, Params.Advance(Damping, frames) / 100.0);
        var a = Math.Exp(-2 * Math.PI * cutoff / rate);
        var maxFrequency = Math.Min(MaxFrequency, rate * 0.45);

        for (var v = 0; v < Voices; v++) {
            var free = Params.Advance(Frequency1 + v, frames);
            var frequency = chord is null ? free : 440 * Math.Pow(2, (root + chord[v] - 69) / 12.0);
            frequency = Math.Clamp(frequency, MinFrequency, maxFrequency);
            var g = Math.Pow(10, -3 / (t60 * frequency));
            delay[v] = (float)(rate / frequency - DampedComb.PhaseDelay(a, frequency, rate));
            feedback[v] = (float)g;
            inputGain[v] = (float)Math.Sqrt(1 - g * g);
            damping[v] = (float)a;

            // Constant-power pan; four voices summed at half gain keep a broadband input near unity.
            var gain = 0.5f * Decibels.ToGain(Params.Advance(Level1 + v, frames));
            var angle = (Params.Advance(Pan1 + v, frames) / 100f + 1) * MathF.PI / 4;
            nextLeft[v] = gain * MathF.Sqrt(2) * MathF.Cos(angle);
            nextRight[v] = gain * MathF.Sqrt(2) * MathF.Sin(angle);
        }
    }
}
