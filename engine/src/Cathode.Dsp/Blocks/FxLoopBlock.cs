using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     An external effects loop through the audio interface's spare channels: the input goes out of the send channels to
///     a pedal and comes back on the return channels (<see cref="HardwareLoop" />). The round trip delays the return, so
///     the dry signal is delayed by the same <c>latency</c> and the graph compiler compensates parallel paths by it.
///     Measure the latency with the loopback test (send cabled to return) and enter it here; changes to it take effect
///     on the next preset load. With no return channels configured, the return is silent.
/// </summary>
public sealed class FxLoopBlock() : DspBlock(Info) {
    public const int Send = 0;
    public const int Return = 1;
    public const int Mix = 2;
    public const int Latency = 3;
    public const int Level = 4;

    /// <summary>Longest round trip the dry delay covers.</summary>
    public const float MaxLatencyMs = 100f;

    NativeBuffer delayLeft = NativeBuffer.Empty;
    NativeBuffer delayRight = NativeBuffer.Empty;
    int delayPosition;
    int latency;
    float returnGain = 1f, mix = 1f, outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "fxloop",
        "FX Loop",
        "Routing",
        [
            ParamDescriptor.Float("send", "Send Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("return", "Return Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("latency", "Loop Latency", 0f, MaxLatencyMs, 0f, "ms"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    /// <summary>Set by the graph compiler from the engine; null (tests, no device) means a silent return.</summary>
    public HardwareLoop? Port { get; set; }

    public override int LatencySamples => latency;

    protected override void OnPrepare(in ProcessSpec spec) {
        latency = (int)Math.Round(Params.GetTarget(Latency) * 0.001 * spec.SampleRate);
        delayLeft.Dispose();
        delayRight.Dispose();
        delayLeft = new(latency + 1);
        delayRight = new(latency + 1);
    }

    protected override void OnReset() {
        delayLeft.Clear();
        delayRight.Clear();
        delayPosition = 0;
        returnGain = Decibels.ToGain(Params.Current(Return));
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var sendGain = Decibels.ToGain(Params.Advance(Send, io.Length));
        var returnGain = Decibels.ToGain(Params.Advance(Return, io.Length));
        var mix = Params.Advance(Mix, io.Length) / 100f;
        Port?.Send(io.Left, io.Right, sendGain);

        // The dry path, delayed to line up with what comes back.
        var length = delayLeft.Length;
        var position = delayPosition;
        for (var i = 0; i < io.Length; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            delayLeft[position] = left;
            delayRight[position] = right;
            position = position + 1 == length ? 0 : position + 1;
            io.Left[i] = delayLeft[position];
            io.Right[i] = delayRight[position];
        }

        delayPosition = position;

        var hasReturn = Port is { HasReturn: true };
        var returnLeft = hasReturn ? Port!.ReturnLeft(io.Length) : default;
        var returnRight = hasReturn ? Port!.ReturnRight(io.Length) : default;
        var t = 0f;
        var step = 1f / io.Length;
        for (var i = 0; i < io.Length; i++) {
            t += step;
            var m = this.mix + (mix - this.mix) * t;
            var g = this.returnGain + (returnGain - this.returnGain) * t;
            var wetLeft = hasReturn ? returnLeft[i] * g : 0f;
            var wetRight = hasReturn ? returnRight[i] * g : 0f;
            io.Left[i] += (wetLeft - io.Left[i]) * m;
            io.Right[i] += (wetRight - io.Right[i]) * m;
        }

        this.returnGain = returnGain;
        this.mix = mix;
        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            delayLeft.Dispose();
            delayRight.Dispose();
        }

        base.Dispose(disposing);
    }
}
