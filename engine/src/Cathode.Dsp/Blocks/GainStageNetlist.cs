using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The skeleton the single-device gain stages' netlists share (<see cref="TransistorStage" />, a common-emitter
///     transistor, and <see cref="DriveTriodeStage" />, a common-cathode triode): six nodes, the input, the device's
///     control, output and common electrodes, the stage output and the supply, and four ports: the source (port 0), the
///     input coupling capacitor, the common electrode's bypass capacitor and the output coupling capacitor.
/// </summary>
static class GainStageNetlist {
    /// <summary>A new netlist with the skeleton's nodes and ports; the stage adds its resistors and device.</summary>
    public static (WdfNetlist Net, int Input, int Control, int Drain, int Common, int Output, int Supply) Create() {
        var net = new WdfNetlist();
        var (input, control, drain, common, output, supply) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, control);
        net.Port(common, WdfNetlist.Ground);
        net.Port(drain, output);
        return (net, input, control, drain, common, output, supply);
    }
}
