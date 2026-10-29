using System.Runtime.InteropServices;

namespace Cathode.Engine.Telemetry;

/// <summary>
///     Where the rig's own controllers (docs/02-architecture.md §5.4) stand as of a callback, for the editor's live
///     markers: the sequencer's stage (from 0) of its <c>SequencerStages</c>, and the LFOs' and ADSRs' values (0..1).
///     <c>Running</c> is whether the controllers advanced in that callback: they stand still without a graph.
///     Blittable: it is part of <see cref="TelemetryLayout" />.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct ControllerState(
    bool Running,
    int SequencerStage,
    int SequencerStages,
    float Lfo1,
    float Lfo2,
    float Adsr1,
    float Adsr2
);
