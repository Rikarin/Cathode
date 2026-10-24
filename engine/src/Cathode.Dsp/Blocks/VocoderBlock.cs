using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Channel vocoder. The modulator (the analysis signal) runs through a bank of band-pass filters whose envelope
///     followers set the gains of a matching bank applied to the carrier, so the carrier takes on the modulator's
///     spectral envelope. The carrier is an internal oscillator (band-limited saw or pulse, or noise, at a note or a
///     chord) or one side of the block's input; every carrier band is gated by its modulator envelope, so a silent
///     modulator gives silence.
/// </summary>
/// <remarks>
///     <para>
///         The analysis signal is the block's input (Analysis Channel: both sides, left or right; with a carrier from one
///         side of the input, both sides means the other side) or, with Analysis
///         Source set to a row, whatever is cabled in from that grid row. The block then takes its inputs by row
///         (<see cref="IRowInputBlock" />): the chosen row feeds the analysis alone and every other row is summed into
///         the block input as usual, which is where an input carrier comes from. With the row not cabled the analysis
///         is silent. Analysis Source can also name one of the interface's inputs (<see cref="HardwareInputs" />): a
///         microphone on Input 2 talking through the guitar, the classic vocoder rig.
///     </para>
///     <para>
///         Bands: 4 to 24 filters spaced logarithmically from Min to Max Frequency, each two cascaded constant-peak
///         band-pass sections of Q Resonance. Frequency Shift moves the synthesis bands against the analysis bands
///         (a formant shift). Each band has its own level and pan (Master Pan scales the pans); High-frequency
///         emphasis tilts the synthesis bands upward by up to 6 dB per octave above 1 kHz, keeping consonants
///         intelligible, and Highpass Mix adds the modulator's own content above the top band back in for sibilance.
///         Freeze holds the band envelopes where they are, so the carrier keeps the last spectrum.
///     </para>
/// </remarks>
public sealed class VocoderBlock() : DspBlock(Info), IRowInputBlock, IHardwareInputBlock {
    public const int Carrier = 0;
    public const int Note = 1;
    public const int Chord = 2;
    public const int Attack = 3;
    public const int Release = 4;
    public const int Emphasis = 5;
    public const int Mix = 6;
    public const int Level = 7;
    public const int Balance = 8;
    public const int Bands = 9;
    public const int MinFrequency = 10;
    public const int MaxFrequency = 11;
    public const int Resonance = 12;
    public const int FrequencyShift = 13;
    public const int Freeze = 14;
    public const int HighpassMix = 15;
    public const int MasterPan = 16;
    public const int InputGain = 17;
    public const int AnalysisChannel = 18;
    public const int AnalysisSource = 19;
    public const int BandLevel1 = 20;
    public const int BandPan1 = BandLevel1 + MaxBands;

    public const int CarrierSaw = 0;
    public const int CarrierPulse = 1;
    public const int CarrierNoise = 2;
    public const int CarrierLeftInput = 3;
    public const int CarrierRightInput = 4;

    public const int MaxBands = 24;

    /// <summary>The source rows the analysis can come from (Row 1 to Row 6), besides the block input.</summary>
    public const int SourceRows = 6;

    const int MaxVoices = 4;
    const float PulseWidth = 0.25f;

    /// <summary>Make-up gain so a typical modulator and saw carrier come out near the modulator's level.</summary>
    const float OutputScale = 16f;

    const double EnvelopeFloor = 1e-15;
    const uint NoiseSeed = 0x9E3779B9u;
    static readonly int[] BandCounts = [4, 8, 12, 16, 20, 24];

    /// <summary>Semitones above the note for each chord option, in option order.</summary>
    static readonly int[][] ChordIntervals = [
        [0],
        [0, 12],
        [0, 7],
        [0, 4, 7],
        [0, 3, 7],
        [0, 5, 7],
        [0, 4, 7, 11],
        [0, 3, 7, 10]
    ];

    readonly SvfCoefficients[] analysisBands = new SvfCoefficients[MaxBands];
    readonly SvfCoefficients[] synthesisBands = new SvfCoefficients[MaxBands];
    readonly SvfState[] analysis = new SvfState[MaxBands * 2];
    readonly SvfState[] synthesis = new SvfState[MaxBands * 2];
    readonly double[] envelope = new double[MaxBands];
    readonly float[] weight = new float[MaxBands];
    readonly float[] gainLeft = new float[MaxBands];
    readonly float[] gainRight = new float[MaxBands];
    readonly double[] phase = new double[MaxVoices];
    float[] sideLeft = [];
    float[] sideRight = [];
    int sideFrames;
    SvfCoefficients highpass;
    SvfState highpassState;
    (int Count, float Min, float Max, float Q, float Shift, float Emphasis) design = (-1, 0, 0, 0, 0, 0);
    uint noise;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "vocoder",
        "Vocoder",
        "Filter",
        [
            ParamDescriptor.Choice("carrier", "Carrier", ["Saw", "Pulse", "Noise", "Left Input", "Right Input"]),
            ParamDescriptor.Float("note", "Note", 24f, 84f, 48f, "note"),
            ParamDescriptor.Choice(
                "chord",
                "Chord",
                ["Single", "Octave", "Fifth", "Major", "Minor", "Sus4", "Major 7", "Minor 7"]
            ),
            ParamDescriptor.Float("attack", "Attack", 0.5f, 50f, 4f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Decay", 5f, 500f, 40f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("emphasis", "HF Emphasis", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("bands", "Bands", ["4", "8", "12", "16", "20", "24"], 4),
            ParamDescriptor.Float("minFreq", "Min Frequency", 50f, 1_000f, 100f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("maxFreq", "Max Frequency", 1_000f, 16_000f, 8_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("resonance", "Resonance", 0.5f, 20f, 3f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("freqShift", "Frequency Shift", 0.5f, 2f, 1f, "x", ParamTaper.Log),
            ParamDescriptor.Switch("freeze", "Freeze"),
            ParamDescriptor.Float("hpMix", "Highpass Mix", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("masterPan", "Master Pan", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("analysisChannel", "Analysis Channel", Stereo.InputSelectOptions),
            ParamDescriptor.Choice(
                "analysisSource",
                "Analysis Source",
                [
                    "Block Input",
                    ..Enumerable.Range(1, SourceRows).Select(static r => $"Row {r}"),
                    ..HardwareInputs.Names
                ]
            ),
            ..Enumerable.Range(1, MaxBands)
                .Select(static b => ParamDescriptor.Float(
                        $"bandLevel{b}",
                        $"Level {b}",
                        Decibels.SilenceDb,
                        12f,
                        0f,
                        "dB"
                    )
                ),
            ..Enumerable.Range(1, MaxBands)
                .Select(static b => ParamDescriptor.Float($"bandPan{b}", $"Pan {b}", -100f, 100f, 0f, "%"))
        ],
        maxInstances: 1,
        channels: 1
    );

    public InputPort? Inputs { get; set; }

    /// <summary>
    ///     Centre frequency of band <paramref name="band" /> of <paramref name="count" />, spaced logarithmically from
    ///     <paramref name="min" /> to <paramref name="max" />.
    /// </summary>
    public static double BandFrequency(int band, int count, double min, double max) =>
        min * Math.Pow(max / min, band / (double)(count - 1));

    /// <summary>Centre frequency of band <paramref name="band" /> of the default layout (20 bands, 100 Hz to 8 kHz).</summary>
    public static double BandFrequency(int band) => BandFrequency(band, 20, 100, 8_000);

    /// <summary>The number of bands for option <paramref name="option" /> of the Bands parameter.</summary>
    public static int BandCount(int option) => BandCounts[Math.Clamp(option, 0, BandCounts.Length - 1)];

    /// <summary>
    ///     Hardware input <paramref name="source" /> (<see cref="HardwareInputs" />) into the side buffer, as a chosen row
    ///     would arrive there; silence without the graph's inputs.
    /// </summary>
    void LoadHardwareSource(int source, int frames) {
        if (frames > sideLeft.Length) {
            return;
        }

        if (Inputs is { } port) {
            port.Read(source, sideLeft.AsSpan(0, frames), sideRight.AsSpan(0, frames));
        } else {
            Array.Clear(sideLeft, 0, frames);
            Array.Clear(sideRight, 0, frames);
        }

        sideFrames = frames;
    }

    public void MixInput(int row, in StereoBuffer source, in StereoBuffer destination) {
        var analysisRow = (int)Params.GetTarget(AnalysisSource) - 1;
        if (row != analysisRow || source.Length > sideLeft.Length) {
            for (var i = 0; i < source.Length; i++) {
                destination.Left[i] += source.Left[i];
                destination.Right[i] += source.Right[i];
            }

            return;
        }

        if (sideFrames != source.Length) {
            Array.Clear(sideLeft, 0, source.Length);
            Array.Clear(sideRight, 0, source.Length);
            sideFrames = source.Length;
        }

        for (var i = 0; i < source.Length; i++) {
            sideLeft[i] += source.Left[i];
            sideRight[i] += source.Right[i];
        }
    }

    /// <summary>PolyBLEP residual that band-limits a unit step at phase 0 (Välimäki and Huovilainen).</summary>
    static double Blep(double t, double dt) {
        if (t < dt) {
            var x = t / dt;
            return x + x - x * x - 1;
        }

        if (t > 1 - dt) {
            var x = (t - 1) / dt;
            return x * x + x + x + 1;
        }

        return 0;
    }

    /// <summary>One sample of the internal carrier: the chord's voices summed, or white noise.</summary>
    double Oscillator(int source, float note, int[] intervals, double sampleRate) {
        if (source == CarrierNoise) {
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            return noise * (2.0 / uint.MaxValue) - 1;
        }

        double sum = 0;
        for (var v = 0; v < intervals.Length; v++) {
            var frequency = Math.Min(440 * Math.Pow(2, (note + intervals[v] - 69) / 12.0), sampleRate * 0.45);
            var dt = frequency / sampleRate;
            var t = phase[v];
            if (source == CarrierSaw) {
                sum += 2 * t - 1 - Blep(t, dt);
            } else {
                var falling = t - PulseWidth;
                falling -= Math.Floor(falling);
                sum += (t < PulseWidth ? 1 : -1) + Blep(t, dt) - Blep(falling, dt) + (1 - 2 * PulseWidth);
            }

            t += dt;
            phase[v] = t - Math.Floor(t);
        }

        return sum;
    }

    /// <summary>
    ///     Redesigns the banks when their layout changes: analysis bands from <paramref name="min" /> to
    ///     <paramref name="max" />, synthesis bands <paramref name="shift" /> times higher, and the synthesis band
    ///     weights, a tilt of up to +6 dB per octave around 1 kHz times the make-up gain.
    /// </summary>
    void Design(int count, float min, float max, float q, float shift, float emphasis) {
        if (design == (count, min, max, q, shift, emphasis)) {
            return;
        }

        if (count != design.Count) {
            Array.Clear(analysis);
            Array.Clear(synthesis);
            Array.Clear(envelope);
        }

        design = (count, min, max, q, shift, emphasis);
        var rate = Spec.SampleRate;
        var top = Math.Min(max, rate * 0.45);
        for (var k = 0; k < count; k++) {
            var frequency = BandFrequency(k, count, min, top);
            var shifted = Math.Min(frequency * shift, rate * 0.45);
            analysisBands[k] = SvfCoefficients.Design(BiquadType.BandPass, rate, frequency, q);
            synthesisBands[k] = SvfCoefficients.Design(BiquadType.BandPass, rate, shifted, q);
            weight[k] = OutputScale * (float)Math.Pow(shifted / 1_000, emphasis);
        }

        highpass = SvfCoefficients.Design(BiquadType.HighPass, rate, top, 0.7071);
    }

    /// <summary>Each band's left and right gains: its level, placed by its pan (scaled by Master Pan) at constant power.</summary>
    void BandGains(int frames) {
        var masterPan = Params.Advance(MasterPan, frames) / 100f;
        for (var k = 0; k < MaxBands; k++) {
            var gain = Decibels.ToGain(Params.Advance(BandLevel1 + k, frames));
            var pan = Params.Advance(BandPan1 + k, frames) / 100f * masterPan;
            if (pan == 0) {
                (gainLeft[k], gainRight[k]) = (gain, gain);
                continue;
            }

            var angle = (pan + 1) * MathF.PI / 4; // constant power, unity per side at the centre
            gainLeft[k] = gain * MathF.Sqrt(2) * MathF.Cos(angle);
            gainRight[k] = gain * MathF.Sqrt(2) * MathF.Sin(angle);
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        sideLeft = new float[spec.MaxBlockSize];
        sideRight = new float[spec.MaxBlockSize];
        design = (-1, 0, 0, 0, 0, 0);
    }

    protected override void OnReset() {
        Array.Clear(analysis);
        Array.Clear(synthesis);
        Array.Clear(envelope);
        Array.Clear(phase);
        highpassState.Reset();
        sideFrames = 0;
        noise = NoiseSeed;
        design = (-1, 0, 0, 0, 0, 0);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var n = io.Length;
        var source = (int)Params.Current(Carrier);
        var intervals = ChordIntervals[(int)Params.Current(Chord)];
        var note = Params.Advance(Note, n);
        var attack = 1 - Math.Exp(-1000 / (Params.Advance(Attack, n) * rate));
        var release = 1 - Math.Exp(-1000 / (Params.Advance(Release, n) * rate));
        var count = BandCount((int)Params.Current(Bands));
        Design(
            count,
            Params.Advance(MinFrequency, n),
            Params.Advance(MaxFrequency, n),
            Params.Advance(Resonance, n),
            Params.Advance(FrequencyShift, n),
            Params.Advance(Emphasis, n) / 100f
        );
        BandGains(n);
        var freeze = Params.Current(Freeze) >= 0.5f;
        var highpassMix = Params.Advance(HighpassMix, n) / 100f;
        var inputGain = Params.Advance(InputGain, n) / 100f;
        var channel = (int)Params.Current(AnalysisChannel);
        var sourceChoice = (int)Params.Current(AnalysisSource);
        if (sourceChoice > SourceRows) {
            LoadHardwareSource(sourceChoice - SourceRows - 1, n);
        }

        var fromRow = sourceChoice > 0;
        if (channel == 0 && !fromRow && source is CarrierLeftInput or CarrierRightInput) {
            channel = source == CarrierLeftInput ? 2 : 1; // the carrier has one side: analyse the other
        }

        var side = fromRow && sideFrames == n;

        var mix = Params.Advance(Mix, n) / 100f;
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        var voices = intervals.Length;
        var voiceGain = 1 / Math.Sqrt(voices);

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var modulator = fromRow
                ? side ? Stereo.Select(channel, sideLeft[i], sideRight[i]) : 0f
                : Stereo.Select(channel, left, right);
            modulator *= inputGain;
            var carrier = source switch {
                CarrierLeftInput => left,
                CarrierRightInput => right,
                _ => Oscillator(source, note, intervals, rate) * voiceGain
            };

            double wetLeft = 0, wetRight = 0;
            for (var k = 0; k < count; k++) {
                var a = analysisBands[k];
                var level = Math.Abs(analysis[2 * k + 1].Tick(a, analysis[2 * k].Tick(a, modulator)));
                if (!freeze) {
                    envelope[k] += (level - envelope[k]) * (level > envelope[k] ? attack : release);
                }

                var s = synthesisBands[k];
                var band = synthesis[2 * k + 1].Tick(s, synthesis[2 * k].Tick(s, carrier)) * envelope[k] * weight[k];
                wetLeft += band * gainLeft[k];
                wetRight += band * gainRight[k];
            }

            if (highpassMix > 0) {
                var air = highpassMix * highpassState.Tick(highpass, modulator);
                wetLeft += air;
                wetRight += air;
            }

            currentMix += mixStep;
            io.Left[i] = left + ((float)wetLeft - left) * currentMix;
            io.Right[i] = right + ((float)wetRight - right) * currentMix;
        }

        for (var k = 0; k < MaxBands; k++) {
            if (envelope[k] < EnvelopeFloor) {
                envelope[k] = 0;
            }

            analysis[2 * k].FlushDenormals();
            analysis[2 * k + 1].FlushDenormals();
            synthesis[2 * k].FlushDenormals();
            synthesis[2 * k + 1].FlushDenormals();
        }

        highpassState.FlushDenormals();
        sideFrames = 0;
        if (side) {
            Array.Clear(sideLeft, 0, n);
            Array.Clear(sideRight, 0, n);
        }

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
