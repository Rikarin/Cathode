# 02 — Architecture

> Project name: **Cathode**, an open-source project licensed GPL-3.0-or-later. Don't use Fractal trademarks such as "Axe", "Cygnus" or "Scene MIDI block" in product or model names.

This document turns the feature audit ([01](01-axe-fx-iii-audit.md)) and the DSP/runtime research into a concrete system design. Each decision has a short rationale. Items marked ⚠ have a go/no-go check in Phase 0 of the [implementation plan](04-implementation-plan.md).

---

## 1. Goals and non-goals

**Goals**
- An Axe-Fx III–class **grid-based** multi-effects processor in software, running on a normal computer with any pro audio interface.
- **< 6 ms measured round-trip latency** at 48 kHz / 64-sample buffers on a good ASIO or CoreAudio interface, with **zero algorithmic latency** in the default chain.
- A **headless engine** that is fully controllable through a versioned **REST API**, with a WebSocket stream for real-time telemetry. The editor is one client among many; others could be a phone app, a foot-controller bridge, or scripts.
- A **desktop editor** built with Electron, Angular 22, Tailwind 4 and xUI, giving the same mental model as Axe-Edit III: grid, block editor, scenes and channels, modifiers, footswitch layouts.
- Presets, scenes and channels must switch **glitch-free**, with spillover.
- Compatibility with the open **NAM** (`.nam` A1/A2) and **IR** (`.wav`) ecosystems from day one.

**Non-goals (v1)**
- Reading or writing Fractal `.syx` presets or IR banks. They are a proprietary format and an IP risk.
- Recreating Fractal's exact amp algorithms. We build our own models (see §6).
- Mobile and embedded targets. The design keeps them possible but does not optimise for them.

---

## 2. System overview

```
┌──────────────────────────── Electron app ────────────────────────────┐
│ Main process: spawns/supervises engine + gateway, native menus,       │
│               file dialogs, auto-update, MIDI-learn passthrough        │
│ Renderer:     Angular 22 (zoneless, signals) + Tailwind 4 + xUI        │
│               └─ generated TS API client (OpenAPI) + WS telemetry      │
└──────────────┬───────────────────────────────────────────────────────┘
               │ HTTP/REST (JSON)  +  WebSocket (binary telemetry, JSON events)
┌──────────────▼──────────── Cathode.Server (ASP.NET Core) ───────────┐
│ REST controllers · OpenAPI · auth (LAN token) · preset library (disk) │
│ IR/NAM file store · validation · undo/redo journal · event fan-out   │
└──────────────┬───────────────────────────────────────────────────────┘
               │ IPC: Unix domain socket / named pipe (commands, events)
               │      + shared-memory ring (meters, tuner, scope, RTA)
┌──────────────▼──────────── Cathode.Host (NativeAOT engine) ─────────┐
│ Control thread: command handling, graph compiler, model/IR loading,  │
│                 modifier config, MIDI input, tempo                   │
│        │  SPSC command ring ▼        ▲ retired-graph / event ring    │
│ Audio thread (owned by OS driver):                                    │
│   drain commands → evaluate modifiers → run compiled graph → meters   │
│ Analysis thread: tuner (YIN/MPM), RTA FFT, looper disk I/O            │
│ Native shim (C/C++): ASIO · CoreAudio(AUHAL) · JACK/PipeWire · WASAPI │
│                      NAM core · RTNeural · FTZ/DAZ · RT thread prio   │
└──────────────────────────────────────────────────────────────────────┘
```

### Why the engine and the REST server are separate processes

A .NET GC **suspends every thread that is running managed code**, including the audio callback, even when that thread never allocates. NativeAOT uses the same GC. Kestrel, routing and System.Text.Json allocate on every request. A 64-sample buffer at 48 kHz is 1.33 ms, so one gen0 pause at the wrong moment causes a dropout.

- **Cathode.Host** is a minimal NativeAOT process. In steady state it allocates nothing: DSP memory is native or pinned, commands are unmanaged structs, and telemetry goes to shared memory. GC settings: workstation, non-concurrent, `SustainedLowLatency`, and a large gen0 budget (`GCgen0size`) so the rare allocations at preset load almost never trigger a collection.
- **Cathode.Server** may allocate freely. Its GCs cannot touch the audio thread.
- Both processes implement the same `IEngineControl` interface. Functional tests can use **in-process mode** (`InProcEngineControl`), and production uses **out-of-process mode** (`IpcEngineControl`).
- **Confirmed by spike S2 ([ADR-0001](adr/0001-process-model.md)):**
  - In-process, even 100 req/s caused dropouts, and a 50 MB/s allocation storm with no HTTP caused 7 in 20 s.
  - Out-of-process, the audio callback stayed at baseline at 14,000 req/s.
  - No GC mode made in-process acceptable.
  - The Host must therefore stay **GC-free** during a performance, not merely low-allocation.

A user who runs no editor (a "headless rig on a mini-PC") starts only Host + Server, the way a hardware unit boots without Axe-Edit.

---

## 3. Repository layout

```
cathode/
├─ engine/                                  # .NET 10 solution
│  ├─ src/
│  │  ├─ Cathode.Dsp/                      # pure DSP: blocks, filters, oversampling, convolution
│  │  │                                     #   no I/O, no threads, no allocation after Prepare()
│  │  ├─ Cathode.Dsp.Native/               # P/Invoke: NAM core, RTNeural, FTZ/DAZ, vDSP FFT
│  │  ├─ Cathode.Engine/                   # graph model + compiler, scheduler, params, modifiers,
│  │  │                                     #   scenes/channels runtime, spillover, telemetry writer
│  │  ├─ Cathode.Audio/                    # IAudioBackend + Offline/Null/Native backends
│  │  ├─ Cathode.Contracts/                # preset schema, DTOs, block catalog metadata, migrations
│  │  ├─ Cathode.Ipc/                      # Host<->Server protocol, shared-memory telemetry
│  │  ├─ Cathode.Host/                     # engine executable (NativeAOT)
│  │  └─ Cathode.Server/                   # ASP.NET Core REST + WS gateway
│  ├─ native/
│  │  ├─ cth_audio/                          # C ABI shim over ASIO / CoreAudio / JACK / WASAPI
│  │  └─ cth_nam/                            # extern "C" wrapper of NeuralAmpModelerCore + RTNeural
│  ├─ tests/                                # see 03-testing-strategy.md
│  ├─ bench/                                # BenchmarkDotNet + callback-timing soak harness
│  └─ tools/
│     ├─ Cathode.Render/                   # CLI: render preset × DI file → wav (offline)
│     └─ Cathode.Analyze/                  # CLI: THD, alias ratio, ESR, MRSTFT, null residual
├─ editor/                                  # Nx workspace (same tooling as xUI)
│  ├─ apps/desktop/                         # Electron main + preload
│  ├─ apps/editor/                          # Angular renderer app
│  ├─ libs/api-client/                      # generated from engine OpenAPI (committed)
│  ├─ libs/audio-ui/                        # knob, meter, VU, tuner, EQ curve, grid... (xUI-styled)
│  ├─ libs/features/*                       # grid, block-editor, scenes, modifiers, library, setup
│  └─ e2e/                                  # Playwright _electron
├─ assets/                                  # factory presets, factory IRs (own captures), test DI corpus
└─ docs/
```

---

## 4. Domain model (what a preset is)

This mirrors the Axe-Fx III concepts from [01](01-axe-fx-iii-audit.md) so users of that unit feel at home. Grid size and instance limits are **configuration**, not hard-coded. The default is **6 rows × 14 columns**, matching the Axe-Fx III grid; software can allow larger grids.

```
Preset
├─ id, name, author, tags, schemaVersion, revision
├─ tempo { bpm, source: preset|global|midi }
├─ grid { rows, cols, cells[r][c] → BlockInstanceId | Shunt | Empty, cables[] }
├─ blocks: BlockInstance[]
│   ├─ instanceId ("amp1", "delay2")   ← stable string, never an index
│   ├─ typeId ("amp", "cab", "delay", …) + typeVersion
│   ├─ channels: { A, B, C, D } → ParamSet (paramId → value)
│   ├─ sceneIgnore: channels on which scene changes leave the block alone
│   ├─ isGlobal? → reference to a Global Block (shared across presets)
│   └─ modifiers: { paramId → ModifierAssignment }
├─ savedScene (the scene active when saved: where it loads, by default)
├─ scenes[8]
│   ├─ name, level
│   ├─ blockState[instanceId] → { bypassed, channel }
│   └─ sceneControllers[1..4] values
├─ controllers: LFO×2 (A/B outs), ADSR×2, Sequencer(2–32), EnvFollower, PitchDetector,
│               SceneCtrl×4, Manual×5, External×16 (global: CC / pedal) — 4 channels of settings
└─ footswitchLayout? (per-preset overrides)
```

Rules:
- **Parameter identity** is `(typeId, paramId)` with stable string IDs. Removing or renaming one requires a migration entry, and a test enforces this.
- **Channels** are complete parameter sets per block. **Scenes** pick a channel and a bypass state per block. This is exactly the Axe-Fx III mental model.
- A **working copy** in the engine (`ActivePreset`) is the authoritative live state. MIDI, footswitches and the editor all mutate it through the same command path. "Save" persists it to the library.

### Block catalog (metadata-driven UI)

Every block type publishes a **descriptor** that the editor uses to render generic UIs, and that the server uses to validate:

```jsonc
{
  "typeId": "delay", "typeVersion": 3, "category": "Delay",
  "maxInstances": 4, "stereo": true, "latencySamples": 0,
  "types": [ { "id": "digital-mono", "name": "Digital Mono" }, … ],  // "Type" selector (like Fractal's Type)
  "pages": [
    { "id": "config", "name": "Config", "params": ["type", "time", "tempo", "feedback", "mix"] },
    { "id": "mod",    "name": "Modulation", "params": ["modRate", "modDepth"] }
  ],
  "params": [
    { "id": "time", "name": "Time", "kind": "float", "min": 1, "max": 8000,
      "default": 375, "unit": "ms", "taper": "log", "smoothing": "glide",
      "modulatable": true, "display": { "decimals": 0 } },
    { "id": "tempo", "kind": "enum", "options": ["none","1/4","1/8.",…] },
    …
  ]
}
```

Types switch to sensible defaults for that type, as Fractal's "Type" knob does. A parameter may carry `typeLabels`, its name per value of the block's `type` (empty where that type has no such control): the editor renames or hides it as the type changes. The Circuit Amp uses it for its per-model panels, and the delays and reverbs for their type-specific pages (Delay's Dual, Mono Tape, Reverse and Sweep, the Plex shifters, the springs); `TypeLabelling` builds them. Custom editors (amp, EQ, cab, tuner) add rich panels **on top of** the generic descriptor; they never replace it.

---

## 5. Engine internals

### 5.1 Threads

| Thread | Owner | May allocate? | May block? | Work |
|---|---|---|---|---|
| Audio | OS driver (ASIO/CoreAudio/JACK), RT priority | **No** | **No** | Drain command ring, modifiers, graph, meters |
| Control | Host | Yes (pooled) | Yes | IPC, validation, graph compile, model/IR load, MIDI |
| Analysis | Host, normal priority | Preallocated | Yes | Tuner, RTA, scope decimation, looper disk streaming |
| Parallel DSP workers (Phase 6+) | Host, joined to macOS audio workgroup / MMCSS "Pro Audio" | No | Spin/wait only | Run independent grid rows concurrently |

### 5.2 Graph compilation

The grid is **edited** as cells and cables, then **compiled** to a flat execution plan:

1. Grid → DAG (shunts collapse into edges; a column with several inputs gets an implicit mixer node).
2. Validate: no cycles except explicit Feedback Send/Return pairs, which become a one-block delay, as on the hardware.
3. Topological sort, then **buffer allocation by graph colouring** (reuse stereo buffers whose lifetimes don't overlap).
4. **Latency compensation:** if a parallel path contains a latent block (for example a linear-phase EQ), the other paths get matching delays. The default chain has 0 latency.
5. Emit a `CompiledGraph`: an array of `Op` structs (`Process(block, inBuf, outBuf)`, `Mix(a,b,dst,gains)`, `Copy`, `Delay`). The audio thread walks it with **no virtual-dispatch surprises and no allocation**.

The **parameter set is not part of compilation**. Changing a knob, a scene or a channel never recompiles. Only grid or block-type changes do, and the few parameters flagged `Recompile` because they change a block's latency (the Compressor's Look Ahead): the server rebuilds the graph, a gapless swap, when one of those changes by a knob, a channel or a scene.

**As built.**
- The Input and Output cells run blocks of their own (`input`: noise gate and input level; `output`: a row mixer with main level and balance). The Output takes each input with its grid row, like the Mixer (a cabled side input, §5.3); a single row is scaled in place (`ScaleRow`), so a plain chain still needs one buffer. Neither can be bypassed.
- **Side inputs** (§5.3): a block that takes signals from elsewhere declares which (`ISideInputBlock.SideInputs`), and every grid signal reaches it through one op, `SideInput`. Cabled inputs by row (`Cables`) are emitted where the block's input is assembled, latency-aligned, each added by the block into its own buffer. Taps (`Taps`: the Compressor and Gate sidechain) are emitted before the block runs: the grid input and the output of the last live node on each row to its left. Each tap counts as one more reader of its source's buffer, so it is neither freed nor processed over in place first. Taps are not latency-aligned.
- A block that follows a Cab (`ICabFollower`: the Circuit Amp, for its Auto speaker load) is handed, at every compile, the first live `cab` block reached breadth-first through its outputs, or null. It reads that block's DynaCab enclosure and speaker (`CabBlock.DynaCab`) on the audio thread; nothing is copied or scheduled.
- Feedback Send/Return come in up to two pairs, matched by the number their ids end in.
- An RTA block gets the graph's `AnalyzerTap`; the engine moves what it holds into the telemetry region after each sub-block.
- **Hardware inputs** (docs/08 §2): the host maps device input channels onto six stereo sources, Input 1–4, USB 5/6 and USB 7/8 (`InputMap`; cathode-host `--input-map "1;3,4;;;5,6"`, the Setup page's input map; by default Input 1 only, on channel 1). Input 1 is the main pair (`AudioIO.InputLeft`/`InputRight`, what `ReadInput` copies); the others travel planar in `AudioIO.Sources` with a mask of the mapped ones. The engine copies all six into `HardwareInputs` at the start of each callback and sets its offset per sub-block. The compiler gives every block that declares `SideInputs.Hardware` (§5.3) its graph's `InputPort`; when the graph is replaced the engine fades that port out over the swap fade, as it fades the grid input into a spilling graph. Blocks name a source by index into `HardwareInputs.Names`, appended after their other choices: the Input cell's Source (0 = Input 1), the Multiplexer's Input Select (`FirstInput` = 7), a sidechain (`SidechainSource.FirstInput` = 8), the Pitch Detector Source and the Vocoder Analysis Source (after Block Input and Rows 1–6). The host reports the mapped sources (`HostInfo.InputSources`, a bit mask) and the Server names them in `EngineStatus.inputs`.
- **Outputs 1–4.** Besides the main `output`, the grid may hold one each of `output2`..`output4` (an `OutputBlock` under another type id). Each `WriteOutput` op carries its output's index; the graph adds Output 1 into the callback's output and Outputs 2–4 into the engine's `GraphOutputs`. Every output is delayed to the slowest path (`LatencySamples` is the latest output's), and the compiler records that path's blocks with their latencies (`CompiledGraph.LatencyPath`) for the latency report.
- **Oversampling filters.** `GraphDefinition.Oversampling` (`minimum` or `linear`, from the global settings) is handed to every `IOversampledBlock` (Circuit Amp, Drive, Dynamic Distortion) before `Prepare`. Linear phase gives those blocks a latency, which the compiler aligns like any other; the Server recompiles when the setting changes.
- **Test tone.** The compiler gives every graph the engine's `TestTone`; while it is on for a slot of that graph's generation, the sine replaces that block's output after it runs (the cab resonance helper). It is engine state, never part of a preset.

### 5.3 Block contract

```csharp
public interface IDspBlock : IDisposable
{
    BlockDescriptor Descriptor { get; }
    ParamBank Params { get; }                             // targets writable from any thread
    int LatencySamples { get; }
    void Prepare(in ProcessSpec spec);                    // control thread: allocate everything here
    void Reset();                                         // clear state, no allocation
    void Process(in StereoBuffer io, in BlockContext ctx); // audio thread, in place
}
```

`BlockContext` carries the sample rate, tempo, sample time, the MIDI notes currently held (`HeldNotes`), and four global settings from the rig bank: the Effects Mix offset that blocks with Global Mix on add to their Mix, the noise gate offset the Input gate adds to its threshold, `CabsOff` (Global "cab modeling off"), under which the Cab and IR Player blocks pass audio through, and Input 1's calibration (`InputCalibrationDb`, dB re 1 V at full scale; `InputVolts` is the factor), by which the voltage-modelled blocks (Circuit Amp, Drive) scale their input. The host's MIDI input posts note on/off straight to the engine as `EngineCommand.Note`, so the Pitch block can take its key and chord from a keyboard or DAW.

Processing is **in place**: the compiler hands each block one buffer that holds its input and receives its output. That is what lets a series chain run in a single buffer.

A block that needs a signal from another grid row, the grid input or a hardware input implements **`ISideInputBlock`** and declares what it takes in `SideInputs`, a set of flags fixed for the block type; its own parameters pick among those signals at run time, so changing the choice never recompiles. The compiler has one path for all of them:

| `SideInputs` | What the block gets | Blocks |
|---|---|---|
| `Cables` | Each cabled input on its own, by grid row, latency-aligned, instead of their sum: before `Process` the compiled graph calls `SideInput(Cable, row, signal, destination)` for every input (a `SideInput` op with a destination buffer), and the block adds it into its buffer as it sees fit | Multiplexer and Mixer (weigh the rows), the Output cells (row level and balance), the Vocoder (Analysis Source) and the Pitch block (Detector Source), which keep one chosen row aside in a side buffer as their analysis signal and sum the rest |
| `Taps` | The grid input (row −1) and the output of the last live block on each row to its left, offered before `Process` as `SideInput(Tap, row, signal, default)` (a `SideInput` op without a destination); not part of the block's input, not latency-aligned | Compressor and Gate (Sidechain, `SidechainSource`) |
| `Hardware` | The graph's `InputPort` (`ISideInputBlock.Inputs`), set before `Prepare`; the block reads the source it has chosen during `Process`, and the engine fades the port out when the graph is replaced (§5.2) | Input cell (Source), Multiplexer (Input Select), Compressor and Gate (Sidechain), Pitch (Detector Source), Vocoder (Analysis Source) |

Each source choice keeps its parameter values: rows and hardware inputs are appended in a fixed order after the block's other options (§5.2).

Other optional interfaces a block may add:
- `IAssetConsumer`: files (a NAM model, an IR) loaded by the compiler before `Prepare`.
- `IControlRefresh`: derived state rebuilt on the control thread (§5.4).
- `ISideInputBlock`: takes cabled inputs by row, taps of the grid input and other rows, or the hardware inputs (above).
- `ICabFollower`: linked at compile time to the next Cab block downstream (§5.2).
- `IEngageAware`: told on the audio thread when its bypass target turns from bypassed to active, before the bypass fade starts, so an LFO can restart from a set phase (Flanger and Phaser LFO Bypass Reset, Tremolo Start Phase). A block that starts engaged is not told.
- `IBlockMeters`: meters of its own (the Cab's `preampIn` level and Motor Drive's `motorReduction`), beside the `peak` meter every block has. They take the one block-meter path (§5.9): the engine reads them into the graph's meters after each callback (`ReadMeters`, on the audio thread, allocation-free), and they reach the editor with the block's peak.

- DSP state lives in `NativeMemory.AlignedAlloc` buffers (64-byte aligned) or pinned arrays.
- `Process` is `[SkipLocalsInit]` and uses `Span<T>` and `Vector128<T>` (the portable SIMD baseline; on Apple Silicon `Vector256` is emulated).
- Signal path is float32. **Double** is used for sensitive IIR state (low-frequency high-pass filters, resonant tone stacks), phase accumulators and Newton solvers.
- **Denormals:** FTZ/DAZ are set by a native helper when the audio thread enters, plus an ε-injection fallback in feedback paths.

### 5.4 Parameters, smoothing, modifiers

- Each block instance has a `ParamBank`: `float target[]`, `float current[]`, and a smoother kind per parameter (one-pole, linear ramp, or step for enums).
- Continuous knob changes **don't use the queue at all**: the control thread writes the target with `Volatile.Write`. Aligned 32-bit writes are atomic.
- Discrete events go through the **SPSC command ring** of unmanaged structs: bypass, channel change, scene change, tap tempo, looper transport, graph swap. The audio thread drains it at the start of each block.
- **Modifiers** (1 or 2 sources per knob, combined with Scale/Operation; min/max, start/mid/end, slope, offset, attack/release damping, auto-engage, PC reset, per-channel enable — the Axe-Fx III surface) are evaluated at **control rate**, every 16–32-sample sub-block, and applied as `target = base + depth·curve(source)` with a per-assignment min, max, slew and curve.
- A modifier may be **scoped to one channel** of its block. The compiler is told which modifiers start enabled; when the block changes channel the server sends `setModifier`, and the audio thread turns the binding on, or off with the channel's value restored, so a modulated value still in flight cannot win (`EngineCommandKind.SetModifier`).
- Filters smooth the **parameter** (cutoff), then recompute coefficients per sub-block. Any filter whose parameters can move uses a TPT/ZDF state-variable filter. *Confirmed in Phase 1:* the property suite found a direct-form biquad emitting +63 dBFS transients during a fast cutoff and Q sweep, and the SVF, which has the same magnitude response, fixed it.
- RBJ shelves are limited to Q ≤ 1. Above that they develop a large resonant bump at the corner.
- **Derived state too expensive for the audio thread** (the Cab block's mixed slot IRs: synthesised DynaCabs, interpolated IR packs, IR files and Flat slots, folded into one stereo pair, or with a Stereo input into a left-fed and a right-fed pair, four convolutions, since slots 1 and 3 hear the left input and 2 and 4 the right; the IR Player's two slots, cut, levelled, panned and combined in parallel, stereo or series) belongs to blocks that implement `IControlRefresh`. The host calls `Refresh()` on the control thread after every parameter message and on each telemetry tick (for targets that modifiers moved). The block compares the targets with what it last built, rebuilds only what changed, and hands the result over through an `Interlocked.Exchange` slot; the audio thread crossfades the new state in and returns the old one through a second slot for the control thread to dispose. Nothing locks, and nothing is allocated or freed on the audio thread.

### 5.5 Switching and spillover

| Change | Mechanism | Audible result |
|---|---|---|
| Knob | Smoothed target | None |
| Block bypass | 5–20 ms crossfade. All six bypass modes: THRU, MUTE, MUTE IN, MUTE OUT, MUTE FX IN, MUTE FX OUT (the "IN" modes give per-block spillover) | Click-free |
| Channel | Crossfade between two param sets on the same instance. For amp/cab, double-buffer the state where needed | Gapless |
| Scene | Batch of bypass, channel and level changes applied atomically in one audio block | Gapless; delays and reverbs spill over naturally |
| Preset | New `CompiledGraph` is built and pre-rolled off-thread, then **RCU swap** via `Interlocked.Exchange`, then a 10–30 ms crossfade. The old graph's delay and reverb tails keep running muted at their input until they drop below −90 dB or hit a time cap (**spillover**). Retired graphs return through a ring to the control thread for disposal | Gapless, with tails |

Budget for **two graphs alive at once** during a preset change.

**As built (Phase 5).**
- **The rig layer** (`Cathode.Engine.Rig`) wraps the compiled graph and outlives preset changes. Per sub-block it runs, in order:
  1. the controllers
  2. the graph's modifiers
  3. the graph, plus the spilling tail graph
  4. the scene level
  5. the rig looper
  6. the global EQ
  7. the metronome
- **Rig settings** live in one parameter bank (`rig`) that IPC reaches without a graph generation, so scene and tempo changes never recompile.
- **Looper:** one core (`Cathode.Dsp.Looping.Looper`) serves the rig looper and the `looper` grid block. While the running graph holds a Looper block (`ILooperBlock`, found by `CompiledGraph.Looper`), the looper commands, telemetry and WAV export go to it; otherwise to the rig looper. A grid loop belongs to its preset: a swap stops the outgoing graph's looper (it fades out in the spillover), and a preset that brings a Looper block stops the rig looper. The loop memory (up to 300 s) is zeroed lazily by the OS, so only what is recorded is committed.
- **Spillover:** the old graph retires after 4.1 s of silence (longer than any delay repeat), or 12 s. With the rig's `spillover` off (global *Preset spillover*) it fades out over the 20 ms swap instead, a clean cut. A grid loop is handed over to the next preset's Looper block when it has one (`ILooperBlock.TakeLoop`: the two blocks swap loopers, no allocation).
- **Scenes and Scene Ignore** (server): the live state of a block is the active scene's, unless its channel has Scene Ignore on, in which case a scene change leaves it held (`LiveState.held`). Manual changes write the active scene and release the hold. Scene Revert recalls a scene from the last-saved copy on selection; a preset loads on its saved scene or the global default scene.
- **MIDI** is read by the host (CoreMIDI), stamped with engine sample time, and routed by the Server's `MidiRouter`. The router owns learn, mappings and footswitches, and drives LEDs.

### 5.6 Audio I/O ⚠

`IAudioBackend` delivers `float**` planar buffers in a callback that the native driver thread owns. There is no intermediate queue, because a queue would add a buffer of latency.

| Platform | Primary backend | Fallback |
|---|---|---|
| Windows | **ASIO**. The SDK is dual-licensed proprietary/GPLv3 since Oct 2025, so a closed-source product signs Steinberg's free proprietary agreement | WASAPI exclusive |
| macOS | **CoreAudio AUHAL** with audio-workgroup join | — |
| Linux | **JACK / PipeWire-JACK** (sample-accurate MIDI too) | ALSA |
| CI/tests | **OfflineBackend** (render as fast as possible from WAV) and **NullBackend** (real-time-paced timer) | — |

These sit in one C ABI shim (`native/cth_audio`). It can be built on RtAudio, which covers all of the above, plus custom CoreAudio code for workgroups. Managed code sees one `delegate* unmanaged` callback.

**As built (audio I/O, [08 §2.1](08-blocks-parity.md#21-audio-io-wiki-audio-io-and-amplification)).**
- The shim opens every output channel of the device. The backend owns an `OutputBus` (Cathode.Engine): which device pair each of Outputs 1–4 plays on (the host's `--outputs 1,3,5,7`, first channel of each pair, 0 for none; the default is Output 1 on 1–2) and the DI out's channels (`--di-out`). It hands the bus to the engine in `AudioIO.Outputs`, then copies Outputs 2–4 and the DI out from it to their channels. The `NullBackend` and the offline renderer (`OfflineRenderer.RenderOutputs`) take a bus too, so the multi-output path is tested without a device.
- Per callback the engine copies the raw input to the DI out, then per sub-block applies **Input 1 Gain** (rig `in1Gain`) to a copy of the input the graph reads. After the graph, the spillover tail, the scene level and the looper, it copies Output 1 to Output 2 when **Copy Output 1 to Output 2** is on and the graph has no Output 2, then gives each output its **mode** (Stereo, Sum L+R, Copy L→R), its own **global EQ** (`GraphicEqBlock`s, IPC blocks `global`, `global2`..`global4`) and its **Boost/Pad**; the metronome is added to Output 1. Only outputs that are mapped and fed are rendered. The engine keeps each output's peak for telemetry (`HostTelemetry.OutputPeaks`).
- Input 1 Gain, the calibration, the input clip warning and the DI out act on Input 1, the main pair the `InputMap` delivers as `AudioIO.InputLeft`/`InputRight`. The other hardware sources reach blocks through the `InputPort` untrimmed; the calibration reaching `BlockContext` is Input 1's. The global settings keep `inputs` as a list, so per-source values can follow.

### 5.7 Neural and native DSP ⚠

- **NAM** (`.nam` A1 and A2) runs through `native/cth_nam`, an `extern "C"` wrapper around NeuralAmpModelerCore with calls such as `nam_load`, `nam_process`, `nam_reset` and `nam_free`. Hosting the reference core gives exact compatibility with the 700k+ models on Tone3000, and new architectures for free.
- **RTNeural** covers AIDA-X and Proteus LSTM/GRU models.
- **ONNX Runtime is not used on the audio path.** Its per-call overhead, allocations and lack of streaming state make it unsuitable.
- Long native calls use normal P/Invoke, **never** `SuppressGCTransition`, so a GC can still proceed while the native code runs.

### 5.8 Amp modelling strategy (the hard part)

Three tiers ship together, in one Amp block:

1. **Capture amps (NAM / RTNeural)**: user-loaded or bundled, snapshot accuracy. These give fast time-to-value.
2. **Cathode "Circuit" amps**: our own white- and grey-box models. This is the long-term differentiator and the closest analogue to Fractal's Cygnus approach:
   - preamp triode stages via WDF or DK-method with ADAA plus 2–8× IIR polyphase oversampling *only around the nonlinearities*
   - an exact passive tone-stack model (FMV, Vox, Marshall, …)
   - phase inverter
   - push-pull power amp with sag/supply model, output transformer, negative feedback (presence/depth) and a speaker impedance curve
   - full "expert" parameters (bias, B+ sag, transformer match, tube types)

   The stages are composable, so new amp types are mostly configuration plus a measured tone-stack topology.
3. **Hybrid**: a neural preamp capture with a parametric power amp and cab. This keeps master/presence/depth knobs "alive" on captured tones.

The **Cab** block covers:
- IR convolution: uniform partitioned at the buffer size, and Gardner-style zero-latency non-uniform partitioning for larger internal block sizes
- up to 4 IR slots with mic blend, alignment, minimum-phase trim, and proximity, room and air controls
- an IR library with Cab-Lab–style mixing and export

### 5.9 Telemetry

The audio thread writes to a **shared-memory region** owned by Host and read by Server. It uses seqlocks and SPSC rings and never blocks. Contents:

- input and output peak/RMS
- per-block level and CPU (sampled timestamps)
- scene and channel state
- tuner sample ring
- RTA/scope decimated ring
- xrun counter
- callback duration histogram

Server fans these out over WebSocket at **30–60 Hz** as binary `Float32Array` frames, plus JSON events for state changes.

**As built (layout v1, `Cathode.Engine.Telemetry`).** The region is a memory-mapped file that the host creates next to its socket and names in the `hello` reply. The Server maps it read-only. One seqlock covers the whole region, so the writer never waits and readers retry.

Version 1 holds:
- the callback count and a frame clock
- the cumulative callback-duration histogram (1 % bins), from which readers derive windowed p99.9 and max by diffing snapshots
- engine overruns, and driver overloads (published by the host's control thread)
- meter peaks in 64 slots of 256 frames, each tagged with its absolute slot number

Every reader takes the maximum over the slots since its own previous poll. Nothing is ever reset, so any number of readers see every peak once.

The Server publishes meters at 30 Hz, still as JSON. IPC telemetry, which carries the tuner, GC counts and device timing windows, dropped to 10 Hz.

Version 3 adds an output scope ring of 16,384 samples, outside the seqlock (the reader checks that the writer did not lap it). The Server reads it for three things:
- scope frames, while the scope is on
- RTA frames: 3, 6 or 12 bands per octave of a 4096-point window (Hann, Blackman-Harris, flat top or rectangular) at 15 Hz, with an optional decay, while the RTA is on
- Tone Match's long-term average spectrum

Version 4 adds a second ring of the same size for the RTA block: while it keeps growing, RTA frames come from it (`source: block`) instead of the output.

Version 5 adds, per meter slot, each grid block's output peak (by graph slot, up to 128, tagged with the graph generation) and the output's ITU-R BS.1770 loudness (momentary and short-term, `LoudnessMeter`). The host's `loadGraph` reply lists the slot ids, so the Server publishes the peaks by block id (`Telemetry.blocks`, shunts left out) with `loudnessMomentary` and `loudnessShortTerm`.
Version 6 makes that section every block meter, one path for all of them. Each graph slot has meters: first `peak` (its output peak, linear), then the block's own (`IBlockMeters`, dB). The compiled graph lays them out slot by slot (`CompiledGraph.Meters`, `MeterIds(slot)`, `MeterOffset(slot)`); the engine writes the peaks while it runs the blocks and reads the own meters after each callback, and the region keeps the highest value of each per meter slot (`BlockMeter`, up to 256 values per slot, `BlockMeterCount` of them, tagged with the graph generation). A reader takes the highest over the slots since its previous poll (`TelemetryMonitor.BlockMeters`), so every meter means "the highest since you last looked". The `loadGraph` reply lists each slot's meter ids (`SlotMeters`) beside the slot ids, and the Server maps the values to ids once, in one section of the `telemetry` message: `blocks: { blockId: { peak, preampIn, motorReduction, ... } }` (shunts left out). The IPC telemetry no longer carries block meters (v5's `blocks` list of own meters is gone, and with it the Server's `blockMeters`).
Version 7 adds each modifier's current value, so the editor's knobs follow their modifiers as Axe-Edit's do. The compiled graph keeps the value of each modifier in its parameter's units (`CompiledGraph.ModifierValues`, NaN for a disabled or engage modifier), and the region holds the latest of them (not a peak: `Modifier`, up to 256, `ModifierCount` of them, tagged with the graph generation; `TelemetryMonitor.ModifierValues`). The `loadGraph` reply names the block and parameter of each modifier (`ModifierTargets`), and the Server sends the finite values of the graph whose modifiers it holds as `modulation: { blockId: { paramId: value } }` in the `telemetry` message, left out when nothing is modulated. In the editor, `EventStream.modulation` is a signal (unlike the rest of telemetry): a control's `computed` over it marks only its own view when its value moved. A knob given a `modulated` value turns its pointer and arc to it, gliding between frames with a short CSS transition, reads it out, and marks the value set with a dot on the track; while the user drags or types, and briefly after a key or wheel step, it shows the value being set. Switches and choices show the modulated state too, and so does the Perform page.

Each output's peak (after its EQ and Boost/Pad) goes over IPC telemetry (`outputPeaks`, 10 Hz) and on to WebSocket clients in the telemetry frames, for the editor's output clip warning at −1 dBFS; the input clip warning (0.5 dB below full scale) reads the existing input peak.

Still to come: RMS, per-block CPU, scene state, and binary frames.

### 5.10 MIDI and controllers

- MIDI is read in the native shim (RtMidi, or JACK MIDI on Linux). Events are timestamped, then converted to **sample offsets** and pushed into the command ring. That gives sample-accurate tap tempo and CC ramps.
- Mappings cover preset and scene change, block bypass and channel, modifier sources, tap tempo, tuner and looper. They live in global settings, and presets can override them.
- Foot-controller support (generic MIDI controllers in v1) uses an Axe-Fx-like **layout/view model**: layouts, per-switch tap/hold functions, LED colours sent via MIDI where the controller supports it.

---

## 6. REST API (Cathode.Server)

**Conventions:**
- Base path `/api/v1`.
- JSON via System.Text.Json source generation.
- **OpenAPI is generated and committed.**
- Resources carry a `revision` (ETag) for optimistic concurrency; `If-Match` is required on mutations.
- Errors are RFC 9457 problem+json.
- Long operations (IR or model import, Tone Match) return `202` plus `/jobs/{id}`.
- **Auth:** the server binds to localhost by default. LAN access is opt-in, protected by a pairing token, so phones and tablets can act as remote editors.

| Area | Endpoints (abridged) |
|---|---|
| System | `GET /system` (version, CPU, xruns), `GET/PUT /system/audio` (device, sample rate, buffer, channel routing), `GET /system/audio/devices`, `POST /system/audio/latency-test` |
| Catalog | `GET /catalog/blocks`, `GET /catalog/blocks/{typeId}` (descriptors above), `GET /catalog/amps`, `GET /catalog/drives` … |
| Live state | `GET /live` (the full working copy), `PUT /live/preset/{presetId}` (load), `POST /live/save`, `POST /live/undo`, `POST /live/redo` |
| Grid | `GET/PUT /live/grid`, `POST /live/grid/cells/{r}/{c}` (place block), `DELETE …`, `POST /live/grid/cables` |
| Blocks | `GET /live/blocks/{iid}`, `PATCH /live/blocks/{iid}/channels/{ch}/params` (batch), `PUT /live/blocks/{iid}/bypass`, `PUT /live/blocks/{iid}/channel`, `POST /live/blocks/{iid}/copy-channel` |
| Scenes | `GET /live/scenes`, `PUT /live/scenes/active` (select), `PATCH /live/scenes/{n}`, `PUT /live/scenes/{n}/blocks/{iid}` (Scene Manager), `POST /live/scenes/copy`, `POST /live/scenes/swap`, `PUT /live/blocks/{iid}/channels/{ch}/scene-ignore`, `POST /live/blocks/{iid}/channels/swap`, `POST …/channels/{ch}/reset`, `POST /live/blocks/{iid}/reset` |
| Modifiers | `GET/PUT /live/controllers`, `PUT /live/blocks/{iid}/params/{pid}/modifier` |
| Library | `GET/POST /presets`, `GET/PUT/DELETE /presets/{id}`, `POST /presets/import`, `GET /presets/{id}/export`, `GET /presets/list.txt`, setlists, banks |
| Global | `GET/PUT /global/settings`, `/global/blocks` (global blocks), `/global/eq`, `/global/midi-mappings`, `/global/footswitch-layouts` |
| Assets | `/irs` (upload, list, tags, mix/export), `/models` (NAM/AIDA-X upload, metadata, loudness normalisation) |
| Utilities | `/tuner` (settings), `/looper` (transport, undo, export), `/metronome`, `/tone-match` (jobs) |
| Stream | `GET /ws`: binary telemetry channels (meters, tuner, RTA, scope, CPU) plus JSON events (`paramChanged`, `sceneChanged`, `presetLoaded`, `xrun`), with subscriptions per channel |

> **As built (Phase 3):** the domain lives in the Server ([ADR-0004](adr/0004-state-ownership.md)). Some routes differ from the sketch above: `PUT /live/blocks/{id}/position` moves a block, `DELETE /live/cables?from=&to=` removes a cable, and parameters are `PATCH /live/blocks/{id}/params?channel=B` with a `{ paramId: value }` body. `/ws` sends JSON text, starting with `{"type":"connected"}`. A client waits for it before reading `GET /live`. The tuner, scope and RTA (`PUT /tuner`, `/scope`, `/rta`) are rig-wide but counted per client: an editor sends its own `?client=` id on those requests and on `/ws`, each runs while any client has it on, and what a client left on is released when its last socket closes (it turns it on again after a reconnect). The committed `engine/src/Cathode.Server/openapi.json` is the source of truth.

> **Audio I/O:** `GET /system/latency` reports the device's input and output latency, the buffer, and the graph's latency along its longest path, block by block. `PUT /live/blocks/{id}/test-tone` (`{ enabled, frequency, level }`) plays the cab resonance helper's sine in place of a block's output, and `GET /live/blocks/{id}/speaker-resonance` gives a Circuit Amp's speaker load resonance at Load resonance 100 % (with Follow Cab, the followed Cab speaker's). The global settings gained `outputs` (mode, Boost/Pad and EQ per output), `copyOutput1To2`, `inputs` (full-scale volts and gain per input) and `oversampling`; the device channel mapping stays with the desktop app's audio settings (host arguments).

Every mutation produces an **event** on `/ws`, whatever its source (REST, MIDI or footswitch). That keeps multiple editors in sync, and makes MIDI-driven changes visible in the UI as they are on Axe-Edit.

---

## 7. Editor (Electron + Angular 22 + Tailwind 4 + xUI)

### 7.1 Shell
- **Electron main:**
  - spawns and supervises `cathode-host` and `cathode-server` (sidecars bundled per OS), with health checks and restart
  - can instead connect to a remote server (headless rig) by URL and token
  - provides native file dialogs, auto-update and crash reporting
  - has contextIsolation on and nodeIntegration off; a minimal preload exposes `window.cathode.{pickFile, revealPath, version}`
- **Renderer:** an Angular 22 standalone, zoneless, signals-based app in an Nx workspace (the same tooling as xUI, so the libs can be consumed as source during development).

### 7.2 State management
- **Server state** is mirrored into **signal stores** per feature: `LiveStore`, `CatalogStore`, `LibraryStore`. They are hydrated from REST and patched by `/ws` events.
- **Optimistic UI for knobs:** the drag updates a local signal immediately, then a throttled PATCH (about 30 Hz) is sent with a trailing final value. The server echo is reconciled by `revision`.
- **Telemetry** (meters, tuner, RTA) **bypasses Angular change detection.** It goes straight into canvas renderers through `requestAnimationFrame` loops, which is important for performance.

### 7.3 xUI usage map

| Editor feature | xUI packages | Custom (new) |
|---|---|---|
| App layout: grid top, block editor bottom, library side | `@xui/dock-manager`, `@xui/splitter`, `@xui/navbar`, `@xui/menubar` | — |
| Grid editor | `@xui/context-menu`, `@xui/omnibar` (block picker, "add block ⌘K"), `@xui/tooltip`, CDK drag-drop | `cth-grid` (CSS grid plus an SVG cable layer). Evaluate `@xui/node-graph` for the cable routing |
| Block editor pages | `@xui/tabs`, `@xui/segmented-control`, `@xui/select`, `@xui/numeric-input`, `@xui/slider`, `@xui/switch`, `@xui/control-group` | **`cth-knob`** (rotary, fine-drag with ⇧, double-click to type, ARIA slider) |
| Scenes and channels | `@xui/button-group`, `@xui/segmented-control`, `@xui/editable-text` | — |
| Meters, tuner, RTA, EQ curves | `@xui/echarts` for static curves (EQ, IR frequency response); `@xui/konva` for interactive EQ node dragging | **`cth-meter`**, **`cth-tuner`**, **`cth-scope`** (Canvas2D/WebGL, rAF) |
| Preset library and setlists | `@xui/data-table`, `@xui/tree`, `@xui/tag-input`, `@xui/omnibar`, `@xui/upload` | — |
| Modifiers | `@xui/drawer`, `@xui/popover`, `@xui/echarts` (curve preview) | — |
| Footswitch layout editor | `@xui/card`, CDK drag-drop | `cth-footswitch` |
| Feedback | `@xui/toast`, `@xui/alert-dialog`, `@xui/progress-bar`, `@xui/non-ideal-state` | — |

xUI currently has **no knob, meter or VU component**. Build them in `editor/libs/audio-ui` against xUI's token layer (`bg-surface`, `--primary`, `--control-height-md`) and CVA variants, then **upstream them to xUI** as `@xui/knob` and `@xui/meter` once they're stable.

### 7.4 UX parity checklist (from Axe-Edit III)
- grid view with drag-to-move, shift-drag-to-copy, and right-click insert, replace and delete
- a block editor with channel A–D tabs, a **Type** selector, pages, and an "expert" toggle
- a scene bar (8), scene rename, and scene-level block state overview
- a modifier editor reached by right-clicking any knob ("Attach modifier…")
- a Cab Manager (IR library, mix, export) and a Preset Manager (drag to reorder, import/export)
- a footswitch layout editor
- a tuner overlay, a looper panel and a metronome
- an **A/B compare** of the working copy against the saved preset, with undo/redo history

### 7.5 Editor workflow (as built)

The editor's software-editor features, built in the ED stream. They follow Fractal's editor in what they do, not in how they look.

- **Shortcuts.** One table in `shell/shortcuts.ts` drives two things: dispatch (`ShortcutService`, through a document `keydown`) and the `?` help overlay.
  - Handlers register per page. The app owns the preset, scene, channel, block, edit and tuner commands. The grid owns the cell commands (Q, ⌘X/C/V/D/U, Delete), because it knows the focused cell; those that change the grid (⌘X/V/D/U, Delete) act only while the grid has the focus.
  - Nothing fires while focus is in a text field. Bare keys (Space, letters, Delete; not ⌘ shortcuts or F-keys) also leave a focused control alone (a button, switch, checkbox, radio, slider, menu item, tab), so Space presses it instead of bypassing the block.
  - ⌥ held, or F1, shows Block Info.
  - A, B, C and D pick the channel; bypass is Space only.
  - A unit test checks that no two bindings collide and that none clashes with Electron's default menu, which is the only menu the desktop app has.
- **Preferences** (`PreferencesStore`) are kept in local storage, per user and per machine: knob mode, wheel, tooltips, meter rate, and the default Circuit Amp view.
  - They reach `cth-knob` and `cth-meter` through audio-ui's `CTH_KNOB_PREFERENCES` and `CTH_METER_PREFERENCES` tokens.
  - Turning tooltips off puts the `cth-no-tooltips` class on the root.
- **Server endpoints.** Everything the editor needs the Server to do lives in `Api/WorkflowApi.cs`, `Rig/LiveRig.Workflow.cs`, `Rig/PresetTools.cs` and `Stores/WorkflowStores.cs`. Live edits are one undo step each.
  - Preset commands: `POST /live/revert`, `POST /live/clear`.
  - Snapshots: `POST /live/snapshots`, `POST /live/snapshots/{id}/load`, `GET`/`DELETE /snapshots`. They are stored in `<data>/snapshots`.
  - Templates: `POST /live/templates`, `POST /live/templates/{id}/new`, `GET`/`DELETE /templates`. They are stored in `<data>/templates`.
  - Grid operations:
    - `PUT /live/blocks/{id}/type` replaces a block and keeps its cables.
    - `POST /live/blocks/{id}/drop` takes `swap` (the cables stay in place) or `replace`.
    - `POST /live/grid/move` moves a row or column, shifting those between. A row keeps every cable. Cables only join adjacent columns, so a column move rewires and the signal paths stay whole:
      - each block of the moved column that sat in a path hands its inputs straight to its outputs, so the path closes behind it;
      - every cable that then jumps over the column where it lands runs through it: through the moved block in its row when that block can carry a path (a shunt, or a block that was in a path), else through an idle moved block that was in a path, else through a new shunt in an empty cell;
      - a moved block that was in a path but gets none goes in parallel with the nearest block of its column that did, and a moved branch end (an Output) is fed from its new row neighbour.
    - `DELETE /live/blocks/{id}/cables` disconnects a block.
    - `PUT /live/blocks/{id}/settings` pastes settings onto a block of the same type, all channels.
    - `PUT /live/blocks/{id}/all-scenes` sets a block's state in every scene.
  - Performance pages and footswitch overrides: `PUT /live/perform`, `PUT /live/footswitch-overrides`.
  - Block Library:
    - `POST /live/block-presets` saves every block.
    - `POST /block-presets/export` and `/import` handle `.cthblock` files.
    - `ApplyBlockPresetRequest.AllChannels` recalls a whole-block entry.
  - Metadata and preset tools:
    - `PATCH /presets/{id}/metadata` and `PATCH /assets/{ref}/metadata` set name, colour tag and favourite.
    - `POST /presets/{id}/duplicate`.
    - `POST /presets/reset-amps`.
    - `POST /presets/batch-set`.
- **Reset Amp blocks** goes through each preset's Circuit Amp channels.
  - It clears every parameter in `BlockDescriptor.TypeResets` whose `AmpPanel.Label(id, "")` is empty.
  - That resets the expert settings and the controls the model lacks.
  - It keeps the model's front-panel knobs and switches, plus type, level, balance and input select.
- **Footswitches.**
  - A `FootswitchLayout` can have named `Views`. Each `Footswitch` sits on one `View` and may `Link` to another layout, which takes over after its tap action.
  - The new actions are `layout`, `view`, `viewNext` and `viewPrevious`. `GlobalSettings.ActiveView` holds the page in use.
  - In the MIDI router, `Preset.FootswitchOverrides` replace a switch's tap and hold while the preset is loaded. LEDs follow the active view and the overrides.
- **The factory bank** (`Stores/FactoryPresets.cs`, recipes in `Stores/FactoryRecipes.*.cs`): 70 full presets of well-known tones, built into the Server on first use and read-only, like the factory Block Library.
  - `PresetStore` lists the user's presets, then the factory ones (`factory: true`, ids `factory-…`); `Library` puts the factory bank (id `factory`) first among the banks. They load, export and go into user banks and setlists like any preset.
  - Saving, overwriting, renaming or deleting one is refused (409); Save on a factory working copy stores the user's own copy under a new id (the editor opens Save as). Colour and favourite are kept beside them (`<data>/factory-marks`).
  - Each recipe (`FactoryRecipe`) is the main row's blocks with their channels and the scenes as changes. Their amp channel and scene levels come from Preset Leveling's measurement of a test phrase (`FactoryLevels.Table.cs`, generated by `FactoryPresetLevelTests` with `CATHODE_UPDATE_FACTORY_LEVELS=1`), so the bank plays at −18 LUFS. Every preset stays under 80 % estimated CPU.
- **Manage Presets** (`pages/manage.ts`) holds bank edits, copies, renames, metadata and deletions in a `ManageSession` until Save.
  - Save applies them in an order the Server accepts: copies, metadata, banks, then deletions.
  - **Manage IRs** works the same way through an `AssetSession`.

---

## 8. Security and IP
- Server binds `127.0.0.1` by default. LAN mode requires a pairing token. Uploaded files are validated: IR WAV headers and length caps; NAM JSON schema and weight-count sanity checks.
- **No Fractal IP:**
  - no copying of IRs, presets, manuals or UI artwork
  - amp and drive model naming follows the industry practice of descriptive names plus a disclaimer
  - factory IRs and captures must be **our own recordings**, or properly licensed
- Licences:
  - **ASIO**: Steinberg's proprietary agreement, or GPLv3 if the whole product is GPL
  - **NAM core**: MIT
  - **RTNeural**: BSD-3
  - **JUCE**: not used, to avoid GPL or commercial licensing
  - Study the GPL code in chowdsp and other projects for ideas only, unless the licence fits.

---

## 9. Optional deployment targets (later)
- **Plugin (VST3/AU/CLAP):** compile `Cathode.Engine` + `Cathode.Dsp` as a **NativeAOT shared library** with a C ABI. A thin CLAP shell plus `clap-wrapper` produces VST3 and AUv2. NPlug, a pure-.NET VST3 route, is an alternative. The editor talks to the plugin through the same REST API on an ephemeral port.
- **Headless appliance:** Linux mini-PC with PipeWire-JACK, Host + Server as systemd services, and the editor on a laptop or phone over LAN.
