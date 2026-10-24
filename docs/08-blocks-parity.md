# 08 — Blocks parity with the Axe-Fx III Blocks Guide

**Purpose:** compare every Cathode block, parameter by parameter, with Fractal Audio's *Blocks Guide* (June 2023, Axe-Fx III / FM9 / FM3, firmware ~22/23), record what this pass implemented, and plan what is left.

**Source:** https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf (101 pages). Page numbers below are the guide's printed page numbers. The guide's text is summarised and paraphrased here, never quoted; parameter names are given so they can be found in it.

**Status date:** October 2026, after the parity pass on this branch. [01](01-axe-fx-iii-audit.md) is the broader feature audit; [06 §1](06-amp-modelling-spec.md#1-axe-fx-iii-amp-block-parameter-parity) is the detailed Amp audit, summarised (not repeated) here.

## Legend

| Mark | Meaning |
|---|---|
| ✅ | Present: same control and behaviour (names may differ) |
| ◐ | Partial: present with a narrower range, fewer options, a different law, or only on some types |
| ❌ | Missing |
| — | Not applicable to Cathode (hardware, front-panel UI, or a deliberate design difference) |
| 🆕 | Added in this pass |

**Effort** for a gap: **cheap** = under about an hour of generic DSP plus a test; **medium** = a day or two, new state or a new algorithm, or touching the engine or the editor; **large** = a new algorithm family, research, reference data or several days.

---

## 1. Summary

### 1.1 Blocks

The guide's comparison table (p. 3) lists 42 block types. Cathode covers 37 of them with a block, 3 under another name or as a type of another block, and 2 not at all. It has 3 blocks the guide does not (Tone Stack, Vibe, FX Loop).

| Guide block | Cathode | Types (guide → Cathode) | Parameters ✅ / ◐ / ❌ | Verdict |
|---|---|---|---|---|
| Input (IN) | built-in `input` | 1 → 1 | 🆕 5 / 0 / 0 | 🆕 Noise gate (Classic, Intelligent, Noise Reducer), input level, global gate offset, channels; 🆕 Source: Input 1–4, USB 5/6 and 7/8 from a multi-channel interface |
| Output (OUT) | built-in `output` | 1 → 1 | 🆕 4 / 0 / 0 | 🆕 Row mixer (level, balance per row), main level and balance, channels; scene levels |
| Common Mix/Level | every block | — | 🆕 10 / 0 / 0 | Level and Balance everywhere; 🆕 Global Mix, engage as a modifier target, Stereo Spread to ±200 %, Input Select/Gain complete |
| Amp (AMP) | `circuitamp`, `nam` | 309+ → 26 circuit + any capture | see [06 §1.4](06-amp-modelling-spec.md#14-parity-summary) | Core panel, expert pages and 🆕 boost, saturation, input filter/Definition, output EQ with location, input dynamics, output compressor, speaker drive/thump/compression, bias tremolo, Balance; 🆕 the advanced page (PB): tube hardness, generic switches, stack type and location, plate frequencies, CF and bias-excursion controls, suppressor diodes, master location and cap, output mode, Auto speaker, screen supply |
| Cab (CAB) | `cab` (Legacy and DynaCab merged) | 2 150+ IRs → user IRs + synthesised DynaCab + IR packs + Flat | 🆕 24 / 1 / 0 | 🆕 Done: stereo room mic spacing, Preamp Mode, the Align view (stream PA); stereo input routing, twelve preamp types and their input meter, per-slot proximity and smoothing, 3.4 m mic distance with Auto-Align, Motor Drive, FullRes IRs, Flat, IR import processing, a global cab-off switch and IR export (stream CB); factory banks stay out (licensing) |
| Chorus (CHO) | `chorus` | 17 → 1 (+ 🆕 12 library presets) | 🆕 20 / 0 / 0 | 🆕 Done: 8 voices, Dual ratios, BBD drive, Dimension, LFO 2, Global Mix; the types as Block Library presets |
| Compressor (CMP) | `compressor` | 16 → 🆕 9 | 🆕 20 / 0 / 0 | 🆕 Detector, ∞ ratio, Dynamics and Feedback types, auto times, light type, emphasis, sidechain EQ, input level, look-ahead and sidechain source; 🆕 JFET, Tube and Compander types, the single Compression knob, sidechain from a hardware input |
| Crossover (XVR) | `crossover` | 1 → 1 | 🆕 7 / 0 / 0 | Done (🆕 per-side band levels and pans) |
| Delay (DLY) | `delay` | 23+ → 🆕 9 | 🆕 48 / 0 / 0 | 🆕 Done: Dual, Mono Tape, Reverse and Sweep types, 16 s, master and signed feedback, feedback EQ, three LFOs, diffuser, Stack, Global Mix, Spread to ±200 % |
| Drive (DRV) | `drive` | 60+ → 64 | 🆕 22 / 1 / 0 | Strong circuits; 🆕 EQ pages and the Advanced page, all the guide's clip types, Slew Rate per op-amp; 🆕 round 4: D1 ten overdrives, D2 15 distortions and amp-in-a-box circuits (Metal Zone, Fat Rat, Suhr Riot ×3, DRV, Crunch Box, B7K, Xotic ACB/Bass RCB, FAS LED-Drive, Ruckus, Shimmer Drive, Tube Driver ×2) with a triode stage and an active band EQ, D3 twelve fuzzes, boosts, preamps, Tape Distortion and Bit Crusher; per-pedal extra knobs on the Guv'nor's stack, the Timmy (Bass, Treble), the Zendrive (Voice), the OCD (HP/LP), the active EQs' bass/middle pots, the Metal Zone's Middle Freq and the B7K's Grunt/Attack |
| Dynamic Distortion (DND) | `dyndist` | 7 → 🆕 14 curves + 7 library presets | 🆕 10 / 0 / 0 | Different design (envelope-driven); 🆕 the guide's frequency-shaped filter, bias, mono; 🆕 the types as filter presets |
| Enhancer (ENH) | `enhancer` | 3 → 🆕 4 | 🆕 10 / 0 / 0 | Done (🆕 Stereoizer, Depth) |
| Filter (FLT) | `filter` | 15 → 🆕 15 | 🆕 21 / 0 / 0 | 🆕 Done: Shelf 2, Peaking 2, all-pass feedback, first-order cuts, LFO duty and quantise |
| Flanger (FLG) | `flanger` | 31 → 1 | 🆕 24 / 0 / 0 | 🆕 Done: thru-zero, drive, bass focus, VCO response, min/max time, LFO reset on engage, Global Mix |
| Formant (FOR) | `formant` | 1 → 1 | 8 / 0 / 0 | Done (Cathode adds Shift) |
| Gate/Expander (GTE) | `gate` | 4 → 🆕 4 | 🆕 15 / 0 / 0 | Done (🆕 Modern types, knee, sidechain from a row or the input) |
| Graphic EQ (GEQ) | `geq` | 16 → 🆕 10 layouts × constant/variable Q | 🆕 7 / 0 / 0 | 🆕 Band layouts, outer shelves; 🆕 the passive types as component-level networks |
| IR Player (IRP) | `irplayer` | 1 → 1 | 🆕 11 / 0 / 0 | Done (🆕 two slots, modes, length, per-IR level and pan, slopes, input select) |
| Looper (LPR) | 🆕 `looper` grid block + rig looper | 1 → 1 | 🆕 18 / 0 / 0 | Done: placeable with bypass modes and the full Config page; the transport drives the block while its preset runs |
| Megatap (MGT) | `megatap` | 20 → 1 | 🆕 23 / 0 / 0 | 🆕 Done: 128 taps, pre-delay, tap feedback, alphas, randomise, diffusion, envelope, Global Mix |
| Mixer (MIX) | 🆕 `mixer` | 1 → 1 | 🆕 6 / 0 / 0 | Done in this pass |
| Multitap Delay (MTD) | `multidelay` | 34+ → 🆕 5 | 🆕 42 / 0 / 0 | 🆕 Done: Quad Tap, Parallel, Series, Diffusor, Tape with per-voice feedback, filter, chorus, comb and ring mod; Global Mix |
| Multiband Comp (MBC) | `mbcomp` | 1 → 1 | 🆕 15 / 0 / 0 | Done (🆕 detector, mutes, 12/24 dB slope, auto times) |
| Multiplexer (MUX) | `mux` | 1 → 1 | 🆕 4 / 0 / 0 | 🆕 Done: rows and the hardware inputs |
| Parametric EQ (PEQ) | `peq` | 1 → 1 | 🆕 8 / 0 / 0 | Done (🆕 Shelving 2, Peaking 2, slopes to 36 dB/oct, Solo) |
| Phaser (PHA) | `phaser` (+ `vibe`) | 16 → 🆕 2 (+ Vibe) | 🆕 24 / 0 / 0 | 🆕 Done: VCR types, Barberpole, tone, feedback point, LFO conditioning, LFO reset on engage, Global Mix |
| Pitch (PIT) | `pitch` | 16 → 9 | 🆕 47 / 0 / 0 | 🆕 Done: polyphonic whammy, per-voice detune and feedback, master scalers, stereo input, temperament, detector source, 16-step arp with shapes, Global Mix, input select; 🆕 Learn, custom scale, detector on a hardware input |
| Plex Delay (PLX) | 🆕 `plex` | 44 → 🆕 5 | 🆕 29 / 0 / 0 | Done: 1–8 lines, Plex Delay, Detune, Shifter, Verb and Shimmer Verb |
| Resonator (RES) | `resonator` | 1 → 1 | 🆕 20 / 0 / 0 | 🆕 Done: per-voice feedback and band-pass, masters, stereo input, Global Mix |
| Return (RTN) | `fbreturn` | 1 → 1 | 🆕 3 / 0 / 0 | Done |
| Reverb (REV) | `reverb` | 66+ → 🆕 7 | 🆕 36 / 0 / 0 | 🆕 Done: early reflections, band decay times, wet EQ, quality, stack, shimmer pitch section, 1–6 springs, Chamber and Cathedral, Global Mix |
| Ring Modulator (RNG) | `ringmod` | 3 → 🆕 3 | 🆕 7 / 0 / 0 | 🆕 Done: upper and lower sideband, pitch tracking |
| Rotary (ROT) | `rotary` | 1 → 1 | 🆕 18 / 0 / 0 | 🆕 Done: brake, tempo, rotor length, mic spacing and distance, input select, spread, 🆕 low rate multiplier |
| RTA | 🆕 `rta` block + analyser (`PUT /rta`) | 1 → 1 | 🆕 5 / 0 / 0 | 🆕 Pass-through grid block, input select; bands, decay and window on the analyser |
| Scene MIDI (MIDI) | 🆕 per-scene MIDI messages | 1 → 1 | 🆕 4 / 0 / 0 | 🆕 Eight PC/CC messages per scene, sent on scene change and preset load |
| Send (SND) | `fbsend` | 1 → 1 | 1 / 0 / 0 | Done (Cathode adds a send level); 🆕 two Send/Return pairs |
| Synth (SYN) | `synth` | 1 → 1 | 🆕 20 / 0 / 0 | 🆕 Done: three voices with tracking modes, noise shapes, per-voice filter, duty and pan, Global Mix |
| Ten-Tap Delay (TTD) | 🆕 `tentap` | 2 → 🆕 2 | 🆕 17 / 0 / 0 | Done: Ten Tap and Rhythm Tap (quantise, learn), tap count and levels, mono/stereo, pan shape |
| Tone Match (TMA) | `tonematch` | 1 → 1 | 2 / 0 / 0 | Done (the guide defers to a separate manual) |
| Tremolo/Panner (TRM) | `tremolo` | 5 → 5 | 🆕 13 / 0 / 0 | 🆕 Done: width to 200 %, trigger, crossover slope, start phase on engage |
| Vocoder (VOC) | `vocoder` | 1 → 1 | 🆕 18 / 0 / 0 | 🆕 Done: 4–24 bands, range, resonance, shift, freeze, per-band level and pan, analysis from a row |
| Volume/Pan (VOL) | `volpan` | 1 → 1 | 🆕 10 / 0 / 0 | Done in this pass |
| Wah (WAH) | `wah` | 9 → 🆕 10 | 🆕 14 / 0 / 0 | 🆕 Done: five more voicings, drive, inductor bias, EQ page; 🆕 min/max frequency on the circuits too |

**Totals** (the counts above, summed over the 42 audited blocks and pages, Amp excluded), after the four parity streams of October 2026 (P1–P4): about **646 / 16 / 1** (✅ / ◐ / ❌), i.e. **about 97 % of the guide's parameters fully present and nearly all of the rest in part**, up from about 64 % after the first pass and 42 % before it. The one ❌ was the Cab's stereo room mic spacing (🆕 done by stream PA); the ◐ rows are listed per block in §6. Type coverage is now closed mostly by the Block Library's factory presets (146 over 19 blocks) on top of the algorithms here, rather than by one algorithm per guide type.

### 1.2 What this pass implemented

All with defaults that leave the earlier sound bit-identical, except the Delay mix law (a deliberate change, golden regenerated).

- **Common:** a shared `OutputStage` (Level + 🆕 **Balance**) on every effect block with a level (31 blocks), plus Level and Balance on Gate and Mixer, Output Level and Balance on Feedback Return. Shared helpers for Input Select, Stereo Spread, phase reverse/invert and per-side pans (`Stereo`), a wet-path low/high cut (`WetCuts`) and a ducker (`Ducker`).
- **Bypass modes:** 🆕 **Mute Out** and **Mute FX Out** (the block keeps running; its output is muted or replaced by the dry), so all six of the guide's modes exist. Server validation, API text and the editor's mode list updated.
- **LFO waveforms (p. 99):** 🆕 Saw Up, Saw Down, Random (sample-and-hold, repeatable after a reset), Log, Exponential, Trapezoid and Astable join Sine, Triangle and Square, with a **duty** control (triangle/square/trapezoid symmetry, astable curvature). The rig LFOs offer the four new shapes too.
- **Tempo (p. 100):** one shared division list (`TempoDivisions`) with 12 new values appended (1/2., 1/2T, 1/16., 1/16T, 1/32, 1/32., 1/32T, 1/64, 1/64T, 2, 3 and 4 bars); used by Delay, Megatap, Multi-Delay, the rig controllers and the new tempo parameters.
- **Tremolo/Panner:** 🆕 types Bias Trem, Harmonic Trem, Optical Trem; LFO type (all ten), duty, LFO phase, tempo, Pan Center, crossover frequency.
- **Chorus:** 🆕 tempo, LFO type, 1–4 voices, wet low/high cut, phase reverse. **Flanger:** 🆕 tempo, LFO type, wet low/high cut, phase reverse. **Phaser:** 🆕 tempo, LFO type.
- **Filter:** 🆕 types Null, All Pass, Tilt EQ, FF Comb, FB Comb; Order (2nd/4th); low and high cut; Pan L/R; phase invert; comb time and depth; a built-in LFO (enable, type, rate, tempo, Mod Frequency).
- **Volume/Pan:** 🆕 Volume with seven pot tapers (Linear, Log 30A–5A, S-taper), Auto-Swell type with threshold and attack, Input Select, Pan L/R.
- **Gate:** 🆕 Gate type with Attenuation, sidechain low/high cut, Peak/RMS detector, Level, Balance.
- **Compressor:** 🆕 Sustainer type (upward compression), Auto Makeup, sidechain low/high cut.
- **Delay:** 🆕 types Ping-Pong and Vintage Digital; Input Gain, L/R time ratio, Drive, Bit Reduction, Spread, Ducker (attenuation, threshold, release), Repeat Hold, phase reverse; the guide's **delay mix law** (wet reaches full level at 50 %, then the dry fades).
- **Reverb:** 🆕 Input Gain, Input Select, Stereo Spread, Ducker, Hold.
- **Megatap, Multi-Delay:** 🆕 Input Gain and Input Select. **Resonator:** 🆕 Input Gain.
- **PEQ:** 🆕 Blocking (high-/low-pass) band types for bands 1 and 5. **Graphic EQ:** 🆕 Master Q and a Variable Q mode.
- **Ring Modulator:** 🆕 frequency multiplier. **Enhancer:** 🆕 invert, Pan L/R. **Wah:** 🆕 resonance (Q), Q tracking, control taper, low cut.
- **Mixer block** (🆕 `mixer`): six rows with gain and balance, stereo or mono output, output level and balance. It uses the Multiplexer's per-row routing, now an interface (`IRowInputBlock`).

#### Engine and global stream (P1)

- **On engage** (§8 item 12): an optional `IEngageAware` interface; the compiled graph calls it on the audio thread when a block's bypass target turns from bypassed to active. 🆕 **LFO Bypass Reset** on Flanger and Phaser, 🆕 **Start Phase** on Tremolo.
- **Global Mix** (§8 item 1): a global Effects Mix offset (±50 %) and a gate offset in the global settings, reaching blocks through `BlockContext`; a 🆕 `globalMix` switch on Chorus, Delay, Flanger, Megatap, Multi-Delay, Phaser, Pitch, Resonator, Reverb and Synth (`EffectsMix` helper).
- **Input and Output nodes** (§8 item 2): the compiler now runs an `input` block after reading the input (🆕 noise gate: Off, Classic, Intelligent with hum notches, Noise Reducer; input level; the global gate offset) and hands an `output` block each row (🆕 row level and balance, main level and balance; one row is scaled in place). Both have channels; neither can be bypassed.
- **Scene MIDI** (§8 item 3): 🆕 up to eight PC/CC messages per scene, sent through the MIDI output when the scene is selected or its preset loads; edited on the Controllers page.
- **Sidechain** (§8 item 10): 🆕 `ISidechainBlock`; Compressor and Gate take a Sidechain source (the block, the grid input, or rows 1–6: the last live block on that row to the left).
- **Look-ahead** (§8 item 11): 🆕 Compressor Look Ahead (0.5–10 ms) reported as latency; a parameter flagged `Recompile` makes the server rebuild the graph (a gapless swap) when it changes.
- **Type presets** (§8 item 7): 🆕 the **Block Library**: 134 factory presets over 17 block types (chorus, flanger, phaser, delay, multi-delay, megatap, reverb, compressor, gate, filter, graphic EQ, wah, pitch, tremolo, ring mod, dynamic distortion, drive), user presets saved from a block's channel, applied to a channel from the block page.
- **Common** (§3): 🆕 Input Select on Pitch (P3 added the Rotary's and Pitch Input Gain), Stereo Spread to ±200 % (a side gain) on Delay, Reverb, Chorus and Flanger and added to Phaser; 🆕 a modifier can drive a block's **engage** (bypassed below 50 %).
- **Routing:** 🆕 two Feedback Send/Return pairs, matched by the number their ids end in; the 🆕 `rta` block taps the analyser anywhere on the grid, and the RTA takes 3/6/12 bands per octave, a decay time and a window.

**Stream P3 (modulation, pitch and synthesis), second pass.** Every ❌ and ◐ row of §6.1, 6.8, 6.9, 6.21, 6.22, 6.25, 6.27, 6.28, 6.31, 6.34, 6.35 and 6.37 is now 🆕 ✅, except what belongs to stream P1 (Global Mix, LFO Bypass Reset and Tremolo Start Phase, since done by P1 through `IEngageAware`), the Pitch block's Learn and custom-scale rows (covered by Key Source Auto and the Custom Shifter tables) and the Wah's Min/Max Frequency on the two component-level circuits (their range is set by their parts; Inductor Bias moves it). Defaults leave every block's sound as it was: the golden renders still match. Shared pieces, all new files: `LfoConditioner` (LFO quantise, hicut, auto depth), `SoftDrive`, `FirstOrderCuts`, `HilbertPair` (single sideband), `AllpassDiffuser`, and in `Pitch/` the `PhaseVocoderShifter` (peak-locked, for the polyphonic Whammy), plus a splice length on `SpliceShifter`, a variable grain on `GrainShifter`, `Scales.JustRatio`, `WahCircuit.Trim` (inductance and R7) and an optional shelf-Q limit on `SvfCoefficients.Design`. The Vocoder and the Pitch block now implement `IRowInputBlock`, so a chosen grid row can be their analysis or detector signal (docs/02 §5.3). The Synth's parameters are regrouped per voice and the Pitch block's grew past its old layout (no preset compatibility is kept). Also from this pass, outside the stream's rows: Input Select on the Rotary, Input Gain on the Pitch block, and Stereo Spread on the Chorus, Flanger and Rotary (§3).

**Time-based stream (P2, later pass).** Every ❌/◐ row of §6.4, 6.14, 6.15, 6.17, 6.23, 6.26 and 6.32 (Global Mix, P1's, is now done too). Defaults keep the earlier sound of Delay, Megatap, Multitap (Quad Tap), Reverb and the rig looper; their golden renders are unchanged. New shared helpers: `LoopFilter` (per-sample feedback-path cuts of 6–24 dB/oct with resonance, plus two peaking bands), `Diffuser` (a modulated four-stage allpass with a mix), `SwellEnvelope` (the Envelope page), `DelayBlock.Limit` (the loop limiter: transparent to full scale, a tanh knee that never passes 2), `TypeLabelling` (per-type pages through `typeLabels`), and a crossfade on `GrainShifter`.

- **Delay:** 🆕 types Dual (independent times, tempos, feedback, levels, pans, cross feedback), Mono Tape (two heads with level, pan and feedback; Motor Speed, also on Tape), Reverse (moved from Multi-Delay; Run, Trigger Restart, Crossfade) and Sweep (moved from Multi-Delay; any LFO shape, tempo). Lines up to 16 s, Master Feedback to 200 %, signed feedback, Echo Pan, Right Post Delay, Stack beside Hold, cut slopes and Q, two peaking bands in the feedback path, LFO 1/2 with type, tempo, phase and target, a depth range, LFO 3 on level, pan or spread, and a diffuser.
- **Megatap:** 🆕 up to 128 taps, pre-delay, feedback from a chosen tap, Time Alpha (the old Time Shape), Time and Amplitude Randomize (fixed per tap, so repeatable), Amplitude and Pan Alpha, signed spread, diffusion and envelope.
- **Multitap Delay:** 🆕 now the quad family: Quad Tap, Quad Parallel, Quad Series (send, return, series feedback), Quad Diffusor (four long allpasses) and Quad Tape. Each voice has a time or tempo, feedback, signed level, pan, filter (LP/HP/BP/peak), chorus, normalised comb and ring modulator; cross feedback, master scalers, two time LFOs, a filter LFO, drive, cut slopes, diffuser, ducker and envelope. Ten Tap and Plex left for their own blocks; Ducking, Reverse and Sweep for the Delay.
- **Ten-Tap Delay** (🆕 `tentap`): Ten Tap and Rhythm Tap (per-tap times, quantise to a tempo division, learn from the playing), 1–10 taps, tap levels, mono/stereo, ratio, pan shape and alpha, feedback.
- **Plex Delay** (🆕 `plex`): 1–8 lines on a Householder matrix with per-line time, tempo, level and pan, decay time, loop and input diffusion, pre-delay, stack/hold, a loop filter with LFO, envelope, ducker, and the types Plex Detune, Plex Shifter (granular shifters in every loop, forward or reversed grains, granule and crossfade), Plex Verb and Shimmer Verb.
- **Reverb:** 🆕 types Chamber and Cathedral. Early reflections with Early/Late Level, Early Diffusion, Time and Decay, and Late Input Mix. Low and High Freq Time multipliers at two crossovers. Low Mid and High Mid peaking bands. Pre-delay Tempo, Feedback and Mix (pre-delay up to 1 s). Echo Density and Wall Diffusion (in-loop allpasses that keep the decay). Input Diffusion and its Time. Quality, from 4 to 16 lines, level-matched. Stack alongside Hold. Mic Spacing. Mod Depth, Rate and LFO Phase. A two-voice granular pitch section at the input, the output or in the tank (shimmer), with splice, feedback, reverse and voice balance. Spring with 1–6 springs, Tone, Drive and Boing. Stereo Spread to ±200 % (M/S widening).
- **Looper** (🆕 `looper`): a grid block on one looper core shared with the rig looper (`Cathode.Dsp.Looping`), with Once, Trim Start/End, threshold recording, Max Loop Time up to 300 s (memory committed only as it is recorded), Playback and Dry Level, Overdub Level (an exact undo of the last pass), a seam Crossfade, Loop Quantize to the tempo, Record 2nd Press, Speed (half/double) and Low/High Cut. Looper commands, telemetry and export go to the running preset's Looper block, otherwise to the rig looper; a preset change stops the outgoing grid loop. Play and stop now fade over 2 ms.

**P4 stream (dynamics, EQ, drive, cab and the amp block).** Every new control defaults to off or stock, so presets sound as before (the golden renders pass, and every Circuit Amp model renders bit-identical at its defaults).

- **Compressor:** 🆕 Detector choice (type default, peak, RMS, RMS+peak), Ratio to 100:1 = ∞ (a limiter), the 🆕 Dynamics type (a dynamics processor: compress or expand above the threshold) and Feedback type (the detector hears the output), Auto Attack/Release (crest-factor scaling, `CrestFactor`), Light Type for the opto, an Emphasis high shelf and one sidechain EQ band on the detector, and Input Level (Line takes 12 dB off the detector).
- **Gate:** 🆕 Modern Expander and Modern Gate (6 dB of hysteresis, no chatter) and a soft Knee for the expanders. **Multiband Compressor:** 🆕 Peak/RMS detector, per-band mute, a 12 dB/oct (LR2) crossover option, Auto Attack/Release.
- **Graphic EQ:** 🆕 a shared `GraphicEqualizer` (also used by the Drive's EQ 2 and the amp's output EQ) with ten layouts (10 octave, 10 at 2/3 octave, 8, 7, 5 bands, and approximated Mark, 5-, 4-, 3-band and console passives), the bands renamed and hidden per type through `typeLabels`, and Outer Shelves. The global output EQ stays on the plain ten-band type.
- **Parametric EQ:** 🆕 Shelving 2 (first order) and Peaking 2 (proportional Q) on bands 1 and 5, blocking slopes from 6 to 36 dB/oct (`CutFilter` gained 30 and 36), and an engine-side Solo; the editor's curve draws every type. **Crossover:** 🆕 four band levels and four band pans (the left and right input's low and high bands).
- **Enhancer:** 🆕 the Stereoizer type (24 third-octave allpasses, complementary and mono compatible) and Depth (the allpasses' sharpness). **IR Player:** 🆕 a second IR slot, Mode (parallel, stereo, series), per-IR Length, Level and Pan, cut slopes, Input Select; the slots are baked on the control thread (`IControlRefresh`) and crossfaded, as the Cab does.
- **Drive:** 🆕 the EQ 1 page (low/high cut, bass, mid with frequency, high mid, treble), EQ 2 (a ten-band 2/3-octave graphic, pre or post), and the Advanced page: Clip Type and Clip Shape for the voicings, diode type and count overrides for the circuits (variants prebuilt, allocation-free), Bass Response, Dry Level, Bias, Slew Rate, Bit Reduce and Sample Rate. **Dynamic Distortion:** 🆕 the boost filter (shelf or peak, with its inverse after the clipper), Bias, Mono/Stereo, and four more clip curves.
- **Cab:** 🆕 Smoothing (fractional-octave, minimum-phase correction that keeps the onset), a continuous Proximity with Proximity Freq, the room page (size, shape, floor reflection, HF and LF damping, diffusion) and the mic preamp's Bass, Mid and Treble.
- **Circuit Amp:** 🆕 Input Boost (six Drive circuits at the amp's oversampled rate), Saturation (a diode-style clipper between preamp and power amp), Input Filter and Definition, an Output EQ on the Graphic EQ's layouts at the input, before the power amp or at the output, Input Dynamics, an Output Compressor (Output/Feedback types, clarity), Speaker Drive, Thump with Cab Resonance, Speaker Compression with its time constant, Bias Tremolo and Balance (36 parameters, `Amp/AmpInserts.cs`). The knobs are read on a fixed 64-frame grid, so the output no longer depends on the host's block size.

**Presets, scenes and routing stream (PS).** The features of Fractal's *Presets* page that Cathode lacked (§9). Mix laws per block (§3): constant power on Chorus, Flanger, Phaser, Reverb, Multitap, Rotary, Ten-Tap and Drive, the sweetened law on Delay, dry at 0 dB on the Vocoder, linear elsewhere, through one helper (`MixLaw`, `MixRamp`); this changes the sound at partial Mix on purpose, and the golden renders of chorus, flanger, phaser, reverb, multidelay and tentap were regenerated (Delay's default render ends before its first echo, so it is unchanged). 🆕 **Kill Dry** (`killDry`) on Delay, Multitap, Pitch, Plex and Reverb. Engine: per-block output meters and an ITU-R BS.1770 loudness meter on the output in the telemetry (layout v5), a spillover switch, the grid loop handed over across a preset change, and modifiers switched per channel. Measured cost (min of passes, 48 kHz / 64): delay 0.59 %, multidelay 0.72 %, reverb 2.81 %, chorus 0.27 %, flanger 0.86 %, phaser 0.43 %, tentap 0.51 %, rotary 0.34 %, vocoder 1.40 %, pitch 2.31 %, plex 2.33 %, drive 13.9 %: all within noise of `BlockCosts.cs`.

**Stream PA (round 3: I/O, cab and effects).** Every ◐/❌ of §2, §5, §6.2, §6.12, §6.19, §6.22, §6.23, §6.28 and §6.37 that was not the Drive's or the Amp's, with defaults that keep the earlier sound (the golden renders pass).

- **Multi-channel input** (§2): the native shim names each device channel (`cth_audio_channel_name`) and silences channels a callback does not deliver; `NativeAudioSession` takes an `InputMap` (cathode-host `--input-map "1;3,4;;;5,6"`: device channels for Input 1, Input 2, Input 3, Input 4, USB 5/6 and USB 7/8; one channel feeds both sides) and passes the extra sources planar in `AudioIO.Sources`; the engine copies them into `HardwareInputs`; the compiler hands each block that reads one (`IHardwareInputBlock`) its graph's `InputPort`, which fades out when the graph is replaced. The default maps Input 1 only, to channel 1: nothing changes for existing rigs. The Setup page has the input map, the host reports the mapped sources (`HostInfo.InputSources`, `EngineStatus.inputs`). Choices that use it: the Input cell's 🆕 **Source**, the Multiplexer's Input Select, the Compressor's and Gate's Sidechain (the old "Input" is now "Grid Input"), the Pitch block's Detector Source and the Vocoder's Analysis Source.
- **Cab** (§5): 🆕 **Room Mic Spacing** (0–300 cm: each DynaCab slot synthesised for a left and a right room mic, its reflections heard from half the spacing to either side; the direct sound stays the close mic's), 🆕 **Preamp Mode** (Economy, the old path, or High Quality: the mic preamp 4× oversampled), and the 🆕 **Align** view in the Cab panel (every slot's IR head on one time axis from `GET /api/v1/live/blocks/{id}/cab/align`; drag or nudge a slot's Delay, or line every heard slot up with the latest onset).
- **Compressor** (§6.2): 🆕 **JFET** (1176-style feedback peak detector; the gain cell's odd harmonics grow with the reduction), 🆕 **Tube** (variable-mu: a knee of at least 18 dB, RMS feedback detector, a little second harmonic) and 🆕 **Compander** (compression above Threshold, downward expansion below Expander Threshold by Expander Ratio); 🆕 **Controls** Compression: one knob for threshold and ratio together. Four Block Library presets for them.
- **Graphic EQ** (§6.12): the passive types are 🆕 component-level networks (`PassiveGraphicEq`): the Mark and 5-band on a simulated inductor graphic (`LcrGraphicEq`: an op-amp with each slider between its inputs and its wiper into a series L-C-R; the Mark with the Mesa IIC+ parts of docs/06 §3.4), the console on three broad resonators of the same circuit, the 3-band on the Marshall stack and the 4-band on that stack plus a presence network. The amp's output EQ keeps the approximations.
- **Pitch** (§6.22): 🆕 **Learn** latches the played note as the key; 🆕 **Custom** scale of 4–8 notes with a tonic. **Plex** (§6.23): Direction confirmed against the guide (reversed granules in order) and tested. **Rotary** (§6.28): 🆕 **Low Rate Multiplier**. **Wah** (§6.37): Min/Max Frequency now 🆕 on the circuits (their inductor scaled per pedal position).

**Cab block stream (CB, round 3).** From the Cab block's wiki page (paraphrased). Every new control defaults to the earlier sound except the Stereo input routing, which deliberately changes it: the `cab` golden render was regenerated (the default block, Input Stereo with slot 1 alone, now hears only the left input). No other golden changed.

- **Stereo input** (CB1): with Input on Stereo, slots 1 and 3 hear the left input and slots 2 and 4 the right, each still panned into the stereo output, as on the Axe-Fx. The slots mix into a left-fed and a right-fed IR pair (`IrFeed.Cross`, four convolutions), or one pair fed by one side when only that side's slots play (two). Left, Right and Sum (the Axe-Fx's Mono) mix all four slots into one pair as before. With a stereo input each side's slot levels are relative among themselves, so a lone slot on each side plays at unity (the split DI/cab trick).
- **Mic preamp** (CB2, CB3): twelve types (`MicPreampTypes`): None, Tube, Bipolar, FET 1 (the old FET), FET 2 (a square-law JFET stage), Transformer, Tape 70/50/35 µs (record pre-emphasis with the time constant, tape saturation, exact playback de-emphasis), Vintage (a transformer's low-frequency flux saturation before a triode), Modern (an op-amp's sharp knee) and Exciter (only the band above 3 kHz through the curve). Tube, Bipolar, FET 1 and Transformer keep their curves; the old single Tape type is replaced by the three equalised ones. The preamp's input level is a block meter (`preampIn`, `IBlockMeters`), 0 dB at each type's 1 dB onset of clipping, shown in the Cab panel.
- **Per slot** (CB4, CB5): Proximity and Proximity Freq (a gradient mic's near-field shelf on IR, IR pack and Flat slots; a DynaCab's Distance sets its own) and Smoothing replace the global controls; the slot Delay goes to 3.4 m (9.9 ms), shown as ms and mm; Auto-Align (on) replaces the global Auto Delay, and off delays a DynaCab or pack slot by its mic's travel time, less the nearest modelled mic's.
- **Motor Drive** (CB6): voice-coil thermal compression (`VoiceCoil`: the coil's resistance follows the power it takes through a 0.05–1 s time constant), off by default, with a gain-reduction meter (`motorReduction`).
- **FullRes** (CB8): IR files up to 65,536 samples at 48 kHz (1.37 s) in the Cab and the IR Player, on the existing zero-latency convolver (64 direct taps, then 64-sample uniform partitions), whose spectrum multiply-accumulate is now vectorised (bit-identical). Measured with `Cathode.Bench --only cab` (best of three, loaded machine): four FullRes slots on a Stereo input (four convolutions) 9.7 % of a core, the same on Sum (two) 5.1 %, the default DynaCab 0.85 %; IR Player with two FullRes IRs about 10 %. A 64-sample partition is memory-bound on a 1.4 s IR; a non-uniform tail (1,024-sample partitions) would cut that several times and is the next step. A change of IR is crossfaded over 20 ms but the new pair starts with an empty history, so for a reverberant FullRes IR the old tail stops at the end of the fade: not gapless.
- **Flat** (CB9): a slot Source that is a unit impulse. **Cab modelling off** (CB10): a global setting (`GlobalSettings.CabModelingOff`, rig `cabsOff`, `BlockContext.CabsOff`) under which every Cab and IR Player block passes audio through, crossfading over one callback; on the Global page.
- **IR import** (CB7): `POST /api/v1/assets` takes `processing` (asIs, autoTrim, minimumPhase), `polarity`, `channels` (both, left, right, sum) and `maxLengthMs` for a .wav (`IrImport`, cepstral minimum phase); the IRs page has an import section with them. **IR export** (CB11): `GET /api/v1/live/blocks/{id}/ir` returns the Cab's current mixed IR as a stereo .wav (every playing slot, DynaCab and pack slots included, slot and master cuts, proximity and smoothing baked in) and `POST` saves it as a new IR asset; the Cab panel has Export IR and Save as IR.

**Stream PB (Drive and the Amp block's advanced page), round 3.** Every new control is stock at its default: `AmpDefaultsTests` renders all 81 Circuit Amp models, every Drive type and the seven original Dynamic Distortion types at their defaults and checks them bit for bit against hashes rendered on master (`tests/assets/defaults/master.sha256`). Where the guide's control is emergent in a circuit model, it is a scale on a component the model already has, and the control is hidden on a model without that component (`Amp/AmpAdvanced.cs`, `AmpFeatures`).

- **Circuit Amp** (25 parameters, §4): 🆕 Power Tube Hardness and Preamp Hardness (both tube knees, `PentodeModel.Hardened`/`TriodeModel.Hardened`, served by the existing tables: G(u) is free of K_p, the plate knee is read at a scaled voltage); generic Presence Shift (presence an octave down), Fat (stack × 0.6), Input Cut (150 Hz), High Treble (±12 dB shelf at 3 kHz before the power amp) and Overdrive (up to 20 dB more swing into the last gain stage), each hidden where the model has its own; Tonestack Type (any model's tone-stack-stage stack) and Location (Pre, Mid, Post: the stack's stage moved, every stage held at its own operating point by an ideal level shift); Triode 1/2 Plate Freq (plate-bypass caps, the SLO and 5150 III); CF Compression and CF Harmonics (a cathode follower's grid current and cathode resistor); Preamp, PI and Power Tube Bias Excursion (grid current), Excursion Time (power-grid coupling caps), Recovery Time (grid leaks) and Master Bias Excursion over all three; Plate Suppressor Diodes (clamps that keep each plate above its cathode); Master Location (Post PI: a divider on both inverter outputs) and Master Cap (a cap across the master's upper half); Output Mode (SS Power Amp + Cab leaves speaker drive, thump and compression to the real cab); Speaker load Follow Cab (formerly Auto (Cab)), which follows the next Cab block's DynaCab enclosure and speaker (`ICabFollower`, linked by the graph compiler) with the load derived from that speaker's own Thiele–Small data (`SpeakerElectrical`, issue #1); Screen Freq and Screen Q (the screen node's cap, and a choke of inductance (QR)²C feeding it).
- **Drive** (§6.5): 🆕 Clip Types FET, Rectifier, Variable, HV Tube, LV Tube, Silicon and 4558 Diode (each with its ADAA antiderivative); Bass Pot and Middle Pot on the Guv'nor (its stack's real pots, noon as built); Slew Rate on each op-amp (the RAT's LM308 device takes it as its own slew; the linear op-amp solves limit their output), the post-clip limiter only on circuits without one.
- **Dynamic Distortion** (§6.6): 🆕 every clip curve (14), and seven Block Library presets standing in for the guide's filter-shaped types.
- Not done: per-pedal extra knobs beyond the Guv'nor (the Timmy-style bass and treble cuts, the Zendrive's Voice, the OCD's HP/LP switch need their networks extended, medium each); Tonestack Location End (no circuit meaning: the stack's tube stage cannot run at speaker voltage) and stacks inside a network stage or a channel switch (Location moves a whole stage; Type swaps a tone-stack stage's values only); Master Location Pre-Triode; Auto speaker from an IR (it follows DynaCab slots only).

### 1.3 CPU costs to regenerate

After the merge with P3 and the amp streams, `BlockCosts.cs` was regenerated (`--write`), so it now holds the engine and global stream's new blocks (**input**, **output**, **rta**) and the changed ones.

Before that (the first parity pass): `BlockCosts.cs` was not regenerated. Blocks whose processing changed (a new per-sample stage or a changed default path) should be re-measured: **chorus, compressor, crossover, delay, enhancer, fbreturn, filter, flanger, gate, geq, megatap, multidelay, phaser, reverb, resonator, ringmod, tremolo, volpan, wah** and the new **mixer** (placeholder entry 0.04 %). Every other effect block only gained the Balance gain stage (an extra multiply per sample when balance is off-centre): **drive, dyndist, formant, fxloop, irplayer, mbcomp, mux, peq, pitch, rotary, synth, tonematch, tonestack, vibe, vocoder** may move by a few hundredths of a percent.

---

## 2. Input and Output blocks (pp. 4–6)

Cathode's `input` and `output` are built-in grid cells that the graph compiler runs as blocks (🆕 `InputBlock`, `OutputBlock`): they have parameters and four channels, but no bypass. Device channel selection stays a host setting (Setup page).

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Input 1–4 / USB source | 🆕 ✅ | Source: Input 1 (the default, the grid input), Input 2–4, USB 5/6, USB 7/8, each mapped to device channels on the Setup page (one channel is mono on both sides); unmapped sources are silent. 10 ms crossfade on a change; up to four Input cells, each with its own source |  |
| Noise gate: Type (Classic, Intelligent, Noise Reducer) | 🆕 ✅ | Off (default), Classic (downward expander), Intelligent (the detector notches the mains frequency, 50 or 60 Hz, and two harmonics), Noise Reducer (gates only above a 2 kHz split) |  |
| Gate Threshold, Ratio, Attack, Release, Output Level | 🆕 ✅ | Output Level is the Input Level |  |
| Global noisegate offset | 🆕 ✅ | Global page, ±20 dB, added to every Input gate threshold |  |
| Input bypass behaves as a shunt | — | No bypass on built-ins |  |
| Instrument impedance (1 MΩ … 22 kΩ ± cap, Auto) | 🆕 ✅ | A pickup-load filter rather than analogue circuitry: §2.1 |  |
| Output: Main level and balance | 🆕 ✅ | On the Output block, before the scene level |  |
| Output: per-row Level and Balance mixer | 🆕 ✅ | Six rows (rows below take the sixth's); a single row is scaled in place, so a plain chain still uses one buffer |  |
| Output: 8 scene levels | ✅ | `Scene.Level` |  |
| Output bypass = mute | — | Built-ins cannot be bypassed |  |
| In/Out as an FX send/return loop | ✅ | `fxloop` block, with loop latency compensation (better than the guide's two-block arrangement) |  |
| Four channels on In/Out | 🆕 ✅ | Switched by scenes like any block |  |
| Output 2–4 blocks | 🆕 ✅ | `output2`..`output4`: §2.1 |  |

### 2.1 Audio I/O (wiki: "Audio I/O and amplification")

The wiki page covers what the hardware does around the grid: its outputs, converters, input level and impedance, oversampling and latency. Cathode's answers, all defaulting to how it behaved before (one stereo output on device channels 1–2, 1 V at full scale, IIR oversampling), so existing rigs sound the same:

- 🆕 **Outputs 2–4** are grid blocks (`output2`..`output4`, category I/O, one each) beside the main Output. The audio settings map each output to a pair of device channels (`--outputs 1,3,5,7` on the host, the Setup page in the desktop app; 0 or nothing leaves it unmapped). An unmapped output is not rendered. Every output is delayed to the slowest path, so outputs played side by side stay in time. The shim already opens every device output channel; the host's `OutputBus` carries Outputs 2–4 and the DI out from the engine to the mapped channels.
- 🆕 **Copy Output 1 to Output 2** (global): Output 2 plays Output 1 (after the scene level and the looper, before Output 1's EQ, mode and Boost/Pad) unless the preset has an Output 2 block.
- 🆕 **Output mode** per output (global): Stereo, Sum L+R (½(L+R) on both, so a mono source keeps its level), Copy L→R and Dual Mono. As the brief defines them, Copy L→R and Dual Mono both put the left channel on both sides, so in Cathode they render identically; the two names are kept for familiarity.
- 🆕 **Per-output global EQ and Boost/Pad** (−18 to +18 dB): each output has its own ten-band EQ (IPC blocks `global`, `global2`..`global4`; Output 1 keeps the old global EQ) and trim, after its mode. Global settings: `outputs[0..3]` with `mode`, `boostPad` and `eq`.
- 🆕 **Input calibration**: `inputs[0].fullScaleVolts`, the peak volts at the jack that reach 0 dBFS (default 1 V, as the Circuit Amp always assumed). It reaches blocks as `BlockContext.InputCalibrationDb`; the Circuit Amp scales its input by it, and the Drive scales its input by it and its output back, so turning the interface down 6 dB and doubling the calibration gives the amp and drive exactly the same volts (tested to −60 dB residual through a Drive into an Amp). Dynamic Distortion clips in full-scale units, not volts, so it is not scaled. The Setup page converts an interface's documented maximum input level (dBu, or V peak) or measures it: play a sine of known RMS volts into input 1 for 2 s.
- 🆕 **Input clip warning** within 0.5 dB of full scale (Setup page callout and a CLIP mark by the input meter, held 1.5 s), and a global **Input 1 Gain** trim (−20 to +20 dB, rig parameter `in1Gain`) ahead of the grid for every preset. Both act on Input 1, the main input (whatever device channels the input map gives it); Inputs 2–4 and the USB pairs that blocks read through the `InputPort` are not trimmed, and the amp and drive models use Input 1's calibration whichever source they get. `inputs` is a list in the global settings and the rig names its parameters by input (`in1Gain`, `in1Cal`), so per-source values can follow.
- 🆕 **Input impedance** on the Input block: a pickup (Single Coil 2.5 H / 6 kΩ / 100 pF, Humbucker 4.5 H / 8.5 kΩ / 150 pF, P90 7 H / 8 kΩ / 110 pF, or Custom L, R, C) with Cable Capacitance (500 pF), loaded by the Input Impedance (Auto = 1M, 1M, 470k, 230k, 90k, 70k, 32k, 22k) or by a Pickup Buffer (the pickup sees 1 MΩ and none of the cable). The loaded pickup is a second-order low-pass, `1 / (LC s² + (RC + L/R_L) s + 1 + R/R_L)`. A DI was already recorded through the real pickup into a high-impedance input, so the block applies the change from that reference (1 MΩ, same cable) to the chosen load, as one biquad with each quadratic warped at its own resonance. Pickup Off (the default) or Auto without the buffer is bit-exact passthrough. Tests: f₀ and Q against the RLC formula, the block's response against the analytic ratio. *Not modelled:* the guitar's volume and tone pots, which load the pickup too.
- 🆕 **Dry DI out**: Input 1 as the converter gave it (before Input 1 Gain) on one or two spare device channels (`--di-out 5,6`), for re-amping.
- 🆕 **Oversampling mode** (global `oversampling`): **Minimum phase** is Cathode's existing polyphase IIR half-bands, causal with no reported latency (the default). Contrary to the brief's premise, the current half-band was not linear phase, so the new mode is **Linear phase**: Kaiser-windowed FIR half-bands with the same band edges and 100 dB stop band (matching the IIR magnitude within 0.01 dB in the pass band), a pure delay of 86 samples at 2x and 93 at 4x (1.8–1.9 ms at 48 kHz). It applies to the Circuit Amp, Drive and Dynamic Distortion (`IOversampledBlock`), which report the delay as their `LatencySamples`; the graph carries the mode, so a change recompiles and parallel paths stay aligned. 🆕 The Drive's own **Oversampling** (Global, 1x, 2x, 4x; `Recompile`) forces a factor; on Global a type that runs at 2x is delayed to the 4x latency, so the latency never changes with the type.
- 🆕 **Latency report** (`GET /api/v1/system/latency`, Setup page): the device's input and output latency from the shim (each including one buffer), the buffer, and the graph's latency along its longest path with the blocks on it, and the total.
- 🆕 **Output clip warning** at −1 dBFS per output: the host reports each output's peak after its EQ and Boost/Pad (`outputPeaks` in telemetry), and the meters' CLIP mark names the outputs.
- 🆕 **Stereo-into-mono hint**: the grid marks a block that sums to mono (Circuit Amp, capture amp, a Cab, IR Player or Volume/Pan on one side or the sum, a Dynamic Distortion or Mixer in mono) when an engaged stereo effect (Chorus, Flanger, Phaser, the delays, Plex, Reverb, Rotary, Pitch, Enhancer, Tremolo, Crossover) feeds it on the same path, with a tooltip naming them. The Drive runs left and right through separate circuits in Cathode, so it does not sum to mono and is not marked.
- 🆕 **Cab resonance helper** on the Circuit Amp: a sine (40–250 Hz, logarithmic slider) replaces the amp's output (`PUT /api/v1/live/blocks/{id}/test-tone`, an engine-side `TestTone`, faded over 10 ms) and plays through the cab; sweep to where the cab booms and Apply writes the frequency as Load resonance, in percent of the speaker load's resonance (with Follow Cab, the followed Cab speaker's) (`GET .../speaker-resonance`). Nothing is stored in the preset, and a graph change or closing the helper stops the tone.

## 3. Common mix and level parameters (pp. 7–8)

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Mix (wet/dry) and its law | 🆕 ✅ | Per block, from one helper (`MixLaw`): **constant power** (dry and wet −3 dB at 50 %) on Chorus, Flanger, Phaser, Reverb, Multitap, Rotary, Ten-Tap and Drive; **linear** (−6 dB each) on Compressor, Formant, Megatap, Multiband Comp, Pitch, Plex, Ring Mod and Synth; **dry at 0 dB** to 50 % on the Vocoder. Mix 0 and 100 % are exact. Gains are interpolated per sample between the law's values at each end of a block |  |
| Delay mix law ("sweetened") | 🆕 ✅ | The dry stays at 0 dB to 50 % and then fades out; the wet rises on a squared taper (−12 dB at 25 %, 0 dB at 50 %), so most of the knob's travel is spent on quiet echoes. Replaces the earlier both-at-full law |  |
| Kill Dry | 🆕 ✅ | `killDry` on Delay, Multitap, Pitch, Plex and Reverb: the dry is muted in the block, so Mix sets the wet level alone (Global Mix still moves it) |  |
| Level | ✅ | Every effect block (`level`, dB) |  |
| Balance | 🆕 ✅ | Every effect block (`balance`, −100…100 %); the far side is turned down, wet and dry alike |  |
| Bypass (as a modifier target) | 🆕 ✅ | A modifier on the `engage` pseudo-parameter engages the block at 50 % and above, bypasses it below, and wins over the scene; set from the engage switch's context menu |  |
| Bypass modes Thru, Mute, Mute In, Mute FX In | ✅ | Since Phase 5 |  |
| Bypass modes Mute Out, Mute FX Out | 🆕 ✅ | The block keeps running ("listening") while bypassed |  |
| Input Select (L, R, L+R) | 🆕 ✅ | Reverb, Megatap, Multitap, Ten-Tap, Plex, Volume/Pan, 🆕 Rotary, Pitch, RTA |  |
| Input Gain | 🆕 ✅ | Delay, Reverb, Megatap, Multitap, Ten-Tap, Plex, Resonator, 🆕 Pitch |  |
| Global Mix (follow the global Effects Mix offset ±50 %) | 🆕 ✅ | `globalMix` switch (off by default) on Chorus, Delay, Flanger, Megatap, Multitap, Phaser, Pitch, Resonator, Reverb, Synth; the offset (Global page) is added where each reads Mix, clamped to 0–100 % |  |
| Stereo Spread (−200…+200 %) | 🆕 ✅ | A gain on the wet side signal with the mid kept: beyond ±100 % the image widens (an M/S widener, not Fractal's psychoacoustic processing). Delay, Reverb, 🆕 Chorus, Flanger, Phaser (besides their LFO phase) to ±200 %; Megatap, Ten-Tap and Plex ±100 % |  |

## 4. Amp block (pp. 9–18)

Audited in detail in [06 §1](06-amp-modelling-spec.md#1-axe-fx-iii-amp-block-parameter-parity). The P4 stream added the non-model parameters in `CircuitAmpBlock` and `Amp/AmpInserts.cs`, all off or neutral by default; what is left needs changes inside the models or the power amp (several are emergent in a circuit model). Current state of `circuitamp` (🆕 113 parameters; catalogue in [07](07-amp-catalogue.md)):

| Page | Present | Missing | Effort for the gaps |
|---|---|---|---|
| Type, Tone | Type, Gain, Bass, Middle, Treble, Master, Presence, Depth, Cut, Bright, model switches and second controls, Input Trim, 🆕 Presence Shift and Overdrive as generic controls where a model has none | — | |
| Preamp | Preamp tube type, Bright Cap, Preamp Bias, Preamp Sag, 🆕 Input Boost (type from the Drive circuits, drive, tone, level), 🆕 Saturation switch and drive (diode-style clipper between preamp and power amp), 🆕 Fat, Input Cut, High Treble | — | |
| Input EQ | Input select, Low Cut, 🆕 Input Filter (off, shelves, peak, high/low-pass; freq, Q, gain), 🆕 Definition (treble/bass tilt into the preamp) | — | |
| Output EQ | 5 model EQ bands (`eq1`–`eq5`), 🆕 Output EQ type (every Graphic EQ layout) with 10 bands, 🆕 EQ location (input, pre power amp, output) | Passive types are approximations (see §6.12) | — |
| Power supply | Sag, B+ time constant, supply type (DC, AC 60 Hz, AC 50 Hz = AC line frequency), rectifier, Variac, 🆕 Screen Freq (the screen node's cap), 🆕 Screen Q (a choke feeding it) | — | |
| Power amp | Power amp on/off, power tube type, bias, negative feedback, transformer matching, LF/HF, drive, speaker impedance, 🆕 bias tremolo (rate, depth: the model's own tremolo circuit where it has one, a bias tremolo from the drawing's oscillator and coupling network in the power amp, an optical vibrato or the 18 W's; elsewhere a sine on the Bias control), 🆕 Power Tube Hardness (both knees of the pentode law), 🆕 Plate Suppressor Diodes | — | |
| Speaker | Speaker load curve (🆕 Follow Cab: the next Cab block's DynaCab cabinet and speaker, from the speaker's Thiele–Small data), load resonance freq/Q/peak, load HF, load impedance (renamed from Speaker / LF Resonance / LF Q / LF Amount / Speaker HF / Speaker Imp, issue #1), 🆕 speaker drive (cone excursion), 🆕 thump with cab resonance, 🆕 speaker compression and its time constant | Compliance (covered by the impedance curve's LF Q and amount); Auto from an IR slot | — |
| Dynamics | 🆕 Input dynamics (expand/compress by envelope), 🆕 output compressor (Output/Feedback type, amount, threshold, clarity), 🆕 Master Bias Excursion (scales the preamp, PI and power-tube grid current) | — | |
| Advanced | Tonestack frequency, master volume trim, presence/depth frequency, cathode time constant, 🆕 Tonestack Type and Location, Triode 1/2 Plate Freq, Preamp Hardness, CF Compression and Harmonics, Preamp/PI/Power Tube Bias Excursion with Excursion and Recovery Time, Master Location and Cap, Output Mode (FRFR / SS PA + cab) | ◐ Tonestack Location End, Master Location Pre-Triode; Type and Location only on a model's tone-stack stage | — (no circuit meaning, or a stage inside a network) |
| Output | Level, 🆕 Balance, bypass mode handled by the engine | — | |

The capture tier (`nam`) has input, output, normalise and a four-band post EQ; the guide has no counterpart (Fractal's NAM block arrived with firmware 33).

## 5. Cab block (pp. 19–24)

Re-audited for the merged `cab` block (the old `cab` and `dynacab` are one block with four slots, each playing an IR file, a synthesised DynaCab or an IR pack).

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Cab Mode (Legacy / DynaCab) | ✅ | Per slot: Source IR, DynaCab or IR Pack |  |
| Legacy: Bank and Cab Number, cab picker | ◐ | Any IR from the asset store; no factory banks (licensing) | — |
| Legacy: 4 IRs per channel | ✅ | Four slots, any mix of sources |  |
| Mic Distance (align, sub-ms delay) | 🆕 ✅ | Per-slot Delay up to 3.4 m (9.9 ms, fractional), shown in ms and mm |  |
| Auto-Align | 🆕 ✅ | Per slot, on by default (aligned); off delays a DynaCab or IR pack slot by its mic's travel time, less the nearest modelled mic's (no added latency) |  |
| Proximity, Proximity Freq | 🆕 ✅ | Per slot: 0–200 % of a gradient mic's near-field shelf at 20–500 Hz on IR, IR pack and Flat slots; a DynaCab's Distance sets its physical proximity |  |
| Smoothing | 🆕 ✅ | Per slot, 0–1 octave magnitude smoothing at build time on IR, DynaCab and pack slots; a minimum-phase correction keeps the onset and the slots' alignment |  |
| Flat IR | 🆕 ✅ | Slot Source Flat: a unit impulse (a DI next to a cab, or Mic Distance without colouring) |  |
| DynaCab: cab type, mic type, position, distance | ✅ | Synthesised models (10 speakers, 11 mics, 7 cabinets), plus mic angle |  |
| Level per IR (relative, normalised mix), Mute, Solo, Pan | ✅ | Every slot, plus Invert |  |
| Per-IR low/high cut and slopes | ✅ | 6–24 dB/oct, baked into the mixed IR |  |
| IR Length | ✅ | Per slot, 5–170 ms with a short fade |  |
| Preamp page: Drive, Saturation, Type (Tube, FET, Tape…) | 🆕 ✅ | None, Tube, Bipolar, FET 1, FET 2, Transformer, Tape 70/50/35 µs (equalised around the saturation), Vintage, Modern, Exciter (`MicPreampTypes`) |  |
| Preamp page: input VU | 🆕 ✅ | The level into the preamp's curve, 0 dB at the type's onset of clipping (1 dB of compression); a block meter in the Cab panel |  |
| Preamp page: Bass/Mid/Treble | 🆕 ✅ | 120 Hz shelf, 800 Hz peak, 4 kHz shelf, ±12 dB; skipped at 0 dB |  |
| Preamp page: Mode | 🆕 ✅ | The guide's Mode is High Quality or Economy (CPU): Economy (default) runs the preamp at the engine rate, High Quality 4× oversampled, so its harmonics fold back far less; a change rebuilds the graph (`Recompile`), so only a High Quality cab holds the oversamplers |  |
| Master low/high cut and slopes | ✅ | 6–24 dB/oct |  |
| Room page: level, size, shape, LF/HF damping, floor reflections, diffusion | 🆕 ✅ | Room, Room Size, Room Shape, Floor Reflection, Room HF/LF Damping, Room Diffusion, in the DynaCab synthesis (an IR file or pack carries its own room) |  |
| Room page: mic spacing | 🆕 ✅ | Room Mic Spacing 0–300 cm: each DynaCab slot gets a left and a right IR whose reflections are heard from half the spacing to either side (`CabRoom.MicOffset`); the direct sound stays the close mic's, so the room widens and the close sound stays centred. IR files and packs carry their own room |  |
| Align page | 🆕 ✅ | Cab panel, Align view: each slot's first 10 ms as mixed, on one axis with its Delay and what Auto-Align off adds, its onset marked; drag a slot or nudge it by 0.01/0.1 ms, or Align onsets (`GET /api/v1/live/blocks/{id}/cab/align`) |  |
| Mix page: Input Mode (L, R, Sum, Stereo), Air, Air Frequency | 🆕 ✅ | Stereo as on the Axe-Fx: slots 1 and 3 hear the left input, 2 and 4 the right (four convolutions when both sides play) |  |
| Motor Drive, Motor Time Constant | 🆕 ✅ | Voice-coil thermal compression, 0.05–1 s, off by default; gain-reduction meter |  |
| FullRes long IRs (1.37 s) | 🆕 ✅ | 65,536 samples at 48 kHz in every Cab slot and the IR Player; zero latency, uniform 64-sample partitions (CPU above); a switch is click-free but not gapless for long reverberant IRs |  |
| Global: cab modelling off | 🆕 ✅ | Global page: every Cab and IR Player passes audio through |  |
| Cab-Lab: import options, export a mix | 🆕 ✅ | IR import: as-is, auto-trim, minimum phase, polarity, stereo channel, length; export the block's mixed IR (`GET`/`POST /live/blocks/{id}/ir`) |  |

## 6. Effect blocks

Each table lists the guide's parameters page by page. Unless noted, every block also has the common Mix/Level/Balance and bypass parameters covered in §3.

### 6.1 Chorus (pp. 25–26) → `chorus`

Types: guide 17 (digital mono/stereo, analog mono/stereo, Japan CE-2, warm stereo, 80's style, triangle, 8-voice, vintage tape, dimension 1–3, 4/8-voice analog, tri-chorus, dual) → Cathode 1 algorithm whose knobs now cover them: voices up to 8, the Dual ratios, BBD drive and the 🆕 Dimension matrix. The named types themselves are 🆕 Block Library presets (§8 item 7).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Number of voices (2–8) | 🆕 ✅ | 1–8 per side, LFOs spread evenly, summed at equal power |  |
| Basic | Rate, Depth, Delay Time, Mix | ✅ |  |  |
| Basic | Tempo | ✅ |  |  |
| Basic | Dual: separate L/R rate and depth | 🆕 ✅ | Right Rate Ratio (0.25–4×) and Right Depth Ratio (0–200 %) against the left; both 1 by default, so a plain chorus is unchanged |  |
| Expert | Low Cut, High Cut | ✅ | Wet only |  |
| Expert | LFO Phase | ✅ | `spread` (0–180°) |  |
| Expert | LFO Type | ✅ | All ten shapes |  |
| Expert | Auto Depth | 🆕 ✅ | Off, Low (depth falls as 1/√rate above 1 Hz), High (as 1/rate: the pitch wobble stops growing) |  |
| Expert | Phase Reverse | ✅ |  |  |
| Expert | Drive (BBD) | 🆕 ✅ | Soft saturation of what enters the lines (shared `SoftDrive`) |  |
| Expert | Width (right time scaled toward 1 ms) | 🆕 ✅ | 0–100 %: the right delay moves to 1 ms |  |
| Expert | LFO2 Rate, LFO2 Depth | 🆕 ✅ | A sine that wobbles the main LFO's rate by up to ±100 % |  |
| Expert | Stereo Spread | 🆕 ✅ | −200…200 % on the wet (as Delay and Reverb, §3), besides LFO Phase |  |
| Expert | Dimension Mode (off, low, med, high) | 🆕 ✅ | One voice per line, lines swept in antiphase, each side's wet minus 30/50/70 % of the other's: wide and still, little pitch movement in mono |  |
| Expert | Right Time Ratio (Dual) | 🆕 ✅ | 0.5–2× |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |

### 6.2 Compressor (pp. 27–29) → `compressor`

Types: guide 16 (Studio FF/FB compressor and sustainer, analog compressor/sustainer, compander, DynaComp, dynamics processor, JFET compressor/sustainer, optical compressor/sustainer, pedal 1/2, tube) → Cathode 🆕 9 (Pedal, Studio, Opto, Sustainer, Dynamics, Feedback, 🆕 JFET, 🆕 Tube, 🆕 Compander); the other guide types are presets of these (detector, ratio, times, light type).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Threshold, Ratio, Attack, Release, Knee, Mix | ✅ |  |  |
| Config | Compression (single-knob types) | 🆕 ✅ | Controls: Threshold/Ratio or Compression, one knob that lowers the threshold (−6 to −50 dB) and raises the ratio (1:1 to 20:1) together, on any type |  |
| Type | JFET, Tube, Compander | 🆕 ✅ | JFET: feedback peak detector, odd harmonics growing with the reduction. Tube: variable-mu, knee ≥ 18 dB, RMS feedback detector, DC-blocked second harmonic. Compander: compression above Threshold, downward expansion (Expander Threshold, Expander Ratio, at most 80 dB) below |  |
| Config | Dynamics (processor: compress ↔ expand) | 🆕 ✅ | Dynamics type: the slope over the threshold is 1 − Dynamics (−100 % expands 2:1 upwards, +100 % limits), at most +24 dB |  |
| Config | Ratio ∞ (limiter) | 🆕 ✅ | 1–100:1; 100 is ∞:1 |  |
| Config | Auto Attack/Release | 🆕 ✅ | Both times scaled by 2 / crest² over 200 ms (Giannoulis et al. 2012), ×0.25–4 |  |
| Config | Look Ahead | 🆕 ✅ | 0.5–10 ms, reported as latency (parallel rows are aligned); a change rebuilds the graph (`Recompile`) |  |
| Config | Detector Type (RMS, Peak, RMS+Peak) | 🆕 ✅ | Type default, Peak, RMS, RMS+Peak (the mean of the two in dB) |  |
| Config | Light Type (optical) | 🆕 ✅ | Classic, Fast LED, Slow Vactrol: the opto's attack and its program-dependent release law |  |
| Config | Auto Makeup | ✅ | Half the reduction at 0 dBFS |  |
| Config | Makeup | ✅ | Cathode extra |  |
| Sidechain | Source (block, row, input) | 🆕 ✅ | Block, Grid Input, Row 1–6 (the last live block on that row left of the compressor; not latency-aligned), 🆕 Input 1–4, USB 5/6 and 7/8 (the hardware inputs); the sidechain cuts, emphasis and EQ filter it |  |
| Sidechain | Low Cut, High Cut | ✅ | Detector only |  |
| Sidechain | Emphasis, Emphasis Frequency; filter type/freq/Q/gain | 🆕 ✅ | Emphasis: a high shelf on the detector up to +12 dB; one sidechain EQ band (low shelf, high shelf, peak) |  |
| Mix | Input Level (instrument/line) | 🆕 ✅ | Line takes 12 dB off the detector |  |

### 6.3 Crossover (p. 30) → `crossover`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Frequency (LR4) | ✅ | 40 Hz–16 kHz covers the ×10 multiplier |  |
| Frequency multiplier | — | Not needed with the wider range |  |
| Lo/Hi Level, left and right | 🆕 ✅ | Four band levels |  |
| Low/Hi Pan for the left and right input bands | 🆕 ✅ | Four band pans (balance law); hard left and right by default, which keeps the image |  |
| Level, Balance | 🆕 ✅ |  |  |
| Mode "Low Left / High Right" | ✅ | Cathode extra |  |

### 6.4 Delay (pp. 31–36) → `delay`

Types: guide table lists 23 (digital/analog/tape mono and stereo, ping-pong, dual, reverse, sweep, ducking, vintage digital, 2290 with modulation, ambient stereo, Deluxe Mind Guy, mono/stereo BBD, lo-fi tape, Stereo Mind Guy, diffused, Graphite Copy, worn tape, Zephyr) → Cathode 🆕 9 (Digital, Analog, Tape, Ping-Pong, Vintage Digital, 🆕 Dual, 🆕 Mono Tape, 🆕 Reverse, 🆕 Sweep). Ducking is any type with the ducker on; diffused is any type with the diffuser; most others are presets over the parameters below. The Dual, Mono Tape, Reverse and Sweep pages show only on their types (`typeLabels`).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Time (up to 16 s), Tempo | 🆕 ✅ | 16 s lines in native memory |  |
| Config | Master Feedback (0–200 %) | 🆕 ✅ | Scales every feedback; above unity the loop limiter (transparent to full scale, then a tanh knee that never passes 2) keeps the loop bounded |  |
| Config | Feedback (±) | 🆕 ✅ | −100…100 % |  |
| Config | Echo Pan | 🆕 ✅ | Pans the repeats (balance law) |  |
| Config | Repeat Hold / Stack | 🆕 ✅ | Off / Hold / Stack; Stack keeps the input open, through the limiter |  |
| Config | Drive | 🆕 ✅ |  |  |
| Config | Bit Reduction | 🆕 ✅ |  |  |
| Config | Right Post Delay | 🆕 ✅ | Up to 1 s on the right repeats |  |
| Config | Spread | 🆕 ✅ |  |  |
| Stereo | L/R Time Ratio | 🆕 ✅ |  |  |
| Stereo | Feedback L, Feedback R | 🆕 ✅ | Dual: Feedback is the left line's, Right Feedback the right's |  |
| Dual | Independent L/R time, tempo, level, pan, feedback; cross feedback L→R, R→L | 🆕 ✅ | Dual type |  |
| Sweep | Start/stop frequency, resonance, sweep type, rate, tempo, phase | 🆕 ✅ | Sweep type (moved from Multi-Delay); the sweep type is any of the ten LFO shapes |  |
| Mono Tape | Motor speed, Head 1 time/tempo, Head 2 ratio, per-head level, feedback, pan | 🆕 ✅ | Mono Tape type. Motor Speed (also on Tape) stretches the time, slows wow and flutter and darkens the loop |  |
| Reverse | Time, Run, Trigger Restart, Crossfade | 🆕 ✅ | Reverse type. Trigger Restart starts a chunk on a note crossing −40 dBFS; chunks up to a third of the line (5.3 s) |  |
| EQ | Low/High Cut | ✅ | In the feedback path |  |
| EQ | Cut slopes, Q | 🆕 ✅ | 6–24 dB/oct, resonance on the last section (`LoopFilter`) |  |
| EQ | Two peaking EQs (freq, Q, gain) | 🆕 ✅ | In the feedback path, so each repeat is shaped again |  |
| Modulation | LFO depth range, phase reverse | 🆕 ✅ | Low (×0.1), Normal, High (×10, up to 100 ms) |  |
| Modulation | LFO 1/2 type, target, rate, tempo, depth, phase; LFO 3 (level/pan/spread) | 🆕 ✅ | LFO 1/2 on the left, right or both line times, with an L/R phase; LFO 3 on the wet level, the echo pan or the spread |  |
| Modulation | Ducker attenuation, threshold, release | 🆕 ✅ |  |  |
| Modulation | Diffuser mix and time, its LFO | 🆕 ✅ | `Diffuser` on the repeats |  |
| Mix | Input Gain | 🆕 ✅ |  |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |
| — | Spillover | ✅ | Mute In / Mute FX In, and across presets |  |

### 6.5 Drive (pp. 37–39) → `drive`

Types: guide about 60 named pedals → Cathode 64 (3 voicings + 61 circuits; see [04 Phase 6 item 7](04-implementation-plan.md)). 🆕 Round 4 group D1 (types 27–36, guide names): **Full OD** (Fulltone Fulldrive, as the Full-Drive 2), **OD-One Overdrive** (Boss OD-1), **OD 250** (DOD 250, the yellow one), **Sonic Drive** (Ibanez SD9M), **Eternal Love** (Lovepedal Eternity), **Jam Ray** (Vemuram Jan Ray), **Horizon Precision Drive**, **Tone of Kings** (Analog Man King of Tone, OD mode), **Hoodoo Drive** (Voodoo Lab Overdrive) and **Valve Screamer VS9** (the guide's TS9; a TS9-type circuit with a TL072); what was guessed is in [09 D1](09-parity-tracker.md#d1-drive-round-4-overdrives). Their cost (`Cathode.Bench --only drive`, 48 kHz / 64 frames, Windows x64) is 3.2–5.8 % of a core each (Jam Ray 3.17, OD-One 3.28, Hoodoo 3.44, VS9 3.49, Full OD 3.62, Tone of Kings 4.64, OD 250 5.16, Eternal Love 5.24, Sonic Drive 5.66, Horizon 5.78), inside the earlier op-amp circuits' 3.1–6.6 % (TS808 3.07, Rodent LED 6.58); the block's worst case stays Bender Fuzz (24.1 %). Missing: only variants of pedals Cathode already has (OD 250 Gray, Super OD beside the SD-1, Treble Boost beside the Rangemaster). Each new circuit is **medium** (netlist, tone network, tests).

🆕 **Round 4, group D2** (distortions and amp-in-a-box; [09 stream D2](09-parity-tracker.md#d2-drive-round-4-distortions-and-amp-in-a-box)), appended after group D1 (types 37–51) in this order: **M-Zone Distortion** (Boss MT-2), **Fat Rat** (Pro Co Fat Rat), **Suhr Riot Ge / LED / LED/Si**, **MCMLXXXI Drive** (1981 Inventions DRV), **Box O' Crunch** (MI Audio Crunch Box), **Blackglass 7K** (Darkglass Microtubes B7K), **Esoteric ACB** (Xotic AC Booster), **Esoteric Bass RCB** (Xotic Bass RC Booster), **FAS LED-Drive** (Fractal's own design), **Ruckus** (Suhr Riot, Fractal's earlier model), **Shimmer Drive** (Fractal original), **Tube Drive 3-Knob** and **Tube Drive 4-Knob** (the Tube Driver). Where the guide (June 2023, pp. 37–39) differs from the round's brief, the guide wins:
- *Suhr Riot* is three types in the guide, one per position of the pedal's voicing switch (germanium, LED, LED/silicon); Cathode has the three.
- *MCMLXXXI* is the guide's 1981 Inventions DRV, not a Dumble-style pedal.
- *Box O' Crunch* is the guide's MI Audio Crunch Box, not a Mesa Throttle Box.
- *Fat Rat* and *Ruckus* are not in the June 2023 guide. Fractal's forum places Fat Rat on the Pro Co Rat family (here the Fat Rat pedal: its Fat bass switch on and its MOSFET clipping) and Ruckus on the original Suhr Riot (here the Riot gain stage with silicon to ground and an LP/HP tone blend).
- *Tube Drv 3-knob / 4-knob*: the guide names only the Tube Driver and its knob count. 3-knob is modelled as the Chandler (passive treble cut), 4-knob as the B.K. Butler with bass and treble; the 4-knob's internal bias trim is fixed at its stock point (the Advanced Bias control sits ahead of the coupling capacitor, so it does not reach the tube).
- *FAS LED-Drive* and *Shimmer Drive* are Fractal originals with no schematic: Cathode's are designs (TL072 with red LEDs across the feedback; a bright TL072 boost that softens on its rails), not models.

New building blocks: a **triode stage** (a 12AX7 in common cathode solved at a Newton root from its DC operating point, with grid current, on the existing triode device) and an **active band EQ** tone network (the one-op-amp graphic-EQ topology: per band a pot between the op-amp's inputs, its wiper to a gyrator low shelf, two RLC mids and an RC treble). Gain ranges are levelled like the earlier circuits (+10 dB at full drive for a 0.1 V note; the clean boosts about +20 dB). CPU at defaults is in the range of the earlier circuits (3.8–8.5 % of a core in the catalogue test, the same run puts the RAT at 8.6 %); the Tube Drivers cost 21–34 %, between the Face Fuzz and the Tone Bender, because the triode needs Newton at 4x (2x aliased too much).

🆕 **Round 4, group D3** (`DriveCircuits.Round4Fuzz`, appended after group D2 (types 52–63)): **Master Fuzz** (Maestro FZ-1A Fuzz-Tone: a starved germanium gain stage into a Face-style pair on 3 V, Drive is the Attack), **Super Fuzz** (Univox Super-Fuzz: silicon gain stage, a soft full-wave rectifier for the octave up, a diode-clipped output stage; Tone sweeps the tone switch's mid scoop, deepest at noon), **Hard Fuzz** (a design: rails slammed into 1 + 2 silicon diodes), **Tape Distortion** (new stage kind `Tape`: the Cab preamp's 50 µs record pre-emphasis, tape saturation with antiderivative antialiasing, exact playback de-emphasis), **Bit Crusher** (new stage kind `Crusher`: Drive takes 16 bits down to 3 and the sample-and-hold rate down to 1.5 kHz), **SDD Preamp** (Korg SDD-3000 input stage: a 4558 on ±15 V with treble emphasis, clipping on its rails), **FET Preamp**, **Integral Pre** (a clean preamp with a real treble/bass/middle stack, so Bass Pot and Middle Pot show), **Micro Boost** (MXR Micro Amp: CA3130 with the gain pot in its ground leg, 0 to 26 dB), **Mid Boost**, **Bosom Boost** (Friedman Buxom Boost: clean op-amp boost to 27 dB, active treble) and **FAS Boost** (Fractal's own: a design, flat to 21 dB with a slight presence lift). Against the guide (June 2023, pp. 37–39): **Super Fuzz** and **Integral Pre** are not in its list (later firmware, presumably); the guide gives Bosom Boost as the Buxom Boost, Master Fuzz as the Maestro Fuzz-Tone, Micro Boost as the Micro Amp and SDD Preamp as the preamp of the SDD digital delay, and calls Hard Fuzz, Mid Boost, FAS Boost, FET Preamp and Bit Crusher its own designs, so those five and Integral Pre are designs here too. What was guessed per type is in [09, stream D3](09-parity-tracker.md#d3-drive-round-4-fuzz-boosts-preamps-and-digital).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Drive, Tone, Level, Mix | ✅ | Per-pedal panel names are generic |  |
| Basic | Per-type extra knobs (e.g. Bass/Treble, Presence) | 🆕 ◐ | 🆕 Bass Pot and Middle Pot on the Guv'nor (its stack's own pots) and on the active EQs (Metal Zone low/middle, B7K low/low-mid, Xotic and 4-knob Tube Driver bass); 🆕 the Timmy's **Bass** (a pot across its gain stage's ground-leg capacitor) with its Tone labelled **Treble**, the Zendrive's **Voice** (a pot in series with its ground leg) and the OCD's **HP/LP** switch (a resistor across its clipper's series resistor); 🆕 Middle Freq on the Metal Zone; 🆕 Grunt and Attack on the B7K; each stock at its default; the other circuits model one tone control, and the generic EQ 1 page stands in | medium per circuit (B7K hi-mid and Blend, Riot Voice) |
| EQ 1 | Low Cut, High Cut | 🆕 ✅ | 12 dB/oct, off at their ends | |
| EQ 1 | Bass, Mid (with frequency), High Mid, Treble ±12 dB | 🆕 ✅ | Low shelf 100 Hz, peak at Mid Freq (200 Hz–5 kHz), peak 2.5 kHz, high shelf 5 kHz | |
| EQ 2 | 10-band 2/3-octave graphic EQ, Pre/Post/Off | 🆕 ✅ | The shared `GraphicEqualizer` (2/3-octave layout) | |
| Advanced | Clip Type (4558/diode, FET, rectifier, Ge, hard, HV/LV tube, LED, op-amp, Si, soft, variable, null) | 🆕 ✅ | Voicings: Soft, Hard, Asymmetric, Germanium, LED, Tube, Op-Amp, 🆕 FET, Rectifier, Variable, HV Tube, LV Tube, Silicon, 4558 Diode, None; circuits: implied by the circuit (see diodes) | |
| Advanced | Clip Shape | 🆕 ✅ | Voicings: bends the curve towards a soft knee or a hard clip | |
| Advanced | Diode ± type and quantity | 🆕 ✅ | Circuits with clipping diodes: Si, Ge, Schottky, LED, MOSFET, 1–4 per side; variants prebuilt | |
| Advanced | Bass Response | 🆕 ✅ | Scales the input coupling corner (voicing pre-filter, circuit coupling cap) | |
| Advanced | Dry Level (dry inside the circuit) | 🆕 ✅ | Summed after the clipping, before the tone | |
| Advanced | Bias | 🆕 ✅ | Offset ahead of the clipping, DC-blocked after | |
| Advanced | Slew Rate | 🆕 ✅ | 🆕 Per op-amp: the RAT's LM308 device slews at it, a linear op-amp stage limits its output to it; a slew limiter after the clipping on the other types | |
| Advanced | Bit Reduce, Sample Rate | 🆕 ✅ | Word length and sample-and-hold rate on the driven signal | |
| — | Input Vol (guitar volume knob) | ✅ | Cathode extra |  |
| Advanced | Oversampling (Global, 1x, 2x, 4x) | 🆕 ✅ | With the global linear-phase mode it sets the latency (§2.1) |  |

### 6.6 Dynamic Distortion (p. 40) → `dyndist`

Cathode's block distorts by envelope (drive follows the level) rather than through the guide's frequency-shaped filter. Types 7 presets → 3 clip shapes.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Preset page | 🆕 ✅ | Type selects the clip shape (🆕 14 curves); 🆕 seven Block Library presets set the filter as the guide's types do | — |
| Filter Type (high shelf, low shelf, peaking), Frequency, Q, Gain | 🆕 ✅ | Boosts the band ahead of the clipper, the inverse filter after: clean signals stay flat, the band clips first | |
| Clip Shape | ✅ | Type |  |
| Bias | 🆕 ✅ | DC-blocked after the clipper | |
| Drive | ✅ |  |  |
| Mono/Stereo | 🆕 ✅ | Mono sums the input and runs one channel | |
| Level, Pan | ✅ | Level and Balance |  |
| — | Dynamics, Threshold, Attack, Release, Mix | ✅ | Cathode extras |  |

### 6.7 Enhancer (p. 41) → `enhancer`

Types: Modern, Classic, Stereoizer → Cathode Modern, Classic (a delayed-mid side signal, mono compatible), Haas (the guide's Classic: right channel delayed), 🆕 Stereoizer (a side from 24 third-octave allpasses: narrow bands alternate left and right, complementary and mono compatible).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Modern: Width, Depth | 🆕 ✅ | Depth is the allpasses' sharpness; 50 % is the earlier voicing |  |
| Modern: Low Cut, High Cut | ✅ |  |  |
| Classic: Width (0–20 ms) | ✅ | Time (Haas) |  |
| Classic: Invert | 🆕 ✅ |  |  |
| Classic: Pan L, Pan R | 🆕 ✅ |  |  |
| Classic: Balance | 🆕 ✅ |  |  |
| Stereoizer: Width, Depth | 🆕 ✅ | As Modern, through the high-order chain |  |

### 6.8 Filter (p. 42) → `filter`

Types: guide 15 → Cathode 🆕 15: Low Pass, High Pass, Band Pass, Notch, Peaking, Low Shelf, High Shelf, Null, All Pass, Tilt EQ, FF Comb, FB Comb and 🆕 Low Shelf 2, High Shelf 2 (Q up to 4, so they overshoot at the corner as passive shelving networks do) and Peaking 2 (proportional Q: the peak narrows as its gain grows, Q × √gain).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type, Frequency, Q, Gain | ✅ |  |  |
| Order (2nd/4th) | ✅ |  |  |
| Feedback (all-pass) | 🆕 ✅ | ±95 %, output back to the input one sample later (around both sections at 4th order) |  |
| Low Cut, High Cut | 🆕 ✅ | Now first-order, as in the guide (shared `FirstOrderCuts`) |  |
| Pan L, Pan R | ✅ |  |  |
| Phase Invert (none, L, R, both) | ✅ |  |  |
| Comb Delay Time, Depth | ✅ |  |  |
| LFO enable, type, rate, Mod Frequency | ✅ | Plus tempo |  |
| LFO duty, quantise | 🆕 ✅ | Duty 0–100 %; Quantize off or 2–16 levels (shared `LfoConditioner`) |  |
| Level, Balance | ✅ |  |  |

### 6.9 Flanger (pp. 43–45) → `flanger`

Types: guide 31 named presets → Cathode 1 algorithm, now with 🆕 thru-zero and the VCO responses, so the named types are presets of its knobs (🆕 in the Block Library, §8 item 7).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Rate, Depth, Feedback (±), Mix | ✅ |  |  |
| Basic | Tempo | ✅ |  |  |
| Basic | Manual (0.01–10 ms) | ✅ | `delay` (0.1–10 ms), now named Manual |  |
| Basic | Low Cut, High Cut | 🆕 ✅ | First-order (6 dB/oct), as in the guide |  |
| Basic | Bass Focus | 🆕 ✅ | First-order high-pass (20 Hz = off … 1 kHz) on what enters the line: the lows stay dry |  |
| Basic | Drive | 🆕 ✅ | Saturation in the loop (`SoftDrive`) |  |
| Expert | Minimum / Maximum Time | 🆕 ✅ | Keep the sweep (Manual to Manual + depth) inside the range; defaults never bind |  |
| Expert | Thru-Zero (off, auto, manual), Dry Delay | 🆕 ✅ | The dry is delayed too: by Dry Delay (Manual) or the middle of the sweep (Auto); with Phase Reverse the wet cancels it where they meet |  |
| Expert | Phase Reverse | ✅ |  |  |
| Expert | LFO Type | ✅ |  |  |
| Expert | LFO Phase | ✅ | `spread` |  |
| Expert | LFO Bypass Reset | 🆕 ✅ | `lfoReset`: engaging restarts the sweep (`IEngageAware`) |  |
| Expert | LFO Hicut, LFO Quantize | 🆕 ✅ | `LfoConditioner`: one-pole on the LFO (0.5–50 Hz, 50 = off), 2–16 levels |  |
| Expert | Auto Depth | 🆕 ✅ | As on the Chorus |  |
| Expert | VCO Response, Exponent | 🆕 ✅ | Linear (in delay) or Exponential (in ratio, notches move evenly in octaves); Exponent 0.25–4 bends the LFO position |  |
| Mix | Stereo Spread | 🆕 ✅ | −200…200 % on the wet (§3), besides LFO Phase |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |

### 6.10 Formant (p. 46) → `formant`

| Parameter | Cathode | Note |
|---|---|---|
| Start, Middle, End Vowel (10 vowels) | ✅ | Same set of vowels |
| Resonance | ✅ |  |
| Control | ✅ |  |
| Mix, Level, Balance | ✅ / 🆕 |  |
| — Shift | ✅ | Cathode extra |

Verdict: **complete**.

### 6.11 Gate/Expander (p. 47) → `gate`

Types: Classic Expander, Classic Gate, Modern Expander, Modern Gate → Cathode Expander, Gate, 🆕 Modern Expander, 🆕 Modern Gate (6 dB of hysteresis: once open they close only that far under the threshold, so a decaying note never chatters).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Threshold, Ratio, Attack, Hold, Release | ✅ |  |  |
| Attenuation (gate types) | 🆕 ✅ |  |  |
| Knee Type (modern expander) | 🆕 ✅ | Hard, Soft (6 dB), Softer (12 dB) on both expander types |  |
| Sidechain Select (block, row, input) | 🆕 ✅ | As the Compressor's, 🆕 hardware inputs included |  |
| Detector Type (peak, RMS) | 🆕 ✅ |  |  |
| Sidechain Low Cut, High Cut | 🆕 ✅ |  |  |
| Level, Balance | 🆕 ✅ |  |  |

### 6.12 Graphic EQ (p. 48) → `geq`

Types: guide 16 (10-band, 10-band 2/3-octave, 8-, 7- and 5-band, each constant or variable Q, plus passive 5-band Mark, 5-, 4- (JMPre-1) and 3-band, 3-band console) → Cathode 🆕 10 layouts (`GraphicEqualizer`: 10 octave, 10 at 2/3 octave, 8, 7, 5 bands, each with constant or variable Q; Mark, 5-, 4- and 3-band passive, console), so all 16. The band parameters keep their ids and are renamed and hidden per type (`typeLabels`).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| 10 octave bands, ±12 dB | ✅ |  |  |
| Outermost bands as shelves | 🆕 ✅ | Outer Shelves switch; off by default, and the global output EQ stays on plain peaks |  |
| Band layouts (10 at 2/3 oct, 8, 7, 5) | 🆕 ✅ | Type; the editor shows each layout's bands under their own names |  |
| Variable Q | 🆕 ✅ |  |  |
| Passive types (Mark, 5/4/3-band, console) | 🆕 ✅ | Component-level networks (`PassiveGraphicEq`): Mark and 5-band, an inductor graphic (op-amp, sliders between its inputs, each wiper into a series L-C-R; the Mark on the Mesa IIC+ parts, 88–4 824 Hz, the input resistors and the inductors' resistance assumed); console, three broad resonators of the same circuit; 3-band, the Marshall stack; 4-band (JMP-1), that stack plus a presence network. Band gains are slider and pot positions, so bands interact and overshoot as the circuits do. The amp's output EQ keeps the approximation |  |
| Master Q | 🆕 ✅ |  |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.13 IR Player (p. 49) → `irplayer`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Two IR slots (bank, number) | 🆕 ✅ | `ir` and `ir2`, baked into one stereo pair on the control thread and crossfaded (as the Cab) |  |
| Length (MAX…256, OFF) | 🆕 ✅ | Per slot, 5–1,366 ms with a short fade (the top is the whole IR, up to 65,536 samples as the Cab's FullRes); a slot's level mutes it |  |
| Per-IR Level, Pan | 🆕 ✅ | Balance-law pan |  |
| Mix | ✅ |  |  |
| Low Cut, High Cut | ✅ |  |  |
| Filter Slope | 🆕 ✅ | 6–24 dB/oct on each cut |  |
| Input Select, Mode (stereo, parallel, series) | 🆕 ✅ | Input: stereo, L+R, left, right. Mode: Parallel (summed), Stereo (IR 1 left, IR 2 right), Series (their convolution, offline by FFT, cut to 680 ms) |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.14 Looper (pp. 50–51) → 🆕 `looper` grid block, and the rig looper (`Cathode.Engine`)

One core (`Cathode.Dsp.Looping.Looper`) serves both. The grid block takes the transport (toolbar, REST `/looper`, MIDI and footswitches) while its preset is running; without one, the rig looper at the end of the chain does, as before. The rig looper keeps the default Config (no params of its own).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Record, Play, Undo, Reverse | ✅ | Toolbar, MIDI and footswitch actions; Undo also divides out the pass's overdub level |  |
| Erase | ✅ |  |  |
| Once (one-shot) | 🆕 ✅ | Playback stops at the end of the loop |  |
| Trim Start, Trim End | 🆕 ✅ | % of the loop; playback, reverse and overdub stay inside the window |  |
| Threshold recording | 🆕 ✅ | Record arms (state `armed`); the recording starts on the first sample above the level |  |
| Max Loop Time | 🆕 ✅ | 1–300 s on the block (memory committed only as it is recorded); the rig looper keeps 60 s |  |
| Playback Level, Dry Level | 🆕 ✅ | Block only, ramped per sample |  |
| Overdub Level (loop decay) | 🆕 ✅ | Each pass scales what the loop held; at 0 % Undo cannot bring it back |  |
| Crossfade | 🆕 ✅ | The playing that follows a closing recording is faded into the loop's first samples (up to 100 ms), so the seam continues the performance; only on a forward first pass at normal speed |  |
| Loop Quantize (to tempo) | 🆕 ✅ | The closing length is rounded to whole beats of the tempo (quarter notes), padded with silence |  |
| Record 2nd Press (overdub, play, stop) | 🆕 ✅ | Default Play, which is what the rig looper always did |  |
| Speed | 🆕 ✅ | Normal, Half, Double; the Half Speed action halves whichever is set |  |
| Low Cut, High Cut | 🆕 ✅ | On the loop's playback |  |
| As a grid block (placed mid-chain, bypass modes) | 🆕 ✅ | `looper`, one per preset. 🆕 A preset change to another preset with a Looper block hands the loop over, still playing (§9); to one without, it stops, where the rig looper plays through. Bypassed in Thru or Mute the block stops running, so the loop pauses; the other modes keep it playing (Mute FX In: the loop plays on while nothing new reaches it) |  |

Also new: play and stop fade over 2 ms instead of cutting.

### 6.15 Megatap (pp. 52–53) → `megatap`

Types: 20 presets → none (shape choices instead; presets are §8 item 7).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Master Level | ✅ | Level |  |
| Config | Delay Time (last tap), Tempo | ✅ |  |  |
| Config | Number of Taps (1–128) | 🆕 ✅ |  |  |
| Config | Predelay | 🆕 ✅ | Up to 1 s before the first tap |  |
| Config | Feedback, Feedback Tap | 🆕 ✅ | The chosen tap re-enters the line through the low and high cut |  |
| Config | Low Cut, High Cut | ✅ |  |  |
| Tap control | Time Alpha | 🆕 ✅ | The former Time Shape (`timeAlpha`) |  |
| Tap control | Time Randomize | 🆕 ✅ | Up to half a tap spacing, fixed per tap |  |
| Tap control | Amplitude Shape | ✅ | Level Shape |  |
| Tap control | Amplitude Alpha, Randomize | 🆕 ✅ | Alpha raises the shape to a power of 1/4…4 |  |
| Tap control | Pan Shape | ✅ |  |  |
| Tap control | Pan Alpha | 🆕 ✅ |  |  |
| Tap control | Stereo Spread | 🆕 ✅ | −100…100 % (negative mirrors) |  |
| Diffusion | Diffusion Mix, Time, LFO Rate/Depth | 🆕 ✅ | `Diffuser` |  |
| Envelope | Threshold, Attack, Release (volume swell into the taps) | 🆕 ✅ | `SwellEnvelope`; attack 0 is off |  |
| Mix | Input Gain, Input Select | 🆕 ✅ |  |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |

### 6.16 Mixer (p. 54) → 🆕 `mixer`

| Parameter | Cathode | Note |
|---|---|---|
| Gain Rows 1–6 (0–100 %) | 🆕 ✅ | Glides at full scale per 20 ms |
| Balance Rows 1–6 | 🆕 ✅ |  |
| Mono/Stereo | 🆕 ✅ |  |
| Output Level, Output Balance | 🆕 ✅ |  |
| Cannot be bypassed | — | Bypass works as for any block |

Verdict: **complete**. The graph compiler hands each cabled input to the block by row (`IRowInputBlock`), as for the Multiplexer.

### 6.17 Multitap Delay (pp. 57–62) → `multidelay`

Core types: Quad-Tap, Quad Parallel, Quad-Series, Quad Diffusor, Quad Tape, Quad Tap Band, plus 28 variants → Cathode 🆕 5: Quad Tap, 🆕 Quad Parallel, 🆕 Quad Series, 🆕 Quad Diffusor, 🆕 Quad Tape. Quad Tap Band is Quad Tap with band-pass voice filters. Ten Tap and Plex moved to their own blocks (§6.32, §6.23), Ducking, Reverse and Sweep to the Delay (§6.4).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Master | Master Time | ✅ | Voice times are % of it |  |
| Master | Master Feedback, Level, Pan | 🆕 ✅ | Master Feedback scales every voice and cross feedback (0–200 %, limited) |  |
| Master | Master LFO rate/depth, filter freq/Q, chorus, comb, ring mod scalers | 🆕 ✅ |  |  |
| Master | Drive | 🆕 ✅ |  |  |
| Diffuser | Diffusion Mix, Time, LFO | 🆕 ✅ | `Diffuser` on the wet |  |
| Ducker | Threshold, Attenuation, Release | ✅ | Every type (off by default) |  |
| Envelope | Threshold, Attack, Release | 🆕 ✅ |  |  |
| Delay | Time 1–4, Tempo 1–4 | 🆕 ✅ | A voice tempo overrides its share of the master time |  |
| Delay | Feedback 1–4, cross feedback 1→2…4→1 | 🆕 ✅ | Cross feedback on Parallel and Diffusor |  |
| Quad Series | Feedback, Send, Return | 🆕 ✅ |  |  |
| Level/Pan | Level 1–4 (± invert), Pan 1–4 | 🆕 ✅ | Level −100…100 % |  |
| EQ | Filter type/freq/Q/gain, low/high cut and slopes | 🆕 ✅ | Voice filter LP/HP/BP/peak; loop cuts 6–24 dB/oct |  |
| Modulation | LFO 1/2 type, phase, rate, tempo, depth | 🆕 ✅ | On every voice's time; the phase steps from voice to voice |  |
| Filter | Band-pass 1–4 frequency and Q, filter LFO | 🆕 ✅ | Filter LFO moves the voice filters by up to 4 octaves |  |
| Chorus | Per-voice chorus rate/depth | 🆕 ✅ |  |  |
| Comb | Comb time and feedback per line | 🆕 ✅ | Normalised (peaks at unity), so it never adds loop gain |  |
| Ring mod | Frequency and mix per line | 🆕 ✅ |  |  |
| Mix | Input Select, Input Gain | 🆕 ✅ |  |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |

### 6.18 Multiband Compressor (p. 55) → `mbcomp`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Crossover Frequency, Frequency Range | ✅ | Low and high split |  |
| Threshold, Ratio, Attack, Release, Level per band | ✅ |  |  |
| Detector (RMS/peak) | 🆕 ✅ | Peak, or RMS over 5 ms |  |
| Mute per band (solo by muting two) | 🆕 ✅ |  |  |
| Crossover Slope (12/24 dB) | 🆕 ✅ | LR4, or LR2 (one-poles squared, the high band inverted, so the bands still sum to an allpass) |  |
| Knee Type | ✅ | Knee (dB) |  |
| Auto Attack/Release | 🆕 ✅ | Per band, crest-factor scaling as the Compressor |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.19 Multiplexer (p. 56) → `mux`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Input Select: rows 1–6 | ✅ | Plus "All" |  |
| Input Select: inputs 1–4, USB 5/6 and 7/8 | 🆕 ✅ | After the rows and All: the hardware input replaces the rows with the same 10 ms fade. The block still needs a cable from the grid input to be in the graph |  |
| Level, Balance | ✅ / 🆕 |  |  |
| Gapless switching | ✅ | 10 ms crossfade |  |

### 6.20 Parametric EQ (p. 63) → `peq`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type: Shelving, Peaking | ✅ | Bands 1 and 5 shelf or peak; 2–4 peak |  |
| Type: Blocking (high-pass, low-pass) | 🆕 ✅ | Bands 1 and 5 |  |
| Type: Shelving 2, Peaking 2 | 🆕 ✅ | Bands 1 and 5: Shelving 2 a first-order shelf centred on the frequency, Peaking 2 a proportional-Q peak |  |
| Frequency, Q, Gain (±12 dB; ours ±24) | ✅ |  |  |
| Slope for blocking bands (6–36 dB/oct) | 🆕 ✅ | 12 dB/oct keeps the band's Q as resonance; the others are Butterworth |  |
| Solo | 🆕 ✅ | In the engine: one band's region alone (a peak's band-pass, band 1's low-pass, band 5's high-pass) |  |

### 6.21 Phaser (pp. 64–65) → `phaser` (and `vibe`)

Types: 16 (digital mono/stereo, Script 45/90, Block 90, Classic/FAS Vibe, Stereo 8-stage, Barberpole, Stripe 90, …) → 🆕 2 algorithms (Classic and Barberpole, a `type` parameter) with the VCR element models; the Uni-Vibe is its own `vibe` block. The named types are presets (§8 item 7).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate, Depth, Feedback (±), Mix | ✅ |  |  |
| Tempo | ✅ |  |  |
| Manual | ✅ | `center`, now named Manual |  |
| Tone (wet) | 🆕 ✅ | A ±6 dB tilt of the wet around 1 kHz |  |
| Order (2–12) | ✅ | `stages` |  |
| Feedback Point | 🆕 ✅ | After the last stage or the middle one |  |
| Minimum / Maximum Frequency | 🆕 ✅ | Bound the sweep (Manual ± Depth octaves); defaults never bind |  |
| LFO Type | ✅ |  |  |
| LFO Phase | ✅ | `spread` |  |
| LFO Quantize, Astable Beta, LFO Hicut | 🆕 ✅ | `LfoConditioner`; LFO Duty is the astable's curvature (beta) |  |
| VCR Type (linear, exponential, JFET, photocell), VCR Bias, Exponent, Photocell attack/release | 🆕 ✅ | Linear in Hz, exponential in octaves (the default, unchanged), JFET square law, photocell: an exponential law on a light level with its own attack and release (1–200 / 5–1000 ms); Bias shifts the LFO position, Exponent bends it |  |
| LFO Reset on Bypass | 🆕 ✅ | `lfoReset` (`IEngageAware`) |  |
| Barberpole: Sweep Direction, Filter Q | 🆕 ✅ | Two chains of second-order allpasses (Q 0.3–10) sweeping the range on a sawtooth half a cycle apart, crossfaded by sin² windows that sum to one: a Shepard-style endless rise or fall. Coefficients on an 8-sample grid |  |
| Global Mix | 🆕 ✅ | §3 |  |

### 6.22 Pitch (pp. 66–76) → `pitch`

Types: 16 (dual/quad detune, dual/quad chromatic and quad chromatic delay, dual/quad diatonic and quad diatonic delay, custom shift, arpeggiator, classic/advanced whammy, octave divider, crystal echoes, virtual capo) → Cathode 9 (Detune, Chromatic, Diatonic, Whammy, Octave Divider, Crystals, Arpeggiator, Virtual Capo, Custom Shifter) with four voices. The dual/quad and "delay" variants are voice counts, voice delays and 🆕 per-voice feedback. 🆕 The new parameters carry `typeLabels`, so the editor shows each only on the types it applies to.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Input Mode (mono, stereo) | 🆕 ✅ | Stereo: voices 1 and 3 shift the left input, 2 and 4 the right (two PSOLA analysis buffers); the single-voice types keep the sides apart. The detector always hears both |  |
| Config | LFO rate/tempo/depth on detune | 🆕 ✅ | A sine sways each voice's detune by up to ±50 cents, the voices half or a quarter cycle apart |  |
| Master | Master Level, Pan, Feedback, Pitch, Delay | 🆕 ✅ | Scale every voice's level (dB), pan, feedback, shift (Detune and Chromatic) and delay |  |
| Detector | Source (block, input 1–4) | 🆕 ✅ | Detector Source: the block input, grid row 1–6 (`IRowInputBlock`; the chosen row feeds only the tracker) or 🆕 a hardware input (Input 1–4, USB 5/6, 7/8) |  |
| Detector | Pitch tracking (fast, smooth, off) | 🆕 ✅ | Smooth (0.15-semitone hysteresis, the old behaviour), Fast (0.05, shorter hold), Off (free splices, harmony holds) |  |
| Detector | Tracking (splice length) | 🆕 ✅ | Splice Length 8–40 ms (the shifters' minimum splice) |  |
| Detector | Temperament (equal, just) | 🆕 ✅ | Just: five-limit ratios on the key for Diatonic and Custom Shifter (`Scales.JustRatio`) |  |
| Detune | Detune 1–4 | 🆕 ✅ | Four voices, each ±50 cents, own shifter; silent voices cost nothing |  |
| Detune | Delay 1–4 | ✅ | Voice delays |  |
| Detune | Feedback mode, Feedback 1–4 | 🆕 ✅ | ±90 %. Delay mode repeats a voice through its own delay line; Shifter mode feeds it back into its shifter (the harmony voices share one input, their sum capped at 95 %) so each repeat shifts again |  |
| Chromatic | Shift 1–4, Level 1–4, Pan 1–4, Delay 1–4 | ✅ |  |  |
| Chromatic | Delay Tempo 1–4 | 🆕 ✅ | A tempo division replaces the voice's delay (up to 1 s) |  |
| Diatonic | Key, Scale, Harmony 1–4 | ✅ | Plus key from MIDI or auto |  |
| Diatonic | Learn | 🆕 ✅ | While on, the key follows the note played; once off, that key stays until Key is moved (the block cannot write its own Key parameter). Key Source Auto remains for a key estimated from a melody |  |
| Diatonic | Glide Time | 🆕 ✅ | 0–1000 ms, in log frequency |  |
| Diatonic | Custom scale (notes, tonic) | 🆕 ✅ | Scale Custom: Number of Notes 4–8, Note 1–8 as degrees above the tonic (the root always included, repeats dropped), Tonic only names them (the Pitch panel lists the notes in it); Auto and MIDI key sources treat it like any scale |  |
| Arpeggiator | Run, Steps, Tempo, Stage shifts | 🆕 ✅ | 16 steps |  |
| Arpeggiator | Repeats, Amplitude/Pan shape and alpha | 🆕 ✅ | Repeats infinite or 1–8 (restarting on each new note, the playing rising through −40 dBFS); amplitude Flat, Decay, Swell, Triangle with alpha as the curve; pan Center, Alternate, Ramp, Random with alpha as the width |  |
| Whammy | Mode (classic), Start/Stop shift, Control | ✅ | Start, End, Pedal |  |
| Whammy | Tracking mode (mono, poly, off) | 🆕 ✅ | Mono (period-synchronous splices), Off (free splices) and Poly: a peak-locked phase vocoder (`PhaseVocoderShifter`, 2048-point frames at 48 kHz, 43 ms wet delay) that shifts chords cleanly; transients smear by up to a frame |  |
| Crystals | Feedback type, splice time/tempo, direction, diffusion | 🆕 ✅ | Shifted or unshifted feedback, splice 20–200 ms or a tempo division, reverse or forward grains, a four-allpass diffuser in the loop |  |
| Custom Shifter | Scale 1/2, Key | ✅ |  |  |
| Virtual Capo | Shift, Detune | 🆕 ✅ | Capo Detune ±50 cents |  |
| Mix | Input Gain | 🆕 ✅ | The level into the shifters (the dry and the detector are unaffected); 🆕 Input Select (L+R, Left, Right) for the mono input |  |
| Mix | Low Cut, High Cut (wet) | 🆕 ✅ | `WetCuts` |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |
| Formant | Formant correction and shift (firmware 33; not on the guide's Pitch pages) | 🆕 ✅ | Formant Correction (off by default: every type renders exactly as before) keeps the input's spectral envelope while the pitch moves; Formant Shift 1–4 (±12 st, per voice; one "Formant Shift" on the single-voice types) moves it. Whammy Mono/Off, Arpeggiator and Virtual Capo: linear prediction whitens the input before the splice shifter and the envelope goes back on after it (`FormantCorrector`: order 24 at 48 kHz, 21 ms frames every 10.7 ms, a 120 Hz lag window, a −50 dB floor, a level guard for spectrum changes within the shifter's delay; about 0.5 % of a core per channel). Whammy Poly: each phase-vocoder bin is scaled to the same all-pole envelope at its new frequency (`SpectralEnvelope`). Chromatic, Diatonic, Custom Shifter: PSOLA keeps the formants of itself, so the control adds the shift (each voice's grains resampled by its ratio). Hidden on Detune, Octave Divider, Crystals. Shifting down cannot restore an envelope above what the input carries, and an octave up with correction is 5–6 dB quieter than without (half as many harmonics under the same envelope) |  |

### 6.23 Plex Delay (pp. 77–79) → 🆕 `plex`

Types: Plex Delay, Plex Detune, Plex Shifter, Plex Verb, Shimmer Verb + 39 variants → 🆕 the five core types; the variants are presets. Up to eight lines on a Householder feedback matrix (orthogonal for any line count), a Schroeder allpass in each loop, loop gains from the decay time. Type-specific pages show only on their types.

| Parameter | Cathode | Note |
|---|---|---|
| Number of Delays (up to 8) | 🆕 ✅ | 1–8 |
| Decay Time | 🆕 ✅ | T60 in seconds, per line from its loop length |
| Diffusion | 🆕 ✅ | Loop allpass gain |
| Low Cut, High Cut | 🆕 ✅ | In the loop |
| Pre-Delay | 🆕 ✅ |  |
| Stack/Hold | 🆕 ✅ |  |
| Master Time / Level / Pan | 🆕 ✅ |  |
| Delay Time 1–8, Tempo 1–8, Level 1–8, Pan 1–8 | 🆕 ✅ | With no diffusion a line's first echo lands exactly on its time |
| Filter (type, freq, Q, gain), LFO, Mod Frequency | 🆕 ✅ | Mod Frequency is the LFO's swing in octaves |
| Envelope follower | 🆕 ✅ | `SwellEnvelope` on the input |
| Detune 1–8, Granule Length, Master Detune | 🆕 ✅ | Granular shifters in the loops (`GrainShifter`) |
| Shift 1–8, Direction | 🆕 ✅ | Shifts ±24 st; Direction plays every granule backwards with the granules in order, as the guide describes, and keeps the shift |
| Plex Verb Size, Decay; Shimmer Intensity, Crossfade | 🆕 ✅ | Crossfade is the grains' crossfade length |
| LFO rate/depth/tempo, input diffusion, ducker | 🆕 ✅ |  |
| Input Gain, Input Select | 🆕 ✅ |  |

### 6.24 Realtime Analyzer (p. 80) → 🆕 `rta` block and `PUT /api/v1/rta`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Tap anywhere on the grid | 🆕 ✅ | The `rta` block passes its input through and fills a second telemetry ring; while one runs, the RTA shows it (frames say `source: block`) |  |
| Decay Time | 🆕 ✅ | 0–10 s: levels fall 60 dB per decay time, rise at once |  |
| Input Select | 🆕 ✅ | On the block |  |
| Bands (32/64/128) | 🆕 ✅ | 3, 6 or 12 per octave (about 30, 60, 120 bands from 20 Hz to 20 kHz) |  |
| Window Type | 🆕 ✅ | Hann, Blackman-Harris, flat top, rectangular (energy-normalised, so levels hold) |  |

### 6.25 Resonator (p. 81) → `resonator`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Mode (manual, chord) | ✅ | Tuning |  |
| Master Freq / chord root, chord type | ✅ |  |  |
| Master Level, Pan, Feedback, Q | 🆕 ✅ | Master Level (dB), Master Pan and Master Feedback (scale every voice), Master Q (scales the band-passes); Decay and Damping remain the loop's design |  |
| Input Mode (mono, stereo) | 🆕 ✅ | Stereo runs a second set of combs: left rings left, right rings right, pans become balances |  |
| Frequency 1–4 | ✅ |  |  |
| Feedback 1–4 | 🆕 ✅ | Scale each voice's loop gain (100 % = what the decay asks for) |  |
| Filter Location 1–4, Filter Q 1–4 (band-pass) | 🆕 ✅ | Off, Pre (only the input near the pitch excites the comb) or Post (the comb rings with its fundamental); Q 0.5–20 |  |
| Level 1–4, Pan 1–4 | ✅ |  |  |
| Input Gain | ✅ |  |  |
| Global Mix | 🆕 ✅ | §3 |  |

### 6.26 Reverb (pp. 82–85) → `reverb`

Types: 66+ (halls, chambers, rooms, cathedrals, studios, plates, springs, classic digital) → Cathode 🆕 11 (Room, Hall, Plate, 🆕 Chamber, 🆕 Cathedral on an FDN of 4–16 lines with per-type early reflections; Dattorro Plate; Spring with one to six springs; and the classic digital topologies, appended at indices 7–10: 🆕 **Classic Hall** and 🆕 **Classic Plate**, a Lexicon 224/480-style ring of four sections (modulated allpass, delay, loss filter, allpass, delay), input diffusers and a multi-tap stereo output, after Griesinger's tank as Dattorro's *Effect Design, Part 1* (JAES 1997) publishes it, with sine "spin" plus random "wander" modulation (`RingTank`); 🆕 **Vintage Digital**, Schroeder's parallel combs and series allpasses (JAES 1962) with Moorer's low-pass combs (CMJ 1979) behind a 10 kHz bandwidth, the first-generation (EMT 250-era) sound (`CombReverb`); 🆕 **Classic Room**, Gardner's nested-allpass small room (1992/1998) for short ambiences (`NestedRoom`)). The guide's reverb pages do not list the type names (only that there are dozens, classic digital ones among them); the named classic types are presets of these topologies in the Block Library (Classic Concert Hall, Classic Large Hall, Random Hall, Rich/Small/Vocal Plate, Early Digital Hall/Room, Ambience, Small Ambience, Recording Studio, Bright Room). Section lengths are ours (the hardware's are not published): these are the topologies, not clones of particular units.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Type | Classic digital types | 🆕 ✅ | Classic Hall, Classic Plate (ring), Vintage Digital (combs), Classic Room (nested allpasses), appended so stored indices keep their types; the seven earlier types render bit-identical. Each loop's gain comes from its length, so Decay is the measured T60 (within 20 %; Classic Room down to about 0.3 s, below which its allpasses' own ringing sets it). A 5 Hz DC blocker sits ahead of these tanks, whose loops recirculate DC. Cost at 48 kHz / 64 (`Cathode.Bench --only reverb`): Classic Hall 0.49 %, Classic Plate 0.49 %, Vintage Digital 0.57 %, Classic Room 0.45 %, against 0.41 % for the FDN types |  |
| Basic | Time | ✅ | Decay |  |
| Basic | Predelay | ✅ | 🆕 Up to 1 s |  |
| Basic | Size | ✅ |  |  |
| Basic | Crossover Frequency, Low/High Freq Time | 🆕 ✅ | Low and High Crossover with Low/High Freq Time (% of Decay) in every FDN line (one-pole splits); High Cut still damps the tail. FDN types and the classic digital types (every ring section, every comb, the room's loop: the 224's bass multiplier and crossover); not Dattorro or Spring |  |
| Basic | Early Level, Late Level | 🆕 ✅ | A 12-tap reflection pattern per type, scaled by Size; Early Level defaults to 0 so the earlier sound is kept. Not on Spring. The classic types take the Hall (Classic Hall), Plate (Classic Plate), Chamber (Vintage Digital) and Room (Classic Room) patterns |  |
| EQ | Low Cut, High Cut | ✅ | High Cut = damping (on the classic types the loss filter in every ring section, comb or room loop) |  |
| EQ | Low Mid and High Mid peaking EQ | 🆕 ✅ | Frequency, Q, gain on the wet; 0 dB is skipped |  |
| Advanced | Pre-Delay Tempo, Feedback, Mix | 🆕 ✅ | Feedback repeats the pre-delay into the tank (to 90 %); Mix blends the direct input into the tank's input |  |
| Advanced | Echo Density, Early Diffusion and Time, Early Decay, Late Input Mix | 🆕 ✅ | Echo Density sets the length of the wall allpasses (on Classic Hall/Plate it scales the ring's in-loop allpass gains, 50 % = the voicing's); Early Time scales the pattern, Early Decay tapers it (100 %: last tap −60 dB), Late Input Mix feeds the reflections into the tank |  |
| Advanced | Quality (economy … ultra-high) | 🆕 ✅ | Economy 4, Normal 8 (default), High 12, Ultra High 16 lines, level-matched. FDN types only (hidden on the others) |  |
| Advanced | Stack/Hold | 🆕 ✅ | Hold closes the input; Stack keeps it open (the FDN's line writes, and the classic tanks' loop writes, pass a soft limit near +12 dBFS) |  |
| Advanced | Wall Diffusion, Input Diffusion and Time | 🆕 ✅ | Wall Diffusion: an allpass in every loop, taken out of the line so the decay is kept (default off; FDN types only); Input Diffusion and Time scale the type's diffusers (Classic Hall/Plate: the four input allpasses; Vintage Digital: the two series allpasses; hidden on Classic Room, whose nested allpasses are its structure) |  |
| Advanced | Mic Spacing | 🆕 ✅ | Delays the right wet by up to 20 ms |  |
| Spring | Number of Springs, Spring Tone, Drive, Boiiinnng | 🆕 ✅ | 1–6 springs (even ones left, odd right); Tone moves the springs' low-pass an octave either way; Drive saturates the springs' input; Boing drives them up to 5× harder on attacks (a fast-against-slow envelope transient detector), our reading of the control |  |
| Modulation | Mod Depth, Rate, LFO Phase | 🆕 ✅ | Depth as % of the former fixed 0.35 ms, rate, and the phase between the two modulated lines (default 180°, the former setting). FDN types; Dattorro keeps its own fixed modulation. Classic Hall/Plate: every ring section's first allpass, Depth as % of 0.5 ms, 75 % sine spin (second half of the ring at LFO Phase) and 25 % random wander (a new target every half period); none on Vintage Digital or Classic Room |  |
| Pitch | Pitch mix, shift 1/2, voice balance, splice, feedback, direction, position | 🆕 ✅ | Two granular shifters (±24 st, grain = splice, Reverse plays the grains backwards); position Input, Output or Tank Feedback (shimmer: the pitched wet feeds the tank and climbs on each pass) |  |
| Ducker | Attenuation, Threshold, Release | 🆕 ✅ |  |  |
| Mix | Stereo Spread | 🆕 ✅ | −200 … 200 % (§3): beyond ±100 % the wet's side signal is raised (M/S widening), not a psychoacoustic model |  |
| Mix | Input Gain, Input Select | 🆕 ✅ |  |  |
| Mix | Global Mix | 🆕 ✅ | §3 |  |

### 6.27 Ring Modulator (p. 86) → `ringmod`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type (classic, two single-sideband) | 🆕 ✅ | Classic, Upper Sideband, Lower Sideband: a 90° allpass pair (`HilbertPair`, Niemitalo's coefficients) and a quadrature carrier; the other sideband is down by more than 40 dB |  |
| Frequency | ✅ | Plus tempo sync |  |
| Frequency Multiplier (0.25–4×) | ✅ |  |  |
| Pitch Tracking | 🆕 ✅ | The carrier follows the played note (a `PitchTracker`) times the multiplier, gliding in 10 ms; with SSB it is a harmonic frequency shifter |  |
| Hi Cut | ✅ |  |  |

### 6.28 Rotary (p. 87) → `rotary`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate (with park at 0), Tempo | 🆕 ✅ | Slow/Fast keep their own rates; 🆕 Brake parks both rotors with their deceleration; Tempo sets the horn to one turn per division and the drum keeps its ratio |  |
| Drive | ✅ |  |  |
| Hi Depth, Low Depth | ✅ | Horn and drum Doppler |  |
| Hi Level | ✅ | Horn and drum levels |  |
| Rotor Length | 🆕 ✅ | 50–200 %: the horn's radius, so its Doppler |  |
| Low Rate Multiplier | 🆕 ✅ | 0.25–4× on every drum rate (Slow, Fast, tempo), 1 by default; the drum's own rates stay as well |  |
| Low/Hi Time Constant | ✅ | Accel and decel for each |  |
| LF and HF Mic Spacing | 🆕 ✅ | Separate angles for the drum and horn mics |  |
| Mic Distance | 🆕 ✅ | 100 % is the far field (the old model); closer uses the exact path √(k² + 1 − 2k cos θ) and 1/r level, so the near pass is louder and the sweep lopsided |  |
| Stereo Spread | 🆕 ✅ | −100…100 % on the output, besides the mic spacing |  |
| Input Select | 🆕 ✅ | L+R, Left, Right |  |

### 6.29 Scene MIDI (p. 88) → 🆕 `Scene.Midi`

| Parameter | Cathode | Note |
|---|---|---|
| Eight messages per scene | 🆕 ✅ | `Scene.Midi`, up to eight; sent through the rig's MIDI output when the scene is selected or its preset loads on it |
| Type (PC, CC) | 🆕 ✅ |  |
| Channel | 🆕 ✅ | 1–16 |
| Number, Value | 🆕 ✅ | Program for a PC; controller and value for a CC |

Not a grid block: the messages belong to the scene, edited on the Controllers page.

### 6.30 Send and Return (pp. 89–90) → `fbsend`, `fbreturn`

| Parameter | Cathode | Note |
|---|---|---|
| Send: no parameters | ✅ | Cathode adds a send level |
| Return Level | ✅ | `level` (named Return Level) |
| Output Level | 🆕 ✅ |  |
| Balance | 🆕 ✅ |  |
| Two pairs | 🆕 ✅ | Two of each; a send feeds the return whose id ends in the same number (`fbsend2` → `fbreturn2`) |

### 6.31 Synth (p. 91) → `synth`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Three voices | 🆕 ✅ | Voice 3 starts silent |  |
| Waveform: sine, triangle, square, saw | ✅ |  |  |
| Waveform: random, white noise, pink noise, off | 🆕 ✅ | Random holds a level each cycle; pink is Kellet's filter on white; each voice has its own seed |  |
| Track: off, envelope only, pitch + envelope | 🆕 ✅ | Per voice. Envelope plays the voice's own Frequency with the input envelope; Off is a drone at −6 dB |  |
| Frequency (manual) | 🆕 ✅ | 20 Hz–10 kHz, for the Envelope and Off modes |  |
| Shift, Tune | ✅ |  |  |
| Duty Cycle | 🆕 ✅ | Per voice |  |
| Pan per voice | 🆕 ✅ | Constant power, unity at the centre |  |
| Filter frequency and Q per voice | 🆕 ✅ | Each voice has its own filter (type, cutoff, resonance), opening with the envelope (Env Amount shared) |  |
| Attack (input envelope) | ✅ | Plus release, glide, threshold |  |
| Level per voice | ✅ |  |  |
| Global Mix | 🆕 ✅ | §3 |  |

### 6.32 Ten-Tap Delay (pp. 92–93) → 🆕 `tentap`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type: Ten-Tap | ✅ |  |  |
| Type: Rhythm Tap (per-tap times, quantise, learn, feedback) | 🆕 ✅ | Learn (Listen/Hold) takes the pattern from the notes played; it lives in the block and does not rewrite the tap time parameters |  |
| Mono/Stereo | 🆕 ✅ |  |  |
| Delay Time, Tempo | ✅ |  |  |
| Number of Taps (1–10) | 🆕 ✅ |  |  |
| Decay | ✅ |  |  |
| Shuffle | ✅ |  |  |
| Spread, Ratio | 🆕 ✅ | Ratio sets the right taps' times against the left's |  |
| Pan Shape, Pan Alpha | 🆕 ✅ |  |  |
| Low Cut, High Cut | ✅ |  |  |
| Tap Level 1–10 | 🆕 ✅ |  |  |
| Input Gain | 🆕 ✅ |  |  |

### 6.33 Tone Match (p. 94) → `tonematch`

The guide refers to a separate manual. Cathode's block applies a matched minimum-phase filter with amount and level; matching runs through `POST /live/blocks/{id}/tonematch`. **Complete** for the guide's scope.

### 6.34 Tremolo/Panner (p. 95) → `tremolo`

Types: Tremolo, Panner, Bias Trem, Harmonic Trem, Optical Trem → all five.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate, Tempo | ✅ |  |  |
| Depth / Width (beyond 100 % for the panner) | 🆕 ✅ | 0–200 %; named Width on the Panner, where past 100 % the swing dwells at the sides; the tremolo types stop at 100 % |  |
| LFO Type, LFO Duty Cycle | ✅ |  |  |
| LFO Phase | ✅ |  |  |
| Start Phase | 🆕 ✅ | 0–360°, applied on engage (`IEngageAware`) |  |
| Trigger Threshold (restart on playing) | 🆕 ✅ | Trigger Level (−80 dB = off … 0 dB): the LFO restarts when the playing rises through it, re-arming 6 dB below |  |
| Crossover Frequency (harmonic) | ✅ | One-pole split |  |
| Crossover Slope | 🆕 ✅ | 6 dB/oct (complementary one-pole, the default), 12 and 24 dB/oct (Linkwitz–Riley, summing to an allpass) |  |
| Pan Center | ✅ |  |  |
| Level, Balance | ✅ |  |  |

### 6.35 Vocoder (p. 96) → `vocoder`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Analysis Source (an input) | 🆕 ✅ | Block Input, grid row 1–6 (`IRowInputBlock`: the chosen row feeds only the analysis, the others are the block input) or 🆕 a hardware input (a microphone on Input 2) |  |
| Analysis Channel | 🆕 ✅ | L+R, Left, Right; with a carrier from one input side, L+R means the other side |  |
| Number of Bands (up to 24) | 🆕 ✅ | 4, 8, 12, 16, 20 (default) or 24 |  |
| Min / Max Frequency | 🆕 ✅ | 50–1000 Hz and 1–16 kHz, log-spaced bands between |  |
| Filter Resonance | 🆕 ✅ | Band Q 0.5–20 (default 3) |  |
| Frequency Shift | 🆕 ✅ | 0.5–2×: the synthesis bands against the analysis bands (a formant shift) |  |
| Freeze | 🆕 ✅ | Holds the band envelopes |  |
| Highpass Mix | 🆕 ✅ | Adds the modulator above the top band back in; HF Emphasis stays as a tilt |  |
| Attack, Decay | ✅ | Attack, Release (now named Decay) |  |
| Master Level | ✅ | Level |  |
| Master Pan, Level 1–24, Pan 1–24 | 🆕 ✅ | Per-band level (dB) and pan (constant power); Master Pan scales the pans |  |
| Input Gain | 🆕 ✅ | The level into the analysis |  |

### 6.36 Volume/Pan (p. 97) → `volpan`

| Parameter | Cathode | Note |
|---|---|---|
| Type (Volume, Auto-Swell) | 🆕 ✅ |  |
| Volume | 🆕 ✅ |  |
| Threshold, Attack (auto-swell) | 🆕 ✅ |  |
| Balance | ✅ |  |
| Taper (Linear, Log 30A/20A/15A/10A/5A, S-Taper) | 🆕 ✅ |  |
| Input Select (stereo, left, right) | 🆕 ✅ |  |
| Pan Left, Pan Right | 🆕 ✅ |  |
| Bypass Mode | ✅ | Engine bypass modes |
| Level | ✅ |  |

Verdict: **complete**.

### 6.37 Wah (p. 98) → `wah`

Types: FAS Wah, Clyde, Cry Babe, VX846, Color-Tone, Funk Wah, Mortal, VX845, Paragon → Cathode Classic, Vox, Wide (filter voicings) and, at component level, 🆕 Clyde, Colorsound, Morley, V845, Parapedal, Cry Baby, V846.

🆕 **The five newer types are circuits now (stream W).** Clyde, Colorsound, Morley, V845 and Parapedal were filter voicings drawn from each pedal's reputation. They are now modelled from their circuits and **replace the voicings in place**: each keeps its type index (3 to 7) and its name, so a preset that chose one keeps it but now hears the circuit. Each circuit lands near the range its voicing had (table below), so no new types were added. The sound does change: these are real circuits, with the inductor wahs' loop-gain sweep (the Q falls toward the toe), transistor or op-amp clipping under Drive, and the Morley's photocell lag. The factory presets "Vocal" (Clyde) and "Funk Quack" (Colorsound) now play the circuits. The default type is Classic, so the golden render does not change.

- **Clyde, Colorsound, V845** are the Thomas Organ two-transistor inductor circuit, which Vox drew the same for the Clyde and the V846. Each is a `WahCircuit` with its own parts (`WahBlock.Circuits`), checked against the reference simulator like the Cry Baby and the V846.
- **Morley** is optical and has no inductor: a lamp behind a vane on the treadle lights a CdS cell, and the cell is the shunt resistor of a one-op-amp multiple-feedback band-pass, followed by a make-up stage. The bandwidth stays constant, so the Q rises toward the toe. The cell's resistance follows the vane's opening log-linearly and lags the treadle, faster as the light rises (4 ms) than as it falls (40 ms).
- **Parapedal** has no inductor either: two op-amps in a two-integrator loop, tuned together by the two gangs of a dual pot. Its Q comes from a fixed resistor ratio, so it stays the same over the whole sweep.
- Morley and Parapedal are `ActiveWahCircuit`s (`WahBlock.ActiveCircuits`). The linear circuit is solved exactly, as a trapezoidal two-integrator loop whose centre, Q and gain come from the parts. Each op-amp's output saturates softly at its rail, and the capacitors charge from that limited voltage. Each costs about a tenth of the Cry Baby. Inductor Bias has no inductor to act on in them, so it gives the shift it would give (up to 1.6×) through their capacitors. Min/Max Frequency also scales the capacitors. Resonance Q scales the Parapedal's damping resistor; on the Morley it scales R1 and R3 by its square and the capacitors by its inverse, which keeps the centre and the gain in place.

| Type | Old voicing (heel–toe, Q) | Circuit (heel–toe, Q heel → toe) | Parts (*guessed* in italics) |
|---|---|---|---|
| Clyde | 420 Hz–2.4 kHz, 7 | 454 Hz–2.31 kHz, 20 → 4.6 | V846 circuit, 100k across L1 (reported on originals; 33k and 68k were also fitted), *Halo inductor 450 mH / 30 Ω*, BC108 |
| Colorsound | 380 Hz–2.6 kHz, 4 | 378 Hz–2.46 kHz, 14 → 2.1 | Thomas circuit, BC108; *C3 8.2 nF, L1 470 mH, R3 220 Ω, R7 27k* (no traced values to hand; chosen for a wide, smooth sweep) |
| Morley | 320 Hz–3.4 kHz, 3 | 330 Hz–3.37 kHz, 0.8 → 8 | One-op-amp MFB band-pass with a CdS shunt; *R1 33k, R3 75k, C 10 nF, cell 300 Ω lit to 500k dark, vane law, cell time constants, make-up gain 1.5, 9 V* |
| V845 | 480 Hz–1.9 kHz, 4 | 464 Hz–1.92 kHz, 8 → 2.2 | Thomas circuit, BC108; *L1 660 mH / 60 Ω, R3 820 Ω, R7 27k* (cheaper parts, a narrower and milder sweep) |
| Parapedal | 280 Hz–2.2 kHz, 5.5 | 257 Hz–2.18 kHz, 4.7 throughout | Two op-amps, dual-gang pot; *25k gangs + 3.3k, 22 nF, damping 47k / 10k, input 22k, 9 V* |

Guessed beyond the table: the op-amps (4558-class, saturating 1.5 V below each rail, otherwise ideal), the Morley's light law and the Parapedal's damping network. The sources (forum traces, vintage-pedal histories and Morley's manuals) establish the topologies but give no complete value list for the Colorsound, V845, Morley or Parapedal. Tests: `WahPedalCircuitTests` (range and Q per pedal, the inductor circuits against 1 / 2π√(LC) and the loop-gain Q ratio, the op-amp circuits against their closed form, Min/Max, Inductor Bias, Resonance, EQ, Drive, the cell lag, and cost against the Cry Baby), plus the new entries in `WahCircuitTests` and `WahBlockTests`.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Wah Control | ✅ | Position |  |
| Control Taper | ✅ | Linear, log, reverse log |  |
| Resonance Q | 🆕 ✅ | Filter types scale their Q; 🆕 the inductor circuits scale R7, the resistor that damps the resonator; 🆕 the Morley and Parapedal their damping (see above) |  |
| Q Tracking | 🆕 ✅ | Less peak toward the toe; 🆕 on the circuits too, through R7 |  |
| Low Cut Frequency | ✅ |  |  |
| Minimum / Maximum Frequency | 🆕 ✅ | 50–200 % of the heel and toe frequencies: the filter voicings' directly, 🆕 the circuits' through their inductor, scaled per pedal position (Min at the heel, Max at the toe, geometrically between) |  |
| Drive | 🆕 ✅ | Filter types saturate ahead of the filter; circuits get up to 18 dB more input (and as much less output), so their transistors clip |  |
| Inductor Bias | 🆕 ✅ | DC through the winding lowers the inductance (to 40 % at full bias), raising the sweep by up to 1.6×: L1 on the inductor circuits (`WahCircuit.Trim`), the matching shift on the filter types and, through their capacitors, on the inductor-less Morley and Parapedal. The core's saturation curve is not modelled |  |
| Fat (dry blend up to 50/50) | ✅ | Mix |  |
| EQ page (8-band graphic) | 🆕 ✅ | Octave bands 63 Hz–8 kHz, ±12 dB; flat bands cost nothing |  |

---

## 7. Missing blocks

Checked against what exists under other names.

| Guide block | Exists as | Verdict | Effort |
|---|---|---|---|
| Looper | 🆕 `looper` grid block, and the rig looper at the end of the rig (one shared core) | **Implemented**: placeable, with bypass modes and the Config page (§6.14); the transport drives the block while its preset runs | — |
| Mixer | 🆕 `mixer` | **Implemented** in this pass | — |
| Plex Delay | 🆕 `plex` | **Implemented**: all five core types (§6.23) | — |
| Ten-Tap Delay | 🆕 `tentap` | **Implemented** with Rhythm Tap (§6.32) | — |
| Scene MIDI | 🆕 `Scene.Midi` | **Implemented** as per-scene messages (§6.29) | — |
| Tremolo/Panner | `tremolo` | **Complete** for its five types after this pass | — |
| Graphic EQ modes | `geq` | 🆕 Ten layouts (all 16 guide types with the Q mode); 🆕 the passive ones as component-level networks (§6.12) | — |
| RTA | 🆕 `rta` block + analyser | **Implemented**: a pass-through block taps the analyser anywhere (§6.24) | — |
| Input/Output features | Built-ins | 🆕 Noise gate on the Input, row mixer on the Output (§2); 🆕 six hardware input sources from a multi-channel interface | — |
| Send/Return | `fbsend` / `fbreturn` | 🆕 Two pairs | — |
| Tone Match | `tonematch` | Present | — |
| — (Cathode only) | `tonestack`, `vibe`, `fxloop` | Cathode extras: a standalone amp tone stack, a separate Uni-Vibe, and an FX loop with latency compensation | — |

---

## 8. Plan for the medium and large gaps

In order of value to a player, then cost. Each item meets the [Definition of done](03-testing-strategy.md#12-definition-of-done-any-block-or-feature) (conformance, analytic tests, golden, measured cost, editor).

1. 🆕 ✅ **Global Mix** (medium). A global "effects mix offset" (±50 %) in the global settings, sent to the engine as a rig parameter; a `globalMix` switch on Chorus, Delay, Flanger, Megatap, Multi-Delay, Phaser, Pitch, Resonator, Reverb, Synth; each block adds the offset where it reads Mix. One shared helper, one engine parameter, one API field, a Global-page control.
2. 🆕 ✅ **Input block noise gate and Output row mixer** (medium + cheap). Give the built-in nodes parameters: the Input node runs the Gate block's code (Classic; Intelligent adds a hum-aware filter at the mains frequency), the Output node gets per-row level and balance through the same row routing as the Mixer, plus a main balance. Needs the compiler to host parameters on built-ins and the editor to show them.
3. 🆕 ✅ **Scene MIDI** (medium). A preset-level `sceneMidi` table (8 × type/channel/number/value per scene) in the contract, sent through the existing MIDI output when a scene loads; an editor table on the Scenes page. Not a DSP block.
4. 🆕 **Done (P2).** **Delay completion** (medium). Dual type (independent L/R time, feedback and cross-feedback), the two-head mono tape with motor speed, the EQ page (two peaking bands, slopes), a second LFO with targets, a diffuser (reuse the reverb's), feedback polarity and master feedback to 200 %, 16 s lines in native memory.
5. 🆕 **Done (P2).** **Plex Delay block** (medium, then large). A new `plex` block: an 8-line feedback matrix (Hadamard or Householder) with per-line time/tempo/level/pan, decay time, input diffusion, pre-delay, stack/hold, filter with LFO, envelope and ducker (reuse `Ducker`). Second step: per-line granular detune and shift (reuse the Crystals splice shifter) for Plex Detune, Plex Shifter and Shimmer Verb.
6. 🆕 **Done (P2).** **Reverb depth** (medium). Separate early reflections with Early/Late level, the two peaking EQ bands, pre-delay tempo/feedback/mix, modulation controls, a quality setting (line count), and a shimmer pitch section (reuse the splice shifter).
7. 🆕 ✅ **Type presets** (medium, ongoing). Many guide "types" are parameter presets (17 choruses, 31 flangers, 16 phasers, 23 delays, 66 reverbs, 20 megataps). Offer them as factory block presets in the Block Library rather than as a Type parameter, so they never fight the knobs. Needs a curated preset set per block and the library's import path.
8. 🆕 **Done (P2).** **Multi-Delay algorithms** (medium each). Quad Parallel (per-line ring mod, band-pass and comb), Quad Series (send/return routing), Quad Diffusor, Quad Tape; per-tap feedback, filters and chorus; Rhythm Tap.
9. 🆕 **Done (P4; the component-level passive networks PA).** **Graphic EQ layouts** (medium). Type with 10 (2/3 oct), 8, 7 and 5 bands using `typeLabels` to rename and hide bands, shelving outer bands, then the passive types (Mark 5-band, 4-band, 3-band, console). Keep the global output EQ on the plain 10-band type.
10. 🆕 ✅ **Compressor and Gate sidechain input** (medium). A sidechain source (row or input) needs a second input into a block: the same `IRowInputBlock` routing can deliver a chosen row into a side buffer.
11. 🆕 ✅ **Look-ahead compressor** (medium). The compiler reads latency at compile time; a parameter-dependent latency needs a recompile on change or a fixed maximum look-ahead always reported.
12. 🆕 ✅ **"On engage" behaviour** (medium). LFO Bypass Reset (Flanger, Phaser) and Tremolo Start Phase need the engine to tell a block it was engaged; add an optional `IEngageAware` the compiled graph calls when a bypass fade starts.
13. 🆕 **Done** (stream P3): Phaser VCR models and Barberpole; Flanger thru-zero; Chorus Dimension.
14. 🆕 **Done** (stream P3): Pitch completion, including polyphonic Whammy tracking (a peak-locked phase vocoder). Detector Source on the hardware inputs 🆕 done (PA). Formant correction and per-voice formant shift 🆕 done (PF, §6.22).
15. 🆕 **Done (P4, PB)**, except per-circuit extra knobs beyond the Guv'nor's stack and new circuits. **Drive EQ pages and expert controls** (medium). The EQ 1 page (low/high cut, bass/mid/high-mid/treble) and EQ 2 (the GEQ bands pre/post) are generic; diode type/count and bias need per-circuit WDF parameters. New circuits continue as in Phase 6 (medium each).
16. 🆕 **Done (P2).** **Looper as a block** (medium), with its Config-page features (cheap): once, trims, threshold start, overdub level, crossfade, quantise, 300 s.
17. 🆕 **Done.** Ring Modulator SSB and pitch tracking, Synth third voice and tracking modes, Vocoder band controls (stream P3); IR Player second slot; Cab preamp, air and input mode (P4).
18. 🆕 **Done (P4, PB)**: boost, saturation, input filter, output EQ with location, output compressor, input dynamics, speaker drive, thump and compression, bias tremolo, Balance; 🆕 the advanced page (PB). Left: Tonestack Location End and Master Location Pre-Triode (no circuit meaning here). **Amp gaps** follow [06 §5](06-amp-modelling-spec.md#5-recommended-plan): input boost (reuse Drive circuits), saturation stage, output EQ types and location, output compressor and input dynamics, speaker drive and compression.

---

## 9. Presets, scenes and routing (Fractal's *Presets* page)

Not a block, so not in the Blocks Guide: the preset and scene behaviour around them. Already there before this pass: scene names and levels, four scene controllers, Scene MIDI, channel copy, six bypass modes with a 10 ms fade, Global blocks, the Block Library, setlists and banks, tags and author, gapless preset changes with spillover, preset export, MIDI scene select / next / previous and channel select, Sequencer modifiers.

| Feature | Cathode | Note |
|---|---|---|
| Scene Ignore (per channel) | 🆕 ✅ | `PresetBlock.sceneIgnore` lists the channels on which a block ignores scene changes: while its live channel is one of them, selecting a scene leaves its bypass and channel (`LiveState.held`, not stored in any scene). The editor, footswitches and MIDI still change it; a channel without it makes the block follow scenes again. Switch under the channel selector in the block editor |
| Scene Revert | 🆕 ✅ | Global `sceneRevert`: off keeps changes made while playing with their scene; on, selecting a scene recalls it from the last-saved copy of the preset |
| Default scene on load | 🆕 ✅ | Global `defaultScene`: −1 As Saved (`Preset.savedScene`, the scene active when saved) or a fixed scene 1–8; a setlist entry's scene still wins |
| Startup preset | 🆕 ✅ | Global `startupPreset`, loaded when the server starts |
| Ignore Redundant PC | 🆕 ✅ | Global `ignoreRedundantPc`: a program change for the preset already loaded does nothing (otherwise it reloads it, discarding edits) |
| Copy / swap scenes | 🆕 ✅ | `POST /live/scenes/copy`, `/live/scenes/swap` (name, block states, level, controllers, Scene MIDI). Swapping moves the active scene with its content, so nothing audible changes |
| Swap / reset channels, reset block | 🆕 ✅ | `POST /live/blocks/{id}/channels/swap`, `/channels/{ch}/reset`, `/live/blocks/{id}/reset`; Scene Ignore moves with the channel. Scenes keep naming the same letters |
| Scene Manager | 🆕 ✅ | The editor's Scenes page: blocks × scenes, bypass and channel editable in place (`PUT /live/scenes/{n}/blocks/{id}`), with copy and swap. All of these are undoable |
| Block level meters | 🆕 ✅ | Each block's output peak in the telemetry (layout v5, by graph slot, mapped to ids by the server), shown along the bottom of each grid tile |
| Output loudness | 🆕 ✅ | ITU-R BS.1770 (K-weighting, momentary 400 ms and short-term 3 s) on the output, after the global EQ; `loudnessMomentary`, `loudnessShortTerm` in the telemetry |
| Preset Leveling | 🆕 ✅ | Editor dialog: measures while you play (BS.1770 gating of the momentary readings: −70 LUFS absolute, −10 LU relative) and sets the amp's Level, or the Output's Main Level, to reach −23, −18 or −14 LUFS. Fractal levels by its own meter; Cathode uses LUFS |
| MIDI Vol Inc / Vol Dec, Level + Save | 🆕 ✅ | Actions `sceneLevelUp` / `sceneLevelDown` (active scene's level ±1 dB, then the preset is saved) and `save` |
| Scene CC above 63 | 🆕 ✅ | A CC mapped to `scene` without a fixed value: 0–7 select that scene, above 63 steps to the next, wrapping |
| Modifier scope | 🆕 ✅ | `PresetModifier.channel`: null for every channel of the block, or one channel; on the others the parameter keeps that channel's value. The engine switches the modifier as the channel changes (`setModifier`, no recompile). Editable in the modifier dialog and on the Controllers page |
| Preset-change spillover on/off | 🆕 ✅ | Global `presetSpillover` (the rig's `spillover`): off, the outgoing preset fades out over the 20 ms swap instead of ringing out |
| Looper across presets | 🆕 ✅ | A grid loop (recorded, playing or stopped) moves to the next preset's Looper block; without one it stops as before |
| Confirm before discarding edits | 🆕 ✅ | Global `confirmPresetChange` (on by default): the editor asks before loading another preset over unsaved edits |
| Preset list as text | 🆕 ✅ | `GET /presets/list.txt` (alphabetical, author, tags and scene names); "Export list…" on the Library page |
| Recent presets | 🆕 ✅ | A menu in the editor's navbar, per editor (local storage) |

## 10. Editor features (stream ED)

These come from Fractal's software-editor documentation rather than the Blocks Guide. They are paraphrased, and the full design is in [02 §7.5](02-architecture.md#75-editor-workflow-as-built). Most are 🆕 ✅.

- **Keyboard.**
  - Presets: P picker (a name, or a slot number of a bank), ⌘PgUp/PgDn, F5 reload, ⌘S and ⇧⌘S save.
  - Scenes: ⌘1–8.
  - Channels: A, ⇧B, C and D (B stays the bypass alias), and X for the next channel.
  - Blocks:
    - Space (or B) bypasses or engages.
    - ⇧Space engages and ⇧⌘Space bypasses, in every scene.
    - ⌘X cut, ⌘D disconnect, ⌘U shunt, ⌘B save to the Library, G global, Q add or replace.
    - ⌥ held or F1 shows Block Info.
  - Other: ⌘T tuner, ⌘Y redo.
  - `?` lists every shortcut.
  - ⌘L opens Preset Leveling and ⌘I resets the channel (the PS stream's dialog and endpoint). Every preset change from the keyboard or the pickers asks before discarding unsaved edits.
- **Preferences:** knob mode (linear or circular), wheel (normal, reverse or off), tooltips, meter refresh rate, and the default Circuit Amp view.
- **Preset commands:** revert to saved, clear (with a confirmation), one-click timestamped snapshots kept on the Server, and templates (save as, new from).
- **Grid:**
  - replace a block with another type, keeping its cables;
  - drag onto a block to swap the two, ⌘-drag to replace it;
  - move a row or column;
  - cut;
  - paste onto a block of the same type, which copies its settings into all channels;
  - disconnect all cables.
- **All scenes:** set a block's channel, or engage or bypass it, in every scene.
- **Block Info:** tiles show an abbreviation, the channel letter and a Global mark, and a line under the grid lists the amps in use (model per channel, and the scenes that use each).
- **Circuit Amp Ideal/Authentic.** Authentic shows only the model's own controls. Ideal shows every control, under the generic name where the model has none. The default is a preference, and the amp page can switch it.
- **Library:**
  - save one channel, or the whole block with its modifiers and assets;
  - recall into the current channel or all channels;
  - folders;
  - `.cthblock` export and import, including dropping a file on the Library page or a grid cell;
  - save every block of the preset.
- **Manage Presets and Manage IRs:**
  - multi-select (⇧ for a range, ⌘ to toggle);
  - drag to swap, ⌘-drag to move, ⇧-drag to copy, within a bank or onto another bank;
  - changes held and marked until Save, and Close discards them;
  - duplicate, rename, delete, colour and favourite;
  - **Reset Amp blocks**, which resets every Circuit Amp parameter except the model's front-panel controls.
  - ◐ Manage IRs has no drag or duplicate: IRs are content-addressed and not slotted, so neither has a meaning.
- **Parameter Batch Set:** a control's context menu sets its value in other presets, on the same block id and type, for one channel or all.
- **Pickers:** colour tags and favourites on presets and IRs, stored in the preset and the asset index. They have search with keyboard navigation and a pin that keeps the picker open while auditioning.
- **Perform pages:** a global page (in Global) and a per-preset page (in the preset), each a grid of large controls. A control's context menu adds to either.
- **Footswitches:**
  - layout links (a switch also jumps to another layout);
  - views (pages of switches, with view, viewNext and viewPrevious actions);
  - per-preset overrides of a switch's action.
  
  All of them are edited in the Global footswitch editor.
