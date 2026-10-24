using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Concert 6G12 (brownface, 40 W, 4 × 10): the Normal or Vibrato channel (<see cref="AmpPanel.Switch1" />), each
///     a 7025 stage into the brownface tone network and the Volume, a second 7025 stage, then on the Vibrato channel the
///     harmonic vibrato's crossover and mixer pair (the dry sound runs through them too), with Speed, Intensity and the
///     vibrato pedal (<see cref="AmpPanel.Switch2" />); a 7025 long-tail pair, two 6L6GC in fixed bias at 460 V on a
///     silicon rectifier, and 6.8k of feedback from the 2 Ω tap into 1.5k with the 5k Presence and 0.1 µF across it.
///     Sources: the Fender drawing and layout "Concert 6G12" (A-FJ; schematicheaven.net/fenderamps/concert_6g12_schem.pdf);
///     <see cref="Brownface" /> and <see cref="HarmonicVibratoStageDesign" /> for the shared circuit and its readings.
/// </summary>
/// <remarks>
///     Read: the power section (456 V plates, 458 V and 430 V nodes, 470 Ω 1 W screens, 0.05 µF and 220k into the grids,
///     −55 V bias, no grid stoppers, three silicon diodes per leg into 2 × 20 µF, the 125C1A choke) and the inverter's
///     315 / 310 V plates. Unverified: the 6L6 idle current (35 mA, as the AB763 models; the 6L6GC's Koren curves give
///     21 mA at the drawing's −55 V with 458 V screens, colder than such an amp's 6L6s run), the output transformer
///     45249's primary (4.2k : 2 Ω assumed, as the Super Reverb's 125A9A), the supply's source resistance, the four 10s'
///     impedance curve (the 4 × 10 preset). The inverter's 47 pF between its plates is left out.
/// </remarks>
public static class Concert6G12 {
    public static AmpDesign Design { get; } = new(
        "Concert 6G12",
        [Brownface.Channels()],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 40,
            PhaseInverter: Brownface.PhaseInverter,
            CouplingCap: 0.05e-6,
            GridLeak: 220e3,
            GridStopper: 1,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(0.5, 6.8e3, 1.5e3, new(5e3, PotTaper.Linear), 0.1e-6),
            HasFeedback: true,
            Transformer: new(4.2e3),
            Speaker: SpeakerDesign.OpenBack4x10,
            PowerSupply: new(40, 40e-6, 100, 20e-6, 4.7e3, 20e-6, 6e-3, RectifierModel.Silicon),
            InverterInput: new(1e-9, 1e6)
        ),
        -5
    ) {
        Panel = Brownface.Panel
    };
}
