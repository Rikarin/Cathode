using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Lichtlaerm Audio Prometheus, clean channel. <b>The topology is inferred, not read from a schematic</b>: none is
///     published. The maker describes two voices, Warm (after a Verellen Meatsmoke) and Brilliant (a Fender Twin with a
///     touch of Hiwatt), with Bass, Middle, Treble and Volume, into the same power amp as the lead channel (see
///     <see cref="Prometheus" />). Built here as a Fender-style channel: V1A with the stack on its plate and the volume
///     after it, V2A, the shared master, then the lead channel's long-tail pair and four EL34. Brilliant
///     (the bright switch, <see cref="SwitchRole.Bright" />) is a bright cap across the volume; Warm is without it.
/// </summary>
public static class PrometheusClean {
    /// <summary>B+ of the preamp node.</summary>
    const double Preamp = 330;

    public static AmpDesign Design { get; } = new(
        "Prometheus Clean",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                Preamp,
                1.5e3,
                ToneStackDesign.Fender,
                new(1e6, PotTaper.Audio),
                PotRole.Gain,
                100e3,
                25e-6,
                68e3,
                1e6,
                120e-12
            ),
            new NetworkStageDesign("V2A", SecondStage()),
            new CouplingStageDesign("Master", 22e-9, new(1e6, PotTaper.Audio), PotRole.Master)
        ],
        Prometheus.Design.Power,
        -3
    ) { Panel = AmpPanel.Standard with { Gain = "Volume", Cut = null, Bright = "Brilliant" } };

    /// <summary>
    ///     V2A, its grid on the volume's wiper, with the 12AX7's grid-plate capacitance: its Miller effect against the
    ///     wiper's resistance is the channel's treble roll-off.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (plate, cathode, supply) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, Preamp);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Capacitor(net.Input, plate, 1.7e-12);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }
}
