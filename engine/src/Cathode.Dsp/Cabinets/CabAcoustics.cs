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
public readonly record struct MicPlacement(
    SpeakerModel Speaker,
    EnclosureModel Enclosure,
    MicModel Mic,
    double Position,
    double Distance,
    double Angle,
    bool Proximity,
    double Room
);

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
    readonly double lowCornerHz;
    readonly double lowQ;
    readonly double tilt;

    /// <summary>Time from the cone to the mic, in seconds.</summary>
    public double DirectDelay => depth / SpeedOfSound;

    /// <summary>The closed box's (or free-air) system resonance, Hz.</summary>
    public double SystemResonance => lowCornerHz;

    public CabAcoustics(in MicPlacement placement) {
        this.placement = placement;
        radius = placement.Speaker.ConeRadius;
        lateral = Math.Clamp(placement.Position, 0, 1) * radius;
        depth = Math.Max(0, placement.Distance) + placement.Enclosure.Recess;
        nearField = 1 / (1 + depth * depth / (radius * radius));
        curvature = Math.Sqrt(depth * depth + Curvature * radius * Curvature * radius);
        tilt = Math.Clamp(placement.Angle, 0, Math.PI / 2);
        var speaker = placement.Speaker;
        if (placement.Enclosure.Open) {
            lowCornerHz = speaker.Fs;
            lowQ = speaker.Qts;
        } else {
            var stiffening = Math.Sqrt(1 + speaker.Vas / placement.Enclosure.LitresPerSpeaker);
            lowCornerHz = speaker.Fs * stiffening;
            lowQ = speaker.Qts * stiffening;
        }
    }

    /// <summary>The magnitude part, in dB at <paramref name="hz" />.</summary>
    public double MagnitudeDb(double hz) {
        var speaker = placement.Speaker;
        var mic = placement.Mic;
        var db = HighPassDb(hz, lowCornerHz, lowQ);
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
            sum -= enclosure.BackWave * shading * (curvature / rho) * Pattern(omega, rho, cos)
                * Complex.FromPolarCoordinates(1, -omega * enclosure.DipolePath / SpeedOfSound);
        }

        if (placement.Room > 0) {
            var room = Math.Clamp(placement.Room, 0, 1);
            var height = enclosure.Height;

            // Floor: the image speaker 2h below, reflected at 0.8, softened above 6 kHz.
            sum += room * 0.8 * Absorb(hz, 6_000) * Source(omega, k, effective, 2 * height, depth);

            // Ceiling, side wall and the wall behind the mic (arriving from behind: a cardioid rejects it, a
            // figure-8 hears it inverted).
            sum += room * 0.6 * Absorb(hz, 4_000)
                * Source(omega, k, effective, 2 * (CeilingHeight - height), depth);
            sum += room * 0.6 * Absorb(hz, 4_000) * Source(omega, k, effective, 2 * SideWallDistance, depth);
            var back = 2 * BackWallDistance - depth;
            var backRho = Math.Sqrt(back * back + Square(Curvature * radius));
            sum += room * 0.5 * Absorb(hz, 3_000) * (curvature / backRho) * Pattern(omega, backRho, -1)
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

    static double Square(double x) => x * x;

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

        // 1 + c / (j ω ρ), held finite below the capsule's floor.
        var boost = 1 + SpeedOfSound / (new Complex(2 * Math.PI * ProximityFloorHz, omega) * rho);
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
        var level = curvature / rho * PistonGain(k * effective * sine)
            * PistonGain(k * placement.Mic.DiaphragmRadius * sine);
        return level * Pattern(omega, rho, cos)
            * Complex.FromPolarCoordinates(1, -omega * (distance - depth) / SpeedOfSound);
    }
}
