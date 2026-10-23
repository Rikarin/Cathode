using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The channel's own expert settings (the rest are <see cref="PreampSettings" /> and <see cref="PowerSettings" />).
/// </summary>
/// <param name="LowCut">An input high-pass corner in hertz (first order, before the first stage); 0 for none.</param>
/// <param name="PreampSag">How much of the power supply's sag reaches the preamp: 1 stock, 0 a stiff preamp supply.</param>
/// <param name="PowerAmp">False to leave the power amp out: the output is then the phase inverter's input.</param>
public readonly record struct ChannelSettings(double LowCut = 0, double PreampSag = 1, bool PowerAmp = true) {
    public static ChannelSettings Stock => new(LowCut: 0);
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

    /// <summary>Whether the power amp is modelled (the Power Amp Modeling switch); otherwise the output is the preamp's.</summary>
    public bool PowerAmpEnabled => channelSettings.PowerAmp;

    public AmpChannel(AmpDesign design, double sampleRate) {
        Design = design;
        this.sampleRate = sampleRate;
        stages = [..design.Preamp.Select(d => d.Create(sampleRate))];
        lastGainStage = Array.FindLastIndex(stages, static s => s.IsGainStage);
        Power = new(design.Power, sampleRate);
        Reset();
    }

    public void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        foreach (var stage in stages) {
            stage.SetSampleRate(sampleRate);
        }

        Power.SetSampleRate(sampleRate);
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
        var rest = 0.0;
        var resistance = 0.0;
        foreach (var stage in stages) {
            stage.SetSourceResistance(resistance);
            stage.SetSupply(Power.PreampSupply);
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
        var supply = sag == 1 ? Power.PreampSupply : 1 + sag * (Power.PreampSupply - 1);
        var x = channelSettings.LowCut > 0 ? lowCut.Process(input) : input;
        foreach (var stage in stages) {
            stage.SetSupply(supply);
            x = stage.Tick(x);
        }

        return channelSettings.PowerAmp ? Power.Tick(x) : x;
    }

    public void FlushDenormals() {
        foreach (var stage in stages) {
            stage.FlushDenormals();
        }

        Power.FlushDenormals();
        lowCut.FlushDenormals();
    }

    void DesignLowCut() {
        var omega = 2 * Math.PI * Math.Max(channelSettings.LowCut, 1e-3);
        lowCut.Design(0, 1, omega, 1, sampleRate); // s / (s + ω)
    }
}
