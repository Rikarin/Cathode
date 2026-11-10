using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Parameters;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Ipc;
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

    [Theory]
    [InlineData(256)]
    [InlineData(1_024)]
    public void Modulation_runs_at_the_control_rate_whatever_the_device_buffer(int buffer) {
        // A 20 Hz square LFO on the level: at a 1,024-frame buffer one evaluation per callback would give 2.3 points
        // per cycle. Cut into control sub-blocks, a buffer that is a multiple of them renders bit for bit what 64-frame
        // buffers do.
        var input = Signals.Step(48_000);

        var reference = Render(Spec, CathodeEngine.ControlBlockSize);
        var large = Render(new(Spec.SampleRate, buffer), buffer);

        Assert.Equal(reference, large);

        float[] Render(ProcessSpec spec, int frames) {
            var engine = new CathodeEngine(spec);
            var definition = Graph();
            definition.Modifiers.Add(Modifier("lfo1", -40f, 0f));
            engine.Publish(Compiler.Compile(definition, spec, 1));
            Set(engine, "lfo1Rate", 20f);
            Set(engine, "lfo1Shape", (float)Dsp.Modulation.LfoShape.Square);
            return OfflineRenderer.Render(engine, input, input, frames).Left;
        }
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

    [Fact]
    public void An_adsr_setting_changed_while_the_engine_runs_shapes_the_envelope() {
        // The engine is built with the defaults (10 ms attack, sustain 0.5); the settings arrive afterwards, as they
        // do from the Server.
        var sustained = Engine(Modifier("adsr1", -60f, 0f));
        Set(sustained, "adsr1Sustain", 1f);
        var slow = Engine(Modifier("adsr1", -60f, 0f));
        Set(slow, "adsr1Attack", 5_000f);
        var deaf = Engine(Modifier("adsr1", -60f, 0f));
        Set(deaf, "adsr1Threshold", -10f);
        var quiet = Enumerable.Repeat(0.1f, 48_000).ToArray(); // -20 dB: over the default threshold, under this one

        var (held, _) = OfflineRenderer.Render(sustained, Signals.Step(48_000), Signals.Step(48_000), 64);
        var (rising, _) = OfflineRenderer.Render(slow, Signals.Step(48_000), Signals.Step(48_000), 64);
        var (closed, _) = OfflineRenderer.Render(deaf, quiet, quiet, 64);

        // Sustain 1 holds the level at the modifier's maximum, 0 dB (the default 0.5 would be -30 dB).
        Assert.InRange(Analysis.Peak(held.AsSpan(36_000, 12_000)), 0.95, 1.0);
        // A 5 s attack is a fifth of the way up after a second: -48 dB, where the default would sit at -30 dB.
        Assert.InRange(Analysis.Peak(rising.AsSpan(47_000, 1_000)), 0.003, 0.005);
        // Under the threshold the envelope is released and idle: the modifier's minimum, -60 dB, on the -20 dB input.
        Assert.InRange(Analysis.Peak(closed.AsSpan(36_000, 12_000)), 0.00009, 0.00011);
    }

    /// <summary>
    ///     The rig's side of the blocks' conformance test: a smoothed rig parameter read with <c>Current</c> but never
    ///     advanced keeps its start-up value for good. Every one changed on a running engine must finish its ramp.
    /// </summary>
    [Fact]
    public void Every_smoothed_rig_parameter_changed_while_the_engine_runs_reaches_its_target() {
        // Read from the target (and smoothed, where it matters, by the engine's own gain ramps), never advanced.
        string[] readFromTheTarget = [
            "metroLevel", "effectsMix", "gateOffset", "in1Gain", "in1Cal", "in2Gain", "in2Cal", "in3Gain", "in3Cal",
            "in4Gain", "in4Cal", "out1Level", "out2Level", "out3Level", "out4Level", "pitchLow", "pitchHigh"
        ];
        var engine = Engine();
        var input = Enumerable.Repeat(0.5f, 48_000).ToArray();
        OfflineRenderer.Render(engine, input, input, 64);
        var moved = new List<int>();
        for (var i = 0; i < engine.Rig.Count; i++) {
            var p = engine.Rig.Descriptor(i);
            if (p.Kind != ParamKind.Float || p.Smoothing == ParamSmoothing.Step || readFromTheTarget.Contains(p.Id)) {
                continue;
            }

            // A quarter of the range away, towards the further end.
            var from = engine.Rig.GetTarget(i);
            var quarter = (p.Max - p.Min) / 4;
            engine.Rig.SetTarget(i, from - p.Min > p.Max - from ? from - quarter : from + quarter);
            moved.Add(i);
        }

        // A stage's value is read while the stage plays: every stage, at 20 Hz, is one cycle in 1.6 s.
        Set(engine, "seqStages", RigControls.SequencerStages);
        Set(engine, "seqRate", 20f);
        var cycle = Enumerable.Repeat(0.5f, 2 * 48_000).ToArray();
        OfflineRenderer.Render(engine, cycle, cycle, 64);

        var stuck = moved.Where(i => engine.Rig.IsRamping(i)).Select(i => engine.Rig.Descriptor(i).Id);
        Assert.Empty(stuck);
        Assert.All(moved, i => Assert.Equal(engine.Rig.GetTarget(i), engine.Rig.Current(i)));
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
    public void The_tuner_mutes_every_output_by_default_with_a_fade_and_unmutes_when_it_closes() {
        var engine = Engine();
        var input = Signals.Sine(220, 48_000, 48_000, 0.5f);

        Set(engine, "tuner", 1f);
        var (tuning, _) = OfflineRenderer.Render(engine, input, input, 64);
        Set(engine, "tuner", 0f);
        var (closed, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.Equal(RigControls.TunerMute, (int)engine.Rig.GetTarget(RigControls.Descriptor.IndexOf("tunerOut")));
        Assert.True(Analysis.Peak(tuning.AsSpan(4_800)) < 1e-6, "silent while tuning");
        Assert.True(Analysis.Peak(tuning.AsSpan(0, 32)) > 0, "fades rather than cuts");
        Assert.True(Analysis.Peak(closed.AsSpan(4_800, 4_800)) > 0.4, "back when the tuner closes");
    }

    [Fact]
    public void The_tuner_in_bypass_plays_the_dry_input_without_the_blocks() {
        // The Volume block at a steady -20 dB makes the processed signal tell from the dry one.
        var engine = Engine(Modifier("manualA", -20f, -20f));
        Set(engine, "tunerOut", RigControls.TunerBypass);
        var input = Signals.Sine(220, 48_000, 48_000, 0.5f);
        OfflineRenderer.Render(engine, input, input, 64); // the level settles at -20 dB

        Set(engine, "tuner", 1f);
        var (left, right) = OfflineRenderer.Render(engine, input, input, 64);
        Set(engine, "tuner", 0f);
        var (closed, _) = OfflineRenderer.Render(engine, input, input, 64);

        for (var i = 4_800; i < input.Length; i++) {
            Assert.Equal(input[i], left[i]);
            Assert.Equal(input[i], right[i]);
        }

        Assert.InRange(Analysis.Peak(closed.AsSpan(4_800)), 0.045, 0.055); // the preset again, at -20 dB
        // Over the first 32 samples the dry share has only begun to rise: still nearer the -20 dB preset than the dry.
        var start = Analysis.Peak(left.AsSpan(0, 32));
        Assert.True(start < 0.5 * Analysis.Peak(input.AsSpan(0, 32)), "crossfades rather than jumps");
    }

    [Fact]
    public void The_tuner_in_pass_through_leaves_the_output_as_it_was() {
        var input = Signals.Sine(220, 48_000, 24_000, 0.5f);
        var reference = Engine(Modifier("manualA", -20f, -20f));
        var (expected, _) = OfflineRenderer.Render(reference, input, input, 64);
        var engine = Engine(Modifier("manualA", -20f, -20f));
        Set(engine, "tunerOut", RigControls.TunerPassThrough);
        Set(engine, "tuner", 1f);

        var (left, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.Equal(expected, left);
    }

    [Theory]
    [InlineData(RigControls.TunerMute)]
    [InlineData(RigControls.TunerBypass)]
    public void The_tuner_output_runs_without_allocating(int mode) {
        var engine = Engine();
        Set(engine, "tunerOut", mode);
        var input = Signals.Noise(64, 1);
        var left = new float[64];
        var right = new float[64];
        engine.Process(new(input, input, left, right));

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 2_000; i++) {
                    Set(engine, "tuner", i % 500 < 250 ? 1f : 0f);
                    engine.Process(new(input, input, left, right));
                }
            }
        );

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_modifier_without_response_settings_follows_its_source_unchanged() {
        var shape = ModShape.From(null, null, null, null, null, null);

        Assert.True(shape.IsIdentity);
        for (var s = 0f; s <= 1f; s += 0.125f) {
            Assert.Equal(s, shape.Apply(s), 5);
            Assert.Equal(
                Modulation.Map(s, ModCurve.SCurve, 2f, 8f, false),
                Modulation.Map(s, shape, ModCurve.SCurve, 2f, 8f, false)
            );
        }
    }

    [Fact]
    public void Start_mid_and_end_set_the_response_at_rest_half_way_and_full_travel() {
        var inverted = ModShape.From(1f, null, 0f, null, null, null);
        var late = ModShape.From(0f, 0f, 1f, null, null, null); // nothing until half travel

        Assert.Equal(1f, inverted.Apply(0f), 5);
        Assert.Equal(0.5f, inverted.Apply(0.5f), 5);
        Assert.Equal(0f, inverted.Apply(1f), 5);
        Assert.Equal(0f, late.Apply(0.4f), 5);
        Assert.Equal(0.5f, late.Apply(0.75f), 5);
    }

    [Fact]
    public void Scale_and_offset_stretch_and_lift_the_response_within_its_range() {
        var doubled = ModShape.From(null, null, null, null, 2f, null);
        var lifted = ModShape.From(null, null, null, null, 0.5f, 0.25f);

        Assert.Equal(0.5f, doubled.Apply(0.25f), 5);
        Assert.Equal(1f, doubled.Apply(0.5f), 5);
        Assert.Equal(1f, doubled.Apply(0.9f), 5);
        Assert.Equal(0.25f, lifted.Apply(0f), 5);
        Assert.Equal(0.75f, lifted.Apply(1f), 5);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.9f)]
    public void Slope_bends_the_response_around_its_centre_and_keeps_its_ends(float slope) {
        var shape = ModShape.From(null, null, null, slope, null, null);

        Assert.Equal(0f, shape.Apply(0f), 5);
        Assert.Equal(0.5f, shape.Apply(0.5f), 5);
        Assert.Equal(1f, shape.Apply(1f), 5);
        var previous = 0f;
        for (var s = 0.05f; s <= 1f; s += 0.05f) {
            Assert.True(shape.Apply(s) >= previous, "rises all the way");
            previous = shape.Apply(s);
        }

        // Above 0.5 the middle is steeper than a straight line, below 0.5 flatter.
        var rise = shape.Apply(0.55f) - shape.Apply(0.45f);
        Assert.True(slope > 0.5f ? rise > 0.15f : rise < 0.07f, $"rise through the middle {rise}");
    }

    [Fact]
    public void A_response_inverts_what_the_source_does_to_the_parameter() {
        var modifier = Modifier("manualA", -40f, 0f);
        modifier.Start = 1f;
        modifier.End = 0f;
        var engine = Engine(modifier);

        var (rest, _) = OfflineRenderer.Render(engine, Signals.Step(9_600), Signals.Step(9_600), 64);
        Set(engine, "manualA", 1f);
        var (full, _) = OfflineRenderer.Render(engine, Signals.Step(9_600), Signals.Step(9_600), 64);

        Assert.Equal(1f, rest[^1], 2); // 0 dB with the pedal at rest
        Assert.Equal(0.01f, full[^1], 3); // -40 dB at full travel
    }

    [Theory]
    [InlineData(null, 0.25f, -10f)] // add: 0.5 + 0.25
    [InlineData("add", 1f, 0f)] // 0.5 + 1, held at 1
    [InlineData("multiply", 0.5f, -30f)] // 0.5 x 0.5
    public void Two_sources_on_one_parameter_are_added_or_multiplied(string? operation, float second, float db) {
        var modifier = Modifier("manualA", -40f, 0f);
        modifier.Source2 = "manualB";
        modifier.Operation = operation;
        var engine = Engine(modifier);
        Set(engine, "manualA", 0.5f);
        Set(engine, "manualB", second);

        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(9_600), Signals.Step(9_600), 64);

        Assert.Equal(MathF.Pow(10f, db / 20f), left[^1], 3);
    }

    [Theory]
    [InlineData("manualE")]
    [InlineData("ext9")]
    [InlineData("ext16")]
    public void Manual_e_and_externals_9_to_16_are_modifier_sources(string source) {
        var engine = Engine(Modifier(source, -40f, 0f));
        Set(engine, source, 0.5f);

        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(9_600), Signals.Step(9_600), 64);

        Assert.Equal(0.1f, left[^1], 3); // -20 dB, half way
        Assert.True(ModNames.TryParseSource(source, out var parsed));
        Assert.Equal(source, ModNames.Sources[(int)parsed]);
    }

    [Fact]
    public void Every_modifier_source_has_a_name_and_a_rig_parameter_where_a_person_sets_it() {
        Assert.Equal(Enum.GetValues<ModSource>().Length, ModNames.Sources.Length);
        Assert.Equal(
            RigControls.ManualCount,
            ModNames.Sources.Count(static s => s.StartsWith("manual", StringComparison.Ordinal))
        );
        Assert.Equal(
            RigControls.ExternalCount,
            ModNames.Sources.Count(static s => s.StartsWith("ext", StringComparison.Ordinal))
        );
        foreach (var name in ModNames.Sources.Where(static s =>
                     s.StartsWith("manual", StringComparison.Ordinal) || s.StartsWith("ext", StringComparison.Ordinal)
                 )) {
            Assert.True(RigControls.Descriptor.IndexOf(name) >= 0, name);
        }
    }

    [Fact]
    public void Modifiers_with_an_unknown_second_source_or_operation_are_rejected() {
        var definition = Graph();
        definition.Modifiers.Add(new() { Block = "vol", Param = "level", Source = "lfo1", Source2 = "lfo9" });
        definition.Modifiers.Add(new() { Block = "vol", Param = "pan", Source = "lfo1", Operation = "divide" });

        var error = Assert.Throws<GraphValidationException>(() => Compiler.Compile(definition, Spec, 1));

        Assert.Equal(2, error.Errors.Count);
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
