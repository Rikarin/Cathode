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
///     </list>
///     Modern and Classic add the derived side as <c>L + s</c>, <c>R - s</c>, so the mono sum L + R is untouched. The
///     side is band-limited by <b>Low Cut</b> (keeping the bass centred) and <b>High Cut</b>.
/// </summary>
public sealed class EnhancerBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Width = 1;
    public const int Time = 2;
    public const int LowCut = 3;
    public const int HighCut = 4;
    public const int Level = 5;

    const double MaxTimeMs = 30;
    const double AllpassQ = 0.6;

    /// <summary>Centre frequencies of the decorrelating allpasses: spread over the guitar range.</summary>
    static readonly double[] AllpassFrequencies = [220, 760, 2_300, 6_400];

    readonly SvfState[] allpasses = new SvfState[AllpassFrequencies.Length];
    readonly SvfCoefficients[] allpassCoefficients = new SvfCoefficients[AllpassFrequencies.Length];
    readonly FractionalDelayLine delay = new();
    SvfState lowCut;
    SvfState highCut;
    float width;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "enhancer",
        "Enhancer",
        "Utility",
        [
            ParamDescriptor.Choice("type", "Type", ["Modern", "Classic", "Haas"]),
            ParamDescriptor.Float("width", "Width", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("time", "Time", 1f, (float)MaxTimeMs, 8f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("lowCut", "Low Cut", 20f, 2_000f, 150f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 1_000f, 20_000f, 12_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    protected override void OnPrepare(in ProcessSpec spec) {
        delay.Allocate((int)Math.Ceiling(MaxTimeMs * 0.001 * spec.SampleRate) + 8);
        for (var i = 0; i < AllpassFrequencies.Length; i++) {
            // An SVF allpass is x - 2k bp; built on the low-pass design's integrator coefficients.
            var lowPass = SvfCoefficients.Design(BiquadType.LowPass, spec.SampleRate, AllpassFrequencies[i], AllpassQ);
            allpassCoefficients[i] = lowPass with { M0 = 1, M1 = -2 / AllpassQ, M2 = 0 };
        }
    }

    protected override void OnReset() {
        Array.Clear(allpasses);
        delay.Reset();
        lowCut.Reset();
        highCut.Reset();
        width = Params.Current(Width) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var width = Params.Advance(Width, io.Length) / 100f;
        var widthStep = (width - this.width) / io.Length;
        var time = (float)(Params.Advance(Time, io.Length) * 0.001 * rate);
        var lowCutHz = Params.Advance(LowCut, io.Length);
        var highCutHz = Params.Advance(HighCut, io.Length);
        var lowCutCoefficients = SvfCoefficients.Design(BiquadType.HighPass, rate, lowCutHz, 0.7071);
        var highCutCoefficients = SvfCoefficients.Design(BiquadType.LowPass, rate, highCutHz, 0.7071);

        for (var i = 0; i < io.Length; i++) {
            var w = this.width + widthStep * (i + 1);
            var l = io.Left[i];
            var r = io.Right[i];
            if (type == 2) {
                delay.Write(r);
                io.Right[i] = r + (delay.Read(time) - r) * w;
                continue;
            }

            var mid = 0.5 * (l + r);
            double derived;
            if (type == 0) {
                derived = mid;
                for (var a = 0; a < allpasses.Length; a++) {
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

        for (var a = 0; a < allpasses.Length; a++) {
            allpasses[a].FlushDenormals();
        }

        lowCut.FlushDenormals();
        highCut.FlushDenormals();
        this.width = width;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            delay.Dispose();
        }

        base.Dispose(disposing);
    }
}
