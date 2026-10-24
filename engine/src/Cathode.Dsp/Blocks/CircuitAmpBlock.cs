using Cathode.Dsp.Amp;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The circuit tier of the Amp (docs/04 Phase 7): complete tube amps simulated from their schematics with the stage
///     library in <see cref="Cathode.Dsp.Amp" />: triode stages and cathode followers as wave digital filters with
///     nonlinear R-type roots, exact passive tone stacks and general passive networks, a long-tail-pair or cathodyne
///     phase inverter, push-pull (or single-ended) pentodes into an output transformer and speaker load with global
///     negative feedback, and the power supply's sag. Mono in (left, right or both), the 8 Ω speaker voltage out on both
///     channels; no cabinet (follow it with a Cab block). The models are <see cref="AmpDesign.All" />.
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
///         Front panel: Gain, Bass, Middle, Treble, Presence, Depth, Cut, Master and Bright, and the model-specific
///         <b>Switch 1-3</b> and <b>Control 1-2</b> and <b>EQ 1-5</b>, which each model names in its
///         <see cref="AmpPanel" /> (a Fat switch, a lead drive, the bands of a Mark-style EQ); the descriptor carries the
///         names per type (<see cref="ParamDescriptor.TypeLabels" />), and a control a model lacks is hidden and does
///         nothing.
///     </para>
///     <para>
///         Expert parameters, each "stock" at its default, so a preset made before they existed sounds the same:
///         <b>Bias</b> moves the output tubes' idle current (fixed bias: the bias voltage from 1.2x to 0.8x of stock;
///         cathode bias: the cathode resistor from 2x to 0.5x), <b>Sag</b> the supply's source resistance (0.25x to 4x
///         of stock), <b>Feedback</b> the global negative feedback (5 is stock, 0 none, 10 twice stock; the AC30 has
///         none up to 5, then a Plexi-like amount by 10), <b>Bright</b> the bright cap across the gain pot. Then, after
///         Level (Axe-Fx III parity, docs/06 §1.4):
///     </para>
///     <list type="bullet">
///         <item><b>Input Trim</b> (dB, 0): gain at the input jack, no tone change.</item>
///         <item><b>Input Select</b> (Left): which input the mono amp takes: left, right, or their average.</item>
///         <item><b>Low Cut</b> (Hz, 0 = off): a first-order high-pass before the first stage ("tightening").</item>
///         <item>
///             <b>Preamp Tube</b> (Stock): every 12AX7 of the preamp replaced by a 12AX7, 12AY7, 5751, 12AT7 or 12AU7
///             (the phase inverter keeps its own).
///         </item>
///         <item><b>Bright Cap</b> (100 %): the bright caps' values.</item>
///         <item>
///             <b>Preamp Bias</b> (0 %): the last gain stage's cathode resistor, from twice (-100 %, colder) to half
///             (+100 %, hotter) of stock: its operating point and so its even harmonics.
///         </item>
///         <item><b>Preamp Sag</b> (100 %): how much of the supply's sag reaches the preamp (0 % a stiff preamp supply).</item>
///         <item><b>Stack Freq</b> (100 %): the tone stack's frequencies (its caps divided by it).</item>
///         <item><b>Master Trim</b> (100 %): scales the master's rotation, so the master reaches full earlier or never.</item>
///         <item>
///             <b>Power Amp</b> (on): off leaves the power amp out, for a real tube power amp; the output is then the
///             phase inverter's input (1.0 is 50 V, less the model's trim).
///         </item>
///         <item>
///             <b>Power Tube</b> (Stock): 6L6GC, EL34, EL84, 6V6GT, KT66, KT88, 6550 or 5881 (6L6GC curves) in place of
///             the stock tubes, rebiased to the design's idle current.
///         </item>
///         <item><b>Xfmr Match</b> (100 %): the output transformer's primary impedance (its impedance ratio).</item>
///         <item><b>Xfmr Low Freq</b>, <b>Xfmr High Freq</b> (100 %): its bandwidth (magnetising; leakage and capacitance).</item>
///         <item>
///             <b>Xfmr Drive</b> (0): core saturation beyond stock, the knee's flux halving every 2: low notes at full
///             power saturate the core.
///         </item>
///         <item>
///             <b>Speaker</b> (Stock): an impedance curve preset: open-back 1x12, 2x12 or 4x10, closed 4x12, closed 8x10.
///         </item>
///         <item>
///             <b>LF Resonance</b>, <b>LF Q</b>, <b>LF Amount</b> (100 %): the speaker's resonance frequency, sharpness and
///             peak height; <b>Speaker HF</b> (100 %) its voice-coil inductance; <b>Speaker Imp</b> (100 %) the whole
///             load against the tap (50 % is a 4 Ω cab on the 8 Ω tap).
///         </item>
///         <item><b>Presence Freq</b>, <b>Depth Freq</b> (100 %): the presence and depth controls' frequencies.</item>
///         <item><b>Variac</b> (100 %): the mains voltage: every supply node, and a fixed bias with them ("brown sound").</item>
///         <item><b>B+ Time Const</b> (100 %): the reservoir cap: how fast the supply sags and recovers.</item>
///         <item><b>Supply Type</b> (Stock): an ideal DC source, or rectified 60 Hz or 50 Hz mains with ripple.</item>
///         <item>
///             <b>Rectifier</b> (Stock): silicon, or a GZ34, 5U4, 5Y3 or EZ81 with its drop rising with the current. A
///             model's panel may switch its rectifier or mains tap too (<see cref="SupplySwitchDesign" />: Mesa's
///             rectifier and Bold/Spongy switches); this choice and the Variac apply on top.
///         </item>
///         <item><b>Cathode Time Const</b> (100 %): a cathode-biased power amp's bypass cap.</item>
///     </list>
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
    public const int Switch1 = 14;
    public const int Switch2 = 15;
    public const int Switch3 = 16;
    public const int Control1 = 17;
    public const int Control2 = 18;
    public const int Eq1 = 19; // to Eq1 + 4
    public const int InputTrim = 24;
    public const int InputSelect = 25;
    public const int LowCut = 26;
    public const int PreampTube = 27;
    public const int BrightCap = 28;
    public const int PreampBias = 29;
    public const int PreampSag = 30;
    public const int StackFrequency = 31;
    public const int MasterTrim = 32;
    public const int PowerAmpModel = 33;
    public const int PowerTube = 34;
    public const int TransformerMatch = 35;
    public const int TransformerLow = 36;
    public const int TransformerHigh = 37;
    public const int TransformerDrive = 38;
    public const int SpeakerCurve = 39;
    public const int SpeakerResonance = 40;
    public const int SpeakerQ = 41;
    public const int SpeakerPeak = 42;
    public const int SpeakerHigh = 43;
    public const int SpeakerImpedance = 44;
    public const int PresenceFrequency = 45;
    public const int DepthFrequency = 46;
    public const int Variac = 47;
    public const int SupplyTime = 48;
    public const int SupplyType = 49;
    public const int Rectifier = 50;
    public const int CathodeTime = 51;

    /// <summary>Guitar volts per unit of input.</summary>
    const double InputVolts = 1;

    /// <summary>The phase inverter's input volts at full scale, with the power amp off.</summary>
    const double PreampFullScale = 50;

    /// <summary>The Preamp Tube choice after Stock.</summary>
    static readonly TriodeModel?[] PreampTubes = [
        null,
        TriodeModel.Ecc83,
        TriodeModel.TwelveAY7,
        TriodeModel.FiveSevenFiveOne,
        TriodeModel.Ecc81,
        TriodeModel.Ecc82
    ];

    /// <summary>The Power Tube choice after Stock.</summary>
    static readonly PentodeModel?[] PowerTubes = [
        null,
        PentodeModel.SixL6GC,
        PentodeModel.EL34,
        PentodeModel.EL84,
        PentodeModel.SixV6GT,
        PentodeModel.KT66,
        PentodeModel.KT88,
        PentodeModel.SixtyFiveFifty,
        PentodeModel.SixL6GC
    ];

    readonly AmpChannel[] channels;
    readonly Oversampler2x oversampler = new(1);
    AmpChannel active;
    int activeType = -1;
    int factor = 2;
    double outputScale = 1;
    double preampScale = 1;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "circuitamp",
        "Amp (circuit)",
        "Amp",
        Labelled([
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
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Switch("switch1", "Switch 1"),
            ParamDescriptor.Switch("switch2", "Switch 2"),
            ParamDescriptor.Switch("switch3", "Switch 3"),
            ParamDescriptor.Float("control1", "Control 1", 0f, 10f, 5f),
            ParamDescriptor.Float("control2", "Control 2", 0f, 10f, 5f),
            ParamDescriptor.Float("eq1", "EQ 1", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("eq2", "EQ 2", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("eq3", "EQ 3", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("eq4", "EQ 4", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("eq5", "EQ 5", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("intrim", "Input Trim", -20f, 20f, 0f, "dB"),
            ParamDescriptor.Choice("inputsel", "Input Select", ["Left", "Right", "Left + Right"]),
            ParamDescriptor.Float("lowcut", "Low Cut", 0f, 500f, 0f, "Hz"),
            ParamDescriptor.Choice("pretube", "Preamp Tube", ["Stock", "12AX7", "12AY7", "5751", "12AT7", "12AU7"]),
            Expert(ParamDescriptor.Float("brightcap", "Bright Cap", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("prebias", "Preamp Bias", -100f, 100f, 0f, "%")),
            ParamDescriptor.Float("presag", "Preamp Sag", 0f, 200f, 100f, "%"),
            Expert(ParamDescriptor.Float("stackfreq", "Stack Freq", 50f, 200f, 100f, "%", ParamTaper.Log)),
            ParamDescriptor.Float("mvtrim", "Master Trim", 10f, 200f, 100f, "%"),
            ParamDescriptor.Switch("poweramp", "Power Amp", true),
            ParamDescriptor.Choice(
                "powertube",
                "Power Tube",
                ["Stock", "6L6GC", "EL34", "EL84", "6V6GT", "KT66", "KT88", "6550", "5881"]
            ),
            Expert(ParamDescriptor.Float("xfmrmatch", "Xfmr Match", 50f, 200f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("xfmrlf", "Xfmr Low Freq", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("xfmrhf", "Xfmr High Freq", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("xfmrdrive", "Xfmr Drive", 0f, 10f, 0f)),
            ParamDescriptor.Choice(
                "speaker",
                "Speaker",
                ["Stock", "1x12 Open", "2x12 Open", "4x10 Open", "4x12 Closed", "8x10 Closed"]
            ),
            Expert(ParamDescriptor.Float("spkres", "LF Resonance", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("spkq", "LF Q", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("spkpeak", "LF Amount", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("spkhf", "Speaker HF", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Expert(ParamDescriptor.Float("spkimp", "Speaker Imp", 50f, 200f, 100f, "%", ParamTaper.Log)),
            ParamDescriptor.Float("presfreq", "Presence Freq", 25f, 400f, 100f, "%", ParamTaper.Log),
            ParamDescriptor.Float("depthfreq", "Depth Freq", 25f, 400f, 100f, "%", ParamTaper.Log),
            ParamDescriptor.Float("variac", "Variac", 50f, 130f, 100f, "%"),
            Expert(ParamDescriptor.Float("btc", "B+ Time Const", 25f, 400f, 100f, "%", ParamTaper.Log)),
            ParamDescriptor.Choice("supply", "Supply Type", ["Stock", "DC", "AC 60 Hz", "AC 50 Hz"]),
            ParamDescriptor.Choice("rectifier", "Rectifier", ["Stock", "Silicon", "GZ34", "5U4", "5Y3", "EZ81"]),
            Expert(ParamDescriptor.Float("katc", "Cathode Time Const", 25f, 400f, 100f, "%", ParamTaper.Log))
        ]),
        maxInstances: 2
    );

    static CircuitAmpBlock() {
        // Every tube table a setting can ask for, built here (control thread), so that changing one on the audio
        // thread only looks it up.
        foreach (var tube in PreampTubes) {
            if (tube is { } model) {
                TriodeTables.For(model);
            }
        }

        foreach (var tube in PowerTubes) {
            if (tube is { } model) {
                PentodeTables.For(model);
            }
        }
    }

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

        var select = (int)Params.Current(InputSelect);
        if (select == 1) {
            io.Right.CopyTo(io.Left);
        } else if (select == 2) {
            for (var i = 0; i < io.Length; i++) {
                io.Left[i] = 0.5f * (io.Left[i] + io.Right[i]);
            }
        }

        var inner = factor == 2 ? oversampler.Up(io.Left) : io.Left;
        var channel = active;
        var scale = channel.PowerAmpEnabled ? outputScale : preampScale;
        var trim = Decibels.ToGain(Params.Advance(InputTrim, io.Length));
        for (var i = 0; i < inner.Length; i++) {
            var y = channel.Tick(inner[i] * InputVolts * trim) * scale;
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

    /// <summary>The descriptor's parameters with each model's panel names (<see cref="ParamDescriptor.TypeLabels" />).</summary>
    static IReadOnlyList<ParamDescriptor> Labelled(IReadOnlyList<ParamDescriptor> parameters) =>
        [
            ..parameters.Select(static p => {
                    string[] labels = [..AmpDesign.All.Select(a => a.Panel.Label(p.Id, p.Name) ?? "")];
                    return labels.All(l => l == p.Name) ? p : p with { TypeLabels = labels };
                }
            )
        ];

    /// <summary>A component value: applied as a step (it may recompute a circuit), not ramped.</summary>
    static ParamDescriptor Expert(ParamDescriptor descriptor) => descriptor with { Smoothing = ParamSmoothing.Step };

    /// <summary>Switches to amp <paramref name="type" />, which starts at its operating point.</summary>
    void Select(int type) {
        activeType = type;
        active = channels[Math.Clamp(type, 0, channels.Length - 1)];
        var design = active.Design;
        var power = design.Power;
        var trim = Decibels.ToGain(design.OutputTrimDb);
        outputScale = trim * power.Transformer.TurnsRatio / (2 * power.Supply);
        preampScale = trim / PreampFullScale;
        Configure(0);
        active.Reset();
    }

    /// <summary>The knobs, advanced over <paramref name="frames" />, applied to the active amp.</summary>
    void Configure(int frames) {
        static double Knob(float value) => value / 10.0;
        static double Percent(float value) => value / 100.0;

        var design = active.Design;
        var switch1 = Params.Current(Switch1) >= 0.5f;
        var switch2 = Params.Current(Switch2) >= 0.5f;
        var switch3 = Params.Current(Switch3) >= 0.5f;
        var preamp = new AmpControls(
            Knob(Params.Advance(Gain, frames)),
            Knob(Params.Advance(Bass, frames)),
            Knob(Params.Advance(Middle, frames)),
            Knob(Params.Advance(Treble, frames)),
            Math.Min(Knob(Params.Advance(Master, frames)) * Percent(Params.Advance(MasterTrim, frames)), 1),
            Params.Current(Bright) >= 0.5f,
            Knob(Params.Advance(Control1, frames)),
            Knob(Params.Advance(Control2, frames)),
            switch1,
            switch2,
            switch3,
            new(
                Params.Advance(Eq1, frames),
                Params.Advance(Eq1 + 1, frames),
                Params.Advance(Eq1 + 2, frames),
                Params.Advance(Eq1 + 3, frames),
                Params.Advance(Eq1 + 4, frames)
            )
        );
        var power = new PowerControls(
            Knob(Params.Advance(Presence, frames)),
            Knob(Params.Advance(Depth, frames)),
            Knob(Params.Advance(Cut, frames)),
            Knob(Params.Advance(Sag, frames)),
            Params.Advance(Bias, frames) / 100.0,
            Knob(Params.Advance(Feedback, frames))
        );

        var preampSettings = new PreampSettings(
            PreampTubes[Math.Clamp((int)Params.Current(PreampTube), 0, PreampTubes.Length - 1)],
            Percent(Params.Current(BrightCap)),
            Percent(Params.Current(StackFrequency)),
            Math.Pow(2, -Percent(Params.Current(PreampBias)))
        );
        var speaker = (int)Params.Current(SpeakerCurve);
        var rectifier = (int)Params.Current(Rectifier);
        var variac = Percent(Params.Advance(Variac, frames));
        var switched = default(RectifierModel);
        if (design.Power.SupplySwitches is { } supplySwitches) {
            for (var i = 0; i < supplySwitches.Count; i++) {
                if (preamp.IsOn(supplySwitches[i].Role)) {
                    switched = supplySwitches[i].Rectifier ?? switched;
                    variac *= supplySwitches[i].Variac;
                }
            }
        }

        var powerSettings = new PowerSettings(
            PowerTubes[Math.Clamp((int)Params.Current(PowerTube), 0, PowerTubes.Length - 1)],
            Percent(Params.Current(TransformerMatch)),
            Percent(Params.Current(TransformerLow)),
            Percent(Params.Current(TransformerHigh)),
            Params.Current(TransformerDrive),
            speaker > 0 ? SpeakerDesign.Presets[Math.Min(speaker, SpeakerDesign.Presets.Count) - 1] : null,
            Percent(Params.Current(SpeakerResonance)),
            Percent(Params.Current(SpeakerQ)),
            Percent(Params.Current(SpeakerPeak)),
            Percent(Params.Current(SpeakerHigh)),
            Percent(Params.Current(SpeakerImpedance)),
            Percent(Params.Advance(PresenceFrequency, frames)),
            Percent(Params.Advance(DepthFrequency, frames)),
            variac,
            Percent(Params.Current(SupplyTime)),
            (SupplyMode)Math.Clamp((int)Params.Current(SupplyType), 0, 3),
            rectifier > 0 ? RectifierModel.All[Math.Min(rectifier, RectifierModel.All.Count) - 1] : switched,
            Percent(Params.Current(CathodeTime)),
            design.Power.TriodeSwitch is { } role && preamp.IsOn(role)
        );
        var channelSettings = new ChannelSettings(
            Params.Advance(LowCut, frames),
            Percent(Params.Advance(PreampSag, frames)),
            Params.Current(PowerAmpModel) >= 0.5f
        );

        active.Apply(preampSettings, powerSettings, channelSettings);
        active.Configure(preamp, power);
    }
}
