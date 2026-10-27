using Cathode.Dsp.Amp;

namespace Cathode.Dsp.Filters;

/// <summary>One series L-C-R resonator of an <see cref="LcrGraphicEq" />: henries, farads and its series ohms.</summary>
public readonly record struct LcrBranch(double Inductance, double Capacitance, double Resistance) {
    /// <summary>Its resonance, Hz.</summary>
    public double Frequency => 1 / (2 * Math.PI * Math.Sqrt(Inductance * Capacitance));

    /// <summary>
    ///     A branch resonating at <paramref name="hz" /> on <paramref name="capacitance" />, with the series resistance that
    ///     gives the resonator alone a Q of <paramref name="q" /> (the characteristic impedance divided by Q).
    /// </summary>
    public static LcrBranch At(double hz, double capacitance, double q = 1) {
        var inductance = 1 / (Square(2 * Math.PI * hz) * capacitance);
        return new(inductance, capacitance, Math.Sqrt(inductance / capacitance) / q);
    }

    static double Square(double x) => x * x;
}

/// <summary>
///     The classic inductor graphic equaliser, as in the Mesa/Boogie Mark series (docs/06-amp-modelling-spec.md §3.4,
///     §3.6): an op-amp whose non-inverting input is fed from the source through <c>R<sub>in</sub></c> and whose
///     feedback resistor <c>R<sub>fb</sub></c> (= R<sub>in</sub>) returns to its inverting input; each slider is a pot
///     whose ends sit on the two inputs and whose wiper goes to ground through a series L-C-R resonator. Centred, a
///     slider loads both inputs alike and the stage is flat; towards the input end it shunts the input near its
///     resonance (a cut), towards the feedback end it shunts the feedback (a boost). Every resonator loads the same two
///     nodes, so the bands interact as the real circuit's do: neighbours' skirts add, and moving one slider shifts what
///     the others do.
/// </summary>
/// <remarks>
///     The circuit is simulated, not approximated: an ideal op-amp holds both inputs at one voltage, every reactance is a
///     trapezoidal companion model (the bilinear transform of the circuit at every setting), and each branch's two
///     nodes are eliminated in closed form, so a sample costs a few operations per band. Slider positions are set per
///     sub-block; moving one recomputes five 2 × 2 eliminations. Per-channel state; allocation-free after construction.
/// </remarks>
public sealed class LcrGraphicEq {
    /// <summary>The slider pots' end-stop resistance, so a slider at an end still has a little track.</summary>
    const double EndOhms = 100;

    readonly LcrBranch[] branches;
    readonly double inputOhms;
    readonly double potOhms;
    readonly double[] conductanceA; // 1 / (input end to wiper)
    readonly double[] conductanceB; // 1 / (wiper to feedback end)
    readonly double[] inductorG;
    readonly double[] inductorK; // 2L/T − R
    readonly double[] capacitorG;
    readonly double[] determinant;
    readonly double[] alpha;
    readonly double[,] inductorJ;
    readonly double[,] capacitorJ;
    double inputConductance;

    public int Count => branches.Length;

    /// <summary>A network of <paramref name="branches" />, with sliders of <paramref name="potOhms" /> (linear).</summary>
    public LcrGraphicEq(IReadOnlyList<LcrBranch> branches, double inputOhms, double potOhms) {
        this.branches = [..branches];
        this.inputOhms = inputOhms;
        this.potOhms = potOhms;
        var n = branches.Count;
        conductanceA = new double[n];
        conductanceB = new double[n];
        inductorG = new double[n];
        inductorK = new double[n];
        capacitorG = new double[n];
        determinant = new double[n];
        alpha = new double[n];
        inductorJ = new double[2, n];
        capacitorJ = new double[2, n];
        Prepare(48_000);
    }

    /// <summary>Control thread: the companion models for <paramref name="sampleRate" />; sliders centred.</summary>
    public void Prepare(double sampleRate) {
        var t = 1 / sampleRate;
        for (var k = 0; k < branches.Length; k++) {
            var b = branches[k];
            inductorG[k] = 1 / (b.Resistance + 2 * b.Inductance / t);
            inductorK[k] = 2 * b.Inductance / t - b.Resistance;
            capacitorG[k] = 2 * b.Capacitance / t;
        }

        inputConductance = 1 / inputOhms;
        Span<double> centred = stackalloc double[branches.Length];
        centred.Fill(0.5);
        Set(centred);
        Reset();
    }

    /// <summary>The slider positions, 0 (the input end: full cut) … 0.5 (flat) … 1 (full boost). Allocation-free.</summary>
    public void Set(ReadOnlySpan<double> positions) {
        for (var k = 0; k < branches.Length; k++) {
            var x = Math.Clamp(k < positions.Length ? positions[k] : 0.5, 0, 1);
            conductanceA[k] = 1 / (x * potOhms + EndOhms);
            conductanceB[k] = 1 / ((1 - x) * potOhms + EndOhms);
            var g = conductanceA[k] + conductanceB[k];
            var gl = inductorG[k];
            var gc = capacitorG[k];
            determinant[k] = (g + gl) * (gl + gc) - gl * gl;
            alpha[k] = g * (gl + gc) / determinant[k];
        }
    }

    public void Reset() {
        Array.Clear(inductorJ);
        Array.Clear(capacitorJ);
    }

    /// <summary>Clears companion state that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        for (var c = 0; c < 2; c++) {
            for (var k = 0; k < branches.Length; k++) {
                if (Math.Abs(inductorJ[c, k]) < 1e-30) {
                    inductorJ[c, k] = 0;
                }

                if (Math.Abs(capacitorJ[c, k]) < 1e-30) {
                    capacitorJ[c, k] = 0;
                }
            }
        }
    }

    /// <summary>Runs channel <paramref name="channel" /> (0 or 1) in place.</summary>
    public void Process(int channel, Span<float> samples) {
        var n = branches.Length;
        Span<double> beta = stackalloc double[n];
        for (var i = 0; i < samples.Length; i++) {
            // The input node: (V − Vin) G_in + Σ (V − W_k) G_a = 0, with each wiper W_k = α_k V + β_k.
            var sum = inputConductance;
            var sources = samples[i] * inputConductance;
            for (var k = 0; k < n; k++) {
                var gl = inductorG[k];
                var jl = inductorJ[channel, k];
                var jc = capacitorJ[channel, k];
                beta[k] = (-jl * capacitorG[k] - gl * jc) / determinant[k];
                sum += (1 - alpha[k]) * conductanceA[k];
                sources += beta[k] * conductanceA[k];
            }

            var v = sources / sum;
            var output = v;
            for (var k = 0; k < n; k++) {
                var gl = inductorG[k];
                var gc = capacitorG[k];
                var ga = conductanceA[k];
                var gb = conductanceB[k];
                var g = ga + gb;
                var jl = inductorJ[channel, k];
                var jc = capacitorJ[channel, k];
                var wiper = alpha[k] * v + beta[k];
                var middle = (g * gl * v + g * jl - (g + gl) * jc) / determinant[k];

                // The feedback side: (Vo − V) / R_fb = Σ (V − W_k) G_b, with R_fb = R_in.
                output += inputOhms * (v - wiper) * gb;

                // Advance the companions: the branch current flows through L and C alike.
                var vl = wiper - middle;
                var current = gl * vl + jl;
                inductorJ[channel, k] = gl * (vl + inductorK[k] * current);
                capacitorJ[channel, k] = -(gc * middle + current);
            }

            samples[i] = (float)output;
        }
    }
}

/// <summary>Which component-level network a passive Graphic EQ layout runs (<see cref="PassiveGraphicEq" />).</summary>
public enum PassiveNetwork {
    None,
    Mark,
    FiveBand,
    FourBand,
    ThreeBand,
    Console
}

/// <summary>
///     The component-level networks behind the Graphic EQ's passive types (docs/08-blocks-parity.md §6.12), stereo:
///     the Mark and 5-band inductor graphics and the console on an <see cref="LcrGraphicEq" />, the 3-band on a Marshall
///     bass-middle-treble stack (<see cref="ToneStackCircuit" />) and the 4-band (after the Marshall JMP-1) on that stack
///     followed by a presence network. The band gains (±12 dB) are slider or pot positions here: 0 dB is a centred
///     slider (flat on the inductor graphics) or a pot at noon (the stack's own voicing, made up to unity at 1 kHz), so
///     the bands interact and overshoot as the circuits do and are not exact dB.
/// </summary>
public sealed class PassiveGraphicEq {
    /// <summary>The inductor graphics' input and feedback resistors, ohms (assumed: Mesa's are not printed).</summary>
    public const double InputOhms = 4_700;

    /// <summary>The sliders, ohms (the Mark IIC+'s 50k, docs/06 §3.4).</summary>
    public const double SliderOhms = 50_000;

    /// <summary>The JMP-1-style presence network: a 47k series arm bypassed by the pot and 4.7 nF, into 10k.</summary>
    const double PresenceSeries = 47_000;

    const double PresenceShunt = 10_000;
    const double PresencePot = 1_000_000;
    const double PresenceCap = 4.7e-9;

    /// <summary>
    ///     The Mark IIC+ sliders' resonators, paired as the TriAxis Dynamic Voice and the Bass 400 drawings pair them
    ///     (docs/06 §3.4, §3.6): 1 H and 3.3 µF, 0.39 H and 0.47 µF, 0.22 H and 0.22 µF, 0.068 H and 0.15 µF, 0.033 H
    ///     and 0.033 µF (88, 372, 724, 1 573 and 4 824 Hz behind the panel's 80 to 6 600 Hz). Each branch's series
    ///     resistance gives it a Q of 1 (assumed: the inductors' winding resistance is not printed).
    /// </summary>
    public static IReadOnlyList<LcrBranch> Mark { get; } = [
        Branch(1.0, 3.3e-6), Branch(0.39, 0.47e-6), Branch(0.22, 0.22e-6), Branch(0.068, 0.15e-6),
        Branch(0.033, 0.033e-6)
    ];

    /// <summary>A five-band inductor graphic at 100, 400, 1 000, 3 000 and 8 000 Hz on stock capacitor values.</summary>
    public static IReadOnlyList<LcrBranch> FiveBand { get; } = [
        LcrBranch.At(100, 2.2e-6), LcrBranch.At(400, 0.47e-6), LcrBranch.At(1_000, 0.22e-6),
        LcrBranch.At(3_000, 0.1e-6), LcrBranch.At(8_000, 0.033e-6)
    ];

    /// <summary>A console's three broad resonators (Q 0.7) at 100 Hz, 1 kHz and 10 kHz.</summary>
    public static IReadOnlyList<LcrBranch> Console { get; } = [
        LcrBranch.At(100, 2.2e-6, 0.7), LcrBranch.At(1_000, 0.22e-6, 0.7), LcrBranch.At(10_000, 0.022e-6, 0.7)
    ];

    readonly LcrGraphicEq mark = new(Mark, InputOhms, SliderOhms);
    readonly LcrGraphicEq fiveBand = new(FiveBand, InputOhms, SliderOhms);
    readonly LcrGraphicEq console = new(Console, InputOhms, SliderOhms);
    readonly ToneStackCircuit[] stacks = [new(ToneStackDesign.Marshall, 48_000), new(ToneStackDesign.Marshall, 48_000)];
    readonly double[] positions = new double[GraphicEqualizer.MaxBands];
    readonly double[] presenceState = new double[2];
    double stackMakeup = 1;
    double presenceB0, presenceB1, presenceA1, presenceMakeup = 1;
    double sampleRate = 48_000;

    /// <summary>Control thread.</summary>
    public void Prepare(double sampleRate) {
        this.sampleRate = sampleRate;
        mark.Prepare(sampleRate);
        fiveBand.Prepare(sampleRate);
        console.Prepare(sampleRate);
        foreach (var stack in stacks) {
            stack.SetSampleRate(sampleRate);
            stack.Set(0.5, 0.5, 0.5);
        }

        stackMakeup = 1 / NoonGain(ToneStackDesign.Marshall, sampleRate);
        DesignPresence(0.5);
        Reset();
    }

    public void Reset() {
        mark.Reset();
        fiveBand.Reset();
        console.Reset();
        foreach (var stack in stacks) {
            stack.Reset();
        }

        Array.Clear(presenceState);
    }

    /// <summary>A band gain (±12 dB) as a slider or pot position, 0 … 1 (0.5 at 0 dB).</summary>
    public static double Position(float gainDb) => Math.Clamp(0.5 + gainDb / 24.0, 0, 1);

    /// <summary>Audio thread: equalises <paramref name="io" /> through <paramref name="kind" />'s network.</summary>
    public void Process(PassiveNetwork kind, in StereoBuffer io, ReadOnlySpan<float> gains) {
        var count = kind switch {
            PassiveNetwork.Mark or PassiveNetwork.FiveBand => 5,
            PassiveNetwork.FourBand => 4,
            PassiveNetwork.ThreeBand or PassiveNetwork.Console => 3,
            _ => 0
        };
        for (var b = 0; b < count; b++) {
            positions[b] = Position(gains[b]);
        }

        var slider = kind switch {
            PassiveNetwork.Mark => mark,
            PassiveNetwork.FiveBand => fiveBand,
            PassiveNetwork.Console => console,
            _ => null
        };
        if (slider is not null) {
            slider.Set(positions.AsSpan(0, count));
            slider.Process(0, io.Left);
            slider.Process(1, io.Right);
            slider.FlushDenormals();
            return;
        }

        if (kind is PassiveNetwork.ThreeBand or PassiveNetwork.FourBand) {
            Stack(io.Left, stacks[0]);
            Stack(io.Right, stacks[1]);
            if (kind == PassiveNetwork.FourBand) {
                DesignPresence(positions[3]);
                Presence(io.Left, 0);
                Presence(io.Right, 1);
            }
        }
    }

    void Stack(Span<float> samples, ToneStackCircuit stack) {
        stack.Set(positions[0], positions[1], positions[2]);
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)(stack.Tick(samples[i]) * stackMakeup);
        }

        stack.FlushDenormals();
    }

    /// <summary>
    ///     The presence network: <c>H(s) = R<sub>sh</sub> / (R<sub>sh</sub> + R<sub>s</sub> ∥ (R<sub>pot</sub> + 1/sC))</c>,
    ///     a first-order treble lift that grows as the pot (log taper) turns towards zero, made up to unity in the bass,
    ///     by the bilinear transform. Allocation-free.
    /// </summary>
    void DesignPresence(double position) {
        // The pot's resistance in the arm: a log taper from 1M (no lift) at 0 to nothing at full.
        var pot = PresencePot * (Math.Pow(1000, 1 - position) - 1) / 999;
        // Z1 = Rs ∥ (Rp + 1/sC) = Rs (1 + s C Rp) / (1 + s C (Rs + Rp)); H = Rsh / (Rsh + Z1).
        var rs = PresenceSeries;
        var rsh = PresenceShunt;
        var c = PresenceCap;
        // H(s) = Rsh (1 + s C (Rs + Rp)) / (Rsh (1 + s C (Rs + Rp)) + Rs (1 + s C Rp))
        var n0 = rsh;
        var n1 = rsh * c * (rs + pot);
        var d0 = rsh + rs;
        var d1 = rsh * c * (rs + pot) + rs * c * pot;
        var k = 2 * sampleRate;
        var a0 = d0 + d1 * k;
        presenceB0 = (n0 + n1 * k) / a0;
        presenceB1 = (n0 - n1 * k) / a0;
        presenceA1 = (d0 - d1 * k) / a0;
        presenceMakeup = d0 / n0; // the bass passes at Rsh / (Rsh + Rs): give that back
    }

    void Presence(Span<float> samples, int channel) {
        var state = presenceState[channel];
        for (var i = 0; i < samples.Length; i++) {
            // Transposed direct form II, first order.
            var x = samples[i] * presenceMakeup;
            var y = presenceB0 * x + state;
            state = presenceB1 * x - presenceA1 * y;
            samples[i] = (float)y;
        }

        presenceState[channel] = Math.Abs(state) < 1e-30 ? 0 : state;
    }

    /// <summary>A tone stack's gain at 1 kHz with its pots at noon, from its impulse response.</summary>
    internal static double NoonGain(ToneStackDesign design, double sampleRate) {
        var stack = new ToneStackCircuit(design, sampleRate);
        var (re, im) = (0.0, 0.0);
        for (var n = 0; n < 16_384; n++) {
            var h = stack.Tick(n == 0 ? 1 : 0);
            var phase = 2 * Math.PI * 1_000 * n / sampleRate;
            re += h * Math.Cos(phase);
            im -= h * Math.Sin(phase);
        }

        return Math.Sqrt(re * re + im * im);
    }

    static LcrBranch Branch(double inductance, double capacitance) =>
        new(inductance, capacitance, Math.Sqrt(inductance / capacitance));
}
