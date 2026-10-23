using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     One channel of an <see cref="AmpDesign" />: its preamp stages and power amp, run sample by sample at the
///     oversampled rate. Each stage reads the previous one's output through that stage's output resistance; the preamp
///     supply follows the power amp's sag one sample behind (the dropping network's time constants are tens of
///     milliseconds).
/// </summary>
public sealed class AmpChannel {
    readonly AmpStage[] stages;

    public AmpDesign Design { get; }

    public IReadOnlyList<AmpStage> Stages => stages;

    public PowerAmp Power { get; }

    public AmpChannel(AmpDesign design, double sampleRate) {
        Design = design;
        stages = [..design.Preamp.Select(d => d.Create(sampleRate))];
        Power = new(design.Power, sampleRate);
        Reset();
    }

    public void SetSampleRate(double sampleRate) {
        foreach (var stage in stages) {
            stage.SetSampleRate(sampleRate);
        }

        Power.SetSampleRate(sampleRate);
    }

    /// <summary>Applies the knobs (per block); circuits recompute only where a pot moved.</summary>
    public void Configure(in AmpControls preamp, in PowerControls power) {
        for (var i = 0; i < stages.Length; i++) {
            stages[i].Update(preamp);
            if (i > 0) {
                stages[i].SetSourceResistance(stages[i - 1].OutputResistance);
            }
        }

        Power.Configure(power);
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
    }

    /// <summary>One sample at the oversampled rate: guitar volts in, 8 Ω speaker volts out.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double input) {
        var supply = Power.PreampSupply;
        var x = input;
        foreach (var stage in stages) {
            stage.SetSupply(supply);
            x = stage.Tick(x);
        }

        return Power.Tick(x);
    }

    public void FlushDenormals() {
        foreach (var stage in stages) {
            stage.FlushDenormals();
        }

        Power.FlushDenormals();
    }
}
