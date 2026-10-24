using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Matchless Chieftain (Reverb), from the factory drawing "Cheiftain Reverb" (Mark Sampson, 1994, drawn 1997) with
///     the hand-drawn trace of a real amp and its measured voltages, both in thetubestore's
///     <c>Matchless-Chieftan-Schematic.pdf</c> (https://www.thetubestore.com/matchless-schematics; also schematicheaven
///     <c>newamps/matchless_chieftan.pdf</c>), which also holds a clean redraw of the drawing. V1, both halves of a 12AX7
///     in parallel (68k, 1M, 100k plate, a shared 1.5k unbypassed), couples through 10 nF into the bass network (2.2 nF
///     in parallel with the 1M reverse-log bass pot and 10k in series, 100k from their junction to ground, 220k to ground
///     after it) and the middle network (a bridged T: two 150k in series with 560 pF across them, their junction through
///     10 nF and the 250k log middle pot to ground), straight into V2, again a paralleled 12AX7 (100k plate, 1.5k with
///     25 µF), whose plate carries the treble control (4.7 nF and a 1M log pot to ground: a high cut, Fractal's
///     "preamp presence") and, through 22 nF, the 250k log volume with 180 pF across its upper part. The wiper feeds
///     the effects loop and, through 22 nF, a 12AX7 long-tail pair (100k plates, 1.2k bias, 47k tail, 1M leaks; the
///     reverb return drives its second grid), 22 nF into 220k leaks and 10k stoppers, the brilliance control (4.7 nF and
///     a 500k log pot across the grids, a Vox cut) and the master (a 500k log rheostat across the grids), two EL34
///     each on its own 270 Ω with 250 µF (24 V), 1k screen resistors, no feedback, 5AR4 and a 20 H choke.
/// </summary>
/// <remarks>
///     <para>
///         Measured on the trace: V1 and V2 plates 220 / 219 V, the inverter's plates 280 V and cathodes 80 V, the
///         EL34s' plates 415 V, screens 369 V, cathodes 24 V (about 89 mA each). Where the drawing and the trace
///         differ, the trace (the real amp) is taken: master and brilliance 500k (the drawing has 1M), and the
///         redraw agrees; the drawing's 180 pF across the volume (the redraw has 100 pF; the trace 180 pF). The treble
///         cap is 4.7 nF on the drawing and 5.1 nF on the trace; 4.7 nF is used.
///     </para>
///     <para>
///         Inferred, not drawn: the preamp node (330 V, which puts V1 and V2 at the measured 220 V; behind 22k from
///         the screen node, which the trace puts at 369 V); the inverter's node (355 V, behind 6.8k); the output
///         transformer's primary (4k plate to plate; the trace gives only "WTI 9356", the part the DC-30 trace also
///         names). The bass pot is drawn so that its resistance grows with rotation, which would cut the bass as the
///         knob turns up; it is wired here to take resistance out instead (bass up), as the panel promises. The reverb
///         (its driver, tank and recovery) and the effects loop are left out (the loop's jacks are joined, the reverb
///         level at zero); so are the reverb and loop branches' 4 mA from the screen node. The panel's Brilliance is the
///         block's Cut, which runs the other way: Cut 0 is Brilliance 10.
///     </para>
/// </remarks>
public static class Chieftain {
    /// <summary>V1 and V2's node (inferred, see the remarks).</summary>
    const double PreampSupply = 330;

    /// <summary>The 12AX7's grid-plate capacitance, doubled for the paralleled halves.</summary>
    const double GridPlate = 2 * 1.7e-12;

    public static AmpDesign Design { get; } = new(
        "Chieftain",
        [
            new NetworkStageDesign("V1", FirstStage(), 68e3),
            new NetworkStageDesign("V2", SecondStage())
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 415,
            ScreenResistor: 1e3,
            IdleCurrent: 0.08,
            CathodeBias: true,
            CathodeBypass: 250e-6,
            RatedPower: 40,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 355, 100e3, 100e3, 47e3, 1.2e3),
            CouplingCap: 22e-9,
            GridLeak: 220e3,
            GridStopper: 10e3,
            CutCap: 4.7e-9,
            CutPot: new(500e3, PotTaper.ReverseAudio),
            Feedback: new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: false,
            Transformer: new(4e3),
            Speaker: SpeakerDesign.OpenBack2x12,
            // The plates on the rectifier's 33 µF (the trace: B+ from the rectifier's pin 8); the 20 H choke (about 200 Ω)
            // and R35 1k to the screens' node (33 µF), which also feeds the inverter (6.8k), the reverb (47k) and V1/V2
            // (22k): about 8 mA in all, drawn here through the inverter's 6.8k.
            PowerSupply: new(80, 33e-6, 1.2e3, 33e-6, 6.8e3, 22e-6, 8e-3, RectifierModel.GZ34),
            SeparateCathodes: true,
            InverterInput: new(22e-9, 1e6),
            ShuntMaster: new Pot(500e3, PotTaper.Audio),
            CathodeResistor: 135 // 270 Ω per tube: half of each side's, as the power amp counts it
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Bright = null
        }
    };

    /// <summary>
    ///     V1: both halves of a 12AX7 as one triode of twice the current (100k plate, 1.5k unbypassed, 1M leak).
    /// </summary>
    static StageNetlist FirstStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, PreampSupply);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83.Paralleled(2));
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The bass and middle networks on V1's plate, straight into V2 (paralleled 12AX7: 100k, 1.5k with 25 µF, its
    ///     grid-plate capacitance), the treble cut on V2's plate (4.7 nF and a 1M log rheostat to ground) and the volume
    ///     (22 nF into 250k log, 180 pF across its upper part). In one circuit with V2: the networks' impedance is
    ///     reactive, and a cut at V2's grid was 22 dB from the reference.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (coupled, bass, joined) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 10e-9);
        net.Capacitor(coupled, joined, 2.2e-9, CapacitorRole.Stack);
        net.Rheostat(coupled, bass, new(1e6, PotTaper.ReverseAudio), PotRole.Bass, falling: true);
        net.Resistor(bass, joined, 10e3);
        net.Resistor(bass, StageNetlist.Ground, 100e3);
        net.Resistor(joined, StageNetlist.Ground, 220e3);
        var (middle, grid, notch) = (net.Node(), net.Node(), net.Node());
        net.Resistor(joined, middle, 150e3);
        net.Resistor(middle, grid, 150e3);
        net.Capacitor(joined, grid, 560e-12, CapacitorRole.Stack);
        net.Capacitor(middle, notch, 10e-9, CapacitorRole.Stack);
        net.Rheostat(notch, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Middle);
        var (supply, plate, cathode, treble, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, PreampSupply);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83.Paralleled(2));
        net.Capacitor(grid, plate, GridPlate);
        net.Capacitor(plate, treble, 4.7e-9);
        net.Rheostat(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(plate, top, 22e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 180e-12, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }
}
