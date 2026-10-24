using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The channel's own expert settings (the rest are <see cref="PreampSettings" /> and <see cref="PowerSettings" />).
/// </summary>
/// <param name="LowCut">An input high-pass corner in hertz (first order, before the first stage); 0 for none.</param>
/// <param name="PreampSag">How much of the power supply's sag reaches the preamp: 1 stock, 0 a stiff preamp supply.</param>
/// <param name="PowerAmp">False to leave the power amp out: the output is then the phase inverter's input.</param>
public readonly record struct ChannelSettings(double LowCut = 0, double PreampSag = 1, bool PowerAmp = true) {
    public static ChannelSettings Stock => new(0);
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

    /// <summary>Applies the knobs (per block); circuits recompute only where a pot moved.</summary>
    public void Configure(in AmpControls preamp, in PowerControls power) {
        for (var i = 0; i < stages.Length; i++) {
            stages[i].Update(preamp);
            if (i > 0) {
                stages[i].SetSourceResistance(stages[i - 1].OutputResistance);
            }
        }

        if (stages.Length > 0) {
            Power.SetSourceResistance(stages[^1].OutputResistance);
        }

        Power.Configure(power);
        Power.ConfigureLoop(preamp);
        SolidState?.Configure(power);
    }

    /// <summary>
    ///     Applies the expert settings (per block; allocation-free). A change that moves an operating point (a tube, the
    ///     preamp bias, the output tubes' wiring) resets the channel, as a type change does.
    /// </summary>
    public void Apply(in PreampSettings preamp, in PowerSettings power, in ChannelSettings channel) {
        var reset = false;
        if (preamp != preampSettings) {
            reset = preamp.Tube != preampSettings.Tube || preamp.CathodeResistor != preampSettings.CathodeResistor;
            preampSettings = preamp;
            for (var i = 0; i < stages.Length; i++) {
                stages[i].Apply(i == lastGainStage ? preamp : preamp with { CathodeResistor = 1 });
            }
        }

        reset |= Power.Apply(power);
        SolidState?.Apply(power);
        if (channel != channelSettings) {
            var cut = channel.LowCut != channelSettings.LowCut;
            channelSettings = channel;
            if (cut) {
                DesignLowCut();
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

        if (stages.Length > 0) {
            Power.SetSourceResistance(resistance);
        }

        lowCut.Reset();
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
        foreach (var stage in stages) {
            stage.SetSupply(supply);
            x = stage.Tick(x);
        }

        Power.SetLoopSupply(supply);

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
        foreach (var stage in stages) {
            stage.SetSupply(supply);
            x = stage.Tick(x);
        }

        Power.SetLoopSupply(supply);

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
