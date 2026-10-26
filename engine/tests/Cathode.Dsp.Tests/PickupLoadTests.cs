using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Input block's pickup load (docs/08-blocks-parity.md §2): the loaded pickup's resonance against the RLC
///     formula, and the filter the block applies against the analytic ratio of the chosen load to the reference.
/// </summary>
public class PickupLoadTests {
    const int Rate = 48_000;

    public static TheoryData<int, double, double> Loads() {
        var data = new TheoryData<int, double, double>();
        foreach (var pickup in new[] { 1, 2, 3 }) {
            foreach (var load in new[] { 1e6, 230e3, 22e3 }) {
                foreach (var cable in new[] { 0.0, 500e-12 }) {
                    data.Add(pickup, load, cable);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Loads))]
    public void The_resonance_follows_the_rlc_formula(int type, double load, double cable) {
        var pickup = InputBlock.PickupOf(type, 0, 0, 0)!.Value;
        var (l, r, c) = (pickup.Inductance, pickup.Resistance, pickup.Capacitance + cable);

        var (frequency, q) = PickupLoad.Resonance(pickup, load, cable);

        // Series R and L into C parallel with the load: s²LC + s(RC + L/R_L) + 1 + R/R_L.
        var expectedFrequency = Math.Sqrt((1 + r / load) / (l * c)) / (2 * Math.PI);
        var expectedQ = Math.Sqrt(l * c * (1 + r / load)) / (r * c + l / load);
        Assert.Equal(expectedFrequency, frequency, expectedFrequency * 1e-9);
        Assert.Equal(expectedQ, q, expectedQ * 1e-9);
    }

    [Fact]
    public void A_single_coil_into_a_high_impedance_peaks_in_the_presence_range() {
        var (frequency, q) = PickupLoad.Resonance(Pickup.SingleCoil, 1e6, 500e-12);

        Assert.InRange(frequency, 3_500, 4_700);
        Assert.InRange(q, 2, 8); // unloaded by volume and tone pots, a pickup rings high
        // At resonance the magnitude rises above the DC gain by about Q.
        var peak = PickupLoad.Magnitude(Pickup.SingleCoil, 1e6, 500e-12, frequency);
        Assert.Equal(q, peak / PickupLoad.Magnitude(Pickup.SingleCoil, 1e6, 500e-12, 0), q * 0.05);
    }

    [Fact]
    public void Lower_loads_damp_the_peak() {
        var qs = PickupLoad.Loads.Skip(1)
            .Select(static load => PickupLoad.Resonance(Pickup.Humbucker, load, 500e-12).Q);

        Assert.True(qs.Zip(qs.Skip(1)).All(static p => p.Second < p.First));
    }

    [Theory]
    [InlineData(1, 6, false)] // single coil, 32k
    [InlineData(2, 3, false)] // humbucker, 230k
    [InlineData(3, 7, false)] // P90, 22k
    [InlineData(1, 0, true)] // single coil, buffered
    public void The_block_applies_the_chosen_load_over_the_reference(int type, int impedance, bool buffered) {
        using var input = TestBlocks.Create<InputBlock>(
            (InputBlock.PickupType, type),
            (InputBlock.Impedance, impedance),
            (InputBlock.PickupBuffer, buffered ? 1 : 0)
        );
        var pickup = InputBlock.PickupOf(type, 0, 0, 0)!.Value;
        var load = PickupLoad.Loads[impedance];
        var cable = 500e-12;

        var response = BlockRender.Mono(input, Signals.Impulse(16_384), 64);

        Assert.True(input.PickupActive);
        var reference = PickupLoad.Resonance(pickup, PickupLoad.ReferenceLoad, cable).Frequency;
        foreach (var frequency in new[] { 100, 1_000, reference * 0.7, reference, reference * 1.3, 8_000 }) {
            var chosen = buffered
                ? PickupLoad.Magnitude(pickup, PickupLoad.ReferenceLoad, 0, frequency)
                : PickupLoad.Magnitude(pickup, load, cable, frequency);
            var expected = Analysis.ToDb(
                chosen / PickupLoad.Magnitude(pickup, PickupLoad.ReferenceLoad, cable, frequency)
            );
            var actual = Analysis.ToDb(Analysis.MagnitudeAt(response, frequency, Rate));
            // The bilinear transform bends the response a little near the resonances and towards Nyquist.
            var tolerance = frequency <= 2_000 ? 0.25 : 1.0;
            Assert.True(
                Math.Abs(actual - expected) < tolerance,
                $"{frequency:0} Hz: {actual:0.00} dB, expected {expected:0.00} dB"
            );
        }
    }

    [Theory]
    [InlineData(0, 6, false)] // pickup off
    [InlineData(2, 0, false)] // Auto: the reference itself
    [InlineData(2, 1, false)] // 1M
    public void Off_or_at_the_reference_load_the_input_is_untouched(int type, int impedance, bool buffered) {
        using var input = TestBlocks.Create<InputBlock>(
            (InputBlock.PickupType, type),
            (InputBlock.Impedance, impedance),
            (InputBlock.PickupBuffer, buffered ? 1 : 0)
        );
        var signal = Signals.Noise(4_800, 3, 0.5f);

        Assert.Equal(signal, BlockRender.Mono(input, signal, 64));
        Assert.False(input.PickupActive);
    }

    [Fact]
    public void A_lower_load_takes_off_highs_and_the_buffer_adds_them() {
        var damped = PickupLoad.Magnitude(Pickup.SingleCoil, 22e3, 500e-12, 4_000)
            / PickupLoad.Magnitude(Pickup.SingleCoil, 1e6, 500e-12, 4_000);
        var buffered = PickupLoad.Magnitude(Pickup.SingleCoil, 1e6, 0, 7_000)
            / PickupLoad.Magnitude(Pickup.SingleCoil, 1e6, 500e-12, 7_000);

        Assert.True(Analysis.ToDb(damped) < -6);
        Assert.True(Analysis.ToDb(buffered) > 3);
    }
}
