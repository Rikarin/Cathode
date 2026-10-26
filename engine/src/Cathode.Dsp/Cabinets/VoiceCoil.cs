namespace Cathode.Dsp.Cabinets;

/// <summary>
///     A loudspeaker voice coil heating up (the Cab block's Motor Drive): power compression, one channel. The coil's
///     resistance rises with its temperature (copper, about 0.4 % per kelvin), so at a fixed drive voltage less current
///     flows and the cone moves less. Its temperature rise θ, taken as the fractional rise in resistance, follows the
///     power it dissipates through a first-order thermal time constant, <c>τ dθ/dt = k V² / (1 + θ) − θ</c>, and the
///     gain is <c>1 / (1 + θ)</c>. Drive sets <c>k</c>; at 100 % a full-scale sine settles about 8 dB down. The gain
///     only falls as θ grows, so the output is bounded for any input; allocation-free.
/// </summary>
public struct VoiceCoil {
    /// <summary>Heating per unit power at Drive 100 %.</summary>
    public const double FullHeating = 8;

    const double DenormalThreshold = 1e-30;

    double theta;

    /// <summary>The largest gain reduction over the last <see cref="Process" />, dB (0 or more).</summary>
    public float ReductionDb { get; private set; }

    /// <summary>True while the coil is above ambient, so it still compresses as it cools.</summary>
    public readonly bool IsWarm => theta > 0;

    public void Reset() {
        theta = 0;
        ReductionDb = 0;
    }

    /// <summary>
    ///     Compresses <paramref name="samples" /> in place, Drive (0 … 1) moving linearly from
    ///     <paramref name="fromDrive" /> to <paramref name="toDrive" />, with time constant <paramref name="tau" />
    ///     seconds.
    /// </summary>
    public void Process(double sampleRate, float fromDrive, float toDrive, double tau, Span<float> samples) {
        var a = 1 - Math.Exp(-1 / (Math.Max(tau, 1e-3) * sampleRate));
        var step = 1.0 / Math.Max(1, samples.Length);
        var lowest = 1.0;
        for (var i = 0; i < samples.Length; i++) {
            var k = FullHeating * (fromDrive + (toDrive - fromDrive) * (i + 1) * step);
            double x = samples[i];
            var gain = 1 / (1 + theta);
            lowest = Math.Min(lowest, gain);
            samples[i] = (float)(x * gain);
            theta += a * (k * x * x * gain - theta);
        }

        if (theta < DenormalThreshold) {
            theta = 0;
        }

        ReductionDb = lowest < 1 ? (float)(-20 * Math.Log10(lowest)) : 0f;
    }
}
