using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark IIC+, lead channel: V1A (150k, 1.5k ‖ 0.47 µF, with a 15 µF bypass through 15k that the Bass
///     Shift pull shorts) driving the Fender-topology stack directly (100k slope, 250 pF treble cap, 750 pF more with
///     Treble Shift, 0.1 µF, 47 nF; 250k treble and bass, 10k middle) and Volume 1 (1M audio, 180 pF pull-bright);
///     V1B; the lead drive (680k into 1M audio) into the V3A cascade (82k, 120 pF grid-to-cathode), a 270k/68k divider
///     into the V3B lead stage (270k plate with 1 nF across it, 3.3k with 0.22 µF through 15k, shorted by the
///     lead-bright pull), a 220k ‖ 250 pF / 100k ‖ 500 pF divider into V2B (1.5k unbypassed), the lead master shunting
///     the signal into a 150k/4.3k pad, the V4A recovery stage (120k, 1k ‖ 0.47 µF, 15 µF through 15k, shorted by the
///     deep pull), the master, the five-band graphic EQ, a long-tail pair and Simul-Class power (two 6L6GC in pentode
///     beside two triode-strapped EL34).
/// </summary>
/// <remarks>
///     <para>
///         Sources: the Yahiake LiveSPICE redraw "Mark IIC+(++) Preamp_GEQ" (every preamp value and the wiring, in its
///         stock, non-"++" positions), the annotated redraw on boogieforum (docs/06 §3.4), and the Music Electronics
///         Forum thread on the 120 pF grid-to-cathode cap. Values from a redraw, not the factory scan.
///     </para>
///     <para>
///         Guesses, marked: the redraw draws V3A's 2.2 µF cathode cap unconnected; it is connected here (a cascade
///         stage this hot is bypassed on every Mark lead circuit described in text). The rhythm path's 3.3M feed into
///         V2B's grid, live in the lead mode too, is left out (about -33 dB under the lead signal). The graphic EQ is
///         the behavioural constant-Q slot (<see cref="EqualizerStageDesign" />), not the LC network, whose topology
///         the redraws disagree on.
///     </para>
///     <para>
///         Power amp, read from the factory scan's power page (el34world <c>Boogie_mkiic_plus.pdf</c>, sheet 2) and
///         the boogieforum redraw's (#102): inverter 82.5k/90.9k with 470 Ω bias and a 22k tail, 0.1 µF into 220k
///         grid leaks, 2.2k grid stoppers, 470 Ω (2 W) screens. The presence is a 250k rheostat in series with the
///         feedback (<see cref="FeedbackDesign.Series" />): 8 Ω tap, the rheostat, 3.3k and 56k ‖ 4.7 nF into the
///         tail's foot, and 3.3k ‖ 47 nF over 1.5k to ground. The factory page reads 3.3k after the rheostat, as the
///         Mark IV's sheet prints it; the redraw has 1.5k there. The redraw draws the 0.1 µF beside the 47 nF; on the
///         factory page, as on the Mark IV's sheet, it is the second inverter grid's return to the tail's foot,
///         outside the divider. With the rheostat at 0 the network feeds back 31 % of the treble (7.5 % of the lows);
///         more presence is more resistance, less feedback everywhere and less of the 4.7 nF's treble feedback, so the
///         knob is brighter turned up. Guessed: the rheostat's taper (linear; the drawings do not say).
///         On the Simul-Class the triode-strapped EL34 pair is fed through about 220k with its own grid leak (less
///         drive than the pentodes); here both pairs share the pentodes' grid network. Guessed: B+ 465 V, 40 mA
///         idle, the 2.5k primary.
///     </para>
///     <para>
///         Panel: the real amp's two Shift pulls (Treble, on the treble pot: the 750 pF; Bass, on the bass pot: V1A's
///         15 µF bypass) are Switch 1 and Switch 3. Five pulls on four switches: the Lead Master's Bright pull (V3B's
///         0.22 µF bypass) shares the Bright switch with Volume 1's, as the Mark IV's model lumps them.
///     </para>
/// </remarks>
public static class MarkIIC {
    public static AmpDesign Design { get; } = new(
        "Mark IIC+",
        [
            new NetworkStageDesign("V1A", InputAndStack()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 408, 100e3, 1.5e3, 15e-6),
            new NetworkStageDesign("V3A", LeadDrive()),
            new NetworkStageDesign("V3B", LeadStage()),
            new NetworkStageDesign("V2B", LeadReturn()),
            new NetworkStageDesign("V4A", Recovery()),
            new CouplingStageDesign("Master", 47e-9, new(1e6, PotTaper.Audio), PotRole.Master, 15e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        new(
            "2 x 6L6GC + 2 x EL34 (Simul-Class)",
            PentodeModel.SixL6GC,
            1,
            465,
            470,
            0.04,
            false,
            0,
            75,
            new LongTailPairDesign(TriodeModel.Ecc83, 427, 82.5e3, 90.9e3, 22e3, 470),
            0.1e-6,
            220e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 3.3e3, 1.5e3, new(250e3, PotTaper.Linear), 4.7e-9) { Series = new(56e3, 3.3e3, 47e-9) },
            true,
            new(2.5e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            new(30, 120e-6, 100, 47e-6, 10e3, 22e-6, 6e-3),
            Strapped: PentodeModel.EL34,
            StrappedPerSide: 1
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Depth = null,
            Cut = null,
            Control1 = "Lead Drive",
            Control2 = "Lead Master",
            Switch1 = "Treble Shift",
            Switch2 = "Deep",
            Switch3 = "Bass Shift",
            Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     V1A with the stack on its plate and Volume 1 on the treble wiper: 1M grid leak; 150k plate; 1.5k ‖ 0.47 µF,
    ///     and 15 µF through 15k (shorted by Bass Shift); the stack (750 pF more with Treble Shift); Volume 1 with 180
    ///     pF across its upper half (Bright).
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, shift, slope, trebleTop, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 408);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, shift, 15e-6);
        net.SwitchedResistor(shift, StageNetlist.Ground, 15e3, 1, SwitchRole.Switch3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var treble = MesaParts.Stack(
            net,
            plate,
            trebleTop,
            net.Node(),
            slope,
            middle,
            250e-12,
            750e-12,
            SwitchRole.Switch1,
            100e3,
            0.1e-6,
            47e-9,
            new(250e3, PotTaper.Linear),
            new(250e3, PotTaper.Audio),
            new(10e3, PotTaper.Linear)
        );

        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(treble, volume, 180e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Output = volume;
        return net;
    }

    /// <summary>
    ///     The lead drive into V3A (<see cref="MesaParts.LeadDrive" />): 20 nF and 680k into the 1M audio lead drive,
    ///     its wiper 470k to ground and 10k to the grid with 120 pF to the cathode.
    /// </summary>
    static StageNetlist LeadDrive() {
        var net = new StageNetlist();
        net.Output = MesaParts.LeadDrive(
            net,
            net.Input,
            680e3,
            new(1e6, PotTaper.Audio),
            PotRole.Control1,
            425,
            20e-9,
            gridStopper: 10e3
        );
        return net;
    }

    /// <summary>
    ///     V3B (<see cref="MesaParts.LeadStage" />): 22 nF, 270k into 68k ‖ 1 nF, 10k to the grid; 270k plate with 1 nF
    ///     across it; 3.3k with 0.22 µF through 15k (shorted by the lead-bright pull).
    /// </summary>
    static StageNetlist LeadStage() {
        var net = new StageNetlist();
        net.Output = MesaParts.LeadStage(
            net,
            net.Input,
            425,
            SwitchRole.Bright,
            15e3,
            gridStopper: 10e3,
            plateCap: 1e-9
        );
        return net;
    }

    /// <summary>
    ///     The lead signal back into V2B: 47 nF, 220k ‖ 250 pF into 100k ‖ 500 pF, with V2B's own 680k ‖ 47 pF grid
    ///     network; 100k plate, 1.5k unbypassed.
    /// </summary>
    static StageNetlist LeadReturn() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        MesaParts.LeadDivider(net, net.Input, coupled, grid, 100e3 * 680e3 / (100e3 + 680e3), 547e-12);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The lead master and the recovery stage V4A: 47k and 47 nF from V2B, the 250k audio lead master as a shunt to
    ///     ground, 150k into 4.7k ‖ 47k, 120 pF grid-to-cathode; 120k plate, 1k ‖ 0.47 µF and 15 µF through 15k
    ///     (shorted by the deep pull).
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (series, master, grid, supply, plate, cathode, deep) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, series, 47e3);
        net.Capacitor(series, master, 47e-9);
        net.Rheostat(master, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        net.Resistor(master, grid, 150e3);
        net.Resistor(grid, StageNetlist.Ground, 4.7e3 * 47e3 / (4.7e3 + 47e3));
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, deep, 15e-6);
        net.SwitchedResistor(deep, StageNetlist.Ground, 15e3, 1, SwitchRole.Switch2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }
}
