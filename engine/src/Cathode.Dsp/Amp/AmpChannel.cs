using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The channel's own expert settings (the rest are <see cref="PreampSettings" /> and <see cref="PowerSettings" />).
/// </summary>
/// <param name="LowCut">An input high-pass corner in hertz (first order, before the first stage); 0 for none.</param>
/// <param name="PreampSag">
///     How much of the power supply's sag reaches the preamp: 1 stock, 0 a stiff preamp supply.
/// </param>
/// <param name="PowerAmp">False to leave the power amp out: the output is then the phase inverter's input.</param>
/// <param name="StackLocation">Where the tone stack's stage runs in the chain (<see cref="StackLocation" />).</param>
/// <param name="Overdrive">
///     The signal into the last gain stage as a multiple of stock (an extra gain stage's worth of drive), applied to
///     its swing about the stage's rest input; 1 for none.
/// </param>
/// <param name="Plate">
///     The last plate-bypass cap's corner (<see cref="AmpFeatures.HasPlateCap(AmpStageDesign)" />,
///     <see cref="PreampSettings.PlateFrequency" />).
/// </param>
public readonly record struct ChannelSettings(
    double LowCut = 0,
    double PreampSag = 1,
    bool PowerAmp = true,
    StackLocation StackLocation = StackLocation.Stock,
    double Overdrive = 1,
    double Plate = 1
) {
    public static ChannelSettings Stock => new(0);
}

/// <summary>Where a channel runs its tone stack's stage (the Circuit Amp's Tonestack Location).</summary>
public enum StackLocation {
    /// <summary>Where the circuit has it.</summary>
    Stock,

    /// <summary>Ahead of the first stage, straight from the input.</summary>
    Pre,

    /// <summary>After the first gain stage.</summary>
    Mid,

    /// <summary>After the last preamp stage, into the phase inverter.</summary>
    Post
}

/// <summary>
///     One channel of an <see cref="AmpDesign" />: its preamp stages and power amp, run sample by sample at the
///     oversampled rate. Each stage reads the previous one's output through that stage's output resistance; the preamp
///     supply follows the power amp's sag one sample behind (the dropping network's time constants are tens of
///     milliseconds).
/// </summary>
/// <remarks>
///     Each preamp stage runs on its own node of the preamp supply's decoupling chain
///     (<see cref="PreampSupplyDesign" />), so a stage further down the chain sees the sag and ripple filtered once
///     more per node. A model that does not transcribe its chain gets the generic one: its gain stages in pairs (one
///     12AX7 each) from the input, the last pair on the preamp node and each earlier pair one node further down, every
///     node <see cref="GenericResistance" /> into <see cref="GenericCap" />, each gain stage drawing an equal share of
///     the preamp's current (the phase inverter's two triodes taking theirs on the preamp node).
/// </remarks>
public sealed class AmpChannel {
    /// <summary>The generic chain's dropping resistor per node (see the remarks).</summary>
    public const double GenericResistance = 10e3;

    /// <summary>The generic chain's filter cap per node.</summary>
    public const double GenericCap = 22e-6;

    readonly AmpStage[] stages;
    readonly int lastGainStage;
    readonly int lastPlateStage; // the last stage with a plate-bypass cap (Triode 2), or -1
    readonly int stackStage; // the stage holding the tone stack, or -1
    readonly int[] order; // the stages in running order
    readonly double[] offsets; // per position in the running order: the level shift that keeps each stage's rest input
    readonly double[] restInputs; // per stage: its rest input in the circuit's own order
    readonly double[] supplies; // per supply node: its supply as a fraction of nominal, with the preamp sag applied
    readonly bool separateSupply; // the preamp's own supply (a rack preamp): no sag reaches it
    bool reordered;
    FirstOrder lowCut = FirstOrder.Unity;
    double sampleRate;
    PreampSettings preampSettings = PreampSettings.Stock;
    ChannelSettings channelSettings = ChannelSettings.Stock;

    public AmpDesign Design { get; }
    public IReadOnlyList<AmpStage> Stages => stages;
    public PowerAmp Power { get; }

    /// <summary>The solid-state power amp that runs in place of <see cref="Power" />, or null for a tube amp.</summary>
    public SolidStatePowerAmp? SolidState { get; }

    /// <summary>
    ///     Whether the model has a tremolo of its own (a bias tremolo in its power amp or one on a preamp stage), which
    ///     the Circuit Amp's tremolo parameters then drive.
    /// </summary>
    public bool HasTremolo { get; }

    /// <summary>
    ///     Whether the power amp is modelled (the Power Amp Modeling switch); otherwise the output is the preamp's.
    /// </summary>
    public bool PowerAmpEnabled => channelSettings.PowerAmp;

    /// <summary>
    ///     Per preamp supply node, the supply its stages run on as a fraction of nominal (the preamp sag applied).
    /// </summary>
    public IReadOnlyList<double> Supplies => supplies;

    public AmpChannel(AmpDesign design, double sampleRate) {
        Design = design;
        this.sampleRate = sampleRate;
        stages = [..design.Preamp.Select(d => d.Create(sampleRate))];
        lastGainStage = Array.FindLastIndex(stages, static s => s.IsGainStage);
        lastPlateStage = Array.FindLastIndex([..design.Preamp], AmpFeatures.HasPlateCap);
        stackStage = Array.FindIndex([..design.Preamp], IsStack);
        order = [..Enumerable.Range(0, stages.Length)];
        offsets = new double[stages.Length];
        restInputs = new double[stages.Length];
        Power = new(design.Power, sampleRate);
        SolidState = design.SolidState is { } solid ? new(solid, sampleRate) : null;
        HasTremolo = design.Power.BiasTremolo is not null || design.Preamp.Any(static s => s.HasTremolo);
        separateSupply = design.PreampSupply?.Separate ?? false;
        var nodes = design.PreampSupply is { } own
            ? Assign(own)
            : AssignGeneric(design.Power.PowerSupply.PreampCurrent);
        Power.Decouple(nodes);
        supplies = new double[nodes.Count + 1];
        Array.Fill(supplies, 1);
        Reset();
    }

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        foreach (var stage in stages) {
            stage.SetSampleRate(sampleRate);
        }

        Power.SetSampleRate(sampleRate);
        SolidState?.SetSampleRate(sampleRate);
        DesignLowCut();
    }

    /// <summary>Puts each stage on its design's supply node (0 where it names none).</summary>
    IReadOnlyList<DecouplingNode> Assign(PreampSupplyDesign supply) {
        for (var i = 0; i < stages.Length; i++) {
            var node = Design.Preamp[i].SupplyNode ?? 0;
            if (node < 0 || node > supply.Nodes.Count) {
                throw new ArgumentException(
                    $"{Design.Name}: stage {Design.Preamp[i].Name} is on node {node}, which "
                    + "the preamp supply does not have."
                );
            }

            stages[i].AssignSupplyNode(node);
        }

        return supply.Nodes;
    }

    /// <summary>
    ///     The generic chain (see the remarks) for a preamp drawing <paramref name="preampCurrent" /> in all, and each
    ///     stage on its pair's node. A channel switch counts with its longer channel's gain stages, all on one node.
    /// </summary>
    DecouplingNode[] AssignGeneric(double preampCurrent) {
        var gains = new int[stages.Length];
        for (var i = 0; i < stages.Length; i++) {
            gains[i] = stages[i] switch {
                ChannelSwitchStage channel => channel.GainStages,
                { IsGainStage: true } => 1,
                _ => 0
            };
        }

        var total = gains.Sum();
        var pairs = (total + 1) / 2;
        var seen = 0;
        var group = 0;
        for (var i = 0; i < stages.Length; i++) {
            if (gains[i] > 0) {
                group = seen / 2;
                seen += gains[i];
            }

            stages[i].AssignSupplyNode(Math.Max(pairs - 1 - group, 0));
        }

        // Node k feeds pair (pairs - 1 - k), hanging from node k - 1; the pair's draw is its gain stages' share.
        var share = preampCurrent / (total + 2);
        var nodes = new DecouplingNode[Math.Max(pairs - 1, 0)];
        for (var k = 1; k <= nodes.Length; k++) {
            var pair = pairs - 1 - k;
            var draw = Math.Min(total - 2 * pair, 2) * share;
            nodes[k - 1] = new(GenericResistance, GenericCap, draw, k - 1);
        }

        return nodes;
    }

    /// <summary>
    ///     Whether the channel has a tone stack it can move (<see cref="ChannelSettings.StackLocation" />).
    /// </summary>
    public bool HasMovableStack => stackStage >= 0;

    /// <summary>
    ///     Whether a stage of <paramref name="design" /> is a tone stack: a stack stage, a network with stack caps, or
    ///     a switch between two single passive such networks (no triode, pentode or op-amp: the JC-120's stack with and
    ///     without its distortion diodes; a switched channel whose network holds its gain stage stays put).
    /// </summary>
    public static bool IsStack(AmpStageDesign design) =>
        design is ToneStackStageDesign
        || (design is NetworkStageDesign network
            && network.Netlist.Elements.Any(static e => e.CapacitorRole == CapacitorRole.Stack))
        || (design is ChannelSwitchStageDesign { Off: [var off], On: [var on] }
            && IsPassiveStack(off)
            && IsPassiveStack(on));

    static bool IsPassiveStack(AmpStageDesign design) =>
        design is NetworkStageDesign network
        && IsStack(network)
        && !network.Netlist.Elements.Any(static e => e.Kind is ElementKind.Triode
            or ElementKind.Pentode
            or ElementKind.OpAmp
            or ElementKind.Amplifier
        );

    /// <summary>Applies the knobs (per block); circuits recompute only where a pot moved.</summary>
    public void Configure(in AmpControls preamp, in PowerControls power) {
        for (var k = 0; k < order.Length; k++) {
            var i = order[k];
            stages[i].Update(preamp);
            if (k > 0) {
                stages[i].SetSourceResistance(stages[order[k - 1]].OutputResistance);
            }
        }

        if (stages.Length > 0) {
            Power.SetSourceResistance(stages[order[^1]].OutputResistance);
        }

        Power.Configure(power);
        Power.ConfigureLoop(preamp);
        SolidState?.Configure(power);
    }

    /// <summary>
    ///     Applies the expert settings (per block; allocation-free). A discrete change that moves an operating point (a
    ///     preamp or power tube, the output tubes' wiring, the stack's location) resets the channel, as a type change
    ///     does; a continuous one (the preamp bias, a tube's hardness, a follower's bias) only retunes it
    ///     (<see cref="Retune" />), so sweeping or automating it does not cut the signal at every block.
    /// </summary>
    public void Apply(in PreampSettings preamp, in PowerSettings power, in ChannelSettings channel) {
        var reset = false;
        var retune = false;
        if (preamp != preampSettings || channel.Plate != channelSettings.Plate) {
            reset = preamp.SwapsTube(preampSettings);
            retune = preamp.MovesOperatingPoint(preampSettings);
            preampSettings = preamp;
            for (var i = 0; i < stages.Length; i++) {
                stages[i]
                    .Apply(
                        preamp with {
                            CathodeResistor = i == lastGainStage ? preamp.CathodeResistor : 1,
                            PlateFrequency = i == lastPlateStage ? channel.Plate : 1
                        }
                    );
            }
        }

        reset |= Power.Apply(power);
        SolidState?.Apply(power);
        if (channel != channelSettings) {
            var cut = channel.LowCut != channelSettings.LowCut;
            var moved = channel.StackLocation != channelSettings.StackLocation;
            channelSettings = channel;
            if (cut) {
                DesignLowCut();
            }

            if (moved) {
                Reorder(channel.StackLocation);
                reset = true;
            }
        }

        if (reset) {
            Reset();
        } else if (retune) {
            Retune();
        }
    }

    /// <summary>
    ///     Every stage's operating point recomputed in signal order (<see cref="AmpStage.Retune" />) for a continuous
    ///     setting, the state left to slew: the rest levels the reordering and the overdrive work about follow, and the
    ///     next <see cref="Configure" /> hands on the new output resistances.
    /// </summary>
    void Retune() => Settle(false);

    /// <summary>
    ///     Every stage brought to its operating point in signal order, by <see cref="AmpStage.Reset" /> when
    ///     <paramref name="reset" /> is set and by <see cref="AmpStage.Retune" /> otherwise: each stage's rest output
    ///     feeds the next, a reordered channel's stages keep the operating points they have in the circuit's order, and
    ///     the power amp sees the last stage's output resistance.
    /// </summary>
    void Settle(bool reset) {
        var rest = 0.0;
        var resistance = 0.0;
        foreach (var stage in stages) {
            stage.SetSourceResistance(resistance);
            stage.SetSupplies(supplies);
            if (reset) {
                stage.Reset(rest);
            } else {
                stage.Retune(rest);
            }

            rest = stage.RestOutput;
            resistance = stage.OutputResistance;
        }

        for (var i = 0; i < stages.Length; i++) {
            restInputs[i] = i == 0 ? 0 : stages[i - 1].RestOutput;
        }

        if (reordered) {
            // Each stage keeps the operating point it has in the circuit's order: an ideal level shift on its input.
            for (var k = 0; k < order.Length; k++) {
                var arriving = k == 0 ? 0 : stages[order[k - 1]].RestOutput;
                offsets[k] = restInputs[order[k]] - arriving;
                if (k > 0) {
                    stages[order[k]].SetSourceResistance(stages[order[k - 1]].OutputResistance);
                }
            }

            resistance = stages[order[^1]].OutputResistance;
        }

        if (stages.Length > 0) {
            Power.SetSourceResistance(resistance);
        }
    }

    /// <summary>
    ///     Every stage at its operating point, in signal order (each stage's rest output feeds the next).
    /// </summary>
    public void Reset() {
        Power.Reset();
        SolidState?.Reset();
        UpdateSupplies();
        Settle(true);
        lowCut.Reset();
    }

    /// <summary>
    ///     The running order for <paramref name="location" />: the stack's stage moved, the rest in their order.
    /// </summary>
    void Reorder(StackLocation location) {
        for (var i = 0; i < order.Length; i++) {
            order[i] = i;
        }

        reordered = false;
        if (stackStage < 0 || location == StackLocation.Stock) {
            return;
        }

        var firstGain = Array.FindIndex(stages, static s => s.IsGainStage);
        var past = firstGain < stackStage ? 1 : 0; // the stack's stage leaves a gap before it
        var target = location switch {
            StackLocation.Pre => 0,
            StackLocation.Mid => firstGain < 0 || firstGain == stackStage ? 0 : firstGain + past,
            _ => order.Length - 1
        };
        // Remove the stack's stage and insert it at the target position.
        var k = 0;
        for (var i = 0; i < stages.Length; i++) {
            if (k == target) {
                order[k++] = stackStage;
            }

            if (i != stackStage) {
                order[k++] = i;
            }
        }

        if (k == target) {
            order[k] = stackStage;
        }

        for (var i = 0; i < order.Length; i++) {
            reordered |= order[i] != i;
        }
    }

    /// <summary>
    ///     One sample at the oversampled rate: guitar volts in, 8 Ω speaker volts out (or, with the power amp off, the
    ///     phase inverter's input volts).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double input) {
        UpdateSupplies();
        var x = channelSettings.LowCut > 0 ? lowCut.Process(input) : input;
        if (reordered || channelSettings.Overdrive != 1) {
            x = RunStages(x);
        } else {
            foreach (var stage in stages) {
                stage.SetSupplies(supplies);
                x = stage.Tick(x);
            }
        }

        Power.SetLoopSupply(supplies[0]);

        if (!channelSettings.PowerAmp) {
            return x;
        }

        return SolidState is { } solid ? solid.Tick(x) : Power.Tick(x);
    }

    /// <summary>
    ///     <see cref="Tick" />'s first half, for a block that inserts a stage between the preamp and the power amp: the
    ///     low cut and the preamp stages, guitar volts in, phase inverter input volts out. Follow it with
    ///     <see cref="TickPowerAmp" /> on the same sample, so the preamp's supply keeps following the sag.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double TickPreamp(double input) {
        UpdateSupplies();
        var x = channelSettings.LowCut > 0 ? lowCut.Process(input) : input;
        if (reordered || channelSettings.Overdrive != 1) {
            x = RunStages(x);
        } else {
            foreach (var stage in stages) {
                stage.SetSupplies(supplies);
                x = stage.Tick(x);
            }
        }

        Power.SetLoopSupply(supplies[0]);

        return x;
    }

    /// <summary><see cref="Tick" />'s second half: the power amp (or nothing, with it off).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double TickPowerAmp(double x) {
        if (!channelSettings.PowerAmp) {
            return x;
        }

        return SolidState is { } solid ? solid.Tick(x) : Power.Tick(x);
    }

    /// <summary>
    ///     The stages in running order with each one's level shift (<see cref="Reorder" />), and the overdrive on the
    ///     last gain stage's input swing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double RunStages(double x) {
        var overdrive = channelSettings.Overdrive;
        for (var k = 0; k < order.Length; k++) {
            var i = order[k];
            var stage = stages[i];
            stage.SetSupplies(supplies);
            x += offsets[k];
            if (i == lastGainStage && overdrive != 1) {
                x = restInputs[i] + overdrive * (x - restInputs[i]);
            }

            x = stage.Tick(x);
        }

        return x;
    }

    /// <summary>
    ///     Each supply node's supply from the power amp's (a solid-state amp's preamp rails are regulated, a rack
    ///     preamp's are its own), with the Preamp Sag applied: 1 as the circuit, 0 a stiff preamp supply.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void UpdateSupplies() {
        if (SolidState is not null || separateSupply) {
            return;
        }

        var sag = channelSettings.PreampSag;
        var nodes = Power.PreampSupplies;
        for (var k = 0; k < supplies.Length; k++) {
            supplies[k] = sag == 1 ? nodes[k] : 1 + sag * (nodes[k] - 1);
        }
    }

    public void FlushDenormals() {
        foreach (var stage in stages) {
            stage.FlushDenormals();
        }

        Power.FlushDenormals();
        SolidState?.FlushDenormals();
        lowCut.FlushDenormals();
    }

    void DesignLowCut() {
        var omega = 2 * Math.PI * Math.Max(channelSettings.LowCut, 1e-3);
        lowCut.Design(0, 1, omega, 1, sampleRate); // s / (s + ω)
    }
}
