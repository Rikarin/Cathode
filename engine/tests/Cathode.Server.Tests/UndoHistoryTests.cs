using Cathode.Server.Rig;

namespace Cathode.Server.Tests;

/// <summary>The live rig's undo stacks: coalescing knob drags, and bounding memory by steps and by bytes.</summary>
public sealed class UndoHistoryTests {
    [Fact]
    public void Edits_with_one_key_coalesce_without_a_snapshot_until_a_pause_or_another_edit() {
        var clock = new ManualClock();
        var history = new UndoHistory(clock);
        var captures = 0;

        UndoSnapshot Capture() {
            captures++;
            return Snapshot(10, captures);
        }

        // A drag: twenty changes 50 ms apart are one step and one snapshot.
        for (var i = 0; i < 20; i++) {
            history.Record("params:drive1:A:gain", Capture);
            clock.Advance(TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(1, captures);
        Assert.Equal((1, 0), history.Steps);

        // The same knob after a pause is a new gesture, and so is another knob or an unkeyed edit.
        clock.Advance(UndoHistory.CoalesceWindow + TimeSpan.FromMilliseconds(1));
        history.Record("params:drive1:A:gain", Capture);
        history.Record("params:drive1:A:tone", Capture);
        history.Record(null, Capture);
        history.Record(null, Capture);

        Assert.Equal(5, captures);
        Assert.Equal((5, 0), history.Steps);
    }

    [Fact]
    public void Undo_and_redo_break_a_drag_and_a_new_edit_clears_redo() {
        var clock = new ManualClock();
        var history = new UndoHistory(clock);
        history.Record("k", static () => Snapshot(10, 1));
        var restored = history.Undo(static () => Snapshot(10, 2));
        Assert.Equal(1, restored!.ActiveScene);
        Assert.True(history.CanRedo);

        // Within the window, but after an undo: a new step, and redo is gone.
        Assert.True(history.Record("k", static () => Snapshot(10, 3)));
        Assert.False(history.CanRedo);
        Assert.Null(history.Redo(static () => throw new InvalidOperationException("nothing to capture")));
    }

    [Fact]
    public void Undo_with_nothing_to_undo_takes_no_snapshot() {
        var history = new UndoHistory(new ManualClock());
        Assert.Null(history.Undo(static () => throw new InvalidOperationException("nothing to capture")));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Each_stack_drops_its_oldest_steps_beyond_the_step_limit() {
        var history = new UndoHistory(new ManualClock(), maxSteps: 3);
        for (var i = 1; i <= 5; i++) {
            history.Push(Snapshot(10, i));
        }

        Assert.Equal((3, 0), history.Steps);
        Assert.Equal(5, history.Undo(static () => Snapshot(10, 0))!.ActiveScene);
        Assert.Equal(4, history.Undo(static () => Snapshot(10, 0))!.ActiveScene);
        Assert.Equal(3, history.Undo(static () => Snapshot(10, 0))!.ActiveScene);
        Assert.Null(history.Undo(static () => Snapshot(10, 0)));
    }

    [Fact]
    public void Memory_is_bounded_by_bytes_as_well_as_steps() {
        const int mb = 1024 * 1024;
        var history = new UndoHistory(new ManualClock(), maxSteps: 100, maxBytes: 10L * mb);

        // A hundred 3 MB presets (a big PUT /live, over and over): only the newest three fit in 10 MB.
        for (var i = 1; i <= 100; i++) {
            history.Push(Snapshot(3 * mb, i));
        }

        Assert.Equal((3, 0), history.Steps);
        Assert.Equal(9L * mb, history.Bytes);
        Assert.Equal(100, history.Undo(static () => Snapshot(3 * mb, 0))!.ActiveScene);

        // The redo stack has the same budget of its own.
        while (history.CanUndo) {
            history.Undo(static () => Snapshot(3 * mb, 0));
        }

        Assert.Equal((0, 3), history.Steps);
        Assert.True(history.Bytes <= 10L * mb);
    }

    [Fact]
    public void A_step_over_the_byte_budget_on_its_own_is_still_kept_so_the_last_edit_can_be_undone() {
        var history = new UndoHistory(new ManualClock(), maxBytes: 100);
        history.Push(Snapshot(50, 1));
        history.Push(Snapshot(500, 2));

        Assert.Equal((1, 0), history.Steps);
        Assert.Equal(2, history.Undo(static () => Snapshot(10, 0))!.ActiveScene);
    }

    [Fact]
    public void Clear_forgets_every_step_and_its_bytes() {
        var history = new UndoHistory(new ManualClock());
        history.Push(Snapshot(10, 1));
        history.Undo(static () => Snapshot(10, 2));
        history.Push(Snapshot(10, 3));
        history.Clear();

        Assert.Equal((0, 0), history.Steps);
        Assert.Equal(0, history.Bytes);
    }

    static UndoSnapshot Snapshot(int bytes, int scene) => new(new byte[bytes], scene);

    sealed class ManualClock : TimeProvider {
        long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan by) => ticks += by.Ticks;
    }
}
