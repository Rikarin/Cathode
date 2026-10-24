using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Tweed Deluxe 5E3 (about 15 W): the Instrument and Microphone channels' V1 halves (12AY7, 100k plates, a
///     shared 820 Ω ‖ 25 µF cathode, 34k stoppers), each plate's 0.1 µF into its 1M volume's wiper, the volumes' tops tied
///     at V2A's grid (wired "backwards": the volumes are V2A's grid leak, and the channel not played loads the one played,
///     about 1M with its volume at 0 down to its V1 plate with it at 10), the 500 pF bright cap across the Instrument
///     volume, the tone control, V2A (12AX7, 100k, 1.5k ‖ 25 µF), 0.1 µF into a cathodyne on V2B (56k plate, 56k cathode,
///     1.5k bias), two 6V6GT cathode-biased on a shared 250 Ω with no screen resistors, an 8k : 8 Ω transformer, a 5Y3,
///     and
///     no feedback. Panel: Instrument and Mic volumes, Tone; <b>Mic Input</b> plays the Microphone channel.
///     <see cref="Jumped" /> is the same amp with the channels jumpered.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Rob Robinette, "How the 5E3 Deluxe works" (shared 820 Ω, 25 µF, 34k, 1M volumes as V2A's grid leak,
///         the unused volume's load "from 100k at maximum to 1M at minimum", the 500 pF bright cap across the bright
///         volume, V2A's 1.5k, 56k cathodyne, 250 Ω cathode at 38 mA per tube, no screen resistors, B+ 370 / 295 /
///         250 V, 8k : 8 Ω, no feedback); AmpBooks "Fender Deluxe 5E3" (0.005 µF and 500 pF tone caps, the cathodyne's
///         56k/56k/1.5k and 220k grid leaks, 0.1 µF coupling caps); the Fractal forum thread on the 5E3's channel
///         interaction (volumes wired input-to-wiper, output from the track's top).
///     </para>
///     <para>
///         With one input played, the other V1 half is linear (its grid at rest): its plate is about 21k from its own DC
///         voltage, through its coupling cap into its volume's wiper, which is how the volume not played loads the one
///         played. The played half gets the shared cathode's per-tube equivalent (1.64k ‖ 12.5 µF, as AmpBooks gives it),
///         which leaves out the idle half's grounded-grid pull on the cathode below about 30 Hz. Jumped, the stage holds
///         both triodes exactly, at about 5 % more CPU.
///     </para>
///     <para>
///         Unverified, reconstructed from those descriptions: the tone control's wiring, taken as a 1M audio pot from
///         V2A's grid in series with 0.005 µF to ground (up is brighter); the power tubes' cathode bypass (25 µF); the
///         supply's droppers (10k to the screen node, 18k to the preamp), its source resistance and caps (16 µF each).
///         The cathodyne's 1M grid leak returns to its bias junction, which follows the grid at about 0.93: V2A's
///         coupling cap feeds the 10M it amounts to. The design's idle current is stated with the screens at B+ (as
///         every model's is); 47 mA there gives about 35 mA per tube with the screens on their 280 V node, near
///         Robinette's 38 mA.
///     </para>
/// </remarks>
public static class TweedDeluxe {
    /// <summary>A resistor switched out, and one switched in as a wire.</summary>
    const double Open = 1e12;

    const double Wire = 1;

    /// <summary>The idle V1 half's plate: a 12AY7's r_p (about 27k) in parallel with its 100k.</summary>
    const double IdlePlate = 21e3;

    public static AmpDesign Design { get; } = Build("Tweed Deluxe 5E3", InputStage(), "Mic Input");

    /// <summary>The 5E3 with its channels jumpered: the guitar into the Instrument channel, a cable on to the Microphone one.</summary>
    public static AmpDesign Jumped { get; } = Build("Tweed Deluxe 5E3 Jumped", JumpedStage(), null);

    static AmpDesign Build(string name, NetworkStageDesign input, string? channelSwitch) =>
        new(
            name,
            [
                input,
                SecondStage()
            ],
            new(
                "2 x 6V6GT",
                PentodeModel.SixV6GT,
                1,
                370,
                1,
                0.047,
                true,
                25e-6,
                15,
                new CathodyneDesign(TriodeModel.Ecc83, 250, 56e3, 56e3, 1.5e3),
                0.1e-6,
                220e3,
                1,
                0,
                new(1e6, PotTaper.Linear),
                new(1, 22e3, 100, new(1e3, PotTaper.Linear), 1e-12),
                false,
                new(8e3),
                SpeakerDesign.OpenBack1x12,
                new(150, 16e-6, 10e3, 16e-6, 18e3, 16e-6, 2.5e-3, RectifierModel.FiveY3)
            ),
            -9
        ) {
            Panel = new() {
                Gain = "Instr Vol",
                Control1 = "Mic Vol",
                Treble = "Tone",
                Bass = null,
                Middle = null,
                Presence = null,
                Depth = null,
                Cut = null,
                Master = null,
                Bright = null,
                Switch1 = channelSwitch
            }
        };

    /// <summary>
    ///     The played half of V1 (Instrument, or Microphone with <see cref="SwitchRole.Switch1" />) into its volume's wiper,
    ///     the idle half's plate as <see cref="IdlePlate" />; the volumes' tops at V2A's grid (the output), the bright cap
    ///     and the tone control.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail, idle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 12.5e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.TwelveAY7);
        net.Supply(idle, Bassman.IdlePlateVoltage(250, 2 * 820));

        var (instrument, mic) = (net.Node(), net.Node());
        net.SwitchedResistor(plate, instrument, Wire, Open, SwitchRole.Switch1);
        net.SwitchedResistor(idle, instrument, Open, IdlePlate, SwitchRole.Switch1);
        net.SwitchedResistor(plate, mic, Open, Wire, SwitchRole.Switch1);
        net.SwitchedResistor(idle, mic, IdlePlate, Open, SwitchRole.Switch1);
        net.Output = Volumes(net, instrument, mic);
        return new("V1", net, 34e3);
    }

    /// <summary>Both V1 halves on their shared cathode, both grids driven (68k each); the rest as <see cref="InputStage" />.</summary>
    static NetworkStageDesign JumpedStage() {
        var net = new StageNetlist();
        var (instrumentGrid, micGrid) = (net.Node(), net.Node());
        net.Resistor(net.Input, instrumentGrid, 68e3);
        net.Resistor(net.Input, micGrid, 68e3);
        net.Resistor(instrumentGrid, StageNetlist.Ground, 1e6);
        net.Resistor(micGrid, StageNetlist.Ground, 1e6);

        var (instrumentPlate, micPlate, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, instrumentPlate, 100e3);
        net.Resistor(rail, micPlate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(instrumentPlate, instrumentGrid, cathode, TriodeModel.TwelveAY7);
        net.Triode(micPlate, micGrid, cathode, TriodeModel.TwelveAY7);
        net.Output = Volumes(net, instrumentPlate, micPlate);
        return new("V1", net);
    }

    /// <summary>
    ///     Each plate's 0.1 µF into its volume's wiper, the volumes' tops tied at V2A's grid, the bright cap across the
    ///     Instrument volume's upper part, the tone control from the grid; returns the grid.
    /// </summary>
    static int Volumes(StageNetlist net, int instrumentPlate, int micPlate) {
        var grid = net.Node();
        var instrument = net.Pot(grid, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(instrumentPlate, instrument, 0.1e-6);
        net.Capacitor(instrument, grid, 500e-12, CapacitorRole.Bright);
        var mic = net.Pot(grid, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Capacitor(micPlate, mic, 0.1e-6);

        var tone = net.Node();
        net.Rheostat(grid, tone, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(tone, StageNetlist.Ground, 5e-9, CapacitorRole.Stack);
        return grid;
    }

    /// <summary>
    ///     V2A (100k, 1.5k ‖ 25 µF) and its 0.1 µF into the cathodyne's grid. Its grid-plate capacitance's Miller multiple
    ///     against the volume network's high impedance is part of the 5E3's top end.
    /// </summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, 10e6);
        net.Output = output;
        return new("V2A", net);
    }
}
