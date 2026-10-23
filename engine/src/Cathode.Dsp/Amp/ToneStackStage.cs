using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>What drives a <see cref="ToneStackStage" />'s tone stack.</summary>
public enum StackDriver {
    /// <summary>A cathode follower (Marshall, Vox top boost): plate at the supply, the stack hangs on the cathode.</summary>
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
/// <param name="LoadRole">The knob that turns <paramref name="Load" />.</param>
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
    PotRole LoadRole,
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

    readonly ToneStackStageDesign design;
    readonly ToneStackNetwork stack;
    readonly PotDivider load;
    readonly int drivenProbe;
    readonly int gridProbe;
    readonly int cathodeProbe;
    readonly int cathodeIndex;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor, Capacitor, Capacitor>, FastTriode> root;
    TriodeModel tube;
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
    double restDriven;
    double restCathode;
    double restCurrent;

    public ToneStackStageDesign Design => design;

    /// <summary>The wiper's Thévenin resistance, roughly: the pot's halves in parallel plus the stack's share.</summary>
    public override double OutputResistance {
        get {
            var a = load.Pot.Fraction(rotation);
            return a * (1 - a) * load.Pot.Ohms + a * a * design.Stack.SlopeResistor;
        }
    }

    public override double RestOutput => 0;

    /// <summary>The DC voltage of the node driving the stack (the cathode or the plate).</summary>
    public double RestDriven => restDriven;

    public double RestCathode => restCathode;

    public double RestCurrent => restCurrent;

    public override NewtonSolver Newton => root.Newton;

    /// <summary>The voltage of the node driving the stack at the last sample.</summary>
    public double Driven => root.Probe(drivenProbe);

    public double Grid => root.Probe(gridProbe);

    public double Cathode => root.Probe(cathodeProbe);

    public override bool IsGainStage => design.Driver == StackDriver.CommonCathode;

    public ToneStackStage(ToneStackStageDesign design, double sampleRate) {
        this.design = design;
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
        root.Ports.B.Configure(design.CathodeBypass > 0 ? design.CathodeBypass : 1e-9, sampleRate);
        root.Ports.C.Configure(design.Stack.TrebleCap / stackScale, sampleRate);
        root.Ports.D.Configure(design.Stack.BassCap / stackScale, sampleRate);
        root.Ports.E.Configure(design.Stack.MiddleCap / stackScale, sampleRate);
        root.Ports.F.Configure(bright && design.BrightCap > 0 ? design.BrightCap * brightScale : Off, sampleRate);
        root.Propagate();
    }

    public override void SetSourceResistance(double ohms) {
        var total = Math.Max(ohms + design.InputResistance, 1);
        if (total == sourceResistance) {
            return;
        }

        sourceResistance = total;
        root.Ports.A.Value = total;
        root.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) {
        supplyScale = scale;
        root.SetVoltage(0, design.Supply * scale);
    }

    public override void Update(in AmpControls controls) {
        var turn = controls.Rotation(design.LoadRole);
        var brightOn = controls.IsOn(design.BrightSwitch) && design.BrightCap > 0;
        if (controls.Bass == bass && controls.Middle == middle && controls.Treble == treble && turn == rotation
            && brightOn == bright) {
            return;
        }

        if (brightOn != bright) {
            bright = brightOn;
            root.Ports.F.Configure(bright ? design.BrightCap * brightScale : Off, sampleRate);
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
        var model = settings.TubeFor(design.Tube);
        if (model != tube) {
            tube = model;
            root.Device = new(model);
        }

        var rk = design.CathodeResistor * settings.CathodeResistor;
        if (settings.BrightCap == brightScale && settings.StackFrequency == stackScale && rk == cathodeResistor) {
            return;
        }

        brightScale = settings.BrightCap;
        stackScale = settings.StackFrequency;
        cathodeResistor = rk;
        root.SetResistance(cathodeIndex, rk);
        SetSampleRate(sampleRate);
    }

    public override void Reset(double restInput) {
        // No grid current at rest: the grid sits at the source's DC, divided by the grid leak if there is one.
        var grid = design.GridLeak > 0 ? restInput * design.GridLeak / (sourceResistance + design.GridLeak) : restInput;
        double driven, cathode, plate;
        if (design.Driver == StackDriver.CathodeFollower) {
            (restCurrent, cathode) = OperatingPoint.CathodeFollower(
                tube,
                grid,
                design.Supply * supplyScale,
                cathodeResistor
            );
            driven = cathode;
            plate = design.Supply * supplyScale;
        } else {
            (restCurrent, plate, cathode) = OperatingPoint.CommonCathode(
                tube,
                grid,
                design.Supply * supplyScale,
                design.PlateResistor,
                cathodeResistor
            );
            driven = plate;
        }

        restDriven = driven;
        restCathode = cathode;
        root.Reset();
        root.SetVoltage(0, design.Supply * supplyScale);
        root.Ports.B.Precharge(design.CathodeBypass > 0 ? cathode : 0);
        root.Ports.C.Precharge(driven); // the stack's caps hold the driver's DC; its pots carry none
        root.Ports.D.Precharge(driven);
        root.Ports.E.Precharge(driven);
        root.SetInitialVoltages([grid - cathode, plate - cathode]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        root.Ports.A.Volts = input;
        return root.Tick();
    }

    public override void FlushDenormals() => Flush.Capacitor(ref root.Ports.F);
}
