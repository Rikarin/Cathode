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
///     <para>
///         The input to each comb is scaled by <c>sqrt(1 - g^2)</c>, which keeps a broadband input at roughly the same
///         power whatever the decay; a sustained input exactly on a voice's pitch still rings up by
///         <c>sqrt((1 + g) / (1 - g))</c>. The loop is stable at any setting (see <see cref="DampedComb" />).
///     </para>
///     <para>
///         Each voice's Feedback scales its loop gain (100 % is the gain the decay asks for), and Master Feedback
///         scales them all. A voice can have a band-pass at its own frequency before its comb (so only the input near
///         its pitch excites it) or after it (so it rings with its fundamental alone); Filter Q times Master Q sets its
///         width. Master Level and Master Pan scale every voice's level and pan.
///     </para>
///     <para>
///         Input Mode Mono feeds every comb the sum of the inputs and places it with its pan (constant power). Stereo
///         runs a second set of combs, so the left input rings in the left combs and the right in the right, and the
///         pans become balances.
///     </para>
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
    public const int Balance = 19;
    public const int InputGain = 20;
    public const int MasterLevel = 21;
    public const int MasterPan = 22;
    public const int MasterFeedback = 23;
    public const int MasterQ = 24;
    public const int InputMode = 25;
    public const int Feedback1 = 26;
    public const int FilterLocation1 = 30;
    public const int FilterQ1 = 34;
    public const int GlobalMix = FilterQ1 + Voices;

    public const int FilterOff = 0;
    public const int FilterPre = 1;
    public const int FilterPost = 2;

    public const int Voices = 4;
    public const float MinFrequency = 20f;
    public const float MaxFrequency = 8_000f;

    /// <summary>Loop low-pass cutoff at 0 % damping.</summary>
    const double OpenCutoff = 20_000;

    /// <summary>Loop low-pass cutoff at 100 % damping.</summary>
    const double ClosedCutoff = 1_000;

    /// <summary>The loop gain never reaches one, whatever the feedback settings ask.</summary>
    const double MaxLoopGain = 0.99999;

    static readonly string[] FilterLocations = ["Off", "Pre", "Post"];

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
    readonly DampedComb[] combsRight = [new(), new(), new(), new()];
    readonly SvfState[] bandLeft = new SvfState[Voices];
    readonly SvfState[] bandRight = new SvfState[Voices];
    readonly SvfCoefficients[] band = new SvfCoefficients[Voices];
    readonly int[] location = new int[Voices];
    bool wasStereo;
    readonly float[] gainLeft = new float[Voices];
    readonly float[] gainRight = new float[Voices];
    readonly float[] nextLeft = new float[Voices];
    readonly float[] nextRight = new float[Voices];
    readonly float[] delay = new float[Voices];
    readonly float[] feedback = new float[Voices];
    readonly float[] inputGain = new float[Voices];
    readonly float[] damping = new float[Voices];
    float mix;
    OutputStage output;

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
            BlockParams.Mix(50f),
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("masterLevel", "Master Level", -24f, 12f, 0f, "dB"),
            ParamDescriptor.Float("masterPan", "Master Pan", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("masterFeedback", "Master Feedback", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("masterQ", "Master Q", 0.25f, 4f, 1f, "x", ParamTaper.Log),
            ParamDescriptor.Choice("inputMode", "Input Mode", ["Mono", "Stereo"]),
            ..Enumerable.Range(1, Voices)
                .Select(static v => ParamDescriptor.Float($"feedback{v}", $"Feedback {v}", 0f, 100f, 100f, "%")),
            ..Enumerable.Range(1, Voices)
                .Select(static v => ParamDescriptor.Choice(
                        $"filterLocation{v}",
                        $"Filter Location {v}",
                        FilterLocations
                    )
                ),
            ..Enumerable.Range(1, Voices)
                .Select(static v =>
                    ParamDescriptor.Float($"filterQ{v}", $"Filter Q {v}", 0.5f, 20f, 4f, string.Empty, ParamTaper.Log)
                ),
            EffectsMix.Param
        ],
        maxInstances: 2,
        channels: 2,
        pages: [
            new(
                "Basic",
                [
                    "tuning", "root", "chord", "decay", "damping", "masterFeedback", "masterQ", "masterLevel",
                    "masterPan"
                ]
            ),
            new(
                "Resonators",
                [
                    "freq1", "level1", "pan1", "feedback1", "filterLocation1", "filterQ1", "freq2", "level2", "pan2",
                    "feedback2", "filterLocation2", "filterQ2", "freq3", "level3", "pan3", "feedback3",
                    "filterLocation3", "filterQ3", "freq4", "level4", "pan4", "feedback4", "filterLocation4", "filterQ4"
                ]
            ),
            new("Advanced", ["inputGain", "inputMode", "globalMix"])
        ]
    );

    /// <summary>One sample of voice <paramref name="v" />: its comb, with its band-pass before or after it.</summary>
    float Voice(int v, DampedComb comb, ref SvfState state, float input) {
        var location = this.location[v];
        if (location == FilterPre) {
            input = (float)state.Tick(band[v], input);
        }

        var ring = comb.Tick(input * inputGain[v], delay[v], feedback[v], damping[v]);
        return location == FilterPost ? (float)state.Tick(band[v], ring) : ring;
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
        var masterLevel = Decibels.ToGain(Params.Advance(MasterLevel, frames));
        var masterPan = Params.Advance(MasterPan, frames) / 100f;
        var masterFeedback = Params.Advance(MasterFeedback, frames) / 100.0;
        var masterQ = Params.Advance(MasterQ, frames);
        var stereo = (int)Params.Current(InputMode) == 1;

        for (var v = 0; v < Voices; v++) {
            var free = Params.Advance(Frequency1 + v, frames);
            var frequency = chord is null ? free : 440 * Math.Pow(2, (root + chord[v] - 69) / 12.0);
            frequency = Math.Clamp(frequency, MinFrequency, maxFrequency);
            var scale = Params.Advance(Feedback1 + v, frames) / 100.0 * masterFeedback;
            var g = Math.Min(MaxLoopGain, Math.Pow(10, -3 / (t60 * frequency)) * scale);
            delay[v] = (float)(rate / frequency - DampedComb.PhaseDelay(a, frequency, rate));
            feedback[v] = (float)g;
            inputGain[v] = (float)Math.Sqrt(1 - g * g);
            damping[v] = (float)a;
            location[v] = (int)Params.Current(FilterLocation1 + v);
            var q = Params.Advance(FilterQ1 + v, frames) * masterQ;
            if (location[v] != FilterOff) {
                band[v] = SvfCoefficients.Design(BiquadType.BandPass, rate, frequency, q);
            }

            // Constant-power pan (a balance in stereo); four voices summed at half gain keep a broadband input near
            // unity.
            var gain = 0.5f * Decibels.ToGain(Params.Advance(Level1 + v, frames)) * masterLevel;
            var pan = Params.Advance(Pan1 + v, frames) / 100f * masterPan;
            if (stereo) {
                (nextLeft[v], nextRight[v]) = OutputStage.Gains(0f, 100f * pan);
                nextLeft[v] *= gain;
                nextRight[v] *= gain;
            } else {
                var angle = (pan + 1) * MathF.PI / 4;
                nextLeft[v] = gain * MathF.Sqrt(2) * MathF.Cos(angle);
                nextRight[v] = gain * MathF.Sqrt(2) * MathF.Sin(angle);
            }
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)Math.Ceiling(spec.SampleRate / MinFrequency) + 4;
        foreach (var comb in combs.Concat(combsRight)) {
            comb.Allocate(capacity);
        }
    }

    protected override void OnReset() {
        foreach (var comb in combs) {
            comb.Reset();
        }

        foreach (var comb in combsRight) {
            comb.Reset();
        }

        Array.Clear(bandLeft);
        Array.Clear(bandRight);
        wasStereo = (int)Params.Current(InputMode) == 1;

        Update(0);
        Array.Copy(nextLeft, gainLeft, Voices);
        Array.Copy(nextRight, gainRight, Voices);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var comb in combs) {
                comb.Dispose();
            }

            foreach (var comb in combsRight) {
                comb.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        Update(n);
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        var feed = Params.Advance(InputGain, n) / 100f;
        var stereo = (int)Params.Current(InputMode) == 1;
        if (stereo && !wasStereo) {
            foreach (var comb in combsRight) {
                comb.Reset(); // the right combs rest in mono: start them clean
            }

            Array.Clear(bandRight);
        }

        wasStereo = stereo;

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var input = 0.5f * (left + right) * feed;
            var position = (i + 1) / (float)n;
            float wetLeft = 0, wetRight = 0;
            for (var v = 0; v < Voices; v++) {
                var gl = gainLeft[v] + (nextLeft[v] - gainLeft[v]) * position;
                var gr = gainRight[v] + (nextRight[v] - gainRight[v]) * position;
                if (stereo) {
                    wetLeft += Voice(v, combs[v], ref bandLeft[v], left * feed) * gl;
                    wetRight += Voice(v, combsRight[v], ref bandRight[v], right * feed) * gr;
                } else {
                    var ring = Voice(v, combs[v], ref bandLeft[v], input);
                    wetLeft += ring * gl;
                    wetRight += ring * gr;
                }
            }

            currentMix += mixStep;
            io.Left[i] = left + (wetLeft - left) * currentMix;
            io.Right[i] = right + (wetRight - right) * currentMix;
        }

        Array.Copy(nextLeft, gainLeft, Voices);
        Array.Copy(nextRight, gainRight, Voices);
        for (var v = 0; v < Voices; v++) {
            bandLeft[v].FlushDenormals();
            bandRight[v].FlushDenormals();
        }

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
