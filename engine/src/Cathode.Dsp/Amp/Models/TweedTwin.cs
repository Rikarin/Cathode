using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Twin 5F8-A (the "High Power" tweed Twin, about 80 W, 2 × 12): the Bright or Normal channel's half of V1
///     (12AY7, 100k plates, a shared 820 Ω ‖ 250 µF cathode), the 0.02 µF caps into the 1M audio volumes (the Bright
///     channel's with 100 pF), 270k mixing resistors into V2A (12AX7, 100k, 820 Ω unbypassed), the V2B cathode follower
///     DC-coupled to it (100k) driving the tweed stack (250 pF, 56k slope, 0.02 µF bass and mid caps; treble 250k linear,
///     bass 1M audio, mid 25k linear with its cap on the wiper), 0.02 µF into a 12AX7 long-tail pair (82k/100k, 470 Ω,
///     10k tail, 47 pF across the plates left out), four 5881 (6L6 curves) in fixed bias at 397 V on a GZ34, a 2.1k : 4 Ω
///     transformer, and 56k feedback from the speaker into the 5k presence pot (the tail's shunt, 0.1 µF on its wiper). Panel as the Bassman's: Bright and Normal
///     volumes, Bass, Middle, Treble, Presence; <b>Normal Input</b> plays the Normal channel. <see cref="Jumped" /> is the
///     same amp with the channels jumpered.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Fender drawing "Twin Amp 5F8-A" with its layout
///         (schematicheaven.net/fenderamps/twin_5f8a_schem.pdf,
///         code I-EG): every value above and the DC voltages (V1 plates 145 V, cathodes 2.2 V; V2A plate 165 V, cathode 1
///         V;
///         the follower's cathode 165 V; inverter plates 230/225 V; the supply 397 V on two 20 µF, a choke to 395 V for
///         the
///         screens, 4.7k to 355 V for the inverter, 10k to 295 V for the preamp; bias −41 V). The mid cap goes to the mid
///         pot's wiper on both factory pages (at full mid the classic stack, at zero the cap is grounded while the bass
///         pot
///         still sees the mid pot's 25k). The output transformer (Fender 45268, 2.1k : 4 Ω) is from replacement listings
///         (tubeampdoctor.com); the speakers (two 8 Ω in parallel on the 4 Ω secondary) are inferred from the drawing.
///     </para>
///     <para>
///         The input stage and V2A are the Bassman's (<see cref="Bassman" />) at this amp's 295 V preamp node. Unverified:
///         the 5881 idle current (35 mA; the drawing gives the bias voltage, not the current), the supply's source
///         resistance. The drawing has one 1.5k grid stopper for each pair of tied grids: here 1.5k a side.
///     </para>
/// </remarks>
public static class TweedTwin {
    const double Preamp = 295;

    public static AmpDesign Design { get; } = Build("Tweed Twin 5F8-A", Bassman.InputStage(Preamp), "Normal Input");

    /// <summary>The 5F8-A with its channels jumpered: the guitar into the Bright channel, a cable on to the Normal one.</summary>
    public static AmpDesign Jumped { get; } = Build("Tweed Twin 5F8-A Jumped", Bassman.JumpedStage(Preamp), null);

    static AmpDesign Build(string name, NetworkStageDesign input, string? channelSwitch) =>
        new(
            name,
            [
                input,
                Bassman.SecondStage(Preamp),
                Follower()
            ],
            new(
                "4 x 5881",
                PentodeModel.SixL6GC,
                2,
                397,
                470,
                0.035,
                false,
                0,
                80,
                new LongTailPairDesign(TriodeModel.Ecc83, 355, 82e3, 100e3, 10e3, 470),
                0.1e-6,
                220e3,
                1.5e3,
                0,
                new(1e6, PotTaper.Linear),
                new(1 / Math.Sqrt(2), 56e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6) { PresenceIsShunt = true },
                true,
                new(2.1e3, 10, 5e-3, 75, 2e-9, 100e3),
                SpeakerDesign.OpenBack2x12,
                new(60, 40e-6, 100, 20e-6, 14.7e3, 20e-6, 6.8e-3, RectifierModel.GZ34),
                InverterInput: new(20e-9, 1e6)
            ),
            -6
        ) {
            Panel = new() {
                Gain = "Bright Vol",
                Control1 = "Normal Vol",
                Depth = null,
                Cut = null,
                Master = null,
                Bright = null,
                Switch1 = channelSwitch
            }
        };

    /// <summary>
    ///     V2B, a cathode follower DC-coupled to V2A's plate (100k cathode resistor), and the tweed stack on its cathode
    ///     with the mid cap on the mid pot's wiper; the output is the treble wiper.
    /// </summary>
    static NetworkStageDesign Follower() {
        var net = new StageNetlist();
        var (cathode, rail) = (net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.Triode(rail, net.Input, cathode, TriodeModel.Ecc83);

        var (top, bottom, slope, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(cathode, top, 250e-12, CapacitorRole.Stack);
        net.Resistor(cathode, slope, 56e3);
        net.Capacitor(slope, bottom, 20e-9, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        var mid = net.Pot(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, mid, 20e-9, CapacitorRole.Stack);
        net.Output = treble;
        return new("V2B", net);
    }
}
