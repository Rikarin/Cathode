using Cathode.Contracts;
using System.Text.Json;

namespace Cathode.Server.Rig;

/// <summary>
///     Preset and scene tools (docs/08-blocks-parity.md, presets and scenes): Scene Ignore, the Scene Manager's per-scene
///     block states, copying and swapping scenes, swapping and resetting channels, scene level steps from MIDI, and the
///     startup preset. Every edit is undoable.
/// </summary>
public sealed partial class LiveRig {
    /// <summary>Turns Scene Ignore on or off for one channel of a block.</summary>
    public Task<LiveState> SetSceneIgnoreAsync(string id, string channel, bool enabled, long? expected) =>
        Locked(() => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no channels.");
                CheckChannel(channel, descriptor.Channels);
                PushUndo(null);
                block.SceneIgnore.Remove(channel);
                if (enabled) {
                    block.SceneIgnore.Add(channel);
                    block.SceneIgnore.Sort(StringComparer.Ordinal);
                }

                return Task.FromResult(Changed("sceneIgnoreChanged", id, channel));
            }
        );

    /// <summary>Sets a block's bypass and channel in any scene (the Scene Manager); the active scene takes effect at once.</summary>
    public Task<LiveState> SetSceneBlockAsync(int scene, string id, SceneBlockState state, long? expected) =>
        Locked(async () => {
                Check(expected);
                CheckScene(scene);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no scene state.");
                CheckChannel(state.Channel, descriptor.Channels);
                if (state.Bypassed && BuiltInTypes.IsBuiltIn(block.Type)) {
                    throw new RigException(400, $"'{id}' cannot be bypassed.");
                }

                PushUndo(null);
                var before = LiveStates();
                preset.Scenes[scene].Blocks[id] = state;
                if (scene == activeScene) {
                    held.TryRemove(id, out _);
                    await ApplyLiveAsync(before);
                }

                return Changed("sceneUpdated", id, state.Channel, scene);
            }
        );

    /// <summary>Copies a whole scene (name, block states, level, controllers, MIDI) onto another.</summary>
    public Task<LiveState> CopySceneAsync(ScenePairRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                CheckScene(request.From);
                CheckScene(request.To);
                PushUndo(null);
                var before = LiveStates();
                preset.Scenes[request.To] = Clone(preset.Scenes[request.From]);
                if (request.To == activeScene) {
                    held.Clear();
                    await ApplyLiveAsync(before);
                    engine.SetParams(generation, SceneValues());
                }

                return Changed("sceneCopied", scene: request.To);
            }
        );

    /// <summary>
    ///     Swaps two scenes. When one of them is active the rig follows it to its new number, so nothing audible
    ///     changes.
    /// </summary>
    public Task<LiveState> SwapScenesAsync(ScenePairRequest request, long? expected) =>
        Locked(() => {
                Check(expected);
                CheckScene(request.From);
                CheckScene(request.To);
                PushUndo(null);
                (preset.Scenes[request.From], preset.Scenes[request.To]) =
                    (preset.Scenes[request.To], preset.Scenes[request.From]);
                if (activeScene == request.From) {
                    activeScene = request.To;
                } else if (activeScene == request.To) {
                    activeScene = request.From;
                }

                return Task.FromResult(Changed("scenesSwapped", scene: activeScene));
            }
        );

    /// <summary>Swaps two channels of a block: their parameters and Scene Ignore. Scenes keep naming the same letters.</summary>
    public Task<LiveState> SwapChannelsAsync(string id, CopyChannelRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no channels.");
                CheckChannel(request.From, descriptor.Channels);
                CheckChannel(request.To, descriptor.Channels);
                PushUndo(null);
                var before = Channels(block);
                var a = block.Channels.GetValueOrDefault(request.From);
                var b = block.Channels.GetValueOrDefault(request.To);
                Put(block, request.From, b);
                Put(block, request.To, a);
                var ignoreA = block.IgnoresScenes(request.From);
                var ignoreB = block.IgnoresScenes(request.To);
                block.SceneIgnore.RemoveAll(c => c == request.From || c == request.To);
                if (ignoreA) {
                    block.SceneIgnore.Add(request.To);
                }

                if (ignoreB) {
                    block.SceneIgnore.Add(request.From);
                }

                block.SceneIgnore.Sort(StringComparer.Ordinal);
                await RefreshChannelAsync(block, before, request.From, request.To);
                return Changed("channelsSwapped", id, request.To);
            }
        );

    /// <summary>Puts one channel of a block back to the type's defaults (and Scene Ignore off).</summary>
    public Task<LiveState> ResetChannelAsync(string id, string channel, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no channels.");
                CheckChannel(channel, descriptor.Channels);
                PushUndo(null);
                var before = Channels(block);
                Put(block, channel, null);
                block.SceneIgnore.Remove(channel);
                await RefreshChannelAsync(block, before, channel);
                return Changed("channelReset", id, channel);
            }
        );

    /// <summary>Puts every channel of a block back to the type's defaults.</summary>
    public Task<LiveState> ResetBlockAsync(string id, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                PushUndo(null);
                var before = Channels(block);
                foreach (var channel in Preset.ChannelNames[..descriptor.Channels]) {
                    Put(block, channel, null);
                }

                block.SceneIgnore.Clear();
                await RefreshChannelAsync(block, before, [..Preset.ChannelNames[..descriptor.Channels]]);
                return Changed("blockReset", id);
            }
        );

    /// <summary>
    ///     MIDI Vol Inc / Vol Dec: steps the active scene's level by <paramref name="deltaDb" /> and saves the preset, so
    ///     the change is kept.
    /// </summary>
    public Task<LiveState> StepSceneLevelAsync(float deltaDb) =>
        Locked(() => {
                PushUndo(null);
                var scene = preset.Scenes[activeScene];
                scene.Level = Math.Clamp(scene.Level + deltaDb, -60f, 12f);
                engine.SetParams(generation, SceneValues());
                Changed("sceneUpdated", scene: activeScene);
                return Task.FromResult(SaveWorkingCopy());
            }
        );

    /// <summary>Loads the global startup preset, if one is set and still in the library.</summary>
    public async Task LoadStartupAsync() {
        if (globals.Get().StartupPreset is { } id && presets.Get(id) is not null) {
            await LoadAsync(id);
        }
    }

    /// <summary>
    ///     Stores <paramref name="values" /> as a block's channel, keeping a linked global block and the preset's other
    ///     blocks linked to it in step.
    /// </summary>
    void Put(PresetBlock block, string channel, Dictionary<string, float>? values) {
        if (values is null) {
            block.Channels.Remove(channel);
        } else {
            block.Channels[channel] = values;
        }

        if (block.Global is { } link) {
            globals.Update(g => {
                    if (g.Blocks.Find(b => b.Id == link) is { } shared) {
                        if (values is null) {
                            shared.Channels.Remove(channel);
                        } else {
                            shared.Channels[channel] = new(values);
                        }
                    }
                }
            );
        }

        MirrorLinked(block);
    }

    /// <summary>The block's channels as they are now (<see cref="Put" /> replaces a channel, never edits it).</summary>
    static Dictionary<string, Dictionary<string, float>> Channels(PresetBlock block) => new(block.Channels);

    /// <summary>
    ///     After a channel's values changed: sends them if the block is running one of <paramref name="channels" />.
    ///     Only a change to a parameter a recompile applies (a look-ahead, the oversampling) rebuilds the graph, as a
    ///     rebuild starts every block of the preset afresh (the pitch, delay and reverb lose their state); the values
    ///     the block had <paramref name="before" /> tell.
    /// </summary>
    async Task RefreshChannelAsync(
        PresetBlock block,
        Dictionary<string, Dictionary<string, float>> before,
        params string[] channels
    ) {
        // The block and the preset's other blocks linked to its global block (which had and now have its values).
        var running = Linked(block)
            .Prepend(block)
            .Select(b => (Block: b, Channel: Live(b.Id).Channel))
            .Where(r => channels.Contains(r.Channel))
            .ToList();
        var descriptor = catalog.Descriptor(block.Type)!;
        if (running.Any(r => RecompileDiffers(
                    descriptor,
                    before.GetValueOrDefault(r.Channel),
                    block.Channels.GetValueOrDefault(r.Channel)
                )
            )) {
            await SyncGraphAsync(); // the compiler has to see the new latency
            return;
        }

        foreach (var (shown, live) in running) {
            SyncModifiers(shown, live);
            engine.SetParams(generation, ChannelValues(shown, live));
        }
    }

    static Scene Clone(Scene scene) =>
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(scene, ContractsJsonContext.Default.Scene),
            ContractsJsonContext.Default.Scene
        )!;
}
