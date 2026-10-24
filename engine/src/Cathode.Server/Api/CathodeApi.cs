using System.Globalization;
using System.Reflection;
using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Midi;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cathode.Server.Api;

/// <summary>
///     REST API v1 (docs/02-architecture.md §6). Mutations accept <c>If-Match: "&lt;revision&gt;"</c> and answer with
///     the new live state and its revision as the ETag.
/// </summary>
public static class CathodeApi {
    static string Version =>
        typeof(CathodeApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";

    public static void MapCathodeApi(this WebApplication app) {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RigExceptionFilter>();

        api.MapGet("/system", (EngineConnection engine) => TypedResults.Ok(new SystemInfo(Version, engine.Status())))
            .WithName("GetSystem");

        api.MapGet("/system/latency", async (EngineConnection engine) => TypedResults.Ok(await engine.LatencyAsync()))
            .WithName("GetLatency")
            .WithSummary(
                "The latency from instrument to speaker: the device's input and output latency, the buffer, and the graph's latency along its longest path, block by block."
            );

        var catalog = api.MapGroup("/catalog").WithTags("Catalog");
        catalog.MapGet("/blocks", (BlockCatalog blocks) => TypedResults.Ok(blocks.All.ToArray()))
            .WithName("ListBlockTypes");
        catalog.MapGet(
                "/blocks/{typeId}",
                Results<Ok<BlockTypeInfo>, NotFound>(string typeId, BlockCatalog blocks) =>
                    blocks.Info(typeId) is { } info ? TypedResults.Ok(info) : TypedResults.NotFound()
            )
            .WithName("GetBlockType");

        catalog.MapGet("/rig", (BlockCatalog blocks) => TypedResults.Ok(blocks.Rig))
            .WithName("GetRigControls")
            .WithSummary("The rig parameters: controller settings, scene level, tempo and modifier sources.");

        var live = api.MapGroup("/live").WithTags("Live");
        live.MapGet("/", async (LiveRig rig, HttpContext http) => Respond(http, await rig.StateAsync()))
            .WithName("GetLive");
        live.MapPut(
                "/",
                async (Preset preset, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.PutAsync(preset, Expected(http)))
            )
            .WithName("PutLive")
            .WithSummary(
                "Replaces the working copy's content (undoable; the preset keeps its id). Used for A/B compare."
            );
        live.MapPost("/new", async (LiveRig rig, HttpContext http) => Respond(http, await rig.NewAsync()))
            .WithName("NewPreset");
        live.MapPut(
                "/preset/{id}",
                async (string id, LiveRig rig, HttpContext http) => Respond(http, await rig.LoadAsync(id))
            )
            .WithName("LoadPreset");
        live.MapPut(
                "/setlists/{id}/entries/{index:int}",
                async (string id, int index, Library books, LiveRig rig, HttpContext http) => {
                    var entry = books.Entry(id, index);
                    return Respond(http, await rig.LoadAsync(entry.PresetId, entry.Scene));
                }
            )
            .WithName("LoadSetlistEntry");
        live.MapPut(
                "/banks/{id}/slots/{slot:int}",
                async (string id, int slot, Library books, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.LoadAsync(books.Slot(id, slot)))
            )
            .WithName("LoadBankSlot");
        live.MapGet(
                "/export",
                async (LiveRig rig, PresetPackage package, CancellationToken cancellationToken) =>
                    await Export((await rig.StateAsync()).Preset, package, cancellationToken)
            )
            .Produces(200, contentType: "application/zip")
            .WithName("ExportLive");
        live.MapPost(
                "/save",
                async (SaveRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SaveAsync(request, Expected(http)))
            )
            .WithName("SavePreset");
        live.MapPost("/undo", async (LiveRig rig, HttpContext http) => Respond(http, await rig.UndoAsync()))
            .WithName("Undo");
        live.MapPost("/redo", async (LiveRig rig, HttpContext http) => Respond(http, await rig.RedoAsync()))
            .WithName("Redo");

        live.MapPost(
                "/blocks",
                async (AddBlockRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.AddBlockAsync(request, Expected(http)))
            )
            .WithName("AddBlock");
        live.MapDelete(
                "/blocks/{id}",
                async (string id, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.RemoveBlockAsync(id, Expected(http)))
            )
            .WithName("RemoveBlock");
        live.MapPut(
                "/blocks/{id}/position",
                async (string id, MoveBlockRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.MoveBlockAsync(id, request, Expected(http)))
            )
            .WithName("MoveBlock");
        live.MapPatch(
                "/blocks/{id}/params",
                async (string id, string? channel, Dictionary<string, float> values, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetParamsAsync(id, channel, values, Expected(http)))
            )
            .WithName("SetParams");
        live.MapPut(
                "/blocks/{id}/bypass",
                async (string id, BypassRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetBypassAsync(id, request.Bypassed, Expected(http)))
            )
            .WithName("SetBypass");
        live.MapPut(
                "/blocks/{id}/channel",
                async (string id, ChannelRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetChannelAsync(id, request.Channel, Expected(http)))
            )
            .WithName("SetChannel");
        live.MapPut(
                "/blocks/{id}/assets/{slot}",
                async (string id, string slot, AssetRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetAssetAsync(id, slot, request, Expected(http)))
            )
            .WithName("SetAsset");
        live.MapPost(
                "/blocks/{id}/tonematch",
                async (string id, ToneMatchRequest request, IrTools tools, HttpContext http) =>
                    Respond(http, await tools.ToneMatchAsync(id, request, http.RequestAborted))
            )
            .WithName("LearnToneMatch")
            .WithSummary(
                "Listens to the rig while you play, then sets a Tone Match block's correction towards a reference .wav."
            );

        live.MapPost(
                "/cables",
                async (CableRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.AddCableAsync(request, Expected(http)))
            )
            .WithName("AddCable");
        live.MapDelete(
                "/cables",
                async (string from, string to, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.RemoveCableAsync(new(from, to), Expected(http)))
            )
            .WithName("RemoveCable");

        live.MapPut(
                "/scenes/active",
                async (SceneSelectRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SelectSceneAsync(request.Scene))
            )
            .WithName("SelectScene");
        live.MapPatch(
                "/scenes/{scene:int}",
                async (int scene, SceneUpdateRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UpdateSceneAsync(scene, request, Expected(http)))
            )
            .WithName("UpdateScene");

        var library = api.MapGroup("/presets").WithTags("Presets");
        library.MapGet("/", (PresetStore store) => TypedResults.Ok(store.List().ToArray())).WithName("ListPresets");
        library.MapGet(
                "/{id}",
                Results<Ok<Preset>, NotFound>(string id, PresetStore store) =>
                    store.Get(id) is { } preset ? TypedResults.Ok(preset) : TypedResults.NotFound()
            )
            .WithName("GetPreset");
        library.MapPut(
                "/{id}",
                (string id, Preset preset, PresetStore store) => {
                    preset.Id = id;
                    store.Save(preset);
                    return TypedResults.NoContent();
                }
            )
            .WithName("PutPreset");
        library.MapDelete(
                "/{id}",
                Results<NoContent, NotFound>(string id, Library books) =>
                    books.DeletePreset(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeletePreset");

        library.MapGet(
                "/{id}/export",
                (string id, PresetStore store, PresetPackage package, CancellationToken cancellationToken) => {
                    var preset = store.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
                    return Export(preset, package, cancellationToken);
                }
            )
            .Produces(200, contentType: "application/zip")
            .WithName("ExportPreset");
        library.MapPost(
                "/import",
                async (HttpRequest request, PresetPackage package, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await package.ImportAsync(request.Body, cancellationToken))
            )
            .Accepts<Stream>("application/zip")
            .WithName("ImportPreset")
            .WithMetadata(new RequestSizeLimitAttribute(PresetPackage.MaxPackageBytes));

        var blockPresets = api.MapGroup("/block-presets").WithTags("Block Library");
        blockPresets.MapGet("/", (string? type, BlockPresetStore store) => TypedResults.Ok(store.List(type)))
            .WithName("ListBlockPresets")
            .WithSummary("The factory and user block presets, of one block type when ?type= is given.");
        blockPresets.MapGet(
                "/{id}",
                Results<Ok<BlockPreset>, NotFound>(string id, BlockPresetStore store) =>
                    store.Get(id) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound()
            )
            .WithName("GetBlockPreset");
        blockPresets.MapPut(
                "/{id}",
                (string id, BlockPreset blockPreset, BlockPresetStore store) => TypedResults.Ok(
                    store.Save(id, blockPreset)
                )
            )
            .WithName("PutBlockPreset")
            .WithSummary("Saves a user block preset (factory ones cannot be changed).");
        blockPresets.MapDelete(
                "/{id}",
                Results<NoContent, NotFound>(string id, BlockPresetStore store) =>
                    store.Delete(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteBlockPreset");
        live.MapPost(
                "/blocks/{id}/preset",
                async (
                    string id,
                    ApplyBlockPresetRequest request,
                    BlockPresetStore store,
                    LiveRig rig,
                    HttpContext http
                ) => {
                    var source = store.Get(request.Preset)
                        ?? throw RigException.NotFound($"No block preset '{request.Preset}'.");
                    return Respond(http, await rig.ApplyBlockPresetAsync(id, source, request.Channel, Expected(http)));
                }
            )
            .WithName("ApplyBlockPreset")
            .WithSummary(
                "Sets a channel of the block (default: the active scene's) to a block preset from the Block Library."
            );

        var setlists = api.MapGroup("/setlists").WithTags("Setlists");
        setlists.MapGet("/", (Library books) => TypedResults.Ok(books.Setlists())).WithName("ListSetlists");
        setlists.MapGet(
                "/{id}",
                Results<Ok<Setlist>, NotFound>(string id, Library books) =>
                    books.Setlist(id) is { } setlist ? TypedResults.Ok(setlist) : TypedResults.NotFound()
            )
            .WithName("GetSetlist");
        setlists.MapPut(
                "/{id}",
                (string id, Setlist setlist, Library books) => {
                    setlist.Id = id;
                    books.Save(setlist);
                    return TypedResults.Ok(setlist);
                }
            )
            .WithName("PutSetlist");
        setlists.MapDelete(
                "/{id}",
                Results<NoContent, NotFound>(string id, Library books) =>
                    books.DeleteSetlist(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteSetlist");

        var banks = api.MapGroup("/banks").WithTags("Banks");
        banks.MapGet("/", (Library books) => TypedResults.Ok(books.Banks())).WithName("ListBanks");
        banks.MapGet(
                "/{id}",
                Results<Ok<Bank>, NotFound>(string id, Library books) =>
                    books.Bank(id) is { } bank ? TypedResults.Ok(bank) : TypedResults.NotFound()
            )
            .WithName("GetBank");
        banks.MapPut(
                "/{id}",
                (string id, Bank bank, Library books) => {
                    bank.Id = id;
                    books.Save(bank);
                    return TypedResults.Ok(bank);
                }
            )
            .WithName("PutBank");
        banks.MapDelete(
                "/{id}",
                Results<NoContent, NotFound>(string id, Library books) =>
                    books.DeleteBank(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteBank");

        MapControls(api, live);

        var assets = api.MapGroup("/assets").WithTags("Assets");
        assets.MapGet("/", (AssetStore store) => TypedResults.Ok(store.List().ToArray())).WithName("ListAssets");
        assets.MapPost(
                "/",
                async (string name, HttpRequest request, AssetStore store, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await store.AddAsync(request.Body, name, cancellationToken))
            )
            .Accepts<Stream>("application/octet-stream")
            .WithName("UploadAsset");
        assets.MapPost(
                "/mix",
                async (IrMixRequest request, IrTools tools, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await tools.MixAsync(request, cancellationToken))
            )
            .WithName("MixImpulseResponses")
            .WithSummary("Blends impulse responses (levels, delays, polarity, peak alignment) into a new IR asset.");
        assets.MapDelete(
                "/{reference}",
                Results<NoContent, NotFound>(string reference, AssetStore store) =>
                    store.Delete(reference) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteAsset");
        assets.MapGet(
                "/{reference}/content",
                Results<FileStreamHttpResult, NotFound>(string reference, AssetStore store) =>
                    store.Get(reference) is { } info && store.PathOf(reference) is { } path
                        ? TypedResults.File(
                            File.OpenRead(path),
                            info.Kind switch {
                                "ir" => "audio/wav",
                                "cabgrid" => "application/zip",
                                _ => "application/json"
                            },
                            info.Name
                        )
                        : TypedResults.NotFound()
            )
            .WithName("GetAssetContent")
            .WithSummary("The asset's file, e.g. an impulse response for the editor's frequency-response plot.");

        live.MapPut(
                "/blocks/{id}/test-tone",
                async Task<IResult> (string id, TestToneRequest request, LiveRig rig, EngineConnection engine) => {
                    if (request.Enabled) {
                        await rig.CheckBlockAsync(id);
                    }

                    await engine.TestToneAsync(id, request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetTestTone")
            .WithSummary(
                "Plays a sine in place of the block's output (the cab resonance helper), or stops it. Nothing is stored in the preset, and loading another graph stops it."
            );

        live.MapGet(
                "/blocks/{id}/speaker-resonance",
                async (string id, LiveRig rig) => TypedResults.Ok(await rig.SpeakerResonanceAsync(id))
            )
            .WithName("GetSpeakerResonance")
            .WithSummary(
                "A Circuit Amp's speaker resonance: the frequency at LF Resonance 100 % for its model and speaker, the current setting and the frequency it gives."
            );

        api.MapPut(
                "/tuner",
                (BypassRequest request, IEngineSync engine) => {
                    engine.SetTuner(!request.Bypassed);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetTuner")
            .WithSummary("Turns the tuner on (bypassed = false) or off.");

        api.MapPut(
                "/scope",
                (ScopeRequest request, EngineConnection engine) => {
                    engine.ScopeEnabled = request.Enabled;
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetScope")
            .WithSummary("Starts or stops scope frames (the engine's output waveform) on the WebSocket.");

        api.MapPut(
                "/rta",
                (RtaRequest request, EngineConnection engine) => {
                    engine.SetRta(request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetRta")
            .WithSummary(
                "Starts or stops RTA frames on the WebSocket (the output's spectrum, or the RTA block's while a preset has one) and sets the bands, decay and window."
            );

        app.Map(
                "/ws",
                async (HttpContext http, EventHub hub) => {
                    if (!http.WebSockets.IsWebSocketRequest) {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    using var socket = await http.WebSockets.AcceptWebSocketAsync();
                    await hub.ServeAsync(socket, http.RequestAborted);
                }
            )
            .ExcludeFromDescription();
    }

    /// <summary>
    ///     Builds the package in a temporary file and streams that: ZipArchive writes some parts synchronously, which
    ///     Kestrel forbids on a response, and a file keeps large models out of memory. The file deletes itself on close.
    /// </summary>
    static async Task<IResult> Export(Preset preset, PresetPackage package, CancellationToken cancellationToken) {
        var name = string.Concat(preset.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        var file = new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            81_920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous
        );
        try {
            await package.ExportAsync(preset, file, cancellationToken);
            file.Position = 0;
            return Results.File(file, "application/zip", (name.Length > 0 ? name : "preset") + PresetPackage.Extension);
        } catch {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Phase 5: bypass modes, modifiers, controllers, tempo, channels, global blocks, globals, looper, MIDI.</summary>
    static void MapControls(RouteGroupBuilder api, RouteGroupBuilder live) {
        live.MapPut(
                "/blocks/{id}/bypass-mode",
                async (string id, BypassModeRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetBypassModeAsync(id, request.Mode, Expected(http)))
            )
            .WithName("SetBypassMode")
            .WithSummary(
                "thru, mute, muteIn, muteFxIn, muteOut or muteFxOut; the 'in' modes let delay and reverb tails spill over, "
                + "the 'out' modes keep the block listening while bypassed."
            );
        live.MapPut(
                "/modifiers",
                async (List<PresetModifier> modifiers, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetModifiersAsync(modifiers, Expected(http)))
            )
            .WithName("SetModifiers")
            .WithSummary("Replaces every modifier of the working copy.");
        live.MapPatch(
                "/controllers",
                async (Dictionary<string, float> values, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetControllersAsync(values, Expected(http)))
            )
            .WithName("SetControllers")
            .WithSummary("Controller settings by rig parameter id (lfo1Rate, adsr2Attack, envGain, seq3, ...).");
        live.MapPut(
                "/tempo",
                async (TempoRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetTempoAsync(request.Bpm, Expected(http)))
            )
            .WithName("SetTempo");
        live.MapPost(
                "/tap",
                (LiveRig rig) => {
                    rig.Tap();
                    return TypedResults.NoContent();
                }
            )
            .WithName("TapTempo");
        live.MapPost(
                "/blocks/{id}/channels/copy",
                async (string id, CopyChannelRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.CopyChannelAsync(id, request, Expected(http)))
            )
            .WithName("CopyChannel");
        live.MapPost(
                "/blocks/{id}/global",
                async (string id, LinkGlobalRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.LinkGlobalAsync(id, request.Name, Expected(http)))
            )
            .WithName("MakeGlobal")
            .WithSummary("Moves the block's settings to a new global block and links the block to it.");
        live.MapPut(
                "/blocks/{id}/global/{globalId}",
                async (string id, string globalId, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UseGlobalAsync(id, globalId, Expected(http)))
            )
            .WithName("UseGlobal");
        live.MapDelete(
                "/blocks/{id}/global",
                async (string id, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UnlinkGlobalAsync(id, Expected(http)))
            )
            .WithName("UnlinkGlobal");
        live.MapPut(
                "/manual/{index:int}",
                Results<NoContent, BadRequest>(int index, ValueRequest request, LiveRig rig) => {
                    if (index is < 0 or > 3) {
                        return TypedResults.BadRequest();
                    }

                    rig.SetManual(index, request.Value);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetManual")
            .WithSummary("Manual A-D (index 0-3), 0..1.");
        live.MapPut(
                "/external/{index:int}",
                Results<NoContent, BadRequest>(int index, ValueRequest request, LiveRig rig) => {
                    if (index is < 0 or > 7) {
                        return TypedResults.BadRequest();
                    }

                    rig.SetExternal(index, request.Value);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetExternal")
            .WithSummary("External 1-8 (index 0-7), 0..1: what a pedal or CC would set.");

        var global = api.MapGroup("/global").WithTags("Global");
        global.MapGet("/", (GlobalStore store) => TypedResults.Ok(store.Get())).WithName("GetGlobal");
        global.MapPut(
                "/",
                (GlobalSettings settings, GlobalStore store) => {
                    GlobalValidation.Check(settings);
                    return TypedResults.Ok(
                        store.Update(g => {
                                g.TempoMode = settings.TempoMode;
                                g.Tempo = Math.Clamp(settings.Tempo, 30, 300);
                                g.Metronome = settings.Metronome;
                                g.MetronomeLevel = Math.Clamp(settings.MetronomeLevel, -60, 0);
                                g.Outputs = settings.Outputs;
                                g.CopyOutput1To2 = settings.CopyOutput1To2;
                                g.Inputs = settings.Inputs;
                                g.Oversampling = settings.Oversampling;
                                g.Manual = settings.Manual;
                                g.MidiChannel = settings.MidiChannel;
                                g.MidiMappings = settings.MidiMappings;
                                g.Footswitches = settings.Footswitches;
                                g.ActiveLayout = settings.ActiveLayout;
                                g.Blocks = settings.Blocks;
                            }
                        )
                    );
                }
            )
            .WithName("PutGlobal")
            .WithSummary(
                "Replaces the global settings: tempo mode, metronome, outputs (mode, Boost/Pad, EQ), inputs (calibration, gain), oversampling, Manual A-D, MIDI, footswitches, global blocks."
            );

        var looper = api.MapGroup("/looper").WithTags("Looper");
        looper.MapPost(
                "/{action}",
                Results<NoContent, BadRequest<string>>(string action, IEngineSync engine) => {
                    string[] actions = ["record", "play", "stop", "overdub", "undo", "reverse", "halfSpeed", "clear"];
                    if (!actions.Contains(action)) {
                        return TypedResults.BadRequest($"Unknown looper action '{action}'.");
                    }

                    engine.Looper(action);
                    return TypedResults.NoContent();
                }
            )
            .WithName("LooperAction")
            .WithSummary("record, play, stop, overdub, undo, reverse, halfSpeed or clear.");
        looper.MapGet(
                "/export",
                async (IEngineSync engine) => {
                    var path = Path.Combine(Path.GetTempPath(), $"cathode-loop-{Guid.NewGuid():n}.wav");
                    try {
                        await engine.ExportLooperAsync(path);
                    } catch (IpcException e) {
                        throw new RigException(409, e.Message);
                    }

                    var file = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81_920,
                        FileOptions.DeleteOnClose | FileOptions.Asynchronous
                    );
                    return Results.File(file, "audio/wav", "loop.wav");
                }
            )
            .Produces(200, contentType: "audio/wav")
            .WithName("ExportLoop");

        var midi = api.MapGroup("/midi").WithTags("MIDI");
        midi.MapGet("/learn", (MidiRouter router) => TypedResults.Ok(router.Learning)).WithName("GetMidiLearn");
        midi.MapPut(
                "/learn",
                (MidiLearnRequest request, MidiRouter router) => {
                    GlobalValidation.CheckAction(request.Action);
                    router.Learn(request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("StartMidiLearn")
            .WithSummary(
                "The next CC, note or program change received is mapped to this action ('midiLearned' event)."
            );
        midi.MapDelete(
                "/learn",
                (MidiRouter router) => {
                    router.Learn(null);
                    return TypedResults.NoContent();
                }
            )
            .WithName("CancelMidiLearn");
    }

    static Ok<LiveState> Respond(HttpContext http, LiveState state) {
        http.Response.Headers.ETag = $"\"{state.Revision.ToString(CultureInfo.InvariantCulture)}\"";
        return TypedResults.Ok(state);
    }

    /// <summary>The revision from <c>If-Match</c>, if the client sent one.</summary>
    static long? Expected(HttpContext http) {
        var header = http.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision)
            ? revision
            : null;
    }
}
