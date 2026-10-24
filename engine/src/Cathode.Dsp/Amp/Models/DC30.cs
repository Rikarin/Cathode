using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Matchless DC-30, from the hand-drawn trace of an early one (thetubestore "Matchless-DC30-Old-Schematic.pdf":
///     channel 1, channel 2, inverter and power amp, supply with measured voltages): channel 1, or (Switch 1) channel 2.
///     Channel 1: both halves of a 12AX7 in parallel (220k plate from 319 V, 1.5k with 25 µF, 68k input, 1M), 560 pF into
///     a 500k log volume with 180 pF across it, a 12AX7 gain stage (100k from 317 V, 1.5k with 25 µF) DC-coupled to a
///     cathode follower (56k) driving a treble/bass network (56 pF to a 1M log treble pot, 100k, 22 nF from the treble
///     pot's far end to the slope node, 22 nF from there to ground, the 1M log bass pot as a rheostat from the treble pot
///     to ground: a TMB stack with its middle at zero), the treble wiper through 220k into 100k. Channel 2: an EF86
///     (330k plate, 2.2M screen resistor with 0.1 µF, 2.2k with 25 µF, from 320 V) into a six-way rotary of coupling
///     caps (the Tone knob: 330 pF (the trace reads 360; AmpBooks 330), 560 pF,
///     1.2 nF, 2.2 nF, 4.7 nF, 10 nF), a 1M log volume with 180 pF across it and 470k from its wiper to ground. Each
///     channel drives its own grid of the 12AX7 long-tail pair (0.01 µF, 1M leaks, 1.2k bias, 47k tail, 100k plates,
///     308 V), 0.047 µF into 220k leaks and 1.5k stoppers, the cut (250k log and 2.2 nF) and the master (1M log as a
///     rheostat) across the grids, four EL84 on a shared 62 Ω with 250 µF, 100 Ω screens, no feedback, GZ34 (or two
///     5V4) into 33 µF, a choke and 33 µF; plates 350 V, screens 338 V.
/// </summary>
/// <remarks>
///     A trace, not a factory drawing, and hand-written: the values below are read by eye and some are guesses. The
///     top-boost slope resistor and the shunt after the 220k read as 100k; the cathode resistors as 1.5k. The bass pot's
///     wiper is drawn tied to its ground end (with the AC30's wiring and no 10k the knob would do nothing until 9). The
///     power
///     tubes' cathode network sits on the Hi/Lo switch: the Hi position is taken as the shared 62 Ω with 250 µF (Lo
///     adds the 68 Ω). The master's pull switch is taken as closed (in circuit); at 10 the master's 1M barely loads
///     the inverter. The tone rotary's order (330 pF at 0, the 10 nF at 10) and the output transformer's primary
///     (4k plate-to-plate, as the AC30's; the trace gives only "WTI 9356") are assumptions; so is the idle channel
///     being out of circuit (each channel has its own inverter grid, so only its coupling cap grounds the other grid).
///     The 5.1M resistors that keep the rotary's idle caps charged are left out.
/// </remarks>
public static class DC30 {
    public static AmpDesign Design { get; } = new(
        "DC-30",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [Parallel(), GainStage(), Stack()],
                [Ef86()]
            )
        ],
        new(
            "4 x EL84",
            PentodeModel.EL84,
            2,
            350,
            100,
            0.04,
            true,
            250e-6,
            30,
            new LongTailPairDesign(TriodeModel.Ecc83, 308, 100e3, 100e3, 47e3, 1.2e3),
            0.047e-6,
            220e3,
            1.5e3,
            2.2e-9,
            new(250e3, PotTaper.ReverseAudio), // log track, its resistance falling fastest as the cut comes in
            new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 90),
            false,
            new(4e3),
            SpeakerDesign.OpenBack2x12,
            // The 22k droppers from the 338 V screen node to the stages' 308-320 V: about 1 mA as one node.
            new(60, 33e-6, 120, 33e-6, 22e3, 33e-6, 1e-3, RectifierModel.GZ34),
            InverterInput: new(0.01e-6, 1e6),
            ShuntMaster: new Pot(1e6, PotTaper.Audio),
            CathodeResistor: 62
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Bright = null,
            Switch1 = "Channel 2",
            Control1 = "Tone (Ch 2)"
        }
    };

    /// <summary>
    ///     Channel 1's input: both halves of a 12AX7 in parallel, 220k plate, a shared 1.5k with 25 µF, 1M leak. The
    ///     pair is one triode of twice the current (exact for matched halves); the Preamp Tube setting leaves it alone.
    /// </summary>
    static NetworkStageDesign Parallel() {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 319);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83.Paralleled(2)); // as one: half the cost of two
        net.Output = plate;
        return new("V1", net, 68e3);
    }

    /// <summary>Channel 1's volume (560 pF in, 500k log, 180 pF across) and the gain stage (100k, 1.5k with 25 µF).</summary>
    static NetworkStageDesign GainStage() {
        var net = new StageNetlist();
        var (top, plate, cathode, supply) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 560e-12);
        var wiper = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 180e-12, CapacitorRole.Bright);
        net.Supply(supply, 317);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, wiper, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return new("V2A", net);
    }

    /// <summary>The cathode follower (56k) and the treble/bass network, the treble wiper through 220k into 100k.</summary>
    static NetworkStageDesign Stack() {
        var net = new StageNetlist();
        var (supply, cathode, output) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, 317);
        net.Resistor(cathode, StageNetlist.Ground, 56e3);
        net.Triode(supply, net.Input, cathode, TriodeModel.Ecc83);
        var (trebleTop, trebleBottom, slope) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(cathode, trebleTop, 56e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Resistor(cathode, slope, 100e3);
        net.Capacitor(trebleBottom, slope, 22e-9, CapacitorRole.Stack);
        net.Capacitor(slope, StageNetlist.Ground, 22e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Resistor(treble, output, 220e3);
        net.Resistor(output, StageNetlist.Ground, 100e3);
        net.Output = output;
        return new("V2B", net);
    }

    /// <summary>Channel 2: the EF86, the tone rotary (Control 1), the 1M log volume with 180 pF, 470k from its wiper.</summary>
    static NetworkStageDesign Ef86() {
        var net = new StageNetlist();
        var (supply, plate, screen, cathode, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 320);
        net.Resistor(supply, plate, 330e3);
        net.Resistor(supply, screen, 2.2e6);
        net.Capacitor(screen, StageNetlist.Ground, 0.1e-6);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Pentode(plate, screen, net.Input, cathode, PentodeModel.EF86);
        net.SelectedCapacitor(plate, top, [330e-12, 560e-12, 1.2e-9, 2.2e-9, 4.7e-9, 10.18e-9], PotRole.Control1);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 180e-12, CapacitorRole.Bright);
        net.Resistor(wiper, StageNetlist.Ground, 470e3);
        net.Output = wiper;
        return new("EF86", net, 68e3);
    }
}
