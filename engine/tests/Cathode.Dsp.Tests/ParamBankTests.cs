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

    /// <summary>
    ///     A parameter with per-type defaults starts, resets and falls back (on a non-finite value) to the default on
    ///     the block's type.
    /// </summary>
    [Fact]
    public void Per_type_defaults_follow_the_type() {
        ParamDescriptor[] descriptors = [
            ParamDescriptor.Choice("type", "Type", ["A", "B", "C"], 1),
            ParamDescriptor.Float("boost", "Boost", 0, 10, 5) with { TypeDefaults = [5, 0, 7] }
        ];
        var bank = new ParamBank(descriptors);
        Assert.Equal(0, bank.GetTarget(1)); // the type's default is B
        Assert.Equal(0, bank.Current(1));

        bank.SetTarget(0, 2);
        Assert.Equal(0, bank.GetTarget(1)); // a set type alone leaves the value: the server sends the defaults
        bank.SetTarget(1, float.NaN);
        Assert.Equal(7, bank.GetTarget(1));
        bank.SetTarget(1, 3);
        bank.SetTarget(0, 0);
        bank.ResetToDefaults();
        Assert.Equal((0f, 5f), (bank.GetTarget(0), bank.GetTarget(1)));
        Assert.Equal(5, descriptors[1].DefaultFor(0));
        Assert.Equal(5, descriptors[1].DefaultFor(9)); // out of range: the plain default
    }

    [Fact]
    public void Per_type_defaults_need_one_valid_default_per_type() {
        var type = ParamDescriptor.Choice("type", "Type", ["A", "B"]);
        var boost = ParamDescriptor.Float("boost", "Boost", 0, 10, 5);

        Assert.Throws<ArgumentException>(() => new BlockDescriptor(
                "t",
                "T",
                "C",
                [type, boost with { TypeDefaults = [1] }]
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new BlockDescriptor("t", "T", "C", [type, boost with { TypeDefaults = [1, 11] }])
        );
        Assert.Throws<ArgumentException>(() => new BlockDescriptor("t", "T", "C", [boost with { TypeDefaults = [1] }]));
        var good = new BlockDescriptor("t", "T", "C", [type, boost with { TypeDefaults = [1, 2] }]);
        Assert.Equal(2, good.ValueOf(1, new Dictionary<string, float> { ["type"] = 1 }));
        Assert.Equal(1, good.ValueOf(1, null));
        Assert.Equal(4, good.ValueOf(1, new Dictionary<string, float> { ["type"] = 1, ["boost"] = 4 }));
    }

    /// <summary>
    ///     A parameter shown by another's setting names a choice or switch of its block (not the type, not itself) and
    ///     values that parameter can take.
    /// </summary>
    [Fact]
    public void Visibility_conditions_name_a_choice_or_switch_of_the_block() {
        var type = ParamDescriptor.Choice("type", "Type", ["A", "B"]);
        var mode = ParamDescriptor.Choice("mode", "Mode", ["One", "Two", "Three"]);
        var on = ParamDescriptor.Switch("on", "On");
        var knob = ParamDescriptor.Float("knob", "Knob", 0, 10, 5);
        var x = knob with { Id = "x" };

        BlockDescriptor Block(ParamDescriptor shown) => new("t", "T", "C", [type, mode, on, knob, shown]);

        Assert.Throws<ArgumentException>(() => Block(x.ShownWhen("missing", 0)));
        Assert.Throws<ArgumentException>(() => Block(x.ShownWhen("knob", 0))); // not a choice
        Assert.Throws<ArgumentException>(() => Block(x.ShownWhen("type", 0))); // TypeLabels' job
        Assert.Throws<ArgumentException>(() => Block((mode with { Id = "x" }).ShownWhen("x", 0))); // itself
        Assert.Throws<ArgumentException>(() => Block(x.ShownWhen("mode", 3))); // no such option
        Assert.Throws<ArgumentException>(() => Block(x.ShownWhen("mode"))); // never shown
        var block = Block(x.ShownWhen("mode", 0, 2));
        Assert.Equal("mode", block.Params[4].VisibleWhen!.Param);
        Assert.Equal([0f, 2f], block.Params[4].VisibleWhen!.Values);
        Assert.Equal("on", Block(x.ShownWhen("on", 1)).Params[4].VisibleWhen!.Param);
    }

    /// <summary>
    ///     The Compressor shows Threshold and Ratio, or the one Compression knob, by its Controls setting; the Vibe its
    ///     Mix in Chorus mode only.
    /// </summary>
    [Fact]
    public void The_compressor_shows_the_knobs_its_controls_setting_uses() {
        var info = Blocks.CompressorBlock.Info;
        ParamCondition? When(string id) => info.Params[info.IndexOf(id)].VisibleWhen;

        Assert.Equal(("controls", 0f), (When("threshold")!.Param, Assert.Single(When("threshold")!.Values)));
        Assert.Equal(("controls", 0f), (When("ratio")!.Param, Assert.Single(When("ratio")!.Values)));
        Assert.Equal(("controls", 1f), (When("compression")!.Param, Assert.Single(When("compression")!.Values)));
        Assert.Null(When("attack"));
        var vibe = Blocks.VibeBlock.Info;
        Assert.Equal("mode", vibe.Params[vibe.IndexOf("mix")].VisibleWhen!.Param);
    }

    [Fact]
    public void Modulated_choices_and_switches_land_on_the_nearest_option() {
        var bank = new ParamBank([..Descriptors, ParamDescriptor.Switch("on", "On")]);
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
