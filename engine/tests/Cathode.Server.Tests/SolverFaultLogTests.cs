using Cathode.Server.Engine;

namespace Cathode.Server.Tests;

/// <summary>
///     The log line for a block whose circuit solver gives up on something (#278): the Amp, Drive and Wah blocks'
///     fault meters, said when they move and not thirty times a second.
/// </summary>
public sealed class SolverFaultLogTests {
    readonly List<string> lines = [];
    readonly ManualClock clock = new();
    readonly SolverFaultLog log;

    public SolverFaultLogTests() {
        using var provider = new CapturingLoggerProvider(lines.Add);
        log = new(provider.CreateLogger("faults"), clock);
    }

    [Fact]
    public void A_healthy_rig_logs_nothing() {
        for (var frame = 0; frame < 100; frame++) {
            log.Observe(Frame(("amp1", 0, 0, 0), ("drive1", 0, 0, 0)));
            clock.Advance(TimeSpan.FromMilliseconds(33));
        }

        log.Observe(null); // meters of another graph
        log.Observe(new() { ["cab1"] = new() { ["peak"] = 0.5f, ["preampIn"] = -6 } });
        Assert.Empty(lines);
    }

    [Fact]
    public void A_count_that_moves_is_logged_with_the_blocks_totals() {
        log.Observe(Frame(("amp1", 0, 0, 0), ("wah1", 0, 0, 0)));
        log.Observe(Frame(("amp1", 13, 0, 1), ("wah1", 0, 0, 0)));
        var line = Assert.Single(lines);
        Assert.StartsWith("Warning faults: Block amp1:", line);
        Assert.Contains("13 samples that did not converge", line);
        Assert.Contains("0 samples with a value that is not a finite number", line);
        Assert.Contains("1 control changes that left a network singular", line);

        // The same totals again say nothing more, however long they stand.
        clock.Advance(SolverFaultLog.Quiet * 3);
        log.Observe(Frame(("amp1", 13, 0, 1), ("wah1", 0, 0, 0)));
        Assert.Single(lines);
    }

    [Fact]
    public void A_block_that_keeps_faulting_is_logged_once_per_quiet_time() {
        var count = 0;
        for (var frame = 0; frame < 30 * 25; frame++) { // 25 seconds of frames, a fault in each
            log.Observe(Frame(("amp1", ++count, 0, 0)));
            clock.Advance(TimeSpan.FromSeconds(1.0 / 30));
        }

        Assert.Equal(3, lines.Count); // at once, and after 10 and 20 seconds
        Assert.Contains(" 1 samples that did not converge", lines[0]);

        // The faults stop inside the quiet time: the totals they stopped at are still said, once.
        clock.Advance(SolverFaultLog.Quiet);
        log.Observe(Frame(("amp1", count, 0, 0)));
        log.Observe(Frame(("amp1", count, 0, 0)));
        Assert.Equal(4, lines.Count);
        Assert.Contains($" {count} samples that did not converge", lines[3]);
    }

    [Fact]
    public void A_block_made_again_starts_over() {
        log.Observe(Frame(("amp1", 40, 0, 0)));
        Assert.Single(lines); // already faulted when first seen

        log.Observe(Frame(("amp1", 0, 0, 0))); // the preset was loaded again: a new block under the same id
        log.Observe(Frame(("amp1", 2, 0, 0)));
        Assert.Equal(2, lines.Count);
        Assert.Contains(" 2 samples that did not converge", lines[1]);

        // A block that left the graph is forgotten, so a later block of that id is new as well.
        log.Observe(Frame(("drive1", 0, 0, 0)));
        log.Observe(Frame(("amp1", 1, 0, 0), ("drive1", 0, 0, 0)));
        Assert.Equal(3, lines.Count);
    }

    static Dictionary<string, Dictionary<string, float>> Frame(
        params (string Block, int Unconverged, int NonFinite, int Failed)[] blocks
    ) =>
        blocks.ToDictionary(
            static b => b.Block,
            static b => new Dictionary<string, float> {
                ["peak"] = 0.5f,
                ["solverUnconverged"] = b.Unconverged,
                ["solverNonFinite"] = b.NonFinite,
                ["solverFailedRecomputes"] = b.Failed
            }
        );

    sealed class ManualClock : TimeProvider {
        long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan by) => ticks += by.Ticks;
    }
}
