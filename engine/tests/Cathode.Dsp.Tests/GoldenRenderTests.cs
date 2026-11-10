using Cathode.Dsp.Blocks;
using Cathode.Dsp.IO;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Routing;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Golden renders (docs/03 §4 and §12): every registered block renders a fixed stereo stimulus that must match its
///     committed reference in <c>tests/assets/golden</c>. A refactor that changes the sound fails here; an intended
///     change is accepted by regenerating with <c>CATHODE_UPDATE_GOLDEN=1 dotnet test</c> and reviewing the diff. The
///     tolerance absorbs last-bit differences between platforms' math libraries, not audible changes.
/// </summary>
/// <remarks>
///     <para>
///         A block renders at its <b>golden setting</b> (<see cref="SetupFor" />): its defaults, except where the
///         defaults leave it a wire or put its effect after the end of the render. There the setting makes it audible
///         (EQ bands off zero, a gate threshold the stimulus crosses, delay times of tens of milliseconds, a recorded
///         loop, a synthetic impulse response, signals on the rows of a mixer).
///         <see cref="Golden_setting_changes_the_stimulus" /> fails for a block whose render is the stimulus or
///         silence, so a new block cannot commit a golden that holds no processing.
///     </para>
///     <para>
///         A block with a <c>type</c> choice also has <c>&lt;block&gt;-types.wav</c>: a shorter render of every type at
///         the same setting, two channels each (<see cref="Type_renders_match_the_committed_reference" />). The Drive's
///         circuit types and the Circuit Amp's models are left to their own files
///         (<see cref="DriveCircuitSolveTests" />, <see cref="AmpDefaultsTests" />).
///     </para>
/// </remarks>
public class GoldenRenderTests {
    const int Frames = 12_000;
    const int TypeFrames = 6_000;
    const int BlockSize = 64;
    const double ResidualDb = -90;

    /// <summary>
    ///     A render this close to another (the stimulus, or another type's render) counts as the same: far above the
    ///     match tolerance, far below any setting a player would hear.
    /// </summary>
    const double SameDb = -60;

    static readonly ProcessSpec Spec = new(48_000, BlockSize);
    static readonly BlockRegistry Registry = BlockRegistry.CreateDefault();
    static readonly string Folder = Path.Combine(FindEngineRoot(), "tests", "assets", "golden");

    /// <summary>The blocks whose render is the stimulus or silence by design, and why.</summary>
    static readonly Dictionary<string, string> NoSoundOfItsOwn = new() {
        ["fbsend"] = "has no output: its input leaves through the Feedback Return, whose golden renders the pair",
        ["rta"] = "an analyser: it passes its input through untouched"
    };

    public static TheoryData<string> BlockTypes => [..Registry.Descriptors.Select(static d => d.TypeId)];

    public static TheoryData<string> BlocksWithTypes =>
        [..Registry.Descriptors.Where(static d => TypesOf(d.TypeId).Count > 0).Select(static d => d.TypeId)];

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Golden_render_matches_the_committed_reference(string typeId) {
        var (left, right) = Render(typeId, null, Frames);
        var path = Path.Combine(Folder, typeId + ".wav");
        if (Updating(path)) {
            Directory.CreateDirectory(Folder);
            WavFile.Write(path, new((int)Spec.SampleRate, [left, right]));
            return;
        }

        Assert.True(File.Exists(path), $"No golden render for {typeId}: run CATHODE_UPDATE_GOLDEN=1 dotnet test.");
        var golden = WavFile.Read(path);
        Assert.True(
            golden.Channels.Length == 2 && golden.Channels[0].Length == Frames,
            $"The golden render of {typeId} is not {Frames} stereo frames: regenerate with CATHODE_UPDATE_GOLDEN=1."
        );
        Assert.True(
            Analysis.NullResidualDb(golden.Channels[0], left) < ResidualDb
            && Analysis.NullResidualDb(golden.Channels[1], right) < ResidualDb,
            $"{typeId} no longer matches its golden render (left "
            + $"{Analysis.NullResidualDb(golden.Channels[0], left):0.0} dB, right "
            + $"{Analysis.NullResidualDb(golden.Channels[1], right):0.0} dB). If the change is intended, regenerate "
            + "with CATHODE_UPDATE_GOLDEN=1."
        );
    }

    [Theory]
    [MemberData(nameof(BlocksWithTypes))]
    public void Type_renders_match_the_committed_reference(string typeId) {
        var types = TypesOf(typeId);
        var renders = new List<float[]>();
        foreach (var type in types) {
            var (left, right) = Render(typeId, type, TypeFrames);
            renders.Add(left);
            renders.Add(right);
        }

        var path = Path.Combine(Folder, typeId + "-types.wav");
        if (Updating(path)) {
            Directory.CreateDirectory(Folder);
            WavFile.Write(path, new((int)Spec.SampleRate, [..renders]));
            return;
        }

        Assert.True(File.Exists(path), $"No type renders for {typeId}: run CATHODE_UPDATE_GOLDEN=1 dotnet test.");
        var golden = WavFile.Read(path);
        Assert.True(
            golden.Channels.Length == renders.Count && golden.Channels[0].Length == TypeFrames,
            $"{typeId}-types.wav holds {golden.Channels.Length / 2} types of {golden.Channels[0].Length} frames, the "
            + $"block has {types.Count} of {TypeFrames}: regenerate with CATHODE_UPDATE_GOLDEN=1."
        );
        var options = Registry.GetDescriptor(typeId).Params[Registry.GetDescriptor(typeId).TypeIndex].Options!;
        var changed = new List<string>();
        for (var c = 0; c < renders.Count; c++) {
            var residual = Analysis.NullResidualDb(golden.Channels[c], renders[c]);
            if (residual >= ResidualDb) {
                changed.Add($"{options[types[c / 2]]} ({(c % 2 == 0 ? "left" : "right")} {residual:0.0} dB)");
            }
        }

        Assert.True(
            changed.Count == 0,
            $"{typeId} no longer matches its type renders: {string.Join(", ", changed)}. If the change is intended, "
            + "regenerate with CATHODE_UPDATE_GOLDEN=1."
        );
    }

    /// <summary>
    ///     The golden protects something: at its golden setting the block's render is neither the stimulus nor
    ///     silence. A block that is a wire at its defaults needs a golden setting in <see cref="SetupFor" />.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Golden_setting_changes_the_stimulus(string typeId) {
        var (left, right) = Render(typeId, null, Frames);
        var audible = Analysis.Peak(left) > 1e-3 || Analysis.Peak(right) > 1e-3;
        var processed = Analysis.NullResidualDb(Stimulus(1, Frames), left) > SameDb
            || Analysis.NullResidualDb(Stimulus(2, Frames), right) > SameDb;
        if (NoSoundOfItsOwn.TryGetValue(typeId, out var reason)) {
            Assert.False(
                audible && processed,
                $"{typeId} is listed as having no sound of its own ({reason}) but its render is one: drop it from "
                + "the list."
            );
            return;
        }

        Assert.True(audible, $"{typeId} renders silence at its golden setting: give it one that makes it audible.");
        Assert.True(
            processed,
            $"{typeId} renders the stimulus itself at its golden setting: give it one that makes it audible."
        );
    }

    /// <summary>
    ///     Each type's render is its own: no two types of a block render alike at the golden setting, so a type that
    ///     stopped doing what sets it apart cannot hide behind another's render.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlocksWithTypes))]
    public void Types_render_differently(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);
        var options = descriptor.Params[descriptor.TypeIndex].Options!;
        var types = TypesOf(typeId);
        var renders = types.Select(type => Render(typeId, type, TypeFrames)).ToArray();
        var same = new List<string>();
        for (var a = 0; a < types.Count; a++) {
            for (var b = a + 1; b < types.Count; b++) {
                if (Analysis.NullResidualDb(renders[a].Left, renders[b].Left) <= SameDb
                    && Analysis.NullResidualDb(renders[a].Right, renders[b].Right) <= SameDb) {
                    same.Add($"{options[types[a]]} and {options[types[b]]}");
                }
            }
        }

        Assert.True(
            same.Count == 0,
            $"{typeId}: these types render alike at the golden setting ({string.Join(", ", same)}): give the block "
            + "a setting that tells them apart."
        );
    }

    static bool Updating(string path) =>
        Environment.GetEnvironmentVariable("CATHODE_UPDATE_GOLDEN") == "1"
        || (!File.Exists(path) && Environment.GetEnvironmentVariable("CI") is null);

    /// <summary>
    ///     The values of the block's <c>type</c> choice that get a render here; none for a block without one. The
    ///     Drive's circuit types and the Circuit Amp's models have their own committed renders.
    /// </summary>
    static IReadOnlyList<int> TypesOf(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);
        if (descriptor.TypeIndex < 0 || typeId == CircuitAmpBlock.Info.TypeId) {
            return [];
        }

        var count = typeId == DriveBlock.Info.TypeId
            ? DriveBlock.CircuitTypes
            : descriptor.Params[descriptor.TypeIndex].Options!.Count;
        return [..Enumerable.Range(0, count)];
    }

    /// <summary>
    ///     Renders <paramref name="typeId" /> at its golden setting, on <paramref name="type" /> (with that type's
    ///     defaults under the setting) or on its default type.
    /// </summary>
    static (float[] Left, float[] Right) Render(string typeId, int? type, int frames) {
        using var block = Registry.Create(typeId);
        using var setup = SetupFor(typeId);
        if (type is { } value) {
            block.Params.SetTarget(block.Descriptor.TypeIndex, value);
            block.Params.ResetToDefaults();
        }

        foreach (var (id, target) in setup.Values) {
            var index = block.Descriptor.IndexOf(id);
            Assert.True(index >= 0, $"{typeId} has no parameter '{id}'.");
            if (type is null || index != block.Descriptor.TypeIndex) {
                block.Params.SetTarget(index, target);
            }
        }

        setup.Wire?.Invoke(block);
        block.Prepare(Spec);

        var inLeft = Stimulus(1, frames);
        var inRight = Stimulus(2, frames);
        if (setup.Hum > 0) {
            // One and a half cycles of 50 Hz in the silence after the tone.
            var hum = Signals.Sine(50, Spec.SampleRate, 1_440, setup.Hum);
            for (var i = 0; i < hum.Length; i++) {
                inLeft[3_000 + i] += hum[i];
                inRight[3_000 + i] += hum[i];
            }
        }

        var outLeft = setup.Rows ? new float[frames] : (float[])inLeft.Clone();
        var outRight = setup.Rows ? new float[frames] : (float[])inRight.Clone();
        for (var offset = 0; offset < frames; offset += BlockSize) {
            var n = Math.Min(BlockSize, frames - offset);
            var left = inLeft.AsSpan(offset, n);
            var right = inRight.AsSpan(offset, n);
            setup.Before?.Invoke(block, offset, left, right);
            var io = new StereoBuffer(outLeft.AsSpan(offset, n), outRight.AsSpan(offset, n));
            if (setup.Rows) {
                // The stimulus on row 1 and, with its sides swapped, on row 2: the graph compiler's cabled inputs.
                var rows = (ISideInputBlock)block;
                rows.SideInput(SideInputKind.Cable, 0, new(left, right), io);
                rows.SideInput(SideInputKind.Cable, 1, new(right, left), io);
            }

            block.Process(io, new(Spec.SampleRate, 120, offset));
        }

        return (outLeft, outRight);
    }

    /// <summary>
    ///     The golden setting of <paramref name="typeId" />: empty (the defaults) unless the defaults leave the block a
    ///     wire, or put what it does after the end of the render.
    /// </summary>
    static Setup SetupFor(string typeId) {
        var setup = new Setup();
        switch (typeId) {
            case "input":
                // The gate on, its threshold above the quiet burst and under the hum the stimulus gets here, which
                // opens every gate type but the Intelligent one, and a ratio and release that shut it within the
                // quiet burst; a pickup and a long cable.
                setup.Values = [
                    ("type", 1), ("threshold", -20), ("ratio", 10), ("release", 5), ("pickup", 2), ("cable", 1_000),
                    ("level", -3)
                ];
                setup.Hum = 0.15f;
                break;
            case "output" or "output2" or "output3" or "output4":
                setup.Rows = true;
                setup.Values = [
                    ("level1", -6), ("balance1", -40), ("level2", -12), ("balance2", 60), ("level", -3),
                    ("balance", 20)
                ];
                break;
            case "mixer":
                setup.Rows = true;
                setup.Values = [
                    ("gain1", 80), ("balance1", -50), ("gain2", 50), ("balance2", 60), ("level", -3), ("balance", 20)
                ];
                break;
            case "mux":
                // Row 2 alone: the stimulus with its sides swapped.
                setup.Rows = true;
                setup.Values = [("input", 1), ("level", -3), ("balance", 20)];
                break;
            case "volpan":
                setup.Values = [("volume", 70), ("taper", 4), ("balance", 30), ("panLeft", -60), ("threshold", -30)];
                break;
            case "gate":
                // The threshold above the tone's tail and the quiet burst, a ratio that leaves an expander's quiet
                // burst audible where a gate shuts it, and no hold and a short release, so the 6 dB the Modern types
                // stay open for under the threshold is heard.
                setup.Values = [("threshold", -20), ("ratio", 2), ("hold", 0), ("release", 5)];
                break;
            case "compressor":
                // The defaults, and the Compander's expander set above the quiet burst (no other type has one).
                setup.Values = [("expThreshold", -20)];
                break;
            case "filter":
                // The default Low Pass has no gain; the peaking, shelving and tilt types are wires without one, and
                // the second shelves are the first ones until Q passes what those allow.
                setup.Values = [("gain", 6), ("q", 2.5f)];
                break;
            case "pitch":
                // The defaults, and the settings that tell the types apart: the Whammy's pedal down, a capo of +3,
                // and custom scales that leave the diatonic ones on the tone's A.
                setup.Values = [("pedal", 60), ("capo", 15), ("custom1A", 5), ("custom2A", 9)];
                break;
            case "nam":
                // No capture is loaded (the models are not in the repository; NamAmpBlockTests renders them where
                // they are): the block's tone controls.
                setup.Values = [("bass", 4), ("middle", -3), ("treble", 3), ("presence", -4)];
                break;
            case "delay":
                setup.Values = [("time", 20), ("timeRight", 31), ("feedback", 50), ("feedbackRight", 40), ("mix", 40)];
                break;
            case "multidelay":
                setup.Values = [("time", 80), ("tap1Feedback", 30), ("tap3Feedback", -30)];
                break;
            case "tentap":
                // Uneven tap times set the Rhythm Tap apart from the Ten Tap's even ones.
                setup.Values = [
                    ("time", 120), ("feedback", 30), ("mix", 50), ("tap2Time", 25), ("tap5Time", 45), ("tap8Time", 85)
                ];
                break;
            case "megatap":
                setup.Values = [("time", 200), ("feedback", 30)];
                break;
            case "plex":
                // The eight lines at a tenth of their times: 14 to 59 ms.
                setup.Values = [("masterTime", 10)];
                break;
            case "peq":
                setup.Values = [("gain1", 6), ("gain2", -4), ("gain3", 5), ("q3", 2), ("gain4", -6), ("gain5", 4)];
                break;
            case "geq":
                setup.Values = [
                    ("g31", 4), ("g63", -3), ("g125", 5), ("g250", -4), ("g500", 3), ("g1k", -5), ("g2k", 4),
                    ("g4k", -3), ("g8k", 5), ("g16k", -4)
                ];
                break;
            case "irplayer":
                setup.Values = [("level2", -6), ("pan1", -50), ("pan2", 50)];
                setup.Wire = block => {
                    ((IAssetConsumer)block).LoadAsset("ir", setup.ImpulseResponse(21, 300f));
                    ((IAssetConsumer)block).LoadAsset("ir2", setup.ImpulseResponse(22, 120f));
                };
                break;
            case "tonematch":
                setup.Values = [("mix", 80)];
                setup.Wire = block => ((IAssetConsumer)block).LoadAsset("match", setup.ImpulseResponse(23, 60f));
                break;
            case "fbreturn": {
                // The pair: a Feedback Send hears the stimulus with its sides swapped, 6 dB down, and the return adds
                // it one block later to the stimulus.
                var bus = new FeedbackBus(BlockSize);
                var send = new FeedbackSendBlock { Bus = bus };
                send.Params.SetTarget(FeedbackSendBlock.Level, -6f);
                send.Prepare(Spec);
                setup.Owned.Add(send);
                var sendLeft = new float[BlockSize];
                var sendRight = new float[BlockSize];
                setup.Wire = block => ((FeedbackReturnBlock)block).Bus = bus;
                setup.Before = (_, offset, left, right) => {
                    right.CopyTo(sendLeft);
                    left.CopyTo(sendRight);
                    send.Process(
                        new(sendLeft.AsSpan(0, left.Length), sendRight.AsSpan(0, left.Length)),
                        new(Spec.SampleRate, 120, offset)
                    );
                };
                setup.Values = [("level", -3), ("outputLevel", -3)];
                break;
            }
            case "fxloop": {
                // A device on the loop that returns what the block is fed with its sides swapped.
                var loop = new HardwareLoop();
                setup.Owned.Add(loop);
                setup.Wire = block => ((FxLoopBlock)block).Port = loop;
                setup.Before = (_, _, left, right) => loop.BeginCallback(right, left, left.Length);
                setup.Values = [("return", -6), ("mix", 60), ("latency", 2)];
                break;
            }
            case "looper":
                // Record the noise burst, play it as a loop, overdub the quiet burst, then play that reversed.
                // (The offsets are whole blocks.)
                setup.Values = [("dryLevel", -6), ("crossfade", 2), ("highCut", 6_000)];
                setup.Before = static (block, offset, _, _) => {
                    var looper = ((LooperBlock)block).Looper!;
                    switch (offset) {
                        case 0:
                            looper.Apply(LooperAction.Record);
                            break;
                        case 1_536:
                            looper.Apply(LooperAction.Play);
                            break;
                        case 4_480:
                            looper.Apply(LooperAction.Overdub);
                            break;
                        case 5_504:
                            looper.Apply(LooperAction.Play);
                            break;
                        case 8_000:
                            looper.Apply(LooperAction.Reverse);
                            break;
                    }
                };
                break;
        }

        return setup;
    }

    /// <summary>
    ///     A noise burst, a decaying 220 Hz tone, silence for the tails, then a burst 12 dB quieter (under the
    ///     thresholds the dynamics blocks are set to) and silence again: different on each side. A shorter render is
    ///     the start of a longer one, and the type renders reach past the quiet burst.
    /// </summary>
    static float[] Stimulus(int seed, int frames) {
        var noise = Signals.Noise(1_500, seed, 0.25f);
        var tone = Signals.Sine(220 * seed, Spec.SampleRate, 1_500, 0.5f)
            .Select(static (v, i) => v * MathF.Exp(-i / 500f))
            .ToArray();
        var quiet = Signals.Noise(1_000, seed + 10, 0.06f);
        var all = Signals.Concat(noise, tone, new float[1_500], quiet, new float[Frames - 5_500]);
        return all[..frames];
    }

    static string FindEngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Cannot find the engine folder.");
    }

    /// <summary>Called before each sub-block with its offset in the render and the stimulus it is about to hear.</summary>
    delegate void BeforeBlock(IDspBlock block, int offset, ReadOnlySpan<float> left, ReadOnlySpan<float> right);

    /// <summary>A block's golden setting, and what the render must keep alive for it.</summary>
    sealed class Setup : IDisposable {
        string? folder;

        /// <summary>Parameter targets by id, set over the defaults before the block is prepared.</summary>
        public (string Id, float Value)[] Values { get; set; } = [];

        /// <summary>What the graph compiler would hand the block before preparing it: assets, a bus, a port.</summary>
        public Action<IDspBlock>? Wire { get; set; }

        public BeforeBlock? Before { get; set; }

        /// <summary>
        ///     The block is fed by row, as the graph compiler feeds a mixer its cabled inputs, instead of in place.
        /// </summary>
        public bool Rows { get; set; }

        /// <summary>
        ///     The amplitude of the 50 Hz mains hum added to both sides of the stimulus, in the silence between the
        ///     tone and the quiet burst; 0 for none.
        /// </summary>
        public float Hum { get; set; }

        public List<IDisposable> Owned { get; } = [];

        /// <summary>
        ///     A synthetic impulse response in a temporary file: a unit impulse and seeded noise that decays by 1/e
        ///     every <paramref name="decay" /> samples.
        /// </summary>
        public string ImpulseResponse(int seed, float decay) {
            folder ??= Directory.CreateTempSubdirectory("cathode-golden-").FullName;
            var samples = Signals.Noise(2_048, seed, 0.2f).Select((v, i) => v * MathF.Exp(-i / decay)).ToArray();
            samples[0] = 1f;
            var path = Path.Combine(folder, $"ir{seed}.wav");
            WavFile.Write(path, new((int)Spec.SampleRate, [samples]));
            return path;
        }

        public void Dispose() {
            foreach (var owned in Owned) {
                owned.Dispose();
            }

            if (folder is not null) {
                Directory.Delete(folder, true);
            }
        }
    }
}
