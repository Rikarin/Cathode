using Cathode.Dsp.Amp;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The circuit tier of the Amp (docs/04 Phase 7): complete tube amps simulated from their schematics with the stage
///     library in <see cref="Cathode.Dsp.Amp" />: triode stages and cathode followers as wave digital filters with
///     nonlinear R-type roots, exact passive tone stacks, a long-tail-pair phase inverter, push-pull pentodes into an
///     output transformer and speaker load with global negative feedback, and power-supply sag. Mono in (left), the
///     8 Ω speaker voltage out on both channels; no cabinet (follow it with a Cab block).
/// </summary>
/// <remarks>
///     <para>
///         Everything runs at twice the sample rate (at rates of 64 kHz and up, at the sample rate): every stage,
///         linear ones included, sits between nonlinear ones, so splitting rates would cost more in resampling than it
///         saves. The power amp's phase inverter, feedback and grid networks are folded into one scalar Newton solve
///         (see <see cref="PowerAmp" />).
///     </para>
///     <para>
///         1.0 at the input is 1 V at the guitar jack; full scale at the output is the largest swing the output tubes
///         can make (each plate from B+ down to 0 V, the other up to twice B+), less a few decibels of trim per model,
///         so a power amp driven into clipping peaks a few decibels under full scale.
///     </para>
///     <para>
///         Expert parameters: <b>Bias</b> moves the output tubes' idle current (fixed bias: the bias voltage from 1.2x
///         to 0.8x of stock; cathode bias: the cathode resistor from 2x to 0.5x), <b>Sag</b> the supply's source
///         resistance (0.25x to 4x of stock), <b>Feedback</b> the global negative feedback (5 is stock, 0 none, 10
///         twice stock; the AC30 has none up to 5, then a Plexi-like amount by 10), <b>Bright</b> the bright cap
///         across the gain pot.
///     </para>
/// </remarks>
public sealed class CircuitAmpBlock : DspBlock {
    public const int Type = 0;
    public const int Gain = 1;
    public const int Bass = 2;
    public const int Middle = 3;
    public const int Treble = 4;
    public const int Presence = 5;
    public const int Depth = 6;
    public const int Cut = 7;
    public const int Master = 8;
    public const int Sag = 9;
    public const int Bias = 10;
    public const int Feedback = 11;
    public const int Bright = 12;
    public const int Level = 13;

    /// <summary>Guitar volts per unit of input.</summary>
    const double InputVolts = 1;

    readonly AmpChannel[] channels;
    readonly Oversampler2x oversampler = new(1);
    AmpChannel active;
    int activeType = -1;
    int factor = 2;
    double outputScale = 1;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "circuitamp",
        "Amp (circuit)",
        "Amp",
        [
            ParamDescriptor.Choice("type", "Type", [..AmpDesign.All.Select(static a => a.Name)]),
            ParamDescriptor.Float("gain", "Gain", 0f, 10f, 5f),
            ParamDescriptor.Float("bass", "Bass", 0f, 10f, 5f),
            ParamDescriptor.Float("middle", "Middle", 0f, 10f, 5f),
            ParamDescriptor.Float("treble", "Treble", 0f, 10f, 5f),
            ParamDescriptor.Float("presence", "Presence", 0f, 10f, 5f),
            ParamDescriptor.Float("depth", "Depth", 0f, 10f, 0f),
            ParamDescriptor.Float("cut", "Cut", 0f, 10f, 0f),
            ParamDescriptor.Float("master", "Master", 0f, 10f, 10f),
            ParamDescriptor.Float("sag", "Sag", 0f, 10f, 5f),
            ParamDescriptor.Float("bias", "Bias", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("feedback", "Feedback", 0f, 10f, 5f),
            ParamDescriptor.Switch("bright", "Bright"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public CircuitAmpBlock() : base(Info) {
        channels = [..AmpDesign.All.Select(static a => new AmpChannel(a, 96_000))];
        active = channels[0];
    }

    /// <summary>The active amp's channel (for tests and diagnostics).</summary>
    public AmpChannel Channel => active;

    /// <summary>The internal processing rate as a multiple of the sample rate.</summary>
    public int Oversampling => factor;

    protected override void OnPrepare(in ProcessSpec spec) {
        factor = spec.SampleRate < 64_000 ? 2 : 1;
        oversampler.Resize(spec.MaxBlockSize);
        foreach (var channel in channels) {
            channel.SetSampleRate(spec.SampleRate * factor);
        }
    }

    protected override void OnReset() {
        oversampler.Reset();
        activeType = -1;
        Select((int)Params.Current(Type));
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        if (type != activeType) {
            Select(type);
        } else {
            Configure(io.Length);
        }

        var inner = factor == 2 ? oversampler.Up(io.Left) : io.Left;
        var channel = active;
        var scale = outputScale;
        for (var i = 0; i < inner.Length; i++) {
            var y = channel.Tick(inner[i] * InputVolts) * scale;
            inner[i] = Math.Abs(y) < 1e-30 ? 0f : (float)y;
        }

        if (factor == 2) {
            oversampler.Down(io.Left);
            oversampler.FlushDenormals();
        }

        channel.FlushDenormals();
        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        this.outputGain = outputGain;
        io.Left.CopyTo(io.Right);
    }

    /// <summary>Switches to amp <paramref name="type" />, which starts at its operating point.</summary>
    void Select(int type) {
        activeType = type;
        active = channels[Math.Clamp(type, 0, channels.Length - 1)];
        var power = active.Design.Power;
        outputScale = Decibels.ToGain(active.Design.OutputTrimDb) * power.Transformer.TurnsRatio / (2 * power.Supply);
        Configure(0);
        active.Reset();
    }

    /// <summary>The knobs, advanced over <paramref name="frames" />, applied to the active amp.</summary>
    void Configure(int frames) {
        static double Knob(float value) => value / 10.0;

        var preamp = new AmpControls(
            Knob(Params.Advance(Gain, frames)),
            Knob(Params.Advance(Bass, frames)),
            Knob(Params.Advance(Middle, frames)),
            Knob(Params.Advance(Treble, frames)),
            Knob(Params.Advance(Master, frames)),
            Params.Current(Bright) >= 0.5f
        );
        var power = new PowerControls(
            Knob(Params.Advance(Presence, frames)),
            Knob(Params.Advance(Depth, frames)),
            Knob(Params.Advance(Cut, frames)),
            Knob(Params.Advance(Sag, frames)),
            Params.Advance(Bias, frames) / 100.0,
            Knob(Params.Advance(Feedback, frames))
        );
        active.Configure(preamp, power);
    }
}
