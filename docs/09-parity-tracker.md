# 09 — Parity tracker

**Purpose:** one auditable list of every parity item that has been assigned to a work stream, and whether it was actually delivered. A stream's own report ("done") is not proof. An item becomes ✅ only after the merge has been checked against the evidence column. The detailed parameter audits stay in [08](08-blocks-parity.md) (blocks), [06 §1](06-amp-modelling-spec.md#1-axe-fx-iii-amp-block-parameter-parity) (Amp) and [07](07-amp-catalogue.md) (amp models). This file tracks the work, not the audit.

**Sources** (paraphrased, never quoted):
- Fractal's *Blocks Guide* (June 2023);
- the Axe-Fx III amp list (FW 12.05);
- the Fractal wiki pages *Amp models*, *Presets*, *Editors*, *Audio I/O and amplification* and *Cab block* (October 2026).

**Status date:** 24 October 2026, round 3 in progress (T, PS, PA, CB merged and audited) (streams T, M1, M2, PA, PB, PS, ED, IO, CB).

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
| T5 | Optical (bulb/LDR) tremolo: Deluxe Reverb, Twin, Super, Princeton Reverb, Vibroverb, Vibrolux Reverb, Bandmaster, Vibro-King, Blues Junior if any | ✅ | The_lamp_driver_sits_where_the_drawings_measure_it + the T2 tests (8 models) | Amp/StageTremolo.cs; CdS cell values guessed (no datasheet); Blues Junior has none. The mixer's Intensity pot track added (accuracy decision): Vibrato channel −0.5 to −4.5 dB with the tremolo off, Vibro-King up to −5.4 dB; test The_netlists_carry_the_intensity_pot_track |
| T6 | Marshall volume-wiper tremolo: 18 W 1974X, Bluesbreaker 1962, JMP 1959T | ◐ | 18 W in the T2 tests | 18 W built (V3B grid drive). Bluesbreaker: FET preset RV2 setting not on the drawing. 1959T: transistor network illegible |
| T7 | Vox AC15 / AC30 vibrato-tremolo, where the model has the channel | ⏸ | — | AC30 model has no Vib-Trem channel; no readable AC15 drawing |
| T8 | `tremrate` / `tremdepth` drive each model's own oscillator; per-model labels via AmpPanel | ✅ | The_panel_names_the_tremolo | 6G12/6G4 keep their own vibrato knobs; others keep the generic bias trem |
| T9 | Intensity 0 renders bit-identical to master for every model | ✅ | Intensity_zero_is_the_model_without_its_tremolo; 81-model hash comparison against master | the pot-track accuracy fix will change the AB763 family at Intensity 0 by design |
| T10 | Tests: rate range vs component values, depth, no clicks, no allocation, CPU within budget | ✅ | The_onset_is_smooth, The_tremolos_do_not_allocate, The_tremolo_costs_little | the CPU test now times on/off interleaved and asserts the median ratio < 1.25 |

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
| PA1 | Multi-channel audio input: shim (CoreAudio), Cathode.Audio, host, engine config, server device settings | ◐ | HardwareInputTests (Input_maps_parse_one_channel_list_per_source, A_replaced_graph_stops_hearing_the_hardware_inputs, …) | tested through the offline renderer only, never on a multi-input device |
| PA2 | Input node source selects hardware inputs 1–4 / USB (§2) | ✅ | The_input_cell_takes_the_source_it_selects, Changing_the_input_source_crossfades, … | Input cell `source` |
| PA3 | Mux Input Select from hardware inputs (§6.19) | ✅ | The_multiplexer_can_select_a_hardware_input | Mux `input` 7–12 |
| PA4 | Pitch Detector Source / Vocoder Analysis Source from hardware inputs | ✅ | The_pitch_detector_can_track_a_hardware_input; The_vocoder_can_analyse_a_hardware_input | needs PA1 |
| PA5 | Compressor / Gate sidechain from a hardware input | ✅ | A_compressor_sidechain_can_listen_to_a_hardware_input; A_gate_sidechain_can_listen_to_a_hardware_input | old "Input" option renamed "Grid Input" |
| PA6 | Cab: stereo room mic spacing (left/right room IRs per slot) | ✅ | CabRoomAndPreampTests (Spaced_room_mics_differ_in_the_reflections_and_more_so_the_wider_they_are, …) | `micspacing` 0–300 cm, DynaCab slots |
| PA7 | Cab: separate Preamp Mode | ✅ | High_quality_preamp_folds_back_less_than_economy | Preamp Mode = Economy / High Quality (4× oversampled) |
| PA8 | Cab: visual Align view in the editor | ◐ | The_cab_align_page_reports_each_slot_onset_and_delays; cab-align specs | editor Align view not clicked through in the app |
| PA9 | Compressor: JFET, tube and compander algorithms; single-knob compression types | ✅ | CompressorTypesTests (8 tests) | JFET, Tube, Compander, Compression knob |
| PA10 | Graphic EQ: component-level passive types (Mark 5-band, 5/4/3-band, console) | ✅ | PassiveGraphicEqTests | Mark values from docs/06; input resistors and inductor ESR assumed. Amp output EQ keeps the approximations (PB area) |
| PA11 | Pitch: Learn and an editable custom scale | ✅ | PitchLearnAndCustomScaleTests | Learn cannot write the Key param; latches until Key moves |
| PA12 | Plex: Shift Direction per the guide's meaning | ✅ | Reverse_direction_keeps_the_interval_and_plays_each_grain_backwards | the existing behaviour matched the guide |
| PA13 | Rotary: Low Rate Multiplier | ✅ | Low_rate_multiplier_scales_the_drum_speed |  |
| PA14 | Wah: Min/Max Frequency on the circuit types | ✅ | Min_and_max_frequency_move_the_circuits_resonance |  |
| PA15 | Any other ◐ / ❌ in 08 §2, §3, §5, §6 not owned by PB | ◐ | — | closed: Gate/Vocoder/Pitch hardware sources, §7 rows. Left: five newer wah voicings as circuits, classic digital reverb types, Pitch formant correction, legacy factory cab banks (licensing) |

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
| PS1 | Scene Ignore, per channel | ✅ | PresetSceneApiTests.Scene_ignore_holds_a_block_across_scenes_until_its_channel_stops_ignoring | `sceneIgnore` per channel; held blocks in `LiveState.held` |
| PS2 | Kill Dry on Delay, Multitap, Pitch, Plex, Reverb | ✅ | MixLawTests.Kill_dry_leaves_only_the_wet | `killDry` on delay, multidelay, pitch, plex, reverb |
| PS3 | Per-block mix laws (constant power / sweetened / linear / dry 0 dB) | ✅ | MixLawTests.Each_block_follows_its_law (14 blocks) | Delay "sweetened" law is an interpretation (docs/08 §3); goldens chorus/flanger/phaser/reverb/multidelay/tentap regenerated |
| PS4 | Scene Revert setting | ✅ | Scene_revert_recalls_the_saved_scene_and_off_keeps_changes_made_while_playing |  |
| PS5 | Default scene on preset load (As Saved / fixed) | ✅ | A_preset_loads_on_the_scene_it_was_saved_on_or_on_the_global_default_scene |  |
| PS6 | Startup preset | ✅ | The_startup_preset_loads_when_the_server_starts | calls the start-up path directly, no process restart |
| PS7 | Ignore Redundant PC | ✅ | Ignore_redundant_pc_leaves_the_loaded_preset_alone |  |
| PS8 | Copy / swap scenes | ✅ | Scenes_copy_and_swap_and_the_scene_manager_edits_any_scene_with_undo |  |
| PS9 | Swap channels; reset channel; reset block | ✅ | Channels_swap_and_reset_and_a_block_resets_to_its_defaults | channel swap moves data; scenes keep their letters |
| PS10 | Scene Manager view (blocks × scenes) | ✅ | scene-manager spec `sceneRows`; PUT /live/scenes/{n}/blocks/{id} in the scene test | checked in the editor by the stream |
| PS11 | Per-block level meters in telemetry and on the grid | ◐ | Block_peaks_and_loudness_reach_the_telemetry_region; Telemetry_carries_block_meters_by_id_and_the_output_loudness | grid meters seen only on silence (no input on the null backend) |
| PS12 | BS.1770 loudness meter on the output | ✅ | LoudnessMeterTests | −3.01 LUFS for a full-scale 997 Hz sine in one channel |
| PS13 | Preset Leveling tool | ◐ | leveling.ts spec | dialog opens; not measured with real playing |
| PS14 | MIDI: Vol Inc / Vol Dec (scene level ±1 dB + save), Level + Save | ✅ | Midi_steps_the_scene_level_and_saves_and_a_high_scene_cc_steps_to_the_next_scene |  |
| PS15 | MIDI: scene CC values above 63 step through scenes | ✅ | same test | wraps 8 → 1 |
| PS16 | Modifier scope: all channels or one | ✅ | A_modifier_scoped_to_one_channel_drives_its_parameter_only_there; A_disabled_modifier_leaves_its_parameter_until_it_is_turned_on |  |
| PS17 | Global preset-change spillover on/off | ✅ | With_spillover_off_a_preset_change_cuts_the_old_tail_after_the_swap_fade; The_spillover_setting_reaches_the_rig |  |
| PS18 | Grid Looper survives a preset change when both presets have one | ✅ | A_playing_grid_loop_carries_on_into_the_next_preset_when_it_has_a_looper_too; A_loop_stops_when_the_next_preset_has_no_looper |  |
| PS19 | Prompt on unsaved edits before changing presets | ✅ | PresetSwitcher spec `needsConfirm` | checked in the editor by the stream |
| PS20 | Export the preset list as text with scene names | ✅ | The_preset_list_exports_as_text_with_scene_names | GET /presets/list.txt; the export button not clicked |
| PS21 | Recent Presets menu | ◐ | spec `pushRecent` | no component test; menu checked in the editor by the stream |

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
| CB1 | Stereo input: slots 1 and 3 read the left input, 2 and 4 the right | ✅ | A_stereo_input_feeds_slots_one_and_three_from_the_left_and_two_and_four_from_the_right; Two_convolutions_serve_one_side_or_a_mono_input | 4 convolutions when both sides play, 2 otherwise; golden cab.wav changed on purpose |
| CB2 | Preamp types to 11 (FET 1/2, Tape 70/50/35 µs, Vintage, Modern, Exciter) | ✅ | Preamp_types_keep_their_curves_and_their_onsets_mark_one_db_of_compression; Tape_emphasis_saturates_the_treble_first_and_vintage_the_lows; The_exciter_adds_harmonics_to_the_treble_and_leaves_the_bass_clean | 12 types (Cabinets/MicPreampTypes.cs); the old single Tape replaced by the three |
| CB3 | Preamp input VU (0 = onset of clipping) | ✅ | The_preamp_input_meter_reads_zero_at_the_onset_of_clipping | new IBlockMeters → Telemetry.BlockMeters (a second meter path beside PS's Telemetry.blocks) |
| CB4 | Smoothing and Proximity per slot | ✅ | Proximity_lifts_the_lows_of_an_ir_slot_but_not_of_a_dynacab; Smoothing_works_per_slot_on_a_dynacab_too | `mNproximity`, `mNproxfreq`, `mNsmoothing`; globals removed |
| CB5 | Mic distance up to 3.4 m, shown in mm and ms; Auto-Align switch | ✅ | Mic_distance_delays_a_slot_by_up_to_three_point_four_metres; Auto_align_off_delays_a_mic_by_its_distance | `mNdelay` to 9.91 ms, `mNalign` per slot |
| CB6 | Motor Drive (voice-coil thermal compression) with time constant and gain-reduction meter | ✅ | Motor_drive_compresses_as_the_voice_coil_heats_and_is_off_by_default | Cabinets/VoiceCoil.cs; `motor`, `motortime`, `motorReduction` meter |
| CB7 | IR import: minimum-phase / auto-trim / as-is, peak-positive polarity, stereo channel choice, length | ✅ | IrImportTests; An_ir_upload_can_be_trimmed_and_its_polarity_corrected | POST /assets takes processing, polarity, channels, maxLengthMs |
| CB8 | FullRes IRs (~1.4 s) in the Cab block via partitioned convolution | ✅ | Full_res_irs_play_to_about_one_point_four_seconds | 65,536 samples; worst case ~9.7 % (4 slots, Stereo); not gapless for long IRs; non-uniform partitions are the next speed-up |
| CB9 | Flat slot source | ✅ | A_flat_slot_and_a_cab_slot_split_a_stereo_input_into_a_di_and_a_cab |  |
| CB10 | Global "cab modelling off" switch | ✅ | Cab_modelling_off_passes_audio_through_and_fades_back_in | GlobalSettings.CabModelingOff; Cab and IR Player |
| CB11 | Export the block's current mix (DynaCab slots included) as an IR file | ✅ | Export_bakes_the_master_cuts_into_the_mixed_ir; A_cab_exports_its_mixed_ir_and_saves_it_as_an_asset | GET/POST /live/blocks/{id}/ir |

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
| Load-sensitive tests (cab crossfade, CPU-per-type) | ⬜ | Pass alone; flaky under heavy load (the tremolo one is fixed) |
| Preset-load allocation budget raised 128 → 136 → 137.5 KB as blocks grew (HostAllocationTests) | ⬜ | Keeps creeping; find what a load allocates per parameter and cut it |
| Two block-meter telemetry paths (`Telemetry.blocks` peaks, `Telemetry.BlockMeters` named meters) | ⬜ | Could be unified |
| 18 W Intensity rheostat R32 (1k) fixed at mid-travel | ⬜ | Drawing doesn't show which end is 0; link it to `tremdepth` once the orientation is known |
