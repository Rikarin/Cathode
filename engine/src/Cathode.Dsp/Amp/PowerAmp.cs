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

/// <summary>
///     A push-pull power amp (<see cref="PowerAmpDesign" />) as a wave digital filter with a single nonlinear port: the
///     output tubes, seen from the transformer primary, are a nonlinear one-port (<see cref="PushPullStage" />) at an
///     R-type root, and the transformer and speaker (magnetising and leakage inductance, copper, the speaker's
///     voice coil and resonance, all referred to the primary) are the linear network below it.
/// </summary>
/// <remarks>
///     <para>
///         The phase inverter, the cut control, the coupling caps and grid networks and the feedback network are folded
///         into the device: each is either static (<see cref="LongTailPair" />) or linear with one state (solved in
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
///         The supply (reservoir, screen node, preamp node) and the cathode bias of a cathode-biased amp are slow, so
///         they are integrated explicitly from the tubes' currents after each sample.
///     </para>
/// </remarks>
public sealed class PowerAmp {
    static readonly PowerControls Unset = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

    readonly PowerAmpDesign design;
    readonly LongTailPair phaseInverter;
    readonly double turns;
    RTypeRoot<Ports<Inductor, Capacitor, Inductor, Inductor, Inductor, Capacitor>, PushPullStage> root;
    double sampleRate;
    double unloadedSupply; // the rectifier's open-circuit voltage
    double nominalPreamp; // the preamp node at idle with every knob at noon
    double reservoir;
    double screen;
    double preamp;
    double cathode;
    double cathodeResistor;
    double gridBias;
    double sagScale = 1;
    double stockBias; // the fixed bias that gives the design's idle current at the design's supply
    double stockCathodeResistor; // the same for a cathode-biased amp
    PowerControls controls = Unset;

    public PowerAmpDesign Design => design;

    public LongTailPair PhaseInverter => phaseInverter;

    public NewtonSolver Newton => root.Newton;

    /// <summary>The preamp node as a fraction of its nominal voltage (the preamp's sag), after the last sample.</summary>
    public double PreampSupply => preamp / nominalPreamp;

    /// <summary>B+ at the centre tap, in volts.</summary>
    public double Supply => reservoir;

    public double ScreenSupply => screen;

    public double PreampVoltage => preamp;

    /// <summary>Shared cathode voltage (cathode bias), or 0.</summary>
    public double CathodeVoltage => cathode;

    /// <summary>The shared cathode resistor of a cathode-biased amp, as the bias knob sets it.</summary>
    public double CathodeResistor => cathodeResistor;

    /// <summary>The grids' DC voltage relative to ground (negative for fixed bias).</summary>
    public double GridBias => gridBias;

    /// <summary>Plate current per tube on each side at the last sample.</summary>
    public (double A, double B) PlateCurrents => (root.Device.PlateA, root.Device.PlateB);

    public (double A, double B) ScreenCurrents => (root.Device.ScreenCurrentA, root.Device.ScreenCurrentB);

    /// <summary>Plate voltages (relative to ground) at the last sample.</summary>
    public (double A, double B) PlateVoltages {
        get {
            var v = root.DeviceVoltage(0);
            return (reservoir + 0.5 * v, reservoir - 0.5 * v);
        }
    }

    /// <summary>The control-grid-to-cathode voltages at the last sample.</summary>
    public (double A, double B) GridVoltages => (root.Device.GridA, root.Device.GridB);

    public PowerAmp(PowerAmpDesign design, double sampleRate) {
        this.design = design;
        this.sampleRate = sampleRate;
        phaseInverter = LongTailPair.For(design.PhaseInverter);
        turns = design.Transformer.TurnsRatio;
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

        var stage = new PushPullStage(design, phaseInverter, sampleRate);
        root = new(net, Network(sampleRate), stage);
        root.Newton.RelativeTolerance = 1e-7;

        // The rectifier's open-circuit voltage gives the design's B+ at idle with the knobs at noon.
        cathodeResistor = 1;
        if (design.CathodeBias) {
            stockCathodeResistor = StockCathodeResistor();
        } else {
            stockBias = StockBias();
        }

        var noon = new PowerControls(0.5, 0, 0, 0.5, 0.5, 0.5);
        Configure(noon);
        var total = IdleCurrents(design.Supply, design.Supply, out _);
        var supply = design.PowerSupply;
        unloadedSupply = design.Supply + supply.SourceResistance * (total.Plates + total.Screens + supply.PreampCurrent);
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
        var n2 = turns * turns;
        var (_, lm, cm) = design.Speaker.Motional;
        root.Ports.A.Configure(design.Transformer.PrimaryInductance, sampleRate);
        root.Ports.B.Configure(design.Transformer.PrimaryCapacitance, sampleRate);
        root.Ports.C.Configure(design.Transformer.LeakageInductance, sampleRate);
        root.Ports.D.Configure(design.Speaker.Inductance * n2, sampleRate);
        root.Ports.E.Configure(lm * n2, sampleRate);
        root.Ports.F.Configure(cm / n2, sampleRate);
        root.Propagate();
        root.Device.SetSampleRate(sampleRate);
        var c = controls;
        controls = Unset;
        Configure(c);
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
            var copper = ot.PlateToPlate / (ot.PlateToPlate + ot.WindingResistance);
            root.Device.SetFeedback(design.Feedback, controls.Presence, controls.Depth, amount * copper, turns, sampleRate);
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
                gridBias = -stockBias * (1.2 - 0.4 * controls.Bias);
            }
        }
    }

    /// <summary>
    ///     The operating point for the current knobs: supply nodes, bias and every state at rest. Allocation-free and
    ///     a few tens of microseconds, so a type change can call it on the audio thread.
    /// </summary>
    public void Reset() {
        reservoir = design.Supply;
        screen = design.Supply;
        var supply = design.PowerSupply;
        for (var i = 0; i < 40; i++) {
            var total = IdleCurrents(reservoir, screen, out var vk);
            cathode = vk;
            var sag = supply.SourceResistance * sagScale;
            var next = unloadedSupply - sag * (total.Plates + total.Screens + supply.PreampCurrent);
            screen = next - supply.ChokeResistance * (total.Screens + supply.PreampCurrent);
            var change = Math.Abs(next - reservoir);
            reservoir = next;
            if (change < 1e-9) {
                break;
            }
        }

        preamp = screen - supply.PreampResistance * supply.PreampCurrent;
        root.Reset();
        root.SetInitialVoltages([0]);
        ref var stage = ref root.Device;
        stage.Reset();
        var idle = IdlePerTube(reservoir, screen);
        stage.SetIdle(idle.Plate, idle.Screen);
        Prime(ref stage);
    }

    /// <summary>One sample: <paramref name="input" /> volts at the phase inverter's grid; returns the 8 Ω speaker voltage.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double input) {
        ref var stage = ref root.Device;
        stage.Input = input;
        Prime(ref stage);
        root.Tick();

        // The supply and the cathode bias, from this sample's currents.
        var supply = design.PowerSupply;
        var dt = 1 / sampleRate;
        var plates = design.TubesPerSide * (stage.PlateA + stage.PlateB);
        var screens = design.TubesPerSide * (stage.ScreenCurrentA + stage.ScreenCurrentB);
        if (!double.IsFinite(plates + screens + stage.GridCurrents)) {
            return root.Probe(0) / turns; // never let a bad sample into the supply's state
        }

        var fromRectifier = (unloadedSupply - reservoir) / (supply.SourceResistance * sagScale);
        var toScreen = (reservoir - screen) / supply.ChokeResistance;
        var toPreamp = (screen - preamp) / supply.PreampResistance;
        reservoir += dt / supply.Reservoir * (Math.Max(fromRectifier, 0) - plates - toScreen);
        screen += dt / supply.ScreenCap * (toScreen - screens - toPreamp);
        preamp += dt / supply.PreampCap * (toPreamp - supply.PreampCurrent);
        if (design.CathodeBias) {
            cathode += dt / design.CathodeBypass * (plates + screens + stage.GridCurrents - cathode / cathodeResistor);
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
    }

    /// <summary>The per-sample inputs of the device that come from the supply and the bias.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Prime(ref PushPullStage stage) {
        stage.Supply = reservoir - cathode;
        stage.SetScreens(
            screen - design.ScreenResistor * stage.ScreenCurrentA - cathode,
            screen - design.ScreenResistor * stage.ScreenCurrentB - cathode
        );
        stage.GridOffset = (design.CathodeBias ? 0 : gridBias) - cathode;
    }

    /// <summary>Total plate and screen currents of all tubes at idle for the given nodes; the cathode voltage out.</summary>
    (double Plates, double Screens) IdleCurrents(double plate, double screen, out double cathode) {
        var idle = IdlePerTube(plate, screen);
        cathode = idle.Cathode;
        var count = 2 * design.TubesPerSide;
        return (count * idle.Plate, count * idle.Screen);
    }

    /// <summary>
    ///     One tube at idle: plate and screen current and (cathode bias) the cathode voltage, which solves
    ///     V_k = R_k × (all plate and screen currents) by the secant method from the last cathode voltage, kept inside a
    ///     shrinking bracket.
    /// </summary>
    (double Plate, double Screen, double Cathode) IdlePerTube(double plate, double screen) {
        var tube = design.Tube;
        if (!design.CathodeBias) {
            var vs = ScreenAtIdle(tube, gridBias, screen);
            return (KorenPentode.Plate(tube, gridBias, vs, plate), KorenPentode.Screen(tube, gridBias, vs), 0);
        }

        var (low, high) = (0.0, plate);
        var x0 = Math.Clamp(cathode, 0.01 * plate, 0.5 * plate);
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
        var screenVoltage = ScreenAtIdle(tube, -bias, screen - bias);
        return (
            KorenPentode.Plate(tube, -bias, screenVoltage, plate - bias),
            KorenPentode.Screen(tube, -bias, screenVoltage),
            bias
        );
    }

    /// <summary>R_k × (all plate and screen currents at idle) - V_k, for a cathode at <paramref name="vk" />.</summary>
    double CathodeResidual(double vk, double plate, double screen) {
        var tube = design.Tube;
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
        var tube = design.Tube;
        var (low, high) = (0.0, design.Supply);
        for (var i = 0; i < 80; i++) {
            var bias = 0.5 * (low + high);
            var vs = ScreenAtIdle(tube, -bias, design.Supply);
            if (KorenPentode.Plate(tube, -bias, vs, design.Supply) > design.IdleCurrent) {
                low = bias;
            } else {
                high = bias;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>Cathode bias: the shared resistor that gives the design's idle current at the design's supply.</summary>
    double StockCathodeResistor() {
        var tube = design.Tube;
        var (low, high) = (0.0, design.Supply);
        for (var i = 0; i < 80; i++) {
            var vk = 0.5 * (low + high);
            var vs = ScreenAtIdle(tube, -vk, design.Supply - vk);
            if (KorenPentode.Plate(tube, -vk, vs, design.Supply - vk) > design.IdleCurrent) {
                low = vk;
            } else {
                high = vk;
            }
        }

        var bias = 0.5 * (low + high);
        var screenVoltage = ScreenAtIdle(tube, -bias, design.Supply - bias);
        var perTube = design.IdleCurrent + KorenPentode.Screen(tube, -bias, screenVoltage);
        return bias / (2 * design.TubesPerSide * perTube);
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
    ///     The grid-cathode voltages for AC source voltages <paramref name="ea" />, <paramref name="eb" /> and the grids'
    ///     DC offset from the cathode <paramref name="offset" />, with their slopes (∂a/∂ea, ∂a/∂eb, ∂b/∂ea, ∂b/∂eb), and
    ///     the grid-leak nodes and grid currents for <see cref="Commit" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly (double A, double B) Peek(
        double ea,
        double eb,
        double offset,
        out (double AA, double AB, double BA, double BB) slope,
        out (double A, double B, double CurrentA, double CurrentB) nodes
    ) {
        var threshold = Threshold - offset;
        var (onA, onB) = (conductingA, conductingB);
        double ya = 0, yb = 0, inverse = 0;
        double a00 = 0, a01 = 0, a10 = 0, a11 = 0;
        for (var attempt = 0; attempt < 4; attempt++) {
            // Each grid-leak node draws i = s y + c; with the plates at e - Z i, the branches give A y = r.
            var sa = onA ? leak + diode : leak;
            var sb = onB ? leak + diode : leak;
            var ca = onA ? -diode * threshold : 0;
            var cb = onB ? -diode * threshold : 0;
            a00 = zaa * sa + 1;
            a01 = zab * sb;
            a10 = zba * sa;
            a11 = zbb * sb + 1;
            inverse = 1 / (a00 * a11 - a01 * a10);
            var ra = ea - historyA - zaa * ca - zab * cb;
            var rb = eb - historyB - zba * ca - zbb * cb;
            ya = (a11 * ra - a01 * rb) * inverse;
            yb = (a00 * rb - a10 * ra) * inverse;
            var (nextA, nextB) = (ya > threshold, yb > threshold);
            if (nextA == onA && nextB == onB) {
                break;
            }

            (onA, onB) = (nextA, nextB);
        }

        var currentA = onA ? diode * (ya - threshold) : 0;
        var currentB = onB ? diode * (yb - threshold) : 0;
        var ka = onA ? 1 - stopper * diode : 1;
        var kb = onB ? 1 - stopper * diode : 1;
        slope = (ka * a11 * inverse, -ka * a01 * inverse, -kb * a10 * inverse, kb * a00 * inverse);
        nodes = (ya, yb, currentA, currentB);
        return (offset + ya - stopper * currentA, offset + yb - stopper * currentB);
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
///     pair (equal power on both sides of the transformer).
/// </summary>
struct PushPullStage : INonlinearDevice {
    readonly PentodeTables tube;
    readonly LongTailPair phaseInverter;
    readonly double tubes;
    readonly double coupling;
    readonly double gridLeak;
    readonly double gridStopper;
    readonly double cutCap;
    readonly Pot cutPot;
    readonly double differentialResistance;
    FirstOrder presence;
    FirstOrder depth;
    FirstOrder cut;
    GridPair grids;
    double feedback; // feedback knob × tap gain / turns ratio: volts at the feedback filter per volt of primary
    bool hasCut;
    double factorA; // the plate current's screen factor per side, per sample
    double factorB;

    /// <summary>The phase inverter's grid voltage (AC), set per sample.</summary>
    public double Input;

    /// <summary>B+ at the centre tap relative to the cathode, set per sample.</summary>
    public double Supply;

    /// <summary>Screen voltages relative to the cathode (<see cref="SetScreens" />).</summary>
    public double ScreenA;

    public double ScreenB;

    /// <summary>The grids' DC voltage relative to the cathode (the bias), set per sample.</summary>
    public double GridOffset;

    /// <summary>Per tube, at the last committed sample.</summary>
    public double PlateA;

    public double PlateB;

    public double ScreenCurrentA;

    public double ScreenCurrentB;

    /// <summary>Grid-cathode voltages at the last committed sample.</summary>
    public double GridA;

    public double GridB;

    /// <summary>All grid currents at the last committed sample.</summary>
    public double GridCurrents;

    public readonly int Ports => 1;

    public PushPullStage(PowerAmpDesign design, LongTailPair phaseInverter, double rate) {
        tube = PentodeTables.For(design.Tube);
        this.phaseInverter = phaseInverter;
        tubes = design.TubesPerSide;
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
        SetSampleRate(rate);
    }

    public void SetSampleRate(double rate) {
        grids.Configure(phaseInverter.OutputImpedance, coupling, gridLeak, gridStopper, rate);
    }

    /// <summary>
    ///     The feedback filter: presence (0..1) shorting the shunt resistor at high frequencies, depth (0..1) cutting the
    ///     feedback below the design's depth frequency to a tenth, and <paramref name="amount" /> times the stock
    ///     feedback.
    /// </summary>
    public void SetFeedback(FeedbackDesign design, double presence, double depth, double amount, double turns, double rate) {
        var rp = design.Presence.Ohms * (1 - design.Presence.Fraction(presence));
        var c = design.PresenceCap;
        var (rf, rx) = (design.FeedbackResistor, design.ShuntResistor);
        var tau1 = rp * c;
        var tau2 = c * (rf * (rx + rp) + rx * rp) / (rf + rx);
        this.presence.Design(design.Beta, design.Beta * tau1, 1, tau2, rate);
        var omega = 2 * Math.PI * design.DepthFrequency;
        var floor = 1 - 0.9 * Math.Clamp(depth, 0, 1);
        this.depth.Design(floor * omega, 1, omega, 1, rate);
        feedback = amount * design.TapGain / turns;
    }

    /// <summary>The cut control (0..1): its pot from the full track (least cut) down to nothing (most).</summary>
    public void SetCut(double amount, double rate) {
        hasCut = cutCap > 0;
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

        var (vgA, vgB) = grids.Peek(ea, eb, GridOffset, out var g, out _);
        var gridDriveA = g.AA * slopeA + g.AB * slopeB; // d(V_g1k) / dv on each side
        var gridDriveB = g.BA * slopeA + g.BB * slopeB;
        var ia = tube.PlateCurrent(factorA, vgA, ScreenA, Supply + 0.5 * v, out var gridSlopeA, out var plateSlopeA);
        var ib = tube.PlateCurrent(factorB, vgB, ScreenB, Supply - 0.5 * v, out var gridSlopeB, out var plateSlopeB);
        currents[0] = 0.5 * tubes * (ia - ib);
        jacobian[0] = 0.5 * tubes
            * (0.5 * plateSlopeA + gridSlopeA * gridDriveA + 0.5 * plateSlopeB - gridSlopeB * gridDriveB);
    }

    /// <summary>Plates may fall to a tenth per Newton step (<see cref="TubeLimit.Plate" />).</summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        var v = previous[0];
        var next = proposed[0];
        if (next > v) {
            var plate = TubeLimit.Plate(Supply - 0.5 * v, Supply - 0.5 * next);
            proposed[0] = 2 * (Supply - plate);
        } else {
            var plate = TubeLimit.Plate(Supply + 0.5 * v, Supply + 0.5 * next);
            proposed[0] = 2 * (plate - Supply);
        }
    }

    /// <summary>Advances every state for the converged primary voltage, and records the currents.</summary>
    public void Commit(ReadOnlySpan<double> voltages) {
        var v = voltages[0];
        var fed = depth.Process(presence.Process(feedback * v));
        var (ea, eb) = phaseInverter.Evaluate(Input - fed, out _, out _);
        if (hasCut) {
            var common = 0.5 * (ea + eb);
            var difference = cut.Process(ea - eb);
            (ea, eb) = (common + 0.5 * difference, common - 0.5 * difference);
        }

        (GridA, GridB) = grids.Peek(ea, eb, GridOffset, out _, out var nodes);
        grids.Commit(nodes);
        GridCurrents = tubes * (nodes.CurrentA + nodes.CurrentB);
        PlateA = tube.PlateCurrent(factorA, GridA, ScreenA, Supply + 0.5 * v, out _, out _);
        PlateB = tube.PlateCurrent(factorB, GridB, ScreenB, Supply - 0.5 * v, out _, out _);
        ScreenCurrentA = tube.ScreenCurrent(GridA, ScreenA);
        ScreenCurrentB = tube.ScreenCurrent(GridB, ScreenB);
    }

    public void Reset() {
        presence.Reset();
        depth.Reset();
        cut.Reset();
        grids.Reset();
        GridA = GridOffset;
        GridB = GridOffset;
        GridCurrents = 0;
    }

    public void FlushDenormals() {
        presence.FlushDenormals();
        depth.FlushDenormals();
        cut.FlushDenormals();
        grids.FlushDenormals();
    }
}
