using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Server;
using Cathode.Server.Api;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Cathode.Server.Stores;

// Cathode.Server: the REST and WebSocket API. It owns presets, scenes and channels and drives cathode-host over a
// Unix domain socket (docs/adr/0001-process-model.md, docs/adr/0004-state-ownership.md). Listens on localhost only.

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5180");
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Cathode"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new BlockCatalog(BlockRegistry.CreateDefault().AddNativeBlocks()));
builder.Services.AddSingleton<PresetStore>();
builder.Services.AddSingleton<AssetStore>();
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<EngineConnection>();
builder.Services.AddSingleton<IEngineSync>(static s => s.GetRequiredService<EngineConnection>());
builder.Services.AddHostedService(static s => s.GetRequiredService<EngineConnection>());
builder.Services.AddSingleton<LiveRig>();

var app = builder.Build();
app.UseWebSockets();
app.MapOpenApi();
app.MapCathodeApi();

var rig = app.Services.GetRequiredService<LiveRig>();
app.Services.GetRequiredService<EngineConnection>().Connected += () => rig.ResyncAsync();

app.Run();

/// <summary>Entry point, public for integration tests.</summary>
public partial class Program;
