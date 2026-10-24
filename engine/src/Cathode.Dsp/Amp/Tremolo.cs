using System.Numerics;
using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A tremolo's oscillator: a triode with a three-section CR phase-shift ladder from its plate back to its grid, the
///     circuit of nearly every tube-amp tremolo (Fender's from the 6G amps on, Gibson's, Marshall's). Its rate and its
///     swing are solved from the parts: see the remarks.
/// </summary>
/// <remarks>
///     <para>
///         <b>Rate.</b> From the plate (a source behind <paramref name="PlateResistor" /> ‖ r_p, with
///         <paramref name="PlateShunt" /> and <paramref name="PlateLoad" /> to AC ground), <paramref name="Ladder1" />
///         to a node with <paramref name="Leg1" /> to AC ground, <paramref name="Ladder2" /> to a node with
///         <paramref name="Leg2" />, <paramref name="Ladder3" /> to the grid with <paramref name="Leg3" />; the Speed
///         rheostat adds to leg <paramref name="SpeedLeg" /> (turned up, it takes resistance out). The oscillator runs where
///         the ladder turns the phase by 180°, found here by bisection, so each drawing's values give its rate range.
///     </para>
///     <para>
///         <b>Swing.</b> The oscillation grows until the triode's gain, compressed by its own curves, just makes up the
///         ladder's loss: a describing-function balance. The grid swings as a sine (the ladder's three sections filter the
///         plate's harmonics) around the cathode's bias, its positive peaks clamped at 0 V grid-to-cathode by grid current
///         once the swing exceeds the bias (the coupling cap charges, as a grid-leak bias does); the plate runs on its AC
///         load line (<paramref name="PlateResistor" /> in parallel with what the ladder and the loads draw at the rate),
///         solved point by point on the tube's Koren curves, and the fundamental of that clipped plate wave, times the
///         ladder's loss, must equal the grid's swing. The plate's first three harmonics are kept: they shape the
///         tremolo's waveform downstream. The cathode is taken as fully bypassed at the rate.
///     </para>
///     <para>
///         Both are solved on a table of Speed rotations when a design is first used (a few tens of milliseconds, on the
///         control thread, shared by every instance); at run time the knobs only interpolate it.
///     </para>
/// </remarks>
/// <param name="Tube">The oscillator's triode.</param>
/// <param name="Supply">Its plate resistor's supply node, volts.</param>
/// <param name="PlateResistor">The plate resistor.</param>
/// <param name="CathodeResistor">The (bypassed) cathode resistor: the bias.</param>
/// <param name="Ladder1">The first ladder cap, from the plate.</param>
/// <param name="Ladder2">The second ladder cap.</param>
/// <param name="Ladder3">The third ladder cap, into the grid.</param>
/// <param name="Leg1">The fixed part of the first node's resistor to AC ground.</param>
/// <param name="Leg2">The second node's.</param>
/// <param name="Leg3">The grid's (the grid leak).</param>
/// <param name="Speed">The Speed pot, wired as a rheostat in leg <paramref name="SpeedLeg" />.</param>
/// <param name="SpeedLeg">Which leg (1 to 3) the Speed rheostat is in.</param>
/// <param name="PlateShunt">A cap from the plate to AC ground.</param>
/// <param name="PlateLoad">A resistive load on the plate (the tremolo's output network at the rate); infinite for none.</param>
public sealed record PhaseShiftOscillatorDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double CathodeResistor,
    double Ladder1,
    double Ladder2,
    double Ladder3,
    double Leg1,
    double Leg2,
    double Leg3,
    Pot Speed,
    int SpeedLeg = 1,
    double PlateShunt = 0,
    double PlateLoad = double.PositiveInfinity
) {
    /// <summary>Speed rotations in the table.</summary>
    internal const int Points = 13;

    /// <summary>Points per cycle where the plate wave is solved.</summary>
    internal const int Samples = 32;

    /// <summary>Harmonics of the plate wave kept.</summary>
    internal const int Harmonics = 3;

    static readonly ConditionalWeakTable<PhaseShiftOscillatorDesign, OscillatorTable> Tables = new();

    /// <summary>The solved table (built on first use, then shared).</summary>
    internal OscillatorTable Table => Tables.GetValue(this, static d => new(d));

    /// <summary>The oscillation frequency, in hertz, at Speed rotation <paramref name="speed" /> (0..1).</summary>
    public double Rate(double speed) {
        var source = PlateSourceResistance();
        var low = 0.05;
        var high = 0.05;
        var previous = Ladder(low, speed, source).Source.Imaginary;
        while (high < 400) {
            high *= 1.25;
            var value = Ladder(high, speed, source).Source.Imaginary;
            if (Math.Sign(value) != Math.Sign(previous)) {
                break;
            }

            low = high;
            previous = value;
        }

        for (var i = 0; i < 60; i++) {
            var middle = Math.Sqrt(low * high);
            if (Math.Sign(Ladder(middle, speed, source).Source.Imaginary) == Math.Sign(previous)) {
                low = middle;
            } else {
                high = middle;
            }
        }

        return Math.Sqrt(low * high);
    }

    /// <summary>The slowest and fastest rates the Speed knob reaches, in hertz.</summary>
    public (double Slowest, double Fastest) RateRange() => (Table.Rates[Table.First], Table.Rates[Table.Last]);

    /// <summary>The Speed rotations between which it oscillates (a ladder with too much loss stalls at an end).</summary>
    public (double From, double To) RunningRange() =>
        (Table.First / (double)(Points - 1), Table.Last / (double)(Points - 1));

    /// <summary>The grid's peak swing at Speed rotation <paramref name="speed" />, volts (0 if it cannot oscillate).</summary>
    public double GridSwing(double speed) => OscillatorTable.At(Table.Amplitudes, speed * (Points - 1));

    /// <summary>The plate's swing at the fundamental at Speed rotation <paramref name="speed" />, peak volts.</summary>
    public double PlateSwing(double speed) => Table.PlateAt(1, speed * (Points - 1)).Magnitude;

    /// <summary>
    ///     The plate's harmonic <paramref name="k" /> (1 to 3) relative to its fundamental at Speed rotation
    ///     <paramref name="speed" />.
    /// </summary>
    public double Harmonic(int k, double speed) =>
        Table.PlateAt(k, speed * (Points - 1)).Magnitude / Table.PlateAt(1, speed * (Points - 1)).Magnitude;

    /// <summary>The oscillator's cathode voltage at its operating point.</summary>
    public double Bias => Table.Bias;

    /// <summary>The DC operating point: plate current, plate and cathode voltages.</summary>
    internal (double Current, double Plate, double Cathode) OperatingPoint() =>
        Amp.OperatingPoint.CommonCathode(Tube, 0, Supply, PlateResistor, CathodeResistor);

    /// <summary>The plate as a source: its resistor in parallel with r_p at the operating point.</summary>
    internal double PlateSourceResistance() {
        var (_, plate, cathode) = OperatingPoint();
        var (rp, _) = Amp.OperatingPoint.SmallSignal(Tube, -cathode, plate - cathode);
        return PlateResistor * rp / (PlateResistor + rp);
    }

    /// <summary>
    ///     The ladder for a unit grid voltage at <paramref name="frequency" />: the plate node's voltage, the source
    ///     behind the plate's resistance that drives it, the second node's voltage (where a lamp driver taps it), and what
    ///     the ladder and the plate's loads draw per volt at the plate.
    /// </summary>
    internal (Complex Plate, Complex Source, Complex Second, Complex Admittance) Ladder(
        double frequency,
        double speed,
        double sourceResistance
    ) {
        var omega = 2 * Math.PI * frequency;
        Complex Reactance(double farads) => new(0, -1 / (omega * farads));
        var rheostat = (1 - Speed.Fraction(speed)) * Speed.Ohms;
        var leg1 = Leg1 + (SpeedLeg == 1 ? rheostat : 0);
        var leg2 = Leg2 + (SpeedLeg == 2 ? rheostat : 0);
        var leg3 = Leg3 + (SpeedLeg == 3 ? rheostat : 0);
        Complex grid = 1;
        var current = grid / leg3;
        var second = grid + current * Reactance(Ladder3);
        current += second / leg2;
        var first = second + current * Reactance(Ladder2);
        current += first / leg1;
        var plate = first + current * Reactance(Ladder1);
        current += plate * new Complex(1 / PlateLoad, omega * PlateShunt);
        return (plate, plate + current * sourceResistance, second, current / plate);
    }
}

/// <summary>
///     A <see cref="PhaseShiftOscillatorDesign" /> solved on <see cref="PhaseShiftOscillatorDesign.Points" /> Speed
///     rotations: the rate, the grid's swing and bias, the plate's harmonics and the ladder's second node, per rotation.
/// </summary>
sealed class OscillatorTable {
    public readonly double[] Rates = new double[PhaseShiftOscillatorDesign.Points];

    /// <summary>The grid's peak swing, volts.</summary>
    public readonly double[] Amplitudes = new double[PhaseShiftOscillatorDesign.Points];

    /// <summary>
    ///     The plate's harmonics (complex amplitudes, the grid swinging as <c>A sin θ</c>), per rotation and harmonic
    ///     (<c>[rotation * Harmonics + k - 1]</c>).
    /// </summary>
    public readonly Complex[] Plate = new Complex[PhaseShiftOscillatorDesign.Points * PhaseShiftOscillatorDesign.Harmonics];

    /// <summary>The second ladder node per grid volt at the rate.</summary>
    public readonly Complex[] Second = new Complex[PhaseShiftOscillatorDesign.Points];

    /// <summary>The cathode's bias voltage at the operating point.</summary>
    public readonly double Bias;

    /// <summary>The first rotation where the oscillator runs (a ladder with too much loss at an end of the Speed knob stalls).</summary>
    public int First { get; }

    /// <summary>The last rotation where it runs.</summary>
    public int Last { get; }

    public OscillatorTable(PhaseShiftOscillatorDesign design) {
        var (current, plateVolts, cathode) = design.OperatingPoint();
        Bias = cathode;
        var source = design.PlateSourceResistance();
        var triode = new Triode(design.Tube);
        Span<double> wave = stackalloc double[PhaseShiftOscillatorDesign.Samples];
        for (var p = 0; p < PhaseShiftOscillatorDesign.Points; p++) {
            var speed = p / (double)(PhaseShiftOscillatorDesign.Points - 1);
            var rate = design.Rate(speed);
            Rates[p] = rate;
            var ladder = design.Ladder(rate, speed, source);
            Second[p] = ladder.Second;
            var loss = 1 / ladder.Plate.Magnitude; // grid volts per plate volt at the rate
            var load = 1 / (1 / design.PlateResistor + ladder.Admittance.Magnitude);

            // The swing where the fundamental's loop gain is 1: it falls as the swing grows.
            double Gain(double amplitude, Span<double> plate) =>
                Fundamental(triode, amplitude, cathode, plateVolts, current, load, plate).Magnitude * loss
                / amplitude;

            if (Gain(1e-3, wave) <= 1) {
                continue; // too little gain to oscillate
            }

            var low = 1e-3;
            var high = plateVolts;
            for (var i = 0; i < 40; i++) {
                var middle = Math.Sqrt(low * high);
                if (Gain(middle, wave) > 1) {
                    low = middle;
                } else {
                    high = middle;
                }
            }

            var amplitude = Math.Sqrt(low * high);
            Amplitudes[p] = amplitude;
            Fundamental(triode, amplitude, cathode, plateVolts, current, load, wave);
            for (var k = 1; k <= PhaseShiftOscillatorDesign.Harmonics; k++) {
                Plate[p * PhaseShiftOscillatorDesign.Harmonics + k - 1] = Harmonic(wave, k);
            }
        }

        First = Array.FindIndex(Amplitudes, static a => a > 0);
        Last = Array.FindLastIndex(Amplitudes, static a => a > 0);
        if (First < 0 || First == Last) {
            throw new ArgumentException("The oscillator does not oscillate over the Speed knob's travel.", nameof(design));
        }
    }

    /// <summary>
    ///     The table position (fractional rotation index) of the rate nearest <paramref name="hertz" />, within the
    ///     rotations where the oscillator runs (<see cref="First" /> to <see cref="Last" />).
    /// </summary>
    public double Position(double hertz) {
        if (!(hertz > Rates[First])) {
            return First;
        }

        for (var p = First + 1; p <= Last; p++) {
            if (hertz <= Rates[p]) {
                return p - 1 + (hertz - Rates[p - 1]) / (Rates[p] - Rates[p - 1]);
            }
        }

        return Last;
    }

    /// <summary>A table column interpolated at <paramref name="position" />.</summary>
    public static double At(double[] column, double position) {
        var i = Math.Min((int)position, column.Length - 2);
        var t = position - i;
        return column[i] + t * (column[i + 1] - column[i]);
    }

    /// <summary>Harmonic <paramref name="k" /> of the plate at <paramref name="position" />.</summary>
    public Complex PlateAt(int k, double position) {
        var i = Math.Min((int)position, PhaseShiftOscillatorDesign.Points - 2);
        var t = position - i;
        var a = Plate[i * PhaseShiftOscillatorDesign.Harmonics + k - 1];
        var b = Plate[(i + 1) * PhaseShiftOscillatorDesign.Harmonics + k - 1];
        return a + t * (b - a);
    }

    /// <summary>
    ///     The plate wave (into <paramref name="plate" />, one cycle) for a grid swinging by <paramref name="amplitude" />,
    ///     and its fundamental.
    /// </summary>
    static Complex Fundamental(
        Triode triode,
        double amplitude,
        double cathode,
        double plateVolts,
        double current,
        double load,
        Span<double> plate
    ) {
        var bias = amplitude <= cathode ? -cathode : -amplitude; // grid current clamps the positive peaks at 0 V
        var most = current + (plateVolts - cathode) / load;
        for (var n = 0; n < plate.Length; n++) {
            var grid = bias + amplitude * Math.Sin(2 * Math.PI * n / plate.Length);
            double low = 0, high = most;
            for (var i = 0; i < 40; i++) {
                var middle = 0.5 * (low + high);
                var vpk = plateVolts - load * (middle - current) - cathode;
                if (Amp.OperatingPoint.PlateCurrent(triode, grid, vpk) > middle) {
                    low = middle;
                } else {
                    high = middle;
                }
            }

            plate[n] = plateVolts - load * (0.5 * (low + high) - current);
        }

        return Harmonic(plate, 1);
    }

    /// <summary>The complex amplitude of harmonic <paramref name="k" /> of one cycle.</summary>
    static Complex Harmonic(ReadOnlySpan<double> wave, int k) {
        var sum = Complex.Zero;
        for (var n = 0; n < wave.Length; n++) {
            var theta = 2 * Math.PI * k * n / wave.Length;
            sum += wave[n] * new Complex(Math.Cos(theta), -Math.Sin(theta));
        }

        return 2 * sum / wave.Length;
    }
}

/// <summary>
///     A bias tremolo (Fender's brownface Deluxe and Vibrolux, the blackface Princetons, Gibson's Scout): the oscillator's
///     plate (or a cathode follower on it) drives, through <paramref name="SeriesResistor" /> (with
///     <paramref name="SeriesShunt" /> to ground after it) and <paramref name="CouplingCap" />, the top of the
///     <paramref name="Intensity" /> pot, whose bottom is the bias supply (an AC ground); the wiper, with
///     <paramref name="WiperCap" /> to ground, is the grid leaks' common return. Without an Intensity pot (the Scout)
///     the cap's far end is the return itself, with <paramref name="ReturnResistor" /> to AC ground.
/// </summary>
/// <remarks>
///     The network is solved at the rate and its first harmonics (<see cref="Return" />), loaded by the grid leaks, each in
///     series with its coupling cap and the inverter's plate (<see cref="InverterResistance" />), so the bias line swings
///     with the amplitude and phase the parts give. From there the power amp carries it to the grids through the same
///     coupling caps and leaks it runs the signal through (<see cref="PowerAmp" />): the swing at the grids lags and falls
///     with the rate, as the leak and coupling cap's time constant makes it.
/// </remarks>
/// <param name="Oscillator">The oscillator.</param>
/// <param name="SeriesResistor">From the plate (or follower) towards the pot.</param>
/// <param name="CouplingCap">In series, to the pot's top.</param>
/// <param name="Intensity">The Intensity pot (wiper to the grid leaks), or null for a fixed depth.</param>
/// <param name="SeriesShunt">A cap to ground after <paramref name="SeriesResistor" />.</param>
/// <param name="WiperCap">From the wiper (the grid leaks' return) to ground.</param>
/// <param name="ReturnResistor">Without an Intensity pot: from the grid leaks' return to AC ground.</param>
/// <param name="FollowerGain">
///     The gain of a cathode follower between the plate and <paramref name="SeriesResistor" /> (the 6G11's), or 0 when
///     the plate drives it directly.
/// </param>
public sealed record BiasTremoloDesign(
    PhaseShiftOscillatorDesign Oscillator,
    double SeriesResistor,
    double CouplingCap,
    Pot? Intensity,
    double SeriesShunt = 0,
    double WiperCap = 0,
    double ReturnResistor = 0,
    double FollowerGain = 0
) {
    /// <summary>The inverter's plates as sources for the grid leaks' branches (about 100k ‖ r_p).</summary>
    public const double InverterResistance = 40e3;

    /// <summary>
    ///     The swing of the grid leaks' return per volt at the oscillator's plate, at <paramref name="frequency" /> and
    ///     Intensity rotation <paramref name="intensity" />, for the power amp <paramref name="power" />.
    /// </summary>
    public Complex Return(double frequency, double intensity, PowerAmpDesign power) {
        var omega = 2 * Math.PI * frequency;
        Complex Cap(double farads) => farads > 0 ? new(0, omega * farads) : Complex.Zero;
        var branch = power.GridLeak + InverterResistance + new Complex(0, -1 / (omega * power.CouplingCap));
        var load = power.Sides / branch;
        Complex top; // from the coupling cap's far end to ground, and the share of its voltage at the return
        Complex share;
        if (Intensity is { } pot) {
            var fraction = pot.Fraction(intensity);
            var upper = Math.Max((1 - fraction) * pot.Ohms, 1);
            var lower = Math.Max(fraction * pot.Ohms, 1);
            var wiper = 1 / lower + Cap(WiperCap) + load;
            top = upper + 1 / wiper;
            share = 1 / wiper / top;
        } else {
            var node = 1 / ReturnResistor + Cap(WiperCap) + load;
            top = 1 / node;
            share = 1;
        }

        var coupled = new Complex(0, -1 / (omega * CouplingCap)) + top;
        var node1 = 1 / SeriesResistor + Cap(SeriesShunt) + 1 / coupled;
        var first = 1 / SeriesResistor / node1;
        var gain = FollowerGain > 0 ? FollowerGain : 1;
        return gain * first * (top / coupled) * share;
    }
}

/// <summary>
///     A <see cref="BiasTremoloDesign" /> running in a <see cref="PowerAmp" />: per sample, the grid leaks' return as the
///     sum of the oscillator's first harmonics through the network. The knobs set the target harmonics at control rate
///     (an interpolation in the oscillator's table and a few complex products); each sample moves the harmonics towards
///     them over <see cref="SmoothingTime" /> (the oscillation builds up and dies away over a few cycles, so engaging it
///     never clicks) and turns a phasor. With the depth at 0 and the swing died away it stops, and the power amp runs as
///     without it.
/// </summary>
sealed class BiasTremolo(BiasTremoloDesign design, PowerAmpDesign power, double sampleRate) {
    const double SmoothingTime = 0.03;
    const double Floor = 1e-7; // volts: below this the tremolo has died away

    readonly OscillatorTable table = design.Oscillator.Table;
    readonly Complex[] target = new Complex[PhaseShiftOscillatorDesign.Harmonics];
    readonly Complex[] current = new Complex[PhaseShiftOscillatorDesign.Harmonics];
    double sampleRate = sampleRate;
    double smoothing;
    double frequency;
    double stepSine;
    double stepCosine = 1;
    double sine;
    double cosine = 1;
    double rate = double.NaN;
    double depth = double.NaN;
    bool running;

    public BiasTremoloDesign Design { get; } = design;

    /// <summary>The oscillator's rate, hertz.</summary>
    public double Frequency => frequency;

    /// <summary>Whether the bias is swinging.</summary>
    public bool Running => running;

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        Step();
    }

    /// <summary>Rate in hertz (held to the Speed knob's range), depth 0..1 (the Intensity knob's rotation).</summary>
    public void Configure(double rate, double depth) {
        if (rate == this.rate && depth == this.depth) {
            return;
        }

        (this.rate, this.depth) = (rate, depth);
        var position = table.Position(rate);
        frequency = OscillatorTable.At(table.Rates, position);
        var amplitude = OscillatorTable.At(table.Amplitudes, position);
        for (var k = 1; k <= target.Length; k++) {
            target[k - 1] = depth > 0 && amplitude > 0
                ? table.PlateAt(k, position)
                * Design.Return(k * frequency, Design.Intensity is null ? 1 : depth, power)
                * (Design.Intensity is null ? depth : 1)
                : Complex.Zero;
        }

        if (depth > 0) {
            running = true;
        }

        Step();
    }

    /// <summary>At rest: the phase at 0, the harmonics at their targets (a reset amp starts with its tremolo running).</summary>
    public void Reset() {
        sine = 0;
        cosine = 1;
        target.CopyTo(current, 0);
        running = depth > 0;
    }

    /// <summary>The grid leaks' return for the coming sample, volts.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick() {
        if (!running) {
            return 0;
        }

        var s = sine * stepCosine + cosine * stepSine;
        var c = cosine * stepCosine - sine * stepSine;
        var norm = 1.5 - 0.5 * (s * s + c * c);
        sine = s * norm;
        cosine = c * norm;
        var z = new Complex(cosine, sine);
        var z2 = z * z;
        var z3 = z2 * z;
        current[0] += smoothing * (target[0] - current[0]);
        current[1] += smoothing * (target[1] - current[1]);
        current[2] += smoothing * (target[2] - current[2]);
        var swing = Real(current[0], z) + Real(current[1], z2) + Real(current[2], z3);
        if (depth == 0 && current[0].Magnitude < Floor) {
            // Died away: stop, at rest.
            Array.Clear(current);
            sine = 0;
            cosine = 1;
            running = false;
            return 0;
        }

        return swing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Real(Complex a, Complex b) => a.Real * b.Real - a.Imaginary * b.Imaginary;

    void Step() {
        (stepSine, stepCosine) = Math.SinCos(2 * Math.PI * frequency / sampleRate);
        smoothing = 1 - Math.Exp(-1 / (SmoothingTime * sampleRate));
    }
}
