using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A graphic equaliser in the amp's signal path, up to five bands set by the Circuit Amp's EQ parameters: the slot
///     for the Mesa Mark series' five-band EQ between the preamp and the phase inverter (place it last in the preamp),
///     or anywhere else a model has one. Band <c>n</c> is a peaking filter at <paramref name="Frequencies" />[n] with
///     quality factor <paramref name="Q" />, ±12 dB.
/// </summary>
/// <remarks>
///     A buffered, behavioural EQ (constant-Q peaking sections), not the Mark's passive LC network, whose topology is
///     still to be read from the factory drawing (docs/06 §3.4); a model that has the real network can describe it as a
///     <see cref="NetworkStageDesign" /> with inductors instead. With every band at 0 dB the stage is exactly bypassed.
///     It passes the previous stage's output resistance through to the next.
/// </remarks>
public sealed record EqualizerStageDesign(string Name, IReadOnlyList<double> Frequencies, double Q = 1.2) : AmpStageDesign(Name) {
    /// <summary>The Mark IIC+ panel's centres: 80, 240, 750, 2200 and 6600 Hz.</summary>
    public static IReadOnlyList<double> MarkBands { get; } = [80, 240, 750, 2_200, 6_600];

    public override AmpStage Create(double sampleRate) => new EqualizerStage(this, sampleRate);
}

/// <summary>An <see cref="EqualizerStageDesign" />: peaking biquads (RBJ), transposed direct form II.</summary>
public sealed class EqualizerStage : AmpStage {
    const int MaxBands = 5;

    readonly EqualizerStageDesign design;
    readonly double[] b0 = new double[MaxBands];
    readonly double[] b1 = new double[MaxBands];
    readonly double[] b2 = new double[MaxBands];
    readonly double[] a1 = new double[MaxBands];
    readonly double[] a2 = new double[MaxBands];
    readonly double[] s1 = new double[MaxBands];
    readonly double[] s2 = new double[MaxBands];
    readonly double[] gains = new double[MaxBands];
    readonly int bands;
    double sampleRate;
    double sourceResistance;
    double restInput;
    bool flat = true;

    public override double OutputResistance => sourceResistance;

    public override double RestOutput => restInput;

    public EqualizerStage(EqualizerStageDesign design, double sampleRate) {
        this.design = design;
        bands = Math.Min(design.Frequencies.Count, MaxBands);
        SetSampleRate(sampleRate);
    }

    public override void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        for (var n = 0; n < bands; n++) {
            Design(n);
        }
    }

    public override void SetSourceResistance(double ohms) => sourceResistance = ohms;

    public override void SetSupply(double scale) { }

    public override void Update(in AmpControls controls) {
        flat = true;
        for (var n = 0; n < bands; n++) {
            var gain = Math.Clamp(controls.Equalizer[n], -12, 12);
            flat &= gain == 0;
            if (gain != gains[n]) {
                gains[n] = gain;
                Design(n);
            }
        }
    }

    public override void Reset(double restInput) {
        this.restInput = restInput;
        for (var n = 0; n < bands; n++) {
            // At rest on a DC input the output equals it (each section's DC gain is 1).
            s2[n] = (b2[n] - a2[n]) * restInput;
            s1[n] = (b1[n] - a1[n]) * restInput + s2[n];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        if (flat) {
            return input;
        }

        var x = input;
        for (var n = 0; n < bands; n++) {
            var y = b0[n] * x + s1[n];
            s1[n] = b1[n] * x - a1[n] * y + s2[n];
            s2[n] = b2[n] * x - a2[n] * y;
            x = y;
        }

        return x;
    }

    public override void FlushDenormals() {
        for (var n = 0; n < bands; n++) {
            s1[n] = Flush.Value(s1[n]);
            s2[n] = Flush.Value(s2[n]);
        }
    }

    /// <summary>Band <paramref name="n" />'s peaking section (Bristow-Johnson's cookbook).</summary>
    void Design(int n) {
        var a = Math.Pow(10, gains[n] / 40);
        var omega = 2 * Math.PI * Math.Min(design.Frequencies[n], 0.45 * sampleRate) / sampleRate;
        var alpha = Math.Sin(omega) / (2 * design.Q);
        var cos = Math.Cos(omega);
        var norm = 1 / (1 + alpha / a);
        b0[n] = (1 + alpha * a) * norm;
        b1[n] = -2 * cos * norm;
        b2[n] = (1 - alpha * a) * norm;
        a1[n] = -2 * cos * norm;
        a2[n] = (1 - alpha / a) * norm;
    }
}
