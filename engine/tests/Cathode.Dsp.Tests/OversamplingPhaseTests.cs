using System.Numerics;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Resampling;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The global oversampling filters (docs/08-blocks-parity.md §2): the linear-phase FIR half-bands against the IIR
///     ones they stand in for, their latency, and what the Amp, Drive and Dynamic Distortion blocks report.
/// </summary>
public class OversamplingPhaseTests {
    [Theory]
    [InlineData(Oversampler4x.FirstTransition, 0.21, 0.27)]
    [InlineData(Oversampler4x.SecondTransition, 0.10, 0.38)]
    public void Linear_phase_kernels_match_the_iir_magnitude_in_the_pass_and_stop_bands(
        double transition,
        double passEdge,
        double stopEdge
    ) {
        var iir = HalfBand.Design(Oversampler4x.AttenuationDb, transition);
        var fir = LinearPhaseKernel.For(Oversampler4x.AttenuationDb, transition);

        for (var f = 0.0; f <= passEdge; f += 0.005) {
            Assert.InRange(Db(fir.Magnitude(f)) - Db(IirMagnitude(iir, f)), -0.01, 0.01);
        }

        for (var f = stopEdge; f <= 0.5; f += 0.005) {
            Assert.True(Db(fir.Magnitude(f)) < -95, $"FIR at {f}: {Db(fir.Magnitude(f)):0.0} dB");
            Assert.True(Db(IirMagnitude(iir, f)) < -95, $"IIR at {f}: {Db(IirMagnitude(iir, f)):0.0} dB");
        }
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(1_000.0)]
    [InlineData(10_000.0)]
    [InlineData(18_000.0)]
    public void Both_phases_keep_the_audio_band_level_alike(double frequency) {
        var input = Signals.Sine(frequency, 48_000, 48_000, 0.5f);

        var minimum2 = RoundTrip2(input, OversamplingPhase.Minimum);
        var linear2 = RoundTrip2(input, OversamplingPhase.Linear);
        var minimum4 = RoundTrip4(input, OversamplingPhase.Minimum);
        var linear4 = RoundTrip4(input, OversamplingPhase.Linear);

        Assert.InRange(GainDb(input, linear2) - GainDb(input, minimum2), -0.01, 0.01);
        Assert.InRange(GainDb(input, linear4) - GainDb(input, minimum4), -0.01, 0.01);
        Assert.InRange(GainDb(input, linear4), -0.1, 0.1);
    }

    [Fact]
    public void Linear_phase_is_a_pure_delay_of_the_reported_latency_and_minimum_phase_is_faster() {
        var impulse = Signals.Impulse(1_024);

        var linear2 = RoundTrip2(impulse, OversamplingPhase.Linear, out var latency2);
        var linear4 = RoundTrip4(impulse, OversamplingPhase.Linear, out var latency4);
        var minimum2 = RoundTrip2(impulse, OversamplingPhase.Minimum, out var none2);
        var minimum4 = RoundTrip4(impulse, OversamplingPhase.Minimum, out var none4);

        Assert.Equal((0, 0), (none2, none4));
        Assert.InRange(latency2, 40, 120);
        Assert.InRange(latency4, latency2, latency2 + 20);
        Assert.Equal(latency2, Analysis.PeakIndex(linear2));
        Assert.Equal(latency4, Analysis.PeakIndex(linear4));
        // A linear phase delays every frequency alike; the IIR filters delay the audio band by a few samples.
        Assert.Equal(latency2, GroupDelay(linear2, 1_000), 0.01);
        Assert.Equal(latency2, GroupDelay(linear2, 10_000), 0.01);
        Assert.True(GroupDelay(minimum2, 1_000) < latency2 / 10.0);
        Assert.True(GroupDelay(minimum4, 1_000) < latency4 / 10.0);
    }

    [Fact]
    public void The_blocks_report_their_oversamplers_latency() {
        using var amp = Prepared(new CircuitAmpBlock(), OversamplingPhase.Linear);
        using var ampMinimum = Prepared(new CircuitAmpBlock(), OversamplingPhase.Minimum);
        using var dynamic = Prepared(new DynamicDistortionBlock(), OversamplingPhase.Linear);
        var two = new Oversampler2x(1);
        two.SetPhase(OversamplingPhase.Linear);
        var four = new Oversampler4x(1);
        four.SetPhase(OversamplingPhase.Linear);

        Assert.Equal(two.LatencySamples, amp.LatencySamples);
        Assert.Equal(0, ampMinimum.LatencySamples);
        Assert.Equal(four.LatencySamples, dynamic.LatencySamples);
        Assert.Equal(four.LatencySamples, Drive(OversamplingPhase.Linear, 0).LatencySamples); // Global: up to 4x
        Assert.Equal(0, Drive(OversamplingPhase.Linear, 1).LatencySamples);
        Assert.Equal(two.LatencySamples, Drive(OversamplingPhase.Linear, 2).LatencySamples);
        Assert.Equal(four.LatencySamples, Drive(OversamplingPhase.Linear, 3).LatencySamples);
        Assert.Equal(0, Drive(OversamplingPhase.Minimum, 3).LatencySamples);
        Assert.True(DriveBlock.Info.Params[DriveBlock.OversamplingSetting].Recompile);
    }

    [Fact]
    public void A_drive_type_at_2x_is_delayed_to_the_reported_latency() {
        var circuit = DriveCircuits.All.Select(static (c, i) => (c, i)).First(static p => p.c.Oversampling == 2).i;
        using var drive = Drive(OversamplingPhase.Linear, 0, (DriveBlock.Type, DriveBlock.CircuitTypes + circuit));
        drive.Params.SetTarget(DriveBlock.Mix, 0); // the dry path: the same filters, without the circuit
        drive.Reset();

        var output = BlockRender.Mono(drive, Signals.Impulse(1_024), 64);

        Assert.Equal(drive.LatencySamples, Analysis.PeakIndex(output));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_forced_drive_factor_stays_clean(int setting) {
        using var drive = Drive(OversamplingPhase.Minimum, setting, (DriveBlock.Drive, 8f));
        var output = BlockRender.Mono(drive, Signals.Sine(1_000, 48_000, 9_600, 0.5f), 64);

        Assert.True(Analysis.AllFinite(output));
        Assert.InRange(Analysis.Rms(output.AsSpan(4_800)), 1e-3, 4);
    }

    static DriveBlock Drive(OversamplingPhase phase, int setting, params (int Index, float Value)[] settings) {
        var drive = new DriveBlock { OversamplingPhase = phase };
        drive.Params.SetTarget(DriveBlock.OversamplingSetting, setting);
        foreach (var (index, value) in settings) {
            drive.Params.SetTarget(index, value);
        }

        drive.Prepare(new(48_000, 64));
        return drive;
    }

    static T Prepared<T>(T block, OversamplingPhase phase)
        where T : IDspBlock, IOversampledBlock {
        block.OversamplingPhase = phase;
        block.Prepare(new(48_000, 64));
        return block;
    }

    static float[] RoundTrip2(float[] input, OversamplingPhase phase) => RoundTrip2(input, phase, out _);

    static float[] RoundTrip2(float[] input, OversamplingPhase phase, out int latency) {
        var oversampler = new Oversampler2x(64);
        oversampler.SetPhase(phase);
        latency = oversampler.LatencySamples;
        var output = (float[])input.Clone();
        for (var offset = 0; offset < output.Length; offset += 64) {
            oversampler.Up(output.AsSpan(offset, 64));
            oversampler.Down(output.AsSpan(offset, 64));
        }

        return output;
    }

    static float[] RoundTrip4(float[] input, OversamplingPhase phase) => RoundTrip4(input, phase, out _);

    static float[] RoundTrip4(float[] input, OversamplingPhase phase, out int latency) {
        var oversampler = new Oversampler4x(64);
        oversampler.SetPhase(phase);
        latency = oversampler.LatencySamples;
        var output = (float[])input.Clone();
        for (var offset = 0; offset < output.Length; offset += 64) {
            oversampler.Up(output.AsSpan(offset, 64));
            oversampler.Down(output.AsSpan(offset, 64));
        }

        return output;
    }

    static double GainDb(float[] input, float[] output) =>
        Analysis.ToDb(Analysis.Rms(output.AsSpan(4_800)) / Analysis.Rms(input.AsSpan(4_800)));

    /// <summary>−dφ/dω of an impulse response around <paramref name="frequency" />, in samples.</summary>
    static double GroupDelay(float[] response, double frequency) {
        const double delta = 10;
        var a = Phase(response, frequency - delta);
        var b = Phase(response, frequency + delta);
        var difference = Math.IEEERemainder(b - a, 2 * Math.PI);
        return -difference / (2 * Math.PI * 2 * delta / 48_000);
    }

    static double Phase(float[] response, double frequency) {
        var w = -2 * Math.PI * frequency / 48_000;
        var sum = Complex.Zero;
        for (var n = 0; n < response.Length; n++) {
            sum += response[n] * Complex.FromPolarCoordinates(1, w * n);
        }

        return sum.Phase;
    }

    /// <summary>The IIR half-band's |H| at <paramref name="f" /> (fraction of the high rate): the average of its allpass branches.</summary>
    static double IirMagnitude(double[] coefficients, double f) {
        var z = Complex.FromPolarCoordinates(1, 2 * Math.PI * f);
        var z2 = z * z;
        Complex Branch(int start) {
            Complex product = 1;
            for (var i = start; i < coefficients.Length; i += 2) {
                product *= (coefficients[i] + 1 / z2) / (1 + coefficients[i] / z2);
            }

            return product;
        }

        return (0.5 * (Branch(0) + Branch(1) / z)).Magnitude;
    }

    static double Db(double gain) => 20 * Math.Log10(Math.Max(gain, 1e-12));
}
