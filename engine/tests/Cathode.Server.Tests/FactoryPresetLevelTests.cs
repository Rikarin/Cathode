using Cathode.Audio;
using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Engine;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using System.Collections.Concurrent;

namespace Cathode.Server.Tests;

/// <summary>
///     Every planned scene of every factory preset (as many as <c>Factory/bank.json</c> gives lifts), rendered through
///     the engine with a guitar-like phrase (and any expression
///     pedal half way): no NaN, no output at or above -1 dBFS (the editor's clip warning), even with the phrase
///     played as hot as an input takes (peaking at full scale), and a loudness within
///     <see cref="Tolerance" /> of the bank's target
///     plus the scene's lift, measured as the editor's Preset Leveling measures (BS.1770 momentary loudness, gated and
///     integrated). With <c>CATHODE_UPDATE_FACTORY_LEVELS=1</c> it levels the bank instead: it rewrites the preset
///     files' amp Levels and scene levels (<see cref="FactoryLevels" />), then rebuild and run it again to check.
/// </summary>
public sealed class FactoryPresetLevelTests {
    const int Rate = 48_000;
    const float Tolerance = 1.5f;
    const float ClipDb = -1f;

    /// <summary>The phrase's peak, of full scale (<see cref="Phrase" />).</summary>
    const float HumbuckerPeak = 0.35f;

    static readonly ProcessSpec Spec = new(Rate, 64);

    /// <summary>Each preset's planned scenes' lifts over the bank's loudness, by id (<c>bank.json</c>).</summary>
    static readonly Dictionary<string, float[]> Lifts =
        FactoryPresets.Entries().ToDictionary(static e => e.Id, static e => e.Lifts);

    [Fact]
    public void Every_scene_renders_clean_at_the_bank_loudness() {
        var catalog = new BlockCatalog(BlockRegistry.CreateDefault());
        var input = Phrase();
        var (presets, problems) = FactoryPresets.Load(catalog);
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_FACTORY_LEVELS") == "1") {
            Level(catalog, presets, input);
            return; // the files just written are embedded, and checked, on the next build
        }

        Assert.Empty(problems);
        var measured = Measure(presets, input);
        // The hottest a guitar gets: peaking at full scale, about 9 dB over the phrase. It may be louder, never clip.
        var hot = Measure(presets, [..input.Select(static v => v / HumbuckerPeak)]);
        var failures = new List<string>();
        foreach (var preset in presets) {
            var lifts = Lifts[preset.Id];
            for (var s = 0; s < lifts.Length; s++) {
                var (loudness, peak, finite) = measured[(preset.Id, s)];
                var target = FactoryLevels.TargetLufs + lifts[s];
                var name = $"{preset.Name} / {preset.Scenes[s].Name}";
                if (!finite) {
                    failures.Add($"{name}: NaN or infinity in the output");
                } else if (peak >= ClipDb) {
                    failures.Add($"{name}: peaks at {peak:0.0} dBFS");
                }

                if (hot[(preset.Id, s)].Peak >= ClipDb) {
                    failures.Add($"{name}: peaks at {hot[(preset.Id, s)].Peak:0.0} dBFS with a guitar at full scale");
                }

                if (Math.Abs(loudness - target) > Tolerance) {
                    failures.Add($"{name}: {loudness:0.0} LUFS, not {target:0.0}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Loudness (LUFS), peak (dBFS) and finiteness of each planned scene, by preset id and scene.</summary>
    static ConcurrentDictionary<(string, int), (float Loudness, float Peak, bool Finite)> Measure(
        List<Preset> presets,
        float[] input
    ) {
        var jobs = presets.SelectMany(static p => Lifts[p.Id].Select((_, s) => (Preset: p, Scene: s))).ToList();
        var results = new ConcurrentDictionary<(string, int), (float, float, bool)>();
        Parallel.ForEach(
            jobs,
            new() { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 2) },
            job => results[(job.Preset.Id, job.Scene)] = Render(job.Preset, job.Scene, input)
        );
        return results;
    }

    static (float Loudness, float Peak, bool Finite) Render(Preset preset, int scene, float[] input) {
        var (left, right) = RenderOutput(preset, scene, input);
        var finite = true;
        var peak = 0f;
        for (var i = 0; i < left.Length; i++) {
            finite &= float.IsFinite(left[i]) && float.IsFinite(right[i]);
            peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        }

        return (finite ? Loudness(left, right) : float.NaN, 20 * MathF.Log10(Math.Max(peak, 1e-9f)), finite);
    }

    /// <summary>
    ///     A scene of a preset that uses no assets, rendered through the engine at the scene's level and the preset's
    ///     tempo, with every expression pedal half way.
    /// </summary>
    internal static (float[] Left, float[] Right) RenderOutput(Preset preset, int scene, float[] input) {
        // Factory presets use no assets, so the asset store is never asked.
        var definition = PresetGraph.Build(preset, scene, null!);
        using var graph = new GraphCompiler(BlockRegistry.CreateDefault()).Compile(definition, Spec, 1);
        var engine = new CathodeEngine(Spec);
        engine.Rig.SetTarget(RigControls.Descriptor.IndexOf("level"), preset.Scenes[scene].Level);
        engine.Rig.SetTarget(RigControls.Descriptor.IndexOf("tempo"), (float)preset.Tempo);
        // Expression pedals half way: a wah or a pitch pedal is levelled where it is played, not parked at the heel.
        for (var e = 1; e <= 8; e++) {
            var pedal = RigControls.Descriptor.IndexOf($"ext{e}");
            var range = RigControls.Descriptor.Params[pedal];
            engine.Rig.SetTarget(pedal, (range.Min + range.Max) / 2);
        }

        engine.Rig.SnapToTargets();
        engine.Publish(graph);
        return OfflineRenderer.Render(engine, input, input, 64);
    }

    /// <summary>
    ///     The editor's Preset Leveling (tools/leveling.ts): momentary loudness read about 30 times a second, readings
    ///     below -70 LUFS dropped, then those more than 10 LU under the average of the rest, and the rest averaged as
    ///     power.
    /// </summary>
    internal static float Loudness(float[] left, float[] right) {
        var meter = new LoudnessMeter(Rate);
        var readings = new List<double>();
        const int frame = Rate / 30;
        for (var start = 0; start + frame <= left.Length; start += frame) {
            meter.Process(left.AsSpan(start, frame), right.AsSpan(start, frame));
            if (meter.Momentary > -70) {
                readings.Add(Math.Pow(10, (meter.Momentary + 0.691) / 10));
            }
        }

        if (readings.Count == 0) {
            return LoudnessMeter.FloorLufs;
        }

        var gate = -0.691 + 10 * Math.Log10(readings.Average()) - 10;
        var kept = readings.Where(p => -0.691 + 10 * Math.Log10(p) > gate).ToList();
        return (float)(-0.691 + 10 * Math.Log10(kept.Average()));
    }

    /// <summary>
    ///     A guitar-like phrase, four seconds: three strummed open-E chords, a run of single notes, and a last chord
    ///     left to ring. Plucked strings (Karplus-Strong), peaking at 0.35 of full scale: a humbucker's level at an
    ///     input calibrated to 1 V.
    /// </summary>
    public static float[] Phrase() {
        var y = new double[Rate * 4];
        double[] chord = [82.41, 123.47, 164.81, 207.65, 246.94, 329.63];
        var seed = 1;
        foreach (var at in new[] { 0.0, 0.5, 1.0 }) {
            for (var s = 0; s < chord.Length; s++) {
                Pluck(y, (int)((at + s * 0.008) * Rate), chord[s], 0.5, seed++, 1.2);
            }
        }

        double[] line = [329.63, 392.0, 440.0, 493.88, 587.33, 493.88, 440.0, 392.0];
        for (var n = 0; n < line.Length; n++) {
            Pluck(y, (int)((1.5 + n * 0.1875) * Rate), line[n], 0.9, seed++, 0.8);
        }

        for (var s = 0; s < chord.Length; s++) {
            Pluck(y, (int)((3.0 + s * 0.012) * Rate), chord[s], 0.5, seed++, 2.5);
        }

        var peak = y.Max(Math.Abs);
        return [..y.Select(v => (float)(HumbuckerPeak * v / peak))];
    }

    /// <summary>A plucked string: a noise burst, low-passed like a pick, circulating in a damped delay line.</summary>
    static void Pluck(double[] y, int start, double frequency, double amplitude, int seed, double decaySeconds) {
        var random = new Random(seed);
        var length = (int)(Rate / frequency);
        var line = new double[length + 1];
        var previous = 0.0;
        for (var i = 0; i < line.Length; i++) {
            previous = 0.6 * previous + 0.4 * (random.NextDouble() * 2 - 1);
            line[i] = previous * amplitude;
        }

        var loss = Math.Pow(0.001, 1 / (decaySeconds * frequency)); // -60 dB after decaySeconds
        var index = 0;
        for (var n = start; n < y.Length && n < start + (int)(decaySeconds * 1.5 * Rate); n++) {
            var next = (index + 1) % line.Length;
            y[n] += line[index];
            line[index] = loss * 0.5 * (line[index] + line[next]);
            index = next;
        }
    }

    /// <summary>
    ///     Preset Leveling for the whole bank (<see cref="FactoryLevels" />): measures every planned scene; moves each
    ///     amp channel's Level by the error of the first scene on it, as far as the Level's range allows, and each
    ///     scene's level by what is left (all of it in a preset without an amp); and repeats until all are within
    ///     0.15 dB (an amp's output stage limits near full scale, so an untrimmed hot amp reads low at first). Then
    ///     writes the preset files.
    /// </summary>
    static void Level(BlockCatalog catalog, List<Preset> presets, float[] input) {
        var output = catalog.Descriptor("circuitamp")!;
        var range = output.Params[output.IndexOf("level")];
        for (var pass = 0; pass < 8; pass++) {
            var measured = Measure(presets, input);
            var worst = 0f;
            foreach (var preset in presets) {
                var lifts = Lifts[preset.Id];
                var error = lifts
                    .Select((lift, s) => FactoryLevels.TargetLufs + lift - measured[(preset.Id, s)].Loudness)
                    .ToArray();
                worst = Math.Max(worst, error.Max(Math.Abs));
                var amp = preset.Blocks.FirstOrDefault(static b => b.Type == "circuitamp");
                var moved = new float[Preset.ChannelNames.Length];
                if (amp is not null) {
                    var channel = lifts.Select((_, s) => Preset.ChannelNames.IndexOf(
                                preset.Scenes[s].StateOf(amp.Id).Channel
                            )
                        )
                        .ToArray();
                    for (var c = 0; c < moved.Length; c++) {
                        if (Array.IndexOf(channel, c) is not (var first and >= 0)
                            || !amp.Channels.TryGetValue(Preset.ChannelNames[c], out var values)) {
                            continue;
                        }

                        var was = values.GetValueOrDefault("level", range.Default);
                        values["level"] = MathF.Round(Math.Clamp(was + error[first], range.Min, range.Max), 1);
                        moved[c] = values["level"] - was;
                    }

                    for (var s = 0; s < lifts.Length; s++) {
                        error[s] -= moved[channel[s]];
                    }
                }

                for (var s = 0; s < lifts.Length; s++) {
                    preset.Scenes[s].Level = MathF.Round(preset.Scenes[s].Level + error[s], 1);
                }
            }

            if (worst < 0.15f) {
                break;
            }
        }

        foreach (var preset in presets) {
            FactoryFiles.Write(preset);
        }
    }
}
