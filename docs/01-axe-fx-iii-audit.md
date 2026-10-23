# 01 — Axe-Fx III feature audit (as of October 2026)

**Purpose:** list what the Fractal Audio Axe-Fx III does, and translate each feature into a requirement for a software-only equivalent.

**Status at time of writing:**
- Latest official firmware: **32.06** (25 June 2026).
- Public beta: **33.00 beta 2** (6 October 2026). It adds a **NAM block** and needs Axe-Edit III 1.15.

**How this was sourced:**
- Primary sources: fractalaudio.com product pages, the *Owner's Manual* (Jul 2022, fw 20), the *Blocks Guide* (Jun 2023, around fw 22/23), and the official firmware release threads on forum.fractalaudio.com.
- The Fractal wiki was blocked by Cloudflare during research, so nothing here comes from it.
- **All model and type counts are lower bounds.** The guides predate fw 25–33.

> **Legend:** the "Software implication" column says what *we* must build. ⚡ means a hardware feature with no direct software equivalent.

---

## 1. Hardware and DSP: what the computer and interface must replace

| Axe-Fx III | Spec | Software implication |
|---|---|---|
| DSP | 2× TI Keystone at 1.0 GHz (Turbo: 1.25 GHz, ~25 % more). UI and USB run on separate MCUs, so they cost no DSP | A modern x64 or Apple Silicon core is far stronger per core. The **UI and control plane must not steal from the audio thread**, which motivates the [process split](02-architecture.md#why-the-engine-and-the-rest-server-are-separate-processes-) |
| Sample rate | **48 kHz fixed**, 24-bit | Run the engine at **48 kHz internally**. This is also NAM-native. Resample at I/O only if the device can't run at 48k |
| Latency | No official figure. Hardware modellers are typically ~1–3 ms analog-to-analog *(unverified)* | Target **< 6 ms round trip at 64 samples** on ASIO or CoreAudio |
| Instrument input | 1 MΩ, **Auto-Z**, selectable impedance 1M/230k/90k/70k/32k/22k (± capacitor), done as *real analog circuitry* | ⚡ This can't be done in software. Recommend an interface with a true Hi-Z input. Optionally model pickup loading with a **digital pickup-load EQ** (RLC resonance) as an approximation |
| Analog I/O | In 1–4, Out 1–4 with Humbuster, AES/S/PDIF, headphones | Generic **N-in / N-out routing matrix** onto whatever channels the interface exposes |
| USB audio | 8 in / 8 out. Reamp via USB 5–8 | Native with any interface. Add **built-in reamp** (render a DI file through the preset) as a bonus feature |
| MIDI | In/Out/Thru, plus 2 pedal jacks | MIDI via the OS (USB-MIDI). Expression pedals arrive through a MIDI controller |
| FASLINK II | Proprietary link to FC-6/FC-12 | ⚡ Replace with **generic MIDI foot-controller support** plus a layout model |
| Presets | 512 (Mk I) / 1024 (Mk II) | Unlimited (disk). Add banks and setlists for live use |

---

## 2. Grid and routing

| Feature | Detail | Requirement |
|---|---|---|
| Grid | **6 rows × 14 columns**, up to 6 parallel paths, 4 independent chains | Default 6×14, **configurable**. The graph compiler is in [02 §5.2](02-architecture.md#52-graph-compilation) |
| Shunts | Stereo pass-through cells | Collapse into edges at compile time (zero cost) |
| Input block | Input 1–4 plus USB. Built-in **noise gate**: Classic, Intelligent, or Noise Reducer (hum-aware, follows AC line frequency). 4 channels | An Input block per physical input pair. The gate has a mains-hum-aware mode with a 50/60 Hz setting |
| Output block | Out 1–4, with a **6-row mixer** (level and balance per row) and **8 per-scene output levels** | Output block with row mixer and scene levels |
| FX loop | Out n → external gear → In n (no dedicated block) | Same pattern, plus **automatic latency compensation** for the loop's round trip |
| Mixer | 6 stereo inputs from the column to its left | Implicit mixer node in the compiled graph, also available as an explicit block |
| Multiplexer | Gapless selector between inputs or rows | MUX block with a crossfade |
| Crossover | 2-way, LR4. Two cascade for 3-way | LR4 crossover; the null test checks it sums flat |
| Feedback Send/Return | 2 pairs. Route backwards or extend past column 14 | Allowed cycles. The compiler inserts a one-block delay, as on the hardware |
| **Bypass modes** | THRU, MUTE, MUTE IN, MUTE OUT, MUTE FX IN, MUTE FX OUT. The "IN" modes give spillover | Implement **all six** per block, with crossfade |
| Kill Dry | Delay, Multitap, Pitch, Plex, Reverb | A per-block flag for parallel routing |
| Common mix | Mix (inverse-linear, 50 % = −6 dB each), Level, Balance, Input Gain, **Global Mix offset ±50 %**, Stereo Spread −200…+200 % | A shared "mix section" in every effect descriptor, plus a global mix offset |
| CPU meter | Per-preset CPU %. Bypassed blocks still count. Inserting a block over budget is refused with "INSUFFICIENT CPU" | **CPU estimator** from benchmark data, plus a live callback-time meter. Warn rather than refuse, since PCs vary |

### Block instance limits per preset

| Block | Types | Ch | Max | | Block | Types | Ch | Max |
|---|---|---|---|---|---|---|---|---|
| Amp | 309+ | 4 | 2 | | Multiband Comp | 1 | 4 | 2 |
| Cab | 2 150+ IRs | 4 | 2 | | Multiplexer | 1 | 4–6? | 2 |
| Chorus | 17 | 4 | 2 | | Output | 1 | 4 | 4 |
| Compressor | 16 | 4 | 4 | | Parametric EQ | 1 | 4 | 4 |
| Crossover | 1 | 2 | 2 | | Phaser | 16 | 4 | 2 |
| Delay | 27+ | 4 | 4 | | Pitch | 16 | 4 | 2 |
| Drive | 65+ | 4 | 4 | | Plex Delay | 44 | 4 | 2 |
| Dynamic Distortion | 7 | 4 | 2 | | Resonator | 1 | 2 | 2 |
| Enhancer | 3 | 4 | 2 | | Send / Return | 1 | 1 | 2 / 2 |
| Filter | 15+ | 4 | 4 | | Reverb | 66+ | 4 | 2 |
| Flanger | 31 | 4 | 2 | | Ring Mod | 1 | 2 | 1 |
| Formant | 1 | 4 | 2 | | Rotary | 1 | 4 | 2 |
| Gate/Expander | 3–4 | 4 | 4 | | RTA | 1 | 1 | 1 |
| Graphic EQ | 16 | 4 | 4 | | Scene MIDI | 1 | 1 | 1 |
| Input | 1 | 4 | 5 | | Synth | 1 | 4 | 2 |
| IR Player | 1 | 4 | 2 | | Ten-Tap Delay | 2 | 4 | 2 |
| Looper | 1 | 1 | 1 | | Tone Match | 1 | 1 | 1 |
| Megatap | 20 | 2 | 2 | | Tremolo/Panner | 5 | 4 | 2 |
| Mixer | 1 | 4 | 4 | | Vocoder | 1 | 1 | 1 |
| Multi-Delay | 34+ | 4 | 2 | | Volume/Pan | 1 | 4 | 4 |
| Wah | 8 | 4 | 2 | | **NAM** (fw 33 beta) | — | — | 2 (see §8) |

- **Total: about 44 block types.**
- In software, the instance limits are **soft limits in the descriptor** (`maxInstances`) and can be raised.
- Keep the **channel counts** (4, 2 or 1) exactly. They are part of the mental model users carry over.

---

## 3. Block catalogue: what each one must do

| Category | Axe-Fx III highlights | Our build approach (see [02](02-architecture.md)) |
|---|---|---|
| **Amp** | 300+ models, Cygnus X-3 component modelling, Authentic/Ideal knob tapers | Three tiers: **Capture** (NAM/AIDA-X), **Circuit** (own WDF/DK stage library), **Hybrid**. The amp parameter surface is in §4 |
| **Cab** | 4 IRs per channel with a Cab-Lab-style mixer (level, pan, align, proximity, smoothing), mic-preamp sim, room, air. **DynaCab**: 2048-sample, phase-aligned IRs with continuous mic Position and Distance, 4 mic types | Partitioned convolution, 4 slots, alignment. **DynaCab equivalent** = a grid of our own phase-aligned captures per cab and mic, *interpolated* by position and distance |
| **IR Player** | 2 IRs, series or parallel, FullRes (up to 1.37 s) | Same block. Long IRs use non-uniform partitioned convolution |
| **Drive** | 65+ types. Clip type, diode type and count, bass response, dry level, expert params. HM-2 added in fw 32 | WDF circuit library with exposed "expert" components (diodes, caps) |
| **Dynamic Distortion** | Frequency-dependent dynamic saturation for mastering | Multiband dynamic waveshaper |
| **Delay** | 27+ types: digital, analog/BBD, tape, dual tape, reverse; up to 16 s; diffusion | Shared delay core with fractional interpolation, BBD/tape colouring, diffusion allpasses |
| **Multi-Delay / Megatap / Plex / Ten-Tap** | 34+, 20, 44 and 2 types: multi-tap, rhythm, band, diffusors, 8-line feedback matrix with pitch | Built on the same delay-line core plus an FDN matrix (Plex) |
| **Reverb** | 66+ types: waveguide springs (fw 23/28), plates, rooms, halls, chambers, digital classics | FDN family + Dattorro plate + dispersion-allpass spring. A "Quality" parameter trades CPU |
| **Chorus / Flanger / Phaser / Rotary / Trem-Pan** | 17 / 31 / 16 (incl. Vibe) / 1 / 5 types | Shared modulated-delay and LFO core, a Vibe photocell model, a multi-mic Leslie |
| **Pitch** | 16 types: detune, chromatic, diatonic harmony, whammy (classic and advanced), octave divider, crystals, 16-step arpeggiator, custom scales, polyphonic **virtual capo**. Polyphonic tracking (fw 29), formant correct and shift (fw 33) | Time-domain PSOLA/WSOLA for low latency. Polyphonic capo via a multiband or phase-vocoder hybrid (a latency trade-off) |
| **Filter / Formant / Wah** | 15+ filter types incl. envelope filter, auto-wah, **ladder** (fw 33); vowel formant; 8 wahs | TPT/ZDF SVF and ladder filters, inductor wah model, envelope follower |
| **Dynamics** | 16 compressor types (pedal, opto, tube, studio bus, VCA bus, JFET); 3-band MBC; 4 gate/expander types with sidechain filters | Feed-forward and feedback topologies with opto/JFET/VCA detector models |
| **EQ** | 16 GEQ types (incl. passive "Mark" 5-band, console 3-band); 5-band PEQ | Analytic-tested EQ family |
| **Misc** | Enhancer, Ring Mod, Resonator (4 combs), 3-voice tracking Synth, 24-band Vocoder, Volume/Pan (width, mono, swap, phase) | Straightforward DSP, built in Phase 6 |
| **Utility blocks** | **Looper** (stereo, 300 s, overdub/undo), **Tone Match**, **RTA**, **Scene MIDI** (up to 8 PC/CC messages per scene) | Same blocks. Tone Match = smoothed spectral-ratio FIR, exportable as an IR |
| **NAM** (fw 33 beta) | Loads `.nam` files. Full and Lite (Full ≈ 6× the CPU of Lite). About a dozen tone-stack types Pre or Post; Bright. Max 2 NAM, or 1 Amp + 2 NAM | **This confirms our hybrid strategy:** even Fractal now hosts NAM next to its own modelling. We host NeuralAmpModelerCore natively (A1 and A2) and add tone stacks Pre/Post |

---

## 4. Amp parameter surface (our Circuit tier must offer this depth)

Axe-Fx III users expect these pages. The table maps each one to the modelling stage that implements it in our stage library ([04 Phase 7](04-implementation-plan.md#phase-7-circuit-amp-modelling-research-track-starts-during-p2-612-months)).

| Page | Parameters | Model stage |
|---|---|---|
| Tone | Gain, Input Trim, Bass/Mid/Treble, Overdrive (+volume), Master, Presence (+shift), Depth, Bright switch, Hi-Cut | Triode stages, passive tone stack, NFB loop |
| Preamp | Saturation, Fat, Cut, **Preamp Tube Type**, Bright Cap, High Treble, Boost (pedal-based) | Triode WDF params, bright-cap network |
| Input/Output EQ | Low/High Cut, Definition (tilt), Pre-EQ, movable post-amp GEQ (console, passive, "Mark") | Linear EQ blocks inside the amp |
| Power supply | Supply Sag, B+ Time Constant, Variac, AC/DC supply, line frequency | Rectifier + RC supply model driven by current draw |
| Power amp | Power-amp modelling on/off, **Power Tube Type** (6AQ5…KT88, EL34, 6L6, Transistor), Grid Bias, **Negative Feedback**, Tube Hardness, Transformer Matching, Transformer Drive, Bias Tremolo | Push-pull pentode stage, phase inverter, OT with core saturation, NFB |
| Speaker | **Impedance curve** (follows the cab: "Auto DynaCab Impedance"), HF slope, Cabinet Resonance, Speaker Drive, Thump, **Speaker Compression** + time constant, Compliance | Speaker impedance load on the OT, plus a speaker nonlinearity |
| Dynamics | **Input Dynamics** (±), Output Compression (type, threshold, clarity), Master Bias Excursion | Dynamics stage plus bias-shift state |
| Expert | Tonestack type and freq and **location** (Pre/Post/Mid/End), preamp sag, hardness, bias and bias excursion, cathode follower compression and harmonics, MV trim, cap and location, presence/depth freq, PA cathode R and time constant, PI and power-tube bias excursion, plate suppressor diodes, Output Mode (FRFR, SS PA + cab, …) | Every stage exposes its component-level parameters. The descriptor's "expert" flag hides them by default |

Fractal's amp algorithm generations show the scale of the target. The Circuit tier is a **multi-year** effort, which is why it is a parallel research track.

| Algorithm | Firmware | Date |
|---|---|---|
| Cygnus | 16 | May 2021 |
| Cygnus X-2 | 21 | Nov 2022 |
| Cygnus X-3 | 25 | Apr 2024 |
| Further power amp and preamp refinements | 27, 32, 33β | to Oct 2026 |

---

## 5. Presets, scenes, channels and modifiers

| Feature | Axe-Fx III | Requirement |
|---|---|---|
| Scenes | **8 per preset**, nameable. Each stores per-block bypass and channel, 4 scene-controller values and output levels. Instant, with spillover | Same. Applied atomically in one audio block |
| Channels | **A–D per block** (some blocks have 1–2). Copy and paste | Same |
| Scene Ignore | Per channel, excludes a block from scene control (fw 19.05) | Same flag |
| Gapless switching | Preset, scene and channel changes (fw 23, on by default) | Default behaviour. Graph pre-roll plus a crossfade |
| Global blocks | 8 globals per block slot, stored in system memory; modifiers stay in the preset; not available for ~14 block types | Global block references in the preset, plus a global store |
| **Modifiers** | 1 or 2 sources per knob (with Scale and Operation). Min/Max, Start/Mid/End, Slope, Scale, Offset, Damping or Attack/Release, Auto-Engage + Off Value, PC Reset, Update Rate, per-channel enable | Same parameter surface. Control-rate evaluation, see [02 §5.4](02-architecture.md#54-parameters-smoothing-modifiers) |
| Controllers | 2 LFOs (A/B outputs, phase), 2 ADSRs, Sequencer (2–32 steps), Pitch Detector, Envelope Follower, **4 Scene Controllers**, **5 Manual controllers**, Tempo, **16 External** (MIDI CC or pedal) | Same set |
| Tempo | Per preset, tap tempo, sync for delay and modulation | Plus MIDI clock in and out |
| Performance Controls | 2 pages × 10 user-picked controls (global and per-preset) | A "Performance" panel in the editor and a REST resource |
| MIDI | PC with banking and mapping, CC for scene and channel, PC transmit, Scene MIDI block | MIDI mapping service plus a Scene MIDI block (MIDI out) |
| Spillover | Per block via MUTE FX IN / MUTE IN; across presets via a global setting (only with matching block positions) | **Better than the hardware:** tails from the retired graph always spill over, regardless of block positions |

---

## 6. Utilities

| Utility | Axe-Fx III | Requirement |
|---|---|---|
| Tuner | Strobe and needle, source select, mute modes, 430–450 Hz calibration, downtune 0–4 semitones, per-string offsets ±25 ¢ (Feiten) | MPM/YIN on the analysis thread, ±0.1 ¢ display, same settings |
| Metronome | Set from the Tempo page, level per output | Same |
| Looper | Stereo, 300 s | Stereo, length limited by RAM, export to WAV |
| Tone Match | Match to a reference; save into the preset or export as a User Cab | Block plus a job API |
| IR capture | Built in (flat SS power amp, or amp + load). Cab-Lab 3 for mixing | **IR capture utility** (sweep, deconvolve, trim) plus an IR mixer/export |
| Global EQ and output | EQ and level per analog output, Global Mix offset, Noisegate offset, Global power-amp modelling off, output copy modes | Global settings resource |
| Leveling | VU meters in grid zoom, Leveling panel in Axe-Edit | LUFS/peak meters per block and a **preset leveling** tool (auto-match loudness across scenes) |
| Firmware and backup | Fractal-Bot backup and restore | Library export/import, auto-backup |

---

## 7. Editor and foot controllers (the Axe-Edit III parity target)

- **Axe-Edit III:**
  - grid view with drag and drop
  - block editor
  - Quick Build templates
  - Block Library (saved block presets)
  - Scene Manager
  - **Axe-Manage** for presets (FC bank colouring) and cabs (drop `.wav` files)
  - **Manage NAM**
  - FC editor with per-preset assignments, Setlists and Songs
  - Leveling panel
  - copy and paste of channels and controllers
  - dark mode
- **Axe-Change:** an online preset and cab sharing site. Out of scope for v1; the content-addressed import/export format makes it possible later.
- **FC-6 and FC-12:**
  - up to 4 controllers chained
  - per-switch **TAP and HOLD** functions
  - global layouts plus **per-preset overrides**
  - clone mode
  - setlists and songs
  - layout count is "up to 12" per the product page *(8 per community posts; unverified)*
  - We replicate this as a **footswitch layout model for generic MIDI controllers**.

Each item maps to the editor feature list in [02 §7](02-architecture.md#7-editor-electron--angular-22--tailwind-4--xui).

---

## 8. Firmware milestones (for context)

| FW | Date | Milestone |
|---|---|---|
| 1.x | 2018 | Launch |
| 16 | May 2021 | **Cygnus** amp modelling; FullRes IRs |
| — | Sep 2021 | Mk II Turbo (1.25 GHz) |
| 19.05 | 2022 | Scene Ignore |
| 21 | Nov 2022 | **Cygnus X-2** |
| 22 | Jun 2023 | **DynaCab** |
| 23 | 2023 | **Gapless switching**; new spring and plate reverbs |
| 25 | Apr 2024 | **Cygnus X-3**; Kill Dry |
| 27 | Sep 2024 | New cathode-follower and power-amp algorithms; envelope filter |
| 28–29 | 2025 | New Vibe; VCA and JFET compressors; polyphonic pitch tracking |
| 32.00–32.06 | Feb–Jun 2026 | Power-tube grid modelling; HM-2 drive; Transistor power tube |
| **33.00β** | Oct 2026 | **NAM block**; ladder filters; formant-aware pitch |

---

## 9. MVP scope derived from this audit

| Include in v1.0 (Phases 1–5) | Defer to Phase 6+ |
|---|---|
| Grid 6×14, shunts, Input/Output blocks with gate and mixer, Mixer, Volume/Pan, all 6 bypass modes | Plex, Megatap, Ten-Tap |
| Amp (Capture tier + post-EQ), Cab (4 IRs, partitioned convolution), Drive (3+), Gate, Compressor, PEQ, GEQ | Synth, Vocoder, Resonator, Ring Mod, Formant |
| Delay (digital, analog, tape), Multi-delay (quad), Reverb (hall, room, plate, spring) | Dynamic Distortion, MBC, Enhancer |
| Chorus, Flanger, Phaser, Trem/Pan, Wah | Rotary, Vibe |
| Pitch (detune, octave, whammy) | Diatonic harmony, polyphonic capo |
| Scenes (8), channels (A–D), modifiers (LFO, ADSR, envelope, CC, expression), tempo, MIDI mapping | Sequencer, pitch follower, Scene MIDI out |
| Tuner, looper, metronome, CPU meter | Tone Match, RTA, IR capture, DynaCab-style interpolated cab |
| Preset spillover, gapless switching | Circuit amp tier (research track, Phase 7) |

## 10. Open items from the research
- Model counts are lower bounds; the 2026 figures were not published in the sources reached.
- Exact Fractal round-trip latency and preset file size are unpublished.
- FC layout count is either 12 or 8.
- The NAM block is still beta, so its limits may change in 33.00 final.
- The Fractal wiki (per-block and per-amp detail) could not be accessed. **Review it manually** before Phase 6/7 block design; it is the best per-parameter reference.

## Sources
- Product pages: https://www.fractalaudio.com/axe-fx-iii/ · https://www.fractalaudio.com/iii/ · https://www.fractalaudio.com/axe-fx-iii-downloads/
- Blocks Guide (Jun 2023): https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf
- Owner's Manual (Jul 2022) and FC Owner's Manual: http://fractalaudio.com/fcm · Footswitch Functions Guide: https://www.fractalaudio.com/fas-ffg
- Firmware threads:
  - 33.00β: https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-33-00-public-beta-beta-2.222956/
  - 32.00: https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-32-00-release.219159/
  - 27.00: https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-27-00-release.207928
  - 25.00: https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-25-00-release.203482/
  - 22.00: https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-22-00-release.195047/
- Axe-Edit III 1.15: https://forum.fractalaudio.com/threads/axe-edit-iii-1-15-00-to-support-fw-33-00-public-beta.222957/
- Axe-Change: https://axechange.fractalaudio.com/
