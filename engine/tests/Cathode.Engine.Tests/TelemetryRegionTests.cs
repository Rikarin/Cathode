using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.Engine.Telemetry;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

public class TelemetryRegionTests {
    static readonly ProcessSpec Spec = new(48_000, 64);

    [Fact]
    public void Engine_callbacks_fill_the_region() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        var monitor = new TelemetryMonitor(region);
        monitor.Poll();

        Run(engine, 100, 0.5f);
        var reading = monitor.Poll()!.Value;

        var snapshot = new TelemetrySnapshot();
        Assert.True(region.TryRead(snapshot));
        Assert.Equal(100, snapshot.Callbacks);
        Assert.Equal(6_400, snapshot.Frames);
        Assert.Equal(100, snapshot.Histogram.Sum());
        Assert.Equal(0.5f, reading.InputPeak);
        Assert.Equal(0f, reading.OutputPeakLeft); // no graph yet: silence out
        Assert.Equal(100, reading.Callbacks);
    }

    [Fact]
    public void An_rta_block_fills_the_analyser_ring_with_its_selected_channel() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        engine.Publish(
            new GraphCompiler(BlockRegistry.CreateDefault()).Compile(
                new() {
                    Nodes = [
                        new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                        new() { Id = "rta", Type = "rta", Row = 0, Column = 1, Parameters = { ["inputSelect"] = 2 } },
                        new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
                    ],
                    Cables = [new() { From = "in", To = "rta" }, new() { From = "rta", To = "out" }]
                },
                Spec,
                1
            )
        );
        var left = Signals.Step(8_192, 0.25f);
        var right = Signals.Step(8_192, 0.75f);

        var (outLeft, outRight) = OfflineRenderer.Render(engine, left, right, 256);
        var analysed = new float[4_096];

        Assert.Equal(8_192, region.AnalyzerWritten);
        Assert.True(region.ReadAnalyzer(analysed));
        Assert.All(analysed, static s => Assert.Equal(0.75f, s)); // Input Select: Right
        Assert.Equal(left, outLeft); // and the signal passes untouched
        Assert.Equal(right, outRight);
    }

    [Fact]
    public void Every_reader_sees_each_peak_once() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        var fast = new TelemetryMonitor(region);
        var slow = new TelemetryMonitor(region);
        fast.Poll();
        slow.Poll();

        Run(engine, 1, 0.9f); // a single loud callback, then quieter ones
        Run(engine, 40, 0.1f);
        var first = fast.Poll()!.Value;
        Run(engine, 40, 0.1f);
        var second = fast.Poll()!.Value;
        var third = fast.Poll()!.Value;
        var slowReading = slow.Poll()!.Value; // 81 callbacks = 20 slots later, still within the history

        Assert.Equal(0.9f, first.InputPeak);
        Assert.Equal(0.1f, second.InputPeak);
        Assert.Equal(0f, third.InputPeak); // nothing new since the previous poll
        Assert.Equal(0.9f, slowReading.InputPeak);
    }

    [Fact]
    public void Callbacks_longer_than_a_slot_update_every_slot_they_cover() {
        using var region = TelemetryRegion.CreateInMemory(48_000);
        var monitor = new TelemetryMonitor(region);
        monitor.Poll();

        region.RecordCallback(1_024, 10, 0.7f, 0.6f, 0.5f); // four slots
        var reading = monitor.Poll()!.Value;

        var snapshot = new TelemetrySnapshot();
        region.TryRead(snapshot);
        Assert.Equal((0.7f, 0.6f, 0.5f), (reading.InputPeak, reading.OutputPeakLeft, reading.OutputPeakRight));
        Assert.Equal([0, 1, 2, 3], snapshot.SlotNumbers.Where(static s => s >= 0).Order());
    }

    [Fact]
    public void Cpu_window_reports_the_percentile_maximum_and_overruns() {
        using var region = TelemetryRegion.CreateInMemory(48_000);
        var monitor = new TelemetryMonitor(region);
        monitor.Poll();

        for (var i = 0; i < 2_000; i++) {
            region.RecordCallback(64, i == 1_000 ? 150.5 : 12.3, 0, 0, 0);
        }

        region.SetDriverOverloads(3);
        var reading = monitor.Poll()!.Value;

        Assert.Equal(13, reading.CpuP999);
        Assert.Equal(151, reading.CpuMax);
        Assert.Equal(1, reading.Overruns);
        Assert.Equal(3, reading.DriverOverloads);
    }

    [Fact]
    public async Task Concurrent_reads_always_see_a_consistent_snapshot() {
        using var region = TelemetryRegion.CreateInMemory(48_000);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var writer = Task.Run(() => {
                var i = 0;
                while (!stop.IsCancellationRequested) {
                    region.RecordCallback(64, i++ % 150, 0.5f, 0.25f, 0.125f);
                }
            },
            TestContext.Current.CancellationToken
        );

        var snapshot = new TelemetrySnapshot();
        var reads = 0;
        while (!writer.IsCompleted) {
            if (!region.TryRead(snapshot)) {
                continue;
            }

            reads++;
            Assert.Equal(snapshot.Callbacks, snapshot.Histogram.Sum());
            Assert.Equal(snapshot.Callbacks * 64, snapshot.Frames);
        }

        await writer;
        Assert.True(reads > 100, $"only {reads} consistent reads");
    }

    [Fact]
    public void A_file_region_is_visible_through_a_separate_read_only_mapping() {
        var path = Path.Combine(Path.GetTempPath(), $"cathode-{Guid.NewGuid():n}.telemetry");
        using (var writer = TelemetryRegion.CreateFile(path, 44_100)) {
            using var reader = TelemetryRegion.OpenFile(path);
            var monitor = new TelemetryMonitor(reader);
            monitor.Poll();

            writer.RecordCallback(128, 20, 0.3f, 0.2f, 0.1f);
            var reading = monitor.Poll()!.Value;

            Assert.Equal(44_100, reader.SampleRate);
            Assert.Equal(1, reading.Callbacks);
            Assert.Equal(0.3f, reading.InputPeak);
        }

        Assert.False(File.Exists(path), "the owner deletes its file");
    }

    [Fact]
    public void Opening_something_that_is_not_a_region_fails_clearly() {
        var path = Path.Combine(Path.GetTempPath(), $"cathode-{Guid.NewGuid():n}.telemetry");
        File.WriteAllBytes(path, new byte[8_192]);
        try {
            Assert.Throws<InvalidDataException>(() => TelemetryRegion.OpenFile(path));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_scope_ring_returns_the_latest_output_in_order() {
        using var region = TelemetryRegion.CreateInMemory(48_000);
        var window = new float[1_000];
        Assert.False(region.ReadScope(window)); // not enough written yet

        var samples = Enumerable.Range(0, 5_000).Select(static i => (float)i).ToArray();
        for (var i = 0; i < samples.Length; i += 64) {
            region.WriteScope(samples.AsSpan(i, Math.Min(64, samples.Length - i)));
        }

        Assert.True(region.ReadScope(window));
        Assert.Equal(4_000f, window[0]);
        Assert.Equal(4_999f, window[^1]);
    }

    [Fact]
    public void Processing_with_telemetry_does_not_allocate() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        engine.Publish(new GraphCompiler(BlockRegistry.CreateDefault()).Compile(GraphTemplates.Basic(), Spec, 1));
        var input = Signals.Noise(64, 1);
        var left = new float[64];
        var right = new float[64];

        var allocated = AllocationProbe.Measure(
            () => {
                for (var i = 0; i < 2_000; i++) {
                    engine.Process(new(input, input, left, right));
                }
            },
            () => engine.Process(new(input, input, left, right))
        );

        Assert.Equal(0, allocated);
    }

    static void Run(CathodeEngine engine, int callbacks, float amplitude) {
        var input = Enumerable.Repeat(amplitude, 64).ToArray();
        var left = new float[64];
        var right = new float[64];
        for (var i = 0; i < callbacks; i++) {
            engine.Process(new(input, input, left, right));
        }
    }
}
