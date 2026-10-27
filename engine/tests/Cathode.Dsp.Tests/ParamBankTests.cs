using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Tests;

public class ParamBankTests {
    static readonly ParamDescriptor[] Descriptors = [
        ParamDescriptor.Float("gain", "Gain", -10, 10, 0, smoothingMs: 1),
        ParamDescriptor.Choice("mode", "Mode", ["A", "B", "C"])
    ];

    [Fact]
    public void Targets_are_clamped_rounded_and_sanitised() {
        var bank = new ParamBank(Descriptors);

        bank.SetTarget(0, 99);
        Assert.Equal(10, bank.GetTarget(0));
        bank.SetTarget(0, float.NaN);
        Assert.Equal(0, bank.GetTarget(0));
        bank.SetTarget(1, 1.6f);
        Assert.Equal(2, bank.GetTarget(1));
    }

    [Fact]
    public void Modulated_choices_and_switches_land_on_the_nearest_option() {
        var bank = new ParamBank([.. Descriptors, ParamDescriptor.Switch("on", "On")]);
        bank.Prepare(48_000);

        bank.Modulate(1, 1.6f, 64);
        Assert.Equal(2, bank.GetTarget(1));
        Assert.Equal(2, bank.Advance(1, 64));
        bank.Modulate(1, 0.4f, 64);
        Assert.Equal(0, (int)bank.Advance(1, 64));

        // A pedal past the half-way point engages the switch (it used to need exactly 100 %).
        bank.Modulate(2, 0.7f, 64);
        Assert.Equal(1, (int)bank.Advance(2, 64));
        bank.Modulate(2, 0.3f, 64);
        Assert.Equal(0, (int)bank.Advance(2, 64));
    }

    [Fact]
    public void Linear_ramp_reaches_target_after_exactly_the_smoothing_time() {
        var bank = new ParamBank(Descriptors);
        bank.Prepare(48_000); // 1 ms = 48 samples
        bank.SetTarget(0, 4.8f);
        bank.BeginBlock();

        Assert.Equal(0.1f * 16, bank.Advance(0, 16), 1e-5f);
        Assert.True(bank.IsRamping(0));
        Assert.Equal(4.8f, bank.Advance(0, 32), 1e-5f);
        Assert.False(bank.IsRamping(0));
    }

    [Fact]
    public void Step_parameters_change_immediately() {
        var bank = new ParamBank(Descriptors);
        bank.Prepare(48_000);
        bank.SetTarget(1, 2);
        bank.BeginBlock();

        Assert.Equal(2, bank.Current(1));
        Assert.False(bank.IsRamping(1));
    }

    [Fact]
    public void Retargeting_mid_ramp_starts_from_the_current_value() {
        var bank = new ParamBank(Descriptors);
        bank.Prepare(48_000);
        bank.SetTarget(0, 4.8f);
        bank.BeginBlock();
        bank.Advance(0, 24); // halfway: 2.4

        bank.SetTarget(0, 0);
        bank.BeginBlock();

        Assert.Equal(1.2f, bank.Advance(0, 24), 1e-5f);
        Assert.Equal(0f, bank.Advance(0, 24), 1e-5f);
    }
}
