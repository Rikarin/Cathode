using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Blues Junior III (15 W, 1 × 12): V1B (10k and 1M at the jack; 100k, 1.5k ‖ 47 µF), 0.0022 µF into the 1M
///     volume with 100 pF across its upper part and 130k from its wiper to ground; 10k into V1A (100k, 1.5k with 22 µF
///     over
///     100k, which the <b>Fat</b> switch's J111 grounds) with the Fender stack on its plate (250 pF, 100k slope, 0.022 µF
///     bass and mid caps, the mid cap on the mid pot's wiper; treble 250k linear, bass 250k audio, mid 25k linear); 10k
///     into
///     V2B (100k, 1.5k unbypassed), 0.0022 µF and 430k into the 50k master (1500 pF and the reverb's 100k beside it), 220k
///     and 0.0077 µF into a 12AX7 long-tail pair (91k / 100k, 820 Ω, a 33k + 4.7k tail), two EL84 in fixed bias at 329 V
///     with 100 Ω screen resistors, 75k feedback from the 8 Ω tap into the tail's 4.7k with a fixed 7.5k and 0.1 µF across
///     it. Panel: Volume, Treble, Bass, Middle, Master, Fat.
/// </summary>
/// <remarks>
///     <para>
///         Source: Fender drawing 0057275000 rev D (24 March 2010; assemblies 0057277000 and 0079641000)
///         (thetubestore.com/lib/thetubestore/schematics/Fender/Fender-Blues-Junior-III-Schematic-Rev-D.pdf), with the
///         original drawing 049301 rev D (1998) agreeing on the dry path: every value above, the supply (329 V on 47 µF,
///         2.2k to 307 V for the screens, 10k to 265 V for the inverter, 10k to 238 V for the preamp), the −10.7 V bias,
///         the 1.5k grid stoppers and 220k grid leaks, 0.022 µF coupling caps, 47 pF across the inverter's plates (left
///         out), the AC test points at 1 kHz with 10 mV in and the knobs at noon (512 mV at V1B's plate, 42 mV after the
///         volume, 800 mV at V1A's plate, 162 mV at the treble wiper, 4.24 V at V2B's plate, 114 mV after the master),
///         which <c>FenderUsAmpTests</c> checks the gain staging against. The mid pot's wiring (the mid cap on its wiper,
///         the bass pot on its top) is read zoomed on both drawings. The reverb (at 0, its pot's wiper grounds the 100k
///         return on the master's top, which is kept) is not modelled.
///     </para>
///     <para>
///         Simplified: the driven inverter triode's load is 91k + 9.1k with the output taken between them; here a 91k
///         plate
///         resistor (the same swing, a 9 % lower plate impedance). The tail's 4.7k is in the inverter's DC tail (37.7k)
///         and
///         is also the feedback's shunt. The fixed 7.5k + 0.1 µF across that shunt is the presence network with its 15k
///         pot
///         at noon; the panel hides it. Unverified: the output transformer (Fender 0049969000; about 7k : 8 Ω per a
///         vendor,
///         tubeampdoctor.com), the EL84s' idle current (30 mA; the bias is fixed, not adjustable), the supply's source
///         resistance.
///     </para>
/// </remarks>
public static class BluesJunior {
    const double Preamp = 238;

    /// <summary>A J111's on-resistance.</summary>
    const double Jfet = 30;

    public static AmpDesign Design { get; } = new(
        "Blues Junior",
        [
            InputStage(),
            StackStage(),
            ThirdStage()
        ],
        new(
            "2 x EL84",
            PentodeModel.EL84,
            1,
            329,
            100,
            0.030,
            false,
            0,
            15,
            new LongTailPairDesign(TriodeModel.Ecc83, 265, 91e3, 100e3, 37.7e3, 820),
            22e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 75e3, 4.7e3, new(15e3, PotTaper.Linear), 0.1e-6),
            true,
            new(7e3),
            SpeakerDesign.OpenBack1x12,
            new(40, 47e-6, 2.2e3, 22e-6, 20e3, 22e-6, 3.5e-3, RectifierModel.Silicon),
            InverterInput: new(7.7e-9, 1e6)
        ),
        -6
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Fat"
        }
    };

    /// <summary>
    ///     V1B (1M grid leak behind the jack's 10k; 100k, 1.5k ‖ 47 µF), 0.0022 µF into the volume (100 pF across its upper
    ///     part, 130k from the wiper to ground); the output is the wiper.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail, top) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, top, 2.2e-9);
        var volume = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, volume, 100e-12, CapacitorRole.Bright);
        net.Resistor(volume, StageNetlist.Ground, 130e3);
        net.Output = volume;
        return new("V1B", net, 10e3);
    }

    /// <summary>V1A behind its 10k (the Fat switch on its cathode) and the stack on its plate; the output is the treble wiper.</summary>
    static NetworkStageDesign StackStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail, fat) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, fat, 22e-6);
        net.Resistor(fat, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(fat, StageNetlist.Ground, 0, Jfet, SwitchRole.Switch1);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (top, bottom, slope, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, top, 250e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        net.Capacitor(slope, bottom, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        var mid = net.Pot(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, mid, 22e-9, CapacitorRole.Stack);
        net.Output = treble;
        return new("V1A", net, 10e3);
    }

    /// <summary>
    ///     V2B behind its 10k (100k, 1.5k unbypassed), 0.0022 µF and 430k into the master (1500 pF and the reverb return's
    ///     100k beside it), its wiper's 220k: the output.
    /// </summary>
    static NetworkStageDesign ThirdStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (coupled, top, output) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 2.2e-9);
        net.Resistor(coupled, top, 430e3);
        net.Capacitor(top, StageNetlist.Ground, 1.5e-9);
        net.Resistor(top, StageNetlist.Ground, 100e3);
        var master = net.Pot(top, StageNetlist.Ground, new(50e3, PotTaper.Linear), PotRole.Master);
        net.Resistor(master, output, 220e3);
        net.Output = output;
        return new("V2B", net, 10e3);
    }
}
