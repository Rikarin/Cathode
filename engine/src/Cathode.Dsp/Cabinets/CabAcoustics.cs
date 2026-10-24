using System.Numerics;

namespace Cathode.Dsp.Cabinets;

/// <summary>Where a mic is and what it is: the inputs to <see cref="CabAcoustics" />.</summary>
/// <param name="Speaker">The miked speaker.</param>
/// <param name="Enclosure">The cabinet it is in.</param>
/// <param name="Mic">The microphone.</param>
/// <param name="Position">Cap (0) to the cone's edge (1).</param>
/// <param name="Distance">From the grille cloth, in metres.</param>
/// <param name="Angle">Off-axis tilt of the mic, in radians.</param>
/// <param name="Proximity">Model the proximity effect of pressure-gradient mics.</param>
/// <param name="Room">Floor and room reflections, 0 (anechoic) to 1.</param>
/// <param name="ProximityAmount">Scales the proximity boost: 1 is the physical amount, 0 none, 2 twice it.</param>
/// <param name="ProximityFrequency">
///     Scales the proximity corner (<c>c / 2πρ</c>): above 1 the boost reaches higher, as if the mic were closer.
/// </param>
/// <param name="RoomShape">The room's geometry and surfaces; null for <see cref="CabRoom.Default" />.</param>
public readonly record struct MicPlacement(
    SpeakerModel Speaker,
    EnclosureModel Enclosure,
    MicModel Mic,
    double Position,
    double Distance,
    double Angle,
    bool Proximity,
    double Room,
    double ProximityAmount = 1,
    double ProximityFrequency = 1,
    CabRoom? RoomShape = null
);

/// <summary>The room around the cab, for the reflections <see cref="CabAcoustics.Paths" /> adds.</summary>
/// <param name="Size">Scales every wall's (and the ceiling's) distance: 1 is a small studio room.</param>
/// <param name="Shape">−1 … 1: the side wall moves out by 2^Shape and the back wall in by as much (0 as stock).</param>
/// <param name="Floor">The floor reflection's level relative to stock (1).</param>
/// <param name="HighDamping">Divides the surfaces' high-frequency absorption corners (1 stock, 4 a dull room).</param>
/// <param name="LowDamping">First-order high-pass on the reflections, Hz (0 none): bass traps.</param>
/// <param name="Diffusion">
///     How rough the walls are, as the spread (standard deviation) of a wall reflection's arrival time in seconds: its
///     coherent share falls as <c>exp(−(ω σ)² / 2)</c> and the rest adds as power, so the comb it makes with the direct
///     sound evens out from the treble down (0 a specular room).
/// </param>
/// <param name="MicOffset">
///     Where the mic hears the room from, in metres sideways from the close mic: one of a stereo pair of room mics,
///     −spacing/2 on the left and +spacing/2 on the right (positive away from the side wall). The direct sound stays
///     the close mic's; every reflection's image is that much further off its axis, so the two sides' room sounds
///     arrive at different times and decorrelate. 0 (the default) is the mono room.
/// </param>
public readonly record struct CabRoom(
    double Size = 1,
    double Shape = 0,
    double Floor = 1,
    double HighDamping = 1,
    double LowDamping = 0,
    double Diffusion = 0,
    double MicOffset = 0
) {
    public static CabRoom Default { get; } = new(1);
}

/// <summary>
///     The physics of one miked speaker, evaluated per frequency. The response splits in two:
///     <list type="bullet">
///         <item>
///             <see cref="MagnitudeDb" />: what has no meaningful phase of its own (speaker, box resonance, mic
///             capsule, the near-field position loss), made minimum phase by the synthesiser;
///         </item>
///         <item>
///             <see cref="Paths" />: what is physically phased: the direct sound with the mic's proximity effect,
///             the other speakers in the cab, an open back's back wave, and floor and room reflections, each with its
///             own path delay relative to the direct sound.
///         </item>
///     </list>
/// </summary>
/// <remarks>
///     <para>
///         Position: above <see cref="SpeakerModel.DecoupleHz" /> the cone stops moving as a piston and the radiating
///         area shrinks towards the voice coil and dust cap (Kinsler et al., Fundamentals of Acoustics, §8;
///         Beranek, Acoustics, ch. 4). A mic off-centre is then laterally off that shrinking radiator: it is further
///         away and off its axis, so it gets the spreading loss and the piston directivity
///         <c>2 J1(x) / x</c>, x = k a sin θ (approximated by its smooth envelope, as a real cone has no deep nulls).
///         Breakup peaks are radial modes: the n-th is weighted near the cone by <c>|J0(j0n r / a)|</c>, so moving
///         across a nodal circle smooths that peak away. Both effects fade with distance, as a far mic hears the
///         whole cone.
///     </para>
///     <para>
///         Proximity: a first-order mic <c>A + B cos θ</c> near a spherical source of radius of curvature ρ responds
///         as <c>A + B cos θ (1 + 1 / (j k ρ))</c> (Olson, Acoustical Engineering, §8.6). The cone is a large source,
///         so its wavefront curvature near the grille is <c>ρ = sqrt(z² + (a/2)²)</c>, not z: the bass boost stays
///         finite against the grille, as measured proximity curves do. An omni (B = 0) has none.
///     </para>
///     <para>
///         Enclosure: closed boxes raise the resonance to <c>fc = Fs sqrt(1 + Vas / Vb)</c> with
///         <c>Qtc = Qts sqrt(1 + Vas / Vb)</c> (Small, JAES 1972). Open backs leave the free-air resonance and add the
///         back wave, inverted and delayed by the path round the cabinet and shaded by the baffle above about
///         <c>c / (π D)</c>: the dipole's low-end cancellation and comb.
///     </para>
/// </remarks>
public readonly struct CabAcoustics {
    /// <summary>Speed of sound at 20 °C, m/s.</summary>
    public const double SpeedOfSound = 343;

    /// <summary>Wavefront curvature of the cone near the grille, as a fraction of its radius.</summary>
    const double Curvature = 0.5;

    /// <summary>Proximity is regularised below this (the capsule's own mechanical limit), Hz.</summary>
    const double ProximityFloorHz = 12;

    const double BackWallDistance = 2.5;
    const double SideWallDistance = 1.2;
    const double CeilingHeight = 2.5;

    readonly MicPlacement placement;
    readonly double radius;
    readonly double lateral;
    readonly double depth;
    readonly double nearField;
    readonly double curvature;
    readonly double lowQ;
    readonly double tilt;
    readonly CabRoom room;

    /// <summary>Time from the cone to the mic, in seconds.</summary>
    public double DirectDelay => depth / SpeedOfSound;

    /// <summary>The closed box's (or free-air) system resonance, Hz.</summary>
    public double SystemResonance { get; }

    public CabAcoustics(in MicPlacement placement) {
        this.placement = placement;
        radius = placement.Speaker.ConeRadius;
        lateral = Math.Clamp(placement.Position, 0, 1) * radius;
        depth = Math.Max(0, placement.Distance) + placement.Enclosure.Recess;
        nearField = 1 / (1 + depth * depth / (radius * radius));
        curvature = Math.Sqrt(depth * depth + Curvature * radius * Curvature * radius);
        tilt = Math.Clamp(placement.Angle, 0, Math.PI / 2);
        room = placement.RoomShape ?? CabRoom.Default;
        var speaker = placement.Speaker;
        var stiffening = placement.Enclosure.Stiffening(speaker);
        SystemResonance = speaker.Fs * stiffening;
        lowQ = speaker.Qts * stiffening;
    }

    /// <summary>The magnitude part, in dB at <paramref name="hz" />.</summary>
    public double MagnitudeDb(double hz) {
        var speaker = placement.Speaker;
        var mic = placement.Mic;
        var db = HighPassDb(hz, SystemResonance, lowQ);
        for (var i = 0; i < speaker.Breakup.Length; i++) {
            var peak = speaker.Breakup[i];
            var mode = CabCatalogue.RadialModes[Math.Min(i, CabCatalogue.RadialModes.Length - 1)];
            var weight = 1 - nearField * (1 - Math.Abs(BesselJ0(mode * placement.Position)));
            db += PeakDb(hz, peak with { Db = peak.Db * weight });
        }

        db += LowPassDb(hz, speaker.HighCutHz, speaker.HighCutOrder);
        foreach (var mode in placement.Enclosure.Modes) {
            db += PeakDb(hz, mode);
        }

        db += HighPassDb(hz, mic.LowCutHz, 0.7071);
        foreach (var peak in mic.Peaks) {
            db += PeakDb(hz, peak);
        }

        db += LowPassDb(hz, mic.HighCutHz, mic.HighCutOrder);

        // Position: the mic is off the shrinking high-frequency radiator by `offset`.
        var k = 2 * Math.PI * hz / SpeedOfSound;
        var effective = EffectiveRadius(hz);
        var offset = Math.Max(0, lateral - effective);
        var reach = Math.Sqrt(offset * offset + depth * depth);
        var sine = offset / reach;
        db += 20 * Math.Log10(depth / reach) + PistonDb(k * effective * sine);

        // The capsule's own off-axis treble loss at the mic's tilt.
        db += PistonDb(k * mic.DiaphragmRadius * Math.Sin(tilt));

        // The other speakers' incoherent share adds as power (see Coherence).
        if (placement.Enclosure.Others.Length > 0) {
            var coherence = Coherence(hz);
            var incoherent = 1 - coherence * coherence;
            var power = 0.0;
            foreach (var (x, y) in placement.Enclosure.Others) {
                var side = Math.Sqrt((x + lateral) * (x + lateral) + y * y);
                var magnitude = Source(2 * Math.PI * hz, k, effective, side, depth).Magnitude;
                power += magnitude * magnitude;
            }

            db += 10 * Math.Log10(1 + incoherent * power);
        }

        // Diffuse walls: what their reflections lose in coherence (see Paths) adds back as power.
        if (placement.Room > 0 && room.Diffusion > 0) {
            var omega = 2 * Math.PI * hz;
            var share = Coherence(omega, room.Diffusion);
            var diffuse = (1 - share * share) * WallPower(hz, omega, k, effective);
            db += 10 * Math.Log10(1 + diffuse);
        }

        return db;
    }

    /// <summary>The physically phased part at <paramref name="hz" />, relative to the direct sound's arrival.</summary>
    public Complex Paths(double hz) {
        var omega = 2 * Math.PI * hz;
        var k = omega / SpeedOfSound;
        var enclosure = placement.Enclosure;
        var effective = EffectiveRadius(hz);
        var sum = Pattern(omega, curvature, Math.Cos(tilt));

        // The other speakers: further away and off their axes; the mic is moved outwards (−x) from the cap. Only
        // their coherent share adds here, with its delay.
        var coherence = Coherence(hz);
        foreach (var (x, y) in enclosure.Others) {
            var side = Math.Sqrt((x + lateral) * (x + lateral) + y * y);
            sum += coherence * Source(omega, k, effective, side, depth);
        }

        if (enclosure.Open) {
            // The back wave comes round the cabinet's edge (half the dipole path to the side), inverted.
            var path = depth + enclosure.DipolePath;
            var shading = 1 / Math.Sqrt(1 + Square(hz * Math.PI * enclosure.DipolePath / SpeedOfSound));
            var rho = Math.Sqrt(path * path + Square(Curvature * radius));
            var cos = depth / Math.Sqrt(depth * depth + Square(enclosure.DipolePath / 2));
            sum -= enclosure.BackWave
                * shading
                * (curvature / rho)
                * Pattern(omega, rho, cos)
                * Complex.FromPolarCoordinates(1, -omega * enclosure.DipolePath / SpeedOfSound);
        }

        if (placement.Room > 0) {
            var (ceiling, sideWall, backWall, damping, level) = Walls();
            var height = enclosure.Height;
            var lowCut = room.LowDamping > 0 ? HighPass(hz, room.LowDamping) : Complex.One;
            var walls = level * lowCut * Coherence(omega, room.Diffusion);

            // Floor: the image speaker 2h below, reflected at 0.8, softened above 6 kHz. Only the walls diffuse.
            sum += level
                * lowCut
                * room.Floor
                * 0.8
                * Absorb(hz, 6_000 / damping)
                * Source(omega, k, effective, Offset(2 * height), depth);

            // Ceiling, side wall and the wall behind the mic (arriving from behind: a cardioid rejects it, a
            // figure-8 hears it inverted).
            sum += walls
                * 0.6
                * Absorb(hz, 4_000 / damping)
                * Source(omega, k, effective, Offset(2 * (ceiling - height)), depth);
            sum += walls
                * 0.6
                * Absorb(hz, 4_000 / damping)
                * Source(omega, k, effective, SideImage(sideWall), depth);
            var back = 2 * backWall - depth;
            var backRho = BackRho(back);
            sum += walls
                * 0.5
                * Absorb(hz, 3_000 / damping)
                * (curvature / backRho)
                * Pattern(omega, backRho, -1)
                * Complex.FromPolarCoordinates(1, -omega * (back - depth) / SpeedOfSound);
        }

        return sum;
    }

    /// <summary>J0 by its power series (exact enough for the arguments here, below 20).</summary>
    public static double BesselJ0(double x) {
        var term = 1.0;
        var sum = 1.0;
        var q = x * x / 4;
        for (var n = 1; n < 60; n++) {
            term *= -q / (n * n);
            sum += term;
            if (Math.Abs(term) < 1e-12) {
                break;
            }
        }

        return sum;
    }

    /// <summary>Second-order high-pass magnitude in dB.</summary>
    public static double HighPassDb(double hz, double corner, double q) {
        var x = hz / corner;
        var x2 = x * x;
        return 10 * Math.Log10(x2 * x2 / (Square(1 - x2) + x2 / (q * q)) + 1e-30);
    }

    /// <summary>Butterworth-like low-pass magnitude in dB, <paramref name="order" /> × 6 dB/octave.</summary>
    public static double LowPassDb(double hz, double corner, int order) =>
        -10 * Math.Log10(1 + Math.Pow(hz / corner, 2 * order));

    /// <summary>Peaking-filter magnitude in dB (the analogue prototype of the RBJ peaking EQ).</summary>
    public static double PeakDb(double hz, Resonance peak) {
        if (peak.Db == 0) {
            return 0;
        }

        var a = Math.Pow(10, peak.Db / 40);
        var x = hz / peak.Hz;
        var real = 1 - x * x;
        var numerator = real * real + Square(x * a / peak.Q);
        var denominator = real * real + Square(x / (a * peak.Q));
        return 10 * Math.Log10(numerator / denominator);
    }

    /// <summary>The smooth envelope of a rigid piston's directivity <c>2 J1(x) / x</c>, in dB: −3 dB at x = 1.62.</summary>
    public static double PistonDb(double x) => 20 * Math.Log10(PistonGain(x));

    /// <summary><see cref="PistonDb" /> as a gain.</summary>
    public static double PistonGain(double x) {
        var u = Math.Abs(x) / 1.62;
        return 1 / Math.Sqrt(1 + u * u * u);
    }

    /// <summary>The power the ceiling, side and back walls reflect at <paramref name="hz" />, before diffusion.</summary>
    double WallPower(double hz, double omega, double k, double effective) {
        var (ceiling, sideWall, backWall, damping, level) = Walls();
        var lowCut = room.LowDamping > 0 ? HighPass(hz, room.LowDamping).Magnitude : 1;
        var height = placement.Enclosure.Height;
        var back = 2 * backWall - depth;
        var backRho = BackRho(back);
        var a = 0.6
            * Absorb(hz, 4_000 / damping)
            * Source(omega, k, effective, Offset(2 * (ceiling - height)), depth).Magnitude;
        var b = 0.6 * Absorb(hz, 4_000 / damping) * Source(omega, k, effective, SideImage(sideWall), depth).Magnitude;
        var c = 0.5 * Absorb(hz, 3_000 / damping) * (curvature / backRho) * Pattern(omega, backRho, -1).Magnitude;
        return Square(level * lowCut) * (a * a + b * b + c * c);
    }

    /// <summary>The room's ceiling height, side and back wall distances, HF damping and level.</summary>
    (double Ceiling, double SideWall, double BackWall, double Damping, double Level) Walls() {
        var size = Math.Max(0.1, room.Size);
        var aspect = Math.Pow(2, Math.Clamp(room.Shape, -1, 1));
        return (
            Math.Max(CeilingHeight * size, placement.Enclosure.Height + 0.1),
            SideWallDistance * size * aspect,
            Math.Max(BackWallDistance * size / aspect, depth + 0.05),
            Math.Max(0.05, room.HighDamping),
            Math.Clamp(placement.Room, 0, 1)
        );
    }

    static double Square(double x) => x * x;

    /// <summary>
    ///     An image <paramref name="side" /> off the mic's axis (above or below it), seen from the room mic
    ///     (<see cref="CabRoom.MicOffset" /> sideways); exactly <paramref name="side" /> for the mono room.
    /// </summary>
    double Offset(double side) => room.MicOffset == 0 ? side : Math.Sqrt(side * side + Square(room.MicOffset));

    /// <summary>The side wall's image, 2 × the wall's distance across, plus the room mic's own offset away from it.</summary>
    double SideImage(double sideWall) =>
        room.MicOffset == 0 ? 2 * sideWall : Math.Max(0.01, 2 * sideWall + room.MicOffset);

    /// <summary>The back wall image's wavefront radius as the room mic sees it.</summary>
    double BackRho(double back) =>
        Math.Sqrt(back * back + Square(Curvature * radius) + (room.MicOffset == 0 ? 0 : Square(room.MicOffset)));

    /// <summary>A first-order high-pass <c>j f / (fc + j f)</c>.</summary>
    static Complex HighPass(double hz, double corner) => new Complex(0, hz) / new Complex(corner, hz);

    /// <summary>The coherent share of a reflection whose arrival is spread by <paramref name="spread" /> seconds.</summary>
    static double Coherence(double omega, double spread) => spread <= 0 ? 1 : Math.Exp(-0.5 * Square(omega * spread));

    /// <summary>First-order HF absorption of a reflecting surface.</summary>
    static double Absorb(double hz, double corner) => 1 / Math.Sqrt(1 + Square(hz / corner));

    /// <summary>
    ///     How coherently the other speakers add to the miked one. Below breakup the cones move alike and sum with
    ///     their path delays (the multi-speaker comb); above it each cone breaks up its own way (no two are identical),
    ///     so their contributions decorrelate and add as power. Falls to 0.7 at half the decoupling frequency.
    /// </summary>
    double Coherence(double hz) => 1 / Math.Sqrt(1 + Square(2 * hz / placement.Speaker.DecoupleHz));

    double EffectiveRadius(double hz) => radius / Math.Sqrt(1 + Square(hz / placement.Speaker.DecoupleHz));

    /// <summary>The mic's pattern with proximity, for sound arriving at <paramref name="cos" /> off its axis.</summary>
    Complex Pattern(double omega, double rho, double cos) {
        var mic = placement.Mic;
        var gradient = 1 - mic.Omni;
        if (!placement.Proximity || gradient == 0) {
            return mic.Omni + gradient * cos;
        }

        // 1 + c / (j ω ρ), held finite below the capsule's floor; Proximity Freq scales the corner c / 2πρ.
        var corner = SpeedOfSound * placement.ProximityFrequency;
        var boost = 1 + placement.ProximityAmount * corner / (new Complex(2 * Math.PI * ProximityFloorHz, omega) * rho);
        return mic.Omni + gradient * cos * boost;
    }

    /// <summary>
    ///     A speaker (or its image) <paramref name="side" /> metres to the side of the mic and <paramref name="front" />
    ///     in front of it: spread relative to the direct sound, its directivity towards the mic, the mic's pattern
    ///     and the extra path delay.
    /// </summary>
    Complex Source(double omega, double k, double effective, double side, double front) {
        var distance = Math.Sqrt(side * side + front * front);
        var rho = Math.Sqrt(distance * distance + Square(Curvature * radius));
        var sine = side / distance;
        var cos = front / distance;
        var level = curvature
            / rho
            * PistonGain(k * effective * sine)
            * PistonGain(k * placement.Mic.DiaphragmRadius * sine);
        return level
            * Pattern(omega, rho, cos)
            * Complex.FromPolarCoordinates(1, -omega * (distance - depth) / SpeedOfSound);
    }
}
