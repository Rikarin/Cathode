using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A common-cathode triode gain stage with its input network, from the source to the grid: optional coupling cap,
///     volume pot (with an optional bright cap across its upper half), series resistor (a mixing resistor or grid
///     stopper, optionally bypassed by a cap) and grid leak; plate resistor from the supply, cathode resistor with an
///     optional bypass cap. Every capacitance in farads, resistance in ohms; 0 leaves the element out.
/// </summary>
/// <param name="Name">A label, e.g. "V1A".</param>
/// <param name="Tube">The triode.</param>
/// <param name="Supply">Nominal B+ at the top of the plate resistor, in volts.</param>
/// <param name="PlateResistor">From the supply to the plate.</param>
/// <param name="CathodeResistor">From the cathode to ground.</param>
/// <param name="CathodeBypass">Across the cathode resistor.</param>
/// <param name="InputResistance">Resistance in series with the source (an input-jack resistor, a grid stopper).</param>
/// <param name="CouplingCap">Series cap from the source; 0 couples the source directly (DC).</param>
/// <param name="Volume">A pot after the coupling cap, wiper towards the grid; null for none.</param>
/// <param name="VolumeRole">The knob that turns <paramref name="Volume" />.</param>
/// <param name="BrightCap">Across the upper half of the volume pot while the bright switch is on.</param>
/// <param name="SeriesResistor">Between the volume wiper and the grid (Marshall's 470k mixing resistor).</param>
/// <param name="SeriesBypass">Across <paramref name="SeriesResistor" />.</param>
/// <param name="GridLeak">Grid to ground.</param>
/// <param name="BrightSwitch">The switch that engages <paramref name="BrightCap" />.</param>
/// <param name="PlateBypass">
///     Across the plate resistor (a cold clipper's treble cut, 1 nF on the SLO's third stage). The stage is then built
///     as a <see cref="NetworkStage{TDevice}" /> from <see cref="ToNetwork" />: the same circuit, one port more.
/// </param>
/// <remarks>
///     A cold clipper is this stage with a large unbypassed cathode resistor (10k-39k): biased near cut-off, it clips
///     the bottom of the wave hard, while the previous stage's grid current into it rounds the top.
/// </remarks>
public sealed record TriodeStageDesign(
    string Name,
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double CathodeResistor,
    double CathodeBypass = 0,
    double InputResistance = 0,
    double CouplingCap = 0,
    Pot? Volume = null,
    PotRole VolumeRole = PotRole.Gain,
    double BrightCap = 0,
    double SeriesResistor = 0,
    double SeriesBypass = 0,
    double GridLeak = 0,
    SwitchRole BrightSwitch = SwitchRole.Bright,
    double PlateBypass = 0
) : AmpStageDesign(Name) {
    public override AmpStage Create(double sampleRate) =>
        PlateBypass > 0 ? ToNetwork().Create(sampleRate) : new TriodeStage(this, sampleRate);

    /// <summary>The same stage as a general <see cref="NetworkStageDesign" />.</summary>
    public NetworkStageDesign ToNetwork() {
        var net = new StageNetlist();
        var top = net.Input;
        if (CouplingCap > 0) {
            top = net.Node();
            net.Capacitor(net.Input, top, CouplingCap);
        }

        var wiper = top;
        if (Volume is { } pot) {
            wiper = net.Pot(top, StageNetlist.Ground, pot, VolumeRole);
            if (BrightCap > 0) {
                net.SwitchedCapacitor(top, wiper, BrightCap, BrightSwitch, CapacitorRole.Bright);
            }
        }

        var grid = wiper;
        if (SeriesResistor > 0) {
            grid = net.Node();
            net.Resistor(wiper, grid, SeriesResistor);
            if (SeriesBypass > 0) {
                net.Capacitor(wiper, grid, SeriesBypass);
            }
        }

        if (GridLeak > 0) {
            net.Resistor(grid, StageNetlist.Ground, GridLeak);
        }

        var (plate, cathode, supply) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, Supply);
        net.Resistor(supply, plate, PlateResistor);
        if (PlateBypass > 0) {
            net.Capacitor(supply, plate, PlateBypass, CapacitorRole.Plate);
        }

        net.CathodeResistor(cathode, StageNetlist.Ground, CathodeResistor);
        if (CathodeBypass > 0) {
            net.Capacitor(cathode, StageNetlist.Ground, CathodeBypass);
        }

        net.Triode(plate, grid, cathode, Tube);
        net.Output = plate;
        return new(Name, net, InputResistance);
    }
}

/// <summary>
///     A <see cref="TriodeStageDesign" /> as one R-type root with the triode's two ports (grid-cathode, plate-cathode)
///     at the top, solved by Newton's method per sample (<see cref="RTypeRoot{TPorts,TDevice}" />). Every possible
///     capacitor is a port; one the design leaves out is declared between ground and ground, where it never charges.
///     The output is the plate's open-circuit voltage, the next stage adding <see cref="OutputResistance" />.
/// </summary>
public sealed class TriodeStage : AmpStage {
    const double Off = 1e-15; // a switched-off bright cap

    readonly PotDivider? volume;
    readonly int gridProbe;
    readonly int cathodeProbe;
    readonly int cathodeIndex;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor, Capacitor>, FastTriode> root;
    TriodeModel tube; // as the operating point sees it (with the hardness)
    PreampSettings applied = PreampSettings.Stock;
    double cathodeResistor;
    double brightScale = 1;
    double sourceResistance;
    double rotation = double.NaN;
    bool bright;
    double sampleRate;
    double supplyScale = 1;
    double outputResistance;
    double restPlate;

    public TriodeStageDesign Design { get; }

    public override double OutputResistance => outputResistance;

    public override double RestOutput => restPlate;

    /// <summary>Plate current at the operating point, in amperes.</summary>
    public double RestCurrent { get; private set; }

    public double RestCathode { get; private set; }

    public override NewtonSolver Newton => root.Newton;

    /// <summary>Grid voltage at the last sample.</summary>
    public double Grid => root.Probe(gridProbe);

    public double Cathode => root.Probe(cathodeProbe);

    public override bool IsGainStage => true;

    public TriodeStage(TriodeStageDesign design, double sampleRate) {
        Design = design;
        this.sampleRate = sampleRate;
        tube = design.Tube;
        cathodeResistor = design.CathodeResistor;
        var net = new WdfNetlist();
        var input = net.Node();
        net.Port(input, WdfNetlist.Ground);

        var top = input;
        if (design.CouplingCap > 0) {
            top = net.Node();
            net.Port(input, top);
        } else {
            net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
        }

        var wiper = top;
        if (design.Volume is { } pot) {
            var divider = PotDivider.Add(net, top, WdfNetlist.Ground, pot);
            volume = divider;
            wiper = divider.Wiper;
        }

        if (design.Volume is not null && design.BrightCap > 0) {
            net.Port(top, wiper);
        } else {
            net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
        }

        var grid = wiper;
        if (design.SeriesResistor > 0) {
            grid = net.Node();
            net.Resistor(wiper, grid, design.SeriesResistor);
        }

        if (design.SeriesResistor > 0 && design.SeriesBypass > 0) {
            net.Port(wiper, grid);
        } else {
            net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
        }

        if (design.GridLeak > 0) {
            net.Resistor(grid, WdfNetlist.Ground, design.GridLeak);
        }

        var (plate, cathode, supply) = (net.Node(), net.Node(), net.Node());
        net.Resistor(supply, plate, design.PlateResistor);
        cathodeIndex = net.Resistor(cathode, WdfNetlist.Ground, design.CathodeResistor);
        if (design.CathodeBypass > 0) {
            net.Port(cathode, WdfNetlist.Ground);
        } else {
            net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
        }

        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Triode(plate, grid, cathode);
        net.Probe(plate);
        gridProbe = net.Probe(grid);
        cathodeProbe = net.Probe(cathode);

        sourceResistance = design.InputResistance;
        root = new(
            net,
            new(
                new(Math.Max(sourceResistance, 1)),
                new(Capacitance(design.CouplingCap), sampleRate),
                new(Off, sampleRate),
                new(Capacitance(design.SeriesBypass), sampleRate),
                new(Capacitance(design.CathodeBypass), sampleRate)
            ),
            new(design.Tube)
        );
        root.SetVoltage(0, design.Supply);
        root.Newton.Linearise = true;
        Update(new(0.5, 0.5, 0.5, 0.5, 1, false));
        Reset(0);
    }

    public override void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        root.Ports.B.Configure(Capacitance(Design.CouplingCap), sampleRate);
        root.Ports.C.Configure(bright ? Capacitance(Design.BrightCap * brightScale) : Off, sampleRate);
        root.Ports.D.Configure(Capacitance(Design.SeriesBypass), sampleRate);
        root.Ports.E.Configure(Capacitance(Design.CathodeBypass), sampleRate);
        root.Propagate();
    }

    public override void SetSourceResistance(double ohms) {
        if (TrySetSourceResistance(ref sourceResistance, ohms, Design.InputResistance)) {
            root.Ports.A.Value = sourceResistance;
            root.Propagate();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) {
        supplyScale = scale;
        root.SetVoltage(0, Design.Supply * scale);
    }

    public override void Update(in AmpControls controls) {
        if (volume is not { } pot) {
            return;
        }

        var turn = controls.Rotation(Design.VolumeRole);
        var on = controls.IsOn(Design.BrightSwitch);
        if (turn == rotation && on == bright) {
            return;
        }

        if (on != bright) {
            bright = on;
            root.Ports.C.Configure(bright ? Capacitance(Design.BrightCap * brightScale) : Off, sampleRate);
        }

        rotation = turn;
        var (upper, lower) = pot.Resistances(turn);
        root.SetResistance(pot.Top, upper);
        root.SetResistance(pot.Bottom, lower);
        root.Propagate();
    }

    public override void Apply(in PreampSettings settings) {
        if (settings.ChangesDevices(applied)) {
            root.Device = settings.Triode(Design.Tube);
        }

        applied = settings;
        tube = settings.Law(Design.Tube);
        var changed = false;
        var brightCap = Design.VolumeRole == PotRole.Master ? 1 : settings.BrightCap;
        if (brightCap != brightScale) {
            brightScale = brightCap;
            root.Ports.C.Configure(bright ? Capacitance(Design.BrightCap * brightScale) : Off, sampleRate);
            changed = true;
        }

        var rk = Design.CathodeResistor * settings.CathodeResistor;
        if (rk != cathodeResistor) {
            cathodeResistor = rk;
            root.SetResistance(cathodeIndex, rk);
            changed = true;
        }

        if (changed) {
            root.Propagate();
        }
    }

    public override void Retune(double restInput) => SolveRest(restInput);

    public override void Reset(double restInput) {
        var (top, wiper, grid, plate, cathode) = SolveRest(restInput);
        root.Reset();
        root.SetVoltage(0, Design.Supply * supplyScale);
        root.Ports.B.Precharge(Design.CouplingCap > 0 ? restInput : 0);
        root.Ports.C.Precharge(top - wiper);
        root.Ports.D.Precharge(wiper - grid);
        root.Ports.E.Precharge(cathode);
        root.SetInitialVoltages([grid - cathode, plate - cathode]);
    }

    /// <summary>The operating point (rest values and output resistance) and the input chain's DC nodes.</summary>
    (double Top, double Wiper, double Grid, double Plate, double Cathode) SolveRest(double restInput) {
        // DC: the input chain is a ladder (source, pot, series resistor, grid leak) unless a coupling cap blocks it.
        var source = Design.CouplingCap > 0 ? 0 : restInput;
        var sourceDrop = 0.0;
        var top = source;
        var wiper = source;
        var grid = source;
        if (Design.CouplingCap <= 0) {
            var (upper, lower) = volume is { } pot ? pot.Resistances(rotation) : (0.0, double.PositiveInfinity);
            var after = Design.GridLeak > 0 ? Design.SeriesResistor + Design.GridLeak : double.PositiveInfinity;
            var shunt = Parallel(lower, after);
            var current = double.IsInfinity(shunt) ? 0 : source / (sourceResistance + upper + shunt);
            sourceDrop = current * sourceResistance;
            top = source - sourceDrop;
            wiper = double.IsInfinity(shunt) ? top : current * shunt;
            grid = Design.GridLeak > 0 ? wiper * Design.GridLeak / (Design.SeriesResistor + Design.GridLeak) : wiper;
        }

        var (ia, plate, cathode) = OperatingPoint.CommonCathode(
            tube,
            grid,
            Design.Supply * supplyScale,
            Design.PlateResistor,
            cathodeResistor
        );
        RestCurrent = ia;
        restPlate = plate;
        RestCathode = cathode;
        var (rp, mu) = OperatingPoint.SmallSignal(tube, grid - cathode, plate - cathode);
        var cathodeImpedance = Design.CathodeBypass > 0 ? 0 : cathodeResistor;
        outputResistance = Parallel(Design.PlateResistor, rp + (mu + 1) * cathodeImpedance);
        return (top, wiper, grid, plate, cathode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        root.Ports.A.Volts = input;
        return root.Tick();
    }

    public override void FlushDenormals() {
        Flush.Capacitor(ref root.Ports.C);
        Flush.Capacitor(ref root.Ports.D);
    }

    static double Capacitance(double farads) => farads > 0 ? farads : 1e-9;

    static double Parallel(double a, double b) => double.IsInfinity(a) ? b : double.IsInfinity(b) ? a : a * b / (a + b);
}
