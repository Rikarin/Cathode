using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A complete amp as a configuration of the stage library: the preamp stages in signal order, then the power amp.
///     <paramref name="OutputTrimDb" /> evens out the models' loudness at the block's output.
/// </summary>
public sealed record AmpDesign(
    string Name,
    IReadOnlyList<AmpStageDesign> Preamp,
    PowerAmpDesign Power,
    double OutputTrimDb
) {
    /// <summary>
    ///     Fender blackface (AB763 Super Reverb-style channel): V1 into the Fender stack and the volume, V2, then the
    ///     mixing network (a small coupling cap and the 220k mixing resistor into the other channel's 220k) and the
    ///     (added) master, a 12AT7 long-tail pair, two 6L6GC at 460 V with GZ34-like sag, feedback from the 4 Ω tap with
    ///     a presence control. The original's V4B mixer and reverb recovery are left out: the mixing network's loss
    ///     stands in for the mixer's net gain into the inverter.
    /// </summary>
    public static AmpDesign Blackface { get; } = new(
        "Blackface",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                Supply: 270,
                CathodeResistor: 1.5e3,
                Stack: ToneStackDesign.Fender,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Gain,
                PlateResistor: 100e3,
                CathodeBypass: 25e-6,
                InputResistance: 68e3,
                GridLeak: 1e6,
                BrightCap: 120e-12
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 270, 100e3, 1.5e3, CathodeBypass: 25e-6),
            new CouplingStageDesign("Mixer", 4.7e-9, new(1e6, PotTaper.Audio), PotRole.Master, 220e3, 220e3)
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 40,
            PhaseInverter: new(TriodeModel.Ecc81, 420, 82e3, 100e3, 22e3, 1e3),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1 / Math.Sqrt(2), 820, 100, new(1e3, PotTaper.Linear), 0.47e-6, 100),
            HasFeedback: true,
            Transformer: new(4.2e3),
            Speaker: new(Resonance: 100, PeakImpedance: 35, Q: 2.5),
            PowerSupply: new(120, 70e-6, 100, 20e-6, 10e3, 16e-6)
        ),
        -3
    );

    /// <summary>
    ///     Marshall 1959-style (a 50 W "1987" with the channels jumpered down to one path): V1A, the volume with its
    ///     bright cap and the 470k mixing resistor (bypassed by 470 pF), V2A with its 0.68 µF partial bypass, the V2B
    ///     cathode follower DC-coupled to V2A and driving the Marshall stack, the (added) master, a 12AX7 long-tail pair,
    ///     two EL34 at 460 V on a silicon rectifier, feedback from the 16 Ω tap with presence.
    /// </summary>
    public static AmpDesign Plexi { get; } = new(
        "Plexi",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                300,
                100e3,
                820,
                CathodeBypass: 250e-6,
                InputResistance: 68e3,
                GridLeak: 1e6
            ),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                300,
                100e3,
                820,
                CathodeBypass: 0.68e-6,
                CouplingCap: 22e-9,
                Volume: new Pot(1e6, PotTaper.Audio),
                VolumeRole: PotRole.Gain,
                BrightCap: 470e-12,
                SeriesResistor: 470e3,
                SeriesBypass: 470e-12,
                GridLeak: 470e3
            ),
            new ToneStackStageDesign(
                "V2B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                Supply: 300,
                CathodeResistor: 100e3,
                Stack: ToneStackDesign.Marshall,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Master
            )
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 1e3,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 50,
            PhaseInverter: new(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 820),
            CouplingCap: 22e-9,
            GridLeak: 220e3,
            GridStopper: 5.6e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(Math.Sqrt(2), 100e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 80),
            HasFeedback: true,
            Transformer: new(3.4e3),
            Speaker: new(Resonance: 100, PeakImpedance: 50, Q: 3),
            PowerSupply: new(40, 50e-6, 300, 50e-6, 10e3, 16e-6)
        ),
        -5
    );

    /// <summary>
    ///     Vox AC30 top-boost-style: V1 (220k plate), the volume into the top-boost gain stage V2A, the V2B cathode
    ///     follower driving the treble/bass stack, the (added) master, a 12AX7 long-tail pair with the cut control
    ///     across its plates, four cathode-biased EL84 (two per side) at 330 V on a GZ34-like rectifier, no global
    ///     feedback.
    /// </summary>
    public static AmpDesign AC30 { get; } = new(
        "AC30",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                250,
                220e3,
                1.5e3,
                CathodeBypass: 25e-6,
                InputResistance: 68e3,
                GridLeak: 1e6
            ),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                250,
                220e3,
                1.8e3,
                CouplingCap: 47e-9,
                Volume: new Pot(1e6, PotTaper.Audio),
                VolumeRole: PotRole.Gain,
                BrightCap: 100e-12
            ),
            new ToneStackStageDesign(
                "V2B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                Supply: 250,
                CathodeResistor: 100e3,
                Stack: ToneStackDesign.Vox,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Master
            )
        ],
        new(
            "4 x EL84",
            PentodeModel.EL84,
            TubesPerSide: 2,
            Supply: 330,
            ScreenResistor: 470,
            IdleCurrent: 0.034,
            CathodeBias: true,
            CathodeBypass: 250e-6,
            RatedPower: 30,
            PhaseInverter: new(TriodeModel.Ecc83, 300, 100e3, 100e3, 22e3, 1.5e3),
            CouplingCap: 47e-9,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 2.2e-9,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: false,
            Transformer: new(4e3),
            Speaker: new(Resonance: 75, PeakImpedance: 35, Q: 2.5),
            PowerSupply: new(150, 32e-6, 100, 32e-6, 10e3, 16e-6)
        ),
        -8
    );

    /// <summary>The models, in the order of the Circuit Amp block's type choice. Append only.</summary>
    public static IReadOnlyList<AmpDesign> All { get; } = [Blackface, Plexi, AC30];
}
