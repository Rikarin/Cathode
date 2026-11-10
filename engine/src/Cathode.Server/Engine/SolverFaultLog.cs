namespace Cathode.Server.Engine;

/// <summary>
///     Says in the log when a block's circuit solver gives up on something: the Amp, Drive and Wah blocks report what
///     their solvers could not finish as three meters (<c>solverUnconverged</c>, <c>solverNonFinite</c>,
///     <c>solverFailedRecomputes</c>: counts since the block was made, 0 on a healthy rig), and a count that moves is
///     a click, a control that stopped responding or a silent amp that a bug report should be able to name.
/// </summary>
/// <remarks>
///     Fed every telemetry frame (about 30 a second) from one thread. A block is logged when one of its counts has
///     risen since it was last logged, and then not again for <see cref="Quiet" />: a stage that drops a sample now
///     and then under hard drive gives a line every ten seconds with the totals, not thirty a second. A block whose
///     counts fall was made again (a preset load, an engine restart) and starts over. The counts arrive as floats,
///     exact up to 2^24.
/// </remarks>
public sealed partial class SolverFaultLog(ILogger logger, TimeProvider? clock = null) {
    /// <summary>The least time between two lines about one block.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(10);

    const string Unconverged = "solverUnconverged";
    const string NonFinite = "solverNonFinite";
    const string FailedRecomputes = "solverFailedRecomputes";

    readonly TimeProvider clock = clock ?? TimeProvider.System;
    readonly Dictionary<string, Entry> blocks = new(StringComparer.Ordinal);

    /// <summary>
    ///     Reads one telemetry frame's block meters (by block id, then meter id) and logs the blocks whose counts
    ///     have risen. Null (the meters belong to another graph) changes nothing.
    /// </summary>
    public void Observe(Dictionary<string, Dictionary<string, float>>? meters) {
        if (meters is null) {
            return;
        }

        var now = clock.GetTimestamp();
        foreach (var (block, values) in meters) {
            if (Read(values) is { } counts) {
                Observe(block, counts, now);
            }
        }

        if (blocks.Count > meters.Count) {
            Forget(meters);
        }
    }

    /// <summary>A block's three counts, or null for a block without them.</summary>
    static Counts? Read(Dictionary<string, float> values) =>
        values.TryGetValue(Unconverged, out var unconverged)
        && values.TryGetValue(NonFinite, out var nonFinite)
        && values.TryGetValue(FailedRecomputes, out var failed)
            ? new((long)unconverged, (long)nonFinite, (long)failed)
            : null;

    /// <summary>One block's counts in one frame: logged once they have risen and its quiet time is over.</summary>
    void Observe(string block, Counts counts, long now) {
        var known = blocks.TryGetValue(block, out var entry) && !counts.Below(entry.Logged);
        if (!known) {
            entry = new(default, null); // new, or made again: its counts start over
        }

        if (!counts.Above(entry.Logged)) {
            blocks[block] = entry;
            return;
        }

        if (entry.At is { } at && clock.GetElapsedTime(at, now) < Quiet) {
            return; // said a moment ago: the next frame after the quiet time carries the totals
        }

        SolverGaveUp(logger, block, counts.Unconverged, counts.NonFinite, counts.FailedRecomputes);
        blocks[block] = new(counts, now);
    }

    /// <summary>Drops the blocks that are no longer in the graph.</summary>
    void Forget(Dictionary<string, Dictionary<string, float>> meters) {
        foreach (var block in blocks.Keys.Where(b => !meters.ContainsKey(b)).ToList()) {
            blocks.Remove(block);
        }
    }

    readonly record struct Counts(long Unconverged, long NonFinite, long FailedRecomputes) {
        public bool Above(Counts other) =>
            Unconverged > other.Unconverged
            || NonFinite > other.NonFinite
            || FailedRecomputes > other.FailedRecomputes;

        public bool Below(Counts other) =>
            Unconverged < other.Unconverged
            || NonFinite < other.NonFinite
            || FailedRecomputes < other.FailedRecomputes;
    }

    /// <summary>The counts last logged for a block (or first seen), and when they were logged.</summary>
    readonly record struct Entry(Counts Logged, long? At);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Block {Block}: its circuit solver has given up on {Unconverged} samples that did not converge, "
            + "{NonFinite} samples with a value that is not a finite number and {FailedRecomputes} control changes "
            + "that left a network singular, since the block was made"
    )]
    static partial void SolverGaveUp(
        ILogger logger,
        string block,
        long unconverged,
        long nonFinite,
        long failedRecomputes
    );
}
