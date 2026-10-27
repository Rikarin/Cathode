using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the Fender AB763 amps (Deluxe, Twin and Super Reverb) share: two channels of two 12AX7 stages each, the
///     Fender stack between them, and the mixing network into the 12AT7 long-tail pair. The Vibrato channel adds a
///     third stage, V4B, behind the reverb mix divider, which about makes up for that divider's loss; the Normal
///     channel goes straight to the mixing resistors. Sources: Rob Robinette, "How the AB763 Deluxe Reverb works" and
///     "AB763 model differences" (robrobinette.com); the LiveSPICE redraw "Fender Vibroverb AB763"
///     (MARKTHERENCE/LiveSPICE-MK-Circuits) for the phase inverter (82k/100k, 22k tail, 470 Ω, 1 nF in, 0.1 µF out, 1M
///     grids).
/// </summary>
/// <remarks>
///     The channel switch (<see cref="SwitchRole.Switch1" />: on is Vibrato) changes the input stages' bright cap (the
///     models wire it) and, here, which coupling cap feeds the mix: the Normal channel's 0.047 µF into its 220k, or the
///     Vibrato channel's 0.02 µF into the 3.3M ‖ 10 pF reverb mix resistor, V4B, its 0.1 µF and 220k. The channel not
///     played still loads the mix node through its own 220k: V4B's plate (in the circuit) when the Normal channel
///     plays; a resistor standing for the idle V1B plate (r_p ‖ 100k, about 38k) when the Vibrato channel plays. Reverb
///     (its knob at 0: its pot's wiper at ground, so the 470k from V4B's grid goes to ground) is not modelled; the
///     vibrato is
///     <see cref="Vibrato" /> on the mixing network.
/// </remarks>
static class Ab763 {
    /// <summary>A 12AX7's grid-to-plate capacitance (datasheet: 1.7 pF).</summary>
    public const double GridPlate = 1.7e-12;

    /// <summary>
    ///     The idle Normal channel's V1B plate as seen through its coupling cap: r_p ‖ 100k at its operating point.
    /// </summary>
    const double IdlePlate = 38e3;

    /// <summary>
    ///     The vibrato's Intensity pot (50k reverse audio), whose whole track loads the Vibrato channel's junction of
    ///     its 0.1 µF and 220k mixing resistor (the drawings: the pot's top on the junction, its bottom grounded).
    /// </summary>
    const double IntensityTrack = 50e3;

    /// <summary>A capacitor switched out (open at audio).</summary>
    const double Absent = 1e-15;

    /// <summary>
    ///     The AB763 stack with the mid pot replaced by a fixed 6.8k (Deluxe, Vibroverb, Vibrolux, Bandmaster,
    ///     Tremolux): here a 13.6k pot left at noon.
    /// </summary>
    public static ToneStackDesign FixedMidStack { get; } = new(
        "Fender AB763 fixed mid",
        250e-12,
        100e3,
        100e-9,
        47e-9,
        new(250e3, PotTaper.Audio),
        new(250e3, PotTaper.Audio),
        new(13.6e3, PotTaper.Linear)
    );

    /// <summary>
    ///     The inverter's input: 1 nF from the mix node, the 1M grid resistor returning to the bias junction (which
    ///     follows the input, so the leak is bootstrapped), and the driven grid's own grid current.
    /// </summary>
    public static InverterInputDesign InverterInput { get; } = new(1e-9, 1e6);

    /// <summary>
    ///     V1B/V2B: the two second stages share one 820 Ω ‖ 25 µF cathode, so each draws its current through what is,
    ///     for its own operating point, twice that resistor; at audio the cap shorts it either way.
    /// </summary>
    /// <remarks>
    ///     With the 12AX7's grid-plate capacitance (1.7 pF, datasheet), whose Miller multiple (about 100 pF) against
    ///     the volume pot and stack ahead of it is the AB763's top-end roll-off above 10 kHz.
    /// </remarks>
    public static NetworkStageDesign SecondStage(double supply) {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, GridPlate);
        net.Output = plate;
        return new("V1B", net);
    }

    /// <summary>
    ///     The mixing network from the second stage's plate to the mix node, with V4B and the reverb mix divider on the
    ///     Vibrato channel (see the type's remarks); the mix node drives <see cref="InverterInput" />.
    /// </summary>
    /// <param name="supply">The preamp node (V4B's plate resistor).</param>
    /// <param name="normalCap">The Normal channel's coupling cap after V1B (0.047 µF).</param>
    /// <param name="vibratoCap">The Vibrato channel's coupling cap after V2B (0.02 µF).</param>
    public static NetworkStageDesign Mixer(double supply, double normalCap = 47e-9, double vibratoCap = 20e-9) {
        var net = new StageNetlist();

        // Normal: V1B's coupling cap into its 220k mixing resistor.
        var normal = net.Node();
        var mix = net.Node();
        net.SwitchedCapacitor(net.Input, normal, Absent, SwitchRole.Switch1, off: normalCap);
        net.Resistor(normal, mix, 220e3);
        net.SwitchedResistor(normal, StageNetlist.Ground, 0, IdlePlate, SwitchRole.Switch1);

        // Vibrato: V2B's coupling cap, the reverb driver's grid leak (1M, assumed), the 3.3M ‖ 10 pF reverb mix
        // resistor into V4B's grid, with its 220k grid leak and the 470k to the reverb pot's wiper (at ground with
        // reverb at 0).
        var vibrato = net.Node();
        var grid = net.Node();
        net.SwitchedCapacitor(net.Input, vibrato, vibratoCap, SwitchRole.Switch1);
        net.Resistor(vibrato, StageNetlist.Ground, 1e6);
        net.Resistor(vibrato, grid, 3.3e6);
        net.Capacitor(vibrato, grid, 10e-12);
        net.Resistor(grid, StageNetlist.Ground, 220e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);

        // V4B: 100k plate; the cathode shared with the reverb recovery V4A (820 Ω ‖ 25 µF, per tube twice the resistor;
        // the value is from a forum report, unverified). Robinette gives V4B a gain of 33.
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        var coupled = net.Node();
        net.Capacitor(plate, coupled, 0.1e-6);
        net.Resistor(coupled, mix, 220e3);
        net.Resistor(
            coupled,
            StageNetlist.Ground,
            IntensityTrack
        ); // the vibrato's Intensity pot (<see cref="Vibrato" />)

        // The mix node drives the inverter's 1 nF input cap and 1M grid resistor (<see cref="InverterInput" />).
        net.Output = mix;
        return new("Mixer", net);
    }

    /// <summary>
    ///     V1A of either channel with the fixed-mid stack, the 1M audio volume and the 120 pF bright switch (34k
    ///     stopper, 100k plate, 1.5k ‖ 25 µF): the first stage of the AB763 amps without a mid control.
    /// </summary>
    public static ToneStackStageDesign FirstStage(double supply) =>
        new(
            "V1A",
            TriodeModel.Ecc83,
            StackDriver.CommonCathode,
            supply,
            1.5e3,
            FixedMidStack,
            new(1e6, PotTaper.Audio),
            PotRole.Gain,
            100e3,
            25e-6,
            34e3,
            BrightCap: 120e-12
        );

    /// <summary>
    ///     The mixing network of the AB763 amps without reverb (Bandmaster, Tremolux, Vibrolux): the Normal channel's
    ///     0.047 µF, the Vibrato channel's 0.1 µF, each into its 220k mixing resistor; the idle channel's second-stage
    ///     plate (<see cref="IdlePlate" />) loads the mix node through its own. The vibrato's LDR, which shunts the
    ///     Vibrato channel's cap-resistor junction, is <see cref="Vibrato" />.
    /// </summary>
    public static NetworkStageDesign VibratoMixer() {
        var net = new StageNetlist();
        var (normal, vibrato, mix) = (net.Node(), net.Node(), net.Node());
        net.SwitchedCapacitor(net.Input, normal, Absent, SwitchRole.Switch1, off: 47e-9);
        net.SwitchedResistor(normal, StageNetlist.Ground, 0, IdlePlate, SwitchRole.Switch1);
        net.Resistor(normal, mix, 220e3);
        net.SwitchedCapacitor(net.Input, vibrato, 0.1e-6, SwitchRole.Switch1, off: Absent);
        net.SwitchedResistor(vibrato, StageNetlist.Ground, IdlePlate, 0, SwitchRole.Switch1);
        net.Resistor(vibrato, mix, 220e3);
        net.Resistor(
            vibrato,
            StageNetlist.Ground,
            IntensityTrack
        ); // the vibrato's Intensity pot (<see cref="Vibrato" />)
        net.Output = mix;
        return new("Mixer", net);
    }

    /// <summary>
    ///     The AB763 and AA763 vibrato's oscillator, as every one of their drawings has it (read on the Deluxe Reverb,
    ///     Twin Reverb, Super Reverb, Vibroverb, Bandmaster, Tremolux and Vibrolux Reverb AA964): a 12AX7 half with
    ///     220k from <paramref name="supply" /> (the drawings' "B" node), 2700 Ω with 25 µF; 0.02 µF from the plate to
    ///     the Speed rheostat (3M reverse audio, 100k in series, to ground), 0.01 µF to a node with 1M to the pedal's
    ///     return (the lamp driver's grid), 0.01 µF to the grid with 1M to the same return. The pedal's return is
    ///     grounded by the jack with no pedal plugged in (its 2.2M or 22M to the bias line only holds it with the pedal
    ///     open, which stops the oscillator).
    /// </summary>
    public static PhaseShiftOscillatorDesign VibratoOscillator(double supply) =>
        new(
            TriodeModel.Ecc83,
            supply,
            220e3,
            2.7e3,
            0.02e-6,
            0.01e-6,
            0.01e-6,
            100e3,
            1e6,
            1e6,
            new(3e6, PotTaper.ReverseAudio)
        );

    /// <summary>
    ///     The AB763 and AA763 vibrato (<see cref="OpticalTremoloDesign" />): the oscillator above; the other 12AX7
    ///     half as the lamp driver, its grid on the ladder's second node, its cathode on
    ///     <paramref name="driverCathode" /> with 25 µF (100k; the Tremolux's 56k), the neon lamp and 100k from the "B"
    ///     node to its plate with 10M across them; the photocell from the 50k reverse-audio Intensity pot's wiper to
    ///     ground, the pot's top on the Vibrato channel's junction of its 0.1 µF coupling cap and its 220k mixing
    ///     resistor, the pot's bottom grounded. The junction's resistance: the stage's plate (100k ‖ r_p, about 38k)
    ///     against the 220k into the mix node (the other channel's 220k and plate, and the inverter's 1M, about 205k):
    ///     35k. The vibrato acts with the Vibrato channel playing (<see cref="SwitchRole.Switch1" />).
    /// </summary>
    /// <remarks>
    ///     The Intensity pot's track (50k from the junction to ground) is in the mixing networks (<see cref="Mixer" />,
    ///     <see cref="VibratoMixer" />), on whichever channel plays: the cell's shunt acts against it.
    /// </remarks>
    public static OpticalTremoloDesign Vibrato(double supply, double driverCathode = 100e3) =>
        new(
            VibratoOscillator(supply),
            supply,
            driverCathode,
            100e3,
            new(IntensityTrack, PotTaper.ReverseAudio),
            35e3,
            SwitchRole.Switch1
        );

    /// <summary>The AB763 long-tail pair: a 12AT7, 82k and 100k plates, 22k tail, 470 Ω bias.</summary>
    public static LongTailPairDesign PhaseInverter(double supply) =>
        new(TriodeModel.Ecc81, supply, 82e3, 100e3, 22e3, 470);

    /// <summary>
    ///     The AB763 feedback: 820 Ω from a speaker tap into the tail's bottom resistor (<paramref name="shunt" />: 47
    ///     Ω on the Deluxe, 100 Ω on the Twin and Super). These amps have no presence control: the presence cap is
    ///     negligible, so the hidden knob does nothing.
    /// </summary>
    public static FeedbackDesign Feedback(double tapGain, double shunt) =>
        new(tapGain, 820, shunt, new(1e3, PotTaper.Linear), 1e-12);
}
