using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Hiwatt DR504 (Custom 50), from Mark Huss's hiwatt.org redraws: the mid-70s four-input preamp it shares with the
///     DR103 (<see cref="HiwattParts" />: "Mid-70s Four-Input Preamp" rev 1.4, with the 12AT7 long-tail pair biased from
///     a cathode follower and 10k of feedback from the 16 Ω tap into 2k2 and the 100k presence with 0.1 µF), the
///     "50 Watt Output Stage" v1.1 (https://hiwatt.org/Schematics/DR_50wOutput.pdf) and the "Power Supply for 50 Watt
///     Amp" v1.1 (https://hiwatt.org/Schematics/DR_50wPS.pdf): two EL34 with 22k grid stoppers, 100k grid leaks to the
///     −36 V bias and 1k5 5 W screen resistors on a Partridge TG6556; a voltage doubler on 350 V (two 220 µF in series)
///     giving 460 V at HT1, 100 Ω 5 W to the screens (HT2), 1k to the preamp's node (HT3).
/// </summary>
/// <remarks>
///     <para>
///         The inverter is the library's long-tail pair with the DR103's rest point (a 22.6k tail under 1.6k, see
///         <see cref="HiwattDR103" />): the preamp drawing gives the same circuit for both amps.
///     </para>
///     <para>
///         hiwatt.org also has a complete DR504 drawing with DC voltages, "HIWATT DR504OL" (date code 8313, so a 1983
///         amp: https://hiwatt.org/Schematics/DR504OL.gif). It is a later, two-input amp with an extra 220k gain stage
///         ahead of the tone network, so it is not the amp modelled here; its rails (506 V at the plates, 433 V and 388 V
///         at the preamp's two nodes, 496 V at the inverter, −40 V bias) are what the guesses below are scaled from.
///     </para>
///     <para>
///         Not on the four-input drawings (guesses): the rails below HT1 (450 V at the inverter from HT3; 395 V at V2A,
///         355 V at V1 and V2B, the DR504OL's nodes scaled to this supply's 460 V), the EL34s' 35 mA, the TG6556's
///         primary (3.4k plate to plate, usual for a 50 W EL34 pair), the doubler's source resistance and the screen
///         node's cap (the drawing's HT2 to HT3 stack, 2 × 50 µF over 220 µF, about 70 µF).
///     </para>
/// </remarks>
public static class HiwattDR504 {
    const double V1Supply = 355;
    const double V2ASupply = 395;

    public static AmpDesign Design { get; } = new(
        "Hiwatt DR504",
        [HiwattParts.Input(V1Supply), HiwattParts.V2A(V2ASupply), HiwattParts.V2B(V1Supply)],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 1.5e3,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 50,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc81, 450, 82e3, 91e3, 22.6e3, 1.6e3),
            CouplingCap: 47e-9,
            GridLeak: 100e3,
            GridStopper: 22e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(Math.Sqrt(2), 10e3, 2.2e3, new(100e3, PotTaper.Linear), 0.1e-6),
            HasFeedback: true,
            Transformer: new(3.4e3),
            Speaker: SpeakerDesign.Closed4x12,
            PowerSupply: new(40, 110e-6, 100, 70e-6, 32e3, 16e-6, 6e-3),
            InverterInput: null
        ),
        -6
    ) {
        Panel = HiwattParts.Panel
    };
}
