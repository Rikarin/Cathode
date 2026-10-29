using Cathode.Engine.Graph;
using Cathode.Engine.Rig;

namespace Cathode.Engine.Tests;

public class EngageSwitchTests {
    const double Rate = 48_000;
    const int Frames = 32;

    /// <summary>Feeds <paramref name="values" /> one sub-block each and returns the state after each.</summary>
    static bool[] Run(ref EngageSwitch engage, params float[] values) {
        var states = new bool[values.Length];
        for (var i = 0; i < values.Length; i++) {
            states[i] = engage.Update(values[i], Frames, Rate);
        }

        return states;
    }

    /// <summary>How many sub-blocks of <paramref name="value" /> the switch stays on for.</summary>
    static int SubBlocksUntilOff(ref EngageSwitch engage, float value) {
        var count = 0;
        while (engage.Update(value, Frames, Rate)) {
            count++;
            Assert.True(count < 100_000, "never turned off");
        }

        return count;
    }

    [Fact]
    public void The_defaults_switch_at_half_exactly_as_before() {
        var engage = new EngageSwitch();
        float[] values = [0.2f, 0.5f, 0.49999f, 0.5f, 0.51f, 0.499f, 0.9f, 0.48f, 0.52f, 0f, 1f, 0.4999f];

        var states = Run(ref engage, values);

        // Before, the engine bypassed the block whenever the value was below 0.5 and engaged it otherwise.
        Assert.Equal(values.Select(static v => v >= 0.5f), states);
        Assert.Equal(EngageMode.Position, engage.Mode);
        Assert.Equal(0.5f, engage.Threshold);
        Assert.Equal(0f, engage.OffDelayMs);
    }

    [Fact]
    public void The_first_value_seeds_the_state_without_waiting_for_the_off_delay() {
        var below = new EngageSwitch(EngageMode.Position, 0.5f, 1000f);
        var above = new EngageSwitch(EngageMode.Position, 0.5f, 1000f);

        Assert.False(below.Update(0.1f, Frames, Rate));
        Assert.True(above.Update(0.9f, Frames, Rate));
    }

    [Fact]
    public void Position_mode_switches_at_its_threshold() {
        var engage = new EngageSwitch(EngageMode.Position, 0.2f, 0f);

        Assert.Equal([false, true, false, true], Run(ref engage, 0.1f, 0.2f, 0.19f, 0.6f));
    }

    [Fact]
    public void The_off_delay_holds_the_block_for_that_many_samples_and_engaging_is_immediate() {
        // 2 ms at 48 kHz = 96 samples = three sub-blocks of 32.
        var engage = new EngageSwitch(EngageMode.Position, 0.5f, 2f);
        Assert.True(engage.Update(1f, Frames, Rate));

        Assert.Equal(3, SubBlocksUntilOff(ref engage, 0.1f));
        Assert.True(engage.Update(0.6f, Frames, Rate), "engaging is immediate");
    }

    [Fact]
    public void The_off_delay_rounds_up_to_whole_sub_blocks() {
        // 1.5 ms = 72 samples: the block stays on until 72 samples of the off region have passed, 96 at this size.
        var engage = new EngageSwitch(EngageMode.Position, 0.5f, 1.5f);
        engage.Update(1f, Frames, Rate);

        Assert.Equal(3, SubBlocksUntilOff(ref engage, 0f));
    }

    [Fact]
    public void A_value_back_above_the_threshold_restarts_the_off_delay() {
        var engage = new EngageSwitch(EngageMode.Position, 0.5f, 2f);
        engage.Update(1f, Frames, Rate);

        Assert.Equal([true, true, true], Run(ref engage, 0.1f, 0.1f, 0.7f));
        Assert.Equal(3, SubBlocksUntilOff(ref engage, 0.1f));
    }

    [Fact]
    public void Hysteresis_keeps_noise_at_the_threshold_from_chattering() {
        var engage = new EngageSwitch(EngageMode.Position, 0.5f, 1f);
        engage.Update(1f, Frames, Rate);

        // Just below the threshold, but inside the hysteresis: still the on region, however long it stays there.
        for (var i = 0; i < 1000; i++) {
            Assert.True(engage.Update(i % 2 == 0 ? 0.49f : 0.505f, Frames, Rate));
        }

        // Once below the hysteresis it is in the off region until it is back at the threshold: noise that does not
        // reach the threshold keeps the delay running.
        Assert.Equal([true, true, false, false], Run(ref engage, 0.47f, 0.49f, 0.495f, 0.49f));
        Assert.Equal([true], Run(ref engage, 0.5f));
    }

    [Fact]
    public void Without_an_off_delay_position_mode_has_no_hysteresis() {
        var engage = new EngageSwitch(EngageMode.Position, 0.5f, 0f);

        Assert.Equal([true, false, true, false], Run(ref engage, 0.5f, 0.495f, 0.5f, 0.499f));
    }

    [Fact]
    public void Moving_mode_does_not_engage_on_load_whatever_the_value() {
        var heel = new EngageSwitch(EngageMode.Moving, 0.05f, 300f);
        var toe = new EngageSwitch(EngageMode.Moving, 0.05f, 300f);

        Assert.All(Run(ref heel, 0f, 0f, 0.01f, 0.02f, 0f), Assert.False);
        Assert.All(Run(ref toe, 1f, 1f, 0.99f, 1f), Assert.False);
    }

    [Fact]
    public void Moving_mode_engages_as_soon_as_the_value_leaves_its_deadband() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.05f, 2f);
        Run(ref engage, 0f, 0.01f);

        Assert.False(engage.Update(EngageSwitch.Deadband * 0.9f, Frames, Rate));
        Assert.True(engage.Update(EngageSwitch.Deadband * 1.1f, Frames, Rate), "a touch at the heel");
    }

    [Fact]
    public void Moving_mode_engages_on_a_move_from_anywhere() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.05f, 0f);
        engage.Update(1f, Frames, Rate);

        Assert.True(engage.Update(0.9f, Frames, Rate), "rocked back from the toe");
    }

    [Fact]
    public void Moving_mode_turns_off_once_the_value_rests_below_the_threshold_for_the_off_delay() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.1f, 2f);
        Run(ref engage, 0f, 0.5f, 0.8f, 0.3f);
        Assert.True(engage.Update(0.12f, Frames, Rate), "above the threshold it stays on");

        Assert.Equal(3, SubBlocksUntilOff(ref engage, 0f));
        Assert.All(Run(ref engage, 0f, 0.01f, 0f), Assert.False);
    }

    [Fact]
    public void Moving_mode_restarts_the_off_delay_while_the_value_moves_below_the_threshold() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.2f, 2f);
        Run(ref engage, 0f, 0.5f);

        // Rocking near the heel: each move by more than the deadband restarts the delay.
        Assert.All(Run(ref engage, 0.15f, 0.15f, 0.1f, 0.1f, 0.05f, 0.05f, 0.1f), Assert.True);
        Assert.Equal(2, SubBlocksUntilOff(ref engage, 0.1f));
    }

    [Fact]
    public void Moving_mode_does_not_take_a_value_settling_back_to_the_heel_for_a_touch() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.3f, 0f);
        Run(ref engage, 0f, 0.6f);
        Assert.False(engage.Update(0.2f, Frames, Rate), "off at once without an off delay");

        // A release ramp still on its way down: no new engage.
        Assert.All(Run(ref engage, 0.15f, 0.1f, 0.05f, 0.02f, 0f), Assert.False);
        Assert.False(engage.Update(0.05f, Frames, Rate), "a move below the threshold lasts as long as the off delay");
        Assert.True(engage.Update(0.35f, Frames, Rate));
    }

    [Fact]
    public void Without_an_off_delay_moving_below_the_threshold_does_not_flicker_the_block() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.5f, 0f);
        engage.Update(0f, Frames, Rate);

        Assert.All(Run(ref engage, 0.1f, 0.2f, 0.3f, 0.4f), Assert.False);
        Assert.True(engage.Update(0.55f, Frames, Rate));
    }

    [Fact]
    public void A_reset_seeds_the_switch_again() {
        var engage = new EngageSwitch(EngageMode.Moving, 0.1f, 0f);
        Run(ref engage, 0f, 0.8f);

        engage.Reset();

        Assert.False(engage.Update(0.8f, Frames, Rate), "seeded, not moved");
        Assert.True(engage.Update(0.5f, Frames, Rate));
    }

    [Fact]
    public void A_threshold_of_zero_never_turns_the_block_off() {
        var engage = new EngageSwitch(EngageMode.Moving, 0f, 0f);
        Run(ref engage, 0f, 0.5f);

        Assert.All(Run(ref engage, 0f, 0f, 0f), Assert.True);
    }

    [Fact]
    public void Settings_out_of_range_are_clamped() {
        var engage = new EngageSwitch(EngageMode.Position, 2f, -5f);
        var nan = new EngageSwitch(EngageMode.Position, float.NaN, float.NaN);
        var tooLong = new EngageSwitch(EngageMode.Position, 0.5f, 60_000f);

        Assert.Equal(1f, engage.Threshold);
        Assert.Equal(0f, engage.OffDelayMs);
        Assert.Equal(EngageSwitch.DefaultThreshold, nan.Threshold);
        Assert.Equal(0f, nan.OffDelayMs);
        Assert.Equal(EngageSwitch.MaxOffDelayMs, tooLong.OffDelayMs);
    }
}
