using Cathode.Contracts;
using Cathode.Server.Engine;

namespace Cathode.Server.Realtime;

/// <summary>
///     The tuner, scope and RTA are rig-wide but opened per editor: each runs while any client has it on, so one
///     editor closing its tuner (or starting up) does not switch it off for another. A client names itself with the
///     same <c>client</c> id on its requests and its <c>/ws</c> socket; when its last socket closes, whatever it left
///     on is released. A request without a client id acts as before clients were counted: on adds an anonymous client,
///     off turns the analyser off for everyone.
/// </summary>
public sealed class AnalysisSubscriptions(IEngineSync sync, EngineConnection engine) {
    readonly Lock gate = new();
    readonly HashSet<string> tuner = [];
    readonly HashSet<string> scope = [];
    readonly HashSet<string> rta = [];
    readonly Dictionary<string, int> sockets = [];

    /// <summary>Whether any client has the tuner on.</summary>
    public bool TunerOn {
        get {
            lock (gate) {
                return tuner.Count > 0;
            }
        }
    }

    public void SetTuner(string? client, bool on) {
        lock (gate) {
            Set(tuner, client, on);
            sync.SetTuner(tuner.Count > 0);
        }
    }

    public void SetScope(string? client, bool on) {
        lock (gate) {
            Set(scope, client, on);
            engine.ScopeEnabled = scope.Count > 0;
        }
    }

    /// <summary>Applies the RTA's settings (whoever sends them) and the client's on or off. Throws on invalid settings.</summary>
    public void SetRta(string? client, RtaRequest request) {
        lock (gate) {
            engine.SetRta(request with { Enabled = engine.RtaEnabled }); // validates before anything changes
            Set(rta, client, request.Enabled);
            engine.RtaEnabled = rta.Count > 0;
        }
    }

    /// <summary>A client's socket opened.</summary>
    public void Join(string client) {
        lock (gate) {
            sockets[client] = sockets.GetValueOrDefault(client) + 1;
        }
    }

    /// <summary>A client's socket closed: with its last one, what it left on is released.</summary>
    public void Leave(string client) {
        lock (gate) {
            var open = sockets.GetValueOrDefault(client) - 1;
            if (open > 0) {
                sockets[client] = open;
                return;
            }

            sockets.Remove(client);
            if (tuner.Remove(client) && tuner.Count == 0) {
                sync.SetTuner(false);
            }

            if (scope.Remove(client) && scope.Count == 0) {
                engine.ScopeEnabled = false;
            }

            if (rta.Remove(client) && rta.Count == 0) {
                engine.RtaEnabled = false;
            }
        }
    }

    static void Set(HashSet<string> clients, string? client, bool on) {
        if (string.IsNullOrEmpty(client)) {
            if (on) {
                clients.Add("");
            } else {
                clients.Clear();
            }
        } else if (on) {
            clients.Add(client);
        } else {
            clients.Remove(client);
        }
    }
}
