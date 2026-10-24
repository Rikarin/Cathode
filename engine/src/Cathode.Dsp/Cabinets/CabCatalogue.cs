namespace Cathode.Dsp.Cabinets;

/// <summary>A resonance (or notch, with a negative gain) as a peaking-filter magnitude: centre, gain and Q.</summary>
public readonly record struct Resonance(double Hz, double Db, double Q);

/// <summary>
///     A guitar loudspeaker as compact parametric data. The low end comes from the Thiele-Small values (the enclosure
///     turns them into the system resonance), the piston band and cone breakup from <see cref="Breakup" />, the top end
///     from the high cut. Geometry sets how the response changes with mic position.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="ConeRadius">Effective radiating radius in metres (from Sd where published: a = sqrt(Sd / pi)).</param>
/// <param name="CapRadius">Dust cap (or dome) radius in metres.</param>
/// <param name="Fs">Free-air resonance in Hz, from the datasheet.</param>
/// <param name="Qts">Total Q at Fs.</param>
/// <param name="Vas">Equivalent compliance volume in litres.</param>
/// <param name="DecoupleHz">
///     Above this the cone stops moving as one piece and the radiating area shrinks towards the voice coil and the
///     dust cap (a_eff = a / sqrt(1 + (f / DecoupleHz)^2)): sets how quickly the sound darkens off-centre.
/// </param>
/// <param name="HighCutHz">Where the datasheet chart falls away at the top.</param>
/// <param name="HighCutOrder">Slope of that fall, in 6 dB/octave steps.</param>
/// <param name="Breakup">Cone breakup peaks and dips, lowest first: the n-th is weighted by the n-th radial mode.</param>
public sealed record SpeakerModel(
    string Name,
    double ConeRadius,
    double CapRadius,
    double Fs,
    double Qts,
    double Vas,
    double DecoupleHz,
    double HighCutHz,
    int HighCutOrder,
    Resonance[] Breakup
);

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
public sealed record EnclosureModel(
    string Name,
    bool Open,
    double LitresPerSpeaker,
    double DipolePath,
    double BackWave,
    double Height,
    double Recess,
    (double X, double Y)[] Others,
    Resonance[] Modes
);

/// <summary>
///     The DynaCab catalogue. Every curve here is a hand fit to a published chart, not a measurement and not a
///     copied IR: the low end from the datasheet's Fs (and Thiele-Small values where the maker publishes them; typical
///     values for the class otherwise, marked <i>typ.</i>), the breakup as peaks and dips read off the maker's
///     1 W / 1 m on-axis frequency-response chart, to about ±2 dB. Change a speaker by editing its line.
/// </summary>
public static class CabCatalogue {
    /// <summary>Radial mode constants: the zeros of J0, so mode n of a clamped disc has n - 1 nodal circles inside.</summary>
    public static readonly double[] RadialModes = [2.4048, 5.5201, 8.6537, 11.7915, 14.9309, 18.0711];

    public static readonly IReadOnlyList<SpeakerModel> Speakers = [
        // Celestion G12M Greenback (25 W), datasheet "G12M Greenback": Fs 75 Hz, 75 Hz-5 kHz, 98 dB. Chart: steady
        // rise to a broad 2 kHz presence hump, a dip near 3 kHz, a small 4 kHz peak, steep fall above 5 kHz.
        // Qts/Vas typ. for a light-cone ceramic 12".
        new(
            "Celestion Greenback (G12M)",
            0.125,
            0.030,
            75,
            0.95,
            50,
            1_100,
            5_000,
            3,
            [new(800, 2.0, 0.7), new(2_000, 4.5, 1.6), new(3_100, -3.0, 3), new(4_000, 2.0, 3), new(6_500, -4, 2)]
        ),
        // Celestion G12H Anniversary (30 W), datasheet "G12H Anniversary": Fs about 85 Hz, 75 Hz-5 kHz, 100 dB. Heavier
        // magnet: tighter bass (lower Qts), brighter 2.5 kHz and 4.5 kHz peaks than the Greenback. Qts/Vas typ.
        new(
            "Celestion G12H30 Anniversary",
            0.125,
            0.030,
            85,
            0.55,
            40,
            1_300,
            5_500,
            3,
            [new(900, 1.5, 0.8), new(2_500, 5.0, 1.8), new(3_400, -2.5, 3), new(4_500, 3.0, 2.5), new(7_000, -3, 2)]
        ),
        // Celestion G12T-75, datasheet "G12T-75": Fs 80 Hz, 70 Hz-5.5 kHz, 97 dB. Chart: scooped 1-1.5 kHz,
        // pronounced 3 kHz and 5 kHz peaks (the "sizzle"). Qts/Vas typ.
        new(
            "Celestion G12T-75",
            0.125,
            0.032,
            80,
            0.60,
            45,
            1_400,
            6_000,
            3,
            [new(1_300, -3.0, 1.2), new(3_000, 4.0, 2.5), new(5_200, 5.0, 2.5), new(7_500, -4, 2)]
        ),
        // Celestion Vintage 30, datasheet "Vintage 30": Fs 75 Hz, 70 Hz-5 kHz, 100 dB. Chart: the strong upper-mid
        // peak (about +6 dB near 2.5 kHz), a notch near 3.5 kHz, a second peak at 4.5 kHz, steep fall above 5 kHz.
        // Qts/Vas typ.
        new(
            "Celestion Vintage 30",
            0.125,
            0.032,
            75,
            0.50,
            45,
            1_200,
            5_200,
            4,
            [new(1_100, 1.5, 1.0), new(2_600, 6.0, 2.0), new(3_500, -4.0, 3.5), new(4_600, 3.5, 3), new(6_000, -3, 2)]
        ),
        // Celestion G12M-65 Creamback, datasheet "G12M-65 Creamback": Fs 85 Hz, 75 Hz-5 kHz, 97 dB. A Greenback
        // voicing with more power handling: tighter low end, the same 2 kHz hump, smoother top. Qts/Vas typ.
        new(
            "Celestion Creamback (G12M-65)",
            0.125,
            0.030,
            85,
            0.70,
            45,
            1_150,
            5_000,
            3,
            [new(800, 1.5, 0.7), new(2_100, 4.0, 1.5), new(3_300, -2.5, 3), new(4_200, 1.5, 3), new(6_500, -4, 2)]
        ),
        // Celestion Alnico Blue, datasheet "Alnico Blue": Fs 75 Hz, 75 Hz-5 kHz, 100 dB. Light cone, high Q: loose
        // bass, chiming 2.5 kHz and 4 kHz peaks, more top than the ceramics. Qts typ. (alnico, light cone).
        new(
            "Celestion Alnico Blue",
            0.125,
            0.030,
            75,
            1.30,
            60,
            1_500,
            6_500,
            2,
            [new(1_000, 1.5, 1), new(2_500, 5.0, 2), new(4_000, 5.0, 2.5), new(5_500, -2, 3), new(8_000, -4, 2)]
        ),
        // Jensen P10R (alnico 10"), Jensen Vintage datasheet: Fs about 90 Hz, 80 Hz-6 kHz. Small cone: less low end,
        // early breakup at 1.5 kHz, a bright 3 kHz peak. Qts/Vas typ. for a vintage 10".
        new(
            "Jensen P10R",
            0.105,
            0.025,
            90,
            1.20,
            25,
            1_500,
            6_000,
            2,
            [new(1_500, 3.0, 1.5), new(3_000, 4.0, 2), new(4_200, -3, 3), new(5_500, 2, 3)]
        ),
        // Jensen C12N (ceramic 12"), Jensen Vintage datasheet: Fs about 70 Hz, 60 Hz-6 kHz. Fender's clean voice:
        // flatter mids than the Celestions, 2 kHz and 4 kHz peaks, an extended top. Qts/Vas typ.
        new(
            "Jensen C12N",
            0.125,
            0.030,
            70,
            0.90,
            55,
            1_400,
            7_000,
            2,
            [new(2_000, 3.0, 1.5), new(3_000, -2, 3), new(4_000, 4.0, 2.5), new(6_000, 2, 3)]
        ),
        // Electro-Voice EVM12L, EV datasheet: Fs about 55 Hz, a flat 100 Hz-5 kHz with a small 3 kHz rise: the clean,
        // hi-fi option. Qts/Vas typ. for its heavy magnet.
        new(
            "Electro-Voice EVM12L",
            0.122,
            0.035,
            55,
            0.40,
            70,
            1_800,
            6_500,
            3,
            [new(3_000, 2.0, 1.5), new(4_500, -1.5, 3), new(5_500, 1.5, 3)]
        ),
        // JBL D120F, JBL datasheet: Fs about 60 Hz, very low Qts (a heavy magnet and light cone), the aluminium
        // dust cap adding a 6 kHz peak; response to about 8-9 kHz. Qts/Vas typ.
        new(
            "JBL D120F",
            0.122,
            0.050,
            60,
            0.30,
            70,
            2_000,
            9_000,
            2,
            [new(1_500, 1.0, 1), new(3_000, 3.0, 2), new(6_000, 4.5, 2.5), new(7_500, -3, 3)]
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
    // Vox AC30); box modes from c / 2L of the internal dimensions with modest gains (lined cabs damp them).
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
            [new(245, 1.5, 6), new(520, 1.0, 6)]
        ),
        new("1x10 Open Back", true, 0, 0.40, 0.95, 0.32, 0.030, [], [new(160, 1.5, 3)])
    ];
}
