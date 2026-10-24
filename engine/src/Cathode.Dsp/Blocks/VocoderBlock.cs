using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Channel vocoder. The modulator (the input, or the left channel) runs through a bank of band-pass filters whose
///     envelope followers set the gains of a matching bank applied to the carrier, so the carrier takes on the
///     modulator's spectral envelope. A block has one stereo input, so the carrier is either an internal oscillator
///     (band-limited saw or pulse, or noise, at a note or a chord) or the right input channel, with the left channel
///     as the modulator. Every carrier band is gated by its modulator envelope, so a silent modulator gives silence.
/// </summary>
/// <remarks>
///     Bands are a fixed bank of <see cref="BandCount" /> third-octave-ish filters spaced logarithmically from
///     <see cref="LowestBand" /> to <see cref="HighestBand" />, each two cascaded constant-peak band-pass sections.
///     High-frequency emphasis tilts the synthesis bands upward by up to 6 dB per octave above 1 kHz, keeping
///     consonants intelligible.
/// </remarks>
public sealed class VocoderBlock() : DspBlock(Info) {
    public const int Carrier = 0;
    public const int Note = 1;
    public const int Chord = 2;
    public const int Attack = 3;
    public const int Release = 4;
    public const int Emphasis = 5;
    public const int Mix = 6;
    public const int Level = 7;
    public const int Balance = 8;

    public const int CarrierSaw = 0;
    public const int CarrierPulse = 1;
    public const int CarrierNoise = 2;
    public const int CarrierRightInput = 3;

    public const int BandCount = 20;
    public const double LowestBand = 100;
    public const double HighestBand = 8_000;

    /// <summary>Q of each of the two cascaded band-pass sections per band.</summary>
    public const double BandQ = 3;

    const int MaxVoices = 4;
    const float PulseWidth = 0.25f;

    /// <summary>Make-up gain so a typical modulator and saw carrier come out near the modulator's level.</summary>
    const float OutputScale = 16f;

    const double EnvelopeFloor = 1e-15;
    const uint NoiseSeed = 0x9E3779B9u;

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

    readonly SvfCoefficients[] bands = new SvfCoefficients[BandCount];
    readonly SvfState[] analysis = new SvfState[BandCount * 2];
    readonly SvfState[] synthesis = new SvfState[BandCount * 2];
    readonly double[] envelope = new double[BandCount];
    readonly float[] weight = new float[BandCount];
    readonly double[] phase = new double[MaxVoices];
    float emphasis = float.NaN;
    uint noise;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "vocoder",
        "Vocoder",
        "Filter",
        [
            ParamDescriptor.Choice("carrier", "Carrier", ["Saw", "Pulse", "Noise", "Right Input"]),
            ParamDescriptor.Float("note", "Note", 24f, 84f, 48f, "note"),
            ParamDescriptor.Choice(
                "chord",
                "Chord",
                ["Single", "Octave", "Fifth", "Major", "Minor", "Sus4", "Major 7", "Minor 7"]
            ),
            ParamDescriptor.Float("attack", "Attack", 0.5f, 50f, 4f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 5f, 500f, 40f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("emphasis", "HF Emphasis", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 1,
        channels: 1
    );

    /// <summary>Centre frequency of band <paramref name="band" />.</summary>
    public static double BandFrequency(int band) =>
        LowestBand * Math.Pow(HighestBand / LowestBand, band / (double)(BandCount - 1));

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

    protected override void OnPrepare(in ProcessSpec spec) {
        for (var k = 0; k < BandCount; k++) {
            bands[k] = SvfCoefficients.Design(BiquadType.BandPass, spec.SampleRate, BandFrequency(k), BandQ);
        }
    }

    protected override void OnReset() {
        Array.Clear(analysis);
        Array.Clear(synthesis);
        Array.Clear(envelope);
        Array.Clear(phase);
        noise = NoiseSeed;
        emphasis = float.NaN;
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
        var emphasis = Params.Advance(Emphasis, n) / 100f;
        if (emphasis != this.emphasis) {
            SetEmphasis(emphasis);
        }

        var mix = Params.Advance(Mix, n) / 100f;
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        var voices = intervals.Length;
        var voiceGain = 1 / Math.Sqrt(voices);

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            double modulator, carrier;
            if (source == CarrierRightInput) {
                modulator = left;
                carrier = right;
            } else {
                modulator = 0.5 * (left + right);
                carrier = Oscillator(source, note, intervals, rate) * voiceGain;
            }

            double wet = 0;
            for (var k = 0; k < BandCount; k++) {
                var c = bands[k];
                var level = Math.Abs(analysis[2 * k + 1].Tick(c, analysis[2 * k].Tick(c, modulator)));
                envelope[k] += (level - envelope[k]) * (level > envelope[k] ? attack : release);
                var band = synthesis[2 * k + 1].Tick(c, synthesis[2 * k].Tick(c, carrier));
                wet += band * envelope[k] * weight[k];
            }

            currentMix += mixStep;
            var output = (float)wet;
            io.Left[i] = left + (output - left) * currentMix;
            io.Right[i] = right + (output - right) * currentMix;
        }

        for (var k = 0; k < BandCount; k++) {
            if (envelope[k] < EnvelopeFloor) {
                envelope[k] = 0;
            }

            analysis[2 * k].FlushDenormals();
            analysis[2 * k + 1].FlushDenormals();
            synthesis[2 * k].FlushDenormals();
            synthesis[2 * k + 1].FlushDenormals();
        }

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
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

    /// <summary>Synthesis band gains: a tilt of up to +6 dB per octave around 1 kHz, times the make-up gain.</summary>
    void SetEmphasis(float emphasis) {
        this.emphasis = emphasis;
        for (var k = 0; k < BandCount; k++) {
            weight[k] = OutputScale * (float)Math.Pow(BandFrequency(k) / 1_000, emphasis);
        }
    }
}
