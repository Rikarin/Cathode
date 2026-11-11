using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark IIC+, both channels as two types. Lead (<see cref="Design" />): V1A (150k, 1.5k ‖ 0.47 µF, with
///     a 15 µF bypass through 15k that the Bass Shift pull shorts) driving the Fender-topology stack directly (100k
///     slope, 250 pF treble cap, 750 pF more with Treble Shift, 0.1 µF, 47 nF; 250k treble and bass, 10k middle) and
///     Volume 1 (1M audio, 180 pF pull-bright); V1B; the lead drive (680k into 1M audio) into the V3A cascade (82k,
///     120 pF grid-to-cathode), a 270k/68k divider into the V3B lead stage (270k plate with 1 nF across it, 3.3k with
///     0.22 µF through 15k, shorted by the lead-bright pull), a 220k ‖ 250 pF / 100k ‖ 500 pF divider into V2B (1.5k
///     unbypassed), the lead master shunting the signal into a 150k/4.3k pad, the V4A recovery stage (120k,
///     1k ‖ 0.47 µF, 15 µF through 15k, shorted by the deep pull), the master, the five-band graphic EQ, a long-tail
///     pair and Simul-Class power (two 6L6GC in pentode beside two triode-strapped EL34). Rhythm
///     (<see cref="Rhythm" />): the same amp without the lead circuit, V1B going straight into V2B through
///     3.3M ‖ 20 pF, and without the lead master.
/// </summary>
/// <remarks>
///     <para>
///         Sources: the Yahiake LiveSPICE redraw "Mark IIC+(++) Preamp_GEQ" (every preamp value and the wiring, in its
///         stock, non-"++" positions), the annotated redraw on boogieforum (docs/06 §3.4), and the Music Electronics
///         Forum thread on the 120 pF grid-to-cathode cap. Values from a redraw, not the factory scan.
///     </para>
///     <para>
///         Guesses, marked: the redraw draws V3A's 2.2 µF cathode cap unconnected; it is connected here (a cascade
///         stage this hot is bypassed on every Mark lead circuit described in text). In the lead type the rhythm
///         path's 3.3M feed into V2B's grid, live in the lead mode too, is left out (about -33 dB under the lead
///         signal). The graphic EQ is
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
///         drive than the pentodes); here both pairs share the pentodes' grid network. Guessed: 40 mA idle, the
///         2.5k primary.
///     </para>
///     <para>
///         The supply, from the same power page ("High Voltage Supply") and the preamp page: A 480 V (the reservoir
///         and the plates' centre tap), a choke to B (the screens, through 470 Ω each), 5.6k 2 W to C (30 µF) and 1k
///         2 W from C to D (30 µF). The preamp page prints C 397 V over V2 and D 392 V over V1, and puts the lead
///         circuit on C; the power page feeds the inverter's 82k and 91k from C. In the el34world scan the power
///         page's numbers are too coarse to read alone; a sharper copy of the same sheet (© 1985) is page 2 of
///         <c>Boogie_mkiii.pdf</c>, the Mark III's set, where they read A 480 V, C 397 V and D 392 V. B+ is that
///         480 V and the preamp node is C: 5.6k into 30 µF. The choke's resistance is not printed (100 Ω here), so
///         C's draw is not taken from B's printed voltage: it is the current that drops the model's screen node to
///         the drawn 397 V across the 5.6k (14.5 mA for the inverter, the triodes and the reverb, which is not
///         modelled).
///     </para>
///     <para>
///         <b>Assumed, not drawn:</b> below C the model keeps the generic chain's two filters, 10k into 22 µF for the
///         lead circuit and 10k into 22 µF more for V1, in place of the drawn 1k into 30 µF to D. With the lead
///         circuit on C and V1 one 1k below it, as drawn, the model motorboats flat out (every gain and master at 10:
///         -13 dB two seconds after a chord stops, and still with 220 µF at D), as the Powerball's does on its drawn
///         chain. The two nodes draw a nominal 0.17 mA each, so that V1's rests at the drawn D, 392 V, and the lead
///         circuit's two thirds of the way up to C, 393.7 V: the stages are built for the sheet's voltages, within
///         4 V. Not taken from the sheet: its filter caps at A and B (two 220 µF in series, doubled on the
///         Simul-Class, and 30 µF); the reservoir and screen caps here are still the first guesses.
///     </para>
///     <para>
///         Panel: the real amp's two Shift pulls (Treble, on the treble pot: the 750 pF; Bass, on the bass pot: V1A's
///         15 µF bypass) are Switch 1 and Switch 3. Five pulls on four switches: the Lead Master's Bright pull (V3B's
///         0.22 µF bypass) shares the Bright switch with Volume 1's.
///     </para>
///     <para>
///         Rhythm channel, read from the factory scan's preamp page (el34world <c>Boogie_mkiic_plus.pdf</c>, sheet 1,
///         "Mark IIC+ Guitar Preamplifier"): V1B's 0.047 µF with 100k to ground, then 3.3M with a cap across it (10 pF
///         printed, "20 pF on Simul-Class": 20 pF here) into the next grid, which has 680k ‖ 47 pF to ground; that
///         stage (100k plate, 1.5k unbypassed) is the one the lead circuit returns into, and LDR 4 opens the lead
///         circuit's output and the lead master's ground in the rhythm mode. From there on the two modes share the
///         path: 47k and 0.047 µF, 150k into 4.7k ‖ 47k, the recovery stage, the master. The scan names these two
///         stages V2A and V2B; the type keeps the lead type's names (V2B, V4A) so the shared stages are called the
///         same in both. Every other value is the lead type's. The reverb (between the 150k and the recovery stage's
///         grid) is left out.
///     </para>
/// </remarks>
public static class MarkIIC {
    /// <summary>
    ///     The supply's node C, in volts as the factory sheets print it: the inverter, V2B and V4A (the preamp node).
    /// </summary>
    internal const double RailC = 397;

    /// <summary>The factory sheets' node D, where V1A and V1B run: the second assumed node's rest voltage.</summary>
    internal const double RailD = 392;

    /// <summary>
    ///     The first assumed node, the lead circuit's: a third of the way from <see cref="RailC" /> down to
    ///     <see cref="RailD" /> (the two 10k carry two and one of the nodes' equal draws).
    /// </summary>
    internal const double RailLead = RailC - 2 * (RailC - RailD) / 3;

    /// <summary>
    ///     What the preamp draws through the 5.6k: the current that rests C at <see cref="RailC" /> below the screen
    ///     node (the remarks).
    /// </summary>
    const double PreampDraw = 14.5e-3;

    /// <summary>
    ///     The preamp's supply below C (the remarks): two assumed nodes, each the generic chain's 10k into 22 µF, node
    ///     1 for the lead circuit and node 2 below it for V1, drawing the nominal current that rests node 2 at
    ///     <see cref="RailD" />.
    /// </summary>
    internal static PreampSupplyDesign Supply { get; } = new(
        [
            new(
                AmpChannel.GenericResistance,
                AmpChannel.GenericCap,
                (RailC - RailD) / (3 * AmpChannel.GenericResistance)
            ),
            new(
                AmpChannel.GenericResistance,
                AmpChannel.GenericCap,
                (RailC - RailD) / (3 * AmpChannel.GenericResistance),
                1
            )
        ]
    );

    public static AmpDesign Design { get; } = new(
        "Mark IIC+",
        [
            new NetworkStageDesign("V1A", InputAndStack()) { SupplyNode = 2 },
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, RailD, 100e3, 1.5e3, 15e-6) { SupplyNode = 2 },
            new NetworkStageDesign("V3A", LeadDrive()) { SupplyNode = 1 },
            new NetworkStageDesign("V3B", LeadStage()) { SupplyNode = 1 },
            new NetworkStageDesign("V2B", LeadReturn()),
            new NetworkStageDesign("V4A", Recovery(true)),
            new CouplingStageDesign("Master", 47e-9, new(1e6, PotTaper.Audio), PotRole.Master, 15e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        new(
            "2 x 6L6GC + 2 x EL34 (Simul-Class)",
            PentodeModel.SixL6GC,
            1,
            480,
            470,
            0.04,
            false,
            0,
            75,
            // The 22k tail stands on the feedback shunt, 3.3k over 1.5k: 26.8k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, RailC, 82.5e3, 90.9e3, 26.8e3, 470),
            0.1e-6,
            220e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 3.3e3, 1.5e3, new(250e3, PotTaper.Linear), 4.7e-9) { Series = new(56e3, 3.3e3, 47e-9) },
            true,
            new(2.5e3),
            SpeakerDesign.Closed4x12,
            // 5.6k 2 W from the screens to C's 30 µF; the draw rests C at the drawn 397 V (see the remarks).
            new(30, 120e-6, 100, 47e-6, 5.6e3, 30e-6, PreampDraw),
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
        },
        PreampSupply = Supply
    };

    /// <summary>
    ///     The rhythm channel: the lead type's V1A, stack, Volume 1 and V1B, then <see cref="RhythmReturn" /> in place
    ///     of the lead circuit, the recovery stage without the lead master, the master, the EQ and the same power amp.
    /// </summary>
    public static AmpDesign Rhythm { get; } = Design with {
        Name = "Mark IIC+ Rhythm",
        Preamp = [
            new NetworkStageDesign("V1A", InputAndStack()) { SupplyNode = 2 },
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, RailD, 100e3, 1.5e3, 15e-6) { SupplyNode = 2 },
            new NetworkStageDesign("V2B", RhythmReturn()),
            new NetworkStageDesign("V4A", Recovery(false)),
            new CouplingStageDesign("Master", 47e-9, new(1e6, PotTaper.Audio), PotRole.Master, 15e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        OutputTrimDb = -7,
        Panel = Design.Panel with { Control1 = null, Control2 = null }
    };

    /// <summary>
    ///     V1A with the stack on its plate and Volume 1 on the treble wiper: 1M grid leak; 150k plate; 1.5k ‖ 0.47 µF,
    ///     and 15 µF through 15k (shorted by Bass Shift); the stack (750 pF more with Treble Shift); Volume 1 with 180
    ///     pF across its upper half (Bright).
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (plate, cathode, shift) = MesaParts.FrontStage(net, RailD);
        var (slope, trebleTop, middle) = (net.Node(), net.Node(), net.Node());
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
            RailLead,
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
            RailLead,
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
        var (coupled, grid) = (net.Node(), net.Node());
        MesaParts.LeadDivider(net, net.Input, coupled, grid, 100e3 * 680e3 / (100e3 + 680e3), 547e-12);
        net.Output = ReturnTriode(net, grid);
        return net;
    }

    /// <summary>
    ///     The rhythm channel into V2B: 47 nF from V1B with 100k to ground, 3.3M ‖ 20 pF into the grid's 680k ‖ 47 pF;
    ///     100k plate, 1.5k unbypassed.
    /// </summary>
    static StageNetlist RhythmReturn() {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        net.Resistor(coupled, grid, 3.3e6);
        net.Capacitor(coupled, grid, 20e-12);
        net.Resistor(grid, StageNetlist.Ground, 680e3);
        net.Capacitor(grid, StageNetlist.Ground, 47e-12);
        net.Output = ReturnTriode(net, grid);
        return net;
    }

    /// <summary>
    ///     V2B itself, the stage both channels come back into: 100k plate, 1.5k unbypassed. Returns the plate.
    /// </summary>
    static int ReturnTriode(StageNetlist net, int grid) {
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, RailC);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>
    ///     The lead master and the recovery stage V4A: 47k and 47 nF from V2B, the 250k audio lead master as a shunt to
    ///     ground (<paramref name="leadMaster" />; the rhythm mode disconnects it), 150k into 4.7k ‖ 47k, 120 pF
    ///     grid-to-cathode; 120k plate, 1k ‖ 0.47 µF and 15 µF through 15k (shorted by the deep pull).
    /// </summary>
    static StageNetlist Recovery(bool leadMaster) {
        var net = new StageNetlist();
        var (series, master, grid, supply, plate, cathode, deep) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, series, 47e3);
        net.Capacitor(series, master, 47e-9);
        if (leadMaster) {
            net.Rheostat(master, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        }

        net.Resistor(master, grid, 150e3);
        net.Resistor(grid, StageNetlist.Ground, 4.7e3 * 47e3 / (4.7e3 + 47e3));
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, RailC);
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
