using System.Numerics;

namespace Cathode.Dsp.Cabinets;

/// <summary>A resonance (or notch, with a negative gain) as a peaking-filter magnitude: centre, gain and Q.</summary>
public readonly record struct Resonance(double Hz, double Db, double Q);

/// <summary>
///     A guitar loudspeaker as compact parametric data. The low end comes from the Thiele-Small values (the enclosure
///     turns them into the system resonance), the piston band and cone breakup from <see cref="Breakup" />, the top end
///     from the high cut. Geometry sets how the response changes with mic position. <see cref="Electrical" /> is the same
///     speaker as an electrical load: what a Circuit Amp following this cab drives (<c>AutoSpeaker</c>).
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="ConeRadius">Effective radiating radius in metres (from Sd where published: a = sqrt(Sd / pi)).</param>
/// <param name="CapRadius">Dust cap (or dome) radius in metres.</param>
/// <param name="Fs">Free-air resonance in Hz, from the datasheet.</param>
/// <param name="Vas">Equivalent compliance volume in litres.</param>
/// <param name="DecoupleHz">
///     Above this the cone stops moving as one piece and the radiating area shrinks towards the voice coil and the
///     dust cap (a_eff = a / sqrt(1 + (f / DecoupleHz)^2)): sets how quickly the sound darkens off-centre.
/// </param>
/// <param name="HighCutHz">Where the datasheet chart falls away at the top.</param>
/// <param name="HighCutOrder">Slope of that fall, in 6 dB/octave steps.</param>
/// <param name="Breakup">Cone breakup peaks and dips, lowest first: the n-th is weighted by the n-th radial mode.</param>
/// <param name="Electrical">The voice coil and the motional Qs: the speaker's impedance curve.</param>
public sealed record SpeakerModel(
    string Name,
    double ConeRadius,
    double CapRadius,
    double Fs,
    double Vas,
    double DecoupleHz,
    double HighCutHz,
    int HighCutOrder,
    Resonance[] Breakup,
    SpeakerElectrical Electrical
) {
    /// <summary>Total Q at Fs (<see cref="SpeakerElectrical.Qts" />): the acoustic low end and the load share it.</summary>
    public double Qts => Electrical.Qts;

    /// <summary>The free-air impedance in ohms at <paramref name="hz" /> (<see cref="SpeakerElectrical.Impedance" />).</summary>
    public Complex Impedance(double hz) => Electrical.Impedance(hz, Fs, 1);
}

/// <summary>
///     A loudspeaker's small-signal electrical data (Thiele-Small), for its impedance
///     <c>Z(f) = Re + Z_L(f) + Res / (1 + j Qms (f / Fs − Fs / f))</c>, <c>Res = Re Qms / Qes</c>: the voice coil's DC
///     resistance, the motional parallel resonance (peaking at <c>Re + Res</c> at Fs, its width set by Qms), and the
///     voice coil's lossy inductance. Eddy currents in the pole piece make that inductance a semi-inductance,
///     <c>Z_L = K (jω)^n</c> with n below 1 (Wright, "An Empirical Model for Loudspeaker Motor Impedance", AES 1990;
///     Vanderkooy, JAES 1989): K is set so its reactance at <see cref="LeFrequency" /> is the datasheet's
///     <c>2π f Le</c>.
/// </summary>
/// <param name="Nominal">Nominal impedance in ohms.</param>
/// <param name="Re">Voice-coil DC resistance in ohms.</param>
/// <param name="Qms">Mechanical Q at Fs.</param>
/// <param name="Qes">Electrical Q at Fs.</param>
/// <param name="Le">Voice-coil inductance in henries, as datasheets quote it at <see cref="LeFrequency" />.</param>
/// <param name="SemiExponent">
///     The semi-inductance's exponent n (1 a pure inductance); 0.7 is typical of steel-pole guitar and bass drivers
///     (Wright's fits run 0.6-0.8).
/// </param>
public sealed record SpeakerElectrical(
    double Nominal,
    double Re,
    double Qms,
    double Qes,
    double Le,
    double SemiExponent = 0.7
) {
    /// <summary>Where datasheets quote Le, Hz.</summary>
    public const double LeFrequency = 1_000;

    /// <summary>The second frequency the lumped voice coil (<see cref="LossyCoil" />) is fitted at, Hz.</summary>
    public const double FitFrequency = 5_000;

    /// <summary>Typical Qms of a ceramic paper-cone guitar speaker (Jensen's C12N: 6.6-7.5), where none is published.</summary>
    public const double TypicalCeramicQms = 7;

    /// <summary>Typical Qms of an alnico paper-cone guitar speaker (Jensen's P10R: 15-24), where none is published.</summary>
    public const double TypicalAlnicoQms = 10;

    /// <summary>
    ///     Typical Le of a 12" guitar speaker's 1.75" voice coil, where none is published (Jensen's C12N: 0.90 mH at
    ///     1 kHz).
    /// </summary>
    public const double TypicalLe = 0.9e-3;

    /// <summary>Total Q at Fs: Qms and Qes in parallel.</summary>
    public double Qts => Qms * Qes / (Qms + Qes);

    /// <summary>The motional resistance: the impedance peak's height above Re.</summary>
    public double Res => Re * Qms / Qes;

    /// <summary>
    ///     A speaker whose datasheet gives Qts (or whose Qts was fitted) and a Qms: Qes follows as
    ///     <c>Qms Qts / (Qms − Qts)</c>.
    /// </summary>
    public static SpeakerElectrical FromQts(double nominal, double re, double qts, double qms, double le) =>
        new(nominal, re, qms, qms * qts / (qms - qts), le);

    /// <summary>The voice coil's semi-inductance <c>K (jω)^n</c> at <paramref name="hz" />, ohms.</summary>
    public Complex Coil(double hz) {
        var at = 2 * Math.PI * LeFrequency;
        var k = at * Le / (Math.Pow(at, SemiExponent) * Math.Sin(SemiExponent * Math.PI / 2));
        return Complex.FromPolarCoordinates(k * Math.Pow(2 * Math.PI * hz, SemiExponent), SemiExponent * Math.PI / 2);
    }

    /// <summary>
    ///     The impedance in ohms at <paramref name="hz" /> with the resonance at <paramref name="resonance" /> hertz
    ///     and the motional Q multiplied by <paramref name="stiffening" /> (a closed box's <c>sqrt(1 + Vas / Vb)</c>,
    ///     which raises Fs and Qms alike and leaves Res alone; 1 in free air).
    /// </summary>
    public Complex Impedance(double hz, double resonance, double stiffening) =>
        Re + Coil(hz) + Res / new Complex(1, Qms * stiffening * (hz / resonance - resonance / hz));

    /// <summary>
    ///     The semi-inductance as the power amp's lumped voice coil: an inductance in parallel with an eddy-current
    ///     resistance, chosen so that <c>|Re + Z_coil|</c> matches <c>|Re + K (jω)^n|</c> at <see cref="LeFrequency" />
    ///     and <see cref="FitFrequency" /> (Newton's method on the logarithms, from the fit of the coil's magnitude
    ///     alone). Control thread.
    /// </summary>
    public (double Inductance, double EddyResistance) LossyCoil() {
        var w1 = 2 * Math.PI * LeFrequency;
        var w2 = 2 * Math.PI * FitFrequency;
        var t1 = Math.Log((Re + Coil(LeFrequency)).Magnitude);
        var t2 = Math.Log((Re + Coil(FitFrequency)).Magnitude);

        // Start: the parallel pair whose magnitude alone matches the coil's, 1 / |Z|² = 1 / (ωL)² + 1 / R².
        var a = 1 / Coil(LeFrequency).Magnitude;
        var b = 1 / Coil(FitFrequency).Magnitude;
        var inverseL2 = (a * a - b * b) / (1 / (w1 * w1) - 1 / (w2 * w2));
        var inverseR2 = Math.Max(a * a - inverseL2 / (w1 * w1), 1e-6);
        var x = Math.Log(1 / Math.Sqrt(inverseL2));
        var y = Math.Log(1 / Math.Sqrt(inverseR2));

        (double, double) Error(double lnL, double lnR) {
            return (At(w1) - t1, At(w2) - t2);

            double At(double omega) {
                var l = new Complex(0, omega * Math.Exp(lnL));
                var r = Math.Exp(lnR);
                return Math.Log((Re + l * r / (l + r)).Magnitude);
            }
        }

        const double Step = 1e-6;
        for (var i = 0; i < 50; i++) {
            var (e1, e2) = Error(x, y);
            if (Math.Abs(e1) + Math.Abs(e2) < 1e-12) {
                break;
            }

            var (x1, x2) = Error(x + Step, y);
            var (y1, y2) = Error(x, y + Step);
            var j11 = (x1 - e1) / Step;
            var j21 = (x2 - e2) / Step;
            var j12 = (y1 - e1) / Step;
            var j22 = (y2 - e2) / Step;
            var det = j11 * j22 - j12 * j21;
            if (Math.Abs(det) < 1e-15) {
                break;
            }

            x -= Math.Clamp((e1 * j22 - e2 * j12) / det, -1, 1);
            y -= Math.Clamp((j11 * e2 - j21 * e1) / det, -1, 1);
        }

        return (Math.Exp(x), Math.Exp(y));
    }
}

/// <summary>
///     A microphone as compact parametric data: its far-field on-axis response (as on the datasheet chart, which is
///     measured at about 1 m) and its first-order polar pattern <c>A + (1 - A) cos θ</c>, which sets the proximity effect.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="Omni">
///     The pattern's pressure share A: 1 omni, 0.5 cardioid, 0.37 supercardioid, 0.25 hypercardioid, 0 figure-8.
///     The pressure-gradient share (1 - A) is what proximity boosts.
/// </param>
/// <param name="DiaphragmRadius">Diaphragm (or ribbon) radius in metres: sets off-axis treble loss.</param>
/// <param name="LowCutHz">Far-field low corner (second order, Q 0.7).</param>
/// <param name="HighCutHz">High corner.</param>
/// <param name="HighCutOrder">High slope, in 6 dB/octave steps.</param>
/// <param name="Peaks">Presence peaks and dips.</param>
public sealed record MicModel(
    string Name,
    double Omni,
    double DiaphragmRadius,
    double LowCutHz,
    double HighCutHz,
    int HighCutOrder,
    Resonance[] Peaks
);

/// <summary>
///     A cabinet: closed (a sealed box of <see cref="LitresPerSpeaker" />) or open-backed (a dipole whose back wave
///     comes round the cabinet <see cref="DipolePath" /> later), with the speakers' layout for the ones not miked.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="Open">Open back.</param>
/// <param name="LitresPerSpeaker">Closed box: net internal volume per speaker.</param>
/// <param name="DipolePath">Open back: extra path from the back of the cone round to the front, in metres.</param>
/// <param name="BackWave">Open back: level of the back wave at low frequencies (the baffle shades it above).</param>
/// <param name="Height">Height of the miked speaker's centre above the floor, in metres.</param>
/// <param name="Recess">Grille cloth to the cone's acoustic centre, in metres: the mic's distance starts at the cloth.</param>
/// <param name="Others">The other speakers' centres relative to the miked one (x right, y up), in metres.</param>
/// <param name="Modes">Box resonances (standing waves between the panels; panel resonance on open cabs).</param>
/// <param name="Series">
///     How many speakers are wired in series in each parallel string (1: all in parallel, as in the combos and the
///     4x10 Bassman and Super Reverb; 2 for a 4x12's series-parallel). The cabinet's load is one speaker's impedance
///     times <see cref="WiringFactor" />.
/// </param>
public sealed record EnclosureModel(
    string Name,
    bool Open,
    double LitresPerSpeaker,
    double DipolePath,
    double BackWave,
    double Height,
    double Recess,
    (double X, double Y)[] Others,
    Resonance[] Modes,
    int Series = 1
) {
    /// <summary>The number of speakers.</summary>
    public int Count => Others.Length + 1;

    /// <summary>The number of parallel strings.</summary>
    public int Parallel => Math.Max(1, Count / Math.Max(1, Series));

    /// <summary>The wiring's impedance scale, series over parallel: one speaker's impedance times this is the cabinet's.</summary>
    public double WiringFactor => (double)Math.Max(1, Series) / Parallel;

    /// <summary>
    ///     A closed box's air spring on each speaker, <c>sqrt(1 + Vas / Vb)</c> (Small, "Closed-Box Loudspeaker
    ///     Systems", JAES 1972): it raises the resonance and the Qs alike. 1 for an open back, whose speakers work
    ///     essentially in free air.
    /// </summary>
    public double Stiffening(SpeakerModel speaker) =>
        Open || LitresPerSpeaker <= 0 ? 1 : Math.Sqrt(1 + speaker.Vas / LitresPerSpeaker);

    /// <summary>
    ///     The whole cabinet's impedance in ohms at <paramref name="hz" /> with <paramref name="speaker" /> in every
    ///     position. The speakers are identical and share the box equally, so each sees its own share of the air
    ///     (<see cref="LitresPerSpeaker" />) and the wiring only scales the one speaker's curve.
    /// </summary>
    public Complex Impedance(SpeakerModel speaker, double hz) {
        var stiffening = Stiffening(speaker);
        return speaker.Electrical.Impedance(hz, speaker.Fs * stiffening, stiffening) * WiringFactor;
    }

    /// <summary>The cabinet's nominal impedance with <paramref name="speaker" />, ohms.</summary>
    public double Nominal(SpeakerModel speaker) => speaker.Electrical.Nominal * WiringFactor;
}

/// <summary>
///     The DynaCab catalogue. Every curve here is a hand fit to a published chart, not a measurement and not a
///     copied IR: the low end from the datasheet's Fs (and Thiele-Small values where the maker publishes them; typical
///     values for the class otherwise, marked <i>typ.</i>), the breakup as peaks and dips read off the maker's
///     1 W / 1 m on-axis frequency-response chart, to about ±2 dB. Change a speaker by editing its line.
/// </summary>
public static class CabCatalogue {
    /// <summary>Radial mode constants: the zeros of J0, so mode n of a clamped disc has n - 1 nodal circles inside.</summary>
    public static ReadOnlySpan<double> RadialModes => [2.4048, 5.5201, 8.6537, 11.7915, 14.9309, 18.0711];

    // Electrical data (SpeakerElectrical), 8 Ω versions. Celestion publishes only Fs and Re for its guitar speakers
    // (product brochures and the Guitar Catalogue 2026 spec tables, celestion.com); their Qms, Le and Vas are typical
    // values for the class (marked typ.), Qes then following from the Qts the low end was fitted with. Jensen and
    // Electro-Voice publish full Thiele-Small sets; JBL never issued a D120F datasheet, so it takes the E120 (JBL's
    // later 12" musical-instrument driver of the same kind: light cone, aluminium dome, heavy magnet) from JBL
    // Professional's "Thiele/Small Low Frequency Driver Parameters" (1998), a stand-in, not a D120F measurement.
    public static readonly IReadOnlyList<SpeakerModel> Speakers = [
        // Celestion G12M Greenback (25 W), brochure "G12M Greenback": Fs 75 Hz, Re 6.7 Ω, 75 Hz-5 kHz, 98 dB. Chart:
        // steady rise to a broad 2 kHz presence hump, a dip near 3 kHz, a small 4 kHz peak, steep fall above 5 kHz.
        // Qts 0.95, Vas, Qms and Le typ. for a light-cone ceramic 12".
        new(
            "Celestion Greenback (G12M)",
            0.125,
            0.030,
            75,
            50,
            1_100,
            5_000,
            3,
            [new(800, 2.0, 0.7), new(2_000, 4.5, 1.6), new(3_100, -3.0, 3), new(4_000, 2.0, 3), new(6_500, -4, 2)],
            SpeakerElectrical.FromQts(8, 6.7, 0.95, SpeakerElectrical.TypicalCeramicQms, SpeakerElectrical.TypicalLe)
        ),
        // Celestion G12H Anniversary (30 W), brochure "G12H Anniversary": Fs 85 Hz, Re 6.7 Ω, 75 Hz-5 kHz, 100 dB.
        // Heavier magnet: tighter bass (lower Qts), brighter 2.5 kHz and 4.5 kHz peaks than the Greenback. Qts 0.55,
        // Vas, Qms and Le typ.
        new(
            "Celestion G12H30 Anniversary",
            0.125,
            0.030,
            85,
            40,
            1_300,
            5_500,
            3,
            [new(900, 1.5, 0.8), new(2_500, 5.0, 1.8), new(3_400, -2.5, 3), new(4_500, 3.0, 2.5), new(7_000, -3, 2)],
            SpeakerElectrical.FromQts(8, 6.7, 0.55, SpeakerElectrical.TypicalCeramicQms, SpeakerElectrical.TypicalLe)
        ),
        // Celestion G12T-75, brochure "G12T-75": Fs 85 Hz, Re 6.77 Ω, 70 Hz-5.5 kHz, 97 dB. Chart: scooped
        // 1-1.5 kHz, pronounced 3 kHz and 5 kHz peaks (the "sizzle"). Qts 0.60, Vas, Qms and Le typ.
        new(
            "Celestion G12T-75",
            0.125,
            0.032,
            85,
            45,
            1_400,
            6_000,
            3,
            [new(1_300, -3.0, 1.2), new(3_000, 4.0, 2.5), new(5_200, 5.0, 2.5), new(7_500, -4, 2)],
            SpeakerElectrical.FromQts(8, 6.77, 0.60, SpeakerElectrical.TypicalCeramicQms, SpeakerElectrical.TypicalLe)
        ),
        // Celestion Vintage 30, brochure "Vintage 30": Fs 75 Hz, Re 7.3 Ω, 70 Hz-5 kHz, 100 dB. Chart: the strong
        // upper-mid peak (about +6 dB near 2.5 kHz), a notch near 3.5 kHz, a second peak at 4.5 kHz, steep fall above
        // 5 kHz. Qts 0.50, Vas, Qms and Le typ.
        new(
            "Celestion Vintage 30",
            0.125,
            0.032,
            75,
            45,
            1_200,
            5_200,
            4,
            [new(1_100, 1.5, 1.0), new(2_600, 6.0, 2.0), new(3_500, -4.0, 3.5), new(4_600, 3.5, 3), new(6_000, -3, 2)],
            SpeakerElectrical.FromQts(8, 7.3, 0.50, SpeakerElectrical.TypicalCeramicQms, SpeakerElectrical.TypicalLe)
        ),
        // Celestion G12M-65 Creamback, product page "G12M-65 Creamback": Fs 75 Hz, Re 6.7 Ω, 75 Hz-5 kHz, 97 dB (the
        // 2026 catalogue table reads 6.1 Ω; the product page is taken). A Greenback voicing with more power handling:
        // tighter low end, the same 2 kHz hump, smoother top. Qts 0.70, Vas, Qms and Le typ.
        new(
            "Celestion Creamback (G12M-65)",
            0.125,
            0.030,
            75,
            45,
            1_150,
            5_000,
            3,
            [new(800, 1.5, 0.7), new(2_100, 4.0, 1.5), new(3_300, -2.5, 3), new(4_200, 1.5, 3), new(6_500, -4, 2)],
            SpeakerElectrical.FromQts(8, 6.7, 0.70, SpeakerElectrical.TypicalCeramicQms, SpeakerElectrical.TypicalLe)
        ),
        // Celestion Alnico Blue, brochure "Alnico Blue": Fs 75 Hz, Re 6.4 Ω, 75 Hz-5 kHz, 100 dB. Light cone, high Q:
        // loose bass, chiming 2.5 kHz and 4 kHz peaks, more top than the ceramics. Qts 1.30, Vas and Le typ.; Qms typ.
        // for an alnico paper-cone speaker (Jensen's alnico P10R: 15-24).
        new(
            "Celestion Alnico Blue",
            0.125,
            0.030,
            75,
            60,
            1_500,
            6_500,
            2,
            [new(1_000, 1.5, 1), new(2_500, 5.0, 2), new(4_000, 5.0, 2.5), new(5_500, -2, 3), new(8_000, -4, 2)],
            SpeakerElectrical.FromQts(8, 6.4, 1.30, SpeakerElectrical.TypicalAlnicoQms, SpeakerElectrical.TypicalLe)
        ),
        // Jensen P10R (alnico 10"), Jensen Vintage spec sheet "P10R", 8 Ω: Fs 97 Hz, Re 6.68 Ω, Qms 14.83, Qts 1.62,
        // Vas 27.4 l, Le 0.54 mH at 1 kHz. Its printed Qes (1.60) cannot sit below Qts; Qes here follows from Qms and
        // Qts (1.82). Small cone: less low end, early breakup at 1.5 kHz, a bright 3 kHz peak.
        new(
            "Jensen P10R",
            0.105,
            0.025,
            97,
            27.4,
            1_500,
            6_000,
            2,
            [new(1_500, 3.0, 1.5), new(3_000, 4.0, 2), new(4_200, -3, 3), new(5_500, 2, 3)],
            SpeakerElectrical.FromQts(8, 6.68, 1.62, 14.83, 0.54e-3)
        ),
        // Jensen C12N (ceramic 12"), Jensen Vintage spec "C12N", 8 Ω: Fs 113 Hz, Re 6.05 Ω, Qms 7.52, Qts 1.02
        // (Qes 1.18), Vas 22.6 l, Le 0.90 mH at 1 kHz. Fender's clean voice: flatter mids than the Celestions, 2 kHz
        // and 4 kHz peaks, an extended top.
        new(
            "Jensen C12N",
            0.125,
            0.030,
            113,
            22.6,
            1_400,
            7_000,
            2,
            [new(2_000, 3.0, 1.5), new(3_000, -2, 3), new(4_000, 4.0, 2.5), new(6_000, 2, 3)],
            SpeakerElectrical.FromQts(8, 6.05, 1.02, 7.52, 0.90e-3)
        ),
        // Electro-Voice EVM12L Classic, EV data sheet: Fs 55 Hz, Qms 4.37, Qes 0.245 (Qts 0.232), Vas 82.9 l; a flat
        // 100 Hz-5 kHz with a small 3 kHz rise: the clean, hi-fi option. EV gives no Re or Le: typ. for a heavy 8 Ω
        // 12".
        new(
            "Electro-Voice EVM12L",
            0.122,
            0.035,
            55,
            82.9,
            1_800,
            6_500,
            3,
            [new(3_000, 2.0, 1.5), new(4_500, -1.5, 3), new(5_500, 1.5, 3)],
            new(8, 5.6, 4.37, 0.245, SpeakerElectrical.TypicalLe)
        ),
        // JBL D120F: no datasheet was ever issued; the low end and load are the JBL E120's (JBL Professional,
        // "Thiele/Small Low Frequency Driver Parameters", 1998): Fs 60 Hz, Re 6.3 Ω, Qms 1.8, Qes 0.19 (Qts 0.17),
        // Vas 79.3 l, Le 0.4 mH. Heavy magnet and light cone, the aluminium dust cap adding a 6 kHz peak; response to
        // about 8-9 kHz.
        new(
            "JBL D120F",
            0.122,
            0.050,
            60,
            79.3,
            2_000,
            9_000,
            2,
            [new(1_500, 1.0, 1), new(3_000, 3.0, 2), new(6_000, 4.5, 2.5), new(7_500, -3, 3)],
            new(8, 6.3, 1.8, 0.19, 0.4e-3)
        )
    ];

    public static readonly IReadOnlyList<MicModel> Mics = [
        // Shure SM57 user guide, "Typical frequency response" (far field): falls below 200 Hz, presence rise to about
        // +5 dB at 5-6 kHz, a dip near 9 kHz, rolls off above 12 kHz. Cardioid.
        new(
            "Dynamic 57 (SM57)",
            0.5,
            0.009,
            140,
            13_500,
            2,
            [new(2_200, 1.5, 1), new(5_600, 5.0, 1.4), new(9_000, -3.5, 3)]
        ),
        // Sennheiser MD 421-II product sheet, bass switch at "M": 30 Hz-17 kHz, a gentle 4-5 kHz lift. Cardioid,
        // large diaphragm.
        new("Dynamic 421 (MD 421)", 0.5, 0.014, 80, 15_500, 2, [new(4_500, 3.0, 1.2), new(9_000, 2.0, 2)]),
        // Sennheiser e 906 product sheet, presence switch neutral: 40 Hz-18 kHz, presence peak at 4 kHz, air at
        // 8 kHz. Supercardioid.
        new("Dynamic 906 (e 906)", 0.37, 0.010, 110, 16_000, 2, [new(4_000, 4.0, 1.0), new(8_000, 2.0, 2)]),
        // Sennheiser e 609 Silver product sheet: 40 Hz-15 kHz, mid-forward with peaks at 3 and 6 kHz.
        // Supercardioid.
        new("Dynamic 609 (e 609)", 0.37, 0.010, 150, 14_000, 2, [new(3_000, 3.0, 1.0), new(6_000, 4.0, 1.5)]),
        // Shure Beta 57A user guide chart: 50 Hz-16 kHz, presence peak about +4 dB at 5 kHz. Supercardioid.
        new("Dynamic Beta 57 (Beta 57A)", 0.37, 0.009, 130, 15_000, 2, [new(2_500, 1.5, 1.5), new(5_000, 4.0, 1.2)]),
        // Royer R-121 specification sheet: 30 Hz-15 kHz ±3 dB, flat mids, gently falling top. Figure-8 ribbon.
        new("Ribbon 121 (R-121)", 0.0, 0.010, 35, 9_500, 2, [new(120, 1.0, 1.0), new(4_000, -1.0, 1.0)]),
        // Coles 4038 data sheet (BBC design): 30 Hz-15 kHz, a darker, earlier top roll-off than the R-121.
        // Figure-8 ribbon.
        new("Ribbon 4038 (Coles 4038)", 0.0, 0.012, 30, 7_000, 2, [new(2_500, -1.0, 1.0), new(10_000, -2, 1)]),
        // Beyerdynamic M 160 data sheet: 40 Hz-18 kHz, double ribbon, hypercardioid; smooth top, falling above
        // 10 kHz.
        new("Ribbon 160 (M 160)", 0.25, 0.008, 50, 11_000, 2, [new(4_000, 1.5, 1.2)]),
        // Neumann U 87 Ai operating manual, cardioid curve: flat 40 Hz-16 kHz with a broad lift of about 2-3 dB around
        // 10 kHz. Large diaphragm.
        new("Condenser 87 (U 87)", 0.5, 0.0135, 35, 18_000, 2, [new(4_000, -0.5, 1.5), new(10_000, 2.5, 1.0)]),
        // AKG C414 XLS manual, cardioid: flat 20 Hz-20 kHz within about 1.5 dB, a slight 12 kHz lift. Large
        // diaphragm.
        new("Condenser 414 (C414 XLS)", 0.5, 0.0127, 30, 20_000, 2, [new(12_000, 1.5, 2.0)]),
        // A measurement omni (Earthworks M30 class, flat 5 Hz-30 kHz): the reference with no proximity effect.
        new("Measurement Omni", 1.0, 0.003, 10, 30_000, 2, [])
    ];

    // Cabinets: typical dimensions of the classic designs (Marshall 1960, Fender Twin/Deluxe/Bassman/Princeton,
    // Vox AC30); box modes from c / 2L of the internal dimensions with modest gains (lined cabs damp them). Wired as
    // the originals: the combos and the 4x10 in parallel (a Twin's two 8 Ω on 4 Ω, a Super Reverb's four 8 Ω on
    // 2 Ω), the 1960 4x12 in series-parallel (four 16 Ω on 16 Ω).
    public static readonly IReadOnlyList<EnclosureModel> Enclosures = [
        new("1x12 Open Back", true, 0, 0.45, 0.95, 0.42, 0.035, [], [new(140, 1.5, 3)]),
        new("1x12 Closed Back", false, 45, 0, 0, 0.25, 0.035, [], [new(390, 1.0, 5), new(640, 1.0, 6)]),
        new("2x12 Open Back", true, 0, 0.50, 0.95, 0.42, 0.035, [(0.36, 0)], [new(120, 1.5, 3)]),
        new("2x12 Closed Back", false, 36, 0, 0, 0.28, 0.035, [(0.34, 0)], [new(250, 1.0, 5), new(560, 1.0, 6)]),
        new(
            "4x10 Open Back",
            true,
            0,
            0.55,
            0.95,
            0.62,
            0.032,
            [(0.28, 0), (0, -0.28), (0.28, -0.28)],
            [new(110, 1.5, 3)]
        ),
        new(
            "4x12 Closed Back",
            false,
            36,
            0,
            0,
            0.56,
            0.035,
            [(0.34, 0), (0, -0.34), (0.34, -0.34)],
            [new(245, 1.5, 6), new(520, 1.0, 6)],
            2
        ),
        new("1x10 Open Back", true, 0, 0.40, 0.95, 0.32, 0.030, [], [new(160, 1.5, 3)])
    ];
}
