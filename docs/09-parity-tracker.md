# 09 — Parity tracker

**Purpose:** one auditable list of every parity item that has been assigned to a work stream, and whether it was actually delivered. A stream's own report ("done") is not proof. An item becomes ✅ only after the merge has been checked against the evidence column. The detailed parameter audits stay in [08](08-blocks-parity.md) (blocks), [06 §1](06-amp-modelling-spec.md#1-axe-fx-iii-amp-block-parameter-parity) (Amp) and [07](07-amp-catalogue.md) (amp models). This file tracks the work, not the audit.

**Sources** (paraphrased, never quoted):
- Fractal's *Blocks Guide* (June 2023);
- the Axe-Fx III amp list (FW 12.05);
- the Fractal wiki pages *Amp models*, *Presets*, *Editors*, *Audio I/O and amplification* and *Cab block* (October 2026).

**Status date:** 24 October 2026, round 3 in progress (T merged and audited) (streams T, M1, M2, PA, PB, PS, ED, IO, CB).

## How to audit

| Mark | Meaning |
|---|---|
| ⬜ | Assigned, not delivered yet |
| 🟡 | The stream reports it done; not yet verified after merge |
| ✅ | Verified on master: the evidence exists and passes |
| ◐ | Partly delivered: what is missing is in Notes |
| ❌ | Not delivered: reassign |
| ⏸ | Blocked: the reason is in Notes |

An item is ✅ only when all of these hold on master:
1. The code path exists and is reachable: the parameter is in the descriptor, the endpoint is in `openapi.json`, or the UI control is rendered.
2. A test named in Evidence covers it and passes, or for a pure UI item, it was checked in the running editor.
3. The defaults leave existing presets and models unchanged, unless Notes says otherwise.
4. The relevant docs (08 / 06 / 07 / 02) say the same thing as the code.

When merging a stream, walk its rows. Set each to ✅, ◐, ❌ or ⏸ with evidence; never copy the stream's own claim. Items that end ❌ or ◐ go into the next round's briefs.

## T: amp tremolo and vibrato circuits

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| T1 | Bias tremolo in the power amp: phase-shift oscillator solved from its parts, bias coupling network | ✅ | TremoloTests.The_oscillator_solves_the_textbook_ladder, The_rate_range_follows_the_drawing | Amp/Tremolo.cs; swing reaches the grids through the real coupling network |
| T2 | Bias tremolo on Deluxe 6G3, Vibrolux 6G11 | ✅ | Full_intensity_modulates_the_note_at_the_rate, Intensity_zero_is_the_model_without_its_tremolo (Deluxe 6G3, Vibrolux 6G11) | 2.9–10.8 Hz; 9 / 26 dB depth |
| T3 | Gibson Scout GA-17RVT tremolo (type checked against the drawing) | ✅ | same tests (Scout GA-17RVT) | a bias tremolo; 2.9–5.1 Hz; panel label Depth |
| T4 | Other models whose drawings show bias modulation (Tremolux AA763, Princetons, AC15: check each) | ✅ | same tests (Princeton AA964, Princeton Reverb) | Tremolux is optical (T5); AC15 has no readable drawing |
| T5 | Optical (bulb/LDR) tremolo: Deluxe Reverb, Twin, Super, Princeton Reverb, Vibroverb, Vibrolux Reverb, Bandmaster, Vibro-King, Blues Junior if any | ✅ | The_lamp_driver_sits_where_the_drawings_measure_it + the T2 tests (8 models) | Amp/StageTremolo.cs; CdS cell values guessed (no datasheet); Blues Junior has none. The mixer's Intensity pot track (~4 dB) is being added, approved for accuracy |
| T6 | Marshall volume-wiper tremolo: 18 W 1974X, Bluesbreaker 1962, JMP 1959T | ◐ | 18 W in the T2 tests | 18 W built (V3B grid drive). Bluesbreaker: FET preset RV2 setting not on the drawing. 1959T: transistor network illegible |
| T7 | Vox AC15 / AC30 vibrato-tremolo, where the model has the channel | ⏸ | — | AC30 model has no Vib-Trem channel; no readable AC15 drawing |
| T8 | `tremrate` / `tremdepth` drive each model's own oscillator; per-model labels via AmpPanel | ✅ | The_panel_names_the_tremolo | 6G12/6G4 keep their own vibrato knobs; others keep the generic bias trem |
| T9 | Intensity 0 renders bit-identical to master for every model | ✅ | Intensity_zero_is_the_model_without_its_tremolo; 81-model hash comparison against master | the pot-track accuracy fix will change the AB763 family at Intensity 0 by design |
| T10 | Tests: rate range vs component values, depth, no clicks, no allocation, CPU within budget | ✅ | The_onset_is_smooth, The_tremolos_do_not_allocate, The_tremolo_costs_little | the CPU test is load-sensitive (failed 1 in 3 alone on a busy machine); being made robust |

## M1: remaining Marshalls

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| M1.1 | DSL100 / JCM2000: Classic Gain and Ultra Gain, modes, Tone Shift, Deep | ⬜ | | inter-board wiring had to be traced |
| M1.2 | JMP-1 preamp: OD1, OD2, Clean (and Crunch) + documented power amp | ⬜ | | TC9176 digital pots |
| M1.3 | JVM410HJS (Satriani) as its own model | ⬜ | | input buffer, THAT4301 gate |
| M1.4 | JVM410H Clean and Crunch channels | ⬜ | | |
| M1.5 | JVM410H Green / Orange / Red modes per channel | ⬜ | | relay states per mode were unknown |

## M2: power-amp driver variants

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| M2.1 | Cathodyne + ECC82 push-pull driver design (no cathode followers) | ⬜ | | validated against ReferenceSimulator |
| M2.2 | Global feedback into a preamp cathode (tone stack inside the loop) | ⬜ | | |
| M2.3 | Marshall Major 1967 model (4 × KT88) | ⬜ | | |
| M2.4 | Cascaded differential driver design | ⬜ | | |
| M2.5 | Mesa/Boogie Bass 400 model, with its LC graphic EQ | ⬜ | | scan partly unreadable: guesses marked |
| M2.6 | Existing inverters bit-identical | ⬜ | | |

## PA: I/O, cab and effects

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| PA1 | Multi-channel audio input: shim (CoreAudio), Cathode.Audio, host, engine config, server device settings | ⬜ | | default stays one stereo pair |
| PA2 | Input node source selects hardware inputs 1–4 / USB (§2) | ⬜ | | needs PA1 |
| PA3 | Mux Input Select from hardware inputs (§6.19) | ⬜ | | needs PA1 |
| PA4 | Pitch Detector Source / Vocoder Analysis Source from hardware inputs | ⬜ | | needs PA1 |
| PA5 | Compressor / Gate sidechain from a hardware input | ⬜ | | needs PA1 |
| PA6 | Cab: stereo room mic spacing (left/right room IRs per slot) | ⬜ | | the last ❌ in 08 §1.1 |
| PA7 | Cab: separate Preamp Mode | ⬜ | | |
| PA8 | Cab: visual Align view in the editor | ⬜ | | |
| PA9 | Compressor: JFET, tube and compander algorithms; single-knob compression types | ⬜ | | |
| PA10 | Graphic EQ: component-level passive types (Mark 5-band, 5/4/3-band, console) | ⬜ | | replaces the approximations |
| PA11 | Pitch: Learn and an editable custom scale | ⬜ | | |
| PA12 | Plex: Shift Direction per the guide's meaning | ⬜ | | |
| PA13 | Rotary: Low Rate Multiplier | ⬜ | | |
| PA14 | Wah: Min/Max Frequency on the circuit types | ⬜ | | |
| PA15 | Any other ◐ / ❌ in 08 §2, §3, §5, §6 not owned by PB | ⬜ | | list them here on merge |

## PB: Drive and the Amp block's advanced page

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| PB1 | Drive: per-type extra knobs from each pedal's schematic | ⬜ | | |
| PB2 | Drive: remaining clip types (FET, rectifier, variable, HV/LV tube, Si, …) | ⬜ | | |
| PB3 | Drive: per-op-amp slew in the WDF op-amp model | ⬜ | | |
| PB4 | Dynamic Distortion: remaining ◐ | ⬜ | | |
| PB5 | Amp: power-tube hardness (pentode law) | ⬜ | | |
| PB6 | Amp: generic Presence Shift, Fat, Cut, High Treble, Overdrive switches where a model has no circuit | ⬜ | | |
| PB7 | Amp: tonestack type substitution and location | ⬜ | | |
| PB8 | Amp: triode plate frequencies, preamp hardness, cathode follower controls | ⬜ | | |
| PB9 | Amp: PI and power-tube bias excursion, plate suppressor diodes | ⬜ | | |
| PB10 | Amp: master volume location / cap | ⬜ | | |
| PB11 | Amp: output mode (FRFR / SS PA + cab), auto DynaCab impedance | ⬜ | | |
| PB12 | Amp: screen frequency / Q | ⬜ | | |
| PB13 | All 81 models bit-identical at defaults (a test) | ⬜ | | |

## PS: presets, scenes and routing (wiki *Presets*)

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| PS1 | Scene Ignore, per channel | ⬜ | | |
| PS2 | Kill Dry on Delay, Multitap, Pitch, Plex, Reverb | ⬜ | | |
| PS3 | Per-block mix laws (constant power / sweetened / linear / dry 0 dB) | ⬜ | | changes mid-Mix sound: goldens to list |
| PS4 | Scene Revert setting | ⬜ | | |
| PS5 | Default scene on preset load (As Saved / fixed) | ⬜ | | |
| PS6 | Startup preset | ⬜ | | |
| PS7 | Ignore Redundant PC | ⬜ | | |
| PS8 | Copy / swap scenes | ⬜ | | |
| PS9 | Swap channels; reset channel; reset block | ⬜ | | |
| PS10 | Scene Manager view (blocks × scenes) | ⬜ | | |
| PS11 | Per-block level meters in telemetry and on the grid | ⬜ | | |
| PS12 | BS.1770 loudness meter on the output | ⬜ | | |
| PS13 | Preset Leveling tool | ⬜ | | |
| PS14 | MIDI: Vol Inc / Vol Dec (scene level ±1 dB + save), Level + Save | ⬜ | | |
| PS15 | MIDI: scene CC values above 63 step through scenes | ⬜ | | |
| PS16 | Modifier scope: all channels or one | ⬜ | | |
| PS17 | Global preset-change spillover on/off | ⬜ | | |
| PS18 | Grid Looper survives a preset change when both presets have one | ⬜ | | |
| PS19 | Prompt on unsaved edits before changing presets | ⬜ | | |
| PS20 | Export the preset list as text with scene names | ⬜ | | |
| PS21 | Recent Presets menu | ⬜ | | |

## ED: editor workflow (wiki *Editors*)

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| ED1 | Shortcuts: presets (P, ⌘PgUp/PgDn, ⌘G, F5), scenes (⌘1–8), channels (A–D, X) | ⬜ | | |
| ED2 | Shortcuts: blocks (Space, ⇧Space, ⇧⌘Space, ⌘X, ⌘D, ⌘U, ⌘B, G, Q, ⌥/F1), ⌘T, ⌘Y | ⬜ | | ⌘I / ⌘L hook into PS |
| ED3 | `?` help overlay listing every shortcut | ⬜ | | |
| ED4 | Preferences: knob mode (linear/circular), wheel (normal/reverse/off), tooltips, meter refresh | ⬜ | | |
| ED5 | Revert preset; Clear preset | ⬜ | | |
| ED6 | Snapshots (timestamped, server-side, load/delete) | ⬜ | | |
| ED7 | Templates: New from template, Save as template | ⬜ | | |
| ED8 | Replace block type keeping cables | ⬜ | | |
| ED9 | Drag-onto swap, ⌘-drag replace; move row / column | ⬜ | | |
| ED10 | Cut; paste onto same-type block copies settings; disconnect all cables | ⬜ | | |
| ED11 | All-scenes actions: channel, engage, bypass | ⬜ | | |
| ED12 | Block Info view; amps-in-use summary | ⬜ | | |
| ED13 | Circuit Amp Ideal / Authentic view | ⬜ | | |
| ED14 | Library: single channel or whole block (with modifiers); recall into channel or all | ⬜ | | |
| ED15 | Library: folders, file export/import, drag-and-drop, save all blocks of a preset | ⬜ | | |
| ED16 | Manage Presets / IRs: multi-select, drag swap/move/copy, pending changes until Save, duplicate/rename/delete | ⬜ | | |
| ED17 | Reset Amp blocks on selected presets (keeps the real amp's panel controls) | ⬜ | | |
| ED18 | Parameter Batch Set | ⬜ | | |
| ED19 | Pickers: colour tags / favourites, keyboard search, pin | ⬜ | | |
| ED20 | Performance pages: global and per-preset | ⬜ | | |
| ED21 | Footswitches: layout links, views, per-preset overrides | ⬜ | | |

## IO: audio inputs and outputs (wiki *Audio I/O and amplification*)

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| IO1 | Output 2–4 blocks on the grid, each mapped to device output channels | ⬜ | | today there is one output |
| IO2 | Copy Output 1 → Output 2 setting; an Output block on the grid overrides it | ⬜ | | |
| IO3 | Per-output mode: Stereo / Sum L+R / Copy L→R / Dual Mono | ⬜ | | |
| IO4 | Per-output global EQ and level trim (Boost/Pad) | ⬜ | | today one global EQ |
| IO5 | Input calibration: full-scale volts or dBu per input, so amp gain doesn't depend on the interface gain | ⬜ | | Circuit Amp treats 0 dBFS as 1 V |
| IO6 | Input clip warning (within 0.5 dB of full scale) and a global Input 1 Gain trim for all presets | ⬜ | | |
| IO7 | Input impedance on the Input block: pickup resonance loaded by the chosen impedance and cable capacitance | ⬜ | | |
| IO8 | Dry DI out: the unprocessed input on spare device channels for re-amping | ⬜ | | |
| IO9 | Oversampling mode: linear-phase (current) or minimum-phase for Amp and Drive; a Drive per-block setting | ⬜ | | |
| IO10 | Latency report in Setup: converters, buffer and per-block latency | ⬜ | | |
| IO11 | Output clip warning at −1 dBFS | ⬜ | | |
| IO12 | Editor hint when stereo effects sit before a mono-summing block | ⬜ | | |
| IO13 | Cab resonance helper: guided sine sweep that writes the amp's LF Resonance | ⬜ | | |

## CB: Cab block (wiki *Cab block*)

| ID | Item | Status | Evidence | Notes |
|---|---|---|---|---|
| CB1 | Stereo input: slots 1 and 3 read the left input, 2 and 4 the right | ⬜ | | today every slot hears both sides |
| CB2 | Preamp types to 11 (FET 1/2, Tape 70/50/35 µs, Vintage, Modern, Exciter) | ⬜ | | PA7 owns the High Quality mode |
| CB3 | Preamp input VU (0 = onset of clipping) | ⬜ | | |
| CB4 | Smoothing and Proximity per slot | ⬜ | | today both are global |
| CB5 | Mic distance up to 3.4 m, shown in mm and ms; Auto-Align switch | ⬜ | | today the delay stops at 5 ms |
| CB6 | Motor Drive (voice-coil thermal compression) with time constant and gain-reduction meter | ⬜ | | |
| CB7 | IR import: minimum-phase / auto-trim / as-is, peak-positive polarity, stereo channel choice, length | ⬜ | | |
| CB8 | FullRes IRs (~1.4 s) in the Cab block via partitioned convolution | ⬜ | | today 8,192 samples |
| CB9 | Flat slot source | ⬜ | | |
| CB10 | Global "cab modelling off" switch | ⬜ | | |
| CB11 | Export the block's current mix (DynaCab slots included) as an IR file | ⬜ | | |

## Known open items from earlier rounds

These were reported as left over or deliberately deferred, and are not assigned to a stream yet.

| Item | Status | Notes |
|---|---|---|
| Silver Jubilee over the high-gain CPU budget on loud noise (28–31.5 %) | ⏸ | Kept by decision (docs/06 §5.3): its sound wins |
| SVT with Ultra switches on: 37 % on noise | ⬜ | Next idea: source stepping on hard samples |
| TriAxis Lead 2 over budget (19 / 32 %); Rhythm over the clean budget | ⬜ | Dynamic Voice op-amp stage ≈ 2.4 %; could become a linear network |
| JCM900 ch B over on guitar, ch A over on noise; JVM410H slightly over on guitar | ⬜ | V1 pair with the 220k loop is 6–10 % alone |
| Orange AD30 | ⏸ | No front-panel board schematic found |
| Dumble ODS HRM | ⏸ | No HRM drawing exists |
| Bassman 135 first stage 4 dB short of the drawing's test voltages | ⬜ | Unresolved; pot tapers guessed |
| Subway Blues toning module | ⏸ | Potted, undrawn |
| TriAxis presence LDR as a panel control | ⬜ | Block has no free control left |
| Mesa Rectifier orange channels and channels 1–2 | ⬜ | |
| Cornford MK50H II second master | ⬜ | |
| Tiny Terror post-inverter master and grid-side tone | ⬜ | Tone is approximated, labelled "Cut" |
| Valvestate current feedback | ⬜ | |
| JCM900 Output Power Select; 4100 US (4 × 5881) | ⬜ | |
| Real OT primaries for the brownface/Bassman/Scout models | ⏸ | Need measurements or datasheets |
| Pitch has two row-input mechanisms (`IRowInputBlock`, `ISidechainBlock`) | ⬜ | Could be unified |
| Library-wide open issues: TriodeStage output resistance with a partly bypassed cathode; Thévenin cut behind plate-bypass cold clippers; JC-120 tone-stack level | ⬜ | docs/06 |
| Windows `HostAllocationTests` intermittent 4-byte allocation | ⬜ | Per-thread allocation measured across awaits |
| Load-sensitive tests (cab crossfade, CPU-per-type) | ⬜ | Pass alone; flaky under heavy load |
