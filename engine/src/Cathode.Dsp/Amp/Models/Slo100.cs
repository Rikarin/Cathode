using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Soldano SLO-100, overdrive and normal channels. Overdrive: V1A (220k, 1.8k ‖ 1 µF), the OD preamp gain (500k
///     audio, 1 nF bright cap, 470k ‖ 2 nF ahead of it and 470k after it) into V1B, a 470k/1M divider into the V2A cold
///     clipper (39k unbypassed,
///     100k plate with a 1 nF plate bypass), V2B (220k) DC-coupled to a cathode follower whose 100k + 1k cathode load
///     is the effects-loop send (about -40 dB), the loop return stage, and a second cathode follower driving a
///     Marshall-topology stack (47k slope, 470 pF, 22 nF, 22 nF) and the OD master; a 12AX7 long-tail pair, four 6L6GC
///     on a silicon rectifier, feedback from the 4 Ω tap with presence and depth. Normal (Switch 1): the Crunch/Clean
///     divider, the bright cap and the normal volume into one more stage, joining before V2B.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Rob Robinette's "How the Soldano SLO-100 works" and AmpBooks' SLO page (stage values, the cold
///         clipper and its plate bypass, the stack), and the Yahiake LiveSPICE redraw "SLO100 OD Channel Preamp"
///         (wiring of the OD gain network, the 470k/1M divider straight into the cold clipper, the loop and the
///         return stage). Values agree between them except where noted.
///     </para>
///     <para>
///         Guesses and conflicts, marked here because no factory drawing could be read: V1A's plate is 220k in the
///         text sources (the redraw has 200k); V1B's cathode bypass is 1 µF in series with 100 Ω (text; the redraw
///         has a plain 1 µF); the stage after the cold clipper has 220k in series and 2.2M to ground (redraw; here the
///         2.2M sits on the grid side of the 220k, which only matters under grid current); the redraw wires the
///         middle pot with its cap on the wiper, here it is the usual Marshall rheostat; the redraw has no grid stopper
///         or leak on the cold clipper (docs/06 quotes 220k and 330k, probably the next stage's). The power amp is not
///         on the redraw: inverter plates 82k/100k, 220k power-grid leaks, 470 V B+, 40 mA idle, a 4.7k feedback
///         shunt and 25k presence pot with 0.1 µF, and the 2.2k primary (Robinette, unverified) are typical values,
///         not readings. The inverter's grid current (<see cref="InverterInputDesign" />) is left out: it costs 1.7 %
///         of a core.
///     </para>
///     <para>
///         The normal channel (Switch 1, with Switch 2 for Crunch and the Bright switch) is read from the
///         "Soldano Super Lead Overdrive 100W" drawing of 15 March 1996 (el34world, Soldano_slo100.pdf, a clean CAD
///         redraw of unknown origin with optocoupler channel switching): V1A's 20 nF output feeds both channels; the
///         Crunch/Clean switch puts 470k in series and 39k across the normal volume (Clean) or neither (Crunch), a 470k
///         with the bright cap follows, the 500k normal volume, the normal V1B (100k, 2.2k unbypassed), 20 nF into
///         2.2M ‖ 120 pF over 330k, which joins the overdrive path at the grid side of the stage after the cold
///         clipper. Robinette's text agrees on the dividers (Crunch passes 52 % at full volume, Clean 4 %; 13 % after
///         the second stage). Read with doubt: the drawing wires a short across the overdrive path's 470k ‖ 2 nF (taken
///         as a drafting error: the overdrive channel keeps it, as in the other sources), and its Bright/Norm switch
///         looks like it selects the 470 pF in place of the 470k (here it puts the cap across the 470k, as Fractal
///         describes a bright cap on this channel); the normal volume's taper is not printed (audio assumed).
///     </para>
///     <para>
///         Not modelled: in overdrive mode the normal channel stays connected and its out-of-phase signal mixes into
///         the overdrive path (Robinette); the separate normal master (Master turns both channels); the loop's jacks
///         (send and return are joined).
///     </para>
/// </remarks>
public static class Slo100 {
    public static ToneStackDesign Stack { get; } = new(
        "SLO-100",
        470e-12,
        47e3,
        22e-9,
        22e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    public static AmpDesign Design { get; } = new(
        "SLO-100",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                360,
                220e3,
                1.8e3,
                1e-6,
                68e3,
                GridLeak: 1e6
            ),
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("V1B", OverdriveGain()),
                    new TriodeStageDesign(
                        "V2A",
                        TriodeModel.Ecc83,
                        350,
                        100e3,
                        39e3,
                        CouplingCap: 20e-9,
                        SeriesResistor: 470e3,
                        GridLeak: 1e6,
                        PlateBypass: 1e-9
                    ),
                    new TriodeStageDesign(
                        "V2B",
                        TriodeModel.Ecc83,
                        380,
                        220e3,
                        1.8e3,
                        1e-6,
                        CouplingCap: 20e-9,
                        SeriesResistor: 220e3,
                        GridLeak: 2.2e6
                    )
                ],
                [
                    new NetworkStageDesign("Normal V1B", NormalChannel()),
                    new TriodeStageDesign(
                        "Normal V2B",
                        TriodeModel.Ecc83,
                        380,
                        220e3,
                        1.8e3,
                        1e-6,
                        SeriesResistor: 220e3
                    )
                ]
            ),
            new NetworkStageDesign("V3A", SendFollower()),
            new TriodeStageDesign(
                "V4A",
                TriodeModel.Ecc83,
                380,
                220e3,
                2.2e3,
                25e-6,
                CouplingCap: 1e-6,
                GridLeak: 1e6
            ),
            new ToneStackStageDesign(
                "V4B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                380,
                100e3,
                Stack,
                new(1e6, PotTaper.Audio),
                PotRole.Master
            )
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            470,
            1e3,
            0.04,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 380, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            1e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 39e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6, 90),
            true,
            new(2.2e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            new(30, 110e-6, 100, 47e-6, 10e3, 22e-6, 6e-3),
            InverterInput: null
        ),
        -7.5
    ) { Panel = AmpPanel.Standard with { Cut = null, Switch1 = "Normal Ch", Switch2 = "Crunch" } };

    /// <summary>
    ///     V1B with the OD gain network: 20 nF from V1A, 470k ‖ 2 nF, the 500k audio OD gain with a fixed 1 nF across
    ///     its upper half, 470k to the grid; 100k plate, 1.8k with 1 µF in series with 100 Ω.
    /// </summary>
    static StageNetlist OverdriveGain() {
        var net = new StageNetlist();
        var (coupled, top, grid, plate, cathode, bypass, supply) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, top, 470e3);
        net.Capacitor(coupled, top, 2e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Supply(supply, 350);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, bypass, 1e-6);
        net.Resistor(bypass, StageNetlist.Ground, 100);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The normal channel from V1A's plate: 20 nF; the overdrive path's 470k ‖ 2 nF and gain pot loading it; the
    ///     Crunch/Clean switch (Clean: 470k in series and 39k across the volume; Crunch: neither), the 470k with the
    ///     Bright switch's 470 pF across it, the 500k normal volume; V1B (100k, 2.2k unbypassed, 359 V); 20 nF, 2.2M
    ///     ‖ 120 pF into 330k to ground, where the overdrive path joins.
    /// </summary>
    static StageNetlist NormalChannel() {
        var net = new StageNetlist();
        var (coupled, overdrive, mode, top, supply, plate, cathode, output, join) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node()
        );
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, overdrive, 470e3);
        net.Capacitor(coupled, overdrive, 2e-9);
        net.Resistor(overdrive, StageNetlist.Ground, 500e3);
        net.Resistor(coupled, mode, 470e3);
        net.SwitchedResistor(coupled, mode, 0, 10, SwitchRole.Switch2);
        net.Resistor(mode, top, 470e3);
        net.SwitchedCapacitor(mode, top, 470e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.SwitchedResistor(top, StageNetlist.Ground, 39e3, 1e12, SwitchRole.Switch2); // open with Crunch on
        var grid = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Supply(supply, 359);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, 20e-9);
        net.Resistor(output, join, 2.2e6);
        net.Capacitor(output, join, 120e-12);
        net.Resistor(join, StageNetlist.Ground, 330e3);
        net.Output = join;
        return net;
    }

    /// <summary>
    ///     The send follower V3A, DC-coupled to V2B's plate (the previous stage's open-circuit plate voltage behind its
    ///     output resistance): 100k and 1k to ground, the send taken across the 1k.
    /// </summary>
    static StageNetlist SendFollower() {
        var net = new StageNetlist();
        var (supply, follower, send) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, 380);
        net.Resistor(follower, send, 100e3);
        net.Resistor(send, StageNetlist.Ground, 1e3);
        net.Triode(supply, net.Input, follower, TriodeModel.Ecc83);
        net.Output = send;
        return net;
    }
}
