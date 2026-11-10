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
///     negative feedback, and the power supply's sag. Mono in (left, right or both), the 8 Ω speaker voltage out on
///     both channels; no cabinet (follow it with a Cab block). The models are <see cref="AmpDesign.All" />.
/// </summary>
/// <remarks>
///     <para>
///         Everything runs at twice the sample rate (at rates of 64 kHz and up, at the sample rate): every stage,
///         linear ones included, sits between nonlinear ones, so splitting rates would cost more in resampling than it
///         saves. <b>Oversampling</b> High doubles that (4 x, 2 x from 64 kHz) for 7-14 dB less aliasing on the
///         cranked high-gain models, at about twice the CPU; it changes the latency, so it recompiles. The power amp's
///         phase inverter, feedback and grid networks are folded into one scalar Newton solve
///         (see <see cref="PowerAmp" />).
///     </para>
///     <para>
///         1.0 at the input is 1 V at the guitar jack; full scale at the output is the largest swing the output tubes
///         can make into a resistive load (each plate from B+ down to 0 V, the other up to twice B+), less a few
///         decibels of trim per model. The load is a speaker, so a power amp driven into clipping is not held under
///         full scale: when the tubes cut off, the current in the voice coil and the leakage inductance flies back
///         through the speaker's losses, the primary's capacitance and the core loss, and nothing clamps the plates
///         (the diodes some amps put from each plate to ground are not modelled). Most cranked models then peak at
///         about full scale in the audio band with spikes one to four samples wide up to 5 dB over it; a
///         single-ended amp flat out, or a power stage driven far harder than its own preamp can, is 2 to 3 dB over
///         in the audio band too (<c>AmpFullScaleTests</c> pins the numbers). A Cab block after the amp removes the
///         spikes; without one, leave the headroom in Level. A NaN or infinite input sample is taken as silence,
///         since it would otherwise stay in the circuit's state until a reset.
///     </para>
///     <para>
///         A solid-state model (<see cref="AmpDesign.SolidState" />, the JC-120) runs a
///         <see cref="SolidStatePowerAmp" /> instead: full scale is its rails less their headroom, and its panel hides
///         the tube-only parameters.
///     </para>
///     <para>
///         Front panel: Gain, Bass, Middle, Treble, Presence, Depth, Cut, Master and Bright, and the model-specific
///         <b>Switch 1-3</b> and <b>Control 1-2</b> and <b>EQ 1-5</b>, which each model names in its
///         <see cref="AmpPanel" /> (a Fat switch, a lead drive, the bands of a Mark-style EQ); the descriptor carries
///         the names per type (<see cref="ParamDescriptor.TypeLabels" />), and a control a model lacks is hidden and
///         does nothing.
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
///         <item>
///             <b>Input Trim</b> (dB, 0): gain at the input jack, no tone change. It comes before the input stages
///             below, so it also sets how hard the boost and Input Dynamics are hit.
///         </item>
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
///         <item>
///             <b>Preamp Sag</b> (100 %): how much of the supply's sag reaches the preamp (0 % a stiff preamp supply).
///         </item>
///         <item><b>Stack Freq</b> (100 %): the tone stack's frequencies (its caps divided by it).</item>
///         <item>
///             <b>Master Trim</b> (100 %): scales the master's rotation, so the master reaches full earlier or never.
///         </item>
///         <item>
///             <b>Power Amp</b> (on): off leaves the power amp out, for a real tube power amp; the output is then the
///             phase inverter's input (1.0 is 50 V, less the model's trim).
///         </item>
///         <item>
///             <b>Power Tube</b> (Stock): 6L6GC, EL34, EL84, 6V6GT, KT66, KT88 or 6550 in place of the stock tubes,
///             rebiased to the design's idle current. A 5881 is a 6L6 electrically (it differs only in its ratings,
///             which the model does not limit), so it is the 6L6GC; up to 1.0.0-rc.3 it was a choice of its own.
///         </item>
///         <item><b>Xfmr Match</b> (100 %): the output transformer's primary impedance (its impedance ratio).</item>
///         <item>
///             <b>Xfmr Low Freq</b>, <b>Xfmr High Freq</b> (100 %): its bandwidth (magnetising; leakage and
///             capacitance).
///         </item>
///         <item>
///             <b>Xfmr Drive</b> (0): core saturation beyond stock, the knee's flux halving every 2: low notes at full
///             power saturate the core. On a single-ended amp the knee comes down towards the flux of the idle
///             current as the amp is set (Bias, Variac, Xfmr Low Freq, the tube), never below it.
///         </item>
///         <item>
///             <b>Speaker load</b> (Follow Cab; Stock up to 1.0.0-rc.3): the speaker as the electrical load on the
///             power amp, not its sound. A speaker's impedance peaks at the cone's resonance and rises with the voice
///             coil's inductance, and a tube power amp's output (most of all with little feedback) follows it: more
///             thump and more treble, and different clipping and sag. That happens inside the amp; the cone's breakup,
///             the cabinet's acoustics and the mic are the Cab block's. Stock is the model's own load; then an
///             impedance curve preset (open-back 1x12, 2x12 or 4x10, closed 4x12, closed 8x10), or <b>Follow Cab</b>:
///             the load of the speaker and cabinet the next Cab block synthesises, from that speaker's own Thiele-Small
///             data (<see cref="AutoSpeaker" />).
///         </item>
///         <item>
///             <b>Load resonance</b>, <b>Load Q</b>, <b>Load peak</b> (100 %): the load's resonance frequency,
///             sharpness and peak height; <b>Load HF</b> (100 %) its voice-coil inductance; <b>Load impedance</b>
///             (100 %) the whole load against the tap (50 % is a 4 Ω cab on the 8 Ω tap). All scale the chosen curve.
///         </item>
///         <item><b>Presence Freq</b>, <b>Depth Freq</b> (100 %): the presence and depth controls' frequencies.</item>
///         <item>
///             <b>Variac</b> (100 %): the mains voltage: every supply node, and a fixed bias with them ("brown sound").
///         </item>
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
///         (helpers in <c>Amp/AmpInserts.cs</c>), all at the inner rate. The input stages work in guitar volts: the
///         input calibration (<see cref="BlockContext.InputVolts" />) and Input Trim scale the signal before them, as
///         the Drive block scales its own.
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Input Dynamics</b> (0 %): expands (positive) or compresses (negative) the input by its envelope,
///             around -20 dBV.
///         </item>
///         <item>
///             <b>Boost Type</b> (Off), <b>Boost Drive</b>, <b>Boost Tone</b>, <b>Boost Level</b>: a drive pedal from
///             <see cref="DriveCircuits" /> in front of the amp (<see cref="AmpBoost" />), hit by the guitar's volts as
///             the same pedal in a Drive block is.
///         </item>
///         <item>
///             <b>Input Filter</b> (Off: shelves, peak, high- or low-pass) with its frequency, Q and gain, and
///             <b>Definition</b> (0 %), a treble-versus-bass tilt into the preamp (<see cref="AmpInputFilter" />).
///         </item>
///         <item>
///             <b>Saturation</b> (off) and <b>Saturation Drive</b>: a diode-style soft clipper between the preamp and
///             the power amp (<see cref="AmpSaturation" />).
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
///             <b>Output Comp</b> (Off, Output, Feedback) with amount, threshold and clarity
///             (<see cref="AmpCompressor" /> ).
///         </item>
///         <item>
///             <b>Bias Trem Rate</b>, <b>Bias Trem Depth</b> (0 %): on a model with a tremolo of its own
///             (<see cref="AmpChannel.HasTremolo" />: a bias tremolo in its power amp, an optical vibrato, the 18 W's
///             oscillating tube, the brownface harmonic vibrato), that tremolo's Speed in hertz (held to its knob's
///             range) and its Intensity (the knob's rotation; the panel names them); elsewhere an LFO on the output
///             tubes' bias, per sub-block.
///         </item>
///         <item><b>Balance</b> (0 %) on the stereo output.</item>
///     </list>
///     <para>
///         Then the advanced page (docs/08 §4, stream PB), each stock at its default. Most scale a component value the
///         model already has, and are hidden on a model without it (<see cref="AmpFeatures" />):
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Power Tube Hardness</b>, <b>Preamp Hardness</b> (5): the tubes' knees sharpened or softened, 4x to
///             0.25x (<see cref="PentodeModel.Hardened" />, <see cref="TriodeModel.Hardened" />).
///         </item>
///         <item>
///             Generic switches where a model has no circuit of its own: <b>Presence Shift</b> (the presence
///             frequency an octave down, on a model whose power amp has a presence network; on the Mark IV's lead and
///             Rhythm 2 it is the amp's own pull shift, a cap in series with the feedback,
///             <see cref="SeriesPresence.ShiftCap" />), <b>Fat</b> (the stack's frequencies down by 0.6, on a model
///             with a tone stack in its preamp), <b>Input Cut</b> (a 150 Hz input high-pass at least),
///             <b>High Treble</b> (5 flat: a ±12 dB shelf at 3 kHz before the power amp) and <b>Overdrive</b> (0: up to
///             20 dB more swing into the last gain stage).
///         </item>
///         <item>
///             <b>Tonestack Type</b> (Stock: any model's stack in a tone-stack stage) and <b>Tonestack Location</b>
///             (Stock, Pre, Mid, Post: the stack's stage moved, each stage kept at its operating point).
///         </item>
///         <item>
///             <b>Triode 2 Plate Freq</b> (100 %: the plate-bypass cap of the last gain stage with one),
///             <b>
///                 CF
///                 Compression
///             </b>
///             (a cathode follower's grid current) and <b>CF Harmonics</b> (its cathode resistor).
///         </item>
///         <item>
///             <b>Preamp</b>, <b>PI</b> and <b>Power Tube Bias Excursion</b> (100 %: their grid current), the power
///             grids' <b>Excursion Time</b> (coupling caps) and <b>Recovery Time</b> (grid leaks),
///             <b>
///                 Master Bias
///                 Excursion
///             </b>
///             over all three, and <b>Plate Suppressor Diodes</b>.
///         </item>
///         <item>
///             <b>Master Location</b> (Stock; Post PI: the master as a divider after the phase inverter; Pre-Triode:
///             as a divider ahead of the last preamp gain stage; where the master belongs to one channel, while
///             that channel plays).
///         </item>
///         <item>
///             <b>Output Mode</b> (FRFR; SS Power Amp + Cab leaves the speaker stages to the real cab), Speaker load
///             <b>Follow Cab</b> (the next Cab block's cabinet and speaker, <see cref="AutoSpeaker" />: the nearest Cab
///             block downstream, breadth first through the grid's cables, so on a split the first branch's; in it the
///             first unmuted DynaCab slot. With no Cab downstream, or one playing only IRs, the load is Stock),
///             <b>Screen Freq</b> and <b>Screen Q</b> (the screen node's filter; Q above 0 makes its feed a choke).
///         </item>
///     </list>
///     <para>
///         The knobs are read on a fixed grid of <see cref="SubBlock" /> frames, so the output does not depend on the
///         host's block size.
///     </para>
///     <para>
///         A model's channel is built when it is first chosen, on the control thread: the type the block is prepared
///         with, and any other the host asks for after setting it (<see cref="IControlRefresh.Refresh" />, after
///         parameters are set and periodically for modifiers). The audio thread never builds one: until the chosen
///         type's channel exists it plays the one it has. A channel built stays built, as all of them used to be.
///     </para>
///     <para>
///         The same refresh puts the channel the audio thread is about to switch to at its operating point
///         (<see cref="Settle(int)" />): the chosen type's while another plays, and, when a setting that resets the
///         playing channel moves (Preamp Tube, Power Tube, the triode switch, Tonestack Location), a second channel
///         of the playing type, which the audio thread swaps in. On a block the host refreshes, the audio thread
///         waits for that (it plays on as it is, up to a quarter of a second) and then switches without solving an
///         operating point; a type change puts the knobs at their targets, since the new amp starts from rest
///         anyway. A block nobody refreshes switches and resets at once, solving on the calling thread, as before.
///     </para>
/// </remarks>
public sealed partial class CircuitAmpBlock
    : DspBlock, IOversampledBlock, ICabFollower, IControlRefresh, IBlockMeters {
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
    public const int PowerTubeHardness = 88;
    public const int PreampHardness = 89;
    public const int PresenceShift = 90;
    public const int Fat = 91;
    public const int InputCut = 92;
    public const int HighTrebleKnob = 93;
    public const int Overdrive = 94;
    public const int StackType = 95;
    public const int StackPlace = 96;
    public const int Triode2Plate = 97;
    public const int FollowerCompression = 98;
    public const int FollowerHarmonics = 99;
    public const int PreampExcursion = 100;
    public const int InverterExcursion = 101;
    public const int PowerTubeExcursion = 102;
    public const int ExcursionTime = 103;
    public const int RecoveryTime = 104;
    public const int MasterExcursion = 105;
    public const int Suppressor = 106;
    public const int MasterLocation = 107;
    public const int OutputMode = 108;
    public const int ScreenFrequency = 109;
    public const int ScreenQ = 110;

    /// <summary>
    ///     Oversampling: Standard (2 x, 1 x from 64 kHz) or High (twice that). Changes the latency, so it recompiles;
    ///     read when the block is prepared.
    /// </summary>
    public const int OversamplingSetting = 111;

    /// <summary>
    ///     Preamp: off leaves the model's preamp out, so the block is its power amp alone and takes another preamp's
    ///     output (an Amp NAM capture of a preamp, or a Circuit Amp with its power amp off). The input's full scale is
    ///     then the inverter's, as the output's is with Power Amp off, so two blocks of one model chained that way are
    ///     the whole amp. A master that sits in the preamp becomes a plain volume control (an audio-taper pot, 10 for
    ///     unity) ahead of the inverter, so Master stays alive on a captured preamp. Kept on a type change.
    /// </summary>
    public const int PreampModel = 112;

    // Values of Speaker, Master Location and Output Mode.
    public const int SpeakerAuto = 6;
    public const int MasterPostInverter = 1;
    public const int MasterPreTriode = 2;
    public const int OutputSolidStateCab = 1;

    /// <summary>Presence Shift: the presence control's frequency multiplied by this.</summary>
    public const double PresenceShiftFactor = 0.5;

    /// <summary>Fat: the tone stack's frequencies multiplied by this.</summary>
    public const double FatFactor = 0.6;

    /// <summary>Input Cut: the input high-pass at least here.</summary>
    public const double InputCutHz = 150;

    /// <summary>Overdrive at 10, in decibels.</summary>
    public const double OverdriveDb = 20;

    // Values of Output EQ Location.
    public const int EqAtOutput = 0;
    public const int EqBeforePowerAmp = 1;
    public const int EqAtInput = 2;

    /// <summary>Frames per knob reading, on a fixed grid that ignores the host's block boundaries.</summary>
    public const int SubBlock = 64;


    /// <summary>The phase inverter's input volts at full scale, with the power amp off.</summary>
    const double PreampFullScale = 50;

    /// <summary>The Preamp Tube choice after Stock.</summary>
    static readonly TriodeModel?[] PreampTubes = [null, ..PreampSettings.Substitutes];

    /// <summary>
    ///     Per model, which generic advanced controls act on it (<see cref="AmpFeatures" />), looked up here once so
    ///     that the audio thread only reads a flag (the lookups enumerate the model's stages).
    /// </summary>
    static readonly AdvancedFeatures[] Features = [..AmpDesign.All.Select(AdvancedFeatures.Of)];

    /// <summary>The Power Tube choice after Stock.</summary>
    static readonly PentodeModel?[] PowerTubes = [
        null,
        PentodeModel.SixL6GC,
        PentodeModel.EL34,
        PentodeModel.EL84,
        PentodeModel.SixV6GT,
        PentodeModel.KT66,
        PentodeModel.KT88,
        PentodeModel.SixtyFiveFifty
    ];

    const int Free = 0; // settling: no thread is switching or settling a channel
    const int Control = 1; // the control thread is settling a channel that is not playing
    const int Audio = 2; // the audio thread is switching channels
    const long NoKey = -1;

    /// <summary>How long the audio thread waits for the control thread to settle a channel, in seconds.</summary>
    const double HoldSeconds = 0.25;

    readonly AmpChannel?[] channels = new AmpChannel?[AmpDesign.All.Count]; // built on first use (control thread)

    // A second channel per type, built by the control thread when a setting that resets the playing channel moves
    // (a tube, the output tubes' wiring, Tonestack Location): it is settled there and swapped in.
    readonly AmpChannel?[] spares = new AmpChannel?[AmpDesign.All.Count];
    readonly Lock building = new();
    readonly Oversampler2x oversampler = new(1);
    readonly Oversampler4x oversampler4 = new(1);
    readonly AmpBoost boost = new();
    readonly AmpEqualizer equalizer = new();
    readonly float[] eqGains = new float[GraphicEqualizer.MaxBands];
    readonly Pot postMaster = new(1e6, PotTaper.Audio); // a master moved after the phase inverter or before a triode
    HighTreble highTreble;
    AdvancedFeatures features = Features[0];
    CabBlock? cab;
    AmpInputFilter inputFilter;
    AmpInputDynamics dynamics;
    AmpSaturation saturation;
    AmpCompressor compressor;
    SpeakerStage speaker;
    OutputStage output;
    int activeType = -1;
    bool prepared;
    int settling; // Free, Control or Audio: who may touch the channels that are not playing
    int refreshed; // 1 once Refresh has run: a control thread settles channels for the audio thread
    int liveType = -1; // (audio to control) the playing type
    long liveKey = NoKey; // (audio to control) the reset key the playing channel runs with
    int settledType = -1; // (control to audio) the type whose idle channel is settled for the present targets
    long spareKey = NoKey; // (control to audio) the reset key the playing type's spare is settled for
    int heldType; // frames a type change has waited for the control thread
    int heldSpare; // frames a resetting setting has waited for it
    int holdFrames = 12_000;
    int phase; // position on the sub-block grid
    double tremoloPhase;
    double outputScale = 1;
    double preampScale = 1;
    double lineMaster = 1; // with the preamp off: the Master as a volume control ahead of the inverter

    /// <summary>
    ///     Guitar volts per unit of input: the input calibration (<see cref="BlockContext.InputVolts" />), 1 V at full
    ///     scale unless set.
    /// </summary>
    double inputVolts = 1;

    /// <summary>
    ///     The speaker load's resonance (Hz) at Load resonance 100 % for amp <paramref name="type" /> with Speaker load
    ///     choice <paramref name="speaker" /> (0 = the model's stock load), following the DynaCab enclosure and speaker
    ///     <paramref name="cab" /> for Follow Cab (null: none, so Stock): what the cab resonance helper scales.
    /// </summary>
    public static double SpeakerResonanceHz(int type, int speaker, (int Enclosure, int Speaker)? cab = null) =>
        (LoadFor(speaker, cab ?? (-1, -1))
            ?? AmpDesign.All[Math.Clamp(type, 0, AmpDesign.All.Count - 1)].Power.Speaker).Resonance;

    /// <summary>
    ///     The load Speaker load choice <paramref name="speaker" /> picks in place of the model's (null: Stock), with
    ///     <paramref name="cab" /> the followed Cab's DynaCab enclosure and speaker ((-1, -1) for none).
    ///     Allocation-free.
    /// </summary>
    public static SpeakerDesign? LoadFor(int speaker, (int Enclosure, int Speaker) cab) =>
        speaker switch {
            SpeakerAuto => AutoSpeaker.For(cab.Enclosure, cab.Speaker),
            > 0 and < SpeakerAuto => SpeakerDesign.Presets[speaker - 1],
            _ => null
        };

    /// <summary>The active amp's channel (for tests and diagnostics).</summary>
    public AmpChannel Channel { get; private set; }

    /// <summary>
    ///     The Cab block the Auto speaker load follows, as the graph compiler linked it (<see cref="ICabFollower" />).
    /// </summary>
    public CabBlock? FollowedCab => cab;

    /// <summary>The internal processing rate as a multiple of the sample rate.</summary>
    public int Oversampling { get; private set; } = 2;

    /// <summary>
    ///     The oversampling the amp runs at at <paramref name="sampleRate" />: 2 x below 64 kHz, else 1 x; twice that
    ///     at <paramref name="high" /> (the Oversampling setting's High).
    /// </summary>
    public static int OversamplingFor(double sampleRate, bool high) => (sampleRate < 64_000 ? 2 : 1) * (high ? 2 : 1);

    /// <summary>The oversampler's filters (the global setting); set before <see cref="IDspBlock.Prepare" />.</summary>
    public OversamplingPhase OversamplingPhase { get; set; }

    public override int LatencySamples =>
        Oversampling switch {
            4 => oversampler4.LatencySamples,
            2 => oversampler.LatencySamples,
            _ => 0
        };

    static CircuitAmpBlock() {
        // Every tube table a setting can ask for, built here (control thread), so that changing one on the audio
        // thread only looks it up. A stage of paralleled triodes builds its own as it is built
        // (PreampSettings.Prepare).
        PreampSettings.Prepare(TriodeModel.Ecc83);

        foreach (var tube in PowerTubes) {
            if (tube is { } model) {
                PentodeTables.For(model);
            }
        }
    }

    public CircuitAmpBlock() : base(Info) {
        Channel = Build(0);
    }

    /// <summary>
    ///     One sub-block of the grid: the knobs, then at the inner rate the input stages (dynamics, boost, filter and
    ///     Definition, an input EQ), the amp (with the saturation and a pre-power-amp EQ between preamp and power amp),
    ///     and the output stages (EQ, speaker, compressor). Each insert is skipped while it is off.
    /// </summary>
    void ProcessChunk(Span<float> samples) {
        var length = samples.Length;
        Configure(length);
        var innerRate = Spec.SampleRate * Oversampling;
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

        highTreble.Design(Params.Advance(HighTrebleKnob, length) / 10.0, innerRate);
        var treble = highTreble.IsActive && features.HighTreble;
        if (!treble) {
            highTreble.Reset();
        }

        var inner = Oversampling switch {
            4 => oversampler4.Up(samples),
            2 => oversampler.Up(samples),
            _ => samples
        };
        var (volts, jack) = ProcessInput(inner, trim, Params.Advance(InputDynamics, length) / 100f, location);
        var channel = Channel;
        var scale = channel.PowerAmpEnabled ? outputScale : preampScale;
        if (!channel.PreampEnabled) {
            // The input is a preamp's output, not a guitar: full scale is the inverter's full-scale input (what Power
            // Amp off puts out), whatever the interface's calibration. Input Trim still trims it.
            volts = lineMaster / (preampScale * inputVolts) * volts;
        }

        if (saturate || location == EqBeforePowerAmp || treble) {
            var eq = location == EqBeforePowerAmp;
            for (var i = 0; i < inner.Length; i++) {
                var x = channel.TickPreamp(inner[i] * volts * jack);
                if (treble) {
                    x = highTreble.Tick(x);
                }

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
                var y = channel.Tick(inner[i] * volts * jack) * scale;
                inner[i] = Math.Abs(y) < 1e-30 ? 0f : (float)y;
            }
        }

        if (location == EqAtOutput) {
            equalizer.Process(inner);
        }

        // Into a solid-state power amp and a real cab, the cab's own cone does the speaker stages' work.
        var realCab = (int)Params.Current(OutputMode) == OutputSolidStateCab;
        speaker.Process(
            inner,
            realCab ? 0 : Params.Advance(SpeakerDrive, length),
            realCab ? 0 : Params.Advance(Thump, length),
            Params.Advance(CabResonance, length),
            realCab ? 0 : Params.Advance(SpeakerCompression, length),
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

        if (Oversampling == 4) {
            oversampler4.Down(samples);
        } else if (Oversampling == 2) {
            oversampler.Down(samples);
        }
    }

    /// <summary>
    ///     The input stages (dynamics, boost, filter and Definition, the EQ at the input), in guitar volts after the
    ///     trim at the jack. Returns the calibration and trim still to apply at the circuit's input: both 1 once the
    ///     stages have scaled <paramref name="inner" />. With all of them off it is left alone and the scaling stays
    ///     in the circuit's (double) input, so an amp without them renders as before.
    /// </summary>
    (double Volts, double Trim) ProcessInput(Span<float> inner, float trim, float dynamicsAmount, int location) {
        if (dynamicsAmount == 0 && !boost.IsOn && !inputFilter.IsActive && location != EqAtInput) {
            dynamics.Process(inner, 0);
            return (inputVolts, trim);
        }

        var gain = (float)(inputVolts * trim);
        GainRamp.Apply(inner, gain, gain);
        dynamics.Process(inner, dynamicsAmount);
        boost.Process(inner);
        inputFilter.Process(inner);
        if (location == EqAtInput) {
            equalizer.Process(inner);
        }

        return (1, 1);
    }

    /// <summary>
    ///     What the circuit solvers gave up on since the block was made, as counts (not dB), 0 on a healthy rig:
    ///     <c>solverUnconverged</c>, the samples Newton ran out of iterations on and kept its best iterate (a click at
    ///     worst); <c>solverNonFinite</c>, the samples a device returned a non-finite value on, the previous solution
    ///     repeated; <c>solverFailedRecomputes</c>, the knob or switch changes that left a stage's network singular,
    ///     its previous matrix kept (a control that stops responding). They are <see cref="Faults" />, the numbers to
    ///     quote in a bug report.
    /// </summary>
    public IReadOnlyList<string> MeterIds => SolverFaults.MeterIds;

    /// <summary>
    ///     Every built channel's <see cref="AmpChannel.Faults" />, the playing one's and those of the types played
    ///     before. Allocation-free.
    /// </summary>
    public SolverFaults Faults {
        get {
            var faults = default(SolverFaults);
            for (var i = 0; i < channels.Length; i++) {
                if (Volatile.Read(ref channels[i]) is { } channel) {
                    faults += channel.Faults;
                }

                if (Volatile.Read(ref spares[i]) is { } spare) {
                    faults += spare.Faults;
                }
            }

            return faults;
        }
    }

    public void ReadMeters(Span<float> values) => Faults.Write(values);

    /// <summary>
    ///     The DC operating-point work every channel of the block has done since it was made
    ///     (<see cref="AmpChannel.OperatingPointSolves" />, the spares' included), whichever thread did it; for tests
    ///     and diagnostics: the audio thread's switch to a channel the control thread has settled adds nothing.
    /// </summary>
    public long OperatingPointSolves {
        get {
            var solves = 0L;
            for (var i = 0; i < channels.Length; i++) {
                solves += (Volatile.Read(ref channels[i])?.OperatingPointSolves ?? 0)
                    + (Volatile.Read(ref spares[i])?.OperatingPointSolves ?? 0);
            }

            return solves;
        }
    }

    /// <summary>The counts stay while the block is bypassed.</summary>
    public void ReadRestingMeters(Span<float> values) => ReadMeters(values);

    /// <summary>The Cab block whose cabinet the Auto speaker load follows (<see cref="ICabFollower" />).</summary>
    public void FollowCab(CabBlock? cab) => this.cab = cab;

    /// <summary>
    ///     Control thread: builds the channel of the type the parameters now ask for, if it is new, and settles the
    ///     channel the audio thread will switch to (<see cref="Settle(int)" />), so that the switch solves nothing.
    /// </summary>
    public void Refresh() {
        var type = TypeIndex(Params.GetTarget(Type));
        Build(type);
        Volatile.Write(ref refreshed, 1);
        Settle(type);
    }

    /// <summary>
    ///     Control thread: the channel of <paramref name="type" />, built (and set to the block's rate once it is
    ///     prepared) if it is the first time, then handed to the audio thread.
    /// </summary>
    AmpChannel Build(int type) {
        lock (building) {
            if (Volatile.Read(ref channels[type]) is { } built) {
                return built;
            }

            var channel = Create(type);
            Volatile.Write(ref channels[type], channel);
            return channel;
        }
    }

    /// <summary>A new channel of <paramref name="type" /> at the block's rate (control thread, locked).</summary>
    AmpChannel Create(int type) {
        var channel = new AmpChannel(AmpDesign.All[type], 96_000);
        if (prepared) {
            channel.SetSampleRate(Spec.SampleRate * Oversampling);
        }

        return channel;
    }

    /// <summary>
    ///     Control thread: puts the channel the audio thread is about to switch to at its operating point for the
    ///     parameters' targets, so that the audio thread's own reset finds every operating point already solved
    ///     (<see cref="AmpChannel.Reset" /> keeps each with what it was solved from) and costs microseconds, not the
    ///     half millisecond and more of a supply bisection or a preamp's worth of Newton solves.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A type change: the target type's channel, while it is not the one playing. A setting that resets the
    ///         playing channel (Preamp Tube, Power Tube, the triode switch, Tonestack Location): a second channel of
    ///         the playing type, the spare, built here the first time, which the audio thread swaps in. A swap is a
    ///         reset: both start the channel from its operating point with every state cleared.
    ///     </para>
    ///     <para>
    ///         The two threads never touch a channel together. The audio thread owns the playing channel; every
    ///         other channel belongs to whoever holds <c>settling</c>, which the control thread takes for the length
    ///         of a settle and the audio thread for the few microseconds of a switch. If the audio thread finds it
    ///         taken it plays on and tries again at the next block; if the control thread does, it leaves the settle
    ///         to the next refresh.
    ///     </para>
    /// </remarks>
    void Settle(int type) {
        if (Interlocked.CompareExchange(ref settling, Control, Free) != Free) {
            return;
        }

        try {
            lock (building) {
                if (!prepared || channels[type] is not { } channel) {
                    return;
                }

                // The playing type cannot change while this thread holds the claim.
                var snapshot = Compute(channel, Features[type], 0, true);
                if (Volatile.Read(ref liveType) != type) {
                    Settle(channel, snapshot);
                    Volatile.Write(ref settledType, type);
                    return;
                }

                Volatile.Write(ref settledType, -1);
                if (snapshot.ResetKey == Volatile.Read(ref liveKey)
                    || snapshot.ResetKey == Volatile.Read(ref spareKey)) {
                    return;
                }

                if (spares[type] is not { } spare) {
                    spare = Create(type);
                    Volatile.Write(ref spares[type], spare);
                }

                Settle(spare, snapshot);
                Volatile.Write(ref spareKey, snapshot.ResetKey);
            }
        } finally {
            Volatile.Write(ref settling, Free);
        }
    }

    /// <summary>
    ///     <paramref name="channel" /> with <paramref name="snapshot" />'s knobs and settings, at its operating point.
    ///     Reset until the supply's solve has nothing left to do (a cathode-biased amp's starts from the last one's
    ///     answer, so its second reset solves once more): the audio thread's reset then finds it all as kept.
    /// </summary>
    static void Settle(AmpChannel channel, in AmpSnapshot snapshot) {
        channel.Apply(snapshot.PreampSettings, snapshot.PowerSettings, snapshot.ChannelSettings);
        channel.Configure(snapshot.Preamp, snapshot.Power);
        for (var pass = 0; pass < 4; pass++) {
            var solves = channel.OperatingPointSolves;
            channel.Reset();
            if (channel.OperatingPointSolves == solves) {
                break;
            }
        }
    }

    static int TypeIndex(float type) => Math.Clamp((int)type, 0, AmpDesign.All.Count - 1);

    /// <summary>
    ///     Whether the audio thread may switch to <paramref name="type" /> now: at once on a block no control thread
    ///     refreshes, else once that thread has settled the type's channel for the present targets, or after
    ///     <see cref="HoldSeconds" /> without it (the switch then solves what moved here, as it always did).
    /// </summary>
    bool Settled(int type, int frames) {
        if (Volatile.Read(ref refreshed) == 0 || Volatile.Read(ref settledType) == TypeIndex(type)) {
            heldType = 0;
            return true;
        }

        heldType += frames;
        if (heldType < holdFrames) {
            return false;
        }

        heldType = 0;
        return true;
    }

    /// <summary>
    ///     The claim on the channels that are not playing, for a switch. The audio thread does not wait for it
    ///     (<paramref name="wait" /> false: the control thread is settling a channel, a millisecond or so); a reset
    ///     does.
    /// </summary>
    bool Claim(bool wait) {
        var spin = new SpinWait();
        while (Interlocked.CompareExchange(ref settling, Audio, Free) != Free) {
            if (!wait) {
                return false;
            }

            spin.SpinOnce();
        }

        return true;
    }

    /// <summary>
    ///     Switches to amp <paramref name="type" />, which starts at its operating point; until its channel is built
    ///     (on the control thread), and while that thread is settling a channel, the amp stays as it is and the next
    ///     block tries again.
    /// </summary>
    void Select(int type, bool wait = false) {
        var index = TypeIndex(type);
        if (Volatile.Read(ref channels[index]) is null || !Claim(wait)) {
            return;
        }

        if (activeType >= 0) {
            // A switch while playing: the new amp starts with its knobs where they are going, not gliding there from
            // the previous amp's positions. Its channel starts from rest either way, and the control thread settled
            // it for these targets, so the reset below finds every operating point as it was kept.
            Params.SnapToTargets();
        }

        activeType = type;
        Channel = channels[index]!;
        features = Features[index];
        var design = Channel.Design;
        var power = design.Power;
        var trim = Decibels.ToGain(design.OutputTrimDb);
        // Full scale: the largest swing the output stage can make (a tube amp's primary, about twice B+ plate to plate
        // in push-pull and B+ single-ended, referred to the secondary; a solid-state amp's rails less its headroom).
        outputScale = design.SolidState is { } solid
            ? trim / solid.Swing
            : trim * power.Transformer.TurnsRatio / (power.Sides * power.Supply);
        // With the power amp off, full scale is its input's: 50 V at a tube amp's inverter, a solid-state amp's swing
        // over its gain.
        preampScale = design.SolidState is { } input ? trim * input.Gain / input.Swing : trim / PreampFullScale;
        Volatile.Write(ref liveType, index);
        Volatile.Write(ref liveKey, NoKey);
        Volatile.Write(ref spareKey, NoKey);
        Volatile.Write(ref settledType, -1);
        heldSpare = 0;
        try {
            Configure(0, true);
            Channel.Reset();
        } finally {
            Volatile.Write(ref settling, Free);
        }
    }

    /// <summary>What a setting that resets the playing channel does on the audio thread this sub-block.</summary>
    enum SpareAction {
        /// <summary>Reset the playing channel in place: nobody settles a spare, or it did not come in time.</summary>
        InPlace,

        /// <summary>Play on with the settings as they were: the control thread is settling the spare.</summary>
        Wait,

        /// <summary>The spare, settled for these settings, is now the playing channel.</summary>
        Swapped
    }

    /// <summary>
    ///     A setting that resets the playing channel has moved (<paramref name="key" /> is the new settings' reset
    ///     key): swaps in the spare if the control thread has settled it for that key, waits for it on a block that
    ///     thread refreshes, and gives up after <see cref="HoldSeconds" />.
    /// </summary>
    SpareAction TakeSpare(long key, int frames) {
        if (Volatile.Read(ref refreshed) == 0) {
            return SpareAction.InPlace;
        }

        if (Volatile.Read(ref spareKey) == key && Claim(false)) {
            var index = TypeIndex(activeType);
            if (Volatile.Read(ref spareKey) == key && spares[index] is { } spare) {
                Volatile.Write(ref spares[index], Channel);
                Volatile.Write(ref channels[index], spare);
                Channel = spare;
                Volatile.Write(ref spareKey, NoKey);
                Volatile.Write(ref settling, Free);
                heldSpare = 0;
                return SpareAction.Swapped;
            }

            Volatile.Write(ref settling, Free);
        }

        heldSpare += frames;
        if (heldSpare < holdFrames) {
            return SpareAction.Wait;
        }

        heldSpare = 0;
        return SpareAction.InPlace;
    }

    /// <summary>
    ///     Bias tremolo on a model without one of its own: the output tubes' bias swung by a sine at Trem Rate, Trem
    ///     Depth 100 % taking it the whole way from cold to hot (the Bias control's range) around the knob's setting.
    ///     Read once per sub-block.
    /// </summary>
    double Tremolo(double bias, double rate, double depth, int frames) {
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

    /// <summary>
    ///     The knobs, advanced over <paramref name="frames" />, applied to the playing amp. A setting that resets the
    ///     channel (<see cref="AmpChannel.NeedsReset" />) goes through the spare the control thread settles
    ///     (<see cref="TakeSpare" />) unless this is the switch to a channel itself (<paramref name="switching" />).
    /// </summary>
    void Configure(int frames, bool switching = false) {
        var snapshot = Compute(Channel, features, frames, false);
        lineMaster = snapshot.LineMaster;
        var swapped = false;
        if (!switching
            && Channel.NeedsReset(snapshot.PreampSettings, snapshot.PowerSettings, snapshot.ChannelSettings)) {
            var action = TakeSpare(snapshot.ResetKey, frames);
            if (action == SpareAction.Wait) {
                Channel.Configure(snapshot.Preamp, snapshot.Power);
                return;
            }

            swapped = action == SpareAction.Swapped;
        } else {
            heldSpare = 0;
        }

        Channel.Apply(snapshot.PreampSettings, snapshot.PowerSettings, snapshot.ChannelSettings);
        Channel.Configure(snapshot.Preamp, snapshot.Power);
        if (swapped) {
            Channel.Reset(); // the spare starts from the operating point the control thread solved
        }

        Volatile.Write(ref liveKey, snapshot.ResetKey);
    }

    /// <summary>
    ///     Every knob and setting of the block as <paramref name="channel" /> takes them: the parameters advanced
    ///     over <paramref name="frames" /> on the audio thread, or (<paramref name="targets" />) their targets, for
    ///     the control thread to settle a channel with, which then reads nothing of the audio thread's state.
    /// </summary>
    AmpSnapshot Compute(AmpChannel channel, in AdvancedFeatures has, int frames, bool targets) {
        float Now(int id) => targets ? Params.GetTarget(id) : Params.Current(id);
        float Glide(int id) => targets ? Params.GetTarget(id) : Params.Advance(id, frames);

        static double Knob(float value) => value / 10.0;
        static double Percent(float value) => value / 100.0;

        static double Hardness(float value) => Math.Pow(2, (value - 5) / 2.5);

        var design = channel.Design;
        var switch1 = Now(Switch1) >= 0.5f;
        var switch2 = Now(Switch2) >= 0.5f;
        var switch3 = Now(Switch3) >= 0.5f;
        // The master moves after the inverter only while the playing channel has one to move.
        var masterLocation =
            has.HasPostMaster(switch1, switch2, switch3) ? (int)Now(MasterLocation) : 0;
        var postInverter = masterLocation == MasterPostInverter;
        var preTriode = masterLocation == MasterPreTriode && channel.HasGainStage;
        var masterRotation =
            Math.Min(Knob(Glide(Master)) * Percent(Glide(MasterTrim)), 1);
        // A three-position switch of the amp is two switches here: the panel settles a pair that is on on both sides.
        var preamp = design.Panel.Settle(
            new(
                Knob(Glide(Gain)),
                Knob(Glide(Bass)),
                Knob(Glide(Middle)),
                Knob(Glide(Treble)),
                postInverter || preTriode ? 1 : masterRotation,
                Now(Bright) >= 0.5f,
                Knob(Glide(Control1)),
                Knob(Glide(Control2)),
                switch1,
                switch2,
                switch3,
                new(
                    Glide(Eq1),
                    Glide(Eq1 + 1),
                    Glide(Eq1 + 2),
                    Glide(Eq1 + 3),
                    Glide(Eq1 + 4)
                ),
                Presence: Knob(Now(Presence))
            )
        );
        // The tremolo: the model's own oscillator where it has one (Speed in hertz, Intensity as the knob's rotation),
        // else the generic bias tremolo.
        double tremoloRate = Glide(TremoloRate);
        var tremoloDepth = Percent(Glide(TremoloDepth));
        var ownTremolo = channel.HasTremolo;
        if (ownTremolo) {
            preamp = preamp with { TremoloRate = tremoloRate, TremoloDepth = tremoloDepth };
        }

        var line = has.PreampMaster && !postInverter ? postMaster.Fraction(masterRotation) : 1;
        var feedback = Knob(Glide(Feedback));
        if (design.Power.FeedbackOffSwitch is { } off && preamp.IsOn(off)) {
            feedback = 0;
        }

        // The feedback's presence pot: the Presence knob, or the model control its relays put there.
        var presence = Knob(Glide(Presence));
        if (design.Power.PresencePot is { } presencePot) {
            presence = preamp.Rotation(presencePot);
        }

        if (design.Power.PresenceOut is { } presenceOut && preamp.IsOn(presenceOut.Switch) == presenceOut.WhileOn) {
            presence = presenceOut.Rotation;
        }

        var power = new PowerControls(
            presence,
            Knob(Glide(Depth)),
            Knob(Glide(Cut)),
            Knob(Glide(Sag)),
            // The generic bias tremolo's phase is the audio thread's: a channel is settled at the knob's own bias.
            ownTremolo || targets
                ? Glide(Bias) / 100.0
                : Tremolo(Glide(Bias) / 100.0, tremoloRate, tremoloDepth, frames),
            feedback,
            preamp.Master,
            ownTremolo ? tremoloRate : 0,
            ownTremolo ? tremoloDepth : 0,
            postInverter ? postMaster.Fraction(masterRotation) : 1,
            design.Power.Feedback.Deep is { } deep && preamp.IsOn(deep.Switch)
        );

        // Presence Freq and Presence Shift move a presence network: stock on a model without one.
        // Presence Shift is the model's own pull shift where its presence has one, else the generic octave.
        var presenceShift = Now(PresenceShift) >= 0.5f && has.Presence;
        var ownShift = design.Power.Feedback.Series is { ShiftCap: > 0 };
        var presenceFrequency = Percent(Glide(PresenceFrequency))
            * (presenceShift && !ownShift ? PresenceShiftFactor : 1);
        if (!has.Presence) {
            presenceFrequency = 1;
        }

        var excursion = Percent(Now(MasterExcursion));
        var fat = Now(Fat) >= 0.5f && has.Fat;
        var stackType = (int)Now(StackType);
        var preampSettings = new PreampSettings(
            PreampTubes[Math.Clamp((int)Now(PreampTube), 0, PreampTubes.Length - 1)],
            Percent(Now(BrightCap)),
            Percent(Now(StackFrequency)) * (fat ? FatFactor : 1),
            Math.Pow(2, -Percent(Now(PreampBias))),
            Hardness(Now(PreampHardness)),
            Percent(Now(PreampExcursion)) * excursion,
            1,
            Percent(Now(FollowerCompression)),
            Percent(Now(FollowerHarmonics)),
            stackType > 0 ? AmpStacks.All[Math.Min(stackType, AmpStacks.All.Count) - 1] : null
        );
        var speaker = (int)Now(SpeakerCurve);
        var speakerDesign = LoadFor(speaker, speaker == SpeakerAuto && cab is not null ? cab.DynaCab : (-1, -1));
        var rectifier = (int)Now(Rectifier);
        var variac = Percent(Glide(Variac));
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
            PowerTubes[Math.Clamp((int)Now(PowerTube), 0, PowerTubes.Length - 1)],
            Percent(Now(TransformerMatch)),
            Percent(Now(TransformerLow)),
            Percent(Now(TransformerHigh)),
            Now(TransformerDrive),
            speakerDesign,
            Percent(Now(SpeakerResonance)),
            Percent(Now(SpeakerQ)),
            Percent(Now(SpeakerPeak)),
            Percent(Now(SpeakerHigh)),
            Percent(Now(SpeakerImpedance)),
            presenceFrequency,
            Percent(Glide(DepthFrequency)),
            variac,
            Percent(Now(SupplyTime)),
            (SupplyMode)Math.Clamp((int)Now(SupplyType), 0, 3),
            rectifier > 0 ? RectifierModel.All[Math.Min(rectifier, RectifierModel.All.Count) - 1] : switched,
            Percent(Now(CathodeTime)),
            design.Power.TriodeSwitch is { } role && preamp.IsOn(role),
            Hardness(Now(PowerTubeHardness)),
            Percent(Now(PowerTubeExcursion)) * excursion,
            Percent(Now(ExcursionTime)),
            Percent(Now(RecoveryTime)),
            Percent(Now(InverterExcursion)) * excursion,
            Now(Suppressor) >= 0.5f,
            Percent(Now(ScreenFrequency)),
            Now(ScreenQ),
            presenceShift && ownShift
        );
        var lowCut = Glide(LowCut);
        if (Now(InputCut) >= 0.5f && has.InputCut) {
            lowCut = Math.Max(lowCut, (float)InputCutHz);
        }

        var overdrive = Glide(Overdrive);
        var channelSettings = new ChannelSettings(
            lowCut,
            Percent(Glide(PreampSag)),
            Now(PowerAmpModel) >= 0.5f,
            (StackLocation)Math.Clamp((int)Now(StackPlace), 0, 3),
            overdrive > 0 && has.Overdrive ? Decibels.ToGain(overdrive * OverdriveDb / 10) : 1,
            Percent(Now(Triode2Plate)),
            preTriode ? postMaster.Fraction(masterRotation) : 1,
            Now(PreampModel) >= 0.5f
        );

        // What a reset of the channel follows from (AmpChannel.NeedsReset), as one number the two threads compare.
        var key = (long)Math.Max((int)Now(PreampTube), 0)
            | ((long)Math.Max((int)Now(PowerTube), 0) << 16)
            | ((long)channelSettings.StackLocation << 32)
            | (powerSettings.Triode ? 1L << 40 : 0);
        return new(preamp, power, preampSettings, powerSettings, channelSettings, line, key);
    }


    protected override void OnPrepare(in ProcessSpec spec) {
        Oversampling = OversamplingFor(spec.SampleRate, Params.GetTarget(OversamplingSetting) >= 0.5f);
        oversampler.Resize(spec.MaxBlockSize);
        oversampler4.Resize(Oversampling == 4 ? spec.MaxBlockSize : 1);
        if (oversampler.Phase != OversamplingPhase) {
            oversampler.SetPhase(OversamplingPhase);
        }

        if (oversampler4.Phase != OversamplingPhase) {
            oversampler4.SetPhase(OversamplingPhase);
        }

        lock (building) {
            // The prepared type's channel first (if it is new), then every built one at the rate, once each.
            prepared = false;
            Build(TypeIndex(Params.GetTarget(Type)));
            foreach (var channel in channels) {
                channel?.SetSampleRate(spec.SampleRate * Oversampling);
            }

            foreach (var spare in spares) {
                spare?.SetSampleRate(spec.SampleRate * Oversampling);
            }

            holdFrames = (int)(HoldSeconds * spec.SampleRate);

            prepared = true;
        }

        dynamics.Prepare(spec.SampleRate * Oversampling);
        compressor.Prepare(spec.SampleRate * Oversampling);
    }

    protected override void OnReset() {
        oversampler.Reset();
        oversampler4.Reset();
        boost.Reset();
        inputFilter.Reset();
        dynamics.Reset();
        equalizer.Reset();
        saturation.Reset();
        compressor.Reset();
        speaker.Reset();
        highTreble.Reset();
        phase = 0;
        tremoloPhase = 0;
        activeType = -1;
        (heldType, heldSpare) = (0, 0);
        Select((int)Params.Current(Type), true);
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    /// <summary>
    ///     Replaces NaN and infinite samples with silence. Everything after the input is recursive (the oversampler's
    ///     allpasses, the stages' capacitors, the supply): one such sample would stay in that state and leave the amp
    ///     silent, with a finite output, until it was reset.
    /// </summary>
    static void SilenceNonFinite(Span<float> samples) {
        for (var i = 0; i < samples.Length; i++) {
            if (!float.IsFinite(samples[i])) {
                samples[i] = 0f;
            }
        }
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        if (type == activeType) {
            heldType = 0;
        } else if (activeType < 0 || Settled(type, io.Length)) {
            Select(type);
        }

        inputVolts = context.InputVolts;
        var select = (int)Params.Current(InputSelect);
        if (select == 1) {
            io.Right.CopyTo(io.Left);
        } else if (select == 2) {
            for (var i = 0; i < io.Length; i++) {
                io.Left[i] = 0.5f * (io.Left[i] + io.Right[i]);
            }
        }

        SilenceNonFinite(io.Left);
        for (var offset = 0; offset < io.Length;) {
            var length = Math.Min(SubBlock - phase, io.Length - offset);
            ProcessChunk(io.Left.Slice(offset, length));
            offset += length;
            phase = (phase + length) % SubBlock;
        }

        if (Oversampling == 4) {
            oversampler4.FlushDenormals();
        } else if (Oversampling == 2) {
            oversampler.FlushDenormals();
        }

        Channel.FlushDenormals();
        equalizer.FlushDenormals();
        highTreble.FlushDenormals();
        io.Left.CopyTo(io.Right);
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}

/// <summary>
///     Everything the Circuit Amp's parameters make of a channel at one moment: its knobs, its expert settings, the
///     Master as a line volume with the preamp off, and the reset key (the settings a change of which resets the
///     channel, as one number).
/// </summary>
readonly record struct AmpSnapshot(
    AmpControls Preamp,
    PowerControls Power,
    PreampSettings PreampSettings,
    PowerSettings PowerSettings,
    ChannelSettings ChannelSettings,
    double LineMaster,
    long ResetKey
);

/// <summary>
///     Which of the Circuit Amp's generic advanced controls a model takes (<see cref="AmpFeatures.Has" />).
///     <c>PostMaster</c> says under which settings of Switch 1 to 3 Master Location acts
///     (<see cref="AmpFeatures.MasterPaths" />), 0 where the model does not take it.
/// </summary>
readonly record struct AdvancedFeatures(
    int PostMaster,
    bool Fat,
    bool Presence,
    bool InputCut,
    bool Overdrive,
    bool HighTreble,
    bool PreampMaster
) {
    public static AdvancedFeatures Of(AmpDesign design) =>
        new(
            AmpFeatures.Has(design, "mvloc") ? AmpFeatures.MasterPaths(design) : 0,
            AmpFeatures.Has(design, "fatsw"),
            AmpFeatures.Has(design, "presshift"),
            AmpFeatures.Has(design, "cutsw"),
            AmpFeatures.Has(design, "overdrive"),
            AmpFeatures.Has(design, "hitreble"),
            AmpFeatures.HasMaster(design)
        );

    /// <summary>Whether Master Location acts with the switches as given: the playing channel has a master.</summary>
    public bool HasPostMaster(bool switch1, bool switch2, bool switch3) =>
        (PostMaster >> ((switch1 ? 1 : 0) | (switch2 ? 2 : 0) | (switch3 ? 4 : 0)) & 1) != 0;
}
