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
| S1 | ◐ macOS done except the 30 min soak and a physical loopback. Linux ALSA (plus JACK) implemented and container-tested; hardware run pending ([ADR-0002](adr/0002-native-audio-backend.md)). **Native audio shim** (`cth_audio`): ASIO (Windows), CoreAudio HAL IOProc (macOS) and ALSA `hw:` / JACK (Linux) calling a managed `delegate* unmanaged` passthrough | 64 samples at 48 kHz, < 6 ms measured loopback round trip, 0 xruns in 30 min on each OS | Use 128-sample buffers as the minimum supported, or use RtAudio as is |
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
- [ ] Audio backends: **Offline** ✅, **Null** ✅, **Native** ✅ macOS CoreAudio, ✅ Windows WASAPI, ◐ Linux ALSA and JACK (no hardware run yet); ASIO still to do
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
- [x] Blocks: **Input**, **Output**, **Shunt**, implicit mixing, **Volume/Pan**, **Filter** (TPT SVF), and an explicit **Mixer** (`mixer`, six rows with gain and balance; added with the parity pass, [08](08-blocks-parity.md))

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

   *Not supported:* conditioned (multi-input) AIDA-X models. A model trained at another rate than the engine's runs at its own rate behind a resampler (`CaptureResampler`).
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
9. [x] **Wah** (Classic/Vox/Wide resonant sweep, plus component-level Cry Baby, V846, Clyde, Colorsound and V845: the inductor circuit as a WDF with both BJTs at an R-type root, the pot inside the adaptor; and the inductor-less Morley (optical) and Parapedal (dual gang) as op-amp band-passes). *Still to do:* driving it from an expression pedal comes with modifiers in Phase 5
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
  - **Bypass modes:** Thru, Mute, Mute In, Mute FX In, Mute Out and Mute FX Out. The "in" modes give scene spillover; the "out" modes keep a bypassed block listening.
- [x] **Channels:** continuous parameters glide through their smoothers on a channel switch, and copy channel is in the API and the block menu.
  - *Not done:* a dual-state crossfade for discrete parameter jumps (a Type change still switches at once).
- [x] **Controllers:** LFO ×2 (6 shapes, free or synced), ADSR ×2 (input-gated), a 16-stage sequencer, an envelope follower, a pitch follower (host MPM at 10 Hz), Manual A–D (global), External 1–8 (CC/pedals), and scene controllers 1–4.
  - They are evaluated per sub-block on the audio thread, and synced ones lock to the sample clock.
  - Modifiers map a source through a curve (linear, exp, log, S) into a log-aware min..max, with attack and release damping.
  - Editor: right-click any knob to open the modifier dialog with a curve preview. A Controllers page holds every setting.
- [x] **Tempo:** preset, global or MIDI clock (24 ppqn, averaged in the host). Tap tempo is sample-accurate (MIDI timestamps become engine sample times), and a tapped tempo is taken back into the preset.
- [x] **MIDI:**
  - The native shim reads CoreMIDI on macOS, WinMM on Windows and the ALSA sequencer on Linux.
  - Mappings (CC, note or PC) cover scene, preset slot or next/previous, bypass, channel, External, Manual, tap, tuner, metronome, the looper, 🆕 scene level up/down (with save) and save. Actions run strictly in arrival order.
  - MIDI learn works from the Global page or from any knob ("Learn MIDI controller" binds a free External source). There is a live monitor.
- [x] **Footswitch layouts:** tap and hold (500 ms) actions per switch, LED feedback (CC or note, 127/0, sent only on change), and an editor on the Global page.
- [x] **Looper:** stereo, 60 s, at the end of the rig so it survives preset changes. Overdub has a one-pass undo layer, plus reverse, half speed and WAV export. **Metronome** (accented, never recorded). Both are in the toolbar. 🆕 The same looper is also a grid block (`looper`, docs/08 §6.14) with the Blocks Guide's Config page; the transport drives it while its preset is running.
- [x] **Preset and scene features** (Fractal's *Presets* page, docs/08 §9): Scene Ignore per channel, Scene Revert, default scene on load (as saved or fixed), startup preset, Ignore Redundant PC, copy/swap scenes, swap/reset channels and reset block, a Scene Manager page, per-block meters on the grid and a BS.1770 loudness meter with a Preset Leveling tool, MIDI scene level ±1 dB with save, a stepping scene CC, modifiers scoped to one channel, a spillover switch, grid loops that survive a preset change, a confirm before discarding edits, a text export of the preset list, and recent presets.
- [x] **Global blocks:** a block can be made global, use a global or be unlinked. Linked blocks take the library's settings on load, and editing one updates the library. There is also a **global output EQ** (10 bands plus level).

**Exit:** a live set of 10 presets × 8 scenes runs with a MIDI foot controller, with no gaps or clicks, verified by HIL soak with scene storms.

---

## Phase 6: catalogue breadth (10–14 weeks, then ongoing) — *first pass built; parallel DSP not yet needed*

Close the gap with the Axe-Fx III block list in [01](01-axe-fx-iii-audit.md), in roughly this order, each block meeting the [Definition of done](03-testing-strategy.md#12-definition-of-done-any-block-or-feature). Every block below has analytic tests, passes the conformance suite (bounded, deterministic, block-size independent, allocation-free, decays to silence), has a golden render and a measured CPU cost, and works in the generated editor.

1. [x] **Multi-delay** (`multidelay`): Quad Tap, Ten Tap (shuffle, taper, spread), Plex (four lines through a Hadamard matrix with allpass diffusion in each loop), Ducking (ping-pong with a ducker on the wet), Reverse (two cross-faded backward heads) and Sweep (a resonant band-pass swept by an LFO). **Megatap** (`megatap`): up to 40 taps with time, level and pan shapes.
   - *Not modelled:* Plex pitch shifters and detune, per-tap filters and feedback, Megatap jitter.
   - *Since then* ([08](08-blocks-parity.md), time-based stream): Multitap became the quad family (Quad Tap, Parallel, Series, Diffusor, Tape, each voice with feedback, filter, chorus, comb and ring mod); Ten Tap moved to a **Ten-Tap** block (`tentap`, with Rhythm Tap) and Plex to a **Plex** block (`plex`: 1–8 lines on a Householder matrix with Detune, Shifter, Verb and Shimmer types); Reverse, Sweep and Ducking became Delay types or features; Megatap goes to 128 taps with randomise, feedback, diffusion and an envelope.
2. [x] **Pitch** (`pitch`): Detune, Chromatic, Diatonic and Custom Shifter (real-time PSOLA, four voices; key and scale, or per-voice tables of a shift for each note of the key), Whammy (pedal 0–100 % for a modifier), Octave Divider, Crystals, Arpeggiator and Virtual Capo. The harmony key is fixed, follows the chord held on the MIDI input (voices snap to chord tones), or is estimated from the notes played (Krumhansl-Schmuckler with hysteresis). They share a YIN tracker (70 Hz–1.4 kHz, within 1 cent, about 29 ms to lock). All types report zero latency; each type's wet delay is part of its sound.
   - *Not modelled:* polyphonic tracking and capo, formant correction, custom scales, a third voice.
3. [x] **Formant** (3-formant vowel morph), **Ring modulator** (tempo-syncable carrier), **Enhancer** (Modern, Classic and Haas; the mono sum is exact for the first two), **Crossover** (Linkwitz–Riley 4th order, sums flat to ±0.1 dB), **Multiband compressor** (3 bands, phase-aligned LR4 splits), **Dynamic distortion** (drive follows the envelope, ADAA clipping at 4×).
4. [x] **Rotary** (horn and drum with Doppler, AM, mic spread and accelerating speed switch), **Uni-Vibe** (`vibe`: four unequal allpass stages driven by a lamp/LDR model; chorus and vibrato), **Synth** (tracker-driven, two PolyBLEP oscillators, filter with envelope; silent on silence), **Vocoder** (20 bands, internal or right-input carrier), **Resonator** (4 damped combs tuned by chord or frequency, decay set as T60).
5. [x] **Routing** (category "Routing"):
   - **Multiplexer** (`mux`): passes the row chosen by its Input parameter (or all), with a 10 ms crossfade. The compiler routes each input through it rather than summing.
   - **Feedback Send/Return** (`fbsend`, `fbreturn`): the return plays what the send got one maximum block earlier, so a later column can feed an earlier one without the result depending on the callback size. The return soft-limits at +18 dBFS, so a loop gain above one saturates instead of running away.
   - **FX Loop** (`fxloop`): send and return through spare interface channels (`--loop-in`/`--loop-out` on the host, set on the Setup page). Its Loop Latency parameter (measured with the latency test) delays the dry path and is reported to the compiler, which compensates parallel paths.
6. [x] **IR Player** (`irplayer`): IRs up to 680 ms at the file's gain, with mix and cuts. It shares the Cab's IR cache. Since the parity pass: two slots (Parallel, Stereo or Series) with per-slot length, level and pan, baked on the control thread (`IControlRefresh`) and crossfaded, cut slopes and Input Select.
   - **Tone Match** (`tonematch` plus `POST /api/v1/live/blocks/{id}/tonematch`): the Server averages the output's spectrum while you play, compares it with a reference recording, smooths the ratio (1/3 octave by default), limits it to ±18 dB and makes it level-neutral. It then designs a 2048-tap minimum-phase filter and stores it as the block's `match` asset. Matching again through an existing match replaces it instead of stacking.
   - **RTA** (`PUT /api/v1/rta`): sixth-octave bands of the output at 15 Hz, shown in the Analyser dialog by the new `cth-rta` canvas. The telemetry scope ring grew to 16k samples for its 4096-point window (layout v3).
7. [x] **Drive catalogue: 24 circuits on a WDF framework** (`Cathode.Dsp.Wdf`). The framework is struct-based with generic adaptors, so there is no virtual dispatch per sample. It has resistors, capacitors, inductors, sources, a diode-pair root solved by Wright omega plus Newton (with first-order antiderivative antialiasing), R-type adaptors from a SPICE-like netlist, and multiport Newton roots for BJTs, tubes and op-amps.
   - The new types are appended to Drive's Type list (indices 3–26), so Overdrive, Distortion and Fuzz keep their indices and their sound.
   - Circuits after: TS808, TS9, modded TS, SD-1, Zendrive, Klon, Timmy, Bluesbreaker, Guv'nor, OCD, RAT, Turbo RAT, DS-1, Distortion+, Big Muff, Fuzz Face (AC128, BC108 and NKT275), Tone Bender MkII, Rangemaster, FET boost, Octavia, a modern high-gain drive and an RC Booster. `DriveCircuits.All` names each one's model.
   - **Tone networks** are the pedals' own, as linear R-type roots at the base rate with the Tone pot inside the adaptor (recomputed only when the smoothed knob moves, on a 32-frame grid): the TS active tone stage, the RAT filter, the Big Muff and DS-1 blends, a Marshall stack (Guv'nor), Baxandall trebles and passive treble cuts. They match the reference simulator's AC analysis to 0.0001 dB.
   - **Op-amp stages**: with diodes across the feedback (the TS family) the op-amp is its linear single-pole model (GBW from `OpAmpModel`) inside the adaptor and the diodes a closed-form root, within -54 dB of the full op-amp device in the reference. Op-amps that clip on their rails get the same linear model with an exponential clamp at the pole node; the RAT's LM308 is the full `OpAmp` device (GBW, slew, rails) at a Newton root.
   - **Transistor fuzzes**: Fuzz Face pairs and the Tone Bender and Rangemaster stages are Ebers-Moll BJTs at Newton roots, started at their DC operating point, within -94 dB of the reference in the time domain.
   - **Input Vol** (appended parameter): the guitar's pickup (8 kΩ, 2.5 H) and audio-taper volume pot as the source the first stage sees, so a Fuzz Face cleans up as it is rolled back.
   - Aliasing at full drive, 770 Hz / 0.1 V: 60 dB or more except the RAT (59 dB) and the transistor fuzzes (26–38 dB, 2x and hard switching).
   - Cost: 3.4 % (TS family) to 5.5 % for most types; 7.5 % for the RATs, 10–14 % for the transistor fuzzes, whose Newton solves are the drive block's worst case.
   - *Not modelled:* op-amp slew outside the RAT, junction and Miller capacitances, germanium leakage drift with temperature, bias trimming.
8. [x] **Cab library tooling:**
   - **IR mixer** (`POST /api/v1/assets/mix`, and the IRs page): blends up to 8 IRs with level, delay, polarity and peak alignment into a new IR asset.
   - **IR capture** (`cathode-host --ir-capture`, and the IRs page in the desktop app): an exponential sweep on outputs 1–2, recorded on input 1, deconvolved with Farina's inverse filter and trimmed 1 ms before the peak. A wire measures flat to ±0.25 dB from 200 Hz up.
9. [ ] **Parallel DSP**: rows on worker threads joined to the audio workgroup or MMCSS, when CPU telemetry shows a need. *Not needed yet:* the measured costs (`BlockCosts`, served as `cpuCost` in the catalog and summed into the editor's estimate) put the most expensive single blocks at 14 % (Drive's transistor fuzzes), 7 % (IR Player with a full-length IR) and 2 % (Cab with a full-length IR), and the Phase 2 "everything" rig at 15 % of one core.
10. [x] **Cab** (`cab`): one block with four slots, as the Axe-Fx III Cab block mixes Legacy IRs and DynaCabs. Each slot has a **Source**: **IR** (an impulse response file), **DynaCab** (a modelled speaker in a cabinet with a mic at a continuous position, cap to edge in %, distance 0–12 in and angle) or **IR Pack** (the user's multi-position pack, interpolated at the slot's position and distance). Every slot has its own low and high cut (6–24 dB/oct), IR Length, level, pan, delay, polarity, mute and solo. A new block plays slot 1 as a DynaCab (closed 4×12, Vintage 30, SM57 at 25 %, 1 in) with slots 2–4 muted. The block replaced the earlier separate `cab` (two IR files) and `dynacab` blocks. There were no real presets yet, so nothing was migrated.
   - **Parameters**, in order: master Low Cut and Low Cut Slope, High Cut and High Cut Slope, Input (Left, Right, Sum L+R, Stereo), Air and Air Freq, Preamp Type, Preamp Drive and Preamp Saturation, Level, Proximity, Room and Auto Delay; then for each slot `m1`–`m4`: Source, Cab, Speaker, Mic, Position, Distance, Angle, Low Cut, Low Cut Slope, High Cut, High Cut Slope, IR Length, Level, Pan, Delay, Invert, Mute and Solo. The asset slots are `slot1`–`slot4`. A `.wav` serves an IR slot, or an IR Pack slot as a one-point pack; a `.cabgrid` serves only an IR Pack slot.
   - **Levels are relative**, as on the Axe-Fx: the levels of the slots that play are scaled to sum to unity. With one slot its level does nothing; with more it sets the blend. The block's Level sets the loudness.
   - **Nothing to play**: an IR or IR Pack slot with no file contributes nothing. When no audible slot has anything to play the block passes audio through untouched (the identity, no convolution), as a cab with no IR always has. Muting every slot silences it.
   - **Catalogue**: 10 speakers (Celestion Greenback, G12H30, G12T-75, Vintage 30, Creamback, Alnico Blue; Jensen P10R, C12N; EVM12L; JBL D120F), 11 mics (SM57, MD 421, e 906, e 609, Beta 57A, R-121, 4038, M 160, U 87, C414, a measurement omni) and 7 cabinets (open and closed 1×12, 2×12, open 4×10 and 1×10, closed 4×12). Each is a line of parametric data hand-fitted to the maker's published chart, with its source in a comment. Thiele–Small values the maker does not publish are typical values, marked as such. Shipping cab IR packs is not possible under the GPL, so DynaCab IRs are **synthesised** (`Cathode.Dsp.Cabinets`).
   - **Model** (`CabAcoustics`): the magnitude part (closed-box or free-air resonance from Fs/Qts/Vas, breakup peaks weighted near the cone by the n-th radial mode `J0(j0n r/a)`, the speaker's and mic's response, and the near-field position loss from a radiating area that shrinks towards the dust cap above breakup) is made minimum phase by the real cepstrum. It is multiplied by the physically phased paths: direct sound with first-order proximity `A + B cos θ (1 + 1/jkρ)`, the cab's other speakers (coherent below breakup, as power above), an open back's inverted back wave, and floor and room reflections. The result is normalised to equal K-weighted pink-noise loudness, 2048 taps at 48 kHz. IR files are resampled, normalised to unit energy and kept up to 8192 samples.
   - **Mixing**: all four slots fold into one left and one right IR, with level, pan, polarity, delay, Auto Delay's distance delay (between modelled slots; an IR file carries its own onset), and each slot's cuts and IR Length baked in. Fractional delays use a windowed sinc. The block runs two convolutions, as long as its longest slot, whatever the slot count. Rebuilds run on the control thread (`IControlRefresh`, §5.4 of [02](02-architecture.md)): 2.7 ms for a moved slot, 0.1 ms for a remix. They are crossfaded in over 20 ms.
   - **Around the convolution**, on the audio thread: Input picks left, right, their sum or stereo. Air mixes the dry signal above Air Freq back in, at up to −12 dB. The mic preamp (`MicPreamp`: None, Tube, Bipolar, FET, Transformer, Tape) is a waveshaper per type with unity small-signal gain. Drive sets how hard it is pushed and Saturation biases it for even harmonics; a fixed treble roll-off per type gives the tone. It is not oversampled. Then come the master cuts with their slopes, and the Level.
   - **User IR grids**: a `.cabgrid` asset (a zip with `manifest.json` listing `{position, distance, file}` on a full grid), a folder, or a single `.wav`. Points are interpolated bilinearly in log magnitude, with the onset delay interpolated apart, and rebuilt as minimum phase. Crossfading neighbouring IRs instead would comb-filter. Packs live in the asset store, never in the repository.
   - **Editor**: one Cab panel. Mic 1–4 (muted slots marked) each has a Source selector. `cth-speaker-cone` (audio-ui, with stories) shows the DynaCab and IR Pack slots; each is dragged across the cone and away from the grille, or moved with the arrow keys. IR and IR Pack slots get the asset picker, and the plot shows the IR files' responses with their relative levels and cuts. Editing a muted slot unmutes it: moving it, changing its source, mic, speaker or cab, loading its file or turning one of its knobs. A banner says so, with an Unmute button.
   - Cost 2.1 % with a full-length IR in a slot (preamp and Air off); the default DynaCab slot costs less, as the convolution is only as long as the longest slot. *Not modelled* (from the Axe-Fx III Cab block): FullRes IRs (up to 1.37 s), per-slot Smoothing and Proximity Freq, the Align page's 0–3400 mm mic distance (slot Delay covers 0–5 ms), the preamp's High Quality/Economy mode, its Bass/Mid/Treble, its Vintage, Modern, Exciter and three Tape types, its VU meter, the room simulator's size, shape, damping, mic spacing and diffusion (Room stays the synthesised early reflections), and the Cab Picker's size filter. Speaker drive and compression, cone ageing, slant cabs and the phase detail lost when a user grid is rebuilt as minimum phase are not modelled either.

11. [x] **Blocks Guide parity pass** ([08](08-blocks-parity.md)): every block audited against the June 2023 Blocks Guide, page by page. Added Balance on every block, the ten guide LFO waveforms with duty, a longer shared tempo list, the Mixer block, the two "out" bypass modes, Tremolo's Bias/Harmonic/Optical types, new Filter types (null, all-pass, tilt, combs) with pans and an LFO, a full Volume/Pan (tapers, auto-swell), Delay ping-pong, ducker, hold, bit reduction and the guide's delay mix law, and input gain, sidechain and ducker controls across the dynamics, delay and reverb blocks. [08 §8](08-blocks-parity.md#8-plan-for-the-medium-and-large-gaps) plans the rest (Global Mix, Input gate and Output mixer, Scene MIDI). The time-based stream then completed Delay, Megatap, Multitap, Reverb and the Looper (now also a grid block) and added the Plex and Ten-Tap blocks.
12. [x] **Parity: engine and global** ([08 §1.2](08-blocks-parity.md#12-what-this-pass-implemented)): `IEngageAware` (LFO Bypass Reset, Tremolo Start Phase), Global Mix with a global Effects Mix offset, blocks on the Input (noise gate) and Output (row mixer) cells, Scene MIDI, Compressor and Gate sidechain sources (`ISidechainBlock`, since folded into `ISideInputBlock` taps), Compressor look-ahead (latency with a `Recompile` parameter), the Block Library of factory and user block presets, two Feedback Send/Return pairs, the RTA as a grid block, Stereo Spread to ±200 %, and a modifier on a block's engage.

13. [x] **Audio I/O** ([08 §2.1](08-blocks-parity.md#21-audio-io-wiki-audio-io-and-amplification)): Output 2–4 grid blocks mapped to device channel pairs, Copy Output 1 to Output 2, per-output mode, global EQ and Boost/Pad, Input 1 calibration (full-scale volts, read by the Circuit Amp and Drive) with a Setup helper, Input 1 Gain, input and output clip warnings, a pickup-load Input Impedance on the Input block, the dry DI out, a linear-phase oversampling mode (and the Drive's own factor) with its latency compensated, a latency report, a stereo-into-mono hint on the grid and the cab resonance helper.
   - With multi-channel input, calibration, gain, the clip warning and the DI out cover Input 1, the main input; the settings are lists per input, ready for the other sources.

**Definition-of-done tooling added in this phase:**
- `tools/Cathode.Bench` measures every block (the worst of its types, with the longest IRs) and writes `BlockCosts.cs`. The conformance suite fails for a block without a cost.
- `GoldenRenderTests` compares each block's default render with `tests/assets/golden/<type>.wav` (null residual below −90 dB). Regenerate with `CATHODE_UPDATE_GOLDEN=1`.

---

## Phase 7: "Circuit" amp modelling (research track, starts during P2, 6–12+ months)

This is the most important differentiator against "just a NAM host" and the hardest work. Run it as a **parallel research track** with its own milestones:

1. **R1 measurement rig:** reactive load box, re-amp box, reference interface and a calibrated mic setup. Capture our own reference amps (start with 3: Fender blackface, Marshall Plexi/JCM800, a modern high-gain).
2. [x] **R2 stage library (C#):** triode (Koren/Cardarelli models) via WDF with an R-type solver, cathode follower, passive tone stacks (exact), phase inverter (long-tail pair), push-pull pentode power stage, output transformer (hysteresis-lite), power-supply sag (RC network driven by current draw), and a speaker impedance load.
   - **As built** (`Cathode.Dsp.Amp`, on the R-type roots of `Cathode.Dsp.Wdf`):
     - `TriodeStage`: common cathode with its input network (coupling cap, volume pot with bright cap, mixing resistor with its bypass cap, grid leak) in one root; the pots are internal resistors, so a knob only recomputes the matrix.
     - `ToneStackStage`: a cathode follower or common-cathode triode driving a Fender, Marshall or Vox top-boost-style TMB stack and a volume/master pot, all in one root, so the stack loads its driver exactly. `ToneStackCircuit` is the stack alone.
     - Stages are joined by a Thévenin cut: the next stage includes the previous plate's small-signal output resistance, so loading, coupling caps and grid current (blocking) act through it.
     - `LongTailPair`: the inverter's static transfer, tabulated from the exact two-triode solution, with its 2 × 2 output impedance (the cross terms matter: they cut the effective output impedance by a third or more). Grid current at its input is left out.
     - `PowerAmp`: push-pull pentodes as one nonlinear port on the transformer primary, with the inverter, cut control, both grid networks (coupling caps, leaks, stoppers, piecewise-linear grid conduction) and the feedback network (presence, depth) folded into the device. Global feedback is solved without a delay, in one scalar Newton iteration per sample. The load is the output transformer (magnetising and leakage inductance, copper, primary capacitance, core loss) and a Thiele-Small speaker with a lossy voice coil. The supply (reservoir, screen and preamp nodes) sags from the tubes' current; cathode bias follows its bypass cap.
     - `TriodeTables`/`PentodeTables`: Koren's laws split into 1-D cubic Hermite tables (within 4e-7 of the exact model), a third of the cost of the exact evaluation.
   - **Validated** against the reference MNA simulator (16x oversampled, exact tubes): tone stacks within 0.001 dB at every knob setting tested; triode stages −71 to −120 dB error in the time domain at moderate drive, tone-stack stages −64 to −76 dB; operating points within 0.2 % (12AX7, 100k and 1.5k: 183 V at 270 V, 203 V at 300 V); the inverter table within 0.01 V of the exact DC sweep; power amps −35 to −40 dB (EL34 with feedback, 6L6 with presence, cathode-biased EL84 quad with cut).
   - **Stage library extensions** (docs/06 §5.1, stream L), each against the same reference:
     - Models are data: one file per amp in `Amp/Models` (an `AmpDesign` with its `AmpPanel`), registered by one line in `AmpDesign.All`. Model-specific panel controls are the block's generic Switch 1–3, Control 1–2 and EQ 1–5, named (or hidden) per model through `ParamDescriptor.TypeLabels`, which the editor follows.
     - Tubes: 12AY7 and 5751 (fitted to their datasheet points within 0.1 %), 6V6GT and KT66 (fitted), KT88 and 6550 (Koren), EF86 (fitted, for a pentode preamp stage); tables within 4e-7 of the exact law.
     - `NetworkStageDesign`: any netlist (resistors, capacitors, inductors, pots and switched elements bound to front-panel controls, supply rails) with up to two triodes, a pentode or a diode clipper, as one R-type root with its operating point from a DC solve of the same netlist. Passive networks (a non-TMB blend stack with a rheostat and a switched cap, a Mark-style LC band) within 0.000002 dB; a cathode follower not driving a stack −108 dB (into grid conduction −109 dB); an EF86 stage −99 to −101 dB; a diode clipper −52 dB; a cold clipper with its plate bypass cap (SLO stage 3) −72 to −75 dB, into grid conduction. A triode stage rebuilt as a network matches the dedicated one to −90 dB and costs the same.
     - Cathodyne phase inverter (table within 0.01 V of the DC sweep); grid current into the inverter's driven grid through its coupling cap and bootstrapped leak, with the plates' load and the cathode's rise from the grid current in its tables (grid voltage −52 dB from the reference driven into conduction, against −25 dB without); a graphic EQ slot (`EqualizerStageDesign`, the Mark's five bands before the inverter).
     - Power section: single-ended class A (−55 dB), ultralinear (−38 dB), triode strapping by a panel switch (−39 dB), Simul-Class (a triode-strapped pair beside the pentodes, −43 dB), separate cathode resistors (−40 dB), a cathodyne-driven pair (−53 dB); tube rectifiers (Child's law: GZ34, 5U4, 5Y3, EZ81) and AC mains with ripple at twice the line frequency, variac scaling; output transformer core saturation (low notes: +21 dB of THD at 42 Hz at full drive, none at 1 kHz); speaker impedance presets (open 1 × 12, 2 × 12, 4 × 10; closed 4 × 12, 8 × 10).
     - 38 parameters after Level, every one stock at its default (Axe-Fx parity, docs/06 §1.4: tubes, bright cap, preamp bias and sag, stack frequency, master trim, power amp off, transformer match, bandwidth and drive, speaker curve and its resonance, Q, amount, HF and impedance, presence and depth frequencies, variac, B+ time constant, supply type, rectifier, cathode time constant, input trim, select and low cut).
     - The R-type root solves one- and two-port devices with written-out Newton (the same arithmetic as the general solver), and the amp's stages start Newton from a linearised step instead of extrapolating: on loud noise a triode stage takes 2.2–4.5 iterations instead of 3.5–6.3. The Circuit Amp's worst case fell from 19.8 % to 14.1 % of a core (guitar 6.5–7.9 %); the first three models are within −143 dB of their earlier renders.
     - *Not yet:* hysteresis, Miller capacitance, an unbypassed shared cathode resistor in the power stage, bias tremolo, op-amp and transistor clippers in a network stage, a rectifier switch on the panel.
3. **R3 first complete amp** matched to its references: harmonic profile, frequency response per knob setting, ESR/MRSTFT dashboard.
   - **Started:** the Circuit Amp block (`circuitamp`) with Blackface (2 x 6L6GC), Plexi (2 x EL34) and AC30 (4 x EL84, cathode bias, cut, no feedback) built from the schematics' values, everything at 2x the sample rate. Cost 8–11 % of one core on guitar, 19.8 % worst case (loud white noise). Not yet matched to references: that needs the R1 rig. A standalone **Tone Stack** block (`tonestack`) uses the same stacks.
   - *Not modelled yet:* transformer hysteresis, Miller capacitance, the Blackface's reverb and vibrato and V4B mixer, Plexi channel jumpering (the stage library now has what they need; the first three models keep their sound).
   - **Reference harness** (docs/06 §4.3): a manifest of real-amp captures (GPL ones fetched by `cathode-references fetch`, T3K ones by hand), NAM and GuitarML LSTM runners, calibrated input in volts, ESR/MRSTFT/harmonic/response report (`cathode-references match --html`) and a tolerance fit. First numbers against GPL captures of a Twin, JCM900, Laney GH, AC15 and Matchless SC30: ESR (pre-emphasised) 0.5–1.5, MRSTFT 0.8–2.3, before any family work.
4. **R4 "expert" parameters** (bias, sag, transformer, tube types, negative feedback, presence/depth), mirroring the Axe-Fx III depth the users expect.
5. **R5 hybrid tier:** neural preamp capture with a parametric power amp, so master, presence and depth respond on captures.
6. **R6 scale-out:** additional amp families as configurations of the stage library, about 1 model per 1–2 weeks once the framework is mature.

Gate for each model: ESR/MRSTFT within target across a knob sweep, plus an ABX panel against the reference.

---

## Phase 8: polish and ship (4–6 weeks)

- [x] **Editor workflow** (stream ED, [02 §7.5](02-architecture.md#75-editor-workflow-as-built)):
  - shortcuts with a `?` overlay, and preferences;
  - revert, clear, snapshots and templates;
  - replace, swap and drop, row and column moves, cut, paste settings, disconnect;
  - all-scene actions, Block Info, and the Circuit Amp Ideal/Authentic view;
  - Library folders, files and whole-block entries;
  - Manage Presets and Manage IRs, Reset Amp blocks, Parameter Batch Set;
  - picker tags, favourites and pin, Perform pages, and footswitch views, links and per-preset overrides.
- [x] Editor workflow on top of the PS stream: ⌘L opens Preset Leveling, ⌘I resets the selected block's channel, and the preset picker, Go to, ⌘PgUp/PgDn, F5 and New from template ask before discarding unsaved edits (`PresetSwitcher`).

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
