using Cathode.Dsp.Wdf;
using System.Numerics;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The output transformer, referred to the whole primary (plate to plate): its nominal load
///     <paramref name="PlateToPlate" /> (the speaker's nominal 8 Ω times the impedance ratio), the primary
///     (magnetising) inductance, the leakage inductance, the copper resistance of both windings, and across the primary
///     its self-capacitance and core loss; and the core's saturation.
/// </summary>
/// <param name="PlateToPlate">The nominal primary load, plate to plate (a single-ended amp: plate to B+).</param>
/// <param name="PrimaryInductance">The magnetising inductance below saturation.</param>
/// <param name="LeakageInductance">Primary-referred.</param>
/// <param name="WindingResistance">Both windings' copper, primary-referred.</param>
/// <param name="PrimaryCapacitance">Across the primary.</param>
/// <param name="CoreLoss">Across the primary.</param>
/// <param name="SaturationFlux">
///     The flux linkage (volt-seconds, primary) at the core's knee; 0 for a linear core. Beyond it the magnetising
///     inductance falls (<see cref="PowerAmp" />), so low notes at full power saturate the core and distort.
/// </param>
/// <remarks>
///     The capacitance and core loss matter: pentodes are current sources, and when both cut off (blocking, deep class
///     B) the leakage inductance's current needs somewhere to go; without them the flyback would be unbounded. With
///     them it is bounded but not small: the defaults are typical values, not read from any transformer's data, and
///     nothing else clamps the plates, so a cranked amp's speaker voltage flies back up to 5 dB past the swing into a
///     resistive load for a few samples, and a single-ended plate reaches several times B+ (the Champ's about 2 kV
///     on 340 V). Whether a real winding's capacitance, its core loss at high frequency or an arc would hold that
///     lower is not known from anything read so far; <c>AmpFullScaleTests</c> pins what the model does.
/// </remarks>
public sealed record OutputTransformerDesign(
    double PlateToPlate,
    double PrimaryInductance = 20,
    double LeakageInductance = 10e-3,
    double WindingResistance = 150,
    double PrimaryCapacitance = 1e-9,
    double CoreLoss = 200e3,
    double SaturationFlux = 0
) {
    /// <summary>The turns ratio to a nominal 8 Ω speaker.</summary>
    public double TurnsRatio => Math.Sqrt(PlateToPlate / SpeakerDesign.Nominal);
}

/// <summary>
///     A guitar speaker's electrical impedance (Thiele-Small): voice-coil resistance, and the voice coil's inductance
///     made lossy by eddy currents (in parallel with <paramref name="EddyResistance" />, a first-order stand-in for its
///     semi-inductance: about 11 Ω at 1 kHz and 26 Ω at 5 kHz), in series with the motional parallel resonance at
///     <paramref name="Resonance" /> hertz, peaking at <paramref name="PeakImpedance" /> ohms with quality factor
///     <paramref name="Q" />. Nominally 8 Ω.
/// </summary>
/// <remarks>
///     The presets are parametric curves of the usual cabinet types normalised to 8 Ω (a 4 × 10 at 2 Ω on a 2 Ω tap
///     looks the same to the output tubes as an 8 Ω load on an 8 Ω tap), with typical values for their kind, not
///     measurements of particular cabinets.
/// </remarks>
public sealed record SpeakerDesign(
    double Resistance = 6.4,
    double Inductance = 1.2e-3,
    double EddyResistance = 25,
    double Resonance = 90,
    double PeakImpedance = 40,
    double Q = 3
) {
    public const double Nominal = 8;

    /// <summary>An open-back 1 × 12 combo: a high, sharp resonance (little air load).</summary>
    public static SpeakerDesign OpenBack1x12 { get; } = new(6.4, 1.0e-3, 25, 100);

    /// <summary>An open-back 2 × 12 combo: two cones share the air load, a slightly lower, broader peak.</summary>
    public static SpeakerDesign OpenBack2x12 { get; } = new(6.4, 1.1e-3, 25, 90, 32, 2.5);

    /// <summary>
    ///     An open-back 4 × 10 (Super Reverb, Bassman): ten-inch cones resonate higher, with less voice-coil
    ///     inductance.
    /// </summary>
    public static SpeakerDesign OpenBack4x10 { get; } = new(6.2, 0.7e-3, 30, 115, 30, 2.5);

    /// <summary>
    ///     A closed-back 4 × 12: the box's air spring damps and broadens the peak; a larger HF rise (Vintage 30-like).
    /// </summary>
    public static SpeakerDesign Closed4x12 { get; } = new(6.4, 1.4e-3, 22, 105, 26, 2);

    /// <summary>A closed 8 × 10 bass cabinet (SVT): low, broad resonance.</summary>
    public static SpeakerDesign Closed8x10 { get; } = new(6.2, 1.0e-3, 28, 60, 24, 1.8);

    /// <summary>The presets, in the order of the Circuit Amp's Speaker choice (after Stock).</summary>
    public static IReadOnlyList<SpeakerDesign> Presets { get; } =
        [OpenBack1x12, OpenBack2x12, OpenBack4x10, Closed4x12, Closed8x10];

    // Stock loads of the models with a speaker none of the presets covers. They are not in Presets, whose order is
    // saved in presets: a model that uses one has it on Speaker load: Stock.

    /// <summary>
    ///     An open-back 1 × 8 (Champ): a high, sharp resonance, little voice-coil inductance (typical values).
    /// </summary>
    public static SpeakerDesign OpenBack1x8 { get; } = new(6.2, 0.5e-3, 30, 130, 35, 2.5);

    /// <summary>
    ///     An open-back 1 × 10 (Princeton): a single ten-inch cone, resonating above a twelve with a sharper peak than
    ///     four of them share (typical values).
    /// </summary>
    public static SpeakerDesign OpenBack1x10 { get; } = new(6.2, 0.7e-3, 30, 110, 38, 2.8);

    /// <summary>
    ///     An open-back 1 × 15 (Vibroverb): a lower resonance than a 12, a little more voice-coil inductance (typical
    ///     values).
    /// </summary>
    public static SpeakerDesign OpenBack1x15 { get; } = new(6.4, 1.3e-3, 25, 75);

    /// <summary>The motional resistance, inductance and capacitance of the resonance.</summary>
    public (double Resistance, double Inductance, double Capacitance) Motional =>
        MotionalOf(Resonance, PeakImpedance - Resistance, Q);

    /// <summary>
    ///     The load's impedance in ohms at <paramref name="hz" />: the voice coil's resistance, its inductance across
    ///     the eddy-current resistance, and the motional parallel resonance, in series (control thread; for tests and
    ///     plots).
    /// </summary>
    public Complex Impedance(double hz) {
        var omega = 2 * Math.PI * hz;
        var coil = new Complex(0, omega * Inductance);
        coil = coil * EddyResistance / (coil + EddyResistance);
        var (rm, lm, cm) = Motional;
        var motional = 1 / (1 / rm + 1 / new Complex(0, omega * lm) + new Complex(0, omega * cm));
        return Resistance + coil + motional;
    }

    /// <summary>
    ///     The motional parallel RLC of a resonance at <paramref name="resonance" /> hertz peaking
    ///     <paramref name="excess" /> ohms above the voice coil's resistance, with quality factor
    ///     <paramref name="q" />.
    /// </summary>
    public static (double Resistance, double Inductance, double Capacitance) MotionalOf(
        double resonance,
        double excess,
        double q
    ) {
        var r = Math.Max(excess, 0.1);
        var omega = 2 * Math.PI * resonance;
        var l = r / (omega * q);
        return (r, l, 1 / (omega * omega * l));
    }
}

/// <summary>
///     A relay that takes the presence pot out of the feedback while <paramref name="Switch" /> is on (or, with
///     <paramref name="WhileOn" /> false, while it is off): the feedback's presence then stays at
///     <paramref name="Rotation" /> whatever the knob. 0, the pot's whole track in series with the cap, is the nearest
///     the network comes to no presence branch (the three-channel Rectifier's channel 2 in Raw); another rotation is a
///     fixed resistor the relay puts in the pot's place (the Rect-o-Verb's 5.6k on the lead channel).
/// </summary>
public readonly record struct PresenceOutDesign(SwitchRole Switch, bool WhileOn = true, double Rotation = 0);

/// <summary>
///     A front-panel switch that changes the feedback resistor: while <paramref name="Switch" /> is on the feedback
///     fraction is <paramref name="Factor" /> times the network's (the two-channel Dual Rectifier's "more feedback"
///     LDR, which puts a second 47k beside the first in the orange channel's Clean). The presence corner stays where
///     the network's own feedback resistor puts it.
/// </summary>
public readonly record struct FeedbackBoostDesign(SwitchRole Switch, double Factor);

/// <summary>
///     Global negative feedback from a speaker tap to the bottom of the phase inverter's tail: the feedback resistor
///     into a shunt resistor, with the presence control (a pot in series with a cap) across the shunt, which lowers the
///     feedback, and so lifts the response, at high frequencies; or, with <see cref="PresenceIsShunt" />, the presence
///     pot's track is the shunt itself and the cap goes from its wiper to ground (the 5F6-A and the amps that copied
///     it); or, with <see cref="Series" />, the presence is a rheostat in series with the feedback resistor (the Mesa
///     Marks). Depth lowers it below <paramref name="DepthFrequency" />, lifting the speaker resonance; a model with a
///     Deep switch of its own (<see cref="Deep" />) has that circuit instead.
/// </summary>
/// <param name="FeedbackResistor">From the tap to the shunt resistor.</param>
/// <param name="ShuntResistor">From the bottom of the phase inverter tail to ground.</param>
/// <param name="Presence">
///     The presence pot, in series with the presence cap (and <see cref="PresenceSeries" />) across the shunt.
/// </param>
/// <param name="PresenceCap">The presence cap.</param>
/// <param name="DepthFrequency">Below it, the depth control lowers the feedback.</param>
/// <param name="TapGain">The tap's voltage relative to an 8 Ω secondary (√2 for a 16 Ω tap).</param>
/// <param name="LowFeedback">
///     The network's own feedback below the depth frequency, as a fraction of the mid band's, with the depth control at
///     0: a cap across part of the feedback resistor (the Friedman BE-100's 220k with 4.7 nF, in series with 47k) is a
///     fixed low shelf with its pole at <paramref name="DepthFrequency" /> and its zero at this fraction of it; 1 for a
///     plain resistor. The depth control lowers it further.
/// </param>
public sealed record FeedbackDesign(
    double TapGain,
    double FeedbackResistor,
    double ShuntResistor,
    Pot Presence,
    double PresenceCap,
    double DepthFrequency = 90,
    double LowFeedback = 1
) {
    /// <summary>
    ///     The smallest <see cref="PresenceCap" /> that counts as a presence circuit. A model without one writes a
    ///     placeholder of a picofarad or less, whose corner lies far above the audio band; the smallest real one is the
    ///     Mark IIC+'s 4.7 nF.
    /// </summary>
    public const double SmallestPresenceCap = 100e-12;

    /// <summary>The feedback fraction at mid frequencies, with presence off.</summary>
    public double Beta => ShuntResistor / (FeedbackResistor + ShuntResistor);

    /// <summary>
    ///     Whether the network has a presence cap for the presence frequency to move: false for a model that only
    ///     writes a placeholder (the SVT, the blackface Fenders, the Legacy).
    /// </summary>
    public bool HasPresenceCap => PresenceCap >= SmallestPresenceCap;

    /// <summary>
    ///     The 5F6-A's wiring: the presence pot (<see cref="ShuntResistor" />, its track) runs from the tail's foot to
    ///     ground and <see cref="PresenceCap" /> shunts the part of the track the knob turns in (at 0 the cap sits
    ///     between ground and ground and the feedback is flat; at 10 it shorts the whole shunt at high frequencies), so
    ///     Z = (1 - p) R + p R / (1 + s p R C). False: a fixed shunt with the pot in series with the cap across it.
    /// </summary>
    public bool PresenceIsShunt { get; init; }

    /// <summary>
    ///     A fixed resistor in series with the presence pot and cap across the shunt (the Rect-o-Verb's 2.7k), which
    ///     limits how much of the feedback the presence can take out at 10; 0 for none. Not with
    ///     <see cref="PresenceIsShunt" /> or <see cref="Series" />.
    /// </summary>
    public double PresenceSeries { get; init; }

    /// <summary>
    ///     The feedback fraction's zero and pole time constants at Presence rotation <paramref name="presence" />
    ///     (0..1), the cap divided by <paramref name="frequency" />: β(s) = <see cref="Beta" /> (1 + s τ₁) / (1 + s
    ///     τ₂), from Z / (R_f + Z) with the shunt's impedance Z (first order in either wiring).
    /// </summary>
    public (double Tau1, double Tau2) PresenceTimes(double presence, double frequency = 1) {
        var c = PresenceCap / frequency;
        var (rf, rx) = (FeedbackResistor, ShuntResistor);
        if (PresenceIsShunt) {
            // Z = a + b / (1 + s b C) with the track's parts a = (1 - p) R above the wiper and b = p R under the cap.
            var p = Presence.Fraction(presence);
            var (a, b) = ((1 - p) * rx, p * rx);
            return (a * b * c / rx, b * c * (rf + a) / (rf + rx));
        }

        var rp = PresenceSeries + Presence.Ohms * (1 - Presence.Fraction(presence));
        return (rp * c, c * (rf * (rx + rp) + rx * rp) / (rf + rx));
    }

    /// <summary>
    ///     A fixed series RC across the shunt beside the presence's (the Hiwatts' 470 Ω with 10 nF), which lowers the
    ///     feedback at high frequencies whatever the knob: a treble shelf toward Z = R_x ∥ R_b; null for none (the
    ///     feedback filter is then <see cref="PresenceTimes" />' single section). The presence frequency leaves it.
    /// </summary>
    public ShuntBranch? Branch { get; init; }

    /// <summary>
    ///     With <see cref="Branch" />: the feedback fraction as two first-order sections, β(s) = <c>Gain</c> (1 + s
    ///     <c>Zero1</c>) (1 + s <c>Zero2</c>) / ((1 + s <c>Pole1</c>) (1 + s <c>Pole2</c>)), from 1 / (1 + R_f Y) with
    ///     the shunt's admittance Y = Y_presence + 1 / (R_b + 1 / s C_b) (either presence wiring). The zeros are the
    ///     presence part's and the branch's; the denominator is a real quadratic (an RC network's poles are real), and
    ///     each zero is paired with the pole of the same rank, the slower with the slower.
    /// </summary>
    public (double Gain, double Zero1, double Pole1, double Zero2, double Pole2) BranchTimes(
        double presence,
        double frequency = 1
    ) {
        var branch = Branch ?? new ShuntBranch(0, 0);
        var c = PresenceCap / frequency;
        var (rf, rx) = (FeedbackResistor, ShuntResistor);

        // The presence part's admittance n(s) / d(s), each first order: n0 + n1 s over d0 + d1 s.
        double n0, n1, d0, d1;
        if (PresenceIsShunt) {
            var p = Presence.Fraction(presence);
            var (a, b) = ((1 - p) * rx, p * rx);
            (n0, n1, d0, d1) = (1, b * c, rx, a * b * c);
        } else {
            var rp = PresenceSeries + Presence.Ohms * (1 - Presence.Fraction(presence));
            (n0, n1, d0, d1) = (1, (rp + rx) * c, rx, rx * rp * c);
        }

        // β = d (1 + s τ_b) / ((d + R_f n) (1 + s τ_b) + s R_f C_b d) = d (1 + s τ_b) / (c0 + c1 s + c2 s²).
        var tb = branch.Resistor * branch.Cap;
        var (p0, p1) = (d0 + rf * n0, d1 + rf * n1);
        var c0 = p0;
        var c1 = p1 + p0 * tb + rf * branch.Cap * d0;
        var c2 = p1 * tb + rf * branch.Cap * d1;
        return Sections(d0, d1 / d0, tb, (c0, c1, c2));
    }

    /// <summary>
    ///     β(s) = n0 (1 + s z_a) (1 + s z_b) / (c0 + c1 s + c2 s²) as two first-order sections: the denominator's real
    ///     roots (an RC network's), each zero paired with the pole of the same rank, the slower with the slower.
    /// </summary>
    static (double Gain, double Zero1, double Pole1, double Zero2, double Pole2) Sections(
        double n0,
        double zeroA,
        double zeroB,
        (double C0, double C1, double C2) denominator
    ) {
        var (c0, c1, c2) = denominator;
        var (sum, product) = (c1 / c0, c2 / c0);
        var slow = 0.5 * (sum + Math.Sqrt(Math.Max(sum * sum - 4 * product, 0)));
        var fast = slow > 0 ? product / slow : 0;
        var (zero1, zero2) = zeroA >= zeroB ? (zeroA, zeroB) : (zeroB, zeroA);
        return (n0 / c0, zero1, slow, zero2, fast);
    }

    /// <summary>
    ///     The Mesa Marks' wiring, in place of the presence across the shunt (and of <see cref="Branch" />): from the
    ///     tap, <see cref="Presence" /> as a rheostat (its resistance rises with the knob),
    ///     <see cref="FeedbackResistor" /> and the leg's resistor with <see cref="PresenceCap" /> across it, in series,
    ///     into the tail's foot; from there to ground <see cref="ShuntResistor" /> and the shunt leg's resistor and cap
    ///     in parallel, in series. More presence is less feedback, and less of the cap's treble feedback. Null for the
    ///     other wirings.
    /// </summary>
    public SeriesPresence? Series { get; init; }

    /// <summary>
    ///     With <see cref="Series" />: the feedback fraction as two first-order sections, as
    ///     <see cref="BranchTimes" /> gives them, from Z_s / (Z_r + Z_s) with the series arm Z_r = r_p + R_f + R_l /
    ///     (1 + s R_l C) and the shunt Z_s = R_x + R_m / (1 + s R_m C_m). The zeros are the series leg's (R_l C) and
    ///     the shunt's (R_x R_m C_m / (R_x + R_m)); the poles are a real quadratic's roots, paired by rank.
    /// </summary>
    public (double Gain, double Zero1, double Pole1, double Zero2, double Pole2) SeriesTimes(
        double presence,
        double frequency = 1
    ) {
        var series = Series ?? default;
        var arm = Presence.Ohms * Presence.Fraction(presence) + FeedbackResistor;
        var leg = series.LegResistor * PresenceCap / frequency;
        var shunt = series.ShuntLegResistor * series.ShuntLegCap;

        // Z_r = (r0 + r1 s) / (1 + s τ_l), Z_s = (h0 + h1 s) / (1 + s τ_m);
        // β = (h0 + h1 s) (1 + s τ_l) / ((r0 + r1 s) (1 + s τ_m) + (h0 + h1 s) (1 + s τ_l)).
        var (r0, r1) = (arm + series.LegResistor, arm * leg);
        var (h0, h1) = (ShuntResistor + series.ShuntLegResistor, ShuntResistor * shunt);
        var c0 = r0 + h0;
        var c1 = r1 + r0 * shunt + h1 + h0 * leg;
        var c2 = r1 * shunt + h1 * leg;
        return Sections(h0, h1 / h0, leg, (c0, c1, c2));
    }

    /// <summary>
    ///     With <see cref="Series" /> and its <see cref="SeriesPresence.ShiftCap" /> in circuit (the pull shift): the
    ///     cap C_p in series with the arm makes the feedback fraction third order and takes it to nothing at DC, β(s)
    ///     = <c>Rise</c> s (1 + s <c>Zero1</c>) (1 + s <c>Zero2</c>) / ((1 + s <c>Pole0</c>) (1 + s <c>Pole1</c>) (1 +
    ///     s <c>Pole2</c>)): <see cref="SeriesTimes" />' divider with 1 / (s C_p) added to Z_r. <c>Rise</c> is C_p
    ///     (R_x + R_m); the zeros are <see cref="SeriesTimes" />'; the poles are a real cubic's roots (an RC
    ///     network's), slowest first, each zero paired with the pole of the same rank after the slowest.
    /// </summary>
    public (double Rise, double Pole0, double Zero1, double Pole1, double Zero2, double Pole2) ShiftedTimes(
        double presence,
        double frequency = 1
    ) {
        var series = Series ?? default;
        var arm = Presence.Ohms * Presence.Fraction(presence) + FeedbackResistor;
        var leg = series.LegResistor * PresenceCap / frequency;
        var shunt = series.ShuntLegResistor * series.ShuntLegCap;
        var cap = series.ShiftCap / frequency;

        // With Z_r = (r0 + r1 s) / (1 + s τ_l) + 1 / (s C_p) and Z_s = (h0 + h1 s) / (1 + s τ_m):
        // β = s C_p (h0 + h1 s) (1 + s τ_l) / (s C_p (c0 + c1 s + c2 s²) + (1 + s τ_l) (1 + s τ_m)),
        // c0..c2 being SeriesTimes' denominator.
        var (r0, r1) = (arm + series.LegResistor, arm * leg);
        var (h0, h1) = (ShuntResistor + series.ShuntLegResistor, ShuntResistor * shunt);
        var c0 = r0 + h0;
        var c1 = r1 + r0 * shunt + h1 + h0 * leg;
        var c2 = r1 * shunt + h1 * leg;
        var (slow, middle, fast) = RealRoots(leg + shunt + cap * c0, leg * shunt + cap * c1, cap * c2);
        var (zeroA, zeroB) = (h1 / h0, leg);
        var (zero1, zero2) = zeroA >= zeroB ? (zeroA, zeroB) : (zeroB, zeroA);
        return (cap * h0, slow, zero1, middle, zero2, fast);
    }

    /// <summary>
    ///     The time constants of 1 + d1 s + d2 s² + d3 s³ = (1 + s τ_a) (1 + s τ_b) (1 + s τ_c) where all three are
    ///     real and positive (the poles of an RC network), largest first: the roots of τ³ - d1 τ² + d2 τ - d3 by the
    ///     trigonometric formula on τ / d1, the two largest polished by Newton's method and the smallest taken from
    ///     the product d3, which keeps its relative accuracy however far below the others it lies.
    /// </summary>
    static (double Slow, double Middle, double Fast) RealRoots(double d1, double d2, double d3) {
        var (b, c) = (d2 / (d1 * d1), d3 / (d1 * d1 * d1));
        var p = b - 1.0 / 3;
        var q = -2.0 / 27 + b / 3 - c;
        var (slow, middle) = (1.0 / 3, 1.0 / 3);
        if (p < 0) {
            var radius = 2 * Math.Sqrt(-p / 3);
            var angle = Math.Acos(Math.Clamp(3 * q / (2 * p) * Math.Sqrt(-3 / p), -1, 1)) / 3;
            slow = radius * Math.Cos(angle) + 1.0 / 3;
            middle = radius * Math.Cos(angle - 2 * Math.PI / 3) + 1.0 / 3;
        }

        for (var i = 0; i < 3; i++) {
            slow = Polish(slow);
            middle = Polish(middle);
        }

        return (slow * d1, middle * d1, c / (slow * middle) * d1);

        double Polish(double y) {
            var slope = (3 * y - 2) * y + b;
            return slope == 0 ? y : y - (((y - 1) * y + b) * y - c) / slope;
        }
    }

    /// <summary>
    ///     The feedback fraction β(jω) at <paramref name="hz" /> straight from the network, Z / (R_f + Z) with the
    ///     shunt, the presence (at rotation <paramref name="presence" />) and <see cref="Branch" /> in parallel, or the
    ///     <see cref="Series" /> wiring's divider, with its shift cap in the arm when <paramref name="shift" /> is set
    ///     (control thread; for tests and plots). With <see cref="Deep" />, its branch is across the shunt too,
    ///     <paramref name="deep" /> its switch.
    /// </summary>
    public Complex Response(double hz, double presence, double frequency = 1, bool deep = false, bool shift = false) {
        var s = new Complex(0, 2 * Math.PI * hz);
        var c = PresenceCap / frequency;
        var (rf, rx) = (FeedbackResistor, ShuntResistor);
        if (Series is { } series) {
            var leg = series.LegResistor / (1 + s * series.LegResistor * c);
            var arm = Presence.Ohms * Presence.Fraction(presence) + rf + leg;
            if (shift && series.ShiftCap > 0) {
                arm += frequency / (s * series.ShiftCap);
            }

            var foot = rx + series.ShuntLegResistor / (1 + s * series.ShuntLegResistor * series.ShuntLegCap);
            return foot / (arm + foot);
        }

        Complex z;
        if (PresenceIsShunt) {
            var p = Presence.Fraction(presence);
            z = (1 - p) * rx + p * rx / (1 + s * p * rx * c);
        } else {
            var rp = PresenceSeries + Presence.Ohms * (1 - Presence.Fraction(presence));
            z = 1 / (1 / rx + s * c / (1 + s * rp * c));
        }

        if (Branch is { } branch) {
            z = 1 / (1 / z + s * branch.Cap / (1 + s * branch.Resistor * branch.Cap));
        }

        if (Deep is { } resonator) {
            z = 1 / (1 / z + 1 / resonator.Impedance(s, deep));
        }

        return z / (rf + z);
    }

    /// <summary>
    ///     A Deep switch's resonant branch across the shunt (the DSL100's simulated inductor); null for none. With it
    ///     the Depth knob does nothing (<see cref="DepthFrequency" /> and <see cref="LowFeedback" /> are unused): the
    ///     branch is the amp's depth circuit, in either switch position. Not with <see cref="Branch" /> or
    ///     <see cref="Series" />.
    /// </summary>
    public DeepSwitchDesign? Deep { get; init; }

    /// <summary>
    ///     With <see cref="Deep" />: the feedback fraction as a first-order and a second-order section in series,
    ///     β(s) = <c>Gain</c> (1 + s <c>Zero</c>) / (1 + s <c>Pole</c>) × (1 + s <c>N1</c> + s² <c>N2</c>) / (1 + s
    ///     <c>D1</c> + s² <c>D2</c>), from 1 / (1 + R_f Y) with the shunt's admittance Y = Y_presence + 1 / Z_deep
    ///     (either presence wiring). The numerator is the presence part's zero and the branch's own series resonance;
    ///     the denominator is a cubic: the first-order pole is its largest real root, or its one real root where the
    ///     other two are a complex pair of larger real part, and those two are the second-order section's.
    ///     <paramref name="frequency" /> divides the presence cap, <paramref name="deepFrequency" /> scales the
    ///     branch's resonance (its cap and inductance divided by it).
    /// </summary>
    public (double Gain, double Zero, double Pole, double N1, double N2, double D1, double D2) DeepTimes(
        double presence,
        bool on,
        double frequency = 1,
        double deepFrequency = 1
    ) {
        var deep = Deep ?? throw new InvalidOperationException("The feedback network has no Deep branch.");
        var c = PresenceCap / frequency;
        var (rf, rx) = (FeedbackResistor, ShuntResistor);

        // The presence part's admittance n(s) / d(s), each first order (as BranchTimes).
        double n0, n1, d0, d1;
        if (PresenceIsShunt) {
            var p = Presence.Fraction(presence);
            var (a, b) = ((1 - p) * rx, p * rx);
            (n0, n1, d0, d1) = (1, b * c, rx, a * b * c);
        } else {
            var rp = Presence.Ohms * (1 - Presence.Fraction(presence));
            (n0, n1, d0, d1) = (1, (rp + rx) * c, rx, rx * rp * c);
        }

        // The branch: 1 / Z = s C / q(s), q = 1 + s R C + s² L C.
        var cap = deep.Cap / deepFrequency;
        var q1 = deep.Resistance(on) * cap;
        var q2 = deep.Inductance / deepFrequency * cap;

        // β = d q / ((d + R_f n) q + s R_f C d) = d q / (c0 + c1 s + c2 s² + c3 s³).
        var (p0, p1) = (d0 + rf * n0, d1 + rf * n1);
        var c0 = p0;
        var c1 = p1 + p0 * q1 + rf * cap * d0;
        var c2 = p1 * q1 + p0 * q2 + rf * cap * d1;
        var c3 = p1 * q2;

        // A real pole, s = -x, of g(x) = c3 x³ - c2 x² + c1 x - c0 (its roots' real parts are positive: the network is
        // passive, so the largest real root is under their sum, c2 / c3). With a real root at or beyond g's local
        // minimum, Newton from the sum only comes down to the largest one; otherwise g has one real root, bisected.
        double Cubic(double at) => ((c3 * at - c2) * at + c1) * at - c0;
        var x = c2 / c3;
        var bend = c2 * c2 - 3 * c1 * c3;
        if (bend >= 0 && Cubic((c2 + Math.Sqrt(bend)) / (3 * c3)) <= 0) {
            for (var i = 0; i < 100; i++) {
                var next = x - Cubic(x) / ((3 * c3 * x - 2 * c2) * x + c1);
                var done = !(Math.Abs(next - x) > 1e-15 * x);
                x = next;
                if (done) {
                    break;
                }
            }
        } else {
            var (low, high) = (0.0, x);
            for (var i = 0; i < 200 && high - low > 1e-15 * high; i++) {
                var middle = 0.5 * (low + high);
                if (Cubic(middle) < 0) {
                    low = middle;
                } else {
                    high = middle;
                }
            }

            x = 0.5 * (low + high);
        }

        // What is left of c0 (1 + s τ_a) (1 + s τ_b) (1 + s / x), from the time constants' sum and product or, where
        // 1 / x is most of that sum (the slowest pole was removed), from the roots' sum and product: each exact, and
        // each without the other's cancellation.
        if (1 / x < 0.5 * c1 / c0) {
            return (d0 / c0, d1 / d0, 1 / x, q1, q2, c1 / c0 - 1 / x, c3 * x / c0);
        }

        var (sum, product) = (c2 / c3 - x, c0 / (c3 * x));
        return (d0 / c0, d1 / d0, 1 / x, q1, q2, sum / product, 1 / product);
    }
}

/// <summary>
///     A Deep switch as the JCM2000 DSL has it (<see cref="FeedbackDesign.Deep" />): from the feedback node to ground,
///     <paramref name="OffResistor" />, <paramref name="Cap" /> and an inductor of <paramref name="Inductance" />
///     henries with <paramref name="SeriesResistance" /> in series; the switch shorts the resistor. At the series
///     resonance, 1 / (2π √(L C)), the branch is down to its resistance and takes the feedback away, so the amp's
///     response rises there (and its damping of the speaker's resonance falls). The inductor is an op-amp gyrator: a
///     follower bootstraps the junction of R_a (from the input) and R_b (to ground) through a cap C_g, which makes
///     Z = R_a + R_b + s C_g R_a R_b (<see cref="Gyrator" />); the op-amp is taken as ideal.
/// </summary>
/// <param name="Switch">The panel switch that shorts <paramref name="OffResistor" />.</param>
/// <param name="Cap">The series cap.</param>
/// <param name="Inductance">The (simulated) inductor.</param>
/// <param name="SeriesResistance">The inductor's series resistance.</param>
/// <param name="OffResistor">In series with the branch while the switch is off.</param>
public sealed record DeepSwitchDesign(
    SwitchRole Switch,
    double Cap,
    double Inductance,
    double SeriesResistance,
    double OffResistor
) {
    /// <summary>
    ///     The branch with a gyrator for its inductor: <paramref name="ra" /> from the input to the bootstrapped
    ///     junction, <paramref name="rb" /> from there to ground, <paramref name="gyratorCap" /> from the follower's
    ///     output to the junction.
    /// </summary>
    public static DeepSwitchDesign Gyrator(
        SwitchRole role,
        double cap,
        double ra,
        double rb,
        double gyratorCap,
        double offResistor
    ) =>
        new(role, cap, gyratorCap * ra * rb, ra + rb, offResistor);

    /// <summary>The series resonance, hertz.</summary>
    public double Resonance => 1 / (2 * Math.PI * Math.Sqrt(Inductance * Cap));

    /// <summary>The branch's series resistance with the switch <paramref name="on" /> or off.</summary>
    public double Resistance(bool on) => SeriesResistance + (on ? 0 : OffResistor);

    /// <summary>The branch's impedance at <paramref name="s" />.</summary>
    public Complex Impedance(Complex s, bool on) => Resistance(on) + 1 / (s * Cap) + s * Inductance;
}

/// <summary>A fixed series resistor and cap across the feedback shunt (<see cref="FeedbackDesign.Branch" />).</summary>
/// <param name="Resistor">The branch's resistor.</param>
/// <param name="Cap">The branch's cap.</param>
public readonly record struct ShuntBranch(double Resistor, double Cap);

/// <summary>
///     The legs of a series presence (<see cref="FeedbackDesign.Series" />): the series arm's resistor (with the
///     design's presence cap across it) and the shunt's resistor and cap in parallel.
/// </summary>
/// <param name="LegResistor">In the series arm, with <see cref="FeedbackDesign.PresenceCap" /> across it.</param>
/// <param name="ShuntLegResistor">In the shunt, under <see cref="FeedbackDesign.ShuntResistor" />.</param>
/// <param name="ShuntLegCap">Across <paramref name="ShuntLegResistor" />.</param>
/// <param name="ShiftCap">
///     A cap in series with the rheostat that a switch shorts until the presence's pull shift opens it (the Mark IV's
///     1 nF on the lead rheostat, 680 pF on Rhythm 2's): shifted, the amp has feedback only where the cap passes it,
///     in the top of the band (<see cref="FeedbackDesign.ShiftedTimes" />). 0 for none. The Circuit Amp's Presence
///     Shift switch is this pull on a model that has it.
/// </param>
public readonly record struct SeriesPresence(
    double LegResistor,
    double ShuntLegResistor,
    double ShuntLegCap,
    double ShiftCap = 0
);

/// <summary>
///     A rectifier: silicon (no drop to speak of) or a vacuum diode, whose drop follows Child's law,
///     V = (I / <paramref name="Perveance" />)^(2/3), fitted to a datasheet drop at a rated current.
/// </summary>
/// <param name="Name">The tube, e.g. "GZ34".</param>
/// <param name="Perveance">I = k V^1.5 in A/V^1.5; 0 for silicon.</param>
public sealed record RectifierModel(string Name, double Perveance) {
    /// <summary>A silicon bridge or full-wave pair: about a volt, neglected.</summary>
    public static RectifierModel Silicon { get; } = new("Silicon", 0);

    /// <summary>GZ34 / 5AR4: about 17 V at 250 mA, the stiffest tube rectifier.</summary>
    public static RectifierModel GZ34 { get; } = new("GZ34", 0.25 / Math.Pow(17, 1.5));

    /// <summary>5U4GB: about 44 V at 275 mA.</summary>
    public static RectifierModel FiveU4 { get; } = new("5U4", 0.275 / Math.Pow(44, 1.5));

    /// <summary>5Y3GT: about 60 V at 125 mA, the softest (tweed Deluxe).</summary>
    public static RectifierModel FiveY3 { get; } = new("5Y3", 0.125 / Math.Pow(60, 1.5));

    /// <summary>EZ81 / 6CA4: about 25 V at 150 mA (approximate).</summary>
    public static RectifierModel EZ81 { get; } = new("EZ81", 0.15 / Math.Pow(25, 1.5));

    /// <summary>The rectifiers, in the order of the Circuit Amp's Rectifier choice (after Stock).</summary>
    public static IReadOnlyList<RectifierModel> All { get; } = [Silicon, GZ34, FiveU4, FiveY3, EZ81];

    /// <summary>The drop at <paramref name="current" /> amperes.</summary>
    public double Drop(double current) => Perveance > 0 && current > 0 ? Math.Pow(current / Perveance, 2.0 / 3) : 0;
}

/// <summary>
///     The power supply: a rectifier and transformer of <paramref name="SourceResistance" /> charging the reservoir cap
///     (the plates' centre tap), a choke or resistor to the screen node, and a dropping resistor to the preamp node,
///     which draws <paramref name="PreampCurrent" />. Sag is the reservoir falling under the tubes' current draw.
/// </summary>
/// <param name="SourceResistance">
///     The transformer and rectifier as one resistance (the rectifier's own drop besides).
/// </param>
/// <param name="Reservoir">The first filter cap.</param>
/// <param name="ChokeResistance">The choke (or resistor) to the screen node.</param>
/// <param name="ScreenCap">The screen node's cap.</param>
/// <param name="PreampResistance">The dropping resistor to the preamp node.</param>
/// <param name="PreampCap">The preamp node's cap.</param>
/// <param name="PreampCurrent">What the preamp draws.</param>
/// <param name="Rectifier">
///     A tube rectifier's drop on top of <paramref name="SourceResistance" />; null where the source resistance already
///     stands for the whole rectifier (the first models).
/// </param>
/// <param name="LineFrequency">
///     The mains frequency, for an AC supply that charges the reservoir on the peaks of the rectified mains (ripple at
///     twice it); 0 for a DC source (no ripple).
/// </param>
public sealed record PowerSupplyDesign(
    double SourceResistance,
    double Reservoir,
    double ChokeResistance,
    double ScreenCap,
    double PreampResistance,
    double PreampCap,
    double PreampCurrent = 4e-3,
    RectifierModel? Rectifier = null,
    double LineFrequency = 0
);

/// <summary>
///     One RC decoupling node of the preamp's supply: a dropping resistor from <paramref name="Parent" />'s node into
///     this node's filter cap, and the stages on it drawing <paramref name="Current" />. Each node filters the supply's
///     ripple and sag once more, so the stages furthest down the chain (a high-gain amp's input stages) see the least
///     of it.
/// </summary>
/// <param name="Resistance">The dropping resistor from the parent node.</param>
/// <param name="Cap">The node's filter cap.</param>
/// <param name="Current">What the stages on this node draw (its children's draw passes through it as well).</param>
/// <param name="Parent">
///     The node it hangs from: 0 for the power supply's preamp node
///     (<see cref="PowerSupplyDesign.PreampResistance" />), k for the k-th node of the chain (an earlier one).
/// </param>
public sealed record DecouplingNode(double Resistance, double Cap, double Current, int Parent = 0);

/// <summary>
///     The preamp's supply below the power supply's preamp node: its decoupling nodes, and on each preamp stage the
///     node it runs on (<see cref="AmpStageDesign.SupplyNode" />). A model without one gets the generic chain
///     (<see cref="AmpChannel" />).
/// </summary>
/// <param name="Nodes">
///     The nodes in order: node k is <c>Nodes[k - 1]</c>; each hangs from node 0 or an earlier one.
/// </param>
public sealed record PreampSupplyDesign(IReadOnlyList<DecouplingNode> Nodes) {
    /// <summary>
    ///     The preamp has a supply of its own, which the power amp's current does not move (a rack preamp in front of
    ///     a separate power amp): every stage runs at its nominal rails.
    /// </summary>
    public bool Separate { get; init; }

    /// <summary>A rack preamp's own supply (see <see cref="Separate" />).</summary>
    public static PreampSupplyDesign OwnSupply { get; } = new([]) { Separate = true };
}

/// <summary>
///     The phase inverter's input network: the coupling cap from the preamp's last stage into the driven grid, and the
///     grid leak to the bias junction (which follows the input: the leak is bootstrapped). With it, the driven grid
///     draws grid current (Dempwolf's law, at the grid-cathode voltage of the inverter's tables, which include the
///     plates' load through the power tubes' grid leaks and the cathode's rise from the grid current itself) through
///     the previous stage's output resistance and charges the cap: the inverter's own blocking under hard drive. The
///     feedback's share of the cathode's movement enters a sample late. Without it (the first models) the inverter's
///     input is the previous stage's voltage, unloaded.
/// </summary>
public sealed record InverterInputDesign(double CouplingCap, double GridLeak);

/// <summary>
///     A front-panel switch on the power supply, which while it is on puts <paramref name="Rectifier" /> in place of
///     the design's rectifier (Mesa's tube/silicon rectifier switch) and scales the mains by <paramref name="Variac" />
///     (Mesa's Bold/Spongy, a lower primary tap). The Rectifier and Variac expert parameters apply on top: a rectifier
///     chosen there wins, and the variacs multiply.
/// </summary>
/// <param name="Role">The switch.</param>
/// <param name="Rectifier">The rectifier while it is on; null to keep the design's.</param>
/// <param name="Variac">The mains while it is on, as a fraction of nominal.</param>
public sealed record SupplySwitchDesign(SwitchRole Role, RectifierModel? Rectifier = null, double Variac = 1);

/// <summary>How the output tubes drive the transformer.</summary>
public enum PowerTopology {
    /// <summary>Two sides in push-pull across a centre-tapped primary.</summary>
    PushPull,

    /// <summary>
    ///     One side (class A, a gapped core carrying the idle current): no phase inverter, the last preamp stage drives
    ///     the grid through the coupling cap.
    /// </summary>
    SingleEnded
}

/// <summary>
///     A push-pull (or single-ended) pentode power amp: phase inverter, coupling caps and grid networks, the output
///     tubes, the output transformer and speaker load, global negative feedback and the power supply.
/// </summary>
/// <param name="Name">A label, e.g. "2 x EL34".</param>
/// <param name="Tube">The output tube.</param>
/// <param name="TubesPerSide">1 for a pair, 2 for a quad (paralleled).</param>
/// <param name="Supply">B+ at the transformer centre tap at idle, in volts.</param>
/// <param name="ScreenResistor">
///     Per tube, from the screen node (or from <see cref="PowerAmpDesign.SharedScreenResistor" />'s foot).
/// </param>
/// <param name="IdleCurrent">Plate current per tube at idle with the bias knob at noon, in amperes.</param>
/// <param name="CathodeBias">Cathode-biased (a bypassed cathode resistor) rather than fixed bias.</param>
/// <param name="CathodeBypass">
///     The cathode resistor's bypass cap (cathode bias; per side with separate
///     cathodes).
/// </param>
/// <param name="RatedPower">
///     Output power at the onset of clipping, in watts. It sizes the output transformer's core where the transformer
///     names no <see cref="OutputTransformerDesign.SaturationFlux" />: the knee sits at eight times the flux of this
///     power at 40 Hz.
/// </param>
/// <param name="PhaseInverter">The phase inverter driving the output tubes (ignored when single-ended).</param>
/// <param name="CouplingCap">Phase inverter plates to the power-tube grids.</param>
/// <param name="GridLeak">Power-tube grid leaks (bias feed).</param>
/// <param name="GridStopper">Series resistors at the power-tube grids.</param>
/// <param name="CutCap">
///     Vox "cut": a cap in series with <paramref name="CutPot" /> between the phase inverter's plates; 0 for none.
/// </param>
/// <param name="CutPot">The cut pot.</param>
/// <param name="Feedback">The feedback network.</param>
/// <param name="HasFeedback">
///     False for an amp without global feedback (the AC30), where <paramref name="Feedback" /> only applies when the
///     feedback knob is turned past noon.
/// </param>
/// <param name="Transformer">The output transformer.</param>
/// <param name="Speaker">The speaker load.</param>
/// <param name="PowerSupply">The supply and its sag.</param>
/// <param name="Topology">Push-pull or single-ended.</param>
/// <param name="UltralinearTap">
///     Ultralinear: the screens hang on taps at this fraction of each half-primary (0.43 is common); 0 for pentode
///     operation (screens on the screen node).
/// </param>
/// <param name="Strapped">
///     A second tube type wired as a triode (screen to plate) beside the main tubes on each side: Mesa's Simul-Class,
///     a pentode pair plus a triode-strapped pair; null for none.
/// </param>
/// <param name="StrappedPerSide">How many <paramref name="Strapped" /> tubes per side.</param>
/// <param name="TriodeSwitch">
///     A front-panel switch that straps the main tubes as triodes (a Hi/Low or Pentode/Triode switch); null for none.
/// </param>
/// <param name="SeparateCathodes">
///     Cathode bias with a resistor and bypass cap per side (per tube, with the tubes of a side in parallel) instead of
///     one shared by all.
/// </param>
/// <param name="InverterInput">
///     The phase inverter's input network with grid current; null for none (see the
///     type).
/// </param>
/// <param name="ShuntMaster">
///     A master volume wired as a rheostat across the phase inverter's outputs (Matchless DC-30), turned by the Master
///     knob: its full track at 10, a short at 0; beside the cut control if there is one. Null for none.
/// </param>
/// <param name="CathodeResistor">
///     Cathode bias: the schematic's cathode resistor (all tubes'; with separate cathodes, half of each side's), which
///     then sets the idle current in place of <paramref name="IdleCurrent" /> (a replacement tube keeps it, as it would
///     in the amp); 0 to derive it from <paramref name="IdleCurrent" />.
/// </param>
/// <param name="SupplySwitches">
///     Front-panel switches on the supply (a rectifier switch, Bold/Spongy); null for none.
/// </param>
/// <param name="ScreenSupply">
///     The screen node's idle voltage where the amp runs its screens far below B+ from a winding of their own (the
///     Ampeg SVT: 350 V screens under 680 V plates), for the stock bias, which gives <paramref name="IdleCurrent" /> at
///     B+ with the screens there; set <see cref="PowerSupplyDesign.ChokeResistance" /> and a large
///     <see cref="PowerSupplyDesign.ScreenCap" /> so that the node rests there. 0 for screens near B+ (the bias is then
///     found with the screens at B+).
/// </param>
/// <param name="FeedbackOffSwitch">
///     A front-panel switch that takes the global feedback out while it is on (Mesa's Modern mode on the Rectifiers);
///     null for none.
/// </param>
/// <param name="Loop">
///     A preamp stage inside the global feedback loop, ahead of the phase inverter (the Marshall Major's V2A and tone
///     stack, with the feedback at V2A's cathode): the power amp's input is then that stage's grid, and the feedback
///     subtracts there. Null for none (the feedback at the inverter). Push-pull only.
/// </param>
/// <param name="Brilliance">
///     The cut is the amp's treble control turned the other way (the Tiny Terror's Tone, the Chieftain's and Hot Cat's
///     Brilliance): <paramref name="CutPot" />'s resistance rises with the knob by its taper, so turning it up is
///     brighter, and the Circuit Amp's Presence knob (which these panels name after the control) turns it, from noon
///     by default. The Cut knob and the feedback's presence then do nothing (the presence stays at noon).
/// </param>
/// <param name="PresencePot">
///     The model control that turns the feedback's presence pot in place of the Presence knob: one presence pot that
///     the amp's relays move between the feedback and a preamp stage (the three-channel Rectifiers', on Control 2).
///     Null for the Presence knob.
/// </param>
/// <param name="PresenceOut">
///     A front-panel switch whose relay takes the presence pot out of the feedback (see the type); null for none.
/// </param>
public sealed record PowerAmpDesign(
    string Name,
    PentodeModel Tube,
    int TubesPerSide,
    double Supply,
    double ScreenResistor,
    double IdleCurrent,
    bool CathodeBias,
    double CathodeBypass,
    double RatedPower,
    PhaseInverterDesign PhaseInverter,
    double CouplingCap,
    double GridLeak,
    double GridStopper,
    double CutCap,
    Pot CutPot,
    FeedbackDesign Feedback,
    bool HasFeedback,
    OutputTransformerDesign Transformer,
    SpeakerDesign Speaker,
    PowerSupplyDesign PowerSupply,
    PowerTopology Topology = PowerTopology.PushPull,
    double UltralinearTap = 0,
    PentodeModel? Strapped = null,
    int StrappedPerSide = 0,
    SwitchRole? TriodeSwitch = null,
    bool SeparateCathodes = false,
    InverterInputDesign? InverterInput = null,
    Pot? ShuntMaster = null,
    double CathodeResistor = 0,
    IReadOnlyList<SupplySwitchDesign>? SupplySwitches = null,
    double ScreenSupply = 0,
    SwitchRole? FeedbackOffSwitch = null,
    LoopStageDesign? Loop = null,
    bool Brilliance = false,
    PotRole? PresencePot = null,
    PresenceOutDesign? PresenceOut = null
) {
    /// <summary>1 when single-ended, else 2.</summary>
    public int Sides => Topology == PowerTopology.SingleEnded ? 1 : 2;

    /// <summary>
    ///     The amp's bias tremolo: an oscillator swinging the grid leaks' return (<see cref="BiasTremoloDesign" />),
    ///     run by <see cref="PowerAmp" />; null for none.
    /// </summary>
    public BiasTremoloDesign? BiasTremolo { get; init; }

    /// <summary>
    ///     A dropping resistor that all the screens share, from the screen node to the junction of the tubes' own
    ///     <see cref="ScreenResistor" />s (the JTM45's 1k 2 W): it carries every tube's screen current, so one side's
    ///     screen current pulls the other side's screens down too. 0 for none (each screen resistor on the screen
    ///     node). Pentode operation only: it is not in the path of ultralinear or triode-strapped screens.
    /// </summary>
    public double SharedScreenResistor { get; init; }

    /// <summary>A front-panel switch that raises or lowers the feedback (see the type); null for none.</summary>
    public FeedbackBoostDesign? FeedbackBoost { get; init; }

    /// <summary>
    ///     With a series presence that has a shift cap (<see cref="SeriesPresence.ShiftCap" />): the amp's relays, not
    ///     a pull switch, put the cap in, and it is in while none of these switches is on (the Mark V's Extreme mode,
    ///     which is neither IIC+ nor Mark IV). The Circuit Amp's Presence Shift then does nothing. Null where the
    ///     shift is the Presence Shift parameter.
    /// </summary>
    public IReadOnlyList<SwitchRole>? ShiftUnless { get; init; }
}
