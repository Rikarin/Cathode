using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Hot Rod Deluxe III (40 W, 1 × 12), from Fender's service drawing 0079604000 rev B (15 June 2010, the
///     "Hot Rod Deluxe/DeVille III" combined schematic, Deluxe values): V1A (100k, 1.5k ‖ 47 µF) feeding both the
///     Normal channel's 0.022 µF and 270k into the 250k Volume and the Drive channel's 560 pF and 240k into the 250k
///     Drive pot; a relay picks a wiper into V1B (10k stopper, 56k to ground; 100k, 1.5k ‖ 47 µF), the Fender stack on
///     V1B's plate (250 pF, 130k slope, 0.1 µF bass, 0.022 µF mid; treble 250k linear, bass 250k 15 % audio, mid 25k
///     linear; 220k from the treble pot's bottom to ground), 100k into V2A (100k, 1.5k). On the Normal channel V2A's
///     0.047 µF and 130k go straight to the effects loop; on the Drive channel they go through 100k into V2B (100k,
///     1.5k unbypassed), its 100k and 0.047 µF into the 100k Master (1 nF across it) and 47k to the effects loop. After
///     the loop, 47k and 220k into the 12AX7 long-tail pair (82k/100k, 470 Ω, 6.8k tail; 0.022 µF and 1M in), two 6L6GC
///     in fixed bias (30 mA each) at 431 V on a silicon bridge, 47k feedback from the 4 Ω tap into 4.7k with the 25k
///     presence pot and 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         Panel: Drive (the Drive channel's gain, on the Gain knob), Volume (the Normal channel's), Bass, Middle,
///         Treble, Master (Drive channel only, as on the amp), Presence; <b>Normal Ch</b> selects the Normal channel
///         (the Drive channel is the default, so that Gain and Master act on it); <b>Bright</b> is the Normal channel's
///         bright switch (it bypasses V2A's cathode with 0.068 µF, a treble boost; the relay disconnects it on the
///         Drive channel);
///         <b>More Drive</b> (Drive channel only) switches J111s that bypass V2A's cathode with 1 µF and V2B's with 22
///         µF (the drawing: "More Drive adds 12.7 dB of gain to the Drive mode"). On the Drive channel a relay grounds
///         V2A's grid through 250 pF.
///     </para>
///     <para>
///         Source: "Service diagram, combined (schematic) Hot Rod Deluxe/DeVille III", drawing 0079604000 rev B,
///         sheets 1–2
///         (thetubestore.com/lib/thetubestore/schematics/Fender/Fender-Hotrod-Deluxe-III-Schematic-Rev-B.pdf): every
///         value above, the supply (431 V B+ on 47 µF, a choke to 430 V on 22 µF, 4.7k to 392 V for the inverter, 10k
///         to 345 V for the preamp), the Deluxe's 0.005 µF … 560 pF and 1000 pF variants, the 4 Ω feedback tap, the
///         bias (60 mV across 1 Ω for both tubes). The test points (1 kHz, 4 mV in, Drive mode, controls at noon: 180
///         mV at V1A's plate, 13 mV at V1B's grid, 1.0 V at V2B's grid, 23.6 V at its plate, 1.45 V into the loop)
///         check the gain staging (<c>FenderUsAmpTests</c>). The output transformer (4.25k : 4/8 Ω) is from the
///         replacement listings of Fender part 005-0438 (amplifiedparts.com); the original 1996 Hot Rod Deluxe (drawing
///         050397) has the same topology.
///     </para>
///     <para>
///         The pots' tapers are from Fender's part numbers: the Volume, Drive and Bass "250k15A" (15 % at half
///         rotation), the Master "100k30A" (30 %), the Treble and Middle "B" (linear); the Presence's "25k30C" is taken
///         as linear. Simplified: the effects loop's op-amps (a 47k / 2.7k divider into a TL072 follower, then a
///         non-inverting TL072 stage of gain 19.9 and 47k out) are a gain of about 1.06 and are a wire here, so the
///         return network (0.47 µF, 47k) hangs on the send, which carries its 49.7k divider: about 0.5 dB apart. The
///         reverb (at 0: its pot's wiper grounds the 470k mixing resistor, which is kept) and the reverb send's 470 pF
///         / 1M load are left out, as are the 47 pF across the inverter's plates and the two 15 V zeners (1N5245B) that
///         clamp the Drive channel's output at about ±16 V, which only the Master near full on More Drive reaches (a
///         diode clipper beside V2B doubled that stage's cost). The J111's on-resistance (30 Ω) is the datasheet
///         maximum.
///     </para>
/// </remarks>
public static class HotRodDeluxe {
    /// <summary>A switched-out element, and a closed relay contact.</summary>
    const double Open = 1e9;

    const double Wire = 1;

    /// <summary>A capacitor switched out (open at audio).</summary>
    const double Absent = 1e-15;

    /// <summary>A J111's on-resistance.</summary>
    const double Jfet = 30;

    /// <summary>The preamp node (X) and the inverter's node (Y).</summary>
    const double Preamp = 345;

    /// <summary>The effects send's divider (47k + 2.7k), which loads the preamp's output.</summary>
    const double SendLoad = 49.7e3;

    public static AmpDesign Design { get; } = new(
        "Hot Rod Deluxe",
        [
            InputStage(),
            StackStage(),
            ThirdStage(),
            new ChannelSwitchStageDesign("Channel", SwitchRole.Switch1, [DriveStage()], [NormalOutput()])
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            431,
            470,
            0.030,
            false,
            0,
            40,
            new LongTailPairDesign(TriodeModel.Ecc83, 392, 82e3, 100e3, 6.8e3, 470),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 47e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6),
            true,
            new(4.25e3),
            SpeakerDesign.OpenBack1x12,
            new(40, 47e-6, 100, 22e-6, 14.7e3, 22e-6, 5.8e-3, RectifierModel.Silicon),
            InverterInput: new(22e-9, 1e6)
        ),
        -6
    ) {
        Panel = new() {
            Gain = "Drive",
            Control1 = "Volume",
            Depth = null,
            Cut = null,
            Switch1 = "Normal Ch",
            Switch2 = "More Drive"
        }
    };

    /// <summary>
    ///     V1A with both channels' networks on its plate: 0.022 µF and 270k into the Volume, 560 pF and 240k into the
    ///     Drive pot; the relay (<see cref="SwitchRole.Switch1" />: on is the Normal channel) takes one wiper to the
    ///     output, which has the 56k to ground.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var (normal, normalTop) = (net.Node(), net.Node());
        net.Capacitor(plate, normal, 22e-9);
        net.Resistor(normal, normalTop, 270e3);
        var volume = net.Pot(normalTop, StageNetlist.Ground, new(250e3, PotTaper.Audio, 0.15), PotRole.Control1);

        var (drive, driveTop) = (net.Node(), net.Node());
        net.Capacitor(plate, drive, 560e-12);
        net.Resistor(drive, driveTop, 240e3);
        var gain = net.Pot(driveTop, StageNetlist.Ground, new(250e3, PotTaper.Audio, 0.15), PotRole.Gain);

        var output = net.Node();
        net.SwitchedResistor(volume, output, Open, Wire, SwitchRole.Switch1);
        net.SwitchedResistor(gain, output, Wire, Open, SwitchRole.Switch1);
        net.Resistor(output, StageNetlist.Ground, 56e3);
        net.Output = output;
        return new("V1A", net, 34e3);
    }

    /// <summary>
    ///     V1B (10k stopper, 100k, 1.5k ‖ 47 µF) and the stack on its plate; the output is the treble wiper.
    /// </summary>
    static NetworkStageDesign StackStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (top, bottom, slope, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, top, 250e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 130e3);
        net.Capacitor(slope, bottom, 100e-9, CapacitorRole.Stack);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bottom, middle, new(250e3, PotTaper.Audio, 0.15), PotRole.Bass);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(bottom, StageNetlist.Ground, 220e3);
        net.Output = treble;
        return new("V1B", net, 10e3);
    }

    /// <summary>
    ///     V2A behind its 100k: 250 pF from the grid to ground on the Drive channel; 1.5k with 0.068 µF over 100k,
    ///     which the bright switch grounds on the Normal channel only, and 1 µF over 100k, which the More Drive JFET
    ///     grounds on the Drive channel only. The output is the plate.
    /// </summary>
    static NetworkStageDesign ThirdStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.SwitchedCapacitor(net.Input, StageNetlist.Ground, Absent, SwitchRole.Switch1, off: 250e-12);

        // Bright: S1 grounds the 0.068 µF through the relay's contact, which the Drive channel opens (1M instead).
        var (bright, contact) = (net.Node(), net.Node());
        net.Capacitor(cathode, bright, 68e-9);
        net.Resistor(bright, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(bright, contact, Open, Wire, SwitchRole.Bright);
        net.SwitchedResistor(contact, StageNetlist.Ground, 1e6, Wire, SwitchRole.Switch1);

        // More Drive: Q1 grounds the 1 µF; its gate is held off on the Normal channel.
        var (more, jfet) = (net.Node(), net.Node());
        net.Capacitor(cathode, more, 1e-6);
        net.Resistor(more, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(more, jfet, Open, Jfet, SwitchRole.Switch2);
        net.SwitchedResistor(jfet, StageNetlist.Ground, Wire, Open, SwitchRole.Switch1);
        net.Output = plate;
        return new("V2A", net);
    }

    /// <summary>
    ///     The Normal channel after V2A: 0.047 µF, 1M to ground, 130k to the loop and on to the inverter.
    /// </summary>
    static NetworkStageDesign NormalOutput() {
        var net = new StageNetlist();
        var (coupled, output) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, output, 130e3);
        net.Output = Loop(net, output);
        return new("Normal", net);
    }

    /// <summary>
    ///     The Drive channel after V2A: 0.047 µF, 1M, 100k into V2B's 220k grid leak; V2B (100k, 1.5k, 22 µF that More
    ///     Drive grounds), 100k and 0.047 µF into the Master (1 nF across it), its wiper through 47k to the loop and on
    ///     to the inverter.
    /// </summary>
    static NetworkStageDesign DriveStage() {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 220e3);

        var (plate, cathode, rail, more) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, more, 22e-6);
        net.Resistor(more, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(more, StageNetlist.Ground, Open, Jfet, SwitchRole.Switch2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, Ab763.GridPlate);

        var (series, top, output) = (net.Node(), net.Node(), net.Node());
        net.Resistor(plate, series, 100e3);
        net.Capacitor(series, top, 47e-9);
        net.Capacitor(top, StageNetlist.Ground, 1e-9);
        var master = net.Pot(top, StageNetlist.Ground, new(100e3, PotTaper.Audio, 0.3), PotRole.Master);
        net.Resistor(master, output, 47e3);
        net.Output = Loop(net, output);
        return new("V2B", net);
    }

    /// <summary>
    ///     From the loop's send (<paramref name="send" />: its 49.7k divider to ground) through the loop, taken as a
    ///     wire (see the type's remarks), its return's 0.47 µF and 47k, and 220k to the mixing node, which has the
    ///     reverb's 470k (its pot at 0) and 47 pF to ground; returns the mixing node, which drives the inverter's 0.022
    ///     µF.
    /// </summary>
    static int Loop(StageNetlist net, int send) {
        net.Resistor(send, StageNetlist.Ground, SendLoad);
        var (coupled, back, mix) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(send, coupled, 0.47e-6);
        net.Resistor(coupled, back, 47e3);
        net.Resistor(back, mix, 220e3);
        net.Resistor(mix, StageNetlist.Ground, 470e3);
        net.Capacitor(mix, StageNetlist.Ground, 47e-12);
        return mix;
    }
}
