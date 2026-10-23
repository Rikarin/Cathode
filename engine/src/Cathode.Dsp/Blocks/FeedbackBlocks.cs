using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The sending half of a feedback loop: its input goes to the preset's <see cref="FeedbackReturnBlock" /> one block
///     later (<see cref="FeedbackBus.Delay" />), so a later column can feed an earlier one, as on the hardware. It has no
///     output of its own. The graph compiler pairs the two and keeps the send alive even though no cable leaves it.
/// </summary>
public sealed class FeedbackSendBlock() : DspBlock(Info) {
    public const int Level = 0;

    float gain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "fbsend",
        "Feedback Send",
        "Routing",
        [ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 6f, 0f, "dB")],
        maxInstances: 1,
        channels: 1
    );

    /// <summary>Set by the graph compiler when the preset has a return; null means the signal goes nowhere.</summary>
    public FeedbackBus? Bus { get; set; }

    protected override void OnReset() => gain = Decibels.ToGain(Params.Current(Level));

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var gain = Decibels.ToGain(Params.Advance(Level, io.Length));
        Bus?.Write(io.Left, io.Right, 0.5f * (this.gain + gain));
        this.gain = gain;
        io.Left.Clear();
        io.Right.Clear();
    }
}

/// <summary>
///     The returning half of a feedback loop: adds what the <see cref="FeedbackSendBlock" /> sent one block ago to its
///     own input (it may have none). The returned signal passes a soft limit at +18 dBFS, so a loop with gain above one
///     saturates instead of running away.
/// </summary>
public sealed class FeedbackReturnBlock() : DspBlock(Info) {
    public const int Level = 0;

    const float Limit = 8f;

    NativeBuffer returnLeft = NativeBuffer.Empty;
    NativeBuffer returnRight = NativeBuffer.Empty;
    float gain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "fbreturn",
        "Feedback Return",
        "Routing",
        [ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 6f, 0f, "dB")],
        maxInstances: 1,
        channels: 1
    );

    /// <summary>Set by the graph compiler, which owns it through this block (disposed with it).</summary>
    public FeedbackBus? Bus { get; set; }

    protected override void OnPrepare(in ProcessSpec spec) {
        returnLeft.Dispose();
        returnRight.Dispose();
        returnLeft = new(spec.MaxBlockSize);
        returnRight = new(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        Bus?.Reset();
        gain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var gain = Decibels.ToGain(Params.Advance(Level, io.Length));
        if (Bus is { } bus) {
            var left = returnLeft.Span[..io.Length];
            var right = returnRight.Span[..io.Length];
            bus.Read(left, right);
            var g = this.gain;
            var step = (gain - g) / io.Length;
            for (var i = 0; i < io.Length; i++) {
                g += step;
                io.Left[i] += Limit * MathF.Tanh(left[i] * g / Limit);
                io.Right[i] += Limit * MathF.Tanh(right[i] * g / Limit);
            }
        }

        this.gain = gain;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Bus?.Dispose();
            returnLeft.Dispose();
            returnRight.Dispose();
        }

        base.Dispose(disposing);
    }
}
