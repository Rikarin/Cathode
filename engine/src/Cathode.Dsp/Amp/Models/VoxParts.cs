using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>What the Vox-style models (AC30, AC15, AC15C1, DC-30) share.</summary>
static class VoxParts {
    /// <summary>
    ///     The Vox output stage: a 12AX7 long-tail pair (100k plates, 1.2k bias, 47k tail, 1M bootstrapped leak behind
    ///     <paramref name="inverterCap" />), <paramref name="couplingCap" /> into 220k leaks, the cut
    ///     (<paramref name="cutPot" /> and <paramref name="cutCap" />) across the inverter's outputs, EL84s on a shared
    ///     <paramref name="cathodeResistor" /> with <paramref name="cathodeBypass" />, no feedback (the feedback
    ///     network only applies when the Feedback knob is turned past noon).
    /// </summary>
    /// <param name="name">The power amp's label.</param>
    /// <param name="tubesPerSide">1 for two EL84, 2 for four.</param>
    /// <param name="supply">B+ at the centre tap.</param>
    /// <param name="idleCurrent">Per tube, for a replacement tube type.</param>
    /// <param name="cathodeResistor">The shared cathode resistor.</param>
    /// <param name="cathodeBypass">Its bypass cap.</param>
    /// <param name="ratedPower">In watts.</param>
    /// <param name="inverterSupply">The inverter's node.</param>
    /// <param name="inverterCap">The cap into the inverter's grid.</param>
    /// <param name="couplingCap">The inverter's output caps.</param>
    /// <param name="cutCap">The cut's cap.</param>
    /// <param name="plateToPlate">The output transformer's primary.</param>
    /// <param name="speaker">The speaker load.</param>
    /// <param name="powerSupply">The supply.</param>
    /// <param name="cutPot">
    ///     The cut's pot; null for the JMI amps' 250k log (its resistance falling fastest as the cut comes in).
    /// </param>
    /// <param name="screenResistor">Per tube.</param>
    /// <param name="gridStopper">Per tube.</param>
    /// <param name="shuntMaster">A master across the inverter's outputs; null for none.</param>
    public static PowerAmpDesign PowerAmp(
        string name,
        int tubesPerSide,
        double supply,
        double idleCurrent,
        double cathodeResistor,
        double cathodeBypass,
        double ratedPower,
        double inverterSupply,
        double inverterCap,
        double couplingCap,
        double cutCap,
        double plateToPlate,
        SpeakerDesign speaker,
        PowerSupplyDesign powerSupply,
        Pot? cutPot = null,
        double screenResistor = 100,
        double gridStopper = 1.5e3,
        Pot? shuntMaster = null
    ) =>
        new(
            name,
            PentodeModel.EL84,
            tubesPerSide,
            supply,
            screenResistor,
            idleCurrent,
            true,
            cathodeBypass,
            ratedPower,
            new LongTailPairDesign(TriodeModel.Ecc83, inverterSupply, 100e3, 100e3, 47e3, 1.2e3),
            couplingCap,
            220e3,
            gridStopper,
            cutCap,
            cutPot ?? new(250e3, PotTaper.ReverseAudio),
            new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            false,
            new(plateToPlate),
            speaker,
            powerSupply,
            InverterInput: new(inverterCap, 1e6),
            ShuntMaster: shuntMaster,
            CathodeResistor: cathodeResistor
        );

    /// <summary>
    ///     A 12AX7 gain stage in <paramref name="net" />: a rail at <paramref name="supply" />, 100k to the plate,
    ///     <paramref name="cathodeResistor" /> with <paramref name="cathodeBypass" /> across it. Returns the rail, the
    ///     plate and the cathode.
    /// </summary>
    public static (int Rail, int Plate, int Cathode) GainStage(
        StageNetlist net,
        int grid,
        double supply,
        double cathodeResistor,
        double cathodeBypass
    ) {
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, cathodeBypass);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return (rail, plate, cathode);
    }
}
