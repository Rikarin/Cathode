using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>The power-amp knobs, as rotations 0..1.</summary>
/// <param name="Presence">0 to 1: the presence pot (more treble from less feedback at high frequencies).</param>
/// <param name="Depth">0 to 1: less feedback below the speaker resonance.</param>
/// <param name="Cut">0 to 1: the cut control (Vox), a treble cut between the phase inverter plates.</param>
/// <param name="Feedback">0.5 is the amp's stock feedback; 0 none, 1 twice as much.</param>
/// <param name="Bias">0.5 is the stock idle current; 0 cold, 1 hot.</param>
/// <param name="Sag">0.5 is the stock supply; 0 stiff, 1 very soft.</param>
/// <param name="Master">
///     0 to 1: a master wired across the phase inverter's outputs (<see cref="PowerAmpDesign.ShuntMaster" />); 1 is its
///     full track.
/// </param>
/// <param name="TremoloRate">The bias tremolo's rate in hertz (<see cref="PowerAmpDesign.BiasTremolo" />).</param>
/// <param name="TremoloDepth">The bias tremolo's Intensity rotation, 0..1 (0 stops it).</param>
public readonly record struct PowerControls(
    double Presence,
    double Depth,
    double Cut,
    double Sag,
    double Bias,
    double Feedback,
    double Master = 1,
    double TremoloRate = 0,
    double TremoloDepth = 0
);

/// <summary>How the supply's rectifier is fed.</summary>
public enum SupplyMode {
    /// <summary>As the design has it (<see cref="PowerSupplyDesign.LineFrequency" />).</summary>
    Stock,

    /// <summary>An ideal DC source behind the source resistance: no ripple.</summary>
    Dc,

    /// <summary>Full-wave rectified 60 Hz mains: 120 Hz ripple.</summary>
    Ac60,

    /// <summary>Full-wave rectified 50 Hz mains: 100 Hz ripple.</summary>
    Ac50
}

/// <summary>
///     The power amp's expert settings, as the Circuit Amp's expert parameters set them: multiples of the design's
///     values, 1 (or null, or false) for stock.
/// </summary>
/// <param name="Tube">A different output tube (same count, rebiased to the design's idle current); null for stock.</param>
/// <param name="Match">The transformer's primary impedance (its impedance ratio).</param>
/// <param name="LowFrequency">The transformer's low corner: the magnetising inductance divided by it.</param>
/// <param name="HighFrequency">The transformer's high corner: leakage inductance and primary capacitance divided by it.</param>
/// <param name="Drive">
///     0 to 10: core saturation beyond stock, the knee's flux halving every 2 (on a linear-core design, from a core that
///     only saturates far beyond full power at 40 Hz).
/// </param>
/// <param name="Speaker">A speaker preset in place of the design's; null for stock.</param>
/// <param name="Resonance">The speaker's resonance frequency.</param>
/// <param name="ResonanceQ">Its quality factor.</param>
/// <param name="ResonancePeak">Its peak's height above the voice-coil resistance.</param>
/// <param name="VoiceCoil">The voice coil's inductance (the high-frequency rise).</param>
/// <param name="Impedance">The whole speaker's impedance against the tap (a 4 Ω cab on the 8 Ω tap is 0.5).</param>
/// <param name="PresenceFrequency">The presence control's frequency (its cap divided by it).</param>
/// <param name="DepthFrequency">The depth control's frequency.</param>
/// <param name="Variac">The mains voltage: every supply node, and a fixed bias with them.</param>
/// <param name="TimeConstant">The reservoir cap (the B+ time constant).</param>
/// <param name="Supply">DC or rectified AC.</param>
/// <param name="Rectifier">A rectifier in place of the design's; null for stock.</param>
/// <param name="CathodeTimeConstant">A cathode-biased amp's bypass cap.</param>
/// <param name="Triode">The output tubes strapped as triodes (the design's <see cref="PowerAmpDesign.TriodeSwitch" />).</param>
public readonly record struct PowerSettings(
    PentodeModel? Tube = null,
    double Match = 1,
    double LowFrequency = 1,
    double HighFrequency = 1,
    double Drive = 0,
    SpeakerDesign? Speaker = null,
    double Resonance = 1,
    double ResonanceQ = 1,
    double ResonancePeak = 1,
    double VoiceCoil = 1,
    double Impedance = 1,
    double PresenceFrequency = 1,
    double DepthFrequency = 1,
    double Variac = 1,
    double TimeConstant = 1,
    SupplyMode Supply = SupplyMode.Stock,
    RectifierModel? Rectifier = null,
    double CathodeTimeConstant = 1,
    bool Triode = false
) {
    public static PowerSettings Stock => new(null);
}

/// <summary>
///     A push-pull power amp (<see cref="PowerAmpDesign" />) as a wave digital filter with a single nonlinear port: the
///     output tubes, seen from the transformer primary, are a nonlinear one-port (<see cref="PushPullStage" />) at an
///     R-type root, and the transformer and speaker (magnetising and leakage inductance, copper, the speaker's
///     voice coil and resonance, all referred to the primary) are the linear network below it.
/// </summary>
/// <remarks>
///     <para>
///         The phase inverter, the cut control, the coupling caps and grid networks and the feedback network are folded
///         into the device: each is either static (<see cref="PhaseInverter" />) or linear with one state (solved in
///         closed form per sample), so the tube currents are an explicit function of the primary voltage alone and the
///         whole loop, global feedback included, is solved by one scalar Newton iteration per sample, with no delay in
///         the loop (a one-sample delay in a 7-10 dB feedback loop is unstable).
///     </para>
///     <para>
///         The feedback is taken from the primary voltage divided by the turns ratio and scaled by the copper's loss
///         into the nominal load: the secondary but for the leakage inductance's drop and the speaker's departure from
///         its nominal impedance, about a tenth of a decibel in the audio band once the loop has done its work.
///     </para>
///     <para>
///         The supply (reservoir, screen node, preamp node), a tube rectifier's drop, mains ripple and the cathode bias
///         of a cathode-biased amp are slow, so they are integrated explicitly from the tubes' currents after each
///         sample. So is the core's saturation: the magnetising current beyond the linear inductance's is a function
///         of the flux (the inductor's current), drawn from the primary by a current source one sample behind, which is
///         stable because the flux changes slowly and the saturated inductance is kept above a floor.
///     </para>
///     <para>
///         With an <see cref="InverterInputDesign" />, the phase inverter's driven grid draws grid current through the
///         previous stage's output resistance and its coupling cap, solved per sample ahead of the power stage (the
///         feedback's share of the inverter's cathode movement is left out of the grid's bias there).
///     </para>
/// </remarks>
public sealed class PowerAmp {
    const double SaturatedInductance = 0.2; // H: the floor that keeps the explicit saturation current stable
    const double ReferenceFrequency = 40; // Hz: a core sized for full power down to here

    static readonly PowerControls Unset = new(
        double.NaN,
        double.NaN,
        double.NaN,
        double.NaN,
        double.NaN,
        double.NaN,
        double.NaN
    );

    static readonly Dictionary<PowerAmpDesign, double> AcResistances = [];

    readonly TriodeTables inverterTube;
    readonly PhaseInverter inverterGridTables; // the inverter with its plates loaded, for its grid current
    readonly double acResistance; // the AC supply's charging resistance, as a multiple of the source resistance
    RTypeRoot<Ports<Inductor, Capacitor, Inductor, Inductor, Inductor, Capacitor>, PushPullStage> root;
    double sampleRate;
    double turns;
    double magnetising; // the magnetising inductance as set
    readonly double unloadedSupply; // the rectifier's open-circuit voltage at stock mains
    readonly double nominalPreamp; // the preamp node at idle with every knob at noon
    double cathodeB;
    double sagScale = 1;
    double stockBias; // the fixed bias that gives the design's idle current at the design's supply
    double stockCathodeResistor; // the same for a cathode-biased amp
    double variac = 1;
    double reservoirCap;
    double bypassCap;
    RectifierModel? rectifier;
    double lineFrequency;
    double phase;
    double rectifierRoot; // (I / k)^(1/3) at the last sample, where the rectifier's Newton starts
    double saturationFlux; // 0: linear core
    double saturationGain; // M / L: the extra magnetising current's slope well beyond the knee
    double excess; // the extra magnetising current for the coming sample
    double sourceResistance = 30e3; // the previous stage's output resistance (inverter input, single-ended grid)
    double inverterHistory;
    double inverterHalfStep;
    double inverterRest; // the driven grid's AC voltage at rest, where the leak carries the grid current
    PentodeModel tube;
    PowerControls controls = Unset;
    PowerSettings settings = PowerSettings.Stock;
    readonly BiasTremolo? tremolo;

    public PowerAmpDesign Design { get; }

    public PhaseInverter PhaseInverter { get; }

    public NewtonSolver Newton => root.Newton;

    /// <summary>The preamp node as a fraction of its nominal voltage (the preamp's sag), after the last sample.</summary>
    public double PreampSupply => PreampVoltage / nominalPreamp;

    /// <summary>B+ at the centre tap, in volts.</summary>
    public double Supply { get; private set; }

    public double ScreenSupply { get; private set; }

    public double PreampVoltage { get; private set; }

    /// <summary>The cathode voltage (cathode bias; side A's with separate cathodes), or 0.</summary>
    public double CathodeVoltage { get; private set; }

    /// <summary>The cathode resistor of a cathode-biased amp as the bias knob sets it (all tubes').</summary>
    public double CathodeResistor { get; private set; }

    /// <summary>The grids' DC voltage relative to ground (negative for fixed bias).</summary>
    public double GridBias { get; private set; }

    /// <summary>The bias tremolo's rate in hertz (0 without one).</summary>
    public double TremoloFrequency => tremolo?.Frequency ?? 0;

    /// <summary>Whether the bias tremolo is swinging the bias.</summary>
    public bool TremoloRunning => tremolo?.Running ?? false;

    /// <summary>The bias tremolo's swing of the grid leaks' return at the last sample, volts.</summary>
    public double BiasSwing => root.Device.BiasSwing;

    /// <summary>The output tube in use.</summary>
    public PentodeModel Tube => tube;

    /// <summary>The phase inverter's driven grid (AC volts) at the last sample, with an <see cref="InverterInputDesign" />.</summary>
    public double InverterGrid { get; private set; }

    /// <summary>The current through the inverter's coupling cap at the last sample (grid current and leak).</summary>
    public double InverterCurrent { get; private set; }

    /// <summary>Plate current per tube on each side at the last sample.</summary>
    public (double A, double B) PlateCurrents => (root.Device.PlateA, root.Device.PlateB);

    public (double A, double B) ScreenCurrents => (root.Device.ScreenCurrentA, root.Device.ScreenCurrentB);

    /// <summary>Plate voltages (relative to ground) at the last sample.</summary>
    public (double A, double B) PlateVoltages {
        get {
            var v = root.DeviceVoltage(0);
            return Design.Topology == PowerTopology.SingleEnded
                ? (Supply + v, Supply)
                : (Supply + 0.5 * v, Supply - 0.5 * v);
        }
    }

    /// <summary>The control-grid-to-cathode voltages at the last sample.</summary>
    public (double A, double B) GridVoltages => (root.Device.GridA, root.Device.GridB);

    /// <summary>The core's flux linkage (volt-seconds) at the last sample.</summary>
    public double Flux => magnetising * root.Ports.A.Current;

    /// <summary>Whether the tubes' wiring leaves the plain push-pull pentode case (see <see cref="PushPullStage" />).</summary>
    bool IsGeneral =>
        Design.Topology == PowerTopology.SingleEnded
        || ScreenTap(settings.Triode) > 0
        || (Design.Strapped is not null && Design.StrappedPerSide > 0);

    public PowerAmp(PowerAmpDesign design, double sampleRate) {
        Design = design;
        this.sampleRate = sampleRate;
        tube = design.Tube;
        PhaseInverter = PhaseInverter.For(design.PhaseInverter);
        inverterTube = TriodeTables.For(design.PhaseInverter.Tube);
        inverterGridTables = design.InverterInput is null
            ? PhaseInverter
            : PhaseInverter.For(design.PhaseInverter, design.GridLeak);
        turns = design.Transformer.TurnsRatio;
        magnetising = design.Transformer.PrimaryInductance;
        reservoirCap = design.PowerSupply.Reservoir;
        bypassCap = design.CathodeBypass;
        rectifier = design.PowerSupply.Rectifier;
        lineFrequency = design.PowerSupply.LineFrequency;
        var n2 = turns * turns;
        var (rm, _, _) = design.Speaker.Motional;

        var net = new WdfNetlist();
        var (primary, leakage, secondary, coil, motional) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Port(primary, WdfNetlist.Ground);
        net.Port(primary, WdfNetlist.Ground);
        net.Resistor(primary, WdfNetlist.Ground, design.Transformer.CoreLoss);
        net.Port(primary, leakage);
        net.Resistor(leakage, secondary, design.Transformer.WindingResistance);
        net.Resistor(secondary, coil, design.Speaker.Resistance * n2);
        net.Port(coil, motional);
        net.Resistor(coil, motional, design.Speaker.EddyResistance * n2);
        net.Port(motional, WdfNetlist.Ground);
        net.Port(motional, WdfNetlist.Ground);
        net.Resistor(motional, WdfNetlist.Ground, rm * n2);
        net.Device(primary, WdfNetlist.Ground);
        net.Probe(secondary);
        net.CurrentSource(primary, WdfNetlist.Ground); // the core's saturation current

        var stage = new PushPullStage(design, PhaseInverter, sampleRate);
        tremolo = design.BiasTremolo is { } bias ? new(bias, design, sampleRate) : null;
        root = new(net, Network(sampleRate), stage);
        root.Newton.RelativeTolerance = 1e-7;
        root.Newton.Linearise = true;
        inverterHalfStep = design.InverterInput is { } input ? 0.5 / (input.CouplingCap * sampleRate) : 0;

        // The rectifier's open-circuit voltage gives the design's B+ at idle with the knobs at noon.
        CathodeResistor = 1;
        Rebias();
        var noon = new PowerControls(0.5, 0, 0, 0.5, 0.5, 0.5);
        Configure(noon);
        var supply = design.PowerSupply;
        var total = IdleCurrents(design.Supply, design.Supply, out _);
        for (var i = 0; i < 40; i++) {
            // The screen node sits below B+ by the choke's (or dropping resistor's) drop, which the currents depend on.
            var node = design.Supply - supply.ChokeResistance * (total.Screens + supply.PreampCurrent);
            total = IdleCurrents(design.Supply, Math.Max(node, 1e-3), out _);
        }

        var draw = total.Plates + total.Screens + supply.PreampCurrent;
        unloadedSupply = design.Supply + supply.SourceResistance * draw + (rectifier?.Drop(draw) ?? 0);
        acResistance = CalibrateAc(draw);
        Reset();
        nominalPreamp = PreampVoltage;
    }

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        ApplyNetwork();
        root.Device.SetSampleRate(sampleRate, sourceResistance);
        inverterHalfStep = Design.InverterInput is { } input ? 0.5 / (input.CouplingCap * sampleRate) : 0;
        tremolo?.SetSampleRate(sampleRate);
        var c = controls;
        controls = Unset;
        Configure(c);
    }

    /// <summary>
    ///     The previous stage's output resistance, which drives the phase inverter's input network or, single-ended,
    ///     the output tube's grid network.
    /// </summary>
    public void SetSourceResistance(double ohms) {
        if (ohms == sourceResistance) {
            return;
        }

        sourceResistance = Math.Max(ohms, 1);
        if (Design.Topology == PowerTopology.SingleEnded) {
            root.Device.SetSourceResistance(sourceResistance, sampleRate);
        }
    }

    /// <summary>The preamp stage inside the feedback loop (<see cref="PowerAmpDesign.Loop" />), or null.</summary>
    public LoopStage? Loop => root.Device.Loop;

    /// <summary>
    ///     The preamp knobs and switches a stage inside the feedback loop uses (its tone stack); nothing without one.
    ///     Allocation-free; rebuilds the loop's network only when a control moved.
    /// </summary>
    public void ConfigureLoop(in AmpControls controls) => root.Device.Loop?.Configure(controls);

    /// <summary>The preamp supply (fraction of nominal) a stage inside the feedback loop runs on; nothing without one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLoopSupply(double scale) => root.Device.Loop?.SetSupply(scale);

    /// <summary>Applies the power-amp knobs; recomputes only what changed.</summary>
    public void Configure(in PowerControls controls) {
        if (controls == this.controls) {
            return;
        }

        var previous = this.controls;
        this.controls = controls;
        if (controls.Presence != previous.Presence
            || controls.Depth != previous.Depth
            || controls.Feedback != previous.Feedback) {
            var amount = Design.HasFeedback ? 2 * controls.Feedback : Math.Max(0, 2 * controls.Feedback - 1);
            // The secondary is the primary less the copper's drop into the nominal load (see the remarks).
            var ot = Design.Transformer;
            var load = ot.PlateToPlate * settings.Match;
            var copper = load / (load + ot.WindingResistance);
            root.Device.SetFeedback(
                Design.Feedback,
                controls.Presence,
                controls.Depth,
                amount * copper,
                turns,
                sampleRate,
                settings.PresenceFrequency,
                settings.DepthFrequency
            );
        }

        if (controls.Cut != previous.Cut || controls.Master != previous.Master) {
            root.Device.SetCut(controls.Cut, controls.Master, sampleRate);
        }

        if (tremolo is { } t
            && (controls.TremoloRate != previous.TremoloRate || controls.TremoloDepth != previous.TremoloDepth)) {
            t.Configure(controls.TremoloRate, controls.TremoloDepth);
        }

        if (controls.Sag != previous.Sag) {
            sagScale = Math.Pow(4, 2 * controls.Sag - 1); // 0.25x to 4x the stock source resistance
        }

        if (controls.Bias != previous.Bias) {
            // Fixed bias: the bias voltage from 1.2x (cold) to 0.8x (hot) of the stock one. Cathode bias: the cathode
            // resistor from twice to half the stock one.
            if (Design.CathodeBias) {
                CathodeResistor = stockCathodeResistor * Math.Pow(2, 1 - 2 * controls.Bias);
            } else {
                GridBias = -stockBias * (1.2 - 0.4 * controls.Bias) * variac;
            }
        }
    }

    /// <summary>
    ///     Applies the expert settings; recomputes only what changed (allocation-free). Returns true when the operating
    ///     point moved (a new tube or triode strapping), after which the caller resets the channel.
    /// </summary>
    public bool Apply(in PowerSettings settings) {
        if (settings == this.settings) {
            return false;
        }

        var previous = this.settings;
        this.settings = settings;
        var reset = false;
        var model = settings.Tube ?? Design.Tube;
        if (model != tube || settings.Triode != previous.Triode) {
            tube = model;
            root.Device.SetTube(PentodeTables.For(model), ScreenTap(settings.Triode));
            Rebias();
            reset = true;
        }

        if (settings.Match != previous.Match
            || settings.LowFrequency != previous.LowFrequency
            || settings.HighFrequency != previous.HighFrequency
            || settings.Speaker != previous.Speaker
            || settings.Resonance != previous.Resonance
            || settings.ResonanceQ != previous.ResonanceQ
            || settings.ResonancePeak != previous.ResonancePeak
            || settings.VoiceCoil != previous.VoiceCoil
            || settings.Impedance != previous.Impedance) {
            turns = Math.Sqrt(Design.Transformer.PlateToPlate * settings.Match / SpeakerDesign.Nominal);
            ApplyNetwork();
        }

        variac = settings.Variac;
        reservoirCap = Design.PowerSupply.Reservoir * settings.TimeConstant;
        bypassCap = Design.CathodeBypass * settings.CathodeTimeConstant;
        rectifier = settings.Rectifier ?? Design.PowerSupply.Rectifier;
        lineFrequency = settings.Supply switch {
            SupplyMode.Dc => 0,
            SupplyMode.Ac60 => 60,
            SupplyMode.Ac50 => 50,
            _ => Design.PowerSupply.LineFrequency
        };
        SetSaturation(settings.Drive);

        // Feedback (turns, presence and depth frequencies) and the bias (tube, variac) follow from the knobs.
        var c = controls;
        controls = Unset;
        Configure(c);
        return reset;
    }

    /// <summary>
    ///     The operating point for the current knobs: supply nodes, bias and every state at rest. Allocation-free and
    ///     a few tens of microseconds, so a type change can call it on the audio thread.
    /// </summary>
    public void Reset() {
        Supply = Design.Supply;
        ScreenSupply = Design.Supply;
        var supply = Design.PowerSupply;
        var unloaded = unloadedSupply * variac;
        var converged = false;
        for (var i = 0; i < 40; i++) {
            var total = IdleCurrents(Supply, ScreenSupply, out var vk);
            CathodeVoltage = vk;
            cathodeB = vk;
            var sag = supply.SourceResistance * sagScale;
            var draw = total.Plates + total.Screens + supply.PreampCurrent;
            var next = unloaded - sag * draw - (rectifier?.Drop(draw) ?? 0);
            var node = next - supply.ChokeResistance * (total.Screens + supply.PreampCurrent);

            // Both nodes must settle: the screen node starts at B+, and a large dropping resistor moves it far.
            var change = Math.Abs(next - Supply) + Math.Abs(node - ScreenSupply);
            Supply = next;
            ScreenSupply = node;
            if (change < 1e-9) {
                converged = true;
                break;
            }
        }

        if (!converged || !(Supply > 0) || !(ScreenSupply > 0)) {
            SolveSupply(unloaded);
        }

        PreampVoltage = ScreenSupply - supply.PreampResistance * supply.PreampCurrent;
        root.Reset();
        root.SetInitialVoltages([0]);
        ref var stage = ref root.Device;
        stage.Reset();
        stage.BiasSwing = 0;
        tremolo?.Reset();
        var idle = IdlePerTube(Supply, ScreenSupply);
        stage.SetIdle(idle.Plate, idle.Screen);
        if (Design.Topology == PowerTopology.SingleEnded) {
            // The idle current flows from B+ through the whole primary: the magnetising inductance carries it.
            var draw = IdleCurrents(Supply, ScreenSupply, out _).Plates;
            root.Ports.A.Precharge(-draw);
        }

        var all = IdleCurrents(Supply, ScreenSupply, out _);
        var rectified = all.Plates + all.Screens + supply.PreampCurrent;
        rectifierRoot = rectifier is { Perveance: > 0 } r ? Math.Cbrt(rectified / r.Perveance) : 0;
        phase = 0;
        // The inverter's input network at rest: its grid current through the leak, the coupling cap charged to match.
        inverterRest = Design.InverterInput is null ? 0 : SolveInverterGrid(0, 0, 0, 0);
        InverterGrid = inverterRest;
        inverterHistory = -inverterRest;
        excess = saturationFlux > 0 ? Excess(Flux) : 0;
        root.SetCurrent(0, -excess);
        Prime(ref stage);
    }

    /// <summary>One sample: <paramref name="input" /> volts at the phase inverter's grid; returns the 8 Ω speaker voltage.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double input) {
        ref var stage = ref root.Device;
        if (tremolo is { } t) {
            stage.BiasSwing = t.Tick();
        }

        stage.Input = Design.InverterInput is null ? input : DriveInverter(input);
        Prime(ref stage);
        root.Tick();

        // The supply and the cathode bias, from this sample's currents.
        var supply = Design.PowerSupply;
        var dt = 1 / sampleRate;
        double plates, screens;
        if (stage.General) {
            plates = stage.ReservoirCurrent;
            screens = stage.ScreenNodeCurrent;
        } else {
            plates = Design.TubesPerSide * (stage.PlateA + stage.PlateB);
            screens = Design.TubesPerSide * (stage.ScreenCurrentA + stage.ScreenCurrentB);
        }

        if (!double.IsFinite(plates + screens + stage.GridCurrents)) {
            return root.Probe(0) / turns; // never let a bad sample into the supply's state
        }

        var fromRectifier = Rectify(supply);
        var toScreen = (Supply - ScreenSupply) / supply.ChokeResistance;
        var toPreamp = (ScreenSupply - PreampVoltage) / supply.PreampResistance;
        Supply += dt / reservoirCap * (Math.Max(fromRectifier, 0) - plates - toScreen);
        ScreenSupply += dt / supply.ScreenCap * (toScreen - screens - toPreamp);
        PreampVoltage += dt / supply.PreampCap * (toPreamp - supply.PreampCurrent);
        if (Design.CathodeBias) {
            if (Design.SeparateCathodes) {
                var side = 2 * CathodeResistor;
                CathodeVoltage += dt / bypassCap * (stage.CathodeCurrentA - CathodeVoltage / side);
                cathodeB += dt / bypassCap * (stage.CathodeCurrentB - cathodeB / side);
            } else {
                CathodeVoltage +=
                    dt / bypassCap * (plates + screens + stage.GridCurrents - CathodeVoltage / CathodeResistor);
                cathodeB = CathodeVoltage;
            }
        }

        if (saturationFlux > 0) {
            excess = Excess(Flux);
            root.SetCurrent(0, -excess);
        }

        return root.Probe(0) / turns;
    }

    public void FlushDenormals() {
        Flush.Inductor(ref root.Ports.A);
        Flush.Capacitor(ref root.Ports.B);
        Flush.Inductor(ref root.Ports.C);
        Flush.Inductor(ref root.Ports.D);
        Flush.Inductor(ref root.Ports.E);
        Flush.Capacitor(ref root.Ports.F);
        root.Device.FlushDenormals();
        inverterHistory = Flush.Value(inverterHistory);
        InverterGrid = Flush.Value(InverterGrid);
        excess = Flush.Value(excess);
    }

    /// <summary>The transformer and speaker, referred to the primary, for <paramref name="rate" />.</summary>
    Ports<Inductor, Capacitor, Inductor, Inductor, Inductor, Capacitor> Network(double rate) {
        var n2 = turns * turns;
        var (_, lm, cm) = Design.Speaker.Motional;
        return new(
            new(Design.Transformer.PrimaryInductance, rate),
            new(Design.Transformer.PrimaryCapacitance, rate),
            new(Design.Transformer.LeakageInductance, rate),
            new(Design.Speaker.Inductance * n2, rate),
            new(lm * n2, rate),
            new(cm / n2, rate)
        );
    }

    /// <summary>
    ///     The current from the rectifier into the reservoir this sample: through the source resistance (sag) from the
    ///     open-circuit voltage, or from the peaks of the rectified mains, less a tube rectifier's drop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Rectify(PowerSupplyDesign supply) {
        var resistance = supply.SourceResistance * sagScale;
        var source = unloadedSupply * variac;
        if (lineFrequency > 0) {
            phase += 2 * Math.PI * lineFrequency / sampleRate;
            if (phase > 2 * Math.PI) {
                phase -= 2 * Math.PI;
            }

            source *= Math.Abs(Math.Sin(phase));
            resistance *= acResistance;
        }

        var headroom = source - Supply;
        if (rectifier is not { Perveance: > 0 } r) {
            return headroom / resistance;
        }

        if (!(headroom > 0)) {
            rectifierRoot = 0;
            return 0;
        }

        // R I + (I / k)^(2/3) = headroom with I = k y³: R k y³ + y² = headroom, Newton from the right (convex).
        var rk = resistance * r.Perveance;
        var y = rectifierRoot > 0 ? Math.Max(rectifierRoot, 0) : Math.Sqrt(headroom);
        if (rk * y * y * y + y * y < headroom) {
            y = Math.Sqrt(headroom);
        }

        for (var i = 0; i < 8; i++) {
            var g = rk * y * y * y + y * y - headroom;
            var step = g / (3 * rk * y * y + 2 * y);
            y -= step;
            if (Math.Abs(step) < 1e-9 * y) {
                break;
            }
        }

        rectifierRoot = y;
        return r.Perveance * y * y * y;
    }

    /// <summary>
    ///     The supply at idle by bisection on B+ (between nothing and the open-circuit voltage), for settings where the
    ///     plain fixed-point iteration in <see cref="Reset" /> does not settle: a hot tube on a soft supply, say.
    /// </summary>
    void SolveSupply(double unloaded) {
        var supply = Design.PowerSupply;
        var (low, high) = (0.0, unloaded);
        for (var i = 0; i < 60; i++) {
            var plate = 0.5 * (low + high);
            var node = plate;
            (double Plates, double Screens) total = default;
            var vk = 0.0;
            for (var n = 0; n < 30; n++) {
                total = IdleCurrents(plate, Math.Max(node, 1e-3), out vk);
                var next = plate - supply.ChokeResistance * (total.Screens + supply.PreampCurrent);
                if (Math.Abs(next - node) < 1e-9) {
                    break;
                }

                node = 0.5 * (node + next);
            }

            var draw = total.Plates + total.Screens + supply.PreampCurrent;
            var residual = unloaded - supply.SourceResistance * sagScale * draw - (rectifier?.Drop(draw) ?? 0) - plate;
            (low, high) = residual > 0 ? (plate, high) : (low, plate);
            Supply = plate;
            ScreenSupply = Math.Max(node, 1e-3);
            CathodeVoltage = vk;
            cathodeB = vk;
        }
    }

    /// <summary>
    ///     The AC supply's charging resistance as a multiple of the DC source resistance, such that the reservoir
    ///     settles at the same mean voltage under <paramref name="load" /> amperes: the rectifier conducts only near
    ///     the mains peaks, so the same mean needs a smaller resistance. By bisection on a quick simulation of the
    ///     reservoir alone at 60 Hz (control thread, about a millisecond).
    /// </summary>
    double CalibrateAc(double load) {
        lock (AcResistances) {
            if (!AcResistances.TryGetValue(Design, out var factor)) {
                factor = CalibrateAc(Design, unloadedSupply, load);
                AcResistances[Design] = factor;
            }

            return factor;
        }
    }

    static double CalibrateAc(PowerAmpDesign design, double unloadedSupply, double load) {
        var supply = design.PowerSupply;
        var target = design.Supply;
        var (low, high) = (1e-3, 1.0);
        const double Rate = 24_000;
        for (var n = 0; n < 24; n++) {
            var factor = Math.Sqrt(low * high);
            var resistance = supply.SourceResistance * factor;
            var v = target;
            var mean = 0.0;
            const int Settle = 6_000;
            const int Average = 4_800;
            for (var i = 0; i < Settle + Average; i++) {
                var source = unloadedSupply * Math.Abs(Math.Sin(2 * Math.PI * 60 * i / Rate));
                var charge = Math.Max(0, (source - v) / resistance);
                v += (charge - load) / (supply.Reservoir * Rate);
                if (i >= Settle) {
                    mean += v / Average;
                }
            }

            if (mean > target) {
                low = factor;
            } else {
                high = factor;
            }
        }

        return Math.Sqrt(low * high);
    }

    /// <summary>The core: the knee's flux for transformer drive <paramref name="drive" /> (0..10).</summary>
    void SetSaturation(double drive) {
        var ot = Design.Transformer;
        var stock = ot.SaturationFlux;
        if (stock <= 0 && drive <= 0) {
            saturationFlux = 0;
            excess = 0;
            root.SetCurrent(0, 0);
            return;
        }

        // Single-ended, the idle current's flux sits in the core: the drive moves the knee closer to it, not to zero (a
        // knee below the idle flux would saturate the core at rest).
        var idle = Design.Topology == PowerTopology.SingleEnded
            ? ot.PrimaryInductance * Design.TubesPerSide * Design.IdleCurrent
            : 0;
        if (stock <= 0) {
            // A core with the knee at eight times the flux of full power at 40 Hz: far out of the way at 0.
            var swing = Math.Sqrt(2 * Design.RatedPower * ot.PlateToPlate);
            stock = 8 * swing / (2 * Math.PI * ReferenceFrequency) + idle;
        }

        saturationFlux = idle + Math.Max(stock - idle, 0) / Math.Pow(2, 0.5 * Math.Max(drive, 0));
        var floor = Math.Min(SaturatedInductance, 0.5 * magnetising);
        saturationGain = (magnetising / floor - 1) / magnetising;
    }

    /// <summary>
    ///     The magnetising current beyond the linear inductance's at flux <paramref name="flux" />:
    ///     (M / L) (φ - φ_s S(φ / φ_s)) with S(x) = x / (1 + x⁶)^(1/6), so it is negligible below the knee φ_s and
    ///     the inductance falls to L / (1 + M) beyond it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Excess(double flux) {
        var x = flux / saturationFlux;
        var x2 = x * x;
        var x6 = x2 * x2 * x2;
        if (x6 < 1e-12) {
            return 0;
        }

        var soft = x / Math.Pow(1 + x6, 1.0 / 6);
        return saturationGain * saturationFlux * (x - soft);
    }

    /// <summary>
    ///     The phase inverter's input network with grid current: the previous stage's open-circuit voltage
    ///     <paramref name="input" /> behind its output resistance and the coupling cap, into the grid leak and the grid.
    ///     One scalar Newton solve for the grid voltage; returns it (AC) and advances the cap.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double DriveInverter(double input) {
        var series = sourceResistance + inverterHalfStep;
        var e = input - inverterHistory;
        // The second grid, a sample behind: the cathode follows the pair's common mode.
        var v = SolveInverterGrid(e, 1 / series, root.Device.Fed, InverterGrid);
        var current = (e - v) / series;
        InverterCurrent = current;
        inverterHistory += 2 * inverterHalfStep * current;
        InverterGrid = v;
        return v - inverterRest;
    }

    /// <summary>
    ///     The driven grid's AC voltage where the current from <paramref name="source" /> volts through
    ///     <paramref name="conductance" /> balances the leak's and the grid current (Newton from <paramref name="start" />);
    ///     with no conductance, its rest: the grid current at rest charges the coupling cap until the leak carries it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double SolveInverterGrid(double source, double conductance, double fed, double start) {
        var leak = 1 / Design.InverterInput!.GridLeak;
        var e = source;
        var v = start;
        for (var i = 0; i < 12; i++) {
            // The grid current leaves through the cathode and raises it: V_gk = table(v) - R_k I_g(V_gk).
            var open = inverterGridTables.GridToCathode(v - fed, out var cathodeSlope);
            var resistance = inverterGridTables.CathodeResistance(v - fed);
            var vgk = open;
            double ig = 0, gridSlope = 0;
            for (var n = 0; n < 8; n++) {
                ig = inverterTube.GridCurrent(vgk, out gridSlope);
                var h = vgk - open + resistance * ig;
                var next = vgk - h / (1 + resistance * gridSlope);
                if (Math.Abs(next - vgk) < 1e-9) {
                    vgk = next;
                    break;
                }

                vgk = next;
            }

            ig = inverterTube.GridCurrent(vgk, out gridSlope);
            var currentSlope = gridSlope * cathodeSlope / (1 + resistance * gridSlope);
            var bootstrap = inverterGridTables.LeakReturn(v - fed, out var returnSlope) + fed;
            var f = (e - v) * conductance - (v - bootstrap) * leak - ig;
            var slope = -conductance - (1 - returnSlope) * leak - currentSlope;
            var step = f / slope;
            v -= step;
            if (Math.Abs(step) < 1e-9 + 1e-9 * Math.Abs(v)) {
                break;
            }
        }

        return v;
    }

    /// <summary>The per-sample inputs of the device that come from the supply and the bias.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Prime(ref PushPullStage stage) {
        stage.SupplyA = Supply - CathodeVoltage;
        stage.SupplyB = Supply - cathodeB;
        stage.SetScreens(
            ScreenSupply - Design.ScreenResistor * stage.ScreenCurrentA - CathodeVoltage,
            ScreenSupply - Design.ScreenResistor * stage.ScreenCurrentB - cathodeB
        );
        var bias = Design.CathodeBias ? 0 : GridBias;
        stage.GridOffsetA = bias - CathodeVoltage;
        stage.GridOffsetB = bias - cathodeB;
    }

    /// <summary>The transformer and speaker values as the expert settings scale them, into the network.</summary>
    void ApplyNetwork() {
        var s = settings;
        var ot = Design.Transformer;
        var speaker = s.Speaker ?? Design.Speaker;
        var n2 = turns * turns;
        var impedance = s.Impedance;
        var (rm, lm, cm) = SpeakerDesign.MotionalOf(
            speaker.Resonance * s.Resonance,
            (speaker.PeakImpedance - speaker.Resistance) * s.ResonancePeak,
            speaker.Q * s.ResonanceQ
        );
        magnetising = ot.PrimaryInductance / s.LowFrequency;
        root.SetResistance(2, speaker.Resistance * impedance * n2);
        root.SetResistance(3, speaker.EddyResistance * impedance * n2);
        root.SetResistance(4, rm * impedance * n2);
        root.Ports.A.Configure(magnetising, sampleRate);
        root.Ports.B.Configure(ot.PrimaryCapacitance / s.HighFrequency, sampleRate);
        root.Ports.C.Configure(ot.LeakageInductance / s.HighFrequency, sampleRate);
        root.Ports.D.Configure(speaker.Inductance * s.VoiceCoil * impedance * n2, sampleRate);
        root.Ports.E.Configure(lm * impedance * n2, sampleRate);
        root.Ports.F.Configure(cm / impedance / n2, sampleRate);
        root.Propagate();
        if (saturationFlux > 0) {
            SetSaturation(s.Drive);
        }
    }

    /// <summary>The main tubes' screen tap: 0 in pentode operation, the ultralinear tap, or 1 strapped as triodes.</summary>
    double ScreenTap(bool triode) => triode && Design.TriodeSwitch is not null ? 1 : Design.UltralinearTap;

    /// <summary>The stock bias for the tube in use: the grid voltage, or the cathode resistor.</summary>
    void Rebias() {
        if (Design.CathodeBias) {
            stockCathodeResistor = StockCathodeResistor();
            CathodeResistor = controls.Bias is var b && double.IsFinite(b)
                ? stockCathodeResistor * Math.Pow(2, 1 - 2 * b)
                : stockCathodeResistor;
        } else {
            stockBias = StockBias();
        }
    }

    /// <summary>
    ///     Total currents of all tubes at idle for the given nodes: what they draw from the reservoir (through the
    ///     primary) and from the screen node; the cathode voltage out.
    /// </summary>
    (double Plates, double Screens) IdleCurrents(double plate, double screen, out double cathode) {
        var idle = IdlePerTube(plate, screen);
        cathode = idle.Cathode;
        if (!IsGeneral) {
            var count = 2 * Design.TubesPerSide;
            return (count * idle.Plate, count * idle.Screen);
        }

        var vg = Design.CathodeBias ? -idle.Cathode : GridBias;
        var (reservoirSide, screenSide) = SideCurrents(vg, plate - idle.Cathode, screen - idle.Cathode);
        return (Design.Sides * reservoirSide, Design.Sides * screenSide);
    }

    /// <summary>
    ///     One side's idle currents at grid-cathode voltage <paramref name="vg" /> with the plate and the screen node at
    ///     <paramref name="plate" /> and <paramref name="screen" /> (relative to the cathode): from the reservoir and
    ///     from the screen node.
    /// </summary>
    (double Reservoir, double Screens) SideCurrents(double vg, double plate, double screen) {
        var tap = ScreenTap(settings.Triode);
        var vs = tap > 0 ? plate : ScreenAtIdle(tube, vg, screen);
        var ip = KorenPentode.Plate(tube, vg, vs, plate);
        var ig2 = KorenPentode.Screen(tube, vg, vs);
        var fromReservoir = Design.TubesPerSide * (ip + (tap > 0 ? ig2 : 0));
        var fromScreens = tap > 0 ? 0 : Design.TubesPerSide * ig2;
        if (Design.Strapped is { } strapped && Design.StrappedPerSide > 0) {
            fromReservoir += Design.StrappedPerSide
                * (KorenPentode.Plate(strapped, vg, plate, plate) + KorenPentode.Screen(strapped, vg, plate));
        }

        return (fromReservoir, fromScreens);
    }

    /// <summary>
    ///     One tube at idle: plate and screen current and (cathode bias) the cathode voltage, which solves
    ///     V_k = R_k × (all plate and screen currents) by the secant method from the last cathode voltage, kept inside a
    ///     shrinking bracket.
    /// </summary>
    (double Plate, double Screen, double Cathode) IdlePerTube(double plate, double screen) {
        var tapped = ScreenTap(settings.Triode) > 0;
        if (!Design.CathodeBias) {
            var vs = tapped ? plate : ScreenAtIdle(tube, GridBias, screen);
            return (KorenPentode.Plate(tube, GridBias, vs, plate), KorenPentode.Screen(tube, GridBias, vs), 0);
        }

        if (!(plate > 0)) {
            return (0, 0, 0);
        }

        var (low, high) = (0.0, plate);
        var x0 = Math.Clamp(double.IsFinite(CathodeVoltage) ? CathodeVoltage : 0, 0.01 * plate, 0.5 * plate);
        var f0 = CathodeResidual(x0, plate, screen);
        var x1 = x0 * 1.01;
        var f1 = CathodeResidual(x1, plate, screen);
        for (var i = 0; i < 60 && Math.Abs(x1 - x0) > 1e-10; i++) {
            // The residual R_k I(V_k) - V_k falls with V_k: keep the bracket around its zero.
            if (f1 > 0) {
                low = Math.Max(low, x1);
            } else {
                high = Math.Min(high, x1);
            }

            var x2 = f1 != f0 ? x1 - f1 * (x1 - x0) / (f1 - f0) : 0.5 * (low + high);
            if (!(x2 > low && x2 < high)) {
                x2 = 0.5 * (low + high);
            }

            (x0, f0) = (x1, f1);
            x1 = x2;
            f1 = CathodeResidual(x1, plate, screen);
        }

        var bias = x1;
        var screenVoltage = tapped ? plate - bias : ScreenAtIdle(tube, -bias, screen - bias);
        return (
            KorenPentode.Plate(tube, -bias, screenVoltage, plate - bias),
            KorenPentode.Screen(tube, -bias, screenVoltage),
            bias
        );
    }

    /// <summary>R_k × (all plate and screen currents at idle) - V_k, for a cathode at <paramref name="vk" />.</summary>
    double CathodeResidual(double vk, double plate, double screen) {
        if (IsGeneral) {
            var (fromReservoir, fromScreens) = SideCurrents(-vk, plate - vk, screen - vk);
            return Design.Sides * (fromReservoir + fromScreens) * CathodeResistor - vk;
        }

        var vs = ScreenAtIdle(tube, -vk, screen - vk);
        var count = 2 * Design.TubesPerSide;
        var current = count * (KorenPentode.Plate(tube, -vk, vs, plate - vk) + KorenPentode.Screen(tube, -vk, vs));
        return current * CathodeResistor - vk;
    }

    /// <summary>
    ///     The screen voltage (relative to the cathode) after the screen resistor's drop, by fixed-point iteration (a
    ///     strong contraction: the screen resistor is small against the screen's slope resistance).
    /// </summary>
    double ScreenAtIdle(PentodeModel tube, double grid, double node) {
        var vs = node;
        for (var i = 0; i < 30; i++) {
            var next = node - Design.ScreenResistor * KorenPentode.Screen(tube, grid, vs);
            if (Math.Abs(next - vs) < 1e-12) {
                return next;
            }

            vs = next;
        }

        return vs;
    }

    /// <summary>Fixed bias: the grid voltage that gives the design's idle current at the design's supply.</summary>
    double StockBias() {
        var (low, high) = (0.0, Design.Supply);
        var tapped = ScreenTap(settings.Triode) > 0;
        var screenNode = Design.ScreenSupply > 0 ? Design.ScreenSupply : Design.Supply;
        for (var i = 0; i < 80; i++) {
            var bias = 0.5 * (low + high);
            var vs = tapped ? Design.Supply : ScreenAtIdle(tube, -bias, screenNode);
            if (KorenPentode.Plate(tube, -bias, vs, Design.Supply) > Design.IdleCurrent) {
                low = bias;
            } else {
                high = bias;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>
    ///     Cathode bias: the design's resistor (all tubes') where it gives one, else the resistor that gives the design's
    ///     idle current at the design's supply.
    /// </summary>
    double StockCathodeResistor() {
        if (Design.CathodeResistor > 0) {
            return Design.CathodeResistor;
        }

        var (low, high) = (0.0, Design.Supply);
        var tapped = ScreenTap(settings.Triode) > 0;
        for (var i = 0; i < 80; i++) {
            var vk = 0.5 * (low + high);
            var vs = tapped ? Design.Supply - vk : ScreenAtIdle(tube, -vk, Design.Supply - vk);
            if (KorenPentode.Plate(tube, -vk, vs, Design.Supply - vk) > Design.IdleCurrent) {
                low = vk;
            } else {
                high = vk;
            }
        }

        var bias = 0.5 * (low + high);
        var screenVoltage = tapped ? Design.Supply - bias : ScreenAtIdle(tube, -bias, Design.Supply - bias);
        var perTube = Design.IdleCurrent + KorenPentode.Screen(tube, -bias, screenVoltage);
        return bias / (Design.Sides * Design.TubesPerSide * perTube);
    }
}

/// <summary>Koren's pentode equations (see <see cref="Pentode" />) for one tube, with the slopes the power stage needs.</summary>
public static class KorenPentode {
    /// <summary>Plate current at (V_g1k, V_g2k, V_pk).</summary>
    public static double Plate(in PentodeModel m, double vg1, double vg2, double vp) =>
        Plate(m, vg1, vg2, vp, out _, out _);

    /// <summary>Plate current and its slopes with respect to the control grid and the plate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Plate(
        in PentodeModel m,
        double vg1,
        double vg2,
        double vp,
        out double gridSlope,
        out double plateSlope
    ) {
        gridSlope = 0;
        plateSlope = 0;
        if (!(vg2 > 1e-3) || !(vp > 0)) {
            return 0;
        }

        var softplus = DeviceMath.Softplus(m.Kp * (1 / m.Mu + vg1 / vg2), out var logistic);
        var e1 = vg2 * softplus / m.Kp;
        if (!(e1 > 0)) {
            return 0;
        }

        var power = 2 * Math.Pow(e1, m.Exponent) / m.Kg1;
        var ratio = vp / m.Kvb;
        var knee = Math.Atan(ratio);
        gridSlope = m.Exponent * power / e1 * logistic * knee;
        plateSlope = power / (m.Kvb * (1 + ratio * ratio));
        return power * knee;
    }

    /// <summary>Screen current at (V_g1k, V_g2k).</summary>
    public static double Screen(in PentodeModel m, double vg1, double vg2) {
        var screen = vg2 / m.Mu + vg1;
        return screen > 0 ? Math.Pow(screen, m.Exponent) / m.Kg2 : 0;
    }
}

/// <summary>A first-order section, transposed direct form II, with a look-ahead that leaves the state alone.</summary>
struct FirstOrder {
    double b1;
    double a1;
    double state;

    public double B0 { get; private set; }

    /// <summary>The identity.</summary>
    public static FirstOrder Unity => new() { B0 = 1 };

    /// <summary>Bilinear transform of (n0 + n1 s) / (d0 + d1 s) at <paramref name="rate" />, keeping the state.</summary>
    public void Design(double n0, double n1, double d0, double d1, double rate) {
        var k = 2 * rate;
        var norm = 1 / (d0 + d1 * k);
        B0 = (n0 + n1 * k) * norm;
        b1 = (n0 - n1 * k) * norm;
        a1 = (d0 - d1 * k) * norm;
    }

    /// <summary>The output for <paramref name="x" /> without advancing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Peek(double x) => B0 * x + state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Process(double x) {
        var y = B0 * x + state;
        state = b1 * x - a1 * y;
        return y;
    }

    public void Reset() => state = 0;

    public void FlushDenormals() => state = Flush.Value(state);
}

/// <summary>
///     The power tubes' grid networks, in AC terms: each phase inverter plate (a two-port Thévenin source with the
///     inverter's output impedance matrix, so that the plates' coupling through the inverter's cathode is kept) through
///     its coupling cap to the grid-leak node, the grid leak to the (AC-grounded) bias supply, the grid stopper to the
///     grid, and grid conduction as a diode of <see cref="ConductionResistance" /> above <see cref="Threshold" /> volts
///     grid-to-cathode. Piecewise linear, so each sample is a 2 × 2 linear solve per conduction state; grid current
///     charging the coupling caps is what shifts the bias under hard drive (blocking).
/// </summary>
/// <remarks>
///     The straight-line grid law stands in for Dempwolf's (as in <see cref="Triode" />): within 15 % of it from 0.5 V to
///     5 V of grid drive, where it matters.
/// </remarks>
struct GridPair {
    /// <summary>Grid-cathode voltage where conduction starts.</summary>
    public const double Threshold = 0.2;

    /// <summary>Slope resistance of the conducting grid.</summary>
    public const double ConductionResistance = 1.2e3;

    double zaa; // the source impedance matrix, plus each coupling cap's companion resistance on the diagonal
    double zab;
    double zba;
    double zbb;
    double halfStep; // T / 2C
    double leak;
    double diode; // 1 / (R_stopper + R_conduction)
    double stopper;
    double historyA; // each coupling cap's v_c[n-1] + (T / 2C) i[n-1]
    double historyB;
    bool conductingA; // the conduction states last committed, where the next solve starts
    bool conductingB;

    public void Configure(
        (double Zaa, double Zab, double Zba, double Zbb) source,
        double coupling,
        double gridLeak,
        double gridStopper,
        double rate
    ) {
        halfStep = 0.5 / (coupling * rate);
        (zaa, zab, zba, zbb) = (source.Zaa + halfStep, source.Zab, source.Zba, source.Zbb + halfStep);
        leak = 1 / gridLeak;
        stopper = gridStopper;
        diode = 1 / (gridStopper + ConductionResistance);
    }

    /// <summary>
    ///     The grid-cathode voltages for AC source voltages <paramref name="ea" />, <paramref name="eb" /> and each
    ///     side's grid DC offset from its cathode (<paramref name="offsetA" />, <paramref name="offsetB" />), with their
    ///     slopes (∂a/∂ea, ∂a/∂eb, ∂b/∂ea, ∂b/∂eb), and the grid-leak nodes and grid currents for <see cref="Commit" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly (double A, double B) Peek(
        double ea,
        double eb,
        double offsetA,
        double offsetB,
        out (double AA, double AB, double BA, double BB) slope,
        out (double A, double B, double CurrentA, double CurrentB) nodes
    ) {
        var thresholdA = Threshold - offsetA;
        var thresholdB = Threshold - offsetB;
        var (onA, onB) = (conductingA, conductingB);
        double ya = 0, yb = 0, inverse = 0;
        double a00 = 0, a01 = 0, a10 = 0, a11 = 0;
        for (var attempt = 0; attempt < 4; attempt++) {
            // Each grid-leak node draws i = s y + c; with the plates at e - Z i, the branches give A y = r.
            var sa = onA ? leak + diode : leak;
            var sb = onB ? leak + diode : leak;
            var ca = onA ? -diode * thresholdA : 0;
            var cb = onB ? -diode * thresholdB : 0;
            a00 = zaa * sa + 1;
            a01 = zab * sb;
            a10 = zba * sa;
            a11 = zbb * sb + 1;
            inverse = 1 / (a00 * a11 - a01 * a10);
            var ra = ea - historyA - zaa * ca - zab * cb;
            var rb = eb - historyB - zba * ca - zbb * cb;
            ya = (a11 * ra - a01 * rb) * inverse;
            yb = (a00 * rb - a10 * ra) * inverse;
            var (nextA, nextB) = (ya > thresholdA, yb > thresholdB);
            if (nextA == onA && nextB == onB) {
                break;
            }

            (onA, onB) = (nextA, nextB);
        }

        var currentA = onA ? diode * (ya - thresholdA) : 0;
        var currentB = onB ? diode * (yb - thresholdB) : 0;
        var ka = onA ? 1 - stopper * diode : 1;
        var kb = onB ? 1 - stopper * diode : 1;
        slope = (ka * a11 * inverse, -ka * a01 * inverse, -kb * a10 * inverse, kb * a00 * inverse);
        nodes = (ya, yb, currentA, currentB);
        return (offsetA + ya - stopper * currentA, offsetB + yb - stopper * currentB);
    }

    /// <summary>Advances the coupling caps for the sample's solution (from <see cref="Peek" />).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Commit(in (double A, double B, double CurrentA, double CurrentB) nodes) {
        historyA += 2 * halfStep * (leak * nodes.A + nodes.CurrentA); // the branch currents
        historyB += 2 * halfStep * (leak * nodes.B + nodes.CurrentB);
        conductingA = nodes.CurrentA > 0;
        conductingB = nodes.CurrentB > 0;
    }

    public void Reset() {
        historyA = 0;
        historyB = 0;
        conductingA = false;
        conductingB = false;
    }

    public void FlushDenormals() {
        historyA = Flush.Value(historyA);
        historyB = Flush.Value(historyB);
    }
}

/// <summary>
///     The output tubes of a push-pull stage as a nonlinear one-port across the whole transformer primary, with
///     everything that drives their grids folded in (see <see cref="PowerAmp" />). Port voltage v is plate A minus plate
///     B; with an ideal centre-tapped primary the plates sit at B+ ± v/2 and the port current is (I_a - I_b) / 2 per tube
///     pair (equal power on both sides of the transformer). Single-ended, v is the plate less B+ and the port current
///     the side's current.
/// </summary>
/// <remarks>
///     The plain case (push-pull pentodes, screens on the screen node) has its own fast path. The general path adds
///     single-ended operation, screens that move with the plates (ultralinear taps at a fraction u of the half-primary,
///     triode strapping at u = 1, where the screen current flows through the primary too: u I_g2 of it), and a second,
///     triode-strapped tube type beside the main one (Simul-Class). Screen resistors are left out where the screens
///     follow the plates.
/// </remarks>
struct PushPullStage : INonlinearDevice {
    /// <summary>The phase inverter's grid voltage (AC), set per sample.</summary>
    public double Input;

    /// <summary>B+ at the centre tap relative to each side's cathode, set per sample.</summary>
    public double SupplyA;

    public double SupplyB;

    /// <summary>Screen voltages relative to the cathode (<see cref="SetScreens" />).</summary>
    public double ScreenA;

    public double ScreenB;

    /// <summary>Each side's grid DC voltage relative to its cathode (the bias), set per sample.</summary>
    public double GridOffsetA;

    public double GridOffsetB;

    /// <summary>
    ///     A bias tremolo's swing of the grid leaks' common return, set per sample: the grids take it through their
    ///     coupling caps and leaks (the inverter's sources are referred to the return, the offsets carry it).
    /// </summary>
    public double BiasSwing;

    /// <summary>Per main tube, at the last committed sample.</summary>
    public double PlateA;

    public double PlateB;

    public double ScreenCurrentA;

    public double ScreenCurrentB;

    /// <summary>Grid-cathode voltages at the last committed sample.</summary>
    public double GridA;

    public double GridB;

    /// <summary>All grid currents at the last committed sample.</summary>
    public double GridCurrents;

    /// <summary>All current each side's cathodes carry at the last committed sample.</summary>
    public double CathodeCurrentA;

    public double CathodeCurrentB;

    /// <summary>The general path only: all current drawn through the primary from B+, and from the screen node.</summary>
    public double ReservoirCurrent;

    public double ScreenNodeCurrent;

    /// <summary>The feedback voltage at the inverter's second grid at the last committed sample.</summary>
    public double Fed;

    readonly PhaseInverter phaseInverter;
    readonly LoopStage? loop; // a preamp stage inside the feedback loop, ahead of the inverter (null for none)
    readonly PentodeTables? strapped;
    readonly double tubes;
    readonly double strappedTubes;
    readonly bool singleEnded;
    readonly double coupling;
    readonly double gridLeak;
    readonly double gridStopper;
    readonly double cutCap;
    readonly Pot cutPot;
    readonly Pot? shuntMaster;
    readonly double differentialResistance;
    PentodeTables tube;
    FirstOrder presence;
    FirstOrder depth;
    FirstOrder cut;
    GridPair grids;
    double feedback; // feedback knob × tap gain / turns ratio: volts at the feedback filter per volt of primary
    bool hasCut;
    double screenTap; // the main tubes' screen tap: 0 pentode, the ultralinear fraction, or 1 triode
    double factorA; // the plate current's screen factor per side, per sample
    double factorB;

    public readonly int Ports => 1;

    /// <summary>The preamp stage inside the feedback loop (<see cref="PowerAmpDesign.Loop" />), or null.</summary>
    public readonly LoopStage? Loop => loop;

    /// <summary>Whether the general path is in use.</summary>
    public readonly bool General => singleEnded || screenTap > 0 || strappedTubes > 0;

    public PushPullStage(PowerAmpDesign design, PhaseInverter phaseInverter, double rate) {
        tube = PentodeTables.For(design.Tube);
        this.phaseInverter = phaseInverter;
        loop = design.Loop is { } stage && design.Topology == PowerTopology.PushPull ? new LoopStage(stage, rate) : null;
        tubes = design.TubesPerSide;
        singleEnded = design.Topology == PowerTopology.SingleEnded;
        if (design.Strapped is { } second && design.StrappedPerSide > 0) {
            strapped = PentodeTables.For(second);
            strappedTubes = design.StrappedPerSide;
        }

        screenTap = design.UltralinearTap;
        coupling = design.CouplingCap;
        gridLeak = design.GridLeak;
        gridStopper = design.GridStopper;
        cutCap = design.CutCap;
        cutPot = design.CutPot;
        shuntMaster = design.ShuntMaster;
        var z = phaseInverter.OutputImpedance;
        // The cut acts on the open-circuit difference that the grid network then divides against the leaks: across the
        // plates it sees the inverter's differential resistance in parallel with both leaks (coupling caps as shorts),
        // and scaling the difference by Z / (Z + R_d || 2 R_leak) gives the grids what that loaded divider would.
        var rd = z.Zaa + z.Zbb - z.Zab - z.Zba;
        differentialResistance = rd * 2 * gridLeak / (rd + 2 * gridLeak);
        presence = FirstOrder.Unity;
        depth = FirstOrder.Unity;
        cut = FirstOrder.Unity;
        SetSampleRate(rate, 30e3);
    }

    /// <summary>The grid networks for <paramref name="rate" />; single-ended, driven from <paramref name="source" /> ohms.</summary>
    public void SetSampleRate(double rate, double source) {
        var z = singleEnded ? (source, 0, 0, 1) : phaseInverter.OutputImpedance;
        grids.Configure(z, coupling, gridLeak, gridStopper, rate);
        loop?.SetSampleRate(rate);
    }

    public void SetSampleRate(double rate) => SetSampleRate(rate, 30e3);

    /// <summary>Single-ended: the driving stage's output resistance into the grid network.</summary>
    public void SetSourceResistance(double ohms, double rate) => SetSampleRate(rate, ohms);

    /// <summary>A different main tube, and the main tubes' screen tap (see the remarks).</summary>
    public void SetTube(PentodeTables tables, double screenTap) {
        tube = tables;
        this.screenTap = screenTap;
    }

    /// <summary>
    ///     The feedback filter: presence (0..1) shorting the shunt resistor at high frequencies, depth (0..1) cutting the
    ///     feedback below the design's depth frequency to a tenth, and <paramref name="amount" /> times the stock
    ///     feedback; the presence and depth frequencies as multiples of stock.
    /// </summary>
    public void SetFeedback(
        FeedbackDesign design,
        double presence,
        double depth,
        double amount,
        double turns,
        double rate,
        double presenceFrequency = 1,
        double depthFrequency = 1
    ) {
        var rp = design.Presence.Ohms * (1 - design.Presence.Fraction(presence));
        var c = design.PresenceCap / presenceFrequency;
        var (rf, rx) = (design.FeedbackResistor, design.ShuntResistor);
        var tau1 = rp * c;
        var tau2 = c * (rf * (rx + rp) + rx * rp) / (rf + rx);
        this.presence.Design(design.Beta, design.Beta * tau1, 1, tau2, rate);
        var omega = 2 * Math.PI * (design.DepthFrequency * depthFrequency);
        var floor = design.LowFeedback * (1 - 0.9 * Math.Clamp(depth, 0, 1));
        this.depth.Design(floor * omega, 1, omega, 1, rate);
        // Push-pull: the inverter's driven output is its inverted plate, so the primary voltage rises with the input.
        // Single-ended the input drives the grid directly and the plate falls as it rises: the tap is taken the other way
        // round (a real amp's secondary is wired for that), or the "feedback" would be positive.
        // A loop stage inverts too (see LoopStage).
        feedback = (singleEnded != (loop is not null) ? -1 : 1) * amount * design.TapGain / turns;
    }

    /// <summary>
    ///     The cut control (0..1): its pot from the full track (least cut) down to nothing (most); and a master across the
    ///     outputs (0..1, <see cref="PowerAmpDesign.ShuntMaster" />): its track from nothing to full. Both load the
    ///     inverter's differential output resistance R_d: with the cut's r + 1/sC beside the master's R, the difference
    ///     is scaled by Z / (Z + R_d), Z = R (1 + s r C) / (1 + s (R + r) C).
    /// </summary>
    public void SetCut(double amount, double master, double rate) {
        var hasCap = cutCap > 0 && !singleEnded;
        var hasMaster = shuntMaster is not null && !singleEnded;
        hasCut = hasCap || hasMaster;
        if (!hasCut) {
            return;
        }

        var rd = differentialResistance;
        var series = cutPot.Ohms * (1 - cutPot.Fraction(amount));
        if (shuntMaster is not { } pot || singleEnded) {
            cut.Design(1, series * cutCap, 1, (series + rd) * cutCap, rate);
            return;
        }

        var shunt = Math.Max(pot.Ohms * pot.Fraction(double.IsFinite(master) ? master : 1), 1);
        if (!hasCap) {
            cut.Design(shunt, 0, shunt + rd, 0, rate);
            return;
        }

        cut.Design(shunt, shunt * series * cutCap, shunt + rd, cutCap * (shunt * series + rd * (shunt + series)), rate);
    }

    /// <summary>Starts the idle currents (for the supply and screens before the first sample).</summary>
    public void SetIdle(double plate, double screen) {
        PlateA = plate;
        PlateB = plate;
        ScreenCurrentA = screen;
        ScreenCurrentB = screen;
    }

    /// <summary>Screen voltages relative to the cathode, set per sample.</summary>
    public void SetScreens(double a, double b) {
        ScreenA = a;
        ScreenB = b;
        factorA = tube.ScreenFactor(a);
        factorB = tube.ScreenFactor(b);
    }

    public readonly double Conductance(int port) => 1e-4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        if (General) {
            EvaluateGeneral(voltages[0], out currents[0], out jacobian[0]);
            return;
        }

        var v = voltages[0];

        // Feedback, phase inverter and cut: the grid drive as a function of v.
        var tap = feedback * v;
        var fed = depth.Peek(presence.Peek(tap));
        var fedSlope = feedback * presence.B0 * depth.B0;
        double slopeA, slopeB;
        var (ea, eb) = loop is null
            ? phaseInverter.Evaluate(Input - fed, out slopeA, out slopeB)
            : ThroughLoop(Input - fed, out slopeA, out slopeB);
        slopeA *= -fedSlope;
        slopeB *= -fedSlope;
        if (hasCut) {
            var common = 0.5 * (ea + eb);
            var difference = cut.Peek(ea - eb);
            var commonSlope = 0.5 * (slopeA + slopeB);
            var differenceSlope = cut.B0 * (slopeA - slopeB);
            (ea, eb) = (common + 0.5 * difference, common - 0.5 * difference);
            (slopeA, slopeB) = (commonSlope + 0.5 * differenceSlope, commonSlope - 0.5 * differenceSlope);
        }

        var (vgA, vgB) = grids.Peek(
            ea - BiasSwing,
            eb - BiasSwing,
            GridOffsetA + BiasSwing,
            GridOffsetB + BiasSwing,
            out var g, out _);
        var gridDriveA = g.AA * slopeA + g.AB * slopeB; // d(V_g1k) / dv on each side
        var gridDriveB = g.BA * slopeA + g.BB * slopeB;
        var ia = tube.PlateCurrent(factorA, vgA, ScreenA, SupplyA + 0.5 * v, out var gridSlopeA, out var plateSlopeA);
        var ib = tube.PlateCurrent(factorB, vgB, ScreenB, SupplyB - 0.5 * v, out var gridSlopeB, out var plateSlopeB);
        currents[0] = 0.5 * tubes * (ia - ib);
        jacobian[0] = 0.5
            * tubes
            * (0.5 * plateSlopeA + gridSlopeA * gridDriveA + 0.5 * plateSlopeB - gridSlopeB * gridDriveB);
    }

    /// <summary>Plates may fall to a tenth per Newton step (<see cref="TubeLimit.Plate" />).</summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        var v = previous[0];
        var next = proposed[0];
        if (singleEnded) {
            var plate = TubeLimit.Plate(SupplyA + v, SupplyA + next);
            proposed[0] = plate - SupplyA;
            return;
        }

        if (next > v) {
            var plate = TubeLimit.Plate(SupplyB - 0.5 * v, SupplyB - 0.5 * next);
            proposed[0] = 2 * (SupplyB - plate);
        } else {
            var plate = TubeLimit.Plate(SupplyA + 0.5 * v, SupplyA + 0.5 * next);
            proposed[0] = 2 * (plate - SupplyA);
        }
    }

    /// <summary>Advances every state for the converged primary voltage, and records the currents.</summary>
    public void Commit(ReadOnlySpan<double> voltages) {
        var v = voltages[0];
        var fed = depth.Process(presence.Process(feedback * v));
        Fed = fed;
        double ea, eb;
        if (singleEnded) {
            (ea, eb) = (Input - fed, 0);
        } else {
            (ea, eb) = phaseInverter.Evaluate(loop is null ? Input - fed : loop.Process(Input - fed), out _, out _);
            if (hasCut) {
                var common = 0.5 * (ea + eb);
                var difference = cut.Process(ea - eb);
                (ea, eb) = (common + 0.5 * difference, common - 0.5 * difference);
            }
        }

        (GridA, GridB) = grids.Peek(
            ea - BiasSwing,
            eb - BiasSwing,
            GridOffsetA + BiasSwing,
            GridOffsetB + BiasSwing,
            out _, out var nodes);
        grids.Commit(nodes);
        GridCurrents = tubes * (nodes.CurrentA + nodes.CurrentB);
        if (!General) {
            PlateA = tube.PlateCurrent(factorA, GridA, ScreenA, SupplyA + 0.5 * v, out _, out _);
            PlateB = tube.PlateCurrent(factorB, GridB, ScreenB, SupplyB - 0.5 * v, out _, out _);
            ScreenCurrentA = tube.ScreenCurrent(GridA, ScreenA);
            ScreenCurrentB = tube.ScreenCurrent(GridB, ScreenB);
            CathodeCurrentA = tubes * (PlateA + ScreenCurrentA + nodes.CurrentA);
            CathodeCurrentB = tubes * (PlateB + ScreenCurrentB + nodes.CurrentB);
            return;
        }

        var half = singleEnded ? 1 : 0.5;
        var (reservoirA, screenA) = CommitSide(
            GridA,
            ScreenA,
            factorA,
            SupplyA,
            SupplyA + half * v,
            out PlateA,
            out ScreenCurrentA
        );
        CathodeCurrentA = reservoirA + screenA + tubes * nodes.CurrentA;
        ReservoirCurrent = reservoirA;
        ScreenNodeCurrent = screenA;
        if (singleEnded) {
            GridCurrents = tubes * nodes.CurrentA;
            CathodeCurrentB = 0;
            PlateB = 0;
            ScreenCurrentB = 0;
            return;
        }

        var (reservoirB, screenB) = CommitSide(
            GridB,
            ScreenB,
            factorB,
            SupplyB,
            SupplyB - half * v,
            out PlateB,
            out ScreenCurrentB
        );
        CathodeCurrentB = reservoirB + screenB + tubes * nodes.CurrentB;
        ReservoirCurrent += reservoirB;
        ScreenNodeCurrent += screenB;
    }

    public void Reset() {
        loop?.Reset();
        presence.Reset();
        depth.Reset();
        cut.Reset();
        grids.Reset();
        GridA = GridOffsetA;
        GridB = GridOffsetB;
        GridCurrents = 0;
        Fed = 0;
    }

    public void FlushDenormals() {
        loop?.FlushDenormals();
        presence.FlushDenormals();
        depth.FlushDenormals();
        cut.FlushDenormals();
        grids.FlushDenormals();
    }

    /// <summary>
    ///     The loop stage and the inverter for the loop stage's input <paramref name="vd" />: the inverter's outputs and
    ///     their slopes with respect to <paramref name="vd" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly (double A, double B) ThroughLoop(double vd, out double slopeA, out double slopeB) {
        var grid = loop!.Peek(vd, out var slope);
        var outputs = phaseInverter.Evaluate(grid, out slopeA, out slopeB);
        slopeA *= slope;
        slopeB *= slope;
        return outputs;
    }

    /// <summary>The general path's port current and its slope (see the remarks).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly void EvaluateGeneral(double v, out double current, out double slope) {
        var fed = depth.Peek(presence.Peek(feedback * v));
        var fedSlope = feedback * presence.B0 * depth.B0;
        double ea, eb, slopeA, slopeB;
        if (singleEnded) {
            (ea, eb, slopeA, slopeB) = (Input - fed, 0, -fedSlope, 0);
        } else {
            (ea, eb) = loop is null
                ? phaseInverter.Evaluate(Input - fed, out slopeA, out slopeB)
                : ThroughLoop(Input - fed, out slopeA, out slopeB);
            slopeA *= -fedSlope;
            slopeB *= -fedSlope;
            if (hasCut) {
                var common = 0.5 * (ea + eb);
                var difference = cut.Peek(ea - eb);
                var commonSlope = 0.5 * (slopeA + slopeB);
                var differenceSlope = cut.B0 * (slopeA - slopeB);
                (ea, eb) = (common + 0.5 * difference, common - 0.5 * difference);
                (slopeA, slopeB) = (commonSlope + 0.5 * differenceSlope, commonSlope - 0.5 * differenceSlope);
            }
        }

        var (vgA, vgB) = grids.Peek(
            ea - BiasSwing,
            eb - BiasSwing,
            GridOffsetA + BiasSwing,
            GridOffsetB + BiasSwing,
            out var g, out _);
        var gridDriveA = g.AA * slopeA + g.AB * slopeB;
        if (singleEnded) {
            current = Side(vgA, ScreenA, factorA, SupplyA, SupplyA + v, 1, gridDriveA, out slope);
            return;
        }

        var gridDriveB = g.BA * slopeA + g.BB * slopeB;
        var ia = Side(vgA, ScreenA, factorA, SupplyA, SupplyA + 0.5 * v, 0.5, gridDriveA, out var sa);
        var ib = Side(vgB, ScreenB, factorB, SupplyB, SupplyB - 0.5 * v, -0.5, gridDriveB, out var sb);
        current = 0.5 * (ia - ib);
        slope = 0.5 * (sa - sb);
    }

    /// <summary>
    ///     One side's current into its end of the primary, all tubes: plate currents and the screen currents that flow
    ///     through the primary; and its slope with respect to the port voltage, given ∂V_pk/∂v
    ///     (<paramref name="plateDrive" />) and ∂V_g1k/∂v (<paramref name="gridDrive" />).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly double Side(
        double vg,
        double screenNode,
        double factor,
        double supply,
        double plate,
        double plateDrive,
        double gridDrive,
        out double slope
    ) {
        double total;
        if (screenTap > 0) {
            var vs = supply + screenTap * (plate - supply);
            var ip = tube.PlateCurrent(vg, vs, plate, out var gs, out var ss, out var ps);
            var ig2 = tube.ScreenCurrent(vg, vs, out var screenSlope);
            var screenDrive = screenTap * plateDrive;
            total = tubes * (ip + screenTap * ig2);
            slope = tubes
                * (gs * gridDrive
                    + ss * screenDrive
                    + ps * plateDrive
                    + screenTap * screenSlope * (gridDrive + screenDrive / tube.Model.Mu));
        } else {
            var ip = tube.PlateCurrent(factor, vg, screenNode, plate, out var gs, out var ps);
            total = tubes * ip;
            slope = tubes * (gs * gridDrive + ps * plateDrive);
        }

        if (strapped is { } second) {
            var ip = second.PlateCurrent(vg, plate, plate, out var gs, out var ss, out var ps);
            var ig2 = second.ScreenCurrent(vg, plate, out var screenSlope);
            total += strappedTubes * (ip + ig2);
            slope += strappedTubes
                * (gs * gridDrive
                    + (ss + ps) * plateDrive
                    + screenSlope * (gridDrive + plateDrive / second.Model.Mu));
        }

        return total;
    }

    /// <summary>
    ///     One side's committed currents: what it draws through the primary and from the screen node, and per main
    ///     tube its plate and screen current.
    /// </summary>
    readonly (double Reservoir, double Screen) CommitSide(
        double vg,
        double screenNode,
        double factor,
        double supply,
        double plate,
        out double perTubePlate,
        out double perTubeScreen
    ) {
        double fromReservoir, fromScreen;
        if (screenTap > 0) {
            var vs = supply + screenTap * (plate - supply);
            perTubePlate = tube.PlateCurrent(vg, vs, plate, out _, out _, out _);
            perTubeScreen = tube.ScreenCurrent(vg, vs);
            fromReservoir = tubes * (perTubePlate + perTubeScreen); // the tap is on the primary, fed from B+
            fromScreen = 0;
        } else {
            perTubePlate = tube.PlateCurrent(factor, vg, screenNode, plate, out _, out _);
            perTubeScreen = tube.ScreenCurrent(vg, screenNode);
            fromReservoir = tubes * perTubePlate;
            fromScreen = tubes * perTubeScreen;
        }

        if (strapped is { } second) {
            fromReservoir += strappedTubes
                * (second.PlateCurrent(vg, plate, plate, out _, out _, out _)
                    + second.ScreenCurrent(vg, plate));
        }

        return (fromReservoir, fromScreen);
    }
}
