using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     What "full scale" at the Circuit Amp's output means on a cranked amp (#277). Full scale is the largest swing
///     the output tubes can make into a resistive load. A speaker is not one: when the tubes cut off, the current in
///     the voice coil and the transformer's leakage inductance has only the speaker's own losses, the primary's
///     capacitance and the core loss to go into, and the speaker voltage flies back past that swing. The output then
///     shows spikes one to four samples wide up to 5 dB over full scale, and on the hardest cases (a single-ended
///     amp, a power stage driven far harder than its preamp can) it is over in the audio band as well, by 2 to 3 dB.
///     The transformer model is left as it is: no drawing read so far gives a winding capacitance, and the diodes
///     some amps put from the plates to ground are per-model parts none of the models has transcribed. These tests
///     pin what the block's documentation says: how far and how long the output goes over, raw and band-limited.
/// </summary>
public class AmpFullScaleTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>
    ///     A decaying 82 / 330 / 1975 Hz chord with a little noise, struck again every 250 ms, about 1.2 V at its
    ///     peak: a hard-picked chord on hot pickups.
    /// </summary>
    static float[] Chords(double seconds) {
        var noise = Signals.Noise((int)(seconds * Rate), 11, 0.05f);
        var signal = new float[noise.Length];
        for (var n = 0; n < signal.Length; n++) {
            var t = n % (int)(0.25 * Rate) / Rate;
            var envelope = Math.Exp(-t / 0.12);
            signal[n] = (float)(envelope
                * (0.5 * Math.Sin(2 * Math.PI * 82 * t)
                    + 0.4 * Math.Sin(2 * Math.PI * 330 * t)
                    + 0.25 * Math.Sin(2 * Math.PI * 1_975 * t))
                + envelope * noise[n]);
        }

        return signal;
    }

    /// <summary>
    ///     Model, Gain, Master, Input Trim (dB; a negative Gain or Master leaves the model's default), then the limits:
    ///     the peak after a 6 kHz two-pole low-pass, the raw peak, and the longest run of samples over full scale.
    /// </summary>
    public static TheoryData<string, float, float, float, double, double, int> Cranked {
        get {
            var data = new TheoryData<string, float, float, float, double, double, int>();
            // Measured on this signal (raw peak, peak after the low-pass, longest run): 1.72, 1.23, 3.
            data.Add("Champ 5F1", 10, 10, 0, 1.45, 2.1, 5);
            data.Add("Plexi", 10, 10, 0, 1.1, 1.7, 4); // 1.40, 0.94, 2
            // 1.76, 1.38, 4: the hardest case, a clean amp's power stage driven 20 dB harder than its preamp can.
            data.Add("Twin Reverb", 10, 10, 20, 1.6, 2.1, 6);
            data.Add("AC30", -1, -1, 0, 0.75, 1.25, 2); // 0.98, 0.59, none over
            data.Add("JCM800 2203", 10, 10, 0, 0.85, 1.35, 3); // 1.08, 0.69, 1
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cranked))]
    public void A_cranked_amp_peaks_at_full_scale_in_the_audio_band_and_spikes_briefly_over_it(
        string name,
        float gain,
        float master,
        float trim,
        double bandLimit,
        double rawLimit,
        int runLimit
    ) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(d => d.Name == name));
        if (gain >= 0) {
            block.Params.SetTarget(CircuitAmpBlock.Gain, gain);
        }

        if (master >= 0) {
            block.Params.SetTarget(CircuitAmpBlock.Master, master);
        }

        block.Params.SetTarget(CircuitAmpBlock.InputTrim, trim);
        block.Prepare(new(Rate, 64));
        var rendered = BlockRender.Mono(block, Chords(2), 64);
        Assert.True(Analysis.AllFinite(rendered));

        var raw = Analysis.Peak(rendered);
        var (over, run, longest) = (0, 0, 0);
        foreach (var sample in rendered) {
            run = Math.Abs(sample) > 1 ? run + 1 : 0;
            over += run > 0 ? 1 : 0;
            longest = Math.Max(longest, run);
        }

        var filter = default(BiquadState);
        var coefficients = BiquadCoefficients.Design(BiquadType.LowPass, Rate, 6_000, Math.Sqrt(0.5));
        var band = 0.0;
        foreach (var sample in rendered) {
            band = Math.Max(band, Math.Abs(filter.Tick(coefficients, sample)));
        }

        output.WriteLine(
            $"{name}: peak {raw:0.00} ({Analysis.ToDb(raw):+0.0;-0.0} dB), {band:0.00} after a 6 kHz low-pass, "
            + $"{over} samples over full scale in 2 s, longest run {longest}"
        );
        Assert.True(band < bandLimit, $"{name}: {band:0.00} in the audio band");
        Assert.True(raw < rawLimit, $"{name}: a spike of {raw:0.00}");
        Assert.True(longest <= runLimit, $"{name}: {longest} samples in a row over full scale");
    }
}
