# ADR-0004: The Server owns the preset domain; the Host only executes graphs

- **Status:** accepted
- **Date:** 2026-10-23
- **Amends:** [02-architecture.md §4](../02-architecture.md#4-domain-model-what-a-preset-is), which put the live working copy in the engine

## Context

[ADR-0001](0001-process-model.md) showed that **any** GC activity in the audio process causes dropouts. A 50 MB/s allocation storm with no HTTP at all caused 7 in 20 s. The preset domain is allocation-heavy by nature:
- JSON in and out
- validation against the block catalog
- scene and channel resolution
- undo snapshots
- the preset library
- the asset store

## Decision

| Concern | Lives in |
|---|---|
| Presets, scenes (8), channels (A–D), undo/redo, library, assets, validation, REST, WebSocket | **Cathode.Server** |
| Compiled graph, parameter targets, bypass ramps, audio I/O, meters, tuner analysis | **Cathode.Host** |

- The Server turns the working copy and the active scene into **engine commands** over IPC (`engine/src/Cathode.Ipc`):
  - **`loadGraph`** carries a full `GraphDefinition`, with asset paths resolved and the active channel's parameter values. It is sent only for structural changes: blocks added, moved or removed, cables, block types, assets.
  - **`setParams` and `setBypass`** cover everything else: knob moves, scene changes and channel changes. **Scenes and channels never recompile the graph.**
- The Host allocates only on `loadGraph` and for small command messages, using source-generated JSON. Telemetry (meters, CPU, tuner) goes out at about 30 Hz, about 10 KB/s of allocation. With the large gen0 budget, that is a GC every few hours, not every few seconds.
- **IPC transport:** a Unix domain socket on every OS (Windows 10 1803 and later supports AF_UNIX), carrying length-prefixed JSON frames. Requests carry an id; replies and events share the stream.

## Consequences

- MIDI and footswitch logic (Phase 5) will also run in the Server, which adds a local socket hop (about 0.1 ms) to scene changes. That is far below a buffer period, so acceptable. If it ever matters, the Host can execute pre-resolved scene command batches.
- Telemetry runs over the same socket instead of shared memory for now. That is simpler, and the measured cost is negligible. Shared memory remains the upgrade path if meter traffic grows (RTA, scope).
- WebSocket telemetry to the editor uses JSON text frames for now, not the binary frames planned in 02 §5.9. This is revisitable when the editor's needs are known.
- Functional tests use the same host service **in process** (`InProcessEngineLink`, in the Server's test project; the Server does not reference the host). Timing and soak tests must use the real two-process setup.
