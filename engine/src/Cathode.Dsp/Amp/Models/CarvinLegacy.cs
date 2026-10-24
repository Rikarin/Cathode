using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Carvin Legacy VL100 (Steve Vai, 100 W), lead and clean channels. V1B (220k, 910 Ω ‖ 1 µF) feeds both. Lead: 2.2 nF
///     into the 500k audio Drive (560 pF from its wiper to ground) and V1A (220k with 560 pF across it, 1.5k ‖ 1 µF);
///     2.2 nF, 1M, 470k ‖ 560 pF and 4.7 nF into V2B (68k grid leak; 150k with 1.12 nF across it, 3.3k unbypassed);
///     470k ‖ (220k + 560 pF) and 2.2 nF into V2A (470k leak; 220k with 560 pF across it, 2.2k ‖ 47 µF); 10 nF into a
///     James (Baxandall-type) tone stack: 100k into the 1M bass pot with 10 nF across its upper and 11.1 nF across its
///     lower half above the 25k Mid rheostat, 100k from its wiper to the treble wiper, 250 pF into the 1M treble pot
///     over 2.2 nF; 33 nF and 47k into Volume 1 (1M linear) with the Presence rheostat (500k audio, 2.2 nF) on its
///     wiper. Clean: 100k into a James stack with its own values (4.7 nF and 22 nF on the bass pot, 250 pF and 2.2 nF
///     on the treble), 10 nF and 470k (330 pF across it with the Presence switch) over 220k, Volume 2 (1M) into V3B
///     (220k, 1.5k unbypassed). Both channels: a 180k/33k pad into the loop's transistor buffer, the return stage V3A
///     (100k, 910 Ω ‖ 10 µF), 100k into the inverter's grid, where the feedback (360k from the 8 Ω tap, 12 dB) sums; a
///     long-tail pair (100k plates, 560 Ω and 44k tail) into four EL34 (4.7k grid stoppers, 350 Ω screens) at 455 V.
/// </summary>
/// <remarks>
///     <para>
///         Source: Carvin's factory schematic "Legacy 100W Tube Series", PCB 30-32101 rev D (G. Dreyer, 7 Jul 1998,
///         revised 16 Dec 1999), a vector PDF distributed by thetubestore
///         (https://www.thetubestore.com/lib/thetubestore/schematics/Carvin/Carvin-Legacy-Vl100-Vl212-Schematic.pdf).
///         Every preamp, inverter and supply value here is read from it, including the rails (385 V preamp, 415 V
///         inverter, 445 V screens, 455 V plates), the bias setting (100 mA for four tubes) and the 12 dB of feedback.
///     </para>
///     <para>
///         Guesses, marked because the schematic does not give them: the output transformer's primary (1.7k, a
///         typical 100 W EL34 value; the part is Carvin 15-02116), the supply's source resistance, the Volume 2 taper
///         (printed "1M", taken as audio). Simplified: the loop's send is an MPSW42 emitter follower (a unity buffer
///         here, so send and return are joined); the inverter's driven grid takes the feedback as shunt feedback
///         through 360k (the 10 µF blocking cap shorted), which here loads V3A's output as 360k to ground, while the
///         feedback itself goes to the tail as the power amp's usual series feedback, sized for 12 dB (the return to
///         the speaker then has about 12 dB more gain than the schematic's "240 mV in for 25 V at 8 Ω": the power
///         section's open-loop gain is likely too high, the transformer being unknown); the inverter's
///         second grid is AC-grounded at the middle of its 22k + 22k tail on the real amp, at ground here; on the lead
///         channel V1B also drives the clean stack, which loads it as a fixed network with its pots at noon. Not
///         modelled: the half-power switch (two tubes), the EL34/6L6 bias switch, the reverb (clean channel) and the
///         line out.
///     </para>
///     <para>
///         Panel: the Bass, Middle and Treble knobs turn both channels' stacks (the amp has a set per channel). Gain is
///         the lead Drive and Master the lead Volume 1; Control 1 is the lead Presence (a preamp control: the power amp
///         has none), Control 2 the clean Volume 2; Switch 1 selects the clean channel, Switch 2 is its Presence switch
///         (about 9 dB at 10 kHz).
///     </para>
///     <para>
///         Where the stages are cut: every plate here has a cap across its load resistor and V1B's cathode is only
///         partly bypassed, so the plates' output impedances are reactive, which a cut's output resistance cannot carry
///         into a low-impedance network such as the stacks (-21 dB linear error at V1B's plate). So V1B and V1A end at
///         the next grid, which draws nothing until it conducts (-68 dB to V2B's plate). V2B ends at its plate, so that
///         V2A's 2.2 nF coupling cap charges under grid current in V2A's own netlist (cut at V2A's grid, the first four
///         stages were 9 dB worse driven hard, 47 dB better in the linear range); the stack is in V2A's netlist.
///     </para>
/// </remarks>
public static class CarvinLegacy {
    const double Rail = 385;

    public static AmpDesign Design { get; } = new(
        "Legacy VL100",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("V1B", LeadInput()),
                    new NetworkStageDesign("V1A", FirstLeadStage()),
                    new NetworkStageDesign("V2B", SecondLeadStage()),
                    new NetworkStageDesign("V2A", LeadStack())
                ],
                [
                    new NetworkStageDesign("V1B", CleanInput()),
                    new NetworkStageDesign("V3B", CleanStage())
                ]
            ),
            new TriodeStageDesign(
                "V3A",
                TriodeModel.Ecc83,
                Rail,
                100e3,
                910,
                10e-6,
                CouplingCap: 10e-9,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("Inverter grid", InverterGrid())
        ],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            455,
            350,
            0.025,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 415, 100e3, 100e3, 44e3, 560),
            47e-9,
            220e3,
            4.7e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 100e3, 4.7e3, new(1e3, PotTaper.Linear), 1e-12),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 66e-6, 350, 22e-6, 6.9e3, 22e-6, 5e-3, RectifierModel.Silicon)
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Drive",
            Master = "Volume 1",
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Control1 = "Presence",
            Control2 = "Volume 2",
            Switch1 = "Clean Ch",
            Switch2 = "Clean Presence"
        }
    };

    /// <summary>
    ///     V1B, which both channels share: 220k and 10 pF from the grid to ground at the jack; 220k plate,
    ///     910 Ω ‖ 1 µF; 47 nF out. Returns the coupled output.
    /// </summary>
    static int InputStage(StageNetlist net) {
        var (supply, plate, cathode, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 220e3);
        net.Capacitor(net.Input, StageNetlist.Ground, 10e-12);
        net.Supply(supply, Rail);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 910);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, 47e-9);
        return output;
    }

    /// <summary>
    ///     V1B and the lead input to V1A's grid: the clean stack's load (pots at noon), 4.7M to ground, 2.2 nF into the
    ///     Drive (500k audio) with 560 pF from its wiper to ground.
    /// </summary>
    static StageNetlist LeadInput() {
        var net = new StageNetlist();
        var relay = InputStage(net);
        var top = net.Node();
        CleanStackLoad(net, relay);
        net.Resistor(relay, StageNetlist.Ground, 4.7e6);
        net.Capacitor(relay, top, 2.2e-9);
        var grid = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(grid, StageNetlist.Ground, 560e-12);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     The clean channel's stack seen from V1B's coupling cap on the lead channel, its pots at noon (bass and treble
    ///     linear halves, the 25k audio middle at about 3k) and the presence switch out.
    /// </summary>
    static void CleanStackLoad(StageNetlist net, int node) {
        var (bassTop, bassWiper, bassBottom, trebleTop, trebleWiper, trebleBottom, coupled, volume) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Resistor(node, bassTop, 100e3);
        net.Resistor(bassTop, bassWiper, 500e3);
        net.Capacitor(bassTop, bassWiper, 4.7e-9);
        net.Resistor(bassWiper, bassBottom, 500e3);
        net.Capacitor(bassWiper, bassBottom, 22e-9);
        net.Resistor(bassBottom, StageNetlist.Ground, 3e3);
        net.Resistor(bassWiper, trebleWiper, 100e3);
        net.Capacitor(node, trebleTop, 250e-12);
        net.Resistor(trebleTop, trebleWiper, 500e3);
        net.Resistor(trebleWiper, trebleBottom, 500e3);
        net.Capacitor(trebleBottom, StageNetlist.Ground, 2.2e-9);
        net.Capacitor(trebleWiper, coupled, 10e-9);
        net.Resistor(coupled, volume, 470e3);
        net.Resistor(volume, StageNetlist.Ground, 180e3);
    }

    /// <summary>
    ///     V1A (220k with 560 pF across it, 1.5k ‖ 1 µF) to V2B's grid: 2.2 nF, 1M to ground, 470k ‖ 560 pF, 4.7 nF,
    ///     68k grid leak.
    /// </summary>
    static StageNetlist FirstLeadStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, series, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, Rail);
        net.Resistor(supply, plate, 220e3);
        net.Capacitor(supply, plate, 560e-12);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 2.2e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, series, 470e3);
        net.Capacitor(coupled, series, 560e-12);
        net.Capacitor(series, grid, 4.7e-9);
        net.Resistor(grid, StageNetlist.Ground, 68e3);
        net.Output = grid;
        return net;
    }

    /// <summary>V2B: 150k with two 560 pF across it, 3.3k unbypassed.</summary>
    static StageNetlist SecondLeadStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, Rail);
        net.Resistor(supply, plate, 150e3);
        net.Capacitor(supply, plate, 1.12e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V2A and the lead stack: 470k ‖ (220k + 560 pF) from V2B's plate, 2.2 nF to the grid, 470k grid leak; 220k
    ///     with 560 pF across it, 2.2k ‖ 47 µF; 10 nF into the stack: 100k into the 1M bass pot (10 nF
    ///     across its upper part, 10 nF and two 560 pF across its lower), the 25k audio Mid rheostat under it, 100k
    ///     from the bass wiper to the treble wiper; 250 pF into the 1M treble pot over 2.2 nF; 33 nF and 47k into
    ///     Volume 1 (1M linear), the Presence rheostat (500k audio) and 2.2 nF on its wiper; the 180k/33k pad to the
    ///     send.
    /// </summary>
    static StageNetlist LeadStack() {
        var net = new StageNetlist();
        var (shelf, series, grid, supply, plate, cathode, drive, coupled, volumeTop, presence, send) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node(), net.Node()
        );
        net.Resistor(net.Input, series, 470e3);
        net.Capacitor(net.Input, shelf, 560e-12);
        net.Resistor(shelf, series, 220e3);
        net.Capacitor(series, grid, 2.2e-9);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Supply(supply, Rail);
        net.Resistor(supply, plate, 220e3);
        net.Capacitor(supply, plate, 560e-12);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, drive, 10e-9);
        var output = JamesStack(net, drive, 10e-9, 11.12e-9);
        net.Capacitor(output, coupled, 33e-9);
        net.Resistor(coupled, volumeTop, 47e3);
        var volume = net.Pot(volumeTop, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Master);
        net.Rheostat(volume, presence, new(500e3, PotTaper.Audio), PotRole.Control1);
        net.Capacitor(presence, StageNetlist.Ground, 2.2e-9);
        net.Resistor(volume, send, 180e3);
        net.Resistor(send, StageNetlist.Ground, 33e3);
        net.Output = send;
        return net;
    }

    /// <summary>
    ///     A Legacy James stack driven at <paramref name="drive" />: 100k slope into the 1M linear bass pot with
    ///     <paramref name="upper" /> across its upper and <paramref name="lower" /> across its lower part, the 25k
    ///     audio Mid rheostat below it, 100k from the bass wiper to the treble wiper; 250 pF into the 1M linear treble
    ///     pot over 2.2 nF to ground. Returns the treble wiper, the stack's output.
    /// </summary>
    static int JamesStack(StageNetlist net, int drive, double upper, double lower) {
        var (bassTop, bassBottom, trebleTop, trebleBottom) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(drive, bassTop, 100e3);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, PotTaper.Linear), PotRole.Bass);
        net.Capacitor(bassTop, bass, upper, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, lower, CapacitorRole.Stack);
        net.Rheostat(bassBottom, StageNetlist.Ground, new(25e3, PotTaper.Audio), PotRole.Middle);
        net.Capacitor(drive, trebleTop, 250e-12, CapacitorRole.Stack);
        var output = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(trebleBottom, StageNetlist.Ground, 2.2e-9, CapacitorRole.Stack);
        net.Resistor(bass, output, 100e3);
        return output;
    }

    /// <summary>
    ///     V1B and the clean channel to V3B's grid: the clean James stack (4.7 nF and 22 nF on the bass pot), 10 nF and
    ///     470k (330 pF across it with the Presence switch in) over 220k, Volume 2 (1M) with 82 pF from its wiper to
    ///     ground.
    /// </summary>
    static StageNetlist CleanInput() {
        var net = new StageNetlist();
        var drive = InputStage(net);
        var (coupled, volumeTop) = (net.Node(), net.Node());
        var stack = JamesStack(net, drive, 4.7e-9, 22e-9);
        net.Capacitor(stack, coupled, 10e-9);
        net.Resistor(coupled, volumeTop, 470e3);
        net.SwitchedCapacitor(coupled, volumeTop, 330e-12, SwitchRole.Switch2);
        net.Resistor(volumeTop, StageNetlist.Ground, 220e3);
        var grid = net.Pot(volumeTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control2);
        net.Capacitor(grid, StageNetlist.Ground, 82e-12);
        net.Output = grid;
        return net;
    }

    /// <summary>V3B (220k, 1.5k unbypassed); 47 nF and 2.2M, the 180k/33k pad to the send.</summary>
    static StageNetlist CleanStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, output, send) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, Rail);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, 47e-9);
        net.Resistor(output, StageNetlist.Ground, 2.2e6);
        net.Resistor(output, send, 180e3);
        net.Resistor(send, StageNetlist.Ground, 33e3);
        net.Output = send;
        return net;
    }

    /// <summary>
    ///     V3A's plate to the inverter's driven grid: 47 nF and 100k, with the feedback resistor (360k to the speaker
    ///     tap, AC ground for the preamp) at the grid.
    /// </summary>
    static StageNetlist InverterGrid() {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 360e3);
        net.Output = grid;
        return net;
    }
}
