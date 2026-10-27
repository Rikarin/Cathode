using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A preamp stage inside the global feedback loop (the Marshall Major's V2A): a common-cathode triode whose cathode
///     takes the feedback, followed by a linear network with pots and switches (its tone stack) into the phase
///     inverter's grid. The feedback enters at the bottom of the cathode circuit: <paramref name="BypassedCathode" />
///     (bypassed: only its DC drop) above <paramref name="CathodeResistor" />, the unbypassed part, which is the
///     feedback network's shunt seen from the cathode (the shunt in parallel with the feedback resistor).
/// </summary>
/// <param name="Tube">The triode.</param>
/// <param name="Supply">Its plate supply.</param>
/// <param name="PlateResistor">Its plate resistor.</param>
/// <param name="BypassedCathode">The bypassed part of its cathode resistor (0 for none).</param>
/// <param name="CathodeResistor">The unbypassed part, where the feedback enters.</param>
/// <param name="Network">
///     The network from the plate (its <see cref="StageNetlist.Input" />, driven by the plate's open-circuit voltage
///     through the stage's output resistance) to the inverter's grid (its <see cref="StageNetlist.Output" />):
///     resistors, pots and capacitors, switched or not.
/// </param>
/// <param name="InputRange">The triode table's span, ± volts at its grid.</param>
public sealed record LoopStageDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double BypassedCathode,
    double CathodeResistor,
    StageNetlist Network,
    double InputRange = 16
);

/// <summary>
///     A <see cref="LoopStageDesign" /> at run time, inside the power amp's per-sample solve: the triode as a table
///     (its plate's open-circuit AC voltage as a function of its grid-to-feedback voltage, from the exact DC solution)
///     and the network as a linear discrete-time system (trapezoidal capacitors), whose output is an affine function of
///     this sample's input, so the power amp's scalar Newton keeps the whole loop without a delay.
/// </summary>
/// <remarks>
///     The triode's plate drives the network through its output resistance at rest (a Thévenin cut, as between preamp
///     stages). The bypassed part of the cathode holds its rest drop (its cap a short for the signal), and the
///     presence control's branch beside the shunt, which lowers the cathode's own degeneration at high frequencies, is
///     left out of the degeneration (it acts on the feedback, as in every model). The stage inverts, so the power amp
///     takes the feedback from the other end of the secondary (as the real amp's wiring does).
/// </remarks>
public sealed class LoopStage {
    const double Floor = 1; // ohms: a pot at an end stop

    readonly LoopTriode triode;
    readonly StageElement[] elements;
    readonly int nodes;
    readonly int input;
    readonly int output;
    readonly double[,] conductance;
    readonly double[,] inverse;
    readonly int[] capA; // capacitor terminals, 0-based node or -1 for ground
    readonly int[] capB;
    readonly StageElement[] capElements;
    readonly double[] capConductance;
    readonly double[] history;
    readonly double[] next;
    readonly double[] weights; // y = gain x + Σ weights h
    readonly double[] feeds; // v_c = feeds x + Σ coupling h
    readonly double[,] coupling;
    AmpControls controls;
    bool configured;
    double sampleRate;
    double gain;
    double supply = 1; // the preamp node's sag, as a fraction of nominal
    double inverseSupply = 1;

    public LoopStageDesign Design { get; }

    /// <summary>The plate's output resistance at rest, which drives the network.</summary>
    public double OutputResistance { get; }

    /// <summary>The triode's table (output A is the plate's AC voltage; B is unused).</summary>
    public PhaseInverter Table => triode;

    /// <summary>The network's output for this sample per volt of plate (the slope Newton sees).</summary>
    public double Gain => gain;

    public LoopStage(LoopStageDesign design, double sampleRate) {
        Design = design;
        triode = LoopTriode.For(design);
        OutputResistance = triode.OutputImpedance.Zaa;
        var net = design.Network;
        nodes = net.Nodes;
        input = net.Input - 1;
        output = net.Output - 1;
        foreach (var e in net.Elements) {
            if (e.Kind is not (ElementKind.Resistor or ElementKind.Capacitor)) {
                throw new ArgumentException(
                    "A loop stage's network takes resistors and capacitors only.",
                    nameof(design)
                );
            }
        }

        elements = [..net.Elements];
        capElements = [..net.Elements.Where(static e => e.Kind == ElementKind.Capacitor)];
        var count = capElements.Length;
        capA = [..capElements.Select(static e => e.A - 1)];
        capB = [..capElements.Select(static e => e.B - 1)];
        capConductance = new double[count];
        history = new double[count];
        next = new double[count];
        weights = new double[count];
        feeds = new double[count];
        coupling = new double[count, count];
        conductance = new double[nodes, nodes];
        inverse = new double[nodes, nodes];
        this.sampleRate = sampleRate;
        controls = new(0.5, 0.5, 0.5, 0.5, 0.5, false);
        Build();
    }

    /// <summary>
    ///     Applies the knobs and switches; rebuilds only when one the network uses moved. Allocation-free.
    /// </summary>
    public void Configure(in AmpControls controls) {
        if (configured && controls == this.controls) {
            return;
        }

        this.controls = controls;
        configured = true;
        Build();
    }

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        Build();
    }

    /// <summary>
    ///     The network's output (the inverter's grid, AC) for the stage's grid-to-feedback voltage
    ///     <paramref name="vd" /> this sample, and its slope; no state moves.
    /// </summary>
    public double Peek(double vd, out double slope) {
        var (plate, _) = triode.Evaluate(vd * inverseSupply, out var plateSlope, out _);
        plate *= supply;
        var y = gain * plate;
        for (var k = 0; k < history.Length; k++) {
            y += weights[k] * history[k];
        }

        slope = gain * plateSlope;
        return y;
    }

    /// <summary><see cref="Peek" />, and advances the network's state.</summary>
    public double Process(double vd) {
        var (plate, _) = triode.Evaluate(vd * inverseSupply, out _, out _);
        plate *= supply;
        var y = gain * plate;
        for (var k = 0; k < history.Length; k++) {
            y += weights[k] * history[k];
        }

        for (var k = 0; k < history.Length; k++) {
            var v = feeds[k] * plate;
            for (var j = 0; j < history.Length; j++) {
                v += coupling[k, j] * history[j];
            }

            next[k] = 2 * capConductance[k] * v - history[k];
        }

        Array.Copy(next, history, history.Length);
        return y;
    }

    /// <summary>
    ///     The stage's supply as a fraction of nominal (the preamp node's sag), set per sample: the triode's transfer
    ///     is scaled with it, its swing and its input range in proportion (exact for the scale-free part of the 3/2
    ///     law, a close approximation for the few percent a supply sags).
    /// </summary>
    public void SetSupply(double scale) {
        if (scale != supply && scale > 0) {
            supply = scale;
            inverseSupply = 1 / scale;
        }
    }

    public void Reset() => Array.Clear(history);

    public void FlushDenormals() {
        for (var k = 0; k < history.Length; k++) {
            if (Math.Abs(history[k]) < 1e-30) {
                history[k] = 0;
            }
        }
    }

    /// <summary>
    ///     The nodal matrix for the current knobs (resistors, the trapezoidal capacitors' conductances 2C/T, the
    ///     source's conductance at the input), its inverse, and the read-out rows.
    /// </summary>
    void Build() {
        Array.Clear(conductance);
        Stamp(input, -1, 1 / Math.Max(OutputResistance, Floor));
        var c = 0;
        foreach (var e in elements) {
            if (e.Kind == ElementKind.Resistor) {
                var on = e.Switch is not { } role || controls.IsOn(role);
                var ohms = !on ? e.OffValue : e.Switch is null || e.Value > 0 ? e.Value : 0;
                if (e.Part != PotPart.None) {
                    var fraction = e.Pot.Fraction(controls.Rotation(e.Role));
                    ohms = (e.Part == PotPart.Upper ? 1 - fraction : fraction) * e.Pot.Ohms;
                }

                if (ohms > 0 || e.Part != PotPart.None) {
                    Stamp(e.A - 1, e.B - 1, 1 / Math.Max(ohms, Floor));
                }
            } else {
                var farads = Math.Max(e.ValueAt(controls), 0);
                capConductance[c] = 2 * farads * sampleRate;
                if (farads == 0) {
                    history[c] = 0;
                }

                Stamp(e.A - 1, e.B - 1, capConductance[c]);
                c++;
            }
        }

        Invert();
        double At(int row, int column) => row < 0 || column < 0 ? 0 : inverse[row, column];
        double Across(int a, int b, int column) => At(a, column) - At(b, column);

        var source = 1 / Math.Max(OutputResistance, Floor);
        gain = At(output, input) * source;
        for (var k = 0; k < capA.Length; k++) {
            weights[k] = At(output, capA[k]) - At(output, capB[k]);
            feeds[k] = Across(capA[k], capB[k], input) * source;
            for (var j = 0; j < capA.Length; j++) {
                coupling[k, j] = Across(capA[k], capB[k], capA[j]) - Across(capA[k], capB[k], capB[j]);
            }
        }
    }

    void Stamp(int a, int b, double g) {
        if (a >= 0) {
            conductance[a, a] += g;
        }

        if (b >= 0) {
            conductance[b, b] += g;
        }

        if (a >= 0 && b >= 0) {
            conductance[a, b] -= g;
            conductance[b, a] -= g;
        }
    }

    /// <summary>
    ///     Gauss-Jordan with partial pivoting into <see cref="inverse" /> (the matrix is small; control thread).
    /// </summary>
    void Invert() {
        var n = nodes;
        Span<double> work = stackalloc double[n * 2 * n];
        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                work[i * 2 * n + j] = conductance[i, j];
                work[i * 2 * n + n + j] = i == j ? 1 : 0;
            }
        }

        for (var col = 0; col < n; col++) {
            var pivot = col;
            for (var r = col + 1; r < n; r++) {
                if (Math.Abs(work[r * 2 * n + col]) > Math.Abs(work[pivot * 2 * n + col])) {
                    pivot = r;
                }
            }

            if (pivot != col) {
                for (var j = 0; j < 2 * n; j++) {
                    (work[col * 2 * n + j], work[pivot * 2 * n + j]) = (work[pivot * 2 * n + j], work[col * 2 * n + j]);
                }
            }

            var scale = 1 / work[col * 2 * n + col];
            for (var j = 0; j < 2 * n; j++) {
                work[col * 2 * n + j] *= scale;
            }

            for (var r = 0; r < n; r++) {
                if (r == col) {
                    continue;
                }

                var f = work[r * 2 * n + col];
                if (f == 0) {
                    continue;
                }

                for (var j = 0; j < 2 * n; j++) {
                    work[r * 2 * n + j] -= f * work[col * 2 * n + j];
                }
            }
        }

        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                inverse[i, j] = work[i * 2 * n + n + j];
            }
        }
    }
}

/// <summary>
///     A <see cref="LoopStageDesign" />'s triode as a table: its plate's open-circuit AC voltage (output A; B is
///     unused) for its grid-to-feedback voltage, read like a phase inverter's.
/// </summary>
sealed class LoopTriode : PhaseInverter {
    static readonly Dictionary<LoopStageDesign, LoopTriode> Cache = [];

    LoopTriode(LoopStageDesign design) : base(design.InputRange) {
        var triode = new Triode(design.Tube);
        var (rp, rb, rk) = (design.PlateResistor, design.BypassedCathode, design.CathodeResistor);
        var rest = Current(triode, 0, design.Supply, rp, 0, rb + rk);
        var held = rest * rb; // the bypass cap's voltage
        RestGrid = 0;
        RestCathode = rest * (rb + rk);
        RestPlateA = design.Supply - rest * rp;
        const double Probe = 1e-6;
        var drawn = Current(triode, 0, design.Supply - Probe * rp, rp, held, rk);
        var z = (RestPlateA - (design.Supply - (drawn + Probe) * rp)) / Probe;
        OutputImpedance = (z, 0, 0, 0);
        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var current = Current(triode, vd, design.Supply, rp, held, rk);
            a[n] = design.Supply - current * rp - RestPlateA;
            gridVoltages[n] = vd - held - current * rk;
            cathodeResistances[n] = rk;
        }

        Differentiate();
    }

    public static LoopTriode For(LoopStageDesign design) {
        lock (Cache) {
            if (!Cache.TryGetValue(design, out var table)) {
                table = new(design);
                Cache[design] = table;
            }

            return table;
        }
    }

    /// <summary>
    ///     The current with the grid at <paramref name="grid" /> and the cathode at offset + I R: bisection.
    /// </summary>
    static double Current(Triode triode, double grid, double supply, double plate, double offset, double cathode) {
        var low = 0.0;
        var high = Math.Max(supply - offset, 0) / (plate + cathode);
        for (var i = 0; i < 100; i++) {
            var current = 0.5 * (low + high);
            var vk = offset + current * cathode;
            if (OperatingPoint.PlateCurrent(triode, grid - vk, supply - current * plate - vk) > current) {
                low = current;
            } else {
                high = current;
            }
        }

        return 0.5 * (low + high);
    }
}
