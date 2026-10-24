using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The channel's own expert settings (the rest are <see cref="PreampSettings" /> and <see cref="PowerSettings" />).
/// </summary>
/// <param name="LowCut">An input high-pass corner in hertz (first order, before the first stage); 0 for none.</param>
/// <param name="PreampSag">How much of the power supply's sag reaches the preamp: 1 stock, 0 a stiff preamp supply.</param>
/// <param name="PowerAmp">False to leave the power amp out: the output is then the phase inverter's input.</param>
/// <param name="StackLocation">Where the tone stack's stage runs in the chain (<see cref="StackLocation" />).</param>
/// <param name="Overdrive">
///     The signal into the last gain stage as a multiple of stock (an extra gain stage's worth of drive), applied to
///     its swing about the stage's rest input; 1 for none.
/// </param>
/// <param name="Plate1">The second-to-last gain stage's plate-bypass corner (<see cref="PreampSettings.PlateFrequency" />).</param>
/// <param name="Plate2">The last gain stage's plate-bypass corner.</param>
public readonly record struct ChannelSettings(
    double LowCut = 0,
    double PreampSag = 1,
    bool PowerAmp = true,
    StackLocation StackLocation = StackLocation.Stock,
    double Overdrive = 1,
    double Plate1 = 1,
    double Plate2 = 1
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
public sealed class AmpChannel {
    readonly AmpStage[] stages;
    readonly int lastGainStage;
    readonly int previousGainStage; // the gain stage before the last, or -1
    readonly int stackStage; // the stage holding the tone stack, or -1
    readonly int[] order; // the stages in running order
    readonly double[] offsets; // per position in the running order: the level shift that keeps each stage's rest input
    readonly double[] restInputs; // per stage: its rest input in the circuit's own order
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

    /// <summary>Whether the power amp is modelled (the Power Amp Modeling switch); otherwise the output is the preamp's.</summary>
    public bool PowerAmpEnabled => channelSettings.PowerAmp;

    /// <summary>The preamp's supply as a fraction of nominal: a solid-state amp's preamp rails are regulated.</summary>
    double PreampSupply => SolidState is null ? Power.PreampSupply : 1;

    public AmpChannel(AmpDesign design, double sampleRate) {
        Design = design;
        this.sampleRate = sampleRate;
        stages = [..design.Preamp.Select(d => d.Create(sampleRate))];
        lastGainStage = Array.FindLastIndex(stages, static s => s.IsGainStage);
        previousGainStage = lastGainStage > 0 ? Array.FindLastIndex(stages, lastGainStage - 1, static s => s.IsGainStage) : -1;
        stackStage = Array.FindIndex(design.Preamp.ToArray(), IsStack);
        order = [..Enumerable.Range(0, stages.Length)];
        offsets = new double[stages.Length];
        restInputs = new double[stages.Length];
        Power = new(design.Power, sampleRate);
        SolidState = design.SolidState is { } solid ? new(solid, sampleRate) : null;
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

    /// <summary>Whether the channel has a tone stack it can move (<see cref="ChannelSettings.StackLocation" />).</summary>
    public bool HasMovableStack => stackStage >= 0;

    /// <summary>Whether a stage of <paramref name="design" /> is a tone stack: a stack stage, or a network with stack caps.</summary>
    public static bool IsStack(AmpStageDesign design) =>
        design is ToneStackStageDesign
        || (design is NetworkStageDesign network
            && network.Netlist.Elements.Any(static e => e.CapacitorRole == CapacitorRole.Stack));

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
        SolidState?.Configure(power);
    }

    /// <summary>
    ///     Applies the expert settings (per block; allocation-free). A change that moves an operating point (a tube, the
    ///     preamp bias, the output tubes' wiring) resets the channel, as a type change does.
    /// </summary>
    public void Apply(in PreampSettings preamp, in PowerSettings power, in ChannelSettings channel) {
        var reset = false;
        var plates = channel.Plate1 != channelSettings.Plate1 || channel.Plate2 != channelSettings.Plate2;
        if (preamp != preampSettings || plates) {
            reset = preamp.MovesOperatingPoint(preampSettings);
            preampSettings = preamp;
            for (var i = 0; i < stages.Length; i++) {
                stages[i].Apply(
                    preamp with {
                        CathodeResistor = i == lastGainStage ? preamp.CathodeResistor : 1,
                        PlateFrequency = i == lastGainStage ? channel.Plate2 : i == previousGainStage ? channel.Plate1 : 1
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
        }
    }

    /// <summary>Every stage at its operating point, in signal order (each stage's rest output feeds the next).</summary>
    public void Reset() {
        Power.Reset();
        SolidState?.Reset();
        var rest = 0.0;
        var resistance = 0.0;
        foreach (var stage in stages) {
            stage.SetSourceResistance(resistance);
            stage.SetSupply(PreampSupply);
            stage.Reset(rest);
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

        lowCut.Reset();
    }

    /// <summary>The running order for <paramref name="location" />: the stack's stage moved, the rest in their order.</summary>
    void Reorder(StackLocation location) {
        for (var i = 0; i < order.Length; i++) {
            order[i] = i;
        }

        reordered = false;
        if (stackStage < 0 || location == StackLocation.Stock) {
            return;
        }

        var firstGain = Array.FindIndex(stages, static s => s.IsGainStage);
        var target = location switch {
            StackLocation.Pre => 0,
            StackLocation.Mid => firstGain < 0 || firstGain == stackStage ? 0 : firstGain + (firstGain < stackStage ? 1 : 0),
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
        var sag = channelSettings.PreampSag;
        var preampSupply = PreampSupply;
        var supply = sag == 1 ? preampSupply : 1 + sag * (preampSupply - 1);
        var x = channelSettings.LowCut > 0 ? lowCut.Process(input) : input;
        if (reordered || channelSettings.Overdrive != 1) {
            x = RunStages(x, supply);
        } else {
            foreach (var stage in stages) {
                stage.SetSupply(supply);
                x = stage.Tick(x);
            }
        }

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
        var sag = channelSettings.PreampSag;
        var preampSupply = PreampSupply;
        var supply = sag == 1 ? preampSupply : 1 + sag * (preampSupply - 1);
        var x = channelSettings.LowCut > 0 ? lowCut.Process(input) : input;
        if (reordered || channelSettings.Overdrive != 1) {
            x = RunStages(x, supply);
        } else {
            foreach (var stage in stages) {
                stage.SetSupply(supply);
                x = stage.Tick(x);
            }
        }

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
    double RunStages(double x, double supply) {
        var overdrive = channelSettings.Overdrive;
        for (var k = 0; k < order.Length; k++) {
            var i = order[k];
            var stage = stages[i];
            stage.SetSupply(supply);
            x += offsets[k];
            if (i == lastGainStage && overdrive != 1) {
                x = restInputs[i] + overdrive * (x - restInputs[i]);
            }

            x = stage.Tick(x);
        }

        return x;
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
