# 04 — Implementation plan

The plan is organised as phases with exit criteria. Each phase ends with something that is **playable and measured**. Durations assume **2–3 engineers**: one strong in DSP, one in C# and systems, and one in front end. With a single developer, multiply the durations by roughly 2.5. The overall order is: **de-risk first, then ship a playable minimum, then broaden the catalogue, then deepen the modelling.**

```
P0 Feasibility spikes ─► P1 Engine core ─► P2 MVP blocks ─► P3 API + presets ─► P4 Editor MVP
   (3–4 wk)               (4–6 wk)          (6–8 wk)          (4–5 wk, ∥ P2)     (6–8 wk, ∥ P3)
                                                                        │
           ┌────────────────────────────────────────────────────────────┘
           ▼
P5 Scenes/modifiers/MIDI ─► P6 Catalogue breadth ─► P7 Circuit amp models ─► P8 Polish & ship
   (5–6 wk)                   (10–14 wk, ongoing)     (research track, starts in P2)  (4–6 wk)
                                                       P9 Plugin build (optional)
```

---

## Phase 0: feasibility spikes (3–4 weeks) — *answer the ⚠ items before committing*

Each spike is a throwaway prototype with a written result in `docs/adr/`.

| # | Spike | Pass criterion | If it fails |
|---|---|---|---|
| S1 | ◐ macOS done except the 30 min soak and a physical loopback ([ADR-0002](adr/0002-native-audio-backend.md)). **Native audio shim** (`cth_audio`): ASIO (Windows), CoreAudio HAL IOProc (macOS) and JACK (Linux) calling a managed `delegate* unmanaged` passthrough | 64 samples at 48 kHz, < 6 ms measured loopback round trip, 0 xruns in 30 min on each OS | Use 128-sample buffers as the minimum supported, or use RtAudio as is |
| S2 | ✅ Out-of-process ([ADR-0001](adr/0001-process-model.md)): in-process dropped out even at 100 req/s; out-of-process stayed at baseline at 14 k req/s. **GC under load**: a passthrough plus a 2 ms synthetic DSP load, with the REST server **in the same process** vs **a separate process**, under a load generator | p99.9 callback < 70 % of budget and 0 xruns over 1 h. Pick the process model (ADR-001) | Out-of-process becomes mandatory; tune `GCgen0size` |
| S3 | ◐ macOS verified, Windows/Linux pending CI ([ADR-0003](adr/0003-nam-hosting.md)): A1 at 4.2 %, A2 Full at 4.0 %, A2 Lite at 0.57 % of one core, golden-matched to upstream. **NativeAOT Host**: a P/Invoke round trip into NAM core via `cth_nam`, loading an A1 and an A2 model | Builds on all 3 operating systems. A standard A1 WaveNet uses < 15 % of one core at 48k/64 | Fall back to LSTM models, or a C# port of the WaveNet for A1 only |
| S4 | **C# DSP throughput**: biquad cascade, 4× IIR polyphase oversampler + tanh ADAA, 4096-tap uniform partitioned convolution, all `Vector128` | Within 1.5× of an equivalent C++ (chowdsp/FFTConvolver) build | Move hot kernels to native `cth_dsp` |
| S5 | ◐ FTZ verified by test; tail benchmark pending ([ADR-0002](adr/0002-native-audio-backend.md)). **FTZ/DAZ** from managed code via a native helper; check denormal handling on x64 and ARM64 | No subnormal slowdown in the IIR tail benchmark | Inject ε in all feedback paths |
| S6 | **Electron sidecar**: package Host + Server per OS, spawn them, health check, codesign/notarise on macOS | Signed `.dmg`/`.exe`/`.AppImage` launches and connects | Adjust packaging: electron-builder `extraResources` |
| S7 | **Licensing**: GPL-3.0-or-later chosen; write a licence inventory (ASIO SDK under GPLv3, NAM core MIT, RTNeural BSD-3, FFTConvolver MIT) | Done | — |

**Exit:** ADRs for the process model (ADR-0001 ✅), audio backend (ADR-0002, macOS), native/managed split (ADR-0003 ✅), and minimum buffer size.

---

## Phase 1: engine core (4–6 weeks) — *in progress*

- [x] Solution scaffold per [02 §3](02-architecture.md#3-repository-layout); CI on Windows, macOS and Linux (GitHub Actions); `Directory.Build.props` with analyzers and `TreatWarningsAsErrors`
- [ ] Audio backends: **Offline** ✅, **Null** ✅, **Native** ✅ macOS CoreAudio (Windows and Linux still stubs)
- [x] `Cathode.Testing` analysis toolkit ([03 §2](03-testing-strategy.md#2-the-analysis-toolkit-build-this-first)) and the `cathode-render` CLI (the `Cathode.Analyze` CLI is still to do)
- [x] `IDspBlock` contract, `ParamBank` + smoothers, `BlockDescriptor` + catalogue registry
- [x] Grid model → DAG → **graph compiler**: topological sort, buffer colouring, implicit mixers, latency compensation
- [x] SPSC command ring, RCU graph swap, retired-graph hand-back
- [x] Telemetry shared-memory region (`TelemetryRegion`, [02 §5.9](02-architecture.md#59-telemetry)). The host maps it to a file, and the Server reads it at 30 Hz for WebSocket meters.
  - It holds meter peaks in 64 frame-tagged slots that readers never reset, the cumulative CPU histogram (readers take windowed p99.9 and max), engine overruns and driver overloads.
  - Writes are protected by a seqlock, and the audio-thread write is allocation-free.
  - IPC telemetry dropped from 30 to 10 Hz.
  - RMS, per-block CPU and the scope/RTA rings come with the editor (Phase 4).
- [x] Generated per-block test suite (null, determinism, properties, zero-allocation)
- [x] Blocks: **Input**, **Output**, **Shunt**, implicit mixing, **Volume/Pan**, **Filter** (TPT SVF). An explicit Mixer block is still to do

**Exit:** a JSON graph file can be rendered offline. A live passthrough with a gain block runs at 64 samples with no xruns, and the CI suites are green.

---

## Phase 2: MVP block set (6–8 weeks): *"it sounds like a real rig"* — *done except the playability tests*

Ordered by value to a guitarist:

1. [x] **Amp (capture tier)**: `CaptureAmpBlock` with input trim, loudness normalisation, output level, and a post-EQ:
   - bass: low shelf at 120 Hz
   - middle: peak at 650 Hz
   - treble: high shelf at 2.5 kHz
   - presence: peak at 5 kHz

   Models:
   - **NAM** A1/A2 run natively (`NamAmpBlock`, ADR-0003).
   - **AIDA-X** (RTNeural Keras JSON, `.aidax`/`.json`) runs in managed code (`AidaModel`): stacked LSTM or GRU plus a dense head, with `in_skip`, `in_gain` and `out_gain` mapped to loudness the same way as NeuralAudio.
     - It matches a naive reference evaluation to 1e-4.
     - Its weights live in native memory, and processing allocates nothing.
     - Cost: LSTM-16 1.8 %, LSTM-40 7.6 % of one core (JIT, M-series).

   *Not supported:* conditioned (multi-input) AIDA-X models, and resampling models trained at another rate.
2. [x] **Cab**: zero-latency two-stage convolution (direct head + uniform partitioned tail, written in C#), 2 IR slots with levels, low/high cut, WAV IRs at any rate (resampled), stereo IRs, unit-energy normalisation. *Still to do:* IR alignment, min-phase trim
3. [x] **Gate/Expander** block (dB-domain smoothing, hold, linked detector). *Still to do:* the gate built into the Input block, sidechain filters
4. [x] **Drive**: Overdrive / Distortion / Fuzz voicings, ADAA clippers inside 4× IIR half-band oversampling (signal-to-aliasing 63–73 dB at full drive, 2.5 kHz). *Still to do:* WDF circuit models (TS, Rat, Klon)
5. [x] **Delay**: digital, analog (dark, saturating repeats), tape (wow/flutter, saturation); tempo sync; Hermite interpolation; spillover-ready
6. [x] **Reverb** types:
   - Room, Hall and Plate: an 8-line FDN.
   - **Dattorro Plate**: the 1997 figure-eight tank, with modulated allpasses, decay derived from T60, and the paper's output taps.
   - **Spring**: two springs, each a chain of 80 stretched first-order allpasses inside a feedback loop (Välimäki, Parker and Abel 2010), so a click turns into a falling chirp. 300 Hz arrives more than 10 ms after 3 kHz.

   Every type's measured T60 is within 20 % of the setting. Switching type starts the new tank empty. *Not modelled:* the spring's separate high-chirp branch, and a spring drive/tension control.
7. [x] **Compressor** (Pedal/Studio/Opto, soft knee, parallel mix), **PEQ** (5-band SVF), **GEQ** (10-band octave)
8. [x] **Chorus**, **Flanger**, **Phaser**, **Tremolo/Panner** (shared LFO and Hermite delay line)
9. [x] **Wah** (Classic/Vox/Wide resonant sweep). *Still to do:* a component-level inductor model; driving it from an expression pedal comes with modifiers in Phase 5
10. [x] **Tuner** (MPM via FFT NSDF on the analysis thread, fed by `CathodeEngine.InputTap`; within 0.01 cent from 41 to 440 Hz) and **CPU meter** (callback stats). *Still to do:* strobe display and mute modes belong to the editor and API

**Exit:**
- ✅ A "high-gain NAM → gate → cab → stereo delay → hall" preset (`engine/examples/high-gain.json`) runs at **7.7 %** of one core. A rig using every Phase 2 block, with two amps (`everything.json`), runs at **15.4 %**.
- ✅ Live on a Mackie ProFX at 32 frames for 20 s: worst callback 16.5 % and 31 % of budget, with no driver overloads.
- ✅ The blocks pass their golden, analytic and aliasing tests.
- [ ] Blind playability tests by two guitarists (feel and latency). These need people, not CI.

---

## Phase 3: REST API and preset system (4–5 weeks, in parallel with late P2) — *done except the 1-hour soak*

- [x] `Cathode.Contracts`: preset schema v1 (A–D channels stored sparsely, 8 scenes), block catalogue DTOs, problem+json errors, `revision`/ETag with `If-Match`
- [x] `Cathode.Server`: endpoints per [02 §6](02-architecture.md#6-rest-api-cathodeserver); OpenAPI committed (`engine/src/Cathode.Server/openapi.json`) with a drift test; `cathode.http` walk-through
- [x] IPC transport per [ADR-0001](adr/0001-process-model.md) and [ADR-0004](adr/0004-state-ownership.md): Unix domain socket, `cathode-host --serve`; two-process test including a host restart. *Partly done:* the full API suite runs in-process; only the two-process test runs over IPC
- [x] Preset library:
  - JSON per preset (atomic writes, safe ids)
  - **banks** (128 slots, for MIDI program change) and **setlists** (preset plus starting scene), with referential integrity
  - **`.cthpreset`** import/export: a zip of the preset and its assets, with hardened import (strict names, hash-verified assets, size limits enforced while reading, no overwrites)
- [x] Undo/redo (snapshots of the working copy; knob drags coalesce into one step)
- [x] `/ws` events and telemetry as JSON text, with a `connected` handshake so clients never miss a change. *Changed:* binary frames and per-channel subscriptions are deferred (ADR-0004)
- [x] Asset store for IRs and models: content-addressed (SHA-256), deduplicated, size-limited
- [ ] 1-hour soak with the real two-process setup ([03 §6](03-testing-strategy.md#6-real-time-safety)):
  - The harness is ready: `engine/tools/Cathode.Soak` plus `run-soak.sh`, with telemetry for driver overloads, native callback timing and host GC counts.
  - A 2-minute dry run passed: 0 overloads, 0 errors.
  - It also found **host gen2 GCs on preset loads stalling the callback for 56–69 % of the budget** (ADR-0001 follow-up).
  - **Fixed:** DSP state moved to native memory, IPC framing reuses its buffers, and the host gets a 256 MB gen0 budget. A 3-minute dry run then had 0 host GCs and a worst callback of 14 %.
  - **1-hour run on GitHub** (nightly `soak.yml`, null audio, 2026-10-23): 111,402 requests and 220,099 WebSocket messages with 0 errors and 0 overloads. **But it had 1 host gen0 GC (1.8 ms pause) at minute 43**, so the zero-GC gate failed. See ADR-0001.

**Exit:**
- ✅ `cathode.http` builds and plays a preset through the API alone.
- [ ] The 1-hour API soak: everything passes except the zero-GC gate (one gen0 collection at minute 43). The fix is to cut the host's remaining allocations (ADR-0001, "1-hour soak").

---

## Phase 4: editor MVP (6–8 weeks, in parallel with P3) — *done except the usability check with a guitarist*

- [x] Nx workspace (`editor/`, pnpm, Nx 24):
  - `apps/desktop` (Electron 45) and `apps/editor` (Angular 22.2, zoneless, signals, Tailwind 4, xUI 3).
  - xUI is consumed as published packages, following its own skill: barrels, variant inputs, semantic tokens only.
- [x] **Generated `api-client`** (`openapi-typescript` + `openapi-fetch`, from the committed `openapi.json`; CI fails on drift).
  - The Server now marks non-nullable properties required and serialises numbers strictly, so the generated types are exact.
  - Signal stores: `LiveStore`, `CatalogStore`, `LibraryStore`, `CompareStore`.
  - WebSocket client with backoff, which resyncs on every reconnect and on any revision gap.
  - Knob drags are coalesced, with one PATCH in flight per block.
- [x] **Electron main:**
  - Spawns cathode-host and cathode-server as supervised sidecars, with restart backoff and a clean stop on quit.
  - Each run gets a random access token, a free loopback port, and the host's GC budget.
  - Remote connect takes a URL plus a token.
  - The window is sandboxed with context isolation. The editor is served from an `app://` scheme with a strict CSP.
  - Navigation and permissions are blocked, and the preload bridge is narrow and validated.
- [x] **Server additions for the editor:**
  - Bearer-token auth: required beyond loopback, with `?access_token=` on `/ws`.
  - CORS: loopback-only without a token, and a WebSocket origin check.
  - `PUT /live`, an undoable whole-preset replace for A/B.
  - `GET /assets/{ref}/content`.
  - `cathode-host --devices --json`.
- [x] **`audio-ui` library:**
  - `cth-knob` is an ARIA slider: drag (⇧ fine), wheel, keys, double-click to type with units, ⌥-click to reset.
  - `cth-meter` is a canvas peak meter with hold and a clip light. `cth-tuner` is a needle tuner. `cth-scope` is an oscilloscope with a rising-edge trigger, fed by a 4096-sample output ring in the telemetry region (v2) and streamed only while open.
  - Telemetry reaches all four on animation frames, never through change detection.
  - Storybook is set up the way xUI's is. Every story renders in Chromium and must pass axe in CI. Chromatic uploads when `CHROMATIC_PROJECT_TOKEN` is set.
- [x] **Grid editor** (6×14, absolutely positioned cells plus an SVG cable layer):
  - The ⌘K block palette (`@xui/omnibar`) opens on an empty cell or onto a shunt.
  - Drag to move, ⌥-drag to copy. Drag from an outlet to the next column to cable.
  - Context menu: bypass, channel, copy/paste, remove. Keyboard: arrows, Enter, Delete, B, ⌘C/⌘V.
  - It is an ARIA grid with descriptive cell names.
- [x] **Block editor:** pages generated from the descriptors (knob, choice, switch), channel tabs A–D, the Type selector first, and bypass.
- [x] **Custom panels:**
  - Amp: capture picker and upload (NAM/AIDA-X).
  - Cab: IR slots and a frequency-response plot via `@xui/echarts`. The IR is measured in the browser, with level and cuts applied.
  - PEQ: an interactive curve via `@xui/konva`. Drag sets frequency and gain; the wheel sets Q.
- [x] **Preset library** (`@xui/data-table`, search across name, author and tags): load, export, import, delete. Plus save, save-as, A/B compare, and undo/redo (⌘Z / ⇧⌘Z).
- [x] **Audio setup page:**
  - Device, sample rate, buffer, then apply, which restarts the host.
  - Loopback latency test.
  - CPU and xrun statistics.
  - Server choice: this computer or remote.
  - Channel routing waits for multi-channel I/O in the shim.
- [x] **Playwright `_electron` E2E** (`nx e2e desktop`; CI under xvfb) drives the real app and its sidecars on null audio:
  - Build a preset from scratch: palette, knob, bypass and engage, save as, then library.
  - Undo, redo, and per-scene bypass.
  - The setup page shows the engine that was started.

**Exit:** a non-developer guitarist can install it, pick an interface, build a preset from scratch, and save it, with no docs.

---

## Phase 5: scenes, channels, modifiers, MIDI, utilities (5–6 weeks) — *built; the hardware exit check remains*

- [x] **Scenes (8):** per-scene bypass and channel. Scene level now reaches the engine. Scene controllers 1–4 are modifier sources. Switching scenes never recompiles.
- [x] **Gapless preset changes with spillover:**
  - The replaced graph keeps running on a fading input while the new one fades in over 20 ms.
  - It retires after 4.1 s below −90 dB (longer than any delay repeat) or 12 s.
  - **Bypass modes:** Thru, Mute, Mute In and Mute FX In. The "in" modes give scene spillover.
- [x] **Channels:** continuous parameters glide through their smoothers on a channel switch, and copy channel is in the API and the block menu.
  - *Not done:* a dual-state crossfade for discrete parameter jumps (a Type change still switches at once).
- [x] **Controllers:** LFO ×2 (6 shapes, free or synced), ADSR ×2 (input-gated), a 16-stage sequencer, an envelope follower, a pitch follower (host MPM at 10 Hz), Manual A–D (global), External 1–8 (CC/pedals), and scene controllers 1–4.
  - They are evaluated per sub-block on the audio thread, and synced ones lock to the sample clock.
  - Modifiers map a source through a curve (linear, exp, log, S) into a log-aware min..max, with attack and release damping.
  - Editor: right-click any knob to open the modifier dialog with a curve preview. A Controllers page holds every setting.
- [x] **Tempo:** preset, global or MIDI clock (24 ppqn, averaged in the host). Tap tempo is sample-accurate (MIDI timestamps become engine sample times), and a tapped tempo is taken back into the preset.
- [x] **MIDI:**
  - The native shim reads CoreMIDI on macOS. Windows and Linux are stubs, like their audio backends.
  - Mappings (CC, note or PC) cover scene, preset slot or next/previous, bypass, channel, External, Manual, tap, tuner, metronome and the looper. Actions run strictly in arrival order.
  - MIDI learn works from the Global page or from any knob ("Learn MIDI controller" binds a free External source). There is a live monitor.
- [x] **Footswitch layouts:** tap and hold (500 ms) actions per switch, LED feedback (CC or note, 127/0, sent only on change), and an editor on the Global page.
- [x] **Looper:** stereo, 60 s, at the end of the rig so it survives preset changes. Overdub has a one-pass undo layer, plus reverse, half speed and WAV export. **Metronome** (accented, never recorded). Both are in the toolbar.
- [x] **Global blocks:** a block can be made global, use a global or be unlinked. Linked blocks take the library's settings on load, and editing one updates the library. There is also a **global output EQ** (10 bands plus level).

**Exit:** a live set of 10 presets × 8 scenes runs with a MIDI foot controller, with no gaps or clicks, verified by HIL soak with scene storms.

---

## Phase 6: catalogue breadth (10–14 weeks, then ongoing)

Close the gap with the Axe-Fx III block list in [01](01-axe-fx-iii-audit.md), in roughly this order, each block meeting the [Definition of done](03-testing-strategy.md#12-definition-of-done-any-block-or-feature):

1. Multi-delay (Quad tap, Ten-tap, Plex/diffusion, Ducking, Reverse, Sweep), Megatap
2. Pitch: octaver, whammy (low-latency time-domain), harmonizer (PSOLA), crystals/arpeggiator; virtual capo
3. Filter, Formant, Ring modulator, Enhancer, Crossover, Multiband compressor, Dynamic distortion
4. Rotary (Leslie), Uni-Vibe-style phaser, Synth, Vocoder, Resonator
5. Multiplexer, Feedback send/return, Send/Return to hardware I/O (an external pedal loop with latency compensation)
6. **IR Player**, **Tone Match**, **RTA**
7. Drive catalogue expansion (20+ circuits) on the WDF framework
8. Cab library tooling: IR mixer/export (Cab-Lab-like), and an **IR capture** utility (sweep, deconvolve, trim)
9. **Parallel DSP**: rows on worker threads joined to the audio workgroup or MMCSS, when CPU telemetry shows a need

---

## Phase 7: "Circuit" amp modelling (research track, starts during P2, 6–12+ months)

This is the most important differentiator against "just a NAM host" and the hardest work. Run it as a **parallel research track** with its own milestones:

1. **R1 measurement rig:** reactive load box, re-amp box, reference interface and a calibrated mic setup. Capture our own reference amps (start with 3: Fender blackface, Marshall Plexi/JCM800, a modern high-gain).
2. **R2 stage library (C#):** triode (Koren/Cardarelli models) via WDF with an R-type solver, cathode follower, passive tone stacks (exact), phase inverter (long-tail pair), push-pull pentode power stage, output transformer (hysteresis-lite), power-supply sag (RC network driven by current draw), and a speaker impedance load.
3. **R3 first complete amp** matched to its references: harmonic profile, frequency response per knob setting, ESR/MRSTFT dashboard.
4. **R4 "expert" parameters** (bias, sag, transformer, tube types, negative feedback, presence/depth), mirroring the Axe-Fx III depth the users expect.
5. **R5 hybrid tier:** neural preamp capture with a parametric power amp, so master, presence and depth respond on captures.
6. **R6 scale-out:** additional amp families as configurations of the stage library, about 1 model per 1–2 weeks once the framework is mature.

Gate for each model: ESR/MRSTFT within target across a knob sweep, plus an ABX panel against the reference.

---

## Phase 8: polish and ship (4–6 weeks)

- [ ] Factory content: 50+ presets, factory IRs (our own captures or properly licensed), factory NAM captures (own or licensed)
- [ ] Installers (signed and notarised), auto-update, crash reporting (opt-in), telemetry-free by default
- [ ] User docs: block guide generated from descriptors, plus how-tos
- [ ] Performance pass: CPU estimator in the grid (from benchmark data), low-power mode
- [ ] HIL release suite and latency table published per interface

## Phase 9 (optional): plugin build

NativeAOT shared library with a C ABI → CLAP shell → `clap-wrapper` for VST3/AU. Validate with pluginval (strictness ≥ 5), `auval`, and the Steinberg validator. The editor connects to the plugin through the same REST API.

---

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| GC pauses cause dropouts | Med | **High** | Process split, zero-alloc discipline, native memory, large gen0 budget; S2 spike and nightly soak |
| Driver/interface diversity (Windows especially) | High | High | ASIO first, HIL rig with 3 interfaces, conservative default buffer (128) with "low latency" opt-in |
| Circuit amp quality is below Fractal's | **High** | Med | Ship the capture tier first; treat Circuit amps as a long research track with an objective dashboard; never market parity before ABX proves it |
| NAM architecture churn (A2 and later) | Med | Med | Host the reference C++ core instead of porting it |
| CPU on low-end laptops | Med | Med | CPU estimator, model "lite" variants, oversampling quality setting, parallel rows |
| Scope creep (the Axe-Fx III has ~70 block types) | **High** | Med | Strict phase gates; catalogue breadth is P6, and the MVP needs only ~12 blocks |
| Trademark/IP (Fractal names, IRs) | Low–Med | High | Own captures, descriptive names, no `.syx`, legal review before release |
| Electron + sidecar packaging and signing on macOS | Med | Med | S6 spike early; CI builds signed artifacts from P4 |

## Open questions to settle early
1. ~~Licence model~~ **Decided:** open source, GPL-3.0-or-later. This lets us use the ASIO SDK under its GPLv3 option, and is compatible with NAM core (MIT) and RTNeural (BSD-3).
2. Target platforms for v1: all three, or macOS + Windows first?
3. Sample rate: fixed at 48 kHz internally (NAM-native, simpler) with SRC at the I/O when the device runs at 44.1 or 96 kHz?
4. Which foot controllers to officially support in v1?
5. Are the xUI knob and meter components upstreamed to xUI or kept app-local?

## First two weeks (concrete)
1. Create the `cathode` mono-repo with the layout from [02 §3](02-architecture.md#3-repository-layout); CI matrix; an empty NativeAOT Host that builds on all 3 operating systems.
2. S1: the `cth_audio` CoreAudio and ASIO passthrough, with a loopback latency measurement.
3. S2: the GC-under-load harness, in-process vs out-of-process.
4. S3: `cth_nam` wrapper, loading one A1 and one A2 `.nam` and measuring CPU.
5. Write ADR-001 (process model) and ADR-002 (audio backend).
