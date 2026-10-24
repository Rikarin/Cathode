using System.Text.Json.Serialization;
using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Server;
using Cathode.Server.Api;
using Cathode.Server.Engine;
using Cathode.Server.Midi;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Cathode.Server.Stores;

// Cathode.Server: the REST and WebSocket API. It owns presets, scenes and channels and drives cathode-host over a
// Unix domain socket (docs/adr/0001-process-model.md, docs/adr/0004-state-ownership.md). Listens on localhost unless
// given other URLs, which then need an access token (Api/Access.cs).

var builder = WebApplication.CreateSlimBuilder(args);
var urls = builder.Configuration["urls"] ?? "http://127.0.0.1:5180";
var token = builder.Configuration["Cathode:Token"] is { Length: > 0 } configured ? configured : null;
Access.RequireTokenBeyondLoopback(urls, token);
builder.WebHost.UseUrls(urls);
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Cathode"));
builder.Services.ConfigureHttpJsonOptions(o => {
        o.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default);
        // Numbers are numbers: the web default also accepts "1", which turns every number in the OpenAPI document
        // (and so in generated clients) into number | string.
        o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    }
);
builder.Services.AddProblemDetails();
builder.Services.AddCathodeCors();
builder.Services.AddOpenApi(static o => o.MarkNonNullablePropertiesRequired());
builder.Services.AddSingleton(new BlockCatalog(BlockRegistry.CreateDefault().AddNativeBlocks()));
builder.Services.AddSingleton<PresetStore>();
builder.Services.AddSingleton<BlockPresetStore>();
builder.Services.AddSingleton<Library>();
builder.Services.AddSingleton<PresetPackage>();
builder.Services.AddSingleton<AssetStore>();
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<EngineConnection>();
builder.Services.AddSingleton<IEngineSync>(static s => s.GetRequiredService<EngineConnection>());
builder.Services.AddHostedService(static s => s.GetRequiredService<EngineConnection>());
builder.Services.AddSingleton<GlobalStore>();
builder.Services.AddSingleton<LiveRig>();
builder.Services.AddSingleton<IrTools>();
builder.Services.AddSingleton<MidiRouter>();
builder.Services.AddSingleton<SnapshotStore>();
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton<PresetTools>();

var app = builder.Build();
app.UseCathodeAccess(token);
app.UseWebSockets();
app.MapOpenApi();
app.MapCathodeApi();

var rig = app.Services.GetRequiredService<LiveRig>();
var engine = app.Services.GetRequiredService<EngineConnection>();
engine.Connected += () => rig.ResyncAsync();
engine.TelemetryUpdated += t => _ = rig.FollowEngineTempoAsync(t.Tempo);
app.Services.GetRequiredService<GlobalStore>().Changed += rig.ApplyGlobalSettings;
app.Services.GetRequiredService<MidiRouter>(); // listens from the start
await rig.LoadStartupAsync();

app.Run();

/// <summary>Entry point, public for integration tests.</summary>
public partial class Program;
