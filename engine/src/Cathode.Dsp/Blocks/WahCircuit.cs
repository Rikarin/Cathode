using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Component values of an inductor wah of the Thomas Organ / Vox V846 / Cry Baby family (US patent 3,530,224).
///     Resistances in ohms, capacitances in farads, inductance in henries; the designators are those of Holters and
///     Zölzer's GCB-95 schematic (DAFx-11), whose node numbers <see cref="WahCircuit" /> keeps.
/// </summary>
/// <remarks>
///     Q1 is a common-emitter stage with collector-to-base bias (R4 to a node bypassed by C2, R8 to ground); Q2 an
///     emitter follower fed from Q1's collector through the pot (a divider: C4 into the pot, its wiper into Q2's base
///     through C5). Q2's emitter feeds back through C3 into the resonator, L1 across R7, and from there through R6 into
///     Q1's base. The pot scales the loop gain T₀, which pulls the resonator's frequency down by √(1 + T₀) and raises its
///     Q by the same factor; the output is Q1's collector, taken after C4 at the top of the pot.
/// </remarks>
public sealed record WahVoicing(string Name, string ModelledAfter) {
    /// <summary>Supply at the circuit, after the battery filter.</summary>
    public double Supply { get; init; } = 9;

    /// <summary>R1: input resistor, from a low-impedance source (an input buffer) into C1.</summary>
    public double InputResistor { get; init; } = 68e3;

    /// <summary>C1: input coupling into Q1's base.</summary>
    public double InputCapacitor { get; init; } = 10e-9;

    /// <summary>R2: Q1's collector load.</summary>
    public double Collector1 { get; init; } = 22e3;

    /// <summary>R3: Q1's emitter resistor (unbypassed: it sets the stage gain with R2).</summary>
    public double Emitter1 { get; init; } = 390;

    /// <summary>R4: collector-to-base bias, from Q1's collector to the bypassed bias node.</summary>
    public double BiasFeedback { get; init; } = 470e3;

    /// <summary>R5: Q2's base bias, from Q1's collector.</summary>
    public double Base2 { get; init; } = 470e3;

    /// <summary>R6: from the resonator into Q1's base.</summary>
    public double Feedback { get; init; } = 1.5e3;

    /// <summary>R7: across the inductor, setting the resonator's Q.</summary>
    public double QResistor { get; init; } = 33e3;

    /// <summary>R8: bias node to ground.</summary>
    public double BiasGround { get; init; } = 82e3;

    /// <summary>R9: Q2's emitter resistor.</summary>
    public double Emitter2 { get; init; } = 10e3;

    /// <summary>R10: Q2's collector resistor.</summary>
    public double Collector2 { get; init; } = 1e3;

    /// <summary>C2: bypasses the bias node, the AC ground of the resonator.</summary>
    public double Bypass { get; init; } = 4.7e-6;

    /// <summary>C3: the resonator's capacitor, from Q2's emitter.</summary>
    public double Tuning { get; init; } = 10e-9;

    /// <summary>C4 and C5: coupling into the pot and from its wiper into Q2's base.</summary>
    public double Coupling { get; init; } = 220e-9;

    /// <summary>L1.</summary>
    public double Inductance { get; init; } = 0.5;

    /// <summary>L1's winding (DC) resistance, in series with it. Must be positive.</summary>
    public double InductorResistance { get; init; } = 20;

    /// <summary>The pedal's pot.</summary>
    public double Pot { get; init; } = 100e3;

    public BjtModel Transistor { get; init; } = BjtModel.Q2N5088;

    /// <summary>
    ///     Wiper-to-ground fraction of the pot with the heel down (most signal into Q2: the lowest frequency) and the toe
    ///     down. The rack and gear never reach the pot's ends.
    /// </summary>
    public double HeelWiper { get; init; } = 0.992;

    public double ToeWiper { get; init; } = 0.001;

    /// <summary>
    ///     The taper: the fraction of the heel-to-toe resistance span reached at half travel (0.5 is linear; an audio
    ///     taper is about 0.1 to 0.2).
    /// </summary>
    public double TaperMidpoint { get; init; } = 0.15;

    /// <summary>Output trim, so that the voicings sit near the other wah types' level.</summary>
    public double OutputDb { get; init; }
}

/// <summary>
///     One channel of a component-level inductor wah (see <see cref="WahVoicing" />): a wave digital filter whose root is
///     an R-type adaptor holding both transistors (Ebers-Moll, four device ports) and every resistor, with the
///     capacitors, the inductor and the input source as its seven ports. The pot is a pair of internal resistors, so
///     moving it refactors the adaptor's matrix (<see cref="SetPosition" />, allocation-free) instead of rebuilding
///     anything. The circuit starts, and resets, at its DC operating point, which a second, resistive-only copy of the
///     circuit finds once per <see cref="Configure" />.
/// </summary>
/// <remarks>
///     <para>
///         Each sample is solved by a Newton iteration specialised to the circuit's normal operation, with both
///         base-collector junctions reverse biased (their exponentials are then below 1e-8 and drop out): the four
///         device-port equations v = p + F r(v) of the root then depend on the two base-emitter voltages only, so Newton
///         runs on two unknowns with a closed-form 2 × 2 step, and the base-collector voltages follow explicitly. When a
///         transistor saturates (a hot input at the heel), or the reduced iteration does not converge, the sample is
///         solved again by the generic four-port solver of <see cref="RTypeRoot{TPorts,TDevice}" />, which models the
///         whole Ebers-Moll transistor. Both find the same solution; the specialised one is several times cheaper.
///     </para>
///     <para>
///         Node numbers (1-based, ground is 0) follow Holters and Zölzer's GCB-95 schematic where it has them:
///         2 input (after R1), 3 Q1 base, 4 Q1 collector, 5 Q1 emitter, 6 bias node, 7 resonator top, 8 pot top (the
///         output), 9 wiper, 10 Q2 base, 11 Q2 collector, 12 Q2 emitter, 13 supply; node 1, the inductor's winding
///         resistance between L1 and the bias node, is ours.
///     </para>
/// </remarks>
public sealed class WahCircuit {
    /// <summary>Nodes besides ground; <see cref="OperatingPoint" /> has one voltage per node.</summary>
    public const int Nodes = 13;

    public const int WindingNode = 1;
    public const int InputNode = 2;
    public const int Base1 = 3;
    public const int Collector1 = 4;
    public const int Emitter1 = 5;
    public const int BiasNode = 6;
    public const int ResonatorNode = 7;
    public const int OutputNode = 8;
    public const int WiperNode = 9;
    public const int Base2 = 10;
    public const int Collector2 = 11;
    public const int Emitter2 = 12;
    public const int SupplyNode = 13;

    // Internal resistor indices, in netlist order (see Build).
    const int PotUpper = 10;
    const int PotLower = 11;
    const int QResistorIndex = 5;

    const int Devices = 4; // Q1 base-emitter, Q1 base-collector, Q2 base-emitter, Q2 base-collector
    const int Stride = 8; // row length of the reduced input matrix: at least the root's input count
    const int MaxIterations = 12;

    /// <summary>Base-collector voltage below which the junction's exponential is negligible (e^-19 at 26 mV).</summary>
    const double ReverseBiased = -0.5;

    readonly double sampleRate;
    readonly double[] operatingPoint = new double[Nodes];
    readonly double[] initialVoltages = new double[Devices];
    readonly double[] inputs; // u: the port waves, then the supply
    readonly double[] waves;
    readonly double[] reducedInputs = new double[Devices * Stride]; // E' = (I + G_c F)⁻¹ E
    readonly double[] reducedImpedance = new double[Devices * Devices]; // F' = (I + G_c F)⁻¹ F
    readonly double[] voltages = new double[Devices];
    readonly double[] previous = new double[Devices];
    readonly double[] residuals = new double[Devices]; // r = i - G_c v

    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor, Capacitor, Capacitor, Inductor>,
        Devices<Bjt, Bjt>> root;

    RTypeRoot<Ports<ResistiveVoltageSource>, Devices<Bjt, Bjt>> dc;
    double inductorCurrent;
    double inductanceScale = 1;
    double qScale = 1;
    double saturationCurrent;
    double forwardLeak;
    double reverseLeak;
    double vt;
    double critical;
    readonly double companion;

    /// <summary>The voicing set by the last <see cref="Configure" />.</summary>
    public WahVoicing Voicing { get; private set; }

    /// <summary>Node voltages at the DC operating point (index node - 1).</summary>
    public ReadOnlySpan<double> OperatingPoint => operatingPoint;

    /// <summary>Statistics of the generic solver, which only sees the samples the specialised one hands over.</summary>
    public NewtonSolver Newton => root.Newton;

    /// <summary>Samples solved by the generic four-port solver since the last <see cref="Configure" />.</summary>
    public long Fallbacks { get; private set; }

    /// <summary>Wiper-to-ground fraction of the pot, as of the last <see cref="SetPosition" />.</summary>
    public double Wiper { get; private set; } = double.NaN;

    public WahCircuit(WahVoicing voicing, double sampleRate) {
        this.sampleRate = sampleRate;
        Voicing = voicing;
        var net = Build(false);
        net.Probe(OutputNode);
        root = new(
            net,
            new(
                new(1e3),
                new(1e-9, sampleRate),
                new(1e-9, sampleRate),
                new(1e-9, sampleRate),
                new(1e-9, sampleRate),
                new(1e-9, sampleRate),
                new(1, sampleRate)
            ),
            new(new(voicing.Transistor), new(voicing.Transistor))
        );
        var scattering = root.Scattering;
        if (scattering.Inputs > Stride) {
            throw new InvalidOperationException("The reduced device rows are too short for the root's inputs.");
        }

        inputs = new double[scattering.Inputs];
        waves = new double[scattering.PortCount];
        companion = root.Device.A.Conductance(0);

        var dcNet = Build(true);
        for (var node = 1; node <= Nodes; node++) {
            dcNet.Probe(node);
        }

        dc = new(dcNet, new(new(1e3)), new(new(voicing.Transistor), new(voicing.Transistor)));
        dc.Newton.Extrapolate = false;
        dc.Newton.MaxIterations = 200;
        Configure(voicing);
    }

    /// <summary>
    ///     Pot setting for a pedal position, 0 (heel) to 1 (toe): the fraction of the pot between wiper and ground.
    /// </summary>
    public static double WiperFraction(WahVoicing voicing, double toe) {
        var taper = Taper(voicing.TaperMidpoint, 1 - Math.Clamp(toe, 0, 1));
        return voicing.ToeWiper + (voicing.HeelWiper - voicing.ToeWiper) * taper;
    }

    /// <summary>
    ///     A pot's taper: the fraction of its track at <paramref name="rotation" /> (0 … 1), exponential so that half
    ///     travel reaches <paramref name="midpoint" /> (0.5 is linear).
    /// </summary>
    public static double Taper(double midpoint, double rotation) {
        var m = midpoint;
        return Math.Abs(m - 0.5) < 1e-9
            ? rotation
            : (Math.Pow((1 - m) / m, 2 * rotation) - 1) / (Math.Pow((1 - m) / m, 2) - 1);
    }

    /// <summary>
    ///     Loads <paramref name="voicing" />'s component values, finds its DC operating point and resets the circuit there.
    ///     Allocation-free, so a type change can call it on the audio thread.
    /// </summary>
    public void Configure(WahVoicing voicing) {
        if (voicing.Transistor.Polarity != BjtPolarity.Npn) {
            throw new ArgumentException("The wah circuit is built with NPN transistors.", nameof(voicing));
        }

        Voicing = voicing;
        (inductanceScale, qScale) = (1, 1);
        var v = voicing;
        var model = v.Transistor;
        saturationCurrent = model.SaturationCurrent;
        forwardLeak = model.SaturationCurrent / model.ForwardBeta;
        reverseLeak = model.SaturationCurrent / model.ReverseBeta;
        vt = model.EmissionVoltage;
        critical = DeviceMath.CriticalVoltage(model.SaturationCurrent, vt);
        Fallbacks = 0;
        Span<double> resistors = stackalloc double[10];
        resistors[0] = v.Collector1;
        resistors[1] = v.Emitter1;
        resistors[2] = v.BiasFeedback;
        resistors[3] = v.Base2;
        resistors[4] = v.Feedback;
        resistors[5] = v.QResistor;
        resistors[6] = v.BiasGround;
        resistors[7] = v.Emitter2;
        resistors[8] = v.Collector2;
        resistors[9] = v.InductorResistance;
        for (var i = 0; i < resistors.Length; i++) {
            root.SetResistance(i, resistors[i]);
            dc.SetResistance(i, resistors[i]);
        }

        root.Ports.A.Value = v.InputResistor;
        dc.Ports.A.Value = v.InputResistor;
        root.Ports.B.Configure(v.InputCapacitor, sampleRate);
        root.Ports.C.Configure(v.Bypass, sampleRate);
        root.Ports.D.Configure(v.Tuning, sampleRate);
        root.Ports.E.Configure(v.Coupling, sampleRate);
        root.Ports.F.Configure(v.Coupling, sampleRate);
        root.Ports.G.Configure(v.Inductance, sampleRate);
        root.Device = new(new(v.Transistor), new(v.Transistor));
        dc.Device = new(new(v.Transistor), new(v.Transistor));
        root.SetVoltage(0, v.Supply);
        dc.SetVoltage(0, v.Supply);
        inputs[^1] = v.Supply;
        Wiper = double.NaN;
        SetPot(0.5, false);
        dc.SetResistance(PotUpper, 0.5 * v.Pot);
        dc.SetResistance(PotLower, 0.5 * v.Pot);
        dc.Propagate();
        Propagate();
        SolveOperatingPoint();
        Reset();
    }

    /// <summary>Moves the pot to pedal position <paramref name="toe" /> (0 heel, 1 toe) and refactors the adaptor.</summary>
    public void SetPosition(double toe) => SetPot(WiperFraction(Voicing, toe), true);

    /// <summary>
    ///     Scales the inductance and R7 (which sets the resonator's Q) against the voicing's values, refactoring the
    ///     adaptor when either changes. Neither moves the DC operating point (the inductor is a short at DC). Allocation
    ///     free; <see cref="Configure" /> returns both to 1.
    /// </summary>
    public void Trim(double inductanceScale, double qScale) {
        if (inductanceScale == this.inductanceScale && qScale == this.qScale) {
            return;
        }

        (this.inductanceScale, this.qScale) = (inductanceScale, qScale);
        root.Ports.G.Configure(Voicing.Inductance * inductanceScale, sampleRate);
        root.SetResistance(QResistorIndex, Voicing.QResistor * qScale);
        Propagate();
    }

    /// <summary>One sample: the input in volts at the source, returns the output (the top of the pot) in volts.</summary>
    public double Tick(double volts) {
        root.Ports.A.Volts = volts;
        var u = inputs.AsSpan();
        root.Ports.Reflect(u); // leaves only read their state, so the generic solver may reflect again
        if (!SolveForwardActive(u)) {
            return Fallback();
        }

        var scattering = root.Scattering;
        var r = residuals.AsSpan();
        var w = waves.AsSpan();
        var n = u.Length;
        for (var j = 0; j < w.Length; j++) {
            var row = scattering.Row(j);
            w[j] = RTypeScattering.Dot(row, u) + RTypeScattering.Dot(row[n..], r);
        }

        root.Ports.Incident(w);
        var probe = scattering.Row(scattering.ProbeRow);
        return RTypeScattering.Dot(probe, u) + RTypeScattering.Dot(probe[n..], r);
    }

    /// <summary>Returns to the DC operating point with no signal: every capacitor and the inductor precharged.</summary>
    public void Reset() {
        root.Reset();
        var p = operatingPoint.AsSpan();
        root.Ports.B.Precharge(Node(p, InputNode) - Node(p, Base1));
        root.Ports.C.Precharge(Node(p, BiasNode));
        root.Ports.D.Precharge(Node(p, ResonatorNode) - Node(p, Emitter2));
        root.Ports.E.Precharge(Node(p, Collector1) - Node(p, OutputNode));
        root.Ports.F.Precharge(Node(p, WiperNode) - Node(p, Base2));
        root.Ports.G.Precharge(inductorCurrent);
        root.SetInitialVoltages(initialVoltages);
        initialVoltages.CopyTo(voltages, 0);
        initialVoltages.CopyTo(previous, 0);
    }

    /// <summary>
    ///     The circuit's netlist: everything but the reactive parts is shared by the audio circuit and its DC copy, in
    ///     which the capacitors are open and the inductor a short.
    /// </summary>
    static WdfNetlist Build(bool dcOnly) {
        var net = new WdfNetlist();
        net.AddNodes(Nodes);
        const int Ground = WdfNetlist.Ground;

        net.Port(InputNode, Ground); // R1 and the source
        if (!dcOnly) {
            net.Port(InputNode, Base1); // C1
            net.Port(BiasNode, Ground); // C2
            net.Port(ResonatorNode, Emitter2); // C3
            net.Port(Collector1, OutputNode); // C4
            net.Port(WiperNode, Base2); // C5
            net.Port(ResonatorNode, WindingNode); // L1
        }

        net.Resistor(SupplyNode, Collector1, 22e3); // R2
        net.Resistor(Emitter1, Ground, 390); // R3
        net.Resistor(Collector1, BiasNode, 470e3); // R4
        net.Resistor(Collector1, Base2, 470e3); // R5
        net.Resistor(Base1, ResonatorNode, 1.5e3); // R6
        net.Resistor(ResonatorNode, BiasNode, 33e3); // R7
        net.Resistor(BiasNode, Ground, 82e3); // R8
        net.Resistor(Emitter2, Ground, 10e3); // R9
        net.Resistor(SupplyNode, Collector2, 1e3); // R10
        net.Resistor(WindingNode, BiasNode, 20); // L1's winding resistance
        net.Resistor(OutputNode, WiperNode, 50e3); // pot, top to wiper
        net.Resistor(WiperNode, Ground, 50e3); // pot, wiper to ground
        if (dcOnly) {
            net.Resistor(ResonatorNode, WindingNode, 1e-3); // L1 as a short
        }

        net.VoltageSource(SupplyNode, Ground);
        net.Transistor(Collector1, Base1, Emitter1);
        net.Transistor(Collector2, Base2, Emitter2);
        return net;
    }

    static double Node(ReadOnlySpan<double> voltages, int node) => voltages[node - 1];

    void SetPot(double fraction, bool propagate) {
        if (fraction == Wiper) {
            return;
        }

        Wiper = fraction;
        root.SetResistance(PotUpper, (1 - fraction) * Voicing.Pot);
        root.SetResistance(PotLower, fraction * Voicing.Pot);
        if (propagate) {
            Propagate();
        }
    }

    /// <summary>
    ///     Refactors the root, then reduces its device rows for the specialised solver: with r = i - G_c v, the rows
    ///     v = E u + F r become (I + F G_c) v = E u + F i, so v = E' u + F' i.
    /// </summary>
    void Propagate() {
        root.Propagate();
        var scattering = root.Scattering;
        var n = scattering.Inputs;
        Span<double> a = stackalloc double[Devices * Devices];
        Span<double> inverse = stackalloc double[Devices * Devices];
        for (var d = 0; d < Devices; d++) {
            var row = scattering.Row(scattering.DeviceRow + d);
            for (var k = 0; k < Devices; k++) {
                a[d * Devices + k] = (d == k ? 1 : 0) + row[n + k] * companion;
            }
        }

        if (!Invert(a, inverse)) {
            return; // keeps the previous reduction; the generic solver still has the right matrix
        }

        reducedInputs.AsSpan().Clear();
        reducedImpedance.AsSpan().Clear();
        for (var d = 0; d < Devices; d++) {
            for (var k = 0; k < Devices; k++) {
                var factor = inverse[d * Devices + k];
                var row = scattering.Row(scattering.DeviceRow + k);
                for (var c = 0; c < n; c++) {
                    reducedInputs[d * Stride + c] += factor * row[c];
                }

                for (var c = 0; c < Devices; c++) {
                    reducedImpedance[d * Devices + c] += factor * row[n + c];
                }
            }
        }
    }

    /// <summary>
    ///     Newton on the two base-emitter voltages with both base-collector junctions off (see the remarks on the type),
    ///     from the last two solutions extrapolated and junction-limited like the generic solver. Leaves the four port
    ///     voltages and residual currents; false when it did not converge or a base-collector junction turned on.
    /// </summary>
    bool SolveForwardActive(ReadOnlySpan<double> u) {
        var e = reducedInputs.AsSpan();
        var f = reducedImpedance.AsSpan();
        var p0 = RTypeScattering.Dot(e.Slice(0, u.Length), u);
        var p2 = RTypeScattering.Dot(e.Slice(2 * Stride, u.Length), u);
        var x0 = DeviceMath.LimitJunction(voltages[0], 2 * voltages[0] - previous[0], vt, critical);
        var x2 = DeviceMath.LimitJunction(voltages[2], 2 * voltages[2] - previous[2], vt, critical);
        double i0 = 0, i1 = 0, i2 = 0, i3 = 0;
        var converged = false;
        for (var iteration = 0; iteration < MaxIterations; iteration++) {
            var e0 = DeviceMath.LimitedExp(x0 / vt, out var d0);
            var e2 = DeviceMath.LimitedExp(x2 / vt, out var d2);
            i0 = saturationCurrent * e0 + forwardLeak * (e0 - 1);
            i1 = -saturationCurrent * e0 - reverseLeak;
            i2 = saturationCurrent * e2 + forwardLeak * (e2 - 1);
            i3 = -saturationCurrent * e2 - reverseLeak;
            var di0 = (saturationCurrent + forwardLeak) * d0 / vt;
            var di1 = -saturationCurrent * d0 / vt;
            var di2 = (saturationCurrent + forwardLeak) * d2 / vt;
            var di3 = -saturationCurrent * d2 / vt;

            var g0 = x0 - p0 - (f[0] * i0 + f[1] * i1 + f[2] * i2 + f[3] * i3);
            var g2 = x2 - p2 - (f[8] * i0 + f[9] * i1 + f[10] * i2 + f[11] * i3);
            var j00 = 1 - f[0] * di0 - f[1] * di1;
            var j02 = -f[2] * di2 - f[3] * di3;
            var j20 = -f[8] * di0 - f[9] * di1;
            var j22 = 1 - f[10] * di2 - f[11] * di3;
            var determinant = j00 * j22 - j02 * j20;
            if (!(Math.Abs(determinant) > 1e-300)) {
                return false;
            }

            var proposed0 = x0 - (g0 * j22 - g2 * j02) / determinant;
            var proposed2 = x2 - (j00 * g2 - j20 * g0) / determinant;
            var limited0 = DeviceMath.LimitJunction(x0, proposed0, vt, critical);
            var limited2 = DeviceMath.LimitJunction(x2, proposed2, vt, critical);
            var step0 = limited0 - x0;
            var step2 = limited2 - x2;
            x0 = limited0;
            x2 = limited2;
            if (limited0 == proposed0
                && limited2 == proposed2
                && Math.Abs(step0) <= 1e-6 + 1e-6 * Math.Abs(x0)
                && Math.Abs(step2) <= 1e-6 + 1e-6 * Math.Abs(x2)) {
                // The currents at the new voltages, to second order in the final step.
                i0 += di0 * step0;
                i1 += di1 * step0;
                i2 += di2 * step2;
                i3 += di3 * step2;
                converged = true;
                break;
            }
        }

        if (!converged || !double.IsFinite(x0) || !double.IsFinite(x2)) {
            return false;
        }

        var x1 = RTypeScattering.Dot(e.Slice(Stride, u.Length), u) + f[4] * i0 + f[5] * i1 + f[6] * i2 + f[7] * i3;
        var x3 = RTypeScattering.Dot(e.Slice(3 * Stride, u.Length), u)
            + f[12] * i0
            + f[13] * i1
            + f[14] * i2
            + f[15] * i3;
        if (!(x1 < ReverseBiased && x3 < ReverseBiased)) {
            return false;
        }

        voltages.CopyTo(previous, 0);
        voltages[0] = x0;
        voltages[1] = x1;
        voltages[2] = x2;
        voltages[3] = x3;
        residuals[0] = i0 - companion * x0;
        residuals[1] = i1 - companion * x1;
        residuals[2] = i2 - companion * x2;
        residuals[3] = i3 - companion * x3;
        return true;
    }

    /// <summary>The sample through the generic solver, started from the last solution.</summary>
    double Fallback() {
        Fallbacks++;
        root.SetInitialVoltages(voltages);
        var output = root.Tick();
        voltages.CopyTo(previous, 0);
        for (var d = 0; d < Devices; d++) {
            voltages[d] = root.DeviceVoltage(d);
        }

        return output;
    }

    /// <summary>
    ///     Gauss-Jordan inverse of a 4 × 4 matrix with partial pivoting, in place of <paramref name="a" />; false if
    ///     singular.
    /// </summary>
    static bool Invert(Span<double> a, Span<double> inverse) {
        inverse.Clear();
        for (var i = 0; i < Devices; i++) {
            inverse[i * Devices + i] = 1;
        }

        for (var column = 0; column < Devices; column++) {
            var pivot = column;
            for (var row = column + 1; row < Devices; row++) {
                if (Math.Abs(a[row * Devices + column]) > Math.Abs(a[pivot * Devices + column])) {
                    pivot = row;
                }
            }

            if (!(Math.Abs(a[pivot * Devices + column]) > 1e-300)) {
                return false;
            }

            if (pivot != column) {
                for (var k = 0; k < Devices; k++) {
                    (a[pivot * Devices + k], a[column * Devices + k]) = (a[column * Devices + k],
                        a[pivot * Devices + k]);
                    (inverse[pivot * Devices + k], inverse[column * Devices + k]) =
                        (inverse[column * Devices + k], inverse[pivot * Devices + k]);
                }
            }

            var scale = 1 / a[column * Devices + column];
            for (var k = 0; k < Devices; k++) {
                a[column * Devices + k] *= scale;
                inverse[column * Devices + k] *= scale;
            }

            for (var row = 0; row < Devices; row++) {
                if (row == column) {
                    continue;
                }

                var factor = a[row * Devices + column];
                for (var k = 0; k < Devices; k++) {
                    a[row * Devices + k] -= factor * a[column * Devices + k];
                    inverse[row * Devices + k] -= factor * inverse[column * Devices + k];
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Solves the resistive DC circuit by repeated Newton solves from a forward-active guess until the device
    ///     voltages stop moving, then stores the node voltages, the inductor's bias current and the start for Newton.
    /// </summary>
    void SolveOperatingPoint() {
        dc.Reset();
        dc.Ports.A.Volts = 0;
        Span<double> last = stackalloc double[4];
        last[0] = 0.6; // forward-active: base-emitter on, base-collector reversed
        last[1] = -2;
        last[2] = 0.6;
        last[3] = -2;
        dc.SetInitialVoltages(last);
        for (var i = 0; i < 100; i++) {
            dc.Tick();
            var settled = true;
            for (var d = 0; d < 4; d++) {
                settled &= Math.Abs(dc.DeviceVoltage(d) - last[d]) < 1e-12;
                last[d] = dc.DeviceVoltage(d);
            }

            if (settled) {
                break;
            }
        }

        for (var node = 1; node <= Nodes; node++) {
            operatingPoint[node - 1] = dc.Probe(node - 1);
        }

        var p = operatingPoint.AsSpan();
        inductorCurrent = (Node(p, WindingNode) - Node(p, BiasNode)) / Voicing.InductorResistance;
        for (var d = 0; d < 4; d++) {
            initialVoltages[d] = dc.DeviceVoltage(d);
        }
    }
}
