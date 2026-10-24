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
| Input (IN) | built-in `input` | 1 → 1 | 0 / 1 / 6 | Signal only; the noise gate and impedance are missing |
| Output (OUT) | built-in `output` | 1 → 1 | 1 / 1 / 2 | Scene levels exist (preset level); no row mixer |
| Common Mix/Level | every block | — | 4 / 3 / 3 | Level and 🆕 Balance everywhere; Global Mix missing |
| Amp (AMP) | `circuitamp`, `nam` | 309+ → 26 circuit + any capture | see [06 §1.4](06-amp-modelling-spec.md#14-parity-summary) | Core panel, expert pages and 🆕 boost, saturation, input filter/Definition, output EQ with location, input dynamics, output compressor, speaker drive/thump/compression, bias tremolo, Balance; power-tube hardness and per-model switches remain |
| Cab (CAB) | `cab` (Legacy and DynaCab merged) | 2 150+ IRs → user IRs + synthesised DynaCab + IR packs | 🆕 15 / 3 / 1 | Done except stereo room mic spacing and a visual align view |
| Chorus (CHO) | `chorus` | 17 → 1 | 🆕 11 / 2 / 7 | Core and LFO done; type presets, drive, dimension missing |
| Compressor (CMP) | `compressor` | 16 → 🆕 6 | 🆕 17 / 1 / 2 | 🆕 Detector, ∞ ratio, Dynamics and Feedback types, auto times, light type, emphasis, sidechain EQ, input level; look-ahead and sidechain source are P1's |
| Crossover (XVR) | `crossover` | 1 → 1 | 🆕 7 / 0 / 0 | Done (🆕 per-side band levels and pans) |
| Delay (DLY) | `delay` | 23+ → 🆕 5 | 🆕 25 / 6 / 17 | Core, ducker, hold, bit reduction, ping-pong done; dual/tape heads, EQ page, LFO 2 missing |
| Drive (DRV) | `drive` | 60+ → 27 | 🆕 17 / 3 / 0 | Strong circuits; 🆕 EQ pages and the Advanced page; per-pedal extra knobs remain |
| Dynamic Distortion (DND) | `dyndist` | 7 → 3 (🆕 7 curves) | 🆕 9 / 1 / 0 | Different design (envelope-driven); 🆕 the guide's frequency-shaped filter, bias, mono |
| Enhancer (ENH) | `enhancer` | 3 → 🆕 4 | 🆕 10 / 0 / 0 | Done (🆕 Stereoizer, Depth) |
| Filter (FLT) | `filter` | 15 → 🆕 12 | 🆕 17 / 2 / 2 | Done except passive shelf/peak 2 and LFO duty/quantise |
| Flanger (FLG) | `flanger` | 31 → 1 | 🆕 12 / 3 / 9 | Core done; thru-zero, drive, VCO response missing |
| Formant (FOR) | `formant` | 1 → 1 | 8 / 0 / 0 | Done (Cathode adds Shift) |
| Gate/Expander (GTE) | `gate` | 4 → 🆕 4 | 🆕 14 / 0 / 1 | Done except the row sidechain (P1) |
| Graphic EQ (GEQ) | `geq` | 16 → 🆕 10 layouts × constant/variable Q | 🆕 6 / 1 / 0 | 🆕 Band layouts, outer shelves; passive types approximated |
| IR Player (IRP) | `irplayer` | 1 → 1 | 🆕 11 / 0 / 0 | Done (🆕 two slots, modes, length, per-IR level and pan, slopes, input select) |
| Looper (LPR) | rig looper (not a block) | 1 → 1 | 9 / 3 / 6 | Works, but as a rig utility at the end of the chain |
| Megatap (MGT) | `megatap` | 20 → 1 | 🆕 10 / 4 / 9 | Shapes done; alphas, randomise, feedback, diffusion, envelope missing |
| Mixer (MIX) | 🆕 `mixer` | 1 → 1 | 🆕 6 / 0 / 0 | Done in this pass |
| Multitap Delay (MTD) | `multidelay` | 34+ → 6 | 🆕 14 / 8 / 20 | Quad Tap, Ten Tap, Plex, Ducking, Reverse, Sweep; quad-series/parallel, per-tap filters, chorus, comb, ring mod missing |
| Multiband Comp (MBC) | `mbcomp` | 1 → 1 | 🆕 15 / 0 / 0 | Done (🆕 detector, mutes, 12/24 dB slope, auto times) |
| Multiplexer (MUX) | `mux` | 1 → 1 | 🆕 3 / 1 / 0 | Done (rows only, no input jacks) |
| Parametric EQ (PEQ) | `peq` | 1 → 1 | 🆕 8 / 0 / 0 | Done (🆕 Shelving 2, Peaking 2, slopes to 36 dB/oct, Solo) |
| Phaser (PHA) | `phaser` (+ `vibe`) | 16 → 1 (+ Vibe) | 🆕 11 / 2 / 11 | Core done; VCR modelling, astable, LFO hicut missing |
| Pitch (PIT) | `pitch` | 16 → 9 | 🆕 24 / 7 / 15 | Most types; per-voice feedback, LFO, temperament, master scalers missing |
| Plex Delay (PLX) | `multidelay` type Plex | 44 → 1 | 4 / 3 / 22 | **Medium/large gap**: a 4-line plex without shifters |
| Resonator (RES) | `resonator` | 1 → 1 | 🆕 12 / 3 / 5 | Done except per-voice feedback, BPF, stereo input |
| Return (RTN) | `fbreturn` | 1 → 1 | 🆕 3 / 0 / 0 | Done |
| Reverb (REV) | `reverb` | 66+ → 5 | 🆕 14 / 3 / 19 | Core and 🆕 ducker/hold/spread; EQ page, early/late, pitch section missing |
| Ring Modulator (RNG) | `ringmod` | 3 → 1 | 🆕 5 / 0 / 2 | Classic only; SSB types and pitch tracking missing |
| Rotary (ROT) | `rotary` | 1 → 1 | 12 / 3 / 3 | Done except tempo, input select, mic distance |
| RTA | analyser (`PUT /rta`) | 1 → 1 | 1 / 1 / 2 | Output-only analyser, not a grid block |
| Scene MIDI (MIDI) | — | 1 → 0 | 0 / 0 / 4 | **Missing**: medium |
| Send (SND) | `fbsend` | 1 → 1 | 1 / 0 / 0 | Done (Cathode adds a send level) |
| Synth (SYN) | `synth` | 1 → 1 | 9 / 5 / 6 | 2 voices, not 3; tracking modes, per-voice filter missing |
| Ten-Tap Delay (TTD) | `multidelay` type Ten Tap | 2 → 1 | 7 / 3 / 7 | Ten Tap works; Rhythm Tap and per-tap levels missing |
| Tone Match (TMA) | `tonematch` | 1 → 1 | 2 / 0 / 0 | Done (the guide defers to a separate manual) |
| Tremolo/Panner (TRM) | `tremolo` | 5 → 🆕 5 | 🆕 10 / 1 / 2 | Done in this pass except start phase and trigger |
| Vocoder (VOC) | `vocoder` | 1 → 1 | 7 / 4 / 7 | 20 fixed bands; analysis/band controls missing |
| Volume/Pan (VOL) | `volpan` | 1 → 1 | 🆕 10 / 0 / 0 | Done in this pass |
| Wah (WAH) | `wah` | 9 → 5 | 🆕 7 / 3 / 4 | Pedal, mix, 🆕 Q, Q tracking, taper, low cut; min/max frequency, drive, EQ missing |

**Totals** (the counts above, summed over the 40 audited blocks and pages, Amp excluded): about **370 / 85 / 245** (✅ / ◐ / ❌), i.e. **roughly 64 % of the guide's parameters present at least in part**, up from about 42 % before this pass. Type coverage remains the largest gap: Fractal's catalogue of named type presets (Chorus 17, Flanger 31, Phaser 16, Delay 23+, Reverb 66+, Plex 44) maps to one or a few algorithms with knobs here.

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

**P4 stream (dynamics, EQ, drive, cab and the amp block).** Every new control defaults to off or stock, so presets sound as before (the golden renders pass, and every Circuit Amp model renders bit-identical at its defaults).

- **Compressor:** 🆕 Detector choice (type default, peak, RMS, RMS+peak), Ratio to 100:1 = ∞ (a limiter), the 🆕 Dynamics type (a dynamics processor: compress or expand above the threshold) and Feedback type (the detector hears the output), Auto Attack/Release (crest-factor scaling, `CrestFactor`), Light Type for the opto, an Emphasis high shelf and one sidechain EQ band on the detector, and Input Level (Line takes 12 dB off the detector).
- **Gate:** 🆕 Modern Expander and Modern Gate (6 dB of hysteresis, no chatter) and a soft Knee for the expanders. **Multiband Compressor:** 🆕 Peak/RMS detector, per-band mute, a 12 dB/oct (LR2) crossover option, Auto Attack/Release.
- **Graphic EQ:** 🆕 a shared `GraphicEqualizer` (also used by the Drive's EQ 2 and the amp's output EQ) with ten layouts (10 octave, 10 at 2/3 octave, 8, 7, 5 bands, and approximated Mark, 5-, 4-, 3-band and console passives), the bands renamed and hidden per type through `typeLabels`, and Outer Shelves. The global output EQ stays on the plain ten-band type.
- **Parametric EQ:** 🆕 Shelving 2 (first order) and Peaking 2 (proportional Q) on bands 1 and 5, blocking slopes from 6 to 36 dB/oct (`CutFilter` gained 30 and 36), and an engine-side Solo; the editor's curve draws every type. **Crossover:** 🆕 four band levels and four band pans (the left and right input's low and high bands).
- **Enhancer:** 🆕 the Stereoizer type (24 third-octave allpasses, complementary and mono compatible) and Depth (the allpasses' sharpness). **IR Player:** 🆕 a second IR slot, Mode (parallel, stereo, series), per-IR Length, Level and Pan, cut slopes, Input Select; the slots are baked on the control thread (`IControlRefresh`) and crossfaded, as the Cab does.
- **Drive:** 🆕 the EQ 1 page (low/high cut, bass, mid with frequency, high mid, treble), EQ 2 (a ten-band 2/3-octave graphic, pre or post), and the Advanced page: Clip Type and Clip Shape for the voicings, diode type and count overrides for the circuits (variants prebuilt, allocation-free), Bass Response, Dry Level, Bias, Slew Rate, Bit Reduce and Sample Rate. **Dynamic Distortion:** 🆕 the boost filter (shelf or peak, with its inverse after the clipper), Bias, Mono/Stereo, and four more clip curves.
- **Cab:** 🆕 Smoothing (fractional-octave, minimum-phase correction that keeps the onset), a continuous Proximity with Proximity Freq, the room page (size, shape, floor reflection, HF and LF damping, diffusion) and the mic preamp's Bass, Mid and Treble.
- **Circuit Amp:** 🆕 Input Boost (six Drive circuits at the amp's oversampled rate), Saturation (a diode-style clipper between preamp and power amp), Input Filter and Definition, an Output EQ on the Graphic EQ's layouts at the input, before the power amp or at the output, Input Dynamics, an Output Compressor (Output/Feedback types, clarity), Speaker Drive, Thump with Cab Resonance, Speaker Compression with its time constant, Bias Tremolo and Balance (36 parameters, `Amp/AmpInserts.cs`). The knobs are read on a fixed 64-frame grid, so the output no longer depends on the host's block size.

### 1.3 CPU costs to regenerate

`BlockCosts.cs` was not regenerated. Blocks whose processing changed (a new per-sample stage or a changed default path) should be re-measured: **chorus, compressor, crossover, delay, enhancer, fbreturn, filter, flanger, gate, geq, megatap, multidelay, phaser, reverb, resonator, ringmod, tremolo, volpan, wah** and the new **mixer** (placeholder entry 0.04 %). Every other effect block only gained the Balance gain stage (an extra multiply per sample when balance is off-centre): **drive, dyndist, formant, fxloop, irplayer, mbcomp, mux, peq, pitch, rotary, synth, tonematch, tonestack, vibe, vocoder** may move by a few hundredths of a percent.

---

## 2. Input and Output blocks (pp. 4–6)

Cathode's `input` and `output` are built-in node types handled by the graph compiler; they have no parameters. Input selection and output routing are host settings (Setup page).

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Input 1–4 / USB source | ◐ | One stereo input pair from the interface; more channels wait for multi-channel I/O in the audio shim | medium (shim) |
| Noise gate: Type (Classic, Intelligent, Noise Reducer) | ❌ | Use a Gate block after the input. A built-in gate belongs on the Input node as parameters | medium |
| Gate Threshold, Ratio, Attack, Release, Output Level | ❌ | As above; the Gate block has all of them | medium (with the above) |
| Global noisegate offset | ❌ | A global setting | cheap once the gate exists |
| Input bypass behaves as a shunt | — | No bypass on built-ins |  |
| Instrument impedance (1 MΩ … 22 kΩ ± cap, Auto) | — | Analogue circuitry; see [01 §1](01-axe-fx-iii-audit.md#1-hardware-and-dsp-what-the-computer-and-interface-must-replace). A pickup-load EQ is possible | medium (optional) |
| Output: Main level and balance | ◐ | Preset scene level only; no balance | cheap |
| Output: per-row Level and Balance mixer | ❌ | Rows summed at unity. The new Mixer block covers the use; folding it into the Output node is cheap once the compiler passes rows (as for the Mixer) | cheap |
| Output: 8 scene levels | ✅ | `Scene.Level` |  |
| Output bypass = mute | — | Built-ins cannot be bypassed |  |
| In/Out as an FX send/return loop | ✅ | `fxloop` block, with loop latency compensation (better than the guide's two-block arrangement) |  |
| Four channels on In/Out | ❌ | Built-ins have no channels | medium (with the gate) |

## 3. Common mix and level parameters (pp. 7–8)

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Mix (wet/dry, 50 % = −6 dB each) | ✅ | Every wet/dry block uses the linear crossfade with that law |  |
| Delay mix law (wet full at 50 %, dry fades after) | 🆕 ✅ | Delay block only, as in the guide |  |
| Level | ✅ | Every effect block (`level`, dB) |  |
| Balance | 🆕 ✅ | Every effect block (`balance`, −100…100 %); the far side is turned down, wet and dry alike |  |
| Bypass (as a modifier target) | ◐ | Bypass is per scene and through the bypass command; a modifier cannot drive it | medium |
| Bypass modes Thru, Mute, Mute In, Mute FX In | ✅ | Since Phase 5 |  |
| Bypass modes Mute Out, Mute FX Out | 🆕 ✅ | The block keeps running ("listening") while bypassed |  |
| Input Select (L, R, L+R) | 🆕 ◐ | Reverb, Megatap, Multi-Delay, Volume/Pan; missing on Rotary (sums), Pitch | cheap per block |
| Input Gain | 🆕 ◐ | Delay, Reverb, Megatap, Multi-Delay, Resonator; missing on Pitch and Plex | cheap (Pitch) |
| Global Mix (follow the global Effects Mix offset ±50 %) | ❌ | Needs a global setting and a per-block switch; the offset applied where each block reads Mix | medium |
| Stereo Spread (−200…+200 %) | ◐ | Delay and Reverb (−100…+100 %); Chorus, Flanger, Phaser have an LFO phase instead. The psychoacoustic range beyond ±100 % is missing | cheap (±100), medium (beyond) |

## 4. Amp block (pp. 9–18)

Audited in detail in [06 §1](06-amp-modelling-spec.md#1-axe-fx-iii-amp-block-parameter-parity). The P4 stream added the non-model parameters in `CircuitAmpBlock` and `Amp/AmpInserts.cs`, all off or neutral by default; what is left needs changes inside the models or the power amp (several are emergent in a circuit model). Current state of `circuitamp` (🆕 88 parameters; catalogue in [07](07-amp-catalogue.md)):

| Page | Present | Missing | Effort for the gaps |
|---|---|---|---|
| Type, Tone | Type, Gain, Bass, Middle, Treble, Master, Presence, Depth, Cut, Bright, model switches and second controls, Input Trim | Presence Shift as a generic switch, Overdrive on all models | per model (emergent in the circuits) |
| Preamp | Preamp tube type, Bright Cap, Preamp Bias, Preamp Sag, 🆕 Input Boost (type from the Drive circuits, drive, tone, level), 🆕 Saturation switch and drive (diode-style clipper between preamp and power amp) | Fat and Cut as generic switches, High Treble | per model (switch roles in the circuits) |
| Input EQ | Input select, Low Cut, 🆕 Input Filter (off, shelves, peak, high/low-pass; freq, Q, gain), 🆕 Definition (treble/bass tilt into the preamp) | — | |
| Output EQ | 5 model EQ bands (`eq1`–`eq5`), 🆕 Output EQ type (every Graphic EQ layout) with 10 bands, 🆕 EQ location (input, pre power amp, output) | Passive types are approximations (see §6.12) | — |
| Power supply | Sag, B+ time constant, supply type (DC, AC 60 Hz, AC 50 Hz = AC line frequency), rectifier, Variac | Screen frequency/Q | model data |
| Power amp | Power amp on/off, power tube type, bias, negative feedback, transformer matching, LF/HF, drive, speaker impedance, 🆕 bias tremolo (rate, depth on the Bias control) | Power tube hardness: needs the pentode law in PowerAmp/Tubes, outside this block | medium (power amp) |
| Speaker | Speaker curve, LF resonance freq/Q/peak, HF, impedance, 🆕 speaker drive (cone excursion), 🆕 thump with cab resonance, 🆕 speaker compression and its time constant | Compliance (covered by the impedance curve's LF Q and amount) | — |
| Dynamics | 🆕 Input dynamics (expand/compress by envelope), 🆕 output compressor (Output/Feedback type, amount, threshold, clarity) | Master bias excursion (emergent in the power amp) | — |
| Advanced | Tonestack frequency, master volume trim, presence/depth frequency, cathode time constant | Tonestack type substitution and location, triode plate frequencies, preamp hardness, cathode follower controls, PI/power tube bias excursion, plate suppressor diodes, master volume location/cap, output mode (FRFR / SS PA + cab), auto DynaCab impedance | medium–large; several are emergent in a circuit model ([06 §1.4](06-amp-modelling-spec.md#14-parity-summary)) |
| Output | Level, 🆕 Balance, bypass mode handled by the engine | — | |

The capture tier (`nam`) has input, output, normalise and a four-band post EQ; the guide has no counterpart (Fractal's NAM block arrived with firmware 33).

## 5. Cab block (pp. 19–24)

Re-audited for the merged `cab` block (the old `cab` and `dynacab` are one block with four slots, each playing an IR file, a synthesised DynaCab or an IR pack).

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Cab Mode (Legacy / DynaCab) | ✅ | Per slot: Source IR, DynaCab or IR Pack |  |
| Legacy: Bank and Cab Number, cab picker | ◐ | Any IR from the asset store; no factory banks (licensing) | — |
| Legacy: 4 IRs per channel | ✅ | Four slots, any mix of sources |  |
| Mic Distance (align, sub-ms delay) | ✅ | Per-slot Delay (0–5 ms, fractional) and Auto Delay (lines the modelled mics up by their travel time) |  |
| Proximity, Proximity Freq | 🆕 ✅ | Proximity 0–200 % of the physical boost (0 off), Proximity Freq 25–400 % of its corner |  |
| Smoothing | 🆕 ✅ | 0–1 octave magnitude smoothing of the mixed IR at build time; a minimum-phase correction keeps the onset and the slots' alignment |  |
| DynaCab: cab type, mic type, position, distance | ✅ | Synthesised models (10 speakers, 11 mics, 7 cabinets), plus mic angle |  |
| Level per IR (relative, normalised mix), Mute, Solo, Pan | ✅ | Every slot, plus Invert |  |
| Per-IR low/high cut and slopes | ✅ | 6–24 dB/oct, baked into the mixed IR |  |
| IR Length | ✅ | Per slot, 5–170 ms with a short fade |  |
| Preamp page: Drive, Saturation, Type (Tube, FET, Tape…) | ✅ | None, Tube, Bipolar, FET, Transformer, Tape |  |
| Preamp page: Bass/Mid/Treble | 🆕 ✅ | 120 Hz shelf, 800 Hz peak, 4 kHz shelf, ±12 dB; skipped at 0 dB |  |
| Preamp page: Mode | ◐ | The type sets both the curve and the treble roll-off; no separate mode | cheap |
| Master low/high cut and slopes | ✅ | 6–24 dB/oct |  |
| Room page: level, size, shape, LF/HF damping, floor reflections, diffusion | 🆕 ✅ | Room, Room Size, Room Shape, Floor Reflection, Room HF/LF Damping, Room Diffusion, in the DynaCab synthesis (an IR file or pack carries its own room) |  |
| Room page: mic spacing | ❌ | The room is in each slot's mono IR; stereo room mics need separate left and right room IRs per slot | medium |
| Align page | ◐ | Per-slot delay and Auto Delay; no visual alignment view | medium (editor) |
| Mix page: Input Mode (L, R, Sum, Stereo), Air, Air Frequency | ✅ |  |  |
| FullRes long IRs (1.37 s) | ✅ | IR Player up to 680 ms; `cab` up to 170 ms per slot | — |

## 6. Effect blocks

Each table lists the guide's parameters page by page. Unless noted, every block also has the common Mix/Level/Balance and bypass parameters covered in §3.

### 6.1 Chorus (pp. 25–26) → `chorus`

Types: guide 17 (digital mono/stereo, analog mono/stereo, Japan CE-2, warm stereo, 80's style, triangle, 8-voice, vintage tape, dimension 1–3, 4/8-voice analog, tri-chorus, dual) → Cathode 1 algorithm. The type list is mostly presets of the parameters below; the missing *algorithms* are BBD colour (analog), tape chorus and the Dimension matrix.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Number of voices (2–8) | 🆕 ◐ | 1–4 per side | cheap (8) |
| Basic | Rate, Depth, Delay Time, Mix | ✅ |  |  |
| Basic | Tempo | 🆕 ✅ |  |  |
| Basic | Dual: separate L/R rate and depth | ❌ |  | cheap |
| Expert | Low Cut, High Cut | 🆕 ✅ | Wet only |  |
| Expert | LFO Phase | ✅ | `spread` (0–180°) |  |
| Expert | LFO Type | 🆕 ✅ | All ten shapes |  |
| Expert | Auto Depth | ❌ | Scale depth by 1/rate | cheap |
| Expert | Phase Reverse | 🆕 ✅ |  |  |
| Expert | Drive (BBD) | ❌ |  | cheap |
| Expert | Width (right time scaled toward 1 ms) | ❌ |  | cheap |
| Expert | LFO2 Rate, LFO2 Depth | ❌ | A second LFO modulating the first | cheap |
| Expert | Stereo Spread | ◐ | LFO phase only | cheap |
| Expert | Dimension Mode (off, low, med, high) | ❌ | The Dimension's cross-mixed matrix | medium |
| Expert | Right Time Ratio (Dual) | ❌ |  | cheap |
| Mix | Global Mix | ❌ | §3 | medium |

### 6.2 Compressor (pp. 27–29) → `compressor`

Types: guide 16 (Studio FF/FB compressor and sustainer, analog compressor/sustainer, compander, DynaComp, dynamics processor, JFET compressor/sustainer, optical compressor/sustainer, pedal 1/2, tube) → Cathode 🆕 6 (Pedal, Studio, Opto, Sustainer, 🆕 Dynamics, 🆕 Feedback). JFET, tube and the compander are still missing algorithms; most other guide types are presets of these (detector, ratio, times, light type).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Threshold, Ratio, Attack, Release, Knee, Mix | ✅ |  |  |
| Config | Compression (single-knob types) | ◐ | Ratio covers it | — |
| Config | Dynamics (processor: compress ↔ expand) | 🆕 ✅ | Dynamics type: the slope over the threshold is 1 − Dynamics (−100 % expands 2:1 upwards, +100 % limits), at most +24 dB |  |
| Config | Ratio ∞ (limiter) | 🆕 ✅ | 1–100:1; 100 is ∞:1 |  |
| Config | Auto Attack/Release | 🆕 ✅ | Both times scaled by 2 / crest² over 200 ms (Giannoulis et al. 2012), ×0.25–4 |  |
| Config | Look Ahead | ❌ | Needs latency reporting that depends on a parameter; the graph compiler only reads latency at compile time (P1 stream) | medium |
| Config | Detector Type (RMS, Peak, RMS+Peak) | 🆕 ✅ | Type default, Peak, RMS, RMS+Peak (the mean of the two in dB) |  |
| Config | Light Type (optical) | 🆕 ✅ | Classic, Fast LED, Slow Vactrol: the opto's attack and its program-dependent release law |  |
| Config | Auto Makeup | ✅ | Half the reduction at 0 dBFS |  |
| Config | Makeup | ✅ | Cathode extra |  |
| Sidechain | Source (block, row, input) | ❌ | Needs a second input into the block (P1 stream) | medium |
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

Types: guide table lists 23 (digital/analog/tape mono and stereo, ping-pong, dual, reverse, sweep, ducking, vintage digital, 2290 with modulation, ambient stereo, Deluxe Mind Guy, mono/stereo BBD, lo-fi tape, Stereo Mind Guy, diffused, Graphite Copy, worn tape, Zephyr) → Cathode 5 (Digital, Analog, Tape, 🆕 Ping-Pong, 🆕 Vintage Digital). Reverse, Sweep and Ducking exist as Multi-Delay types. Most others are presets over the parameters below.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Time (up to 16 s), Tempo | ◐ | Up to 4 s; the extended tempo list | cheap (longer line in native memory) |
| Config | Master Feedback (0–200 %) | ❌ |  | cheap |
| Config | Feedback (±) | ◐ | 0–100 %, no polarity | cheap |
| Config | Echo Pan | ❌ |  | cheap |
| Config | Repeat Hold / Stack | 🆕 ◐ | Hold only (no Stack) | cheap |
| Config | Drive | 🆕 ✅ |  |  |
| Config | Bit Reduction | 🆕 ✅ |  |  |
| Config | Right Post Delay | ❌ |  | cheap |
| Config | Spread | 🆕 ✅ |  |  |
| Stereo | L/R Time Ratio | 🆕 ✅ |  |  |
| Stereo | Feedback L, Feedback R | ❌ | One feedback for both | cheap |
| Dual | Independent L/R time, tempo, level, pan, feedback; cross feedback L→R, R→L | ❌ |  | medium |
| Sweep | Start/stop frequency, resonance, sweep type, rate, tempo, phase | ◐ | In Multi-Delay's Sweep type | cheap (move into Delay) |
| Mono Tape | Motor speed, Head 1 time/tempo, Head 2 ratio, per-head level, feedback, pan | ◐ | Tape type has wow/flutter, one head | medium |
| Reverse | Time, Run, Trigger Restart, Crossfade | ◐ | Multi-Delay Reverse: no run/restart | cheap |
| EQ | Low/High Cut | ✅ | In the feedback path |  |
| EQ | Cut slopes, Q | ❌ |  | cheap |
| EQ | Two peaking EQs (freq, Q, gain) | ❌ |  | cheap |
| Modulation | LFO depth range, phase reverse | ◐ | 🆕 phase reverse; no depth range | cheap |
| Modulation | LFO 1/2 type, target, rate, tempo, depth, phase; LFO 3 (level/pan/spread) | ◐ | One sine LFO (rate, depth) | cheap–medium |
| Modulation | Ducker attenuation, threshold, release | 🆕 ✅ |  |  |
| Modulation | Diffuser mix and time, its LFO | ❌ | Reuse the reverb diffusers | cheap |
| Mix | Input Gain | 🆕 ✅ |  |  |
| Mix | Global Mix | ❌ | §3 | medium |
| — | Spillover | ✅ | Mute In / Mute FX In, and across presets |  |

### 6.5 Drive (pp. 37–39) → `drive`

Types: guide about 60 named pedals → Cathode 27 (3 voicings + 24 WDF circuits; see [04 Phase 6 item 7](04-implementation-plan.md)). Missing notable ones: Bit Crusher, Blackglass 7K, Box O' Crunch, Esoteric ACB/Bass RCB, Eternal Love, FAS LED-Drive/Boost, Fat Rat (have RAT), Full OD, Hoodoo, Horizon Precision, Jam Ray, M-Zone (Metal Zone), Master Fuzz, MCMLXXXI, Micro Boost, OD-One, OD 250, Shred Dist, Sonic Drive, Super OD (have SD-1), Suhr Riot, SDD preamp, Tape Distortion, Tube Driver, Tone of Kings, Treble Boost (have Rangemaster). Each new circuit is **medium** (netlist, tone network, tests).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Drive, Tone, Level, Mix | ✅ | Per-pedal panel names are generic |  |
| Basic | Per-type extra knobs (e.g. Bass/Treble, Presence) | ◐ | One tone control per circuit; the generic EQ 1 page stands in | medium per circuit |
| EQ 1 | Low Cut, High Cut | 🆕 ✅ | 12 dB/oct, off at their ends | |
| EQ 1 | Bass, Mid (with frequency), High Mid, Treble ±12 dB | 🆕 ✅ | Low shelf 100 Hz, peak at Mid Freq (200 Hz–5 kHz), peak 2.5 kHz, high shelf 5 kHz | |
| EQ 2 | 10-band 2/3-octave graphic EQ, Pre/Post/Off | 🆕 ✅ | The shared `GraphicEqualizer` (2/3-octave layout) | |
| Advanced | Clip Type (4558/diode, FET, rectifier, Ge, hard, HV/LV tube, LED, op-amp, Si, soft, variable, null) | 🆕 ◐ | Voicings: Soft, Hard, Asymmetric, Germanium, LED, Tube, Op-Amp, None; circuits: implied by the circuit (see diodes). FET, rectifier and variable missing | cheap (more curves) |
| Advanced | Clip Shape | 🆕 ✅ | Voicings: bends the curve towards a soft knee or a hard clip | |
| Advanced | Diode ± type and quantity | 🆕 ✅ | Circuits with clipping diodes: Si, Ge, Schottky, LED, MOSFET, 1–4 per side; variants prebuilt | |
| Advanced | Bass Response | 🆕 ✅ | Scales the input coupling corner (voicing pre-filter, circuit coupling cap) | |
| Advanced | Dry Level (dry inside the circuit) | 🆕 ✅ | Summed after the clipping, before the tone | |
| Advanced | Bias | 🆕 ✅ | Offset ahead of the clipping, DC-blocked after | |
| Advanced | Slew Rate | 🆕 ◐ | A generic slew limiter after the clipping on every type; the RAT's op-amp keeps its modelled slew | medium (per op-amp model) |
| Advanced | Bit Reduce, Sample Rate | 🆕 ✅ | Word length and sample-and-hold rate on the driven signal | |
| — | Input Vol (guitar volume knob) | ✅ | Cathode extra |  |

### 6.6 Dynamic Distortion (p. 40) → `dyndist`

Cathode's block distorts by envelope (drive follows the level) rather than through the guide's frequency-shaped filter. Types 7 presets → 3 clip shapes.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Preset page | ◐ | Type selects the clip shape (🆕 7 curves) | — |
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

Types: guide 15 → Cathode 🆕 12: Low Pass, High Pass, Band Pass, Notch, Peaking, Low Shelf, High Shelf, Null, All Pass, Tilt EQ, FF Comb, FB Comb. Missing: Low Shelf 2, High Shelf 2 (passive "overshoot" shelves) and Peaking 2 (variable Q): **cheap** each.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type, Frequency, Q, Gain | ✅ |  |  |
| Order (2nd/4th) | 🆕 ✅ |  |  |
| Feedback (all-pass) | ❌ | All-pass has no feedback around it | cheap |
| Low Cut, High Cut | 🆕 ◐ | Second-order, the guide's are first-order | cheap |
| Pan L, Pan R | 🆕 ✅ |  |  |
| Phase Invert (none, L, R, both) | 🆕 ✅ |  |  |
| Comb Delay Time, Depth | 🆕 ✅ |  |  |
| LFO enable, type, rate, Mod Frequency | 🆕 ✅ | Plus tempo |  |
| LFO duty, quantise | ❌ |  | cheap |
| Level, Balance | 🆕 ✅ |  |  |

### 6.9 Flanger (pp. 43–45) → `flanger`

Types: guide 31 named presets → Cathode 1 algorithm. Missing algorithms: thru-zero and analog VCO responses.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Rate, Depth, Feedback (±), Mix | ✅ |  |  |
| Basic | Tempo | 🆕 ✅ |  |  |
| Basic | Manual (0.01–10 ms) | ✅ | `delay` (0.1–10 ms) |  |
| Basic | Low Cut, High Cut | 🆕 ◐ | 12 dB/oct, the guide's 6 | cheap |
| Basic | Bass Focus | ❌ |  | cheap |
| Basic | Drive | ❌ |  | cheap |
| Expert | Minimum / Maximum Time | ◐ | Manual + depth define the range | cheap |
| Expert | Thru-Zero (off, auto, manual), Dry Delay | ❌ |  | medium |
| Expert | Phase Reverse | 🆕 ✅ |  |  |
| Expert | LFO Type | 🆕 ✅ |  |  |
| Expert | LFO Phase | ✅ | `spread` |  |
| Expert | LFO Bypass Reset | ❌ | Needs the engine to tell a block it was engaged | medium |
| Expert | LFO Hicut, LFO Quantize | ❌ |  | cheap |
| Expert | Auto Depth | ❌ |  | cheap |
| Expert | VCO Response, Exponent | ❌ |  | cheap |
| Mix | Stereo Spread | ◐ | LFO phase | cheap |
| Mix | Global Mix | ❌ | §3 | medium |

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
| Sidechain Select (block, row, input) | ❌ | Needs a second input (P1 stream) | medium |
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
| Passive types (Mark, 5/4/3-band, console) | 🆕 ◐ | Broad, overlapping proportional-Q bands and end shelves at those networks' centres; neighbours interact through their skirts, but a cut does not move the other bands' corners as a real passive network does | large (component-level networks) |
| Master Q | 🆕 ✅ |  |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.13 IR Player (p. 49) → `irplayer`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Two IR slots (bank, number) | 🆕 ✅ | `ir` and `ir2`, baked into one stereo pair on the control thread and crossfaded (as the Cab) |  |
| Length (MAX…256, OFF) | 🆕 ✅ | Per slot, 5–680 ms with a short fade (the top is the whole IR); a slot's level mutes it |  |
| Per-IR Level, Pan | 🆕 ✅ | Balance-law pan |  |
| Mix | ✅ |  |  |
| Low Cut, High Cut | ✅ |  |  |
| Filter Slope | 🆕 ✅ | 6–24 dB/oct on each cut |  |
| Input Select, Mode (stereo, parallel, series) | 🆕 ✅ | Input: stereo, L+R, left, right. Mode: Parallel (summed), Stereo (IR 1 left, IR 2 right), Series (their convolution, offline by FFT, cut to 680 ms) |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.14 Looper (pp. 50–51) → rig looper (`Cathode.Engine.Rig.Looper`), not a grid block

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Record, Play, Undo, Reverse | ✅ | Toolbar, MIDI and footswitch actions |  |
| Erase | ✅ |  |  |
| Once (one-shot) | ❌ |  | cheap |
| Trim Start, Trim End | ❌ |  | cheap |
| Threshold recording | ❌ |  | cheap |
| Max Loop Time | ◐ | Fixed 60 s; the guide's 300 s fits in native memory | cheap |
| Playback Level, Dry Level | ◐ | Looper level only | cheap |
| Overdub Level (loop decay) | ❌ |  | cheap |
| Crossfade | ❌ |  | cheap |
| Loop Quantize (to tempo) | ❌ |  | cheap |
| Record 2nd Press (overdub, play, stop) | ◐ | Fixed to overdub | cheap |
| Speed | ◐ | Half speed only | cheap |
| Low Cut, High Cut | ❌ |  | cheap |
| As a grid block (placed mid-chain, bypass modes) | ❌ | See §7 | medium |

### 6.15 Megatap (pp. 52–53) → `megatap`

Types: 20 presets → none (shape choices instead).

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Master Level | ◐ | Level | — |
| Config | Delay Time (last tap), Tempo | ✅ |  |  |
| Config | Number of Taps (1–128) | ◐ | Up to 40 | cheap |
| Config | Predelay | ❌ |  | cheap |
| Config | Feedback, Feedback Tap | ❌ |  | cheap |
| Config | Low Cut, High Cut | ✅ |  |  |
| Tap control | Time Alpha | ◐ | Time Shape (±) | — |
| Tap control | Time Randomize | ❌ |  | cheap |
| Tap control | Amplitude Shape | ✅ | Level Shape |  |
| Tap control | Amplitude Alpha, Randomize | ❌ |  | cheap |
| Tap control | Pan Shape | ✅ |  |  |
| Tap control | Pan Alpha | ❌ |  | cheap |
| Tap control | Stereo Spread | ◐ | Spread 0–100 % | cheap |
| Diffusion | Diffusion Mix, Time, LFO Rate/Depth | ❌ |  | cheap |
| Envelope | Threshold, Attack, Release (volume swell into the taps) | ❌ |  | cheap |
| Mix | Input Gain, Input Select | 🆕 ✅ |  |  |
| Mix | Global Mix | ❌ | §3 | medium |

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

Core types: Quad-Tap, Quad Parallel, Quad-Series, Quad Diffusor, Quad Tape, Quad Tap Band, plus 28 variants → Cathode 6: Quad Tap, Ten Tap, Plex, Ducking, Reverse, Sweep. Missing algorithms: Quad Parallel (with ring mod, band-pass, comb per line), Quad Series, Quad Diffusor (four 2 s diffusers), Quad Tape: **medium** each.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Master | Master Time | ✅ | Time (taps are % of it) |  |
| Master | Master Feedback, Level, Pan | ◐ | Feedback, Level; Spread for pan | cheap |
| Master | Master LFO rate/depth, filter freq/Q, chorus, comb, ring mod scalers | ❌ |  | with the features |
| Master | Drive | ❌ |  | cheap |
| Diffuser | Diffusion Mix, Time, LFO | ◐ | Diffusion (Plex only) | cheap |
| Ducker | Threshold, Attenuation, Release | ✅ | Ducking type |  |
| Envelope | Threshold, Attack, Release | ❌ |  | cheap |
| Delay | Time 1–4, Tempo 1–4 | ◐ | Tap times as % of Time; no per-tap tempo | cheap |
| Delay | Feedback 1–4, cross feedback 1→2…4→1 | ❌ | One feedback | cheap–medium |
| Quad Series | Feedback, Send, Return | ❌ |  | medium |
| Level/Pan | Level 1–4 (± invert), Pan 1–4 | ◐ | No polarity | cheap |
| EQ | Filter type/freq/Q/gain, low/high cut and slopes | ◐ | Low and high cut | cheap |
| Modulation | LFO 1/2 type, phase, rate, tempo, depth | ❌ |  | cheap |
| Filter | Band-pass 1–4 frequency and Q, filter LFO | ❌ |  | cheap–medium |
| Chorus | Per-voice chorus rate/depth | ❌ |  | cheap |
| Comb | Comb time and feedback per line | ❌ |  | cheap |
| Ring mod | Frequency and mix per line | ❌ |  | cheap |
| Mix | Input Select, Input Gain | 🆕 ✅ |  |  |
| Mix | Global Mix | ❌ | §3 | medium |

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
| Input Select: inputs 1–4, USB 5/6 and 7/8 | ❌ | Needs multi-channel input: the audio shim delivers one stereo pair, so there is nothing to select yet (P4 left it for the shim work) | medium (shim) |
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

Types: 16 (digital mono/stereo, Script 45/90, Block 90, Classic/FAS Vibe, Stereo 8-stage, Barberpole, Stripe 90, …) → 1 algorithm; the Uni-Vibe is its own `vibe` block. Missing algorithms: Barberpole, the VCR element models.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate, Depth, Feedback (±), Mix | ✅ |  |  |
| Tempo | 🆕 ✅ |  |  |
| Manual | ✅ | `center` |  |
| Tone (wet) | ❌ |  | cheap |
| Order (2–12) | ✅ | `stages` |  |
| Feedback Point | ❌ |  | cheap |
| Minimum / Maximum Frequency | ◐ | Centre and sweep in octaves | cheap |
| LFO Type | 🆕 ✅ |  |  |
| LFO Phase | ✅ | `spread` |  |
| LFO Quantize, Astable Beta, LFO Hicut | ◐ | The Astable shape is offered (fixed curvature); no quantise or hicut | cheap |
| VCR Type (linear, exponential, JFET, photocell), VCR Bias, Exponent, Photocell attack/release | ❌ |  | medium |
| LFO Reset on Bypass | ❌ | Needs an engage signal | medium |
| Barberpole: Sweep Direction, Filter Q | ❌ | Shepard-style algorithm | medium |
| Global Mix | ❌ | §3 | medium |

### 6.22 Pitch (pp. 66–76) → `pitch`

Types: 16 (dual/quad detune, dual/quad chromatic and quad chromatic delay, dual/quad diatonic and quad diatonic delay, custom shift, arpeggiator, classic/advanced whammy, octave divider, crystal echoes, virtual capo) → Cathode 9 (Detune, Chromatic, Diatonic, Whammy, Octave Divider, Crystals, Arpeggiator, Virtual Capo, Custom Shifter) with four voices. The dual/quad and "delay" variants are voice counts and feedback placement.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Input Mode (mono, stereo) | ❌ |  | cheap |
| Config | LFO rate/tempo/depth on detune | ❌ |  | cheap |
| Master | Master Level, Pan, Feedback, Pitch, Delay | ❌ |  | cheap |
| Detector | Source (block, input 1–4) | ❌ | Needs another input | medium |
| Detector | Pitch tracking (fast, smooth, off) | ◐ | One YIN tracker | cheap |
| Detector | Tracking (splice length) | ❌ |  | cheap |
| Detector | Temperament (equal, just) | ❌ |  | cheap |
| Detune | Detune 1–4 | ◐ | One detune amount | cheap |
| Detune | Delay 1–4 | ✅ | Voice delays |  |
| Detune | Feedback mode, Feedback 1–4 | ❌ |  | cheap |
| Chromatic | Shift 1–4, Level 1–4, Pan 1–4, Delay 1–4 | ✅ |  |  |
| Chromatic | Delay Tempo 1–4 | ❌ |  | cheap |
| Diatonic | Key, Scale, Harmony 1–4 | ✅ | Plus key from MIDI or auto |  |
| Diatonic | Learn | ◐ | Key Source "Auto" | — |
| Diatonic | Glide Time | ❌ |  | cheap |
| Diatonic | Custom scale (notes, tonic) | ◐ | Custom Shifter tables (12 per voice) | — |
| Arpeggiator | Run, Steps, Tempo, Stage shifts | ✅ | 8 of 16 steps | cheap (16) |
| Arpeggiator | Repeats, Amplitude/Pan shape and alpha | ❌ |  | cheap |
| Whammy | Mode (classic), Start/Stop shift, Control | ✅ | Start, End, Pedal |  |
| Whammy | Tracking mode (mono, poly, off) | ❌ | Polyphonic shifting | large |
| Crystals | Feedback type, splice time/tempo, direction, diffusion | ◐ | Shift, time, feedback | cheap |
| Custom Shifter | Scale 1/2, Key | ✅ |  |  |
| Virtual Capo | Shift, Detune | ◐ | Shift (capo) | cheap |
| Mix | Input Gain | ❌ |  | cheap |
| Mix | Low Cut, High Cut (wet) | ❌ |  | cheap |
| Mix | Global Mix | ❌ | §3 | medium |

### 6.23 Plex Delay (pp. 77–79) → `multidelay` type Plex

Types: Plex Delay, Plex Detune, Plex Shifter, Plex Verb, Shimmer Verb + 39 variants → one 4-line Hadamard plex with allpass diffusion. **Verdict: a separate block is a medium-to-large gap** (see §8).

| Parameter | Cathode | Note |
|---|---|---|
| Number of Delays (up to 8) | ◐ | Fixed 4 |
| Decay Time | ◐ | Feedback |
| Diffusion | ✅ |  |
| Low Cut, High Cut | ✅ |  |
| Pre-Delay | ❌ |  |
| Stack/Hold | ❌ |  |
| Master Time / Level / Pan | ◐ | Time, Level, Spread |
| Delay Time 1–8, Tempo 1–8, Level 1–8, Pan 1–8 | ❌ | Fixed ratios |
| Filter (type, freq, Q, gain), LFO, Mod Frequency | ❌ |  |
| Envelope follower | ❌ |  |
| Detune 1–8, Granule Length, Master Detune | ❌ |  |
| Shift 1–8, Direction | ❌ |  |
| Plex Verb Size, Decay; Shimmer Intensity, Crossfade | ❌ |  |
| LFO rate/depth/tempo, input diffusion, ducker | ❌ |  |
| Input Gain, Input Select | 🆕 ✅ | Through Multi-Delay |

### 6.24 Realtime Analyzer (p. 80) → `PUT /api/v1/rta`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Tap anywhere on the grid | ❌ | Output only; a pass-through block that publishes its input is | medium |
| Decay Time | ◐ | Fixed ballistics | cheap |
| Input Select | ❌ |  | cheap |
| Bands (32/64/128) | ◐ | Sixth-octave bands | cheap |
| Window Type | ❌ |  | cheap |

### 6.25 Resonator (p. 81) → `resonator`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Mode (manual, chord) | ✅ | Tuning |  |
| Master Freq / chord root, chord type | ✅ |  |  |
| Master Level, Pan, Feedback, Q | ◐ | Decay and damping act as master feedback | cheap |
| Input Mode (mono, stereo) | ❌ |  | cheap |
| Frequency 1–4 | ✅ |  |  |
| Feedback 1–4 | ❌ | One decay for all | cheap |
| Filter Location 1–4, Filter Q 1–4 (band-pass) | ❌ |  | cheap |
| Level 1–4, Pan 1–4 | ✅ |  |  |
| Input Gain | 🆕 ✅ |  |  |
| Global Mix | ❌ | §3 | medium |

### 6.26 Reverb (pp. 82–85) → `reverb`

Types: 66+ (halls, chambers, rooms, cathedrals, studios, plates, springs, classic digital) → Cathode 5 (Room, Hall, Plate on an FDN; Dattorro Plate; Spring). Most named types are presets of the FDN; missing algorithms: chamber/cathedral early-reflection sets, the classic digital reverbs, multi-spring variants.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Basic | Time | ✅ | Decay |  |
| Basic | Predelay | ✅ |  |  |
| Basic | Size | ✅ |  |  |
| Basic | Crossover Frequency, Low/High Freq Time | ◐ | High Cut damps the tail | cheap |
| Basic | Early Level, Late Level | ❌ | No separate early reflections | medium |
| EQ | Low Cut, High Cut | ✅ | High Cut = damping |  |
| EQ | Low Mid and High Mid peaking EQ | ❌ |  | cheap |
| Advanced | Pre-Delay Tempo, Feedback, Mix | ❌ |  | cheap |
| Advanced | Echo Density, Early Diffusion and Time, Early Decay, Late Input Mix | ❌ |  | medium |
| Advanced | Quality (economy … ultra-high) | ❌ |  | medium |
| Advanced | Stack/Hold | 🆕 ◐ | Hold (a 600 s decay); no Stack | cheap |
| Advanced | Wall Diffusion, Input Diffusion and Time | ◐ | Fixed diffusers | cheap |
| Advanced | Mic Spacing | ❌ |  | cheap |
| Spring | Number of Springs, Spring Tone, Drive, Boiiinnng | ◐ | Two springs, size and decay | cheap |
| Modulation | Mod Depth, Rate, LFO Phase | ◐ | Fixed internal modulation | cheap |
| Pitch | Pitch mix, shift 1/2, voice balance, splice, feedback, direction, position | ❌ | Shimmer | medium |
| Ducker | Attenuation, Threshold, Release | 🆕 ✅ |  |  |
| Mix | Stereo Spread | 🆕 ◐ | ±100 % | cheap |
| Mix | Input Gain, Input Select | 🆕 ✅ |  |  |
| Mix | Global Mix | ❌ | §3 | medium |

### 6.27 Ring Modulator (p. 86) → `ringmod`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type (classic, two single-sideband) | ◐ | Classic only; SSB needs a Hilbert pair | medium |
| Frequency | ✅ | Plus tempo sync |  |
| Frequency Multiplier (0.25–4×) | 🆕 ✅ |  |  |
| Pitch Tracking | ❌ | Reuse the YIN tracker from Pitch | medium |
| Hi Cut | ✅ |  |  |

### 6.28 Rotary (p. 87) → `rotary`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate (with park at 0), Tempo | ◐ | Slow/Fast switch with separate rates; no tempo, no park | cheap |
| Drive | ✅ |  |  |
| Hi Depth, Low Depth | ✅ | Horn and drum Doppler |  |
| Hi Level | ✅ | Horn and drum levels |  |
| Rotor Length | ❌ |  | cheap |
| Low Rate Multiplier | ◐ | Separate drum rates | — |
| Low/Hi Time Constant | ✅ | Accel and decel for each |  |
| LF and HF Mic Spacing | ◐ | One mic spread | cheap |
| Mic Distance | ❌ |  | cheap |
| Stereo Spread | ◐ | Mic spread | cheap |
| Input Select | ❌ |  | cheap |

### 6.29 Scene MIDI (p. 88) → missing

Eight PC/CC messages (type, channel, number, value) per scene, sent when a scene loads. **Verdict: missing, medium** (see §7).

### 6.30 Send and Return (pp. 89–90) → `fbsend`, `fbreturn`

| Parameter | Cathode | Note |
|---|---|---|
| Send: no parameters | ✅ | Cathode adds a send level |
| Return Level | ✅ | `level` (named Return Level) |
| Output Level | 🆕 ✅ |  |
| Balance | 🆕 ✅ |  |
| Two pairs | ◐ | One pair per preset (max instances 1) |

### 6.31 Synth (p. 91) → `synth`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Three voices | ◐ | Two | cheap |
| Waveform: sine, triangle, square, saw | ✅ |  |  |
| Waveform: random, white noise, pink noise, off | ◐ | Off = level at −∞ | cheap |
| Track: off, envelope only, pitch + envelope | ◐ | Always pitch + envelope | cheap |
| Frequency (manual) | ❌ | With the tracking modes | cheap |
| Shift, Tune | ✅ |  |  |
| Duty Cycle | ◐ | Global pulse width | cheap |
| Pan per voice | ❌ |  | cheap |
| Filter frequency and Q per voice | ◐ | One shared filter with type and envelope | cheap |
| Attack (input envelope) | ✅ | Plus release, glide, threshold |  |
| Level per voice | ✅ |  |  |
| Global Mix | ❌ | §3 | medium |

### 6.32 Ten-Tap Delay (pp. 92–93) → `multidelay` type Ten Tap

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type: Ten-Tap | ✅ |  |  |
| Type: Rhythm Tap (per-tap times, quantise, learn, feedback) | ❌ |  | medium |
| Mono/Stereo | ❌ |  | cheap |
| Delay Time, Tempo | ✅ |  |  |
| Number of Taps (1–10) | ❌ | Fixed ten | cheap |
| Decay | ✅ | Level Taper |  |
| Shuffle | ✅ |  |  |
| Spread, Ratio | ◐ | Spread | cheap |
| Pan Shape, Pan Alpha | ❌ | Alternating pans | cheap |
| Low Cut, High Cut | ✅ |  |  |
| Tap Level 1–10 | ❌ |  | cheap |
| Input Gain | 🆕 ✅ |  |  |

### 6.33 Tone Match (p. 94) → `tonematch`

The guide refers to a separate manual. Cathode's block applies a matched minimum-phase filter with amount and level; matching runs through `POST /live/blocks/{id}/tonematch`. **Complete** for the guide's scope.

### 6.34 Tremolo/Panner (p. 95) → `tremolo`

Types: Tremolo, Panner, Bias Trem, Harmonic Trem, Optical Trem → 🆕 all five.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate, Tempo | ✅ / 🆕 |  |  |
| Depth / Width (beyond 100 % for the panner) | ◐ | Up to 100 % | cheap |
| LFO Type, LFO Duty Cycle | 🆕 ✅ |  |  |
| LFO Phase | 🆕 ✅ |  |  |
| Start Phase | ❌ | Needs an engage signal | medium |
| Trigger Threshold (restart on playing) | ❌ |  | cheap |
| Crossover Frequency (harmonic) | 🆕 ✅ | One-pole split |  |
| Crossover Slope | ❌ |  | cheap |
| Pan Center | 🆕 ✅ |  |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.35 Vocoder (p. 96) → `vocoder`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Analysis Source (an input) | ◐ | Carrier from the right input or internal; analysis is the block input | medium |
| Analysis Channel | ❌ |  | cheap |
| Number of Bands (up to 24) | ◐ | Fixed 20 | cheap |
| Min / Max Frequency | ❌ |  | cheap |
| Filter Resonance | ❌ |  | cheap |
| Frequency Shift | ❌ |  | cheap |
| Freeze | ❌ |  | cheap |
| Highpass Mix | ◐ | HF Emphasis | cheap |
| Attack, Decay | ✅ | Attack, Release |  |
| Master Level | ✅ | Level |  |
| Master Pan, Level 1–24, Pan 1–24 | ❌ |  | cheap |
| Input Gain | ❌ |  | cheap |

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

Types: FAS Wah, Clyde, Cry Babe, VX846, Color-Tone, Funk Wah, Mortal, VX845, Paragon → Cathode Classic, Vox, Wide (filter models) and Cry Baby, V846 (component-level circuits). Missing: Clyde, Colorsound, Morley-style, V845, Parapedal: **medium** each as circuits, **cheap** as filter voicings.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Wah Control | ✅ | Position |  |
| Control Taper | 🆕 ✅ | Linear, log, reverse log |  |
| Resonance Q | 🆕 ◐ | Filter types only | — |
| Q Tracking | 🆕 ◐ | Filter types only | — |
| Low Cut Frequency | 🆕 ✅ |  |  |
| Minimum / Maximum Frequency | ❌ | Fixed per voicing | cheap |
| Drive | ❌ | Circuit types saturate naturally | cheap |
| Inductor Bias | ❌ |  | medium (circuit) |
| Fat (dry blend up to 50/50) | ✅ | Mix |  |
| EQ page (8-band graphic) | ❌ |  | cheap |

---

## 7. Missing blocks

Checked against what exists under other names.

| Guide block | Exists as | Verdict | Effort |
|---|---|---|---|
| Looper | Rig looper (stereo, 60 s, overdub with undo, reverse, half speed, WAV export), at the end of the rig | Functionally present, not placeable. A grid block would let it sit before effects and take bypass modes; most Config-page features are cheap additions to the rig looper (§6.14) | medium (block), cheap (features) |
| Mixer | 🆕 `mixer` | **Implemented** in this pass | — |
| Plex Delay | Multi-Delay type "Plex" (4 lines) | Partial; a real Plex block is the largest delay gap | medium (Plex Delay/Verb), large (Detune/Shifter/Shimmer) |
| Ten-Tap Delay | Multi-Delay type "Ten Tap" | Covered; Rhythm Tap and per-tap levels missing (§6.32) | cheap–medium |
| Scene MIDI | — | **Missing.** MIDI output exists (footswitch LED feedback), so the block is a preset-level table of 8 messages per scene sent on scene change | medium |
| Tremolo/Panner | `tremolo` | **Complete** for its five types after this pass | — |
| Graphic EQ modes | `geq` | 🆕 Ten layouts (all 16 guide types with the Q mode); the passive ones approximated (§6.12) | large (component-level passives) |
| RTA | Analyser utility (`PUT /rta`, editor dialog) | Output only; a pass-through block to tap mid-grid is medium | medium |
| Input/Output features | Built-ins | Noise gate on the Input and the row mixer on the Output missing (§2) | medium / cheap |
| Send/Return | `fbsend` / `fbreturn` | Present (one pair) | cheap (two pairs) |
| Tone Match | `tonematch` | Present | — |
| — (Cathode only) | `tonestack`, `vibe`, `fxloop` | Cathode extras: a standalone amp tone stack, a separate Uni-Vibe, and an FX loop with latency compensation | — |

---

## 8. Plan for the medium and large gaps

In order of value to a player, then cost. Each item meets the [Definition of done](03-testing-strategy.md#12-definition-of-done-any-block-or-feature) (conformance, analytic tests, golden, measured cost, editor).

1. **Global Mix** (medium). A global "effects mix offset" (±50 %) in the global settings, sent to the engine as a rig parameter; a `globalMix` switch on Chorus, Delay, Flanger, Megatap, Multi-Delay, Phaser, Pitch, Resonator, Reverb, Synth; each block adds the offset where it reads Mix. One shared helper, one engine parameter, one API field, a Global-page control.
2. **Input block noise gate and Output row mixer** (medium + cheap). Give the built-in nodes parameters: the Input node runs the Gate block's code (Classic; Intelligent adds a hum-aware filter at the mains frequency), the Output node gets per-row level and balance through the same row routing as the Mixer, plus a main balance. Needs the compiler to host parameters on built-ins and the editor to show them.
3. **Scene MIDI** (medium). A preset-level `sceneMidi` table (8 × type/channel/number/value per scene) in the contract, sent through the existing MIDI output when a scene loads; an editor table on the Scenes page. Not a DSP block.
4. **Delay completion** (medium). Dual type (independent L/R time, feedback and cross-feedback), the two-head mono tape with motor speed, the EQ page (two peaking bands, slopes), a second LFO with targets, a diffuser (reuse the reverb's), feedback polarity and master feedback to 200 %, 16 s lines in native memory.
5. **Plex Delay block** (medium, then large). A new `plex` block: an 8-line feedback matrix (Hadamard or Householder) with per-line time/tempo/level/pan, decay time, input diffusion, pre-delay, stack/hold, filter with LFO, envelope and ducker (reuse `Ducker`). Second step: per-line granular detune and shift (reuse the Crystals splice shifter) for Plex Detune, Plex Shifter and Shimmer Verb.
6. **Reverb depth** (medium). Separate early reflections with Early/Late level, the two peaking EQ bands, pre-delay tempo/feedback/mix, modulation controls, a quality setting (line count), and a shimmer pitch section (reuse the splice shifter).
7. **Type presets** (medium, ongoing). Many guide "types" are parameter presets (17 choruses, 31 flangers, 16 phasers, 23 delays, 66 reverbs, 20 megataps). Offer them as factory block presets in the Block Library rather than as a Type parameter, so they never fight the knobs. Needs a curated preset set per block and the library's import path.
8. **Multi-Delay algorithms** (medium each). Quad Parallel (per-line ring mod, band-pass and comb), Quad Series (send/return routing), Quad Diffusor, Quad Tape; per-tap feedback, filters and chorus; Rhythm Tap.
9. 🆕 **Done (P4)**, except component-level passive networks. **Graphic EQ layouts** (medium). Type with 10 (2/3 oct), 8, 7 and 5 bands using `typeLabels` to rename and hide bands, shelving outer bands, then the passive types (Mark 5-band, 4-band, 3-band, console). Keep the global output EQ on the plain 10-band type.
10. **Compressor and Gate sidechain input** (medium). A sidechain source (row or input) needs a second input into a block: the same `IRowInputBlock` routing can deliver a chosen row into a side buffer.
11. **Look-ahead compressor** (medium). The compiler reads latency at compile time; a parameter-dependent latency needs a recompile on change or a fixed maximum look-ahead always reported.
12. **"On engage" behaviour** (medium). LFO Bypass Reset (Flanger, Phaser) and Tremolo Start Phase need the engine to tell a block it was engaged; add an optional `IEngageAware` the compiled graph calls when a bypass fade starts.
13. **Phaser VCR models and Barberpole; Flanger thru-zero; Chorus Dimension** (medium each).
14. **Pitch completion** (cheap items batched, medium overall). Master scalers, per-voice detune and feedback, input gain and mode, wet cuts, temperament, glide, 16 arpeggiator steps, LFO on detune; polyphonic whammy tracking is large.
15. 🆕 **Done (P4)**, except per-circuit extra knobs, op-amp slew per model and new circuits. **Drive EQ pages and expert controls** (medium). The EQ 1 page (low/high cut, bass/mid/high-mid/treble) and EQ 2 (the GEQ bands pre/post) are generic; diode type/count and bias need per-circuit WDF parameters. New circuits continue as in Phase 6 (medium each).
16. **Looper as a block** (medium), with its Config-page features (cheap): once, trims, threshold start, overdub level, crossfade, quantise, 300 s.
17. **Ring Modulator SSB and pitch tracking; Synth third voice and tracking modes; Vocoder band controls; IR Player second slot; Cab preamp, air and input mode** (cheap to medium each). 🆕 The IR Player and Cab items are done (P4).
18. 🆕 **Mostly done (P4)**: boost, saturation, input filter, output EQ with location, output compressor, input dynamics, speaker drive, thump and compression, bias tremolo, Balance; power-tube hardness and the per-model items remain. **Amp gaps** follow [06 §5](06-amp-modelling-spec.md#5-recommended-plan): input boost (reuse Drive circuits), saturation stage, output EQ types and location, output compressor and input dynamics, speaker drive and compression.
