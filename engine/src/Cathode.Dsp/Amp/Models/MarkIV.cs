using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark IV, its three channels as three types. Lead (<see cref="Design" />, channel 3): the IIC+ front
///     end with its own values. V1A (150k, 1.5k ‖ 0.47 µF and 22 µF through 22k) driving the Fender-topology stack (250
///     pF, 750 pF more with Fat) and the lead gain (1M
///     audio), V1B (100k, 1.5k ‖ 15 µF), the lead drive (680k into 1M audio, 470k at the wiper, 120 pF to the cathode)
///     into V2A (82k, 1.5k ‖ 2.2 µF), a 270k/68k ‖ 1 nF divider into V2B (270k ‖ 1 nF, 3.3k with 0.22 µF through 22k,
///     shorted by Bright), 220k ‖ 250 pF into 100k ‖ 500 pF and V3A (100k, 1.5k with 15 µF through 22k, and 2.2 µF
///     straight across with Mid Boost), the lead master (100k audio shunt) into a 150k/4.7k pad and 3.3k to V4A (120k,
///     1k ‖ 15 µF), the master (250k audio), the five-band EQ, and Simul-Class power with four 6L6GC (a pentode pair
///     beside a triode-strapped pair). Rhythm 1 (<see cref="Rhythm1" />, clean) and Rhythm 2 (<see cref="Rhythm2" />,
///     crunch) share V1A, V1B, the return stage and the recovery stage with the lead and skip the lead circuit; each
///     has its own gain and master, and Rhythm 2 its own treble pot.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Yahiake LiveSPICE redraw "Mark IV Lead Channel Preamp_GEQ" (every preamp value and the wiring),
///         and the factory power-amp sheet (el34world <c>Boogie_mkiv.pdf</c>, "Mark IV Power Amp", 8/92) for the power
///         section: an 82k/91k inverter with 470 Ω bias and a 22k tail, 0.1 µF into 220k grid leaks, 2.2k grid
///         stoppers and 470 Ω (2 W) screens on all four 6L6 (#102). The preamp is a redraw, not the factory scan.
///         The presence, from the same sheet, is as on the IIC+ a 250k rheostat in series with the feedback
///         (<see cref="FeedbackDesign.Series" />), one per channel picked by LDRs: 0.1 µF from the 8 Ω tap, the
///         lead channel's rheostat, 3.3k ‖ 600 pF and 56k ‖ 5 nF into the tail's foot, and 3.3k ‖ 47 nF over 1.5k to
///         ground (the 0.1 µF beside them is the second inverter grid's return to the foot). Left out: the 0.1 µF
///         from the tap (a corner near 25 Hz at 0, lower turned up) and the 600 pF (a corner at 80 kHz), which keeps
///         the network second order. The lead and R2 rheostats each have a cap in series
///         (1 nF, 680 pF) with a switch across it: the Presence knob's pull shift
///         (<see cref="SeriesPresence.ShiftCap" />). The sheet does not say which position is which; the model takes
///         the switch closed (the cap shorted, feedback at every frequency) as pushed in. The panel has no switch
///         left for it, so the Circuit Amp's Presence Shift is this pull on the lead and Rhythm 2 types, in place of
///         the generic octave; Rhythm 1's rheostat has no cap and keeps the generic one.
///     </para>
///     <para>
///         Guesses, marked: the rheostat's taper (linear), the IIC+ model's 465 V and 40 mA, a 2.2k primary. The 2.7k
///         that straps the outer pair's screens to their plates in triode is left out. In the lead type the rhythm
///         path's 3.3M feed into V3A is left out; the EQ is the behavioural slot
///         (<see cref="EqualizerStageDesign" />).
///     </para>
///     <para>
///         Rhythm 1 and Rhythm 2 are read from the factory scan's "Preamp: Section 1" and "Section 3" sheets (el34world
///         <c>Boogie_mkiv.pdf</c>), not the redraw; the channel in brackets beside each LDR and relay contact says when
///         it is closed. V1A: 150k, 1.5k ‖ 0.47 µF and 2.2 µF through 22k, the 22k shorted in Rhythm 2 (LDR7). Its
///         plate carries two stacks at once, the lead's (always grounded) and the rhythm one, whose foot RY1 grounds
///         in Rhythm 1: 250 pF, 100k slope, 0.1 µF, 47 nF, 250k treble, 250k bass, 10k middle. Rhythm 1: the treble
///         wiper into the 1M gain, 120 pF across its upper half (the Bright pull) and 470k at its wiper, into V1B.
///         Rhythm 2: LDR2 puts a second 250k treble pot beside the first, from the treble cap to the first pot's foot
///         through 68k, and its wiper feeds the 1M Rhythm 2 gain and V1B. V1B's 0.047 µF has 100k to ground; in
///         Rhythm 1 it goes on through 3.3M ‖ 20 pF into the return stage's grid with 680k ‖ 47 pF to ground, in Rhythm
///         2 RY2 shorts the 3.3M and opens the 680k. The return stage (100k, 1.5k) has 15 µF through 22k on its
///         cathode, the 22k shorted in Rhythm 2. Then 0.047 µF and 47k, the channel's 100k master as a shunt, 150k,
///         4.7k to ground in Rhythm 2 and the lead only, 3.3k and 47k at the recovery stage's grid. The scan names the
///         return and recovery stages V2A and V2B; the types keep the lead type's names (V3A, V4A).
///     </para>
///     <para>
///         Guesses and simplifications in the rhythm types, marked: the scan does not say which channel closes LDR3
///         (the 750 pF beside the rhythm treble cap, joined through 10M when open) and LDR6 (a second ground for the
///         rhythm stack's foot). Here LDR3 is Rhythm 2's Fat pull and LDR6 grounds the foot in Rhythm 2. The lead
///         stack on V1A's plate is a fixed load with the lead channel's tone knobs at noon; its treble wiper's 1M lead
///         gain, the idle channel's 1M gain on the other treble wiper and the 10M are left out. The Mid Gain switch's
///         2.2 µF acts in Rhythm 2 too, beside 15 µF already straight across the cathode (17.2 µF against 15 µF): left
///         out. Supply voltages, pot tapers and the power amp are the lead type's but for the presence rheostat's
///         shift cap (none on Rhythm 1, 680 pF on Rhythm 2); the reverb is left out.
///     </para>
/// </remarks>
public static class MarkIV {
    public static AmpDesign Design { get; } = new(
        "Mark IV",
        [
            new NetworkStageDesign("V1A", InputAndStack()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 425, 100e3, 1.5e3, 15e-6),
            new NetworkStageDesign("V2A", LeadDrive()),
            new NetworkStageDesign("V2B", LeadStage()),
            new NetworkStageDesign("V3A", LeadReturn()),
            new NetworkStageDesign("V4A", Recovery(PotRole.Control2, true)),
            new CouplingStageDesign("Master", 47e-9, new(250e3, PotTaper.Audio), PotRole.Master, 22e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        MarkIIC.Design.Power with {
            Name = "4 x 6L6GC (Simul-Class)",
            RatedPower = 85,
            PhaseInverter = new LongTailPairDesign(TriodeModel.Ecc83, 427, 82e3, 91e3, 22e3, 470),
            Feedback = new(1, 3.3e3, 1.5e3, new(250e3, PotTaper.Linear), 5e-9) {
                Series = new(56e3, 3.3e3, 47e-9, 1e-9)
            },
            Transformer = new(2.2e3),
            Strapped = PentodeModel.SixL6GC
        },
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Lead Gain",
            Depth = null,
            Cut = null,
            Bright = null,
            Control1 = "Lead Drive",
            Control2 = "Lead Master",
            Switch1 = "Fat",
            Switch2 = "Bright",
            Switch3 = "Mid Boost",
            Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     Rhythm 1, the clean channel: V1A and the rhythm stack into the gain (Bright: 120 pF), V1B, 3.3M ‖ 20 pF into
    ///     the return stage, the Rhythm 1 master and the recovery stage without the 4.7k pad.
    /// </summary>
    public static AmpDesign Rhythm1 { get; } = Design with {
        Name = "Mark IV Rhythm 1",
        Preamp = [
            new NetworkStageDesign("V1A", RhythmInput(false)),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 425, 100e3, 1.5e3, 15e-6),
            new NetworkStageDesign("V3A", RhythmReturn(false)),
            new NetworkStageDesign("V4A", Recovery(PotRole.Control1, false)),
            new CouplingStageDesign("Master", 47e-9, new(250e3, PotTaper.Audio), PotRole.Master, 22e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        Power = Design.Power with { Feedback = Feedback(0) },
        OutputTrimDb = -7,
        Panel = AmpPanel.Standard with {
            Depth = null, Cut = null, Control1 = "R1 Master", Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     Rhythm 2, the crunch channel: V1A fully bypassed, the rhythm stack with Rhythm 2's own treble pot (Fat: 750
    ///     pF more) into the gain, V1B straight into the return stage (fully bypassed), the Rhythm 2 master and the
    ///     recovery stage behind the 4.7k pad.
    /// </summary>
    public static AmpDesign Rhythm2 { get; } = Design with {
        Name = "Mark IV Rhythm 2",
        Preamp = [
            new NetworkStageDesign("V1A", RhythmInput(true)),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 425, 100e3, 1.5e3, 15e-6),
            new NetworkStageDesign("V3A", RhythmReturn(true)),
            new NetworkStageDesign("V4A", Recovery(PotRole.Control1, true)),
            new CouplingStageDesign("Master", 47e-9, new(250e3, PotTaper.Audio), PotRole.Master, 22e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        Power = Design.Power with { Feedback = Feedback(680e-12) },
        OutputTrimDb = -7,
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Control1 = "R2 Master",
            Switch1 = "Fat",
            Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     V1A with the stack on its plate and the lead gain on the treble wiper: 1M grid leak; 150k plate; 1.5k ‖
    ///     0.47 µF and 22 µF through 22k; 250 pF treble cap (750 pF more with Fat), 100k slope, 0.1 µF, 47 nF.
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, bass, slope, trebleTop, trebleBottom, middle) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, bass, 22e-6);
        net.Resistor(bass, StageNetlist.Ground, 22e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var treble = MesaParts.Stack(
            net,
            plate,
            trebleTop,
            trebleBottom,
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
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     The lead type's feedback with a rhythm channel's presence rheostat: the same network, with
    ///     <paramref name="shiftCap" /> as its pull shift's cap (Rhythm 2: 680 pF; Rhythm 1: none).
    /// </summary>
    static FeedbackDesign Feedback(double shiftCap) {
        var lead = Design.Power.Feedback;
        return lead with { Series = lead.Series!.Value with { ShiftCap = shiftCap } };
    }

    /// <summary>
    ///     V1A as a rhythm channel has it (<paramref name="crunch" />: Rhythm 2): 1M grid leak; 150k plate; 1.5k ‖
    ///     0.47 µF and 2.2 µF through 22k (Rhythm 2: straight across); the lead channel's stack as a fixed load; the
    ///     rhythm stack (250 pF, 100k slope, 0.1 µF, 47 nF). Rhythm 1: the treble wiper into the 1M audio gain, 120 pF
    ///     across its upper half with Bright, 470k at its wiper. Rhythm 2: 750 pF more with Fat, the first treble pot a
    ///     fixed 250k, the second (the Treble knob) beside it with 68k at its foot, its wiper into the 1M audio gain.
    /// </summary>
    static StageNetlist RhythmInput(bool crunch) {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        if (crunch) {
            net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        } else {
            var partial = net.Node();
            net.Capacitor(cathode, partial, 2.2e-6);
            net.Resistor(partial, StageNetlist.Ground, 22e3);
        }

        net.Supply(supply, 425);
        net.Resistor(supply, plate, 150e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        LeadStackLoad(net, plate);

        var (treble, bass, middle) =
            (new Pot(250e3, PotTaper.Linear), new Pot(250e3, PotTaper.Audio), new Pot(10e3, PotTaper.Linear));
        var gain = new Pot(1e6, PotTaper.Audio);
        if (!crunch) {
            var (wiper, _, _) =
                MesaParts.Stack(net, plate, 250e-12, 0, null, 100e3, 0.1e-6, 47e-9, treble, bass, middle);
            var volume = net.Pot(wiper, StageNetlist.Ground, gain, PotRole.Gain);
            net.SwitchedCapacitor(wiper, volume, 120e-12, SwitchRole.Bright, CapacitorRole.Bright);
            net.Resistor(volume, StageNetlist.Ground, 470e3);
            net.Output = volume;
            return net;
        }

        var (trebleTop, trebleFoot, secondFoot, slope, bassFoot) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, trebleTop, 250e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(plate, trebleTop, 750e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        net.Resistor(trebleTop, trebleFoot, treble.Ohms);
        var second = net.Pot(trebleTop, secondFoot, treble, PotRole.Treble);
        net.Resistor(secondFoot, trebleFoot, 68e3);
        net.Capacitor(slope, trebleFoot, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, bassFoot, bass, PotRole.Bass);
        net.Capacitor(slope, bassFoot, 47e-9, CapacitorRole.Stack);
        net.Rheostat(bassFoot, StageNetlist.Ground, middle, PotRole.Middle);
        net.Output = net.Pot(second, StageNetlist.Ground, gain, PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     The lead channel's stack as the load it is on V1A's plate while a rhythm channel plays: 250 pF into the 250k
    ///     treble track, 100k slope, 0.1 µF, 47 nF, the bass (250k audio) and middle (10k) rheostats at noon.
    /// </summary>
    static void LeadStackLoad(StageNetlist net, int plate) {
        var (trebleTop, trebleFoot, slope, bassFoot) = (net.Node(), net.Node(), net.Node(), net.Node());
        var bass = new Pot(250e3, PotTaper.Audio);
        net.Capacitor(plate, trebleTop, 250e-12);
        net.Resistor(trebleTop, trebleFoot, 250e3);
        net.Resistor(plate, slope, 100e3);
        net.Capacitor(slope, trebleFoot, 0.1e-6);
        net.Resistor(trebleFoot, bassFoot, bass.Ohms * bass.Fraction(0.5));
        net.Capacitor(slope, bassFoot, 47e-9);
        net.Resistor(bassFoot, StageNetlist.Ground, 5e3);
    }

    /// <summary>
    ///     A rhythm channel into V3A (<paramref name="crunch" />: Rhythm 2): 47 nF from V1B with 100k to ground; Rhythm
    ///     1: 3.3M ‖ 20 pF into the grid's 680k ‖ 47 pF; Rhythm 2: straight to the grid, 47 pF to ground. 100k plate;
    ///     1.5k with 15 µF through 22k (Rhythm 2: straight across).
    /// </summary>
    static StageNetlist RhythmReturn(bool crunch) {
        var net = new StageNetlist();
        var coupled = net.Node();
        var grid = crunch ? coupled : net.Node();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        if (!crunch) {
            net.Resistor(coupled, grid, 3.3e6);
            net.Capacitor(coupled, grid, 20e-12);
            net.Resistor(grid, StageNetlist.Ground, 680e3);
        }

        net.Capacitor(grid, StageNetlist.Ground, 47e-12);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        if (crunch) {
            net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        } else {
            var partial = net.Node();
            net.Capacitor(cathode, partial, 15e-6);
            net.Resistor(partial, StageNetlist.Ground, 22e3);
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The lead drive into V2A (<see cref="MesaParts.LeadDrive" />): 20 nF and 680k into the 1M audio drive, its
    ///     wiper on the grid with 470k to ground and 120 pF to the cathode.
    /// </summary>
    static StageNetlist LeadDrive() {
        var net = new StageNetlist();
        net.Output = MesaParts.LeadDrive(net, net.Input, 680e3, new(1e6, PotTaper.Audio), PotRole.Control1, 433, 20e-9);
        return net;
    }

    /// <summary>
    ///     V2B (<see cref="MesaParts.LeadStage" />): 22 nF, 270k into 68k ‖ 1 nF at the grid; 270k plate with 1 nF
    ///     across it; 3.3k with 0.22 µF through 22k (shorted by Bright).
    /// </summary>
    static StageNetlist LeadStage() {
        var net = new StageNetlist();
        net.Output = MesaParts.LeadStage(net, net.Input, 433, SwitchRole.Switch2, 22e3, plateCap: 1e-9);
        return net;
    }

    /// <summary>
    ///     Back into V3A: 47 nF, 220k ‖ 250 pF into 100k ‖ 500 pF (with V3A's own 47 pF); 100k plate, 1.5k with 15 µF
    ///     through 22k and, with Mid Boost, 2.2 µF straight across.
    /// </summary>
    static StageNetlist LeadReturn() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, partial, boost) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        MesaParts.LeadDivider(net, net.Input, coupled, grid, 100e3, 547e-12);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, partial, 15e-6);
        net.Resistor(partial, StageNetlist.Ground, 22e3);
        net.Capacitor(cathode, boost, 2.2e-6);
        net.SwitchedResistor(boost, StageNetlist.Ground, 0, 1, SwitchRole.Switch3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The channel's master and V4A: 47 nF and 47k from V3A, the 100k audio master (<paramref name="master" />) as
    ///     a shunt, 150k into 4.7k (<paramref name="pad" />: the lead and Rhythm 2) and 3.3k to the grid with 47k to
    ///     ground; 120k plate, 1k ‖ 15 µF.
    /// </summary>
    static StageNetlist Recovery(PotRole master, bool pad) {
        var net = new StageNetlist();
        var (coupled, shunt, padded, grid) = (net.Node(), net.Node(), net.Node(), net.Node());
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, shunt, 47e3);
        net.Rheostat(shunt, StageNetlist.Ground, new(100e3, PotTaper.Audio), master);
        net.Resistor(shunt, padded, 150e3);
        if (pad) {
            net.Resistor(padded, StageNetlist.Ground, 4.7e3);
        }

        net.Resistor(padded, grid, 3.3e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }
}
