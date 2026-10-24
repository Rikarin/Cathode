using Cathode.Dsp.Amp;
using Cathode.Dsp.Filters;
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
///         A solid-state model (<see cref="AmpDesign.SolidState" />, the JC-120) runs a <see cref="SolidStatePowerAmp" />
///         instead: full scale is its rails less their headroom, and its panel hides the tube-only parameters.
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
///     <para>
///         Then the stages around the circuit (docs/08 §4), each off or neutral at its default and skipped while it is
///         (helpers in <c>Amp/AmpInserts.cs</c>), all at the inner rate:
///     </para>
///     <list type="bullet">
///         <item><b>Input Dynamics</b> (0 %): expands (positive) or compresses (negative) the input by its envelope.</item>
///         <item>
///             <b>Boost Type</b> (Off), <b>Boost Drive</b>, <b>Boost Tone</b>, <b>Boost Level</b>: a drive pedal from
///             <see cref="DriveCircuits" /> in front of the amp (<see cref="AmpBoost" />).
///         </item>
///         <item>
///             <b>Input Filter</b> (Off: shelves, peak, high- or low-pass) with its frequency, Q and gain, and
///             <b>Definition</b> (0 %), a treble-versus-bass tilt into the preamp (<see cref="AmpInputFilter" />).
///         </item>
///         <item>
///             <b>Saturation</b> (off) and <b>Saturation Drive</b>: a diode-style soft clipper between the preamp and the
///             power amp (<see cref="AmpSaturation" />).
///         </item>
///         <item>
///             <b>Output EQ</b> (Off, or a <see cref="GraphicEqualizer" /> layout) with ten band gains, at the
///             <b>Output EQ Location</b>: the output (default), before the power amp, or the input.
///         </item>
///         <item>
///             <b>Speaker Drive</b>, <b>Speaker Thump</b> at <b>Cab Resonance</b>, <b>Speaker Compression</b> with its
///             time (all 0 = off; <see cref="SpeakerStage" />).
///         </item>
///         <item>
///             <b>Output Comp</b> (Off, Output, Feedback) with amount, threshold and clarity (<see cref="AmpCompressor" />).
///         </item>
///         <item><b>Bias Trem Rate</b>, <b>Bias Trem Depth</b> (0 %): an LFO on the output tubes' bias, per sub-block.</item>
///         <item><b>Balance</b> (0 %) on the stereo output.</item>
///     </list>
///     <para>
///         The knobs are read on a fixed grid of <see cref="SubBlock" /> frames, so the output does not depend on the
///         host's block size.
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
    public const int BoostType = 52;
    public const int BoostDrive = 53;
    public const int BoostTone = 54;
    public const int BoostLevel = 55;
    public const int Saturation = 56;
    public const int SaturationDrive = 57;
    public const int InputFilter = 58;
    public const int InputFilterFrequency = 59;
    public const int InputFilterQ = 60;
    public const int InputFilterGain = 61;
    public const int Definition = 62;
    public const int OutputEq = 63;
    public const int OutputEqLocation = 64;
    public const int OutputEq1 = 65; // to OutputEq1 + 9
    public const int InputDynamics = 75;
    public const int CompressorType = 76;
    public const int CompressorAmount = 77;
    public const int CompressorThreshold = 78;
    public const int CompressorClarity = 79;
    public const int SpeakerDrive = 80;
    public const int Thump = 81;
    public const int CabResonance = 82;
    public const int SpeakerCompression = 83;
    public const int SpeakerCompressionTime = 84;
    public const int TremoloRate = 85;
    public const int TremoloDepth = 86;
    public const int Balance = 87;

    // Values of Output EQ Location.
    public const int EqAtOutput = 0;
    public const int EqBeforePowerAmp = 1;
    public const int EqAtInput = 2;

    /// <summary>Frames per knob reading, on a fixed grid that ignores the host's block boundaries.</summary>
    public const int SubBlock = 32;

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
    readonly AmpBoost boost = new();
    readonly AmpEqualizer equalizer = new();
    readonly float[] eqGains = new float[GraphicEqualizer.MaxBands];
    AmpChannel active;
    AmpInputFilter inputFilter;
    AmpInputDynamics dynamics;
    AmpSaturation saturation;
    AmpCompressor compressor;
    SpeakerStage speaker;
    OutputStage output;
    int activeType = -1;
    int factor = 2;
    int phase; // position on the sub-block grid
    double tremoloPhase;
    double outputScale = 1;
    double preampScale = 1;

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
            Expert(ParamDescriptor.Float("katc", "Cathode Time Const", 25f, 400f, 100f, "%", ParamTaper.Log)),
            ParamDescriptor.Choice("boost", "Boost Type", AmpBoost.Types),
            ParamDescriptor.Float("boostdrive", "Boost Drive", 0f, 10f, 3f),
            ParamDescriptor.Float("boosttone", "Boost Tone", 0f, 10f, 5f),
            ParamDescriptor.Float("boostlevel", "Boost Level", -20f, 20f, 0f, "dB"),
            ParamDescriptor.Switch("saturation", "Saturation"),
            ParamDescriptor.Float("satdrive", "Saturation Drive", 0f, 10f, 5f),
            ParamDescriptor.Choice("infilter", "Input Filter", AmpInputFilter.Types),
            ParamDescriptor.Float("inffreq", "Input Filter Freq", 20f, 20_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("infq", "Input Filter Q", 0.1f, 10f, 0.7071f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("infgain", "Input Filter Gain", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("definition", "Definition", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("outeq", "Output EQ", ["Off", ..GraphicEqualizer.LayoutNames]),
            ParamDescriptor.Choice("outeqloc", "Output EQ Location", ["Output", "Pre Power Amp", "Input"]),
            ..Enumerable.Range(1, GraphicEqualizer.MaxBands)
                .Select(static b => ParamDescriptor.Float($"outeq{b}", $"Output EQ {b}", -12f, 12f, 0f, "dB")),
            ParamDescriptor.Float("indyn", "Input Dynamics", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("comptype", "Output Comp", AmpCompressor.Types),
            ParamDescriptor.Float("compamount", "Output Comp Amount", 0f, 10f, 5f),
            ParamDescriptor.Float("compthresh", "Output Comp Threshold", -60f, 0f, -20f, "dB"),
            ParamDescriptor.Float("compclarity", "Output Comp Clarity", 0f, 10f, 0f),
            ParamDescriptor.Float("spkdrive", "Speaker Drive", 0f, 10f, 0f),
            ParamDescriptor.Float("thump", "Speaker Thump", 0f, 10f, 0f),
            ParamDescriptor.Float("cabres", "Cab Resonance", 50f, 250f, 100f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("spkcomp", "Speaker Compression", 0f, 10f, 0f),
            ParamDescriptor.Float("spkcomptc", "Speaker Comp Time", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("tremrate", "Bias Trem Rate", 0.5f, 15f, 4f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("tremdepth", "Bias Trem Depth", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
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

        dynamics.Prepare(spec.SampleRate * factor);
        compressor.Prepare(spec.SampleRate * factor);
    }

    protected override void OnReset() {
        oversampler.Reset();
        boost.Reset();
        inputFilter.Reset();
        dynamics.Reset();
        equalizer.Reset();
        saturation.Reset();
        compressor.Reset();
        speaker.Reset();
        phase = 0;
        tremoloPhase = 0;
        activeType = -1;
        Select((int)Params.Current(Type));
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        if (type != activeType) {
            Select(type);
        }

        var select = (int)Params.Current(InputSelect);
        if (select == 1) {
            io.Right.CopyTo(io.Left);
        } else if (select == 2) {
            for (var i = 0; i < io.Length; i++) {
                io.Left[i] = 0.5f * (io.Left[i] + io.Right[i]);
            }
        }

        for (var offset = 0; offset < io.Length;) {
            var length = Math.Min(SubBlock - phase, io.Length - offset);
            ProcessChunk(io.Left.Slice(offset, length));
            offset += length;
            phase = (phase + length) % SubBlock;
        }

        if (factor == 2) {
            oversampler.FlushDenormals();
        }

        active.FlushDenormals();
        equalizer.FlushDenormals();
        io.Left.CopyTo(io.Right);
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    /// <summary>
    ///     One sub-block of the grid: the knobs, then at the inner rate the input stages (dynamics, boost, filter and
    ///     Definition, an input EQ), the amp (with the saturation and a pre-power-amp EQ between preamp and power amp),
    ///     and the output stages (EQ, speaker, compressor). Each insert is skipped while it is off.
    /// </summary>
    void ProcessChunk(Span<float> samples) {
        var length = samples.Length;
        Configure(length);
        var innerRate = Spec.SampleRate * factor;
        var trim = Decibels.ToGain(Params.Advance(InputTrim, length));
        var boostDrive = Params.Advance(BoostDrive, length);
        var boostTone = Params.Advance(BoostTone, length);
        var boostLevel = Params.Advance(BoostLevel, length);
        boost.Configure((int)Params.Current(BoostType), boostDrive, boostTone, boostLevel, innerRate);
        inputFilter.Design(
            (int)Params.Current(InputFilter),
            Params.Advance(InputFilterFrequency, length),
            Params.Advance(InputFilterQ, length),
            Params.Advance(InputFilterGain, length),
            Params.Advance(Definition, length) / 100.0,
            innerRate
        );
        for (var b = 0; b < eqGains.Length; b++) {
            eqGains[b] = Params.Advance(OutputEq1 + b, length);
        }

        equalizer.Design((int)Params.Current(OutputEq) - 1, eqGains, innerRate);
        var location = equalizer.IsActive ? (int)Params.Current(OutputEqLocation) : -1;
        var saturate = Params.Current(Saturation) >= 0.5f;
        var satDrive = Decibels.ToGain(Params.Advance(SaturationDrive, length) * (AmpSaturation.MaxDriveDb / 10));
        if (!saturate) {
            saturation.Reset();
        }

        var inner = factor == 2 ? oversampler.Up(samples) : samples;
        dynamics.Process(inner, Params.Advance(InputDynamics, length) / 100f);
        boost.Process(inner);
        inputFilter.Process(inner);
        if (location == EqAtInput) {
            equalizer.Process(inner);
        }

        var channel = active;
        var scale = channel.PowerAmpEnabled ? outputScale : preampScale;
        if (saturate || location == EqBeforePowerAmp) {
            var eq = location == EqBeforePowerAmp;
            for (var i = 0; i < inner.Length; i++) {
                var x = channel.TickPreamp(inner[i] * InputVolts * trim);
                if (saturate) {
                    x = saturation.Tick(x, preampScale, satDrive);
                }

                if (eq) {
                    x = equalizer.Tick(x);
                }

                var y = channel.TickPowerAmp(x) * scale;
                inner[i] = Math.Abs(y) < 1e-30 ? 0f : (float)y;
            }
        } else {
            for (var i = 0; i < inner.Length; i++) {
                var y = channel.Tick(inner[i] * InputVolts * trim) * scale;
                inner[i] = Math.Abs(y) < 1e-30 ? 0f : (float)y;
            }
        }

        if (location == EqAtOutput) {
            equalizer.Process(inner);
        }

        speaker.Process(
            inner,
            Params.Advance(SpeakerDrive, length),
            Params.Advance(Thump, length),
            Params.Advance(CabResonance, length),
            Params.Advance(SpeakerCompression, length),
            Params.Advance(SpeakerCompressionTime, length),
            innerRate
        );
        compressor.Process(
            inner,
            (int)Params.Current(CompressorType),
            Params.Advance(CompressorAmount, length),
            Params.Advance(CompressorThreshold, length),
            Params.Advance(CompressorClarity, length),
            innerRate
        );

        if (factor == 2) {
            oversampler.Down(samples);
        }
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
        // Full scale: the largest swing the output stage can make (a tube amp's primary, about twice B+ plate to plate in
        // push-pull and B+ single-ended, referred to the secondary; a solid-state amp's rails less its headroom).
        outputScale = design.SolidState is { } solid
            ? trim / solid.Swing
            : trim * power.Transformer.TurnsRatio / (power.Sides * power.Supply);
        // With the power amp off, full scale is its input's: 50 V at a tube amp's inverter, a solid-state amp's swing over
        // its gain.
        preampScale = design.SolidState is { } input ? trim * input.Gain / input.Swing : trim / PreampFullScale;
        Configure(0);
        active.Reset();
    }

    /// <summary>
    ///     Bias tremolo: the output tubes' bias swung by a sine at Trem Rate, Trem Depth 100 % taking it the whole way
    ///     from cold to hot (the Bias control's range) around the knob's setting. Read once per sub-block.
    /// </summary>
    double Tremolo(double bias, int frames) {
        var depth = Params.Advance(TremoloDepth, frames) / 100.0;
        var rate = Params.Advance(TremoloRate, frames);
        if (depth == 0) {
            tremoloPhase = 0;
            return bias;
        }

        tremoloPhase += 2 * Math.PI * rate * frames / Spec.SampleRate;
        if (tremoloPhase >= 2 * Math.PI) {
            tremoloPhase -= 2 * Math.PI;
        }

        return Math.Clamp(bias + 0.5 * depth * Math.Sin(tremoloPhase), 0, 1);
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
            Tremolo(Params.Advance(Bias, frames) / 100.0, frames),
            Knob(Params.Advance(Feedback, frames)),
            preamp.Master
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
