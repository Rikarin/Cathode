namespace Cathode.Dsp.Blocks;

/// <summary>The two inductor-less wah topologies <see cref="ActiveWahCircuit" /> builds.</summary>
public enum ActiveWahTopology {
    /// <summary>
    ///     An optical wah of the Morley kind: one op-amp multiple-feedback band-pass whose shunt resistor is a CdS
    ///     photocell, lit by a lamp through a vane on the treadle, then a non-inverting make-up stage.
    /// </summary>
    Optical,

    /// <summary>
    ///     A dual-gang wah of the Parapedal kind: two op-amps in a two-integrator loop whose integrating resistors are
    ///     the two gangs of the pedal's pot, the damping taken from a fixed resistor ratio (constant Q over the sweep).
    /// </summary>
    DualGang
}

/// <summary>
///     Component values of an inductor-less wah (see <see cref="ActiveWahTopology" />). Resistances in ohms,
///     capacitances in farads. Which fields a topology uses is noted on each.
/// </summary>
public sealed record ActiveWahVoicing(string Name, string ModelledAfter, ActiveWahTopology Topology) {
    /// <summary>Supply; the op-amps run from a half-supply reference.</summary>
    public double Supply { get; init; } = 9;

    /// <summary>How far below each rail an op-amp output saturates (a 4558-class part).</summary>
    public double Headroom { get; init; } = 1.5;

    /// <summary>
    ///     Optical: R1, from the input into the band-pass's middle node. Dual gang: the input resistor into the first
    ///     integrator, which with <see cref="DampingResistor" /> sets the gain at the peak.
    /// </summary>
    public double InputResistor { get; init; } = 33e3;

    /// <summary>Optical: R3, the feedback resistor from the op-amp's output to its inverting input.</summary>
    public double FeedbackResistor { get; init; } = 75e3;

    /// <summary>
    ///     Optical: C1 and C2, the band-pass's two capacitors (equal). Dual gang: the two integrating capacitors
    ///     (equal).
    /// </summary>
    public double Capacitor { get; init; } = 10e-9;

    /// <summary>Optical: the photocell fully lit (the treadle at the toe, the vane wide open).</summary>
    public double CellLit { get; init; } = 300;

    /// <summary>Optical: the photocell with the vane closed at the heel (the light that leaks past it).</summary>
    public double CellDark { get; init; } = 500e3;

    /// <summary>
    ///     Optical: how the vane opens with the treadle. The cell's resistance moves log-linearly with the opening, the
    ///     opening as the pedal position raised to this power (below 1: it opens quickly from the heel).
    /// </summary>
    public double VaneLaw { get; init; } = 0.58;

    /// <summary>Optical: the cell's time constant as the light rises (its resistance falls), seconds.</summary>
    public double CellAttack { get; init; } = 0.004;

    /// <summary>
    ///     Optical: the cell's time constant as the light falls (its resistance rises), seconds: CdS is slower so.
    /// </summary>
    public double CellRelease { get; init; } = 0.04;

    /// <summary>Dual gang: each gang's series resistor, the resistance left at the toe.</summary>
    public double SeriesResistor { get; init; } = 3.3e3;

    /// <summary>Dual gang: each gang of the pot.</summary>
    public double Pot { get; init; } = 25e3;

    /// <summary>Dual gang: the fraction of each gang in circuit with the heel and the toe down.</summary>
    public double HeelWiper { get; init; } = 0.992;

    public double ToeWiper { get; init; } = 0.001;

    /// <summary>
    ///     Dual gang: the pot's taper, the fraction of its track reached at half travel (see
    ///     <see cref="WahCircuit.Taper" />).
    /// </summary>
    public double TaperMidpoint { get; init; } = 0.24;

    /// <summary>
    ///     Dual gang: the damping network. The band-pass output feeds back through the divider of these two, so the Q
    ///     is their ratio (constant over the sweep) and the peak gain the damping resistor over the input resistor.
    /// </summary>
    public double DampingResistor { get; init; } = 47e3;

    public double DampingDivider { get; init; } = 10e3;

    /// <summary>
    ///     The make-up stage after the filter: non-inverting, gain 1 + feedback / ground (0 feedback: none).
    /// </summary>
    public double OutputFeedback { get; init; }

    public double OutputGround { get; init; } = 10e3;

    /// <summary>Output trim, so that the voicings sit near the other wah types' level.</summary>
    public double OutputDb { get; init; }

    /// <summary>Where an op-amp output saturates, either side of the reference.</summary>
    public double Rail => Supply / 2 - Headroom;

    /// <summary>The make-up stage's gain.</summary>
    public double OutputGain => 1 + OutputFeedback / OutputGround;

    /// <summary>
    ///     Optical: the photocell's settled resistance at pedal position <paramref name="toe" /> (0 heel, 1 toe).
    /// </summary>
    public double Cell(double toe) {
        var opening = Math.Pow(Math.Clamp(toe, 0, 1), VaneLaw);
        return CellDark * Math.Pow(CellLit / CellDark, opening);
    }

    /// <summary>
    ///     Dual gang: each gang's resistance at pedal position <paramref name="toe" />, series resistor included.
    /// </summary>
    public double Gang(double toe) =>
        SeriesResistor
        + Pot * (ToeWiper + (HeelWiper - ToeWiper) * WahCircuit.Taper(TaperMidpoint, 1 - Math.Clamp(toe, 0, 1)));

    /// <summary>
    ///     The settled small-signal response at a pedal position, from the parts alone: centre frequency (Hz), Q and
    ///     the gain at the peak (the make-up stage included). The analytic check for the simulation.
    /// </summary>
    public (double Frequency, double Q, double Gain) Response(double toe) {
        double omega, q, gain;
        if (Topology == ActiveWahTopology.Optical) {
            // Multiple feedback, equal capacitors: ω0² = (1/R1 + 1/R2) / (R3 C²), bandwidth 2 / (R3 C), peak R3 / 2R1.
            var c = Capacitor;
            omega = Math.Sqrt((1 / InputResistor + 1 / Cell(toe)) / (FeedbackResistor * c * c));
            q = omega * FeedbackResistor * c / 2;
            gain = FeedbackResistor / (2 * InputResistor);
        } else {
            // Two equal integrators: ω0 = 1 / (R C); Q and the peak gain from the damping network.
            omega = 1 / (Gang(toe) * Capacitor);
            q = DampingResistor / DampingDivider;
            gain = DampingResistor / InputResistor;
        }

        return (omega / (2 * Math.PI), q, gain * OutputGain);
    }
}

/// <summary>
///     One channel of an inductor-less wah (see <see cref="ActiveWahVoicing" />). Both topologies are a second-order
///     band-pass made of op-amps, so the linear circuit is solved exactly as a trapezoidal (bilinear) two-integrator
///     loop whose centre, Q and gain come from the parts; each op-amp output saturates softly at its rail, and that
///     limited voltage is what charges the capacitors, so a hot input compresses the resonance as the real op-amps do.
///     The optical type's photocell follows the lamp with its own lag, faster as the light rises than as it falls.
/// </summary>
/// <remarks>
///     The op-amps are otherwise ideal (no slew limit, no finite gain-bandwidth: at a few kilohertz a 4558-class part
///     is far from either). The pedal moves on a fixed grid of <c>samplesPerUpdate</c> samples
///     (<see cref="SetPosition" />), and the coefficients follow it there, so nothing is computed per sample beyond
///     the filter and the saturation.
/// </remarks>
public sealed class ActiveWahCircuit {
    readonly double sampleRate;
    readonly int samplesPerUpdate;
    double target; // optical: the cell's settled log resistance; dual gang: the gang resistance
    double cell; // optical: the cell's log resistance now
    bool snap = true;
    double toe;
    double capacitanceScale = 1;
    double qScale = 1;

    double g, k, denominator, scale, rail, outputGain;
    double s1, s2;

    /// <summary>The voicing set by the last <see cref="Configure" />.</summary>
    public ActiveWahVoicing Voicing { get; private set; }

    /// <summary>Optical: the photocell's resistance now, ohms (it lags the treadle).</summary>
    public double CellResistance => Math.Exp(cell);

    public ActiveWahCircuit(ActiveWahVoicing voicing, double sampleRate, int samplesPerUpdate = 16) {
        this.sampleRate = sampleRate;
        this.samplesPerUpdate = samplesPerUpdate;
        Voicing = voicing;
        Configure(voicing);
    }

    /// <summary>
    ///     Loads <paramref name="voicing" /> and resets; the next <see cref="SetPosition" /> lands at once.
    ///     Allocation-free.
    /// </summary>
    public void Configure(ActiveWahVoicing voicing) {
        Voicing = voicing;
        (capacitanceScale, qScale) = (1, 1);
        rail = voicing.Rail;
        outputGain = voicing.OutputGain;
        toe = 0.5;
        target = voicing.Topology == ActiveWahTopology.Optical ? Math.Log(voicing.Cell(toe)) : voicing.Gang(toe);
        cell = target;
        Design();
        Reset();
    }

    /// <summary>
    ///     Moves the treadle to <paramref name="toe" /> (0 heel, 1 toe). The optical cell then moves toward its new
    ///     resistance for <c>samplesPerUpdate</c> samples; right after <see cref="Configure" /> or <see cref="Reset" />
    ///     it lands there at once.
    /// </summary>
    public void SetPosition(double toe) {
        var v = Voicing;
        this.toe = Math.Clamp(toe, 0, 1);
        if (v.Topology == ActiveWahTopology.DualGang) {
            target = v.Gang(this.toe);
            Design();
            return;
        }

        target = Math.Log(v.Cell(this.toe));
        if (snap) {
            cell = target;
        } else if (cell != target) {
            var time = target < cell ? v.CellAttack : v.CellRelease;
            cell = target + (cell - target) * Math.Exp(-samplesPerUpdate / (time * sampleRate));
            if (Math.Abs(cell - target) < 1e-9) {
                cell = target;
            }
        }

        snap = false;
        Design();
    }

    /// <summary>
    ///     Scales the parts the way <see cref="WahCircuit.Trim" /> scales an inductor circuit's: the centre frequency
    ///     by one over the root of <paramref name="inductanceScale" /> (both capacitors move, there being no inductor)
    ///     and the Q by <paramref name="qScale" /> (the dual gang's damping resistor; the optical band-pass's R1 and R3
    ///     by its square and its capacitors by its inverse, which keeps the centre and the gain where they were).
    /// </summary>
    public void Trim(double inductanceScale, double qScale) {
        var capacitance = Math.Sqrt(inductanceScale);
        if (capacitance == capacitanceScale && qScale == this.qScale) {
            return;
        }

        (capacitanceScale, this.qScale) = (capacitance, qScale);
        Design();
    }

    /// <summary>Returns to rest: capacitors discharged, the cell settled where the treadle is.</summary>
    public void Reset() {
        s1 = 0;
        s2 = 0;
        snap = true;
        if (Voicing.Topology == ActiveWahTopology.Optical) {
            cell = Math.Log(Voicing.Cell(toe));
            Design();
        }
    }

    /// <summary>
    ///     One sample: the input in volts, returns the output in volts. Inverted, as the band-pass's op-amp inverts
    ///     (the block flips it back, as it does the inductor circuits' collector).
    /// </summary>
    public double Tick(double volts) {
        var hp = (volts - (k + g) * s1 - s2) * denominator;
        var bp = g * hp + s1;
        // The band-pass op-amp's output, limited at its rail; the capacitors charge from the limited voltage.
        var out1 = Saturate(-scale * bp);
        bp = -out1 / scale;
        s1 = 2 * bp - s1;
        var lp = g * bp + s2;
        if (Voicing.Topology == ActiveWahTopology.DualGang) {
            // The second integrator's op-amp swings as far as the first at the centre: limit it too.
            lp = Saturate(scale * lp) / scale;
        }

        s2 = 2 * lp - s2;
        if (Math.Abs(s1) < 1e-20) {
            s1 = 0;
        }

        if (Math.Abs(s2) < 1e-20) {
            s2 = 0;
        }

        return outputGain == 1 ? out1 : Saturate(outputGain * out1);
    }

    /// <summary>A soft rail: nearly linear to about half the swing, then bending over toward ±rail.</summary>
    double Saturate(double v) {
        var x = v / rail;
        var x2 = x * x;
        return v / Math.Sqrt(Math.Sqrt(1 + x2 * x2));
    }

    /// <summary>The loop's coefficients from the parts as now trimmed and set.</summary>
    void Design() {
        var v = Voicing;
        double omega, q, gain;
        if (v.Topology == ActiveWahTopology.Optical) {
            var q2 = qScale * qScale;
            var r1 = v.InputResistor * q2;
            var r3 = v.FeedbackResistor * q2;
            var c = v.Capacitor * capacitanceScale / qScale;
            omega = Math.Sqrt((1 / r1 + Math.Exp(-cell)) / (r3 * c * c));
            q = omega * r3 * c / 2;
            gain = r3 / (2 * r1);
        } else {
            var damping = v.DampingResistor * qScale;
            omega = 1 / (target * v.Capacitor * capacitanceScale);
            q = damping / v.DampingDivider;
            gain = damping / v.InputResistor;
        }

        g = Math.Tan(Math.Min(omega / (2 * sampleRate), 0.49 * Math.PI));
        k = 1 / q;
        denominator = 1 / (1 + g * (g + k));
        scale = gain * k; // the band-pass state has unit gain at the peak over k
    }
}
