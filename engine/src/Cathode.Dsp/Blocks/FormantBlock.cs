using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Vowel formant filter ("talk box" without the tube): three parallel resonant band-passes placed on the first three
///     formants of a vowel (adult male averages after Peterson and Barney, JASA 1952). <b>Control</b> morphs from the
///     start vowel (0 %) through the mid vowel (50 %) to the end vowel (100 %), interpolating formant frequencies
///     geometrically, so a modifier or expression pedal can make the guitar "talk". <b>Shift</b> scales every formant
///     (smaller or larger "throat"). The band-passes are state-variable filters, so fast sweeps stay smooth.
/// </summary>
public sealed class FormantBlock() : DspBlock(Info) {
    public const int Start = 0;
    public const int Mid = 1;
    public const int End = 2;
    public const int Control = 3;
    public const int Resonance = 4;
    public const int Shift = 5;
    public const int Mix = 6;
    public const int Level = 7;
    public const int Balance = 8;

    public const int Formants = 3;

    const int SubBlock = 16;

    /// <summary>F1–F3 in Hz for A ("father"), E ("bed"), I ("heed"), O ("ought"), U ("food").</summary>
    static readonly float[][] VowelFormants = [
        [730f, 1_090f, 2_440f],
        [530f, 1_840f, 2_480f],
        [270f, 2_290f, 3_010f],
        [570f, 840f, 2_410f],
        [300f, 870f, 2_240f]
    ];

    /// <summary>Relative level of each formant: a gentle tilt, so the vowel reads without the guitar losing its top.</summary>
    static readonly float[] FormantGains = [1f, 0.63f, 0.4f];

    readonly SvfState[] left = new SvfState[Formants];
    readonly SvfState[] right = new SvfState[Formants];
    readonly SvfCoefficients[] coefficients = new SvfCoefficients[Formants];
    float mix = 1f;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "formant",
        "Formant",
        "Filter",
        [
            ParamDescriptor.Choice("start", "Start Vowel", Vowels, 0),
            ParamDescriptor.Choice("mid", "Mid Vowel", Vowels, 1),
            ParamDescriptor.Choice("end", "End Vowel", Vowels, 2),
            ParamDescriptor.Float("control", "Control", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("resonance", "Resonance", 1f, 30f, 8f, "Q", ParamTaper.Log),
            ParamDescriptor.Float("shift", "Shift", -12f, 12f, 0f, "st"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 2
    );

    static IReadOnlyList<string> Vowels => ["A", "E", "I", "O", "U"];

    /// <summary>
    ///     Formant <paramref name="formant" /> (0-based) in Hz for a morph position <paramref name="control" /> in [0, 1]
    ///     between the vowels at indices <paramref name="start" />, <paramref name="mid" /> and <paramref name="end" />,
    ///     before <see cref="Shift" />.
    /// </summary>
    public static float FormantFrequency(int formant, int start, int mid, int end, float control) {
        var (from, to, t) = control < 0.5f ? (start, mid, 2 * control) : (mid, end, 2 * control - 1);
        var a = VowelFormants[from][formant];
        var b = VowelFormants[to][formant];
        return a * MathF.Pow(b / a, t);
    }

    void Design(int length) {
        var control = Params.Advance(Control, length) / 100f;
        var q = Params.Advance(Resonance, length);
        var scale = MathF.Pow(2f, Params.Advance(Shift, length) / 12f);
        var start = (int)Params.Current(Start);
        var mid = (int)Params.Current(Mid);
        var end = (int)Params.Current(End);
        for (var f = 0; f < Formants; f++) {
            var hz = FormantFrequency(f, start, mid, end, control) * scale;
            coefficients[f] = SvfCoefficients.Design(BiquadType.BandPass, Spec.SampleRate, hz, q);
        }
    }

    protected override void OnReset() {
        Array.Clear(left);
        Array.Clear(right);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            Design(length);
            for (var i = start; i < start + length; i++) {
                var dryLeft = io.Left[i];
                var dryRight = io.Right[i];
                double wetLeft = 0, wetRight = 0;
                for (var f = 0; f < Formants; f++) {
                    wetLeft += FormantGains[f] * left[f].Tick(coefficients[f], dryLeft);
                    wetRight += FormantGains[f] * right[f].Tick(coefficients[f], dryRight);
                }

                var m = this.mix + mixStep * (i + 1);
                io.Left[i] = dryLeft + ((float)wetLeft - dryLeft) * m;
                io.Right[i] = dryRight + ((float)wetRight - dryRight) * m;
            }
        }

        for (var f = 0; f < Formants; f++) {
            left[f].FlushDenormals();
            right[f].FlushDenormals();
        }

        this.mix = mix;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
