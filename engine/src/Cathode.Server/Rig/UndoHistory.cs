namespace Cathode.Server.Rig;

/// <summary>A working copy as it was: the preset as UTF-8 JSON, and the scene it was on.</summary>
public sealed record UndoSnapshot(byte[] Json, int ActiveScene);

/// <summary>
///     The live rig's undo and redo stacks. Each stack is bounded by steps and by bytes, dropping its oldest steps
///     first (a <c>PUT /live</c> body may be megabytes, and a hundred of them must not pin gigabytes). Knob changes
///     coalesce: an edit with the same key as the previous one, within <see cref="CoalesceWindow" /> of it, continues
///     that step (one drag, one undo) without taking a snapshot at all; a pause starts a new step.
/// </summary>
public sealed class UndoHistory(
    TimeProvider clock,
    int maxSteps = UndoHistory.DefaultMaxSteps,
    long maxBytes = UndoHistory.DefaultMaxBytes
) {
    public const int DefaultMaxSteps = 100;

    /// <summary>Per stack: undo and redo together hold at most twice this.</summary>
    public const long DefaultMaxBytes = 32L * 1024 * 1024;

    /// <summary>How long after an edit the next one with the same key still belongs to its step.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);

    readonly Stack undo = new(maxSteps, maxBytes);
    readonly Stack redo = new(maxSteps, maxBytes);
    string? coalesceKey;
    long lastEdit;

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Steps held, undo then redo (for tests and diagnostics).</summary>
    public (int Undo, int Redo) Steps => (undo.Count, redo.Count);

    /// <summary>Bytes held by both stacks.</summary>
    public long Bytes => undo.Bytes + redo.Bytes;

    /// <summary>
    ///     Before an edit: records an undo step from <paramref name="capture" /> unless the edit continues the previous
    ///     one (same non-null <paramref name="key" />, within the window). <paramref name="capture" /> runs only when a
    ///     step is recorded. Returns whether one was.
    /// </summary>
    public bool Record(string? key, Func<UndoSnapshot> capture) {
        var now = clock.GetTimestamp();
        var continues = key is not null
            && key == coalesceKey
            && clock.GetElapsedTime(lastEdit, now) <= CoalesceWindow;
        coalesceKey = key;
        lastEdit = now;
        if (continues) {
            return false;
        }

        undo.Push(capture());
        redo.Clear();
        return true;
    }

    /// <summary>After a structural edit that succeeded: <paramref name="before" /> is its undo step.</summary>
    public void Push(UndoSnapshot before) {
        coalesceKey = null;
        undo.Push(before);
        redo.Clear();
    }

    /// <summary>
    ///     Steps back: the state from <paramref name="current" /> goes on the redo stack; returns the state to restore,
    ///     or null (without capturing) when there is nothing to undo.
    /// </summary>
    public UndoSnapshot? Undo(Func<UndoSnapshot> current) => Step(undo, redo, current);

    /// <summary>Steps forward again: the state from <paramref name="current" /> goes on the undo stack.</summary>
    public UndoSnapshot? Redo(Func<UndoSnapshot> current) => Step(redo, undo, current);

    /// <summary>Forgets every step (a preset was loaded).</summary>
    public void Clear() {
        undo.Clear();
        redo.Clear();
        coalesceKey = null;
    }

    UndoSnapshot? Step(Stack from, Stack to, Func<UndoSnapshot> current) {
        coalesceKey = null;
        if (from.Pop() is not { } target) {
            return null;
        }

        to.Push(current());
        return target;
    }

    /// <summary>Newest step last; the oldest go first once there are too many or they hold too many bytes.</summary>
    sealed class Stack(int maxSteps, long maxBytes) {
        readonly LinkedList<UndoSnapshot> steps = [];

        public int Count => steps.Count;

        public long Bytes { get; private set; }

        public void Push(UndoSnapshot snapshot) {
            steps.AddLast(snapshot);
            Bytes += snapshot.Json.Length;
            // The newest step stays even if it alone is over the budget: undo of the last edit always works.
            while (steps.Count > 1 && (steps.Count > maxSteps || Bytes > maxBytes)) {
                Bytes -= steps.First!.Value.Json.Length;
                steps.RemoveFirst();
            }
        }

        public UndoSnapshot? Pop() {
            if (steps.Last is not { } last) {
                return null;
            }

            steps.RemoveLast();
            Bytes -= last.Value.Json.Length;
            return last.Value;
        }

        public void Clear() {
            steps.Clear();
            Bytes = 0;
        }
    }
}
