using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Laney GH100L, drive channel (factory drawing 2188 issue 4, "GH100L circuit", with its DC voltages): V1a (100k,
///     2k7 with 680 nF, 47 pF plate to cathode) from the High input (68k, 1M) or the Low input's 220k/220k divider
///     (switch 1); two 4n7 in series and 220k into the 1M-A Drive pot (1M to ground before it); V1b (100k, 2k7 with
///     680 nF) through 68k; 22 nF into the 1M-A Gain pot and 68k to the V2a cold clipper (100k, 10k unbypassed); 22 nF
///     into 470k with 22 nF over 470k to V2b (100k, 820 unbypassed); the V3a cathode follower (100k) on a
///     Marshall-style stack (470 pF, 33k, 22 nF, 22 nF; 220k-B/1M-A/22k-B) and the 1M log master; the effects loop's
///     op-amps and the V3b recovery stage (100k, 1k unbypassed, 300 V); a 12AX7 long-tail pair (22 nF in, 1M, 470, 10k,
///     82k/100k, 22 nF out to 220k), four EL34 (5k6 stoppers, 1k5 screens) at 460 V, 100k feedback with 4k7 and the
///     22k-B presence with 100 nF.
/// </summary>
/// <remarks>
///     <para>
///         Nodes from the drawing: 460 V plates, 457 V screens, 310 V inverter, 260 V, 250 V and 235 V preamp; the
///         inverter's plates at 205 V and the EL34 grids at -35 V. The drawing's stack has 220 nF for the bass cap
///         (other drawings 22 nF, docs/06): here 22 nF. The 22 nF across V2b's 470k is as drawn (the JCM800 has 470 pF
///         there).
///     </para>
///     <para>
///         The effects loop (bypassed) is two inverting TL072 stages, -33k/1M then -22k/22k, into a 22 nF to 1k and
///         100k at V3b's grid: a gain of 0.033 with a 79 Hz high-pass and a 1M load on the master. It is a passive
///         divider here with the same ratio, corner and load (1.97 nF, 1M over 34k), whose higher source resistance
///         only matters if V3b's grid conducts. The clean channel (the relay that skips V1b) and the Resonance switch
///         (a second 100k of feedback) are not modelled. Unverified: the feedback tap (4 Ω assumed) and the output
///         transformer's 1.7k (Laney part 6011).
///     </para>
/// </remarks>
public static class LaneyGH100L {
    /// <summary>The drawing's stack, with the 22 nF bass cap (see the remarks).</summary>
    public static ToneStackDesign Stack { get; } = new(
        "GH100L",
        470e-12,
        33e3,
        22e-9,
        22e-9,
        new(220e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(22e3, PotTaper.Linear)
    );

    public static AmpDesign Design { get; } = new(
        "Laney GH100L",
        [
            Input(),
            new TriodeStageDesign("V1b", TriodeModel.Ecc83, 235, 100e3, 2.7e3, 680e-9, 68e3),
            new TriodeStageDesign(
                "V2a",
                TriodeModel.Ecc83,
                250,
                100e3,
                10e3,
                CouplingCap: 22e-9,
                Volume: new Pot(1e6, PotTaper.Audio),
                VolumeRole: PotRole.Gain,
                SeriesResistor: 68e3
            ),
            new TriodeStageDesign(
                "V2b",
                TriodeModel.Ecc83,
                250,
                100e3,
                820,
                CouplingCap: 22e-9,
                SeriesResistor: 470e3,
                SeriesBypass: 22e-9,
                GridLeak: 470e3
            ),
            new ToneStackStageDesign(
                "V3a",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                260,
                100e3,
                Stack,
                new(1e6, PotTaper.Audio),
                PotRole.Master
            ),
            new TriodeStageDesign(
                "V3b",
                TriodeModel.Ecc83,
                300,
                100e3,
                1e3,
                CouplingCap: 1.97e-9,
                SeriesResistor: 1e6,
                GridLeak: 34.1e3
            )
        ],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            460,
            1.5e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 310, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(0.5), 100e3, 4.7e3, new(22e3, PotTaper.Linear), 100e-9, 80),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 100e-6, 22e3, 15e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -9
    ) {
        Panel = AmpPanel.Standard with {
            Control1 = "Drive",
            Cut = null,
            Depth = null,
            Bright = null,
            Switch1 = "Low Input"
        }
    };

    /// <summary>
    ///     The inputs and V1a to the Drive pot's wiper: the High input's 1M and 68k, or (switch 1) the Low input's
    ///     220k/220k divider before them; two 4n7 in series out, 1M to ground, 220k into the 1M-A Drive pot.
    /// </summary>
    static NetworkStageDesign Input() {
        const double Closed = 10;
        var net = new StageNetlist();
        var (jack, grid, rail, plate, cathode, divider, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.SwitchedResistor(net.Input, jack, Closed, 220e3, SwitchRole.Switch1);
        net.SwitchedResistor(jack, StageNetlist.Ground, 0, 220e3, SwitchRole.Switch1);
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 68e3);
        net.Supply(rail, 235);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 680e-9);
        net.Capacitor(plate, cathode, 47e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, divider, 2.35e-9); // C5 and C6, 4n7 each, in series
        net.Resistor(divider, StageNetlist.Ground, 1e6);
        net.Resistor(divider, top, 220e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Output = wiper;
        return new("V1a", net);
    }
}
