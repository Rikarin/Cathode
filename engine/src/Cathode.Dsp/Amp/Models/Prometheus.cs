using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Lichtlaerm Audio Prometheus, lead channel (Berlin, 2023-24; 100-140 W, four EL34 stock).
///     <b>
///         The topology is
///         inferred, not read from a schematic
///     </b>
///     : none is published. What is documented (the maker's page, owners'
///     reports) is a lead channel with Gain, Bass, Middle, Treble, a Vintage/Modern voicing switch, a three-way "Bite"
///     switch (high-mid bump / off / low-mid bump), a "Cut" knob (a low cut ahead of the gain stages), and global
///     Presence, Depth and Master into a fixed-bias EL34 quad. Players compare it with Soldano, Driftwood and Diezel
///     amps, so the preamp here is a plausible cascade in that lineage: V1A; the Cut network and the gain pot into V1B,
///     whose cathode carries the Bite caps; the cold clipper V2A; V2B; the V3A cathode follower driving a
///     Marshall/Soldano-type stack and the master; a long-tail pair into four EL34 with feedback from the 16 Ω tap.
/// </summary>
/// <remarks>
///     <para>
///         Guessed, then fitted to captures of the real amp (docs/07): every value below, which switch element does
///         what, and how many stages there are. The panel is the real amp's; the channel volume is folded into the
///         master. The stages behind a 470k grid resistor carry the 12AX7's grid-plate capacitance (1.7 pF), whose Miller
///         effect is much of a cascade's treble loss.
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Cut</b> (<see cref="PotRole.Control1" />): a 1M reverse-log pot, wired as a variable resistor in series
///             with 47k, shunts the gain pot after its 10 nF coupling cap: turning Cut up lowers the shunt and raises
///             the high-pass corner from about 30 Hz to 150 Hz, a variable low cut ahead of the clipping stages.
///         </item>
///         <item>
///             <b>Modern</b> (<see cref="SwitchRole.Switch1" />): bypasses V2B's cathode (more gain) and couples the
///             cold clipper through a smaller cap (tighter).
///         </item>
///         <item>
///             <b>Bite</b>: up (<see cref="SwitchRole.Switch2" />) bypasses V1B's cathode with a small cap, a boost from
///             about 1.2 kHz that the later stages' treble losses turn into a high-mid bump; down
///             (<see cref="SwitchRole.Switch3" />) with a larger one, a low-mid boost from about 400 Hz.
///         </item>
///     </list>
/// </remarks>
public static class Prometheus {
    /// <summary>B+ of the preamp node.</summary>
    const double Preamp = 330;

    /// <summary>A 12AX7's grid-plate capacitance.</summary>
    const double GridPlate = 1.7e-12;

    /// <summary>The stack: Marshall topology with a 33k slope resistor and a 50k middle pot (assumed; flatter mids).</summary>
    static readonly ToneStackDesign Stack = new(
        "Prometheus",
        470e-12,
        33e3,
        22e-9,
        22e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(50e3, PotTaper.Linear)
    );

    public static AmpDesign Design { get; } = new(
        "Prometheus",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                Preamp,
                220e3,
                2.7e3,
                0.68e-6,
                68e3,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("V1B", GainStage()),
            new NetworkStageDesign("V2A", ColdClipper()),
            new NetworkStageDesign("V2B", Driver()),
            new ToneStackStageDesign(
                "V3A",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                Preamp,
                100e3,
                Stack,
                new(1e6, PotTaper.Audio),
                PotRole.Master
            )
        ],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 340, 82e3, 100e3, 10e3, 1e3),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 100e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            true,
            new(1.7e3),
            new(Resonance: 95, PeakImpedance: 30, Q: 2.5),
            new(25, 100e-6, 100, 50e-6, 10e3, 22e-6)
        ),
        -10
    ) {
        Panel = AmpPanel.Standard with {
            Cut = null,
            Bright = null,
            Switch1 = "Modern",
            Switch2 = "Bite Up",
            Switch3 = "Bite Down",
            Control1 = "Cut"
        }
    };

    /// <summary>The Cut network and the gain pot into V1B, with the Bite caps on its cathode.</summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var top = net.Node();
        net.Capacitor(net.Input, top, 10e-9);
        var cut = net.Node();
        net.Resistor(top, cut, 47e3);
        var shorted = net.Pot(cut, StageNetlist.Ground, new(1e6, PotTaper.ReverseAudio), PotRole.Control1);
        net.Resistor(shorted, StageNetlist.Ground, 1);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        var grid = net.Node();
        net.Resistor(wiper, grid, 470e3);
        net.Capacitor(wiper, grid, 470e-12, CapacitorRole.Bright);
        var cathode = Triode(net, grid, 100e3, 2.7e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 47e-9, SwitchRole.Switch2);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 0.15e-6, SwitchRole.Switch3);
        return net;
    }

    /// <summary>V2A, biased near cut-off by its unbypassed 10k cathode; Modern couples it through a smaller cap.</summary>
    static StageNetlist ColdClipper() {
        var net = new StageNetlist();
        var leak = net.Node();
        net.SwitchedCapacitor(net.Input, leak, 2.2e-9, SwitchRole.Switch1, off: 4.7e-9);
        net.Resistor(leak, StageNetlist.Ground, 470e3);
        var grid = net.Node();
        net.Resistor(leak, grid, 470e3);
        Triode(net, grid, 100e3, 10e3);
        return net;
    }

    /// <summary>V2B, its plate bypassed for the treble; Modern bypasses its cathode.</summary>
    static StageNetlist Driver() {
        var net = new StageNetlist();
        var leak = net.Node();
        net.Capacitor(net.Input, leak, 22e-9);
        net.Resistor(leak, StageNetlist.Ground, 1e6);
        var grid = net.Node();
        net.Resistor(leak, grid, 470e3);
        var cathode = Triode(net, grid, 100e3, 1.5e3, 470e-12);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 1e-6, SwitchRole.Switch1);
        return net;
    }

    /// <summary>
    ///     A 12AX7 with its plate resistor (and optional plate bypass) from the preamp rail, its cathode resistor and its
    ///     grid-plate capacitance; the plate is the output. Returns the cathode.
    /// </summary>
    static int Triode(
        StageNetlist net,
        int grid,
        double plateResistor,
        double cathodeResistor,
        double plateBypass = 0
    ) {
        var (plate, cathode, supply) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, Preamp);
        net.Resistor(supply, plate, plateResistor);
        if (plateBypass > 0) {
            net.Capacitor(supply, plate, plateBypass);
        }

        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(grid, plate, GridPlate);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return cathode;
    }
}
