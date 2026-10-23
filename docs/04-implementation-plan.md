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
| S1 | **Native audio shim** (`cth_audio`): ASIO (Windows), CoreAudio AUHAL (macOS) and JACK (Linux) calling a managed `delegate* unmanaged` passthrough | 64 samples at 48 kHz, < 6 ms measured loopback round trip, 0 xruns in 30 min on each OS | Use 128-sample buffers as the minimum supported, or use RtAudio as is |
| S2 | **GC under load**: a passthrough plus a 2 ms synthetic DSP load, with the REST server **in the same process** vs **a separate process**, under a load generator | p99.9 callback < 70 % of budget and 0 xruns over 1 h. Pick the process model (ADR-001) | Out-of-process becomes mandatory; tune `GCgen0size` |
| S3 | **NativeAOT Host**: a P/Invoke round trip into NAM core via `cth_nam`, loading an A1 and an A2 model | Builds on all 3 operating systems. A standard A1 WaveNet uses < 15 % of one core at 48k/64 | Fall back to LSTM models, or a C# port of the WaveNet for A1 only |
| S4 | **C# DSP throughput**: biquad cascade, 4× IIR polyphase oversampler + tanh ADAA, 4096-tap uniform partitioned convolution, all `Vector128` | Within 1.5× of an equivalent C++ (chowdsp/FFTConvolver) build | Move hot kernels to native `cth_dsp` |
| S5 | **FTZ/DAZ** from managed code via a native helper; check denormal handling on x64 and ARM64 | No subnormal slowdown in the IIR tail benchmark | Inject ε in all feedback paths |
| S6 | **Electron sidecar**: package Host + Server per OS, spawn them, health check, codesign/notarise on macOS | Signed `.dmg`/`.exe`/`.AppImage` launches and connects | Adjust packaging: electron-builder `extraResources` |
| S7 | **Licensing**: GPL-3.0-or-later chosen; write a licence inventory (ASIO SDK under GPLv3, NAM core MIT, RTNeural BSD-3, FFTConvolver MIT) | Done | — |

**Exit:** ADRs for the process model, audio backend, native/managed split, and minimum buffer size.

---

## Phase 1: engine core (4–6 weeks) — *in progress*

- [x] Solution scaffold per [02 §3](02-architecture.md#3-repository-layout); CI on Windows, macOS and Linux (GitHub Actions); `Directory.Build.props` with analyzers and `TreatWarningsAsErrors`
- [ ] `IAudioBackend` with **OfflineBackend** ✅, **NullBackend** ✅ and **NativeBackend** (from S1)
- [x] `Cathode.Testing` analysis toolkit ([03 §2](03-testing-strategy.md#2-the-analysis-toolkit-build-this-first)) and the `cathode-render` CLI (the `Cathode.Analyze` CLI is still to do)
- [x] `IDspBlock` contract, `ParamBank` + smoothers, `BlockDescriptor` + catalogue registry
- [x] Grid model → DAG → **graph compiler**: topological sort, buffer colouring, implicit mixers, latency compensation
- [x] SPSC command ring, RCU graph swap, retired-graph hand-back
- [ ] Telemetry shared-memory region (meters, CPU histogram ✅ in-process, xruns)
- [x] Generated per-block test suite (null, determinism, properties, zero-allocation)
- [x] Blocks: **Input**, **Output**, **Shunt**, implicit mixing, **Volume/Pan**, **Filter** (TPT SVF). An explicit Mixer block is still to do

**Exit:** a JSON graph file can be rendered offline. A live passthrough with a gain block runs at 64 samples with no xruns, and the CI suites are green.

---

## Phase 2: MVP block set (6–8 weeks): *"it sounds like a real rig"*

Ordered by value to a guitarist:

1. [ ] **Amp (capture tier)**: NAM A1/A2 + AIDA-X via native, input trim, loudness normalisation, output level, simple post-EQ (bass, mid, treble, presence as shelving/peaking filters on the capture)
2. [ ] **Cab**: uniform-partitioned convolution (port FFTConvolver), 2 IR slots with blend, alignment, min-phase trim and low/high cut; IR import (WAV 44.1/48/96k, resampled at import)
3. [ ] **Noise Gate / Input gate** (with sidechain from the input, like Fractal's Input block gate)
4. [ ] **Drive**: three WDF circuits (TS-style, Rat-style, Klon-style) with ADAA + 4× oversampling
5. [ ] **Delay**: digital, analog (BBD-style filtered), tape (wow/flutter, saturation); tempo sync; spillover-ready
6. [ ] **Reverb**: FDN hall and room + Dattorro plate + spring (dispersion allpass chain)
7. [ ] **Compressor** (feed-forward VCA and opto-style), **PEQ** (5-band TPT), **GEQ** (10-band)
8. [ ] **Chorus**, **Flanger**, **Phaser**, **Tremolo/Panner** (shared modulated-delay and LFO core)
9. [ ] **Wah** (inductor-wah model, expression-driven)
10. [ ] **Tuner** (MPM, analysis thread) and **CPU meter**

**Exit:**
- a "Plexi crunch → cab → delay → plate" and a "high-gain NAM → gate → cab → stereo delay → hall" preset each run under 25 % of one core
- they pass the golden and aliasing tests
- they are blind-tested by two guitarists for playability (feel/latency)

---

## Phase 3: REST API and preset system (4–5 weeks, in parallel with late P2)

- [ ] `Cathode.Contracts`: preset schema v1, block catalogue DTOs, problem+json errors, `revision`/ETag
- [ ] `Cathode.Server`: endpoints per [02 §6](02-architecture.md#6-rest-api-cathodeserver); OpenAPI generation committed with a drift test
- [ ] IPC transport (`IpcEngineControl`) per the ADR-001 decision; shared contract test suite for in-process and IPC
- [ ] Preset library on disk (JSON per preset plus an index), banks and setlists, import/export (`.cthpreset` = zip of JSON plus referenced IR/NAM hashes)
- [ ] Undo/redo journal (command pattern on live-state mutations)
- [ ] `/ws` telemetry (binary frames) and events (JSON), with subscriptions
- [ ] Asset store for IRs and models: content-addressed (SHA-256) and deduplicated, so presets reference assets by hash
- [ ] Load generator for the soak test in [03 §6](03-testing-strategy.md#6-real-time-safety)

**Exit:** Postman/HTTP-file and Bruno collections build and play a full preset through the API alone, and the 1-hour API soak passes with 0 xruns.

---

## Phase 4: editor MVP (6–8 weeks, in parallel with P3)

- [ ] Nx workspace: `apps/desktop` (Electron) and `apps/editor` (Angular 22, zoneless, Tailwind 4, xUI); `ng add @xui/core`
- [ ] Generated `api-client` and signal stores (`LiveStore`, `CatalogStore`, `LibraryStore`); WS client with resync
- [ ] Electron main: sidecar spawn and supervise; remote connect (URL + token); secure preload
- [ ] **`audio-ui` library**: `cth-knob`, `cth-meter`, `cth-tuner`, `cth-scope`, each with Storybook and Chromatic stories and a11y
- [ ] **Grid editor**: 6×14 CSS grid, block palette (omnibar ⌘K), drag/move/copy, cables (SVG), shunts, context menu
- [ ] **Block editor**: generic descriptor-driven pages; channel tabs A–D; Type selector; bypass
- [ ] Custom panels: Amp (capture picker plus tone controls), Cab (IR slots plus frequency-response plot via `@xui/echarts`), PEQ (interactive curve via `@xui/konva`)
- [ ] Preset library (`@xui/data-table`, tags, search), save/save-as, A/B compare, undo/redo
- [ ] Audio setup page (device, sample rate, buffer, channel routing, latency test, CPU/xrun stats)
- [ ] Playwright `_electron` E2E for the golden paths

**Exit:** a non-developer guitarist can install it, pick an interface, build a preset from scratch, and save it, with no docs.

---

## Phase 5: scenes, channels, modifiers, MIDI, utilities (5–6 weeks)

- [ ] Scenes (8): block bypass/channel per scene, scene level, scene controllers 1–4; gapless switching
- [ ] Channel switching with crossfade; copy and paste of channels and blocks
- [ ] **Preset spillover** (RCU swap + tail retirement) and scene spillover
- [ ] Controllers: LFO ×2, ADSR ×2, sequencer, envelope follower, pitch follower, Manual A–D, external pedals/CC; a modifier editor UI (right-click any knob, curve preview)
- [ ] Tempo: global, preset and MIDI clock; tap tempo with sample accuracy
- [ ] MIDI: input in the native shim; mappings for preset/scene change, bypass, channel, CC→modifier, tap; MIDI-learn in the editor
- [ ] Footswitch layout model plus an editor for generic MIDI foot controllers (tap/hold functions, LED feedback where supported)
- [ ] **Looper** (stereo, overdub, undo, half speed, reverse, export), **metronome**
- [ ] Global blocks (shared across presets) and global EQ/output

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
