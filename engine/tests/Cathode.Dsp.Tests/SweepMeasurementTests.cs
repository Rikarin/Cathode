using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class SweepMeasurementTests {
    const double Rate = 48_000;

    [Fact]
    public void A_wire_measures_flat_at_its_latency() {
        var measurement = new SweepMeasurement(Rate, 2);
        var recorded = Signals.Concat(new float[144], measurement.Sweep, new float[4_800]); // 3 ms round trip

        var ir = measurement.Deconvolve(recorded);

        Assert.Equal(144, Analysis.PeakIndex(ir));
        // Within 0.25 dB from 200 Hz up; lower down, ringing from the sweep's 20 Hz edge outlasts a short window.
        foreach (var hz in new[] { 200.0, 1_000, 8_000 }) {
            Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(ir.AsSpan(0, 4_800), hz, Rate)), 0.25);
        }
    }

    [Fact]
    public void A_known_response_is_recovered_with_its_delay_and_trimmed_around_its_peak() {
        // A smooth "cab" (a resonant low-pass), 3 ms after the sweep starts (the output-to-input latency).
        var cab = Signals.Impulse(2_048);
        var state = new BiquadState();
        state.Process(BiquadCoefficients.Design(BiquadType.LowPass, Rate, 3_000, 2.0), cab);
        var delayed = Signals.Concat(new float[144], cab);
        var measurement = new SweepMeasurement(Rate, 3);
        using var convolver = new Convolver(delayed);
        var recorded = Signals.Concat(measurement.Sweep, new float[9_600]);
        convolver.Process(recorded);

        var ir = measurement.Deconvolve(recorded);
        var trimmed = measurement.Trim(ir, 40, 1);

        foreach (var hz in new[] { 200.0, 1_000, 3_000, 8_000 }) {
            Assert.Equal(
                Analysis.ToDb(Analysis.MagnitudeAt(delayed, hz, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(ir.AsSpan(0, 4_800), hz, Rate)),
                0.25
            );
        }

        Assert.Equal(48, Analysis.PeakIndex(trimmed)); // 1 ms of pre-roll before the peak
        Assert.Equal(1_920, trimmed.Length);
    }
}
