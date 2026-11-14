using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark V (90 W), its three channels as three types. Channel 3 (<see cref="Design" />: Mark IIC+,
///     Mark IV, Extreme): V1A (150k, 1.5k ‖ 0.47 µF) drives the channel's stack (1000 pF treble cap, 100k slope,
///     0.1 µF, 47 nF; 200k audio treble, 250k audio bass, 10k audio middle) and a fixed divider on the treble wiper
///     (330k and 56k into 470k + 100k, 180 pF from the treble pot's top); V1B (100k, 1.5k ‖ 0.47 µF); 47 nF and 100k
///     to ground, 680k into the 1M audio gain, its wiper on V5A's grid with 475k to ground and 120 pF to the cathode
///     (82k, 1.5k ‖ 2.2 µF); 20 nF and 270k into 68k ‖ 1 nF at V4B (120 pF grid to cathode, 270k, 3.3k with 0.22 µF
///     by the Bright switch); 47 nF, 220k ‖ 250 pF into 680k ‖ 100k ‖ 547 pF at V3A (100k, 1.5k with the mode's
///     bypass); 47 nF, 47k into 47k, 150k and 3.3k into V6A's grid with 47k to ground (4.7k more in Mark IV mode) and
///     120 pF to the cathode (120k, 1k with 4.4 µF in IIC+ mode, else 15 µF); the channel master (100k audio, a
///     shunt). Channel 1 (<see cref="Clean" />: Clean, Fat, Tweed) and channel 2 (<see cref="Crunch" />: Edge,
///     Crunch, Mark I) are described below. All three end in the five-band graphic EQ, the effects loop's recovery
///     stage V6B with the Output control, a long-tail pair and Simul-Class power (two 6L6GC in pentode beside two
///     triode-strapped 6L6GC).
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawings "Mark V" (John M, Mar 10 2009, board rev MARKV-1, eight sheets:
///         supply, preamp parts 1–3, reverb, EQ, loop/driver, power amp),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Mesaboogiemark_v.pdf. The supply sheet names the
///         relays (C1 to C3 the channels; M1 Clean, M2 Fat, M3 Tweed, M4 Edge, M5 Crunch, M6 Mark I, M7 IIC+, M8
///         Mark IV, M9 Extreme; a relay is on in its mode, one marked with an asterisk off in it) and the voltages per
///         channel: C 376 / 378 / 410 V, E 370 / 334 / 405 V, D 380 V, A 448 V. Every preamp value is read from the
///         drawing. Mesa's owner's manual for the amp ("Mark Five", read at manualshive.com, manual 1775235) gives the
///         settings the drawing cannot: the loop's Send Level at 12:00, and the power modes described below.
///     </para>
///     <para>
///         As drawn, marked: the stack's 750 pF and 250 pF treble caps are both connected (1000 pF). The relays read
///         from the drawing: Mark IV mode (RYM8) adds the 4.7k at V6A's grid; IIC+ mode (RYM7) puts 4.4 µF (two
///         2.2 µF) on V6A's cathode in place of 15 µF. The amp's mode switch has three positions and the panel here
///         two switches (IIC+ Mode, Mark IV Mode; both off is Extreme): with both on the model is in Mark IV mode
///         (<see cref="AmpPanel.ThreeWay" />), so the IIC+'s 4.4 µF and the Mark IV's pad never meet.
///     </para>
///     <para>
///         Channel 1 (preamp sheets 1 to 3). V1A's plate load is 100k + 47k (the 47k is shorted in Edge only) from E,
///         with 0.15 µF and 82k to ground beside it; its cathode is two 1.5k in series (the lower one shorted in
///         Crunch only) with 0.47 µF across both. The stack (250 pF, 100k, 0.1 µF, 47 nF; 250k audio treble and bass,
///         10k audio middle) feeds the 1M audio gain, with 120 pF from its top and 100k on to V1B's grid; RYM3A puts
///         the wiper on the junction of the two, with 475k from there to ground, in Clean and Fat, and straight on
///         the grid in Tweed. V1B (100k, 1.5k ‖ 0.47 µF and 15 µF) through 47 nF, with 100k to ground, into V3A's
///         grid by the mode: 3.3M in Clean, 2.2M with 680k to ground in Fat, 475k ‖ 500 pF with 3.3M ‖ 180 pF to
///         ground in Tweed; 20 pF beside the 3.3M or 2.2M; the reverb's 475k return is the grid's leak. V3A (100k,
///         1.5k), 47 nF, 47k and 150k (75k with the panel's Bold switch) into V3B's grid with 47k and the channel
///         master (100k audio, a shunt) to ground; V3B (120k, 1k ‖ 15 µF), 47 nF and 22k into 8.2k ‖ 250 pF (and 10k
///         more to ground in Tweed).
///     </para>
///     <para>
///         Channel 2. V1A's plate through 5 nF, with 1M to ground, and 220k (500 pF across it in Edge) into the 1M
///         audio gain and V2A's grid (220k leak). V2A: 100k from E; 1.5k with 0.22 µF through 1.5k, the 1.5k shorted
///         in Edge, and 15 µF more in Mark I. Its plate drives the stack, through 475k ‖ 500 pF in Edge: 250 pF and,
///         except in Mark I, 500 pF more to the 250k audio treble; a 100k slope, 250k in Mark I; the bass cap is 47 nF
///         to the treble pot's foot in Edge and 0.1 µF to the bass pot's top otherwise, with 15k between the two that
///         Crunch shorts; 47 nF to the 10k audio middle. The treble wiper through 120k, then 33k (shorted in Mark I)
///         into 120 pF and 330k over 470k ‖ 47 pF; V1B takes the top of the 330k, in Crunch its foot. V1B has its
///         15 µF except in Edge. V3A is fed as in Fat (RYM1 and RYM3B are released); its plate through 47 nF and 220k
///         into the 1M audio channel master, with 1 nF from the master's top into 330k (Edge), 330k ‖ 150k (Mark I)
///         or nothing (Crunch).
///     </para>
///     <para>
///         The loop and the driver (sheet "Loop/Driver"), in every channel: the EQ's output into the Send Level pot
///         (25k audio over 1.5k), 470 Ω into V6B's grid (47k), V6B (120k from D, 1k unbypassed), 100k, 0.68 µF and
///         220k into the 1M audio Output control. Up to 1.0.0-rc.5 the model had the 0.68 µF, the 220k and the Output
///         control without V6B and the Send Level, which left the power amp about 15 dB short of drive. The inverter
///         is 82k / 91k, 470 Ω and a 22k tail standing on 3.3k ‖ 47 nF over 1.5k. The feedback comes from the 8 Ω tap
///         through 4.7k, 0.1 µF, the playing channel's presence rheostat (channel 2's with 2.7k in series, channel
///         3's with 1 nF in series in Extreme, RYM9F), 3.3k ‖ 500 pF and 56k ‖ 5 nF into the top of that 3.3k: the
///         Mark IV's network value for value (<see cref="MarkIV" />, <see cref="FeedbackDesign.Series" />). Up to
///         1.0.0-rc.5 the model had a 4.8k tail and a generic 25k presence across the 1.5k.
///     </para>
///     <para>
///         Guesses, marked. The drawing does not say which mode drives the JFET switches: here V1A's 15 µF bypass
///         (J175A) is on in Fat alone (the relays change nothing in the bass between Clean and Fat, and the manual
///         gives Fat "a big breathy bottom that extends down into the sub-low region"); V3A's 2.2 µF (J175B) is on in
///         Mark IV mode and its 100 µF (J175C) in Extreme mode, IIC+ mode and channels 1 and 2 leaving it unbypassed.
///         On RYM3A the contact for Clean and Fat ends in a stub; it is read as joined to the junction of the 120 pF
///         and the 100k, since otherwise the Gain pot would not be in the signal path. On channel 2's stack two lines
///         cross the treble pot's foot and the bass pot's top without a dot; they are read as joined (the 47 nF to the
///         foot, the 0.1 µF and RYM5C to the bass pot's top), which is what makes the 15k switchable. The presence
///         rheostats' values are not printed: 250k linear, as on the IIC+ and the IV. Left out: the 3.3M ‖ 20 pF feed
///         from V1B into V3A's grid in channel 3, the reverb (level zero: its 475k ‖ 250 pF send into 150k loads
///         V1B in channels 1 and 2; in channel 3 its loads are the 47k and 150k above), the zener clamps, the 0.1 µF
///         from the tap and the 500 pF across the 3.3k (as on the Mark IV), the caps between the inverter's plates,
///         and the Solo control. The primary (2.2k) is a typical value.
///     </para>
///     <para>
///         Not modelled: the 45 W and 10 W power modes. The manual says what they are (45 W: "only the middle pair
///         of 6L6s are on", in "extended Class A" push-pull "with their bias reduced"; 10 W: "the two nearest the 5U4
///         run pure Class A, single-ended"; silicon diodes at 90 W, the 5U4 at 10 W, either at 45 W), and the supply
///         sheet gives their voltages (45 W: 20 V lower on the tube, 8 V higher on the diodes; 10 W: 50 V lower). The
///         power amp sheet has the relays that do it (RYSE1 to 4, RYTP1 and 2, RY100, two 360 Ω cathode resistors,
///         two 1.6k 25 W loads) but neither a legend nor a driver sheet for them, so the bias shift and which pair
///         keeps which screen wiring are not known; the power amp block has no way to drop a pair either.
///     </para>
/// </remarks>
public static class MarkV {
    public static AmpDesign Design { get; } = new(
        "Mark V",
        [
            new NetworkStageDesign("V1A", InputAndStack()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 405, 100e3, 1.5e3, 0.47e-6),
            new NetworkStageDesign("V5A", GainStage()),
            new NetworkStageDesign("V4B", LeadStage()),
            new NetworkStageDesign("V3A", LeadReturn()),
            new NetworkStageDesign("V6A", Recovery()),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands),
            new NetworkStageDesign("V6B", LoopRecovery())
        ],
        // RYM9F: the 1 nF is in series with channel 3's rheostat in Extreme, neither IIC+ nor Mark IV.
        Power(410, 0, 1e-9) with { ShiftUnless = [SwitchRole.Switch1, SwitchRole.Switch2] },
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Master = "Output",
            Control1 = "Master",
            Switch1 = "IIC+ Mode",
            Switch2 = "Mark IV Mode",
            Switch3 = "Bright",
            Equalizer = ["80", "240", "750", "2200", "6600"],
            // One mode switch on the amp (IIC+ / Mark IV / Extreme): with both on it is Mark IV.
            ThreeWay = [new(SwitchRole.Switch1, SwitchRole.Switch2)]
        }
    };

    /// <summary>
    ///     Channel 1: Clean, with Fat on Switch 1 and Tweed on Switch 2 (the amp's one mode switch: with both on it is
    ///     Tweed), and the panel's Bold switch on Switch 3.
    /// </summary>
    public static AmpDesign Clean { get; } = Design with {
        Name = "Mark V Clean",
        Preamp = [
            new NetworkStageDesign("V1A", CleanInput()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 370, 100e3, 1.5e3, 15.47e-6),
            new NetworkStageDesign("V3A", CleanReturn()),
            new NetworkStageDesign("V3B", CleanOutput()),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands),
            new NetworkStageDesign("V6B", LoopRecovery())
        ],
        Power = Power(376, 0, 0),
        Panel = Design.Panel with { Switch1 = "Fat", Switch2 = "Tweed", Switch3 = "Bold" }
    };

    /// <summary>
    ///     Channel 2: Crunch, with Edge on Switch 1 and Mark I on Switch 2 (one mode switch: with both on it is
    ///     Mark I).
    /// </summary>
    public static AmpDesign Crunch { get; } = Design with {
        Name = "Mark V Crunch",
        Preamp = [
            new NetworkStageDesign("V1A", CrunchInput()),
            new NetworkStageDesign("V2A", CrunchStack()),
            new NetworkStageDesign("V1B", CrunchSecond()),
            new NetworkStageDesign("V3A", CrunchReturn()),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands),
            new NetworkStageDesign("V6B", LoopRecovery())
        ],
        // 2.7k in series with channel 2's presence rheostat.
        Power = Power(378, 2.7e3, 0),
        Panel = Design.Panel with { Switch1 = "Edge", Switch2 = "Mark I", Switch3 = null }
    };

    /// <summary>
    ///     The driver and Simul-Class power amp with the playing channel's presence: the inverter from
    ///     <paramref name="supply" /> volts (node C in that channel), <paramref name="presenceSeries" /> ohms in
    ///     series with the rheostat and <paramref name="shiftCap" /> in series with it (0 for none).
    /// </summary>
    static PowerAmpDesign Power(double supply, double presenceSeries, double shiftCap) =>
        new(
            "4 x 6L6GC (Simul-Class)",
            PentodeModel.SixL6GC,
            1,
            448,
            470,
            0.04,
            false,
            0,
            90,
            // The 22k tail stands on the feedback shunt, 3.3k over 1.5k: 26.8k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, supply, 82e3, 91e3, 26.8e3, 470),
            0.1e-6,
            150e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            // 4.7k from the tap and the 3.3k ahead of the 56k ‖ 5 nF leg.
            new(1, 4.7e3 + 3.3e3 + presenceSeries, 1.5e3, new(250e3, PotTaper.Linear), 5e-9) {
                Series = new(56e3, 3.3e3, 47e-9, shiftCap)
            },
            true,
            new(2.2e3),
            SpeakerDesign.Closed4x12,
            new(30, 110e-6, 100, 30e-6, 5.6e3, 33e-6, 6e-3, RectifierModel.Silicon),
            Strapped: PentodeModel.SixL6GC,
            StrappedPerSide: 1
        );

    /// <summary>
    ///     A relay contact from <paramref name="a" /> to <paramref name="b" />, closed while its switch is on.
    /// </summary>
    static void Closed(StageNetlist net, int a, int b, SwitchRole role) => net.SwitchedResistor(a, b, 0, 1, role);

    /// <summary>A contact closed while its switch is off.</summary>
    static void Open(StageNetlist net, int a, int b, SwitchRole role) => net.SwitchedResistor(a, b, 1, 0, role);

    /// <summary>A contact closed in a channel's middle mode alone: with neither Switch 1 nor Switch 2 on.</summary>
    static void Middle(StageNetlist net, int a, int b) {
        var between = net.Node();
        Open(net, a, between, SwitchRole.Switch1);
        Open(net, between, b, SwitchRole.Switch2);
    }

    /// <summary>
    ///     The effects loop's recovery stage and the Output control: the Send Level pot (25k audio over 1.5k) at
    ///     12:00, where the manual sets it, 470 Ω into the grid with 47k to ground; 120k from D (380 V); 1k unbypassed;
    ///     100k, 0.68 µF and 220k into the 1M audio Output (the Solo control under it is shorted).
    /// </summary>
    static StageNetlist LoopRecovery() {
        var net = new StageNetlist();
        var (wiper, foot, grid, rail, plate, cathode, fed, coupled, top) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        Pot send = new(25e3, PotTaper.Audio);
        var under = send.Fraction(0.5) * send.Ohms;
        net.Resistor(net.Input, wiper, send.Ohms - under);
        net.Resistor(wiper, foot, under);
        net.Resistor(foot, StageNetlist.Ground, 1.5e3);
        net.Resistor(wiper, grid, 470);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Supply(rail, 380);
        net.Resistor(rail, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Resistor(plate, fed, 100e3);
        net.Capacitor(fed, coupled, 0.68e-6);
        net.Resistor(coupled, top, 220e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }

    /// <summary>
    ///     Channel 1's V1A, stack and gain: 1M grid leak; 147k plate from E (370 V) with 0.15 µF and 82k to ground;
    ///     3k ‖ 0.47 µF, and 15 µF in Fat (a guess); 250 pF, 100k, 0.1 µF, 47 nF; the 1M audio gain on the treble
    ///     wiper with 120 pF from its top and 100k on to the output; the wiper on their junction with 475k to ground
    ///     in Clean and Fat, on the output in Tweed.
    /// </summary>
    static StageNetlist CleanInput() {
        var net = new StageNetlist();
        var (supply, plate, cathode, load, slope, trebleTop, trebleBottom, middle, mix, output) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node()
        );
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 370);
        net.Resistor(supply, plate, 147e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 15e-6, SwitchRole.Switch1);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, load, 0.15e-6);
        net.Resistor(load, StageNetlist.Ground, 82e3);
        Pot volume = new(250e3, PotTaper.Audio);
        var treble = MesaParts.Stack(
            net,
            plate,
            trebleTop,
            trebleBottom,
            slope,
            middle,
            250e-12,
            0,
            null,
            100e3,
            0.1e-6,
            47e-9,
            volume,
            volume,
            new(10e3, PotTaper.Audio)
        );
        var gain = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(treble, mix, 120e-12, CapacitorRole.Bright);
        net.Resistor(mix, output, 100e3);
        Open(net, gain, mix, SwitchRole.Switch2);
        net.SwitchedResistor(mix, StageNetlist.Ground, 475e3, 0, SwitchRole.Switch2);
        Closed(net, gain, output, SwitchRole.Switch2);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V3A in channels 1 and 2, on <paramref name="net" />: 47 nF from V1B with 100k to ground and the reverb's
    ///     send (475k ‖ 250 pF into 150k); the grid's leak is the reverb's 475k return. With
    ///     <paramref name="modes" /> (channel 1) the feed is 3.3M in Clean, 2.2M with 680k to ground in Fat (Switch
    ///     1), and in Tweed (Switch 2) 475k ‖ 500 pF from a node with 3.3M ‖ 180 pF to ground; without (channel 2)
    ///     it is Fat's. 20 pF lies across the 3.3M or 2.2M. 100k plate from <paramref name="supply" /> volts; 1.5k
    ///     unbypassed (a guess: see the remarks). Returns the plate.
    /// </summary>
    static int Return(StageNetlist net, double supply, bool modes) {
        var (coupled, send, grid, rail, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        net.Resistor(coupled, send, 475e3);
        net.Capacitor(coupled, send, 250e-12);
        net.Resistor(send, StageNetlist.Ground, 150e3);
        net.Resistor(grid, StageNetlist.Ground, 475e3);
        if (modes) {
            // The Tweed feed, taken off the line with the mode: its 475k ‖ 500 pF loaded the node in Clean and Fat
            // too (3.8M and 132 pF to ground), where the amp's relay has the network out. Its foot stays, so the
            // node it feeds is never left floating.
            var tweed = net.Node();
            net.SwitchedResistor(coupled, tweed, 0, 475e3, SwitchRole.Switch2);
            net.SwitchedCapacitor(coupled, tweed, 500e-12, SwitchRole.Switch2);
            net.Resistor(tweed, StageNetlist.Ground, 3.3e6);
            net.Capacitor(tweed, StageNetlist.Ground, 180e-12);
            var clean = net.Node();
            net.Resistor(coupled, clean, 3.3e6);
            Middle(net, clean, grid);
            net.SwitchedResistor(coupled, grid, 0, 2.2e6, SwitchRole.Switch1);
            net.SwitchedResistor(grid, StageNetlist.Ground, 0, 680e3, SwitchRole.Switch1);
            net.SwitchedCapacitor(coupled, grid, 0, SwitchRole.Switch2, off: 20e-12);
            Closed(net, tweed, grid, SwitchRole.Switch2);
        } else {
            net.Resistor(coupled, grid, 2.2e6);
            net.Resistor(grid, StageNetlist.Ground, 680e3);
            net.Capacitor(coupled, grid, 20e-12);
        }

        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>Channel 1's V3A (<see cref="Return" />, from 376 V).</summary>
    static StageNetlist CleanReturn() {
        var net = new StageNetlist();
        net.Output = Return(net, 376, true);
        return net;
    }

    /// <summary>
    ///     Channel 1's master and V3B: 47 nF, 47k and 150k (another 150k beside it with Bold) into the grid, with 47k
    ///     and the 100k audio master to ground; 120k from E; 1k ‖ 15 µF; 47 nF and 22k into 8.2k ‖ 250 pF, and 10k
    ///     more to ground in Tweed.
    /// </summary>
    static StageNetlist CleanOutput() {
        var net = new StageNetlist();
        var (coupled, fed, grid, rail, plate, cathode, blocked, pad) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, fed, 47e3);
        net.Resistor(fed, grid, 150e3);
        net.SwitchedResistor(fed, grid, 0, 150e3, SwitchRole.Switch3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Rheostat(grid, StageNetlist.Ground, new(100e3, PotTaper.Audio), PotRole.Control1);
        net.Supply(rail, 370);
        net.Resistor(rail, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, blocked, 47e-9);
        net.Resistor(blocked, pad, 22e3);
        net.Resistor(pad, StageNetlist.Ground, 8.2e3);
        net.Capacitor(pad, StageNetlist.Ground, 250e-12);
        net.SwitchedResistor(pad, StageNetlist.Ground, 0, 10e3, SwitchRole.Switch2);
        net.Output = pad;
        return net;
    }

    /// <summary>
    ///     A channel 2 stage's plate load on <paramref name="net" />: 100k from E (334 V in that channel), and
    ///     <paramref name="leak" /> from the grid, the stage's input, to ground (0 for none). Returns the 100k's foot.
    /// </summary>
    static int CrunchPlate(StageNetlist net, double leak) {
        var (rail, plate) = (net.Node(), net.Node());
        if (leak > 0) {
            net.Resistor(net.Input, StageNetlist.Ground, leak);
        }

        net.Supply(rail, 334);
        net.Resistor(rail, plate, 100e3);
        return plate;
    }

    /// <summary>
    ///     Channel 2's V1A and gain: 1M grid leak; 100k and 47k from E (334 V), the 47k shorted in Edge; two 1.5k
    ///     under the cathode, the lower shorted in Crunch, with 0.47 µF across both; 5 nF with 1M to ground, 220k
    ///     (500 pF across it in Edge) into the 1M audio gain.
    /// </summary>
    static StageNetlist CrunchInput() {
        var net = new StageNetlist();
        var upper = CrunchPlate(net, 1e6);
        var (plate, cathode, tap, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(upper, plate, 47e3);
        Closed(net, upper, plate, SwitchRole.Switch1);
        net.CathodeResistor(cathode, tap, 1.5e3);
        net.CathodeResistor(tap, StageNetlist.Ground, 1.5e3);
        Middle(net, tap, StageNetlist.Ground);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 5e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, top, 220e3);
        net.SwitchedCapacitor(coupled, top, 500e-12, SwitchRole.Switch1, CapacitorRole.Bright);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     Channel 2's V2A, stack and divider, by the mode (see the remarks): 220k grid leak; 100k from E; 1.5k with
    ///     0.22 µF through 1.5k (shorted in Edge) and 15 µF in Mark I; the plate, through 475k ‖ 500 pF in Edge, into
    ///     the stack; the treble wiper through 120k and 33k (shorted in Mark I) into 120 pF and 330k over 470k ‖
    ///     47 pF; the output is the top of the 330k, in Crunch its foot.
    /// </summary>
    static StageNetlist CrunchStack() {
        var net = new StageNetlist();
        var plate = CrunchPlate(net, 220e3);
        var (cathode, partial, fed, slope, low, trebleTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (trebleFoot, bassTop, middle, divider, upper, lower, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, partial, 0.22e-6);
        net.Resistor(partial, StageNetlist.Ground, 1.5e3);
        Closed(net, partial, StageNetlist.Ground, SwitchRole.Switch1);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 15e-6, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Resistor(plate, fed, 475e3);
        net.Capacitor(plate, fed, 500e-12);
        Open(net, plate, fed, SwitchRole.Switch1);
        net.Capacitor(fed, trebleTop, 250e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(fed, trebleTop, 0, SwitchRole.Switch2, CapacitorRole.Stack, 500e-12);
        net.Resistor(fed, slope, 150e3);
        Open(net, fed, slope, SwitchRole.Switch2);
        net.Resistor(slope, low, 100e3);
        Pot volume = new(250e3, PotTaper.Audio);
        var treble = net.Pot(trebleTop, trebleFoot, volume, PotRole.Treble);
        net.SwitchedCapacitor(low, trebleFoot, 47e-9, SwitchRole.Switch1, CapacitorRole.Stack);
        net.SwitchedCapacitor(low, bassTop, 0, SwitchRole.Switch1, CapacitorRole.Stack, 0.1e-6);
        net.Resistor(trebleFoot, bassTop, 15e3);
        Middle(net, trebleFoot, bassTop);
        net.Rheostat(bassTop, middle, volume, PotRole.Bass);
        net.Capacitor(low, middle, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(10e3, PotTaper.Audio), PotRole.Middle);
        net.Resistor(treble, divider, 120e3);
        net.Resistor(divider, upper, 33e3);
        Closed(net, divider, upper, SwitchRole.Switch2);
        net.Capacitor(upper, StageNetlist.Ground, 120e-12);
        net.Resistor(upper, lower, 330e3);
        net.Resistor(lower, StageNetlist.Ground, 470e3);
        net.Capacitor(lower, StageNetlist.Ground, 47e-12);
        Closed(net, upper, output, SwitchRole.Switch1);
        Closed(net, upper, output, SwitchRole.Switch2);
        Middle(net, lower, output);
        net.Output = output;
        return net;
    }

    /// <summary>Channel 2's V1B: 100k from E; 1.5k ‖ 0.47 µF, and 15 µF except in Edge.</summary>
    static StageNetlist CrunchSecond() {
        var net = new StageNetlist();
        var plate = CrunchPlate(net, 0);
        var cathode = net.Node();
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 0, SwitchRole.Switch1, off: 15e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     Channel 2's V3A (<see cref="Return" />, from 378 V) and master: 47 nF and 220k into the 1M audio master,
    ///     with 1 nF from its top into 330k (Edge and Mark I) and 150k beside it (Mark I).
    /// </summary>
    static StageNetlist CrunchReturn() {
        var net = new StageNetlist();
        var plate = Return(net, 378, false);
        var (blocked, top, shunt) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, blocked, 47e-9);
        net.Resistor(blocked, top, 220e3);
        net.Capacitor(top, shunt, 1e-9);
        net.SwitchedResistor(shunt, StageNetlist.Ground, 0, 330e3, SwitchRole.Switch1);
        net.SwitchedResistor(shunt, StageNetlist.Ground, 0, 330e3, SwitchRole.Switch2);
        net.SwitchedResistor(shunt, StageNetlist.Ground, 0, 150e3, SwitchRole.Switch2);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        return net;
    }

    /// <summary>
    ///     V1A with channel 3's stack on its plate: 1M grid leak; 150k plate; 1.5k ‖ 0.47 µF; 1000 pF, 100k slope,
    ///     0.1 µF, 47 nF; the treble wiper into 330k + 56k and 470k + 100k to ground, 180 pF from the treble pot's top.
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, slope, trebleTop, trebleBottom, middle, divider, output) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 405);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var treble = MesaParts.Stack(
            net,
            plate,
            trebleTop,
            trebleBottom,
            slope,
            middle,
            1000e-12,
            0,
            null,
            100e3,
            0.1e-6,
            47e-9,
            new(200e3, PotTaper.Audio),
            new(250e3, PotTaper.Audio),
            new(10e3, PotTaper.Audio)
        );

        net.Resistor(treble, divider, 330e3);
        net.Resistor(divider, output, 56e3);
        net.Resistor(output, StageNetlist.Ground, 570e3);
        net.Capacitor(trebleTop, output, 180e-12, CapacitorRole.Bright);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The gain into V5A (<see cref="MesaParts.LeadDrive" />): 680k (no coupling cap) into the 1M audio gain, its
    ///     wiper on the grid with 475k to ground and 120 pF to the cathode.
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        net.Output = MesaParts.LeadDrive(
            net,
            net.Input,
            680e3,
            new(1e6, PotTaper.Audio),
            PotRole.Gain,
            410,
            gridLeak: 475e3
        );
        return net;
    }

    /// <summary>
    ///     V4B (<see cref="MesaParts.LeadStage" />): 20 nF, 270k into 68k ‖ 1 nF at the grid, 120 pF to the cathode;
    ///     270k plate (no cap across it); 3.3k with 0.22 µF while the Bright switch is on.
    /// </summary>
    static StageNetlist LeadStage() {
        var net = new StageNetlist();
        net.Output = MesaParts.LeadStage(
            net,
            net.Input,
            410,
            SwitchRole.Switch3,
            0,
            20e-9,
            gridCathode: 120e-12
        );
        return net;
    }

    /// <summary>
    ///     V3A: 47 nF, 220k ‖ 250 pF into 680k ‖ 100k ‖ 547 pF; 100k plate; 1.5k with 2.2 µF in Mark IV mode and
    ///     100 µF in Extreme mode (neither IIC+ nor Mark IV), both guesses.
    /// </summary>
    static StageNetlist LeadReturn() {
        var net = new StageNetlist();
        var (grid, plate, cathode) = MesaParts.LeadReturn(net, 680e3 * 100e3 / (680e3 + 100e3), 410);
        var (extreme, notIv) = (net.Node(), net.Node());
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 2.2e-6, SwitchRole.Switch2);
        net.Capacitor(cathode, extreme, 100e-6);
        net.SwitchedResistor(extreme, notIv, 1, 0, SwitchRole.Switch1);
        net.SwitchedResistor(notIv, StageNetlist.Ground, 1, 0, SwitchRole.Switch2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The pad and V6A: 47 nF, 47k into 47k to ground, 150k and 3.3k into the grid with 47k to ground (and 4.7k in
    ///     Mark IV mode), 120 pF to the cathode; 120k plate; 1k with 4.4 µF in IIC+ mode, else 15 µF; 47 nF into the
    ///     channel master, a 100k audio rheostat to ground.
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (coupled, send, ret, grid, supply, plate, cathode, master) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, send, 47e3);
        net.Resistor(send, StageNetlist.Ground, 47e3);
        net.Resistor(send, ret, 150e3);
        net.Resistor(ret, grid, 3.3e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.SwitchedResistor(grid, StageNetlist.Ground, 0, 4.7e3, SwitchRole.Switch2);
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 410);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 4.4e-6, SwitchRole.Switch1, off: 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, master, 47e-9);
        net.Rheostat(master, StageNetlist.Ground, new(100e3, PotTaper.Audio), PotRole.Control1);
        net.Output = master;
        return net;
    }
}
