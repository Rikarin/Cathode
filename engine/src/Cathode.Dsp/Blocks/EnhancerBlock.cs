using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Stereo enhancer for a mono (or narrow) source.
///     <list type="bullet">
///         <item>
///             <b>Modern</b>: a side signal is derived from the mid through a chain of allpasses, whose phase rotates
///             across the spectrum, so frequency regions spread alternately left and right (decorrelation).
///         </item>
///         <item>
///             <b>Classic</b>: the side is the mid delayed by <b>Time</b>, giving complementary comb filters on the two
///             sides.
///         </item>
///         <item><b>Haas</b>: the right channel is delayed by <b>Time</b> (precedence effect). Not mono compatible.</item>
///         <item>
///             <b>Stereoizer</b>: as Modern, through a high-order chain of <see cref="StereoizerStages" /> allpasses at
///             third-octave spacing, so the phase turns many times across the spectrum and narrow bands alternate left
///             and right (complementary filtering).
///         </item>
///     </list>
///     <b>Depth</b> (Modern, Stereoizer) is the allpasses' sharpness: higher depth turns the phase more abruptly, for
///     stronger, narrower alternations; 50 % is the original Modern voicing.
///     Modern, Classic and Stereoizer add the derived side as <c>L + s</c>, <c>R - s</c>, so the mono sum L + R is
///     untouched. The
///     side is band-limited by <b>Low Cut</b> (keeping the bass centred) and <b>High Cut</b>. Then either side can be
///     phase-inverted and each panned, as a channel converter (merge to mono, swap or narrow).
/// </summary>
public sealed class EnhancerBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Width = 1;
    public const int Time = 2;
    public const int LowCut = 3;
    public const int HighCut = 4;
    public const int Level = 5;
    public const int Balance = 6;
    public const int Invert = 7;
    public const int PanLeft = 8;
    public const int PanRight = 9;
    public const int Depth = 10;

    public const int Modern = 0;
    public const int Classic = 1;
    public const int Haas = 2;
    public const int Stereoizer = 3;

    /// <summary>Allpass stages of the Stereoizer.</summary>
    public const int StereoizerStages = 24;

    const double MaxTimeMs = 30;
    const double AllpassQ = 0.6;

    /// <summary>Centre frequencies of the decorrelating allpasses: spread over the guitar range.</summary>
    static readonly double[] AllpassFrequencies = [220, 760, 2_300, 6_400];

    /// <summary>The Stereoizer's allpasses: third-octave steps from 100 Hz.</summary>
    static readonly double[] StereoizerFrequencies =
        [..Enumerable.Range(0, StereoizerStages).Select(static i => 100 * Math.Pow(2, i / 3.0))];

    readonly SvfState[] allpasses = new SvfState[StereoizerStages];
    readonly SvfCoefficients[] allpassCoefficients = new SvfCoefficients[StereoizerStages];
    int designedType = -1;
    float designedDepth = float.NaN;
    readonly FractionalDelayLine delay = new();
    SvfState lowCut;
    SvfState highCut;
    float width;
    SampleGlide timeGlide;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "enhancer",
        "Enhancer",
        "Utility",
        [
            ParamDescriptor.Choice("type", "Type", ["Modern", "Classic", "Haas", "Stereoizer"]),
            ParamDescriptor.Float("width", "Width", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("time", "Time", 1f, (float)MaxTimeMs, 8f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("lowCut", "Low Cut", 20f, 2_000f, 150f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 1_000f, 20_000f, 12_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("invert", "Invert", ChorusBlock.PhaseOptions),
            ParamDescriptor.Float("panLeft", "Pan Left", -100f, 100f, -100f, "%"),
            ParamDescriptor.Float("panRight", "Pan Right", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 50f, "%") with {
                TypeLabels = ["Depth", string.Empty, string.Empty, "Depth"]
            }
        ],
        maxInstances: 2
    );

    /// <summary>
    ///     The allpasses' Q for <paramref name="depth" /> (0..1): a quarter to four times the Modern voicing's, 0.5 is
    ///     it.
    /// </summary>
    public static double AllpassQFor(float depth) => depth == 0.5f ? AllpassQ : AllpassQ * Math.Pow(4, 2 * depth - 1);

    /// <summary>Designs the allpass chain for <paramref name="type" /> and <paramref name="depth" /> when they change.</summary>
    int DesignAllpasses(int type, float depth) {
        var frequencies = type == Stereoizer ? StereoizerFrequencies : AllpassFrequencies;
        if (type == designedType && depth == designedDepth) {
            return frequencies.Length;
        }

        if (type != designedType) {
            Array.Clear(allpasses);
        }

        designedType = type;
        designedDepth = depth;
        var q = AllpassQFor(depth);
        for (var i = 0; i < frequencies.Length; i++) {
            // An SVF allpass is x - 2k bp; built on the low-pass design's integrator coefficients.
            var lowPass = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequencies[i], q);
            allpassCoefficients[i] = lowPass with { M0 = 1, M1 = -2 / q, M2 = 0 };
        }

        return frequencies.Length;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        delay.Allocate((int)Math.Ceiling(MaxTimeMs * 0.001 * spec.SampleRate) + 8);
        designedType = -1;
    }

    protected override void OnReset() {
        Array.Clear(allpasses);
        designedType = -1;
        delay.Reset();
        lowCut.Reset();
        highCut.Reset();
        timeGlide.Reset();
        width = Params.Current(Width) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var width = Params.Advance(Width, io.Length) / 100f;
        var widthStep = (width - this.width) / io.Length;
        timeGlide.Begin((float)(Params.Advance(Time, io.Length) * 0.001 * rate), io.Length); // glides, never steps
        var lowCutHz = Params.Advance(LowCut, io.Length);
        var highCutHz = Params.Advance(HighCut, io.Length);
        var lowCutCoefficients = SvfCoefficients.Design(BiquadType.HighPass, rate, lowCutHz, 0.7071);
        var highCutCoefficients = SvfCoefficients.Design(BiquadType.LowPass, rate, highCutHz, 0.7071);
        var stages = type is Modern or Stereoizer ? DesignAllpasses(type, Params.Advance(Depth, io.Length) / 100f) : 0;

        for (var i = 0; i < io.Length; i++) {
            var w = this.width + widthStep * (i + 1);
            var time = timeGlide.At(i);
            var l = io.Left[i];
            var r = io.Right[i];
            if (type == Haas) {
                delay.Write(r);
                io.Right[i] = r + (delay.Read(time) - r) * w;
                continue;
            }

            var mid = 0.5 * (l + r);
            double derived;
            if (type != Classic) {
                derived = mid;
                for (var a = 0; a < stages; a++) {
                    derived = allpasses[a].Tick(allpassCoefficients[a], derived);
                }
            } else {
                delay.Write((float)mid);
                derived = delay.Read(time);
            }

            var side = w * highCut.Tick(highCutCoefficients, lowCut.Tick(lowCutCoefficients, derived));
            io.Left[i] = (float)(l + side);
            io.Right[i] = (float)(r - side);
        }

        for (var a = 0; a < stages; a++) {
            allpasses[a].FlushDenormals();
        }

        lowCut.FlushDenormals();
        highCut.FlushDenormals();
        this.width = width;
        Stereo.Invert(io, (int)Params.Current(Invert));
        Stereo.Pan(io, Params.Advance(PanLeft, io.Length) / 100f, Params.Advance(PanRight, io.Length) / 100f);
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            delay.Dispose();
        }

        base.Dispose(disposing);
    }
}
