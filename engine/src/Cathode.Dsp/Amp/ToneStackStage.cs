using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>What drives a <see cref="ToneStackStage" />'s tone stack.</summary>
public enum StackDriver {
    /// <summary>
    ///     A cathode follower (Marshall, Vox top boost): plate at the supply, the stack hangs on the cathode.
    /// </summary>
    CathodeFollower,

    /// <summary>A common-cathode stage (Fender): the stack hangs on the plate.</summary>
    CommonCathode
}

/// <summary>
///     A triode driving a passive tone stack, which drives a pot (a volume or master) whose wiper is the output.
/// </summary>
/// <param name="Name">A label, e.g. "V2B".</param>
/// <param name="Tube">The triode.</param>
/// <param name="Driver">Cathode follower or common cathode.</param>
/// <param name="Supply">Nominal B+ at the plate (or the plate resistor), in volts.</param>
/// <param name="CathodeResistor">Cathode to ground.</param>
/// <param name="Stack">The tone stack.</param>
/// <param name="InputResistance">In series with the source (a grid stopper, an input-jack resistor).</param>
/// <param name="GridLeak">Grid to ground; 0 for a grid DC-coupled to the previous plate (a cathode follower).</param>
/// <param name="PlateResistor">Common cathode only.</param>
/// <param name="CathodeBypass">Common cathode only; 0 for none.</param>
/// <param name="Load">The pot after the stack, its top on the treble wiper.</param>
/// <param name="LoadRole">
///     The knob that turns <paramref name="Load" />; null for none: the whole track loads the stack and the output is
///     the treble wiper itself (the next grid's resistor on an amp without a master, the 5F6-A's).
/// </param>
/// <param name="BrightCap">Across the upper half of <paramref name="Load" /> while the bright switch is on.</param>
/// <param name="BrightSwitch">The switch that engages <paramref name="BrightCap" />.</param>
public sealed record ToneStackStageDesign(
    string Name,
    TriodeModel Tube,
    StackDriver Driver,
    double Supply,
    double CathodeResistor,
    ToneStackDesign Stack,
    Pot Load,
    PotRole? LoadRole,
    double PlateResistor = 0,
    double CathodeBypass = 0,
    double InputResistance = 0,
    double GridLeak = 0,
    double BrightCap = 0,
    SwitchRole BrightSwitch = SwitchRole.Bright
) : AmpStageDesign(Name) {
    public override AmpStage Create(double sampleRate) => new ToneStackStage(this, sampleRate);
}

/// <summary>
///     A <see cref="ToneStackStageDesign" /> as one R-type root: the triode's two ports at the top, the tone stack's
///     pots and the load pot inside the adaptor. The stack loads its driver exactly (a cathode follower's limited
///     current into the stack is part of its sound), and turning a knob recomputes the matrix. The output is the load
///     pot's wiper, AC (the stack's caps block the driver's DC).
/// </summary>
public sealed class ToneStackStage : AmpStage {
    const double Off = 1e-15;

    ToneStackNetwork stack; // its design is the Tonestack Type's (the stock one by default)
    readonly PotDivider load;
    readonly int drivenProbe;
    readonly int gridProbe;
    readonly int cathodeProbe;
    readonly int cathodeIndex;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor, Capacitor, Capacitor>, FastTriode> root;
    TriodeModel tube; // as the operating point sees it (with the hardness)
    PreampSettings applied = PreampSettings.Stock;
    double cathodeResistor;
    double brightScale = 1;
    double stackScale = 1;
    double sourceResistance;
    double bass = double.NaN;
    double middle = double.NaN;
    double treble = double.NaN;
    double rotation = double.NaN;
    bool bright;
    double sampleRate;
    double supplyScale = 1;

    public ToneStackStageDesign Design { get; }

    /// <summary>
    ///     The wiper's Thévenin resistance, roughly: the pot's halves in parallel plus the stack's share.
    /// </summary>
    public override double OutputResistance {
        get {
            var a = load.Pot.Fraction(rotation);
            return a * (1 - a) * load.Pot.Ohms + a * a * stack.Design.SlopeResistor;
        }
    }

    public override double RestOutput => 0;

    /// <summary>The DC voltage of the node driving the stack (the cathode or the plate).</summary>
    public double RestDriven { get; private set; }

    public double RestCathode { get; private set; }

    public double RestCurrent { get; private set; }

    public override NewtonSolver Newton => root.Newton;

    /// <summary>The voltage of the node driving the stack at the last sample.</summary>
    public double Driven => root.Probe(drivenProbe);

    public double Grid => root.Probe(gridProbe);

    public double Cathode => root.Probe(cathodeProbe);

    public override bool IsGainStage => Design.Driver == StackDriver.CommonCathode;

    public ToneStackStage(ToneStackStageDesign design, double sampleRate) {
        Design = design;
        this.sampleRate = sampleRate;
        tube = design.Tube;
        cathodeResistor = design.CathodeResistor;
        var net = new WdfNetlist();
        var grid = net.Node();
        net.Port(grid, WdfNetlist.Ground);
        if (design.GridLeak > 0) {
            net.Resistor(grid, WdfNetlist.Ground, design.GridLeak);
        }

        var (cathode, supply) = (net.Node(), net.Node());
        cathodeIndex = net.Resistor(cathode, WdfNetlist.Ground, design.CathodeResistor);
        int driven;
        if (design.Driver == StackDriver.CathodeFollower) {
            driven = cathode;
            net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
            net.Triode(supply, grid, cathode);
        } else {
            driven = net.Node();
            net.Resistor(supply, driven, design.PlateResistor);
            if (design.CathodeBypass > 0) {
                net.Port(cathode, WdfNetlist.Ground);
            } else {
                net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
            }

            net.Triode(driven, grid, cathode);
        }

        stack = ToneStackNetwork.Add(net, driven, design.Stack);
        load = PotDivider.Add(net, stack.Output, WdfNetlist.Ground, design.Load);
        if (design.BrightCap > 0) {
            net.Port(stack.Output, load.Wiper);
        } else {
            net.Port(WdfNetlist.Ground, WdfNetlist.Ground);
        }

        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Probe(load.Wiper);
        drivenProbe = net.Probe(driven);
        gridProbe = net.Probe(grid);
        cathodeProbe = net.Probe(cathode);

        sourceResistance = Math.Max(design.InputResistance, 1);
        root = new(
            net,
            new(
                new(sourceResistance),
                new(design.CathodeBypass > 0 ? design.CathodeBypass : 1e-9, sampleRate),
                new(design.Stack.TrebleCap, sampleRate),
                new(design.Stack.BassCap, sampleRate),
                new(design.Stack.MiddleCap, sampleRate),
                new(Off, sampleRate)
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
        root.Ports.B.Configure(Design.CathodeBypass > 0 ? Design.CathodeBypass : 1e-9, sampleRate);
        root.Ports.C.Configure(stack.Design.TrebleCap / stackScale, sampleRate);
        root.Ports.D.Configure(stack.Design.BassCap / stackScale, sampleRate);
        root.Ports.E.Configure(stack.Design.MiddleCap / stackScale, sampleRate);
        root.Ports.F.Configure(bright && Design.BrightCap > 0 ? Design.BrightCap * brightScale : Off, sampleRate);
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
        var turn = Design.LoadRole is { } role ? controls.Rotation(role) : 1;
        var brightOn = controls.IsOn(Design.BrightSwitch) && Design.BrightCap > 0;
        if (controls.Bass == bass
            && controls.Middle == middle
            && controls.Treble == treble
            && turn == rotation
            && brightOn == bright) {
            return;
        }

        if (brightOn != bright) {
            bright = brightOn;
            root.Ports.F.Configure(bright ? Design.BrightCap * brightScale : Off, sampleRate);
        }

        (bass, middle, treble, rotation) = (controls.Bass, controls.Middle, controls.Treble, turn);
        var (top, bottom, b, m) = stack.Resistances(bass, middle, treble);
        root.SetResistance(stack.TrebleTop, top);
        root.SetResistance(stack.TrebleBottom, bottom);
        root.SetResistance(stack.BassResistor, b);
        root.SetResistance(stack.MiddleResistor, m);
        var (upper, lower) = load.Resistances(turn);
        root.SetResistance(load.Top, upper);
        root.SetResistance(load.Bottom, lower);
        root.Propagate();
    }

    public override void Apply(in PreampSettings settings) {
        var follower = Design.Driver == StackDriver.CathodeFollower;
        var previous = applied;
        applied = settings;
        if (settings.ChangesDevices(previous) || settings.FollowerGrid != previous.FollowerGrid) {
            var model = settings.TubeFor(Design.Tube);
            root.Device = new(model, settings.Hardness, follower ? settings.FollowerGrid : settings.GridCurrent);
        }

        tube = settings.Law(Design.Tube);
        var design = settings.Stack ?? Design.Stack;
        var substituted = design != stack.Design;
        if (substituted) {
            stack = stack with { Design = design };
            root.SetResistance(stack.SlopeResistor, design.SlopeResistor);
            bass = double.NaN; // the pots are the new stack's: recompute them on the next update
        }

        var rk = Design.CathodeResistor * settings.CathodeResistor * (follower ? settings.FollowerBias : 1);
        var brightCap = Design.LoadRole == PotRole.Master ? 1 : settings.BrightCap;
        if (!substituted
            && brightCap == brightScale
            && settings.StackFrequency == stackScale
            && rk == cathodeResistor) {
            return;
        }

        brightScale = brightCap;
        stackScale = settings.StackFrequency;
        cathodeResistor = rk;
        root.SetResistance(cathodeIndex, rk);
        SetSampleRate(sampleRate);
    }

    public override void Retune(double restInput) => SolveRest(restInput);

    public override void Reset(double restInput) {
        var (grid, plate, cathode, driven) = SolveRest(restInput);
        root.Reset();
        root.SetVoltage(0, Design.Supply * supplyScale);
        root.Ports.B.Precharge(Design.CathodeBypass > 0 ? cathode : 0);
        root.Ports.C.Precharge(driven); // the stack's caps hold the driver's DC; its pots carry none
        root.Ports.D.Precharge(driven);
        root.Ports.E.Precharge(driven);
        root.SetInitialVoltages([grid - cathode, plate - cathode]);
    }

    /// <summary>
    ///     The driver's operating point (<see cref="RestDriven" />, <see cref="RestCathode" />, the
    ///     current).
    /// </summary>
    (double Grid, double Plate, double Cathode, double Driven) SolveRest(double restInput) {
        // No grid current at rest: the grid sits at the source's DC, divided by the grid leak if there is one.
        var grid = Design.GridLeak > 0 ? restInput * Design.GridLeak / (sourceResistance + Design.GridLeak) : restInput;
        double driven, cathode, plate;
        if (Design.Driver == StackDriver.CathodeFollower) {
            (RestCurrent, cathode) = OperatingPoint.CathodeFollower(
                tube,
                grid,
                Design.Supply * supplyScale,
                cathodeResistor
            );
            driven = cathode;
            plate = Design.Supply * supplyScale;
        } else {
            (RestCurrent, plate, cathode) = OperatingPoint.CommonCathode(
                tube,
                grid,
                Design.Supply * supplyScale,
                Design.PlateResistor,
                cathodeResistor
            );
            driven = plate;
        }

        RestDriven = driven;
        RestCathode = cathode;
        return (grid, plate, cathode, driven);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        root.Ports.A.Volts = input;
        return root.Tick();
    }

    public override void FlushDenormals() => Flush.Capacitor(ref root.Ports.F);
}
