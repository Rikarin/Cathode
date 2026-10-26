using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

public class RigTests {
    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly GraphCompiler Compiler = new(BlockRegistry.CreateDefault());

    [Fact]
    public void An_lfo_modifier_sweeps_a_parameter_between_its_limits_at_the_rate() {
        var engine = Engine(Modifier("lfo1", -40f, 0f));
        Set(engine, "lfo1Rate", 4f); // 4 Hz sine

        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(48_000), Signals.Step(48_000), 64);

        // Level swings between -40 dB and 0 dB, four times a second.
        var quarter = left.AsSpan(24_000, 12_000);
        Assert.InRange(Analysis.Peak(quarter), 0.95, 1.0);
        Assert.InRange(quarter.ToArray().Min(), 0.0, 0.02);
        var crossings = 0;
        for (var i = 24_001; i < 48_000; i++) {
            if (left[i - 1] < 0.5f && left[i] >= 0.5f) {
                crossings++;
            }
        }

        Assert.Equal(2, crossings); // 0.5 s at 4 Hz
    }

    [Fact]
    public void The_envelope_follower_opens_a_parameter_with_the_playing() {
        var engine = Engine(Modifier("env", -60f, 0f));
        Set(engine, "envGain", 0f);
        var input = Signals.Concat(new float[9_600], Signals.Sine(220, Spec.SampleRate, 9_600));

        var (left, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.True(Analysis.Peak(left.AsSpan(0, 9_600)) < 1e-6, "silence in, silence out");
        // A full-scale note reads about 0.95, mapped linearly onto -60..0 dB: about -3 dB, a peak near 0.7.
        Assert.True(Analysis.Peak(left.AsSpan(14_400, 4_800)) > 0.6, "loud playing opens the level");
    }

    [Theory]
    [InlineData(0.5, 120)]
    [InlineData(0.4, 150)]
    public void Tap_tempo_averages_sample_accurate_taps(double interval, double bpm) {
        var engine = Engine();
        for (var i = 0; i < 4; i++) {
            engine.Post(EngineCommand.TapTempo((long)(1_000 + i * interval * Spec.SampleRate)));
        }

        OfflineRenderer.Render(engine, new float[64], new float[64], 64);

        Assert.Equal(bpm, engine.Tempo, 3);
    }

    [Fact]
    public void A_tap_after_a_long_pause_starts_a_new_count() {
        var engine = Engine();
        engine.Post(EngineCommand.TapTempo(0));
        engine.Post(EngineCommand.TapTempo(24_000)); // 120 BPM
        engine.Post(EngineCommand.TapTempo(48_000 * 10)); // ten seconds later: a new count, tempo unchanged
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);

        Assert.Equal(120, engine.Tempo, 3);
    }

    [Fact]
    public void Scene_level_and_global_eq_shape_every_preset() {
        var engine = Engine();
        Set(engine, "level", -6.0206f);
        engine.GlobalEq.Params.SetTarget(engine.GlobalEq.Descriptor.IndexOf("level"), -6.0206f);

        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(9_600), Signals.Step(9_600), 64);

        Assert.Equal(0.25f, left[^1], 3);
    }

    [Fact]
    public void The_looper_records_plays_overdubs_and_undoes_the_last_pass() {
        var engine = Engine();
        engine.EnableLooper();
        var note = Signals.Sine(440, Spec.SampleRate, 4_800, 0.25f);
        var silence = new float[4_800];

        engine.Post(EngineCommand.Looper(LooperAction.Record));
        OfflineRenderer.Render(engine, note, note, 64);
        engine.Post(EngineCommand.Looper(LooperAction.Play));
        var (played, _) = OfflineRenderer.Render(engine, silence, silence, 64);

        Assert.Equal(LooperState.Playing, engine.Looper!.State);
        Assert.Equal(4_800, engine.Looper.Length);
        Assert.InRange(Analysis.Rms(played) / Analysis.Rms(note), 0.98, 1.02);

        // Overdub the same note once: the loop doubles; undo takes it back out.
        engine.Post(EngineCommand.Looper(LooperAction.Overdub));
        OfflineRenderer.Render(engine, note, note, 64);
        engine.Post(EngineCommand.Looper(LooperAction.Play));
        var (doubled, _) = OfflineRenderer.Render(engine, silence, silence, 64);
        engine.Post(EngineCommand.Looper(LooperAction.Undo));
        var (undone, _) = OfflineRenderer.Render(engine, silence, silence, 64);

        Assert.InRange(Analysis.Rms(doubled) / Analysis.Rms(note), 1.95, 2.05);
        Assert.InRange(Analysis.Rms(undone) / Analysis.Rms(note), 0.98, 1.02);
    }

    [Fact]
    public void Half_speed_and_reverse_change_how_the_loop_plays() {
        var engine = Engine();
        engine.EnableLooper();
        var ramp = Enumerable.Range(0, 4_800).Select(static i => i / 4_800f * 0.5f).ToArray();
        engine.Post(EngineCommand.Looper(LooperAction.Record));
        OfflineRenderer.Render(engine, ramp, ramp, 64);
        engine.Post(EngineCommand.Looper(LooperAction.Play));
        engine.Post(EngineCommand.Looper(LooperAction.HalfSpeed));
        var (slow, _) = OfflineRenderer.Render(engine, new float[4_800], new float[4_800], 64);
        engine.Post(EngineCommand.Looper(LooperAction.HalfSpeed));
        engine.Post(EngineCommand.Looper(LooperAction.Reverse));
        var (backwards, _) = OfflineRenderer.Render(engine, new float[1_000], new float[1_000], 64);

        // Half speed covers half the ramp in the time the loop lasts; reverse runs it downwards.
        Assert.InRange(slow[^1], 0.24f, 0.26f);
        Assert.True(backwards[^1] < backwards[100], "reverse plays the ramp downwards");
    }

    [Fact]
    public void Looper_commands_drive_the_grid_looper_when_the_preset_has_one_and_the_rig_looper_otherwise() {
        var engine = new CathodeEngine(Spec);
        engine.EnableLooper();
        var rig = engine.RigLooper!;
        engine.Publish(Compiler.Compile(LooperGraph(), Spec, 1));
        var note = Signals.Sine(440, Spec.SampleRate, 4_800, 0.25f);

        engine.Post(EngineCommand.Looper(LooperAction.Record));
        OfflineRenderer.Render(engine, note, note, 64);
        engine.Post(EngineCommand.Looper(LooperAction.Play));
        var (played, _) = OfflineRenderer.Render(engine, new float[4_800], new float[4_800], 64);

        var grid = ((LooperBlock)engine.Current!.Block(engine.Current.SlotOf("loop"))).Looper!;
        Assert.Same(grid, engine.Looper);
        Assert.Equal(LooperState.Playing, grid.State);
        Assert.Equal(4_800, grid.Length);
        Assert.Equal(LooperState.Empty, rig.State);
        Assert.InRange(Analysis.Rms(played) / Analysis.Rms(note), 0.95, 1.02); // the first 10 ms fade in (the seam)

        // A preset without a looper block: the grid loop stops with its preset and the rig looper takes the transport.
        engine.Publish(Compiler.Compile(Graph(), Spec, 2));
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        engine.Post(EngineCommand.Looper(LooperAction.Record));
        OfflineRenderer.Render(engine, note, note, 64);

        Assert.Equal(LooperState.Stopped, grid.State);
        Assert.Same(rig, engine.Looper);
        Assert.Equal(LooperState.Recording, rig.State);
    }

    [Fact]
    public void The_metronome_clicks_on_every_beat() {
        var engine = Engine();
        Set(engine, "metronome", 1f);
        Set(engine, "tempo", 120f);

        var (left, _) = OfflineRenderer.Render(engine, new float[96_000], new float[96_000], 64);

        // 120 BPM: a click every 24,000 samples, louder on the downbeat of four.
        int[] beats = [0, 24_000, 48_000, 72_000];
        Assert.All(beats, b => Assert.True(Analysis.Peak(left.AsSpan(b, 1_200)) > 0.05, $"click at {b}"));
        Assert.True(Analysis.Peak(left.AsSpan(10_000, 10_000)) < 1e-6, "silence between clicks");
    }

    [Fact]
    public void Mute_fades_every_output_out_and_back_in_metronome_included() {
        var engine = Engine();
        Set(engine, "metronome", 1f);
        Set(engine, "tempo", 120f);
        var input = Signals.Sine(220, 48_000, 48_000, 0.5f);

        Set(engine, "mute", 1f);
        var (muted, _) = OfflineRenderer.Render(engine, input, input, 64);
        Set(engine, "mute", 0f);
        var (unmuted, _) = OfflineRenderer.Render(engine, input, input, 64);

        // Silent once the fade is done (a beat's click at 24,000 included), and back to full level after it.
        Assert.True(Analysis.Peak(muted.AsSpan(4_800)) < 1e-6, "silent while muted");
        Assert.True(Analysis.Peak(unmuted.AsSpan(4_800, 4_800)) > 0.4, "back after unmuting");
        Assert.True(Analysis.Peak(muted.AsSpan(0, 32)) > 0, "fades rather than cuts");
    }

    [Fact]
    public void Modifiers_with_unknown_sources_or_parameters_are_rejected() {
        var definition = Graph();
        definition.Modifiers.Add(new() { Block = "vol", Param = "nope", Source = "lfo1" });
        definition.Modifiers.Add(new() { Block = "vol", Param = "level", Source = "lfo9" });

        var error = Assert.Throws<GraphValidationException>(() => Compiler.Compile(definition, Spec, 1));

        Assert.Equal(2, error.Errors.Count);
    }

    [Fact]
    public void The_rig_runs_without_allocating() {
        var engine = Engine(Modifier("lfo2", -20f, 0f));
        engine.EnableLooper();
        Set(engine, "metronome", 1f);
        engine.Post(EngineCommand.Looper(LooperAction.Record));
        var input = Signals.Noise(64, 1);
        var left = new float[64];
        var right = new float[64];

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 2_000; i++) {
                    if (i == 1_000) {
                        engine.Post(EngineCommand.Looper(LooperAction.Overdub));
                    }

                    engine.Process(new(input, input, left, right));
                }
            }
        );

        Assert.Equal(0, allocated);
    }

    static CathodeEngine Engine(ModifierDefinition? modifier = null) {
        var engine = new CathodeEngine(Spec);
        var definition = Graph();
        if (modifier is not null) {
            definition.Modifiers.Add(modifier);
        }

        engine.Publish(Compiler.Compile(definition, Spec, 1));
        return engine;
    }

    static ModifierDefinition Modifier(string source, float min, float max) =>
        new() {
            Block = "vol",
            Param = "level",
            Source = source,
            Min = min,
            Max = max
        };

    static void Set(CathodeEngine engine, string id, float value) =>
        engine.Rig.SetTarget(RigControls.Descriptor.IndexOf(id), value);

    static GraphDefinition Graph() =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "vol", Type = "volpan", Row = 0, Column = 1 },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "vol" }, new() { From = "vol", To = "out" }]
        };

    static GraphDefinition LooperGraph() =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "loop", Type = "looper", Row = 0, Column = 1 },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "loop" }, new() { From = "loop", To = "out" }]
        };
}
