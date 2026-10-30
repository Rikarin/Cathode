using Cathode.Contracts;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;

namespace Cathode.Server.Api;

/// <summary>
///     Listening tools: the test tone and speaker resonance, the tuner, scope and RTA subscriptions, and the
///     WebSocket they and the rig's events flow on.
/// </summary>
static class AnalysisApi {
    public static void Map(WebApplication app, RouteGroupBuilder api, RouteGroupBuilder live) {
        live.MapPut(
                "/blocks/{id}/test-tone",
                async Task<IResult>(string id, TestToneRequest request, LiveRig rig, EngineConnection engine) => {
                    if (request.Enabled) {
                        await rig.CheckBlockAsync(id);
                    }

                    await engine.TestToneAsync(id, request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetTestTone")
            .WithSummary(
                "Plays a sine in place of the block's output (the cab resonance helper), or stops it. Nothing is "
                + "stored in the preset, and loading another graph stops it."
            );

        live.MapGet(
                "/blocks/{id}/speaker-resonance",
                async (string id, LiveRig rig) => TypedResults.Ok(await rig.SpeakerResonanceAsync(id))
            )
            .WithName("GetSpeakerResonance")
            .WithSummary(
                "A Circuit Amp's speaker load resonance: the frequency at Load resonance 100 % for its model and "
                + "Speaker load (with Follow Cab, the followed Cab's speaker), the current setting and the frequency "
                + "it gives."
            );

        live.MapGet(
                "/blocks/{id}/response",
                async (string id, LiveRig rig) => TypedResults.Ok(await rig.GraphicEqResponseAsync(id))
            )
            .WithName("GetBlockResponse")
            .WithSummary(
                "A Graphic EQ's frequency response on its active channel, measured on the engine's equaliser (the "
                + "passive types on their networks): dB at 256 frequencies from 20 Hz to 20 kHz. 400 for another block."
            );

        api.MapPut(
                "/tuner",
                async (BypassRequest request, AnalysisSubscriptions analysis, string? client) => {
                    await analysis.SetTunerAsync(client, !request.Bypassed);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetTuner")
            .WithSummary(
                "Turns the tuner on (bypassed = false) or off for a client (the id it also opens /ws?client= with): "
                + "it runs while any client has it on. Without a client, off turns it off for everyone."
            );

        api.MapPut(
                "/scope",
                (ScopeRequest request, AnalysisSubscriptions analysis, string? client) => {
                    analysis.SetScope(client, request.Enabled);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetScope")
            .WithSummary(
                "Starts or stops scope frames (the engine's output waveform) on the WebSocket for a client (the id "
                + "it also opens /ws?client= with): they flow while any client has the scope on. Without a client, "
                + "off stops them for everyone."
            );

        api.MapPut(
                "/rta",
                (RtaRequest request, AnalysisSubscriptions analysis, string? client) => {
                    analysis.SetRta(client, request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetRta")
            .WithSummary(
                "Starts or stops RTA frames on the WebSocket (the output's spectrum, or the RTA block's while a "
                + "preset has one) for a client (the id it also opens /ws?client= with), and sets the bands, decay "
                + "and window. They flow while any client has the RTA on; without a client, off stops them for "
                + "everyone."
            );

        app.Map(
                "/ws",
                async (
                    HttpContext http,
                    EventHub hub,
                    AnalysisSubscriptions analysis,
                    IHostApplicationLifetime lifetime
                ) => {
                    if (!http.WebSockets.IsWebSocketRequest) {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    // ?client= names the editor, so the tuner, scope and RTA it leaves on are released with its socket.
                    // It is counted before the handshake completes: the client may close its old socket as soon as the
                    // new one is open, and the old one's leave must already see the new one.
                    var client = http.Request.Query["client"].ToString();
                    if (client.Length > 0) {
                        analysis.Join(client);
                    }

                    try {
                        using var socket = await http.WebSockets.AcceptWebSocketAsync();

                        // Closed (with a close frame) when the Server stops, too: shutdown does not wait out the
                        // timeout.
                        using var served = CancellationTokenSource.CreateLinkedTokenSource(
                            http.RequestAborted,
                            lifetime.ApplicationStopping
                        );
                        await hub.ServeAsync(socket, served.Token);
                    } finally {
                        if (client.Length > 0) {
                            await analysis.LeaveAsync(client);
                        }
                    }
                }
            )
            .ExcludeFromDescription();
    }
}
