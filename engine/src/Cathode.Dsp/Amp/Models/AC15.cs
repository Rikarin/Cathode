using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Vox AC15 (early 1960s, EF86 Normal channel), from AmpBooks' analysis of it
///     (https://ampbooks.com/mobile/classic-circuits/vox-ac15/): the EF86 channel, or (Switch 1) a 12AX7 channel. The
///     EF86 runs on a 220k plate, a 1M screen resistor with 0.1 µF, 2.2k with 25 µF, from 291 V (plate about 90 V); it
///     couples through 0.01 µF, and with the Brilliance switch on through 250 pF in series as well (a bass cut near
///     900 Hz), into a 500k log volume. A 12AX7 long-tail pair (100k plates, 1.2k bias, 47k tail, from the 291 V node:
///     plates about 225 V) drives two EL84 through 0.01 µF and 220k leaks; the EL84 share 130 Ω (about 38 mA each),
///     100 Ω screens, an 8k plate-to-plate transformer, no feedback.
/// </summary>
/// <remarks>
///     Not from an AC15 drawing (none could be read), so marked as assumptions: the channels mix as on the AC30
///     (OS/065), each volume's wiper through 220k into the inverter's grid (47 nF, 1M bootstrapped leak), the idle
///     channel's volume at zero; the 12AX7 channel is the AC30's Normal channel (220k plate, a shared 1.5k with 25 µF,
///     47 nF into its 500k log volume), without the vibrato and tremolo; the EF86's 68k input resistor and 1M leak;
///     the cut control (the AC30's 250k log and 4.7 nF across the grids), 1.5k grid stoppers, the cathode bypass
///     (50 µF), the EZ81 rectifier with an 8 H choke (as on the AC15 Amplifier No.2), 32 µF and 16 µF filter caps,
///     B+ 320 V at the centre tap (310 V measured at the plates).
/// </remarks>
public static class AC15 {
    public static AmpDesign Design { get; } = new(
        "AC15",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [Ef86()],
                [AC30.FirstStage("V1"), AC30.NormalVolume("Vib-Trem volume", 47e-9)]
            )
        ],
        new(
            "2 x EL84",
            PentodeModel.EL84,
            TubesPerSide: 1,
            Supply: 320,
            ScreenResistor: 100,
            IdleCurrent: 0.034,
            CathodeBias: true,
            CathodeBypass: 50e-6,
            RatedPower: 16,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 291, 100e3, 100e3, 47e3, 1.2e3),
            CouplingCap: 0.01e-6,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 4.7e-9,
            CutPot: new(250e3, PotTaper.ReverseAudio), // log track, its resistance falling fastest as the cut comes in
            Feedback: new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: false,
            Transformer: new(8e3),
            Speaker: SpeakerDesign.OpenBack1x12,
            // The 22k dropper to the EF86's 291 V: about 1.2 mA.
            PowerSupply: new(80, 32e-6, 150, 16e-6, 22e3, 16e-6, 1.2e-3, RectifierModel.EZ81),
            InverterInput: new(47e-9, 1e6),
            CathodeResistor: 130
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Bass = null,
            Middle = null,
            Treble = null,
            Master = null,
            Presence = null,
            Depth = null,
            Bright = "Brilliance",
            Switch1 = "12AX7 Ch"
        }
    };

    /// <summary>
    ///     The EF86 channel: input resistor and grid leak, the pentode (suppressor on the cathode), its coupling caps and
    ///     the Brilliance switch (on: the 250 pF stays in series; off: a short across it), the volume (Gain), its wiper
    ///     through 220k into the mixing node with the other channel's 220k to ground.
    /// </summary>
    static NetworkStageDesign Ef86() {
        var net = new StageNetlist();
        var (supply, plate, screen, cathode, coupled, top, mix) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 291);
        net.Resistor(supply, plate, 220e3);
        net.Resistor(supply, screen, 1e6);
        net.Capacitor(screen, StageNetlist.Ground, 0.1e-6);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Pentode(plate, screen, net.Input, cathode, PentodeModel.EF86);
        net.Capacitor(plate, coupled, 0.01e-6);
        net.Capacitor(coupled, top, 250e-12, CapacitorRole.Bright);
        net.SwitchedResistor(coupled, top, 10, 1e12, SwitchRole.Bright);
        var wiper = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Resistor(wiper, mix, 220e3);
        net.Resistor(mix, StageNetlist.Ground, 220e3);
        net.Output = mix;
        return new("EF86", net, 68e3);
    }
}
