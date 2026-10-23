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
public readonly record struct PowerControls(
    double Presence,
    double Depth,
    double Cut,
    double Sag,
    double Bias,
    double Feedback
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
    public static PowerSettings Stock => new(Tube: null);
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

    static readonly PowerControls Unset = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
    static readonly Dictionary<PowerAmpDesign, double> AcResistances = [];

    readonly PowerAmpDesign design;
    readonly PhaseInverter phaseInverter;
    readonly TriodeTables inverterTube;
    readonly PhaseInverter inverterGridTables; // the inverter with its plates loaded, for its grid current
    readonly double acResistance; // the AC supply's charging resistance, as a multiple of the source resistance
    RTypeRoot<Ports<Inductor, Capacitor, Inductor, Inductor, Inductor, Capacitor>, PushPullStage> root;
    double sampleRate;
    double turns;
    double magnetising; // the magnetising inductance as set
    double unloadedSupply; // the rectifier's open-circuit voltage at stock mains
    double nominalPreamp; // the preamp node at idle with every knob at noon
    double reservoir;
    double screen;
    double preamp;
    double cathodeA;
    double cathodeB;
    double cathodeResistor; // all tubes' (with separate cathodes, each side's is twice it)
    double gridBias;
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
    double inverterGrid;
    double inverterCurrent;
    PentodeModel tube;
    PowerControls controls = Unset;
    PowerSettings settings = PowerSettings.Stock;

    public PowerAmpDesign Design => design;

    public PhaseInverter PhaseInverter => phaseInverter;

    public NewtonSolver Newton => root.Newton;

    /// <summary>The preamp node as a fraction of its nominal voltage (the preamp's sag), after the last sample.</summary>
    public double PreampSupply => preamp / nominalPreamp;

    /// <summary>B+ at the centre tap, in volts.</summary>
    public double Supply => reservoir;

    public double ScreenSupply => screen;

    public double PreampVoltage => preamp;

    /// <summary>The cathode voltage (cathode bias; side A's with separate cathodes), or 0.</summary>
    public double CathodeVoltage => cathodeA;

    /// <summary>The cathode resistor of a cathode-biased amp as the bias knob sets it (all tubes').</summary>
    public double CathodeResistor => cathodeResistor;

    /// <summary>The grids' DC voltage relative to ground (negative for fixed bias).</summary>
    public double GridBias => gridBias;

    /// <summary>The output tube in use.</summary>
    public PentodeModel Tube => tube;

    /// <summary>The phase inverter's driven grid (AC volts) at the last sample, with an <see cref="InverterInputDesign" />.</summary>
    public double InverterGrid => inverterGrid;

    /// <summary>The current through the inverter's coupling cap at the last sample (grid current and leak).</summary>
    public double InverterCurrent => inverterCurrent;

    /// <summary>Plate current per tube on each side at the last sample.</summary>
    public (double A, double B) PlateCurrents => (root.Device.PlateA, root.Device.PlateB);

    public (double A, double B) ScreenCurrents => (root.Device.ScreenCurrentA, root.Device.ScreenCurrentB);

    /// <summary>Plate voltages (relative to ground) at the last sample.</summary>
    public (double A, double B) PlateVoltages {
        get {
            var v = root.DeviceVoltage(0);
            return design.Topology == PowerTopology.SingleEnded
                ? (reservoir + v, reservoir)
                : (reservoir + 0.5 * v, reservoir - 0.5 * v);
        }
    }

    /// <summary>The control-grid-to-cathode voltages at the last sample.</summary>
    public (double A, double B) GridVoltages => (root.Device.GridA, root.Device.GridB);

    /// <summary>The core's flux linkage (volt-seconds) at the last sample.</summary>
    public double Flux => magnetising * root.Ports.A.Current;

    public PowerAmp(PowerAmpDesign design, double sampleRate) {
        this.design = design;
        this.sampleRate = sampleRate;
        tube = design.Tube;
        phaseInverter = PhaseInverter.For(design.PhaseInverter);
        inverterTube = TriodeTables.For(design.PhaseInverter.Tube);
        inverterGridTables = design.InverterInput is null ? phaseInverter : PhaseInverter.For(design.PhaseInverter, design.GridLeak);
        turns = design.Transformer.TurnsRatio;
        magnetising = design.Transformer.PrimaryInductance;
        reservoirCap = design.PowerSupply.Reservoir;
        bypassCap = design.CathodeBypass;
        rectifier = design.PowerSupply.Rectifier;
        lineFrequency = design.PowerSupply.LineFrequency;
        var n2 = turns * turns;
        var (rm, _, _) = design.Speaker.Motional;

        var net = new WdfNetlist();
        var (primary, leakage, secondary, coil, motional) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
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

        var stage = new PushPullStage(design, phaseInverter, sampleRate);
        root = new(net, Network(sampleRate), stage);
        root.Newton.RelativeTolerance = 1e-7;
        root.Newton.Linearise = true;
        inverterHalfStep = design.InverterInput is { } input ? 0.5 / (input.CouplingCap * sampleRate) : 0;

        // The rectifier's open-circuit voltage gives the design's B+ at idle with the knobs at noon.
        cathodeResistor = 1;
        Rebias();
        var noon = new PowerControls(0.5, 0, 0, 0.5, 0.5, 0.5);
        Configure(noon);
        var total = IdleCurrents(design.Supply, design.Supply, out _);
        var supply = design.PowerSupply;
        var draw = total.Plates + total.Screens + supply.PreampCurrent;
        unloadedSupply = design.Supply + supply.SourceResistance * draw + (rectifier?.Drop(draw) ?? 0);
        acResistance = CalibrateAc(draw);
        Reset();
        nominalPreamp = preamp;
    }

    /// <summary>The transformer and speaker, referred to the primary, for <paramref name="rate" />.</summary>
    Ports<Inductor, Capacitor, Inductor, Inductor, Inductor, Capacitor> Network(double rate) {
        var n2 = turns * turns;
        var (_, lm, cm) = design.Speaker.Motional;
        return new(
            new(design.Transformer.PrimaryInductance, rate),
            new(design.Transformer.PrimaryCapacitance, rate),
            new(design.Transformer.LeakageInductance, rate),
            new(design.Speaker.Inductance * n2, rate),
            new(lm * n2, rate),
            new(cm / n2, rate)
        );
    }

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        ApplyNetwork();
        root.Device.SetSampleRate(sampleRate, sourceResistance);
        inverterHalfStep = design.InverterInput is { } input ? 0.5 / (input.CouplingCap * sampleRate) : 0;
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
        if (design.Topology == PowerTopology.SingleEnded) {
            root.Device.SetSourceResistance(sourceResistance, sampleRate);
        }
    }

    /// <summary>Applies the power-amp knobs; recomputes only what changed.</summary>
    public void Configure(in PowerControls controls) {
        if (controls == this.controls) {
            return;
        }

        var previous = this.controls;
        this.controls = controls;
        if (controls.Presence != previous.Presence || controls.Depth != previous.Depth
            || controls.Feedback != previous.Feedback) {
            var amount = design.HasFeedback ? 2 * controls.Feedback : Math.Max(0, 2 * controls.Feedback - 1);
            // The secondary is the primary less the copper's drop into the nominal load (see the remarks).
            var ot = design.Transformer;
            var load = ot.PlateToPlate * settings.Match;
            var copper = load / (load + ot.WindingResistance);
            root.Device.SetFeedback(
                design.Feedback,
                controls.Presence,
                controls.Depth,
                amount * copper,
                turns,
                sampleRate,
                settings.PresenceFrequency,
                settings.DepthFrequency
            );
        }

        if (controls.Cut != previous.Cut) {
            root.Device.SetCut(controls.Cut, sampleRate);
        }

        if (controls.Sag != previous.Sag) {
            sagScale = Math.Pow(4, 2 * controls.Sag - 1); // 0.25x to 4x the stock source resistance
        }

        if (controls.Bias != previous.Bias) {
            // Fixed bias: the bias voltage from 1.2x (cold) to 0.8x (hot) of the stock one. Cathode bias: the cathode
            // resistor from twice to half the stock one.
            if (design.CathodeBias) {
                cathodeResistor = stockCathodeResistor * Math.Pow(2, 1 - 2 * controls.Bias);
            } else {
                gridBias = -stockBias * (1.2 - 0.4 * controls.Bias) * variac;
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
        var model = settings.Tube ?? design.Tube;
        if (model != tube || settings.Triode != previous.Triode) {
            tube = model;
            root.Device.SetTube(PentodeTables.For(model), ScreenTap(settings.Triode));
            Rebias();
            reset = true;
        }

        if (settings.Match != previous.Match || settings.LowFrequency != previous.LowFrequency
            || settings.HighFrequency != previous.HighFrequency || settings.Speaker != previous.Speaker
            || settings.Resonance != previous.Resonance || settings.ResonanceQ != previous.ResonanceQ
            || settings.ResonancePeak != previous.ResonancePeak || settings.VoiceCoil != previous.VoiceCoil
            || settings.Impedance != previous.Impedance) {
            turns = Math.Sqrt(design.Transformer.PlateToPlate * settings.Match / SpeakerDesign.Nominal);
            ApplyNetwork();
        }

        variac = settings.Variac;
        reservoirCap = design.PowerSupply.Reservoir * settings.TimeConstant;
        bypassCap = design.CathodeBypass * settings.CathodeTimeConstant;
        rectifier = settings.Rectifier ?? design.PowerSupply.Rectifier;
        lineFrequency = settings.Supply switch {
            SupplyMode.Dc => 0,
            SupplyMode.Ac60 => 60,
            SupplyMode.Ac50 => 50,
            _ => design.PowerSupply.LineFrequency
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
        reservoir = design.Supply;
        screen = design.Supply;
        var supply = design.PowerSupply;
        var unloaded = unloadedSupply * variac;
        var converged = false;
        for (var i = 0; i < 40; i++) {
            var total = IdleCurrents(reservoir, screen, out var vk);
            cathodeA = vk;
            cathodeB = vk;
            var sag = supply.SourceResistance * sagScale;
            var draw = total.Plates + total.Screens + supply.PreampCurrent;
            var next = unloaded - sag * draw - (rectifier?.Drop(draw) ?? 0);
            screen = next - supply.ChokeResistance * (total.Screens + supply.PreampCurrent);
            var change = Math.Abs(next - reservoir);
            reservoir = next;
            if (change < 1e-9) {
                converged = true;
                break;
            }
        }

        if (!converged || !(reservoir > 0) || !(screen > 0)) {
            SolveSupply(unloaded);
        }

        preamp = screen - supply.PreampResistance * supply.PreampCurrent;
        root.Reset();
        root.SetInitialVoltages([0]);
        ref var stage = ref root.Device;
        stage.Reset();
        var idle = IdlePerTube(reservoir, screen);
        stage.SetIdle(idle.Plate, idle.Screen);
        if (design.Topology == PowerTopology.SingleEnded) {
            // The idle current flows from B+ through the whole primary: the magnetising inductance carries it.
            var draw = IdleCurrents(reservoir, screen, out _).Plates;
            root.Ports.A.Precharge(-draw);
        }

        var all = IdleCurrents(reservoir, screen, out _);
        var rectified = all.Plates + all.Screens + supply.PreampCurrent;
        rectifierRoot = rectifier is { Perveance: > 0 } r ? Math.Cbrt(rectified / r.Perveance) : 0;
        phase = 0;
        inverterHistory = 0;
        inverterGrid = 0;
        excess = saturationFlux > 0 ? Excess(Flux) : 0;
        root.SetCurrent(0, -excess);
        Prime(ref stage);
    }

    /// <summary>One sample: <paramref name="input" /> volts at the phase inverter's grid; returns the 8 Ω speaker voltage.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double input) {
        ref var stage = ref root.Device;
        stage.Input = design.InverterInput is null ? input : DriveInverter(input);
        Prime(ref stage);
        root.Tick();

        // The supply and the cathode bias, from this sample's currents.
        var supply = design.PowerSupply;
        var dt = 1 / sampleRate;
        double plates, screens;
        if (stage.General) {
            plates = stage.ReservoirCurrent;
            screens = stage.ScreenNodeCurrent;
        } else {
            plates = design.TubesPerSide * (stage.PlateA + stage.PlateB);
            screens = design.TubesPerSide * (stage.ScreenCurrentA + stage.ScreenCurrentB);
        }

        if (!double.IsFinite(plates + screens + stage.GridCurrents)) {
            return root.Probe(0) / turns; // never let a bad sample into the supply's state
        }

        var fromRectifier = Rectify(supply);
        var toScreen = (reservoir - screen) / supply.ChokeResistance;
        var toPreamp = (screen - preamp) / supply.PreampResistance;
        reservoir += dt / reservoirCap * (Math.Max(fromRectifier, 0) - plates - toScreen);
        screen += dt / supply.ScreenCap * (toScreen - screens - toPreamp);
        preamp += dt / supply.PreampCap * (toPreamp - supply.PreampCurrent);
        if (design.CathodeBias) {
            if (design.SeparateCathodes) {
                var side = 2 * cathodeResistor;
                cathodeA += dt / bypassCap * (stage.CathodeCurrentA - cathodeA / side);
                cathodeB += dt / bypassCap * (stage.CathodeCurrentB - cathodeB / side);
            } else {
                cathodeA += dt / bypassCap * (plates + screens + stage.GridCurrents - cathodeA / cathodeResistor);
                cathodeB = cathodeA;
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
        inverterGrid = Flush.Value(inverterGrid);
        excess = Flush.Value(excess);
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

        var headroom = source - reservoir;
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
        var supply = design.PowerSupply;
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
            reservoir = plate;
            screen = Math.Max(node, 1e-3);
            cathodeA = vk;
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
            if (!AcResistances.TryGetValue(design, out var factor)) {
                factor = CalibrateAc(design, unloadedSupply, load);
                AcResistances[design] = factor;
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
        var ot = design.Transformer;
        var stock = ot.SaturationFlux;
        if (stock <= 0 && drive <= 0) {
            saturationFlux = 0;
            excess = 0;
            root.SetCurrent(0, 0);
            return;
        }

        if (stock <= 0) {
            // A core with the knee at eight times the flux of full power at 40 Hz: far out of the way at 0.
            var swing = Math.Sqrt(2 * design.RatedPower * ot.PlateToPlate);
            stock = 8 * swing / (2 * Math.PI * ReferenceFrequency);
            if (design.Topology == PowerTopology.SingleEnded) {
                stock += ot.PrimaryInductance * design.TubesPerSide * design.IdleCurrent;
            }
        }

        saturationFlux = stock / Math.Pow(2, 0.5 * Math.Max(drive, 0));
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
        var network = design.InverterInput!;
        var fed = root.Device.Fed; // the second grid, a sample behind: the cathode follows the pair's common mode
        var series = sourceResistance + inverterHalfStep;
        var leak = 1 / network.GridLeak;
        var e = input - inverterHistory;
        var v = inverterGrid;
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
            var f = (e - v) / series - (v - bootstrap) * leak - ig;
            var slope = -1 / series - (1 - returnSlope) * leak - currentSlope;
            var step = f / slope;
            v -= step;
            if (Math.Abs(step) < 1e-9 + 1e-9 * Math.Abs(v)) {
                break;
            }
        }

        var current = (e - v) / series;
        inverterCurrent = current;
        inverterHistory += 2 * inverterHalfStep * current;
        inverterGrid = v;
        return v;
    }

    /// <summary>The per-sample inputs of the device that come from the supply and the bias.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Prime(ref PushPullStage stage) {
        stage.SupplyA = reservoir - cathodeA;
        stage.SupplyB = reservoir - cathodeB;
        stage.SetScreens(
            screen - design.ScreenResistor * stage.ScreenCurrentA - cathodeA,
            screen - design.ScreenResistor * stage.ScreenCurrentB - cathodeB
        );
        var bias = design.CathodeBias ? 0 : gridBias;
        stage.GridOffsetA = bias - cathodeA;
        stage.GridOffsetB = bias - cathodeB;
    }

    /// <summary>The transformer and speaker values as the expert settings scale them, into the network.</summary>
    void ApplyNetwork() {
        var s = settings;
        var ot = design.Transformer;
        var speaker = s.Speaker ?? design.Speaker;
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
    double ScreenTap(bool triode) => triode && design.TriodeSwitch is not null ? 1 : design.UltralinearTap;

    /// <summary>The stock bias for the tube in use: the grid voltage, or the cathode resistor.</summary>
    void Rebias() {
        if (design.CathodeBias) {
            stockCathodeResistor = StockCathodeResistor();
            cathodeResistor = controls.Bias is var b && double.IsFinite(b)
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
            var count = 2 * design.TubesPerSide;
            return (count * idle.Plate, count * idle.Screen);
        }

        var vg = design.CathodeBias ? -idle.Cathode : gridBias;
        var (reservoirSide, screenSide) = SideCurrents(vg, plate - idle.Cathode, screen - idle.Cathode);
        return (design.Sides * reservoirSide, design.Sides * screenSide);
    }

    /// <summary>Whether the tubes' wiring leaves the plain push-pull pentode case (see <see cref="PushPullStage" />).</summary>
    bool IsGeneral => design.Topology == PowerTopology.SingleEnded || ScreenTap(settings.Triode) > 0
        || (design.Strapped is not null && design.StrappedPerSide > 0);

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
        var fromReservoir = design.TubesPerSide * (ip + (tap > 0 ? ig2 : 0));
        var fromScreens = tap > 0 ? 0 : design.TubesPerSide * ig2;
        if (design.Strapped is { } strapped && design.StrappedPerSide > 0) {
            fromReservoir += design.StrappedPerSide
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
        if (!design.CathodeBias) {
            var vs = tapped ? plate : ScreenAtIdle(tube, gridBias, screen);
            return (KorenPentode.Plate(tube, gridBias, vs, plate), KorenPentode.Screen(tube, gridBias, vs), 0);
        }

        if (!(plate > 0)) {
            return (0, 0, 0);
        }

        var (low, high) = (0.0, plate);
        var x0 = Math.Clamp(double.IsFinite(cathodeA) ? cathodeA : 0, 0.01 * plate, 0.5 * plate);
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
            return design.Sides * (fromReservoir + fromScreens) * cathodeResistor - vk;
        }

        var vs = ScreenAtIdle(tube, -vk, screen - vk);
        var count = 2 * design.TubesPerSide;
        var current = count * (KorenPentode.Plate(tube, -vk, vs, plate - vk) + KorenPentode.Screen(tube, -vk, vs));
        return current * cathodeResistor - vk;
    }

    /// <summary>
    ///     The screen voltage (relative to the cathode) after the screen resistor's drop, by fixed-point iteration (a
    ///     strong contraction: the screen resistor is small against the screen's slope resistance).
    /// </summary>
    double ScreenAtIdle(PentodeModel tube, double grid, double node) {
        var vs = node;
        for (var i = 0; i < 30; i++) {
            var next = node - design.ScreenResistor * KorenPentode.Screen(tube, grid, vs);
            if (Math.Abs(next - vs) < 1e-12) {
                return next;
            }

            vs = next;
        }

        return vs;
    }

    /// <summary>Fixed bias: the grid voltage that gives the design's idle current at the design's supply.</summary>
    double StockBias() {
        var (low, high) = (0.0, design.Supply);
        var tapped = ScreenTap(settings.Triode) > 0;
        for (var i = 0; i < 80; i++) {
            var bias = 0.5 * (low + high);
            var vs = tapped ? design.Supply : ScreenAtIdle(tube, -bias, design.Supply);
            if (KorenPentode.Plate(tube, -bias, vs, design.Supply) > design.IdleCurrent) {
                low = bias;
            } else {
                high = bias;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>Cathode bias: the resistor (all tubes') that gives the design's idle current at the design's supply.</summary>
    double StockCathodeResistor() {
        var (low, high) = (0.0, design.Supply);
        var tapped = ScreenTap(settings.Triode) > 0;
        for (var i = 0; i < 80; i++) {
            var vk = 0.5 * (low + high);
            var vs = tapped ? design.Supply - vk : ScreenAtIdle(tube, -vk, design.Supply - vk);
            if (KorenPentode.Plate(tube, -vk, vs, design.Supply - vk) > design.IdleCurrent) {
                low = vk;
            } else {
                high = vk;
            }
        }

        var bias = 0.5 * (low + high);
        var screenVoltage = tapped ? design.Supply - bias : ScreenAtIdle(tube, -bias, design.Supply - bias);
        var perTube = design.IdleCurrent + KorenPentode.Screen(tube, -bias, screenVoltage);
        return bias / (design.Sides * design.TubesPerSide * perTube);
    }
}

/// <summary>Koren's pentode equations (see <see cref="Pentode" />) for one tube, with the slopes the power stage needs.</summary>
public static class KorenPentode {
    /// <summary>Plate current at (V_g1k, V_g2k, V_pk).</summary>
    public static double Plate(in PentodeModel m, double vg1, double vg2, double vp) => Plate(m, vg1, vg2, vp, out _, out _);

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
    double b0;
    double b1;
    double a1;
    double state;

    public readonly double B0 => b0;

    /// <summary>The identity.</summary>
    public static FirstOrder Unity => new() { b0 = 1 };

    /// <summary>Bilinear transform of (n0 + n1 s) / (d0 + d1 s) at <paramref name="rate" />, keeping the state.</summary>
    public void Design(double n0, double n1, double d0, double d1, double rate) {
        var k = 2 * rate;
        var norm = 1 / (d0 + d1 * k);
        b0 = (n0 + n1 * k) * norm;
        b1 = (n0 - n1 * k) * norm;
        a1 = (d0 - d1 * k) * norm;
    }

    /// <summary>The output for <paramref name="x" /> without advancing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Peek(double x) => b0 * x + state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Process(double x) {
        var y = b0 * x + state;
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
    readonly PhaseInverter phaseInverter;
    readonly PentodeTables? strapped;
    readonly double tubes;
    readonly double strappedTubes;
    readonly bool singleEnded;
    readonly double coupling;
    readonly double gridLeak;
    readonly double gridStopper;
    readonly double cutCap;
    readonly Pot cutPot;
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

    public readonly int Ports => 1;

    /// <summary>Whether the general path is in use.</summary>
    public readonly bool General => singleEnded || screenTap > 0 || strappedTubes > 0;

    public PushPullStage(PowerAmpDesign design, PhaseInverter phaseInverter, double rate) {
        tube = PentodeTables.For(design.Tube);
        this.phaseInverter = phaseInverter;
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
        var z = phaseInverter.OutputImpedance;
        differentialResistance = z.Zaa + z.Zbb - z.Zab - z.Zba;
        presence = FirstOrder.Unity;
        depth = FirstOrder.Unity;
        cut = FirstOrder.Unity;
        SetSampleRate(rate, 30e3);
    }

    /// <summary>The grid networks for <paramref name="rate" />; single-ended, driven from <paramref name="source" /> ohms.</summary>
    public void SetSampleRate(double rate, double source) {
        var z = singleEnded ? (source, 0, 0, 1) : phaseInverter.OutputImpedance;
        grids.Configure(z, coupling, gridLeak, gridStopper, rate);
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
        var floor = 1 - 0.9 * Math.Clamp(depth, 0, 1);
        this.depth.Design(floor * omega, 1, omega, 1, rate);
        feedback = amount * design.TapGain / turns;
    }

    /// <summary>The cut control (0..1): its pot from the full track (least cut) down to nothing (most).</summary>
    public void SetCut(double amount, double rate) {
        hasCut = cutCap > 0 && !singleEnded;
        if (!hasCut) {
            return;
        }

        var r = cutPot.Ohms * (1 - cutPot.Fraction(amount));
        cut.Design(1, r * cutCap, 1, (r + differentialResistance) * cutCap, rate);
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
    public readonly void Evaluate(ReadOnlySpan<double> voltages, Span<double> currents, Span<double> jacobian, int stride) {
        if (General) {
            EvaluateGeneral(voltages[0], out currents[0], out jacobian[0]);
            return;
        }

        var v = voltages[0];

        // Feedback, phase inverter and cut: the grid drive as a function of v.
        var tap = feedback * v;
        var fed = depth.Peek(presence.Peek(tap));
        var fedSlope = feedback * presence.B0 * depth.B0;
        var (ea, eb) = phaseInverter.Evaluate(Input - fed, out var slopeA, out var slopeB);
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

        var (vgA, vgB) = grids.Peek(ea, eb, GridOffsetA, GridOffsetB, out var g, out _);
        var gridDriveA = g.AA * slopeA + g.AB * slopeB; // d(V_g1k) / dv on each side
        var gridDriveB = g.BA * slopeA + g.BB * slopeB;
        var ia = tube.PlateCurrent(factorA, vgA, ScreenA, SupplyA + 0.5 * v, out var gridSlopeA, out var plateSlopeA);
        var ib = tube.PlateCurrent(factorB, vgB, ScreenB, SupplyB - 0.5 * v, out var gridSlopeB, out var plateSlopeB);
        currents[0] = 0.5 * tubes * (ia - ib);
        jacobian[0] = 0.5 * tubes
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
            (ea, eb) = phaseInverter.Evaluate(Input - fed, out _, out _);
            if (hasCut) {
                var common = 0.5 * (ea + eb);
                var difference = cut.Process(ea - eb);
                (ea, eb) = (common + 0.5 * difference, common - 0.5 * difference);
            }
        }

        (GridA, GridB) = grids.Peek(ea, eb, GridOffsetA, GridOffsetB, out _, out var nodes);
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
        var (reservoirA, screenA) = CommitSide(GridA, ScreenA, factorA, SupplyA, SupplyA + half * v, out PlateA, out ScreenCurrentA);
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

        var (reservoirB, screenB) = CommitSide(GridB, ScreenB, factorB, SupplyB, SupplyB - half * v, out PlateB, out ScreenCurrentB);
        CathodeCurrentB = reservoirB + screenB + tubes * nodes.CurrentB;
        ReservoirCurrent += reservoirB;
        ScreenNodeCurrent += screenB;
    }

    public void Reset() {
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
        presence.FlushDenormals();
        depth.FlushDenormals();
        cut.FlushDenormals();
        grids.FlushDenormals();
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
            (ea, eb) = phaseInverter.Evaluate(Input - fed, out slopeA, out slopeB);
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

        var (vgA, vgB) = grids.Peek(ea, eb, GridOffsetA, GridOffsetB, out var g, out _);
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
            slope = tubes * (gs * gridDrive + ss * screenDrive + ps * plateDrive
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
            slope += strappedTubes * (gs * gridDrive + (ss + ps) * plateDrive
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
            fromReservoir += strappedTubes * (second.PlateCurrent(vg, plate, plate, out _, out _, out _)
                + second.ScreenCurrent(vg, plate));
        }

        return (fromReservoir, fromScreen);
    }
}
