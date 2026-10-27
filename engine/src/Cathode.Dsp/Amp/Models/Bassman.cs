using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Bassman 5F6-A (tweed, about 45 W): the Bright or Normal channel's half of V1 (12AY7, 100k plates, a shared
///     820 Ω ‖ 250 µF cathode), the channels' 0.02 µF caps and 1M volumes (the Bright channel's with a fixed 100 pF), the
///     270k mixing resistors into V2A (12AX7, 100k, 820 Ω unbypassed), the V2B cathode follower DC-coupled to it (100k)
///     driving the Bassman stack (Yeh and Smith's values: 250 pF, 56k slope, 0.02 µF bass and mid; treble 250k, bass 1M,
///     mid 25k), whose treble wiper drives the inverter's 1M grid resistor directly (the amp has no master), a 12AX7
///     long-tail pair (82k/100k, 470 Ω, 10k tail over the 5k presence pot), two 5881 (6L6 curves) in fixed bias at
///     430 V on a GZ34, a 4k : 2 Ω transformer and 27k feedback
///     from the 2 Ω tap into the presence pot, which is the tail's shunt with the 0.1 µF on its wiper. Panel: Bright and
///     Normal volumes, Bass, Middle, Treble, Presence;
///     <b>Normal Input</b> plays the Normal channel instead of the Bright one. <see cref="Jumped" /> is the same amp with
///     the classic jumper driving both channels.
/// </summary>
/// <remarks>
///     <para>
///         Sources: AmpBooks "Fender Bassman 5F6-A" (12AY7 at 325 V with 820 Ω ‖ 250 µF and 34k stoppers; 0.02 µF; 1M
///         volume; V2A 100k / 820 Ω unbypassed; the inverter's 470 Ω, 10k + 5k tail, 82k plates, 0.1 µF out; 470 Ω
///         screens;
///         GZ34); Rob Robinette's 5F6-A page (100 pF bright cap, 270k mixers, 100k cathode follower, 2 × 5881 at −48 V,
///         220k grid leaks, B+ 442 V unloaded and 411 V loaded, 4.7k to the preamp, 27k feedback from the 2 Ω tap, a 5k
///         presence pot); Yeh and Smith, DAFx-06, for the stack (the schematic's 56k slope and 0.02 µF bass cap; Robinette
///         says production used 100k and 0.1 µF: this model takes the schematic, which is also the published
///         reference).
///     </para>
///     <para>
///         With one input played, the other half of V1 has its grid at rest, and with its cathode bypassed (250 µF, a
///         corner near 1 Hz) it is linear: its plate is its r_p in parallel with its 100k, about 21k (behind its coupling
///         cap, from its own DC plate voltage), the load that the channel not played puts on the one played through the
///         volumes. The played half gets the shared cathode's per-tube equivalent (1.64k ‖ 125 µF). Jumped, both halves
///         are
///         driven and the stage holds both triodes exactly, at about 5 % more CPU.
///     </para>
///     <para>
///         Unverified: the pot tapers (treble and mid linear, bass and volumes audio, as on the JTM45 drawing that copied
///         this circuit), the presence cap (0.1 µF), the output transformer (about 4k), the 5881 idle current (35 mA), no
///         grid stoppers, the inverter's supply (330 V), and the supply's source resistance and reservoir. Jumpered, each
///         grid
///         sees about 68k (the two input jacks' resistors in series).
///     </para>
///     <para>
///         The stack's output is the treble wiper, loaded by the inverter's 1M grid resistor: the amp has no master.
///         (The model first had that 1M as a pot on the Master role, which the hidden panel knob left at its default of
///         5, halving the drive into the inverter, with Master Location and Master Trim acting on an amp without a
///         master.)
///     </para>
/// </remarks>
public static class Bassman {
    /// <summary>A resistor switched out, and one switched in as a wire.</summary>
    const double Open = 1e12;

    const double Wire = 1;

    /// <summary>The idle V1 half's plate: a 12AY7's r_p (about 27k at 1.65 mA) in parallel with its 100k.</summary>
    const double IdlePlate = 21e3;

    /// <summary>The Bassman 5F6-A stack (Yeh and Smith, DAFx-06).</summary>
    public static ToneStackDesign Stack { get; } = new(
        "Bassman 5F6-A",
        250e-12,
        56e3,
        20e-9,
        20e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    public static AmpDesign Design { get; } = Build("Bassman 5F6-A", InputStage(), "Normal Input");

    /// <summary>The Bassman with its channels jumpered: the guitar into the Bright channel, a cable on to the Normal one.</summary>
    public static AmpDesign Jumped { get; } = Build("Bassman 5F6-A Jumped", JumpedStage(), null);

    static AmpDesign Build(string name, NetworkStageDesign input, string? channelSwitch) =>
        new(
            name,
            [
                input,
                SecondStage(),
                new ToneStackStageDesign(
                    "V2B",
                    TriodeModel.Ecc83,
                    StackDriver.CathodeFollower,
                    325,
                    100e3,
                    Stack,
                    new(1e6, PotTaper.Linear), // the inverter's grid resistor: no knob, no master
                    null
                )
            ],
            new(
                "2 x 5881",
                PentodeModel.SixL6GC,
                1,
                430,
                470,
                0.035,
                false,
                0,
                45,
                new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 470),
                0.1e-6,
                220e3,
                1,
                0,
                new(1e6, PotTaper.Linear),
                new(0.5, 27e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6) { PresenceIsShunt = true },
                true,
                new(4e3),
                SpeakerDesign.OpenBack4x10,
                new(100, 32e-6, 100, 16e-6, 4.7e3, 16e-6, 6e-3, RectifierModel.GZ34)
            ),
            -11
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

    /// <summary>The coupling caps, volumes (the Bright one with its 100 pF) and 270k mixing resistors; returns the mix node.</summary>
    static int Volumes(StageNetlist net, int brightPlate, int normalPlate) {
        var (brightTop, normalTop, mix) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(brightPlate, brightTop, 20e-9);
        var bright = net.Pot(brightTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(brightTop, bright, 100e-12, CapacitorRole.Bright);
        net.Resistor(bright, mix, 270e3);
        net.Capacitor(normalPlate, normalTop, 20e-9);
        var normal = net.Pot(normalTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(normal, mix, 270e3);
        return mix;
    }

    /// <summary>
    ///     The played half of V1 (Bright, or Normal with <see cref="SwitchRole.Switch1" />) routed to its channel, the idle
    ///     half's plate as <see cref="IdlePlate" />, the coupling caps, volumes and 270k mixing resistors; the output is V2A's
    ///     grid (the mixing node).
    /// </summary>
    internal static NetworkStageDesign InputStage(double supply = 325) {
        var net = new StageNetlist();
        var (plate, cathode, rail, idle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 125e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.TwelveAY7);
        net.Supply(idle, IdlePlateVoltage(supply, 2 * 820));

        var (bright, normal) = (net.Node(), net.Node());
        net.SwitchedResistor(plate, bright, Wire, Open, SwitchRole.Switch1);
        net.SwitchedResistor(idle, bright, Open, IdlePlate, SwitchRole.Switch1);
        net.SwitchedResistor(plate, normal, Open, Wire, SwitchRole.Switch1);
        net.SwitchedResistor(idle, normal, IdlePlate, Open, SwitchRole.Switch1);
        net.Output = Volumes(net, bright, normal);
        return new("V1", net, 34e3);
    }

    /// <summary>Both V1 halves on their shared cathode, both grids driven (68k each); the rest as <see cref="InputStage" />.</summary>
    internal static NetworkStageDesign JumpedStage(double supply = 325) {
        var net = new StageNetlist();
        var (normalGrid, brightGrid) = (net.Node(), net.Node());
        net.Resistor(net.Input, brightGrid, 68e3);
        net.Resistor(net.Input, normalGrid, 68e3);
        net.Resistor(brightGrid, StageNetlist.Ground, 1e6);
        net.Resistor(normalGrid, StageNetlist.Ground, 1e6);

        var (normalPlate, brightPlate, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, normalPlate, 100e3);
        net.Resistor(rail, brightPlate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 250e-6);
        net.Triode(brightPlate, brightGrid, cathode, TriodeModel.TwelveAY7);
        net.Triode(normalPlate, normalGrid, cathode, TriodeModel.TwelveAY7);
        net.Output = Volumes(net, brightPlate, normalPlate);
        return new("V1", net);
    }

    /// <summary>
    ///     V2A: 100k, 820 Ω unbypassed, with its grid-plate capacitance, whose Miller multiple against the 270k mixing
    ///     resistors rolls off the top.
    /// </summary>
    internal static NetworkStageDesign SecondStage(double supply = 325) {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Output = plate;
        return new("V2A", net);
    }

    /// <summary>The idle half's DC plate voltage (its grid at rest), so that switching channels leaves the caps charged.</summary>
    internal static double IdlePlateVoltage(double supply, double cathode) =>
        OperatingPoint.CommonCathode(TriodeModel.TwelveAY7, 0, supply, 100e3, cathode).Plate;
}
