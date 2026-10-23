using Cathode.Engine.Graph;
using Cathode.Ipc;

namespace Cathode.Server.Engine;

/// <summary>What the rig needs from the engine. Commands are delivered in the order they are issued.</summary>
public interface IEngineSync {
    bool IsConnected { get; }

    /// <summary>
    ///     Compiles and publishes a graph. Throws <see cref="IpcException" /> if the host rejects it. Completes at
    ///     once when no engine is connected; the rig resyncs on connect.
    /// </summary>
    Task LoadGraphAsync(GraphDefinition graph, int generation);

    /// <summary>Fire-and-forget, ordered after any earlier command.</summary>
    void SetParams(int generation, IReadOnlyList<ParamValue> values);

    /// <summary>Fire-and-forget, ordered after any earlier command.</summary>
    void SetBypass(int generation, string block, bool bypassed);

    void SetTuner(bool enabled);
}
