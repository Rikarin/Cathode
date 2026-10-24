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
| Amp (AMP) | `circuitamp`, `nam` | 309+ → 26 circuit + any capture | see [06 §1.4](06-amp-modelling-spec.md#14-parity-summary) | Core panel and most expert pages done; boost, saturation, output compressor missing |
| Cab (CAB) | `cab`, `dynacab` | 2 150+ IRs → user IRs + synthesised DynaCab | 11 / 6 / 13 | DynaCab strong; legacy Cab thin (2 slots, no preamp/room/air) |
| Chorus (CHO) | `chorus` | 17 → 1 | 🆕 19 / 0 / 1 | 🆕 Done: 8 voices, Dual ratios, BBD drive, Dimension, LFO 2; Global Mix (P1) and type presets left |
| Compressor (CMP) | `compressor` | 16 → 🆕 4 | 🆕 10 / 2 / 7 | Core done; look-ahead, detector choice, feedback types missing |
| Crossover (XVR) | `crossover` | 1 → 1 | 🆕 5 / 2 / 0 | Done (per-side levels merged) |
| Delay (DLY) | `delay` | 23+ → 🆕 5 | 🆕 25 / 6 / 17 | Core, ducker, hold, bit reduction, ping-pong done; dual/tape heads, EQ page, LFO 2 missing |
| Drive (DRV) | `drive` | 60+ → 27 | 🆕 6 / 1 / 12 | Strong circuits; EQ pages and expert clip controls missing |
| Dynamic Distortion (DND) | `dyndist` | 7 → 3 | 🆕 5 / 2 / 3 | Different design (envelope-driven); filter section missing |
| Enhancer (ENH) | `enhancer` | 3 → 3 | 🆕 7 / 2 / 1 | Done except the Stereoizer type |
| Filter (FLT) | `filter` | 15 → 🆕 15 | 🆕 21 / 0 / 0 | 🆕 Done: Shelf 2, Peaking 2, all-pass feedback, first-order cuts, LFO duty and quantise |
| Flanger (FLG) | `flanger` | 31 → 1 | 🆕 22 / 0 / 2 | 🆕 Done: thru-zero, drive, bass focus, VCO response, min/max time; LFO reset on engage (P1) and Global Mix left |
| Formant (FOR) | `formant` | 1 → 1 | 8 / 0 / 0 | Done (Cathode adds Shift) |
| Gate/Expander (GTE) | `gate` | 4 → 🆕 2 | 🆕 12 / 1 / 1 | Done except modern types and row sidechain |
| Graphic EQ (GEQ) | `geq` | 16 → 🆕 2 | 🆕 4 / 1 / 2 | 10-band only; band layouts and passive types missing |
| IR Player (IRP) | `irplayer` | 1 → 1 | 🆕 6 / 1 / 4 | One IR; second slot, series/parallel and length missing |
| Looper (LPR) | rig looper (not a block) | 1 → 1 | 9 / 3 / 6 | Works, but as a rig utility at the end of the chain |
| Megatap (MGT) | `megatap` | 20 → 1 | 🆕 10 / 4 / 9 | Shapes done; alphas, randomise, feedback, diffusion, envelope missing |
| Mixer (MIX) | 🆕 `mixer` | 1 → 1 | 🆕 6 / 0 / 0 | Done in this pass |
| Multitap Delay (MTD) | `multidelay` | 34+ → 6 | 🆕 14 / 8 / 20 | Quad Tap, Ten Tap, Plex, Ducking, Reverse, Sweep; quad-series/parallel, per-tap filters, chorus, comb, ring mod missing |
| Multiband Comp (MBC) | `mbcomp` | 1 → 1 | 🆕 10 / 1 / 4 | Done except detector, mute, slope, auto attack/release |
| Multiplexer (MUX) | `mux` | 1 → 1 | 🆕 3 / 1 / 0 | Done (rows only, no input jacks) |
| Parametric EQ (PEQ) | `peq` | 1 → 1 | 🆕 5 / 1 / 1 | Done except Shelving 2/Peaking 2 and Solo |
| Phaser (PHA) | `phaser` (+ `vibe`) | 16 → 🆕 2 (+ Vibe) | 🆕 22 / 0 / 2 | 🆕 Done: VCR types, Barberpole, tone, feedback point, LFO conditioning; LFO reset on engage (P1) and Global Mix left |
| Pitch (PIT) | `pitch` | 16 → 9 | 🆕 43 / 2 / 1 | 🆕 Done: polyphonic whammy, per-voice detune and feedback, master scalers, stereo input, temperament, detector source, 16-step arp with shapes |
| Plex Delay (PLX) | `multidelay` type Plex | 44 → 1 | 4 / 3 / 22 | **Medium/large gap**: a 4-line plex without shifters |
| Resonator (RES) | `resonator` | 1 → 1 | 🆕 19 / 0 / 1 | 🆕 Done: per-voice feedback and band-pass, masters, stereo input |
| Return (RTN) | `fbreturn` | 1 → 1 | 🆕 3 / 0 / 0 | Done |
| Reverb (REV) | `reverb` | 66+ → 5 | 🆕 14 / 3 / 19 | Core and 🆕 ducker/hold/spread; EQ page, early/late, pitch section missing |
| Ring Modulator (RNG) | `ringmod` | 3 → 🆕 3 | 🆕 7 / 0 / 0 | 🆕 Done: upper and lower sideband, pitch tracking |
| Rotary (ROT) | `rotary` | 1 → 1 | 🆕 17 / 1 / 0 | 🆕 Done: brake, tempo, rotor length, mic spacing and distance, input select, spread |
| RTA | analyser (`PUT /rta`) | 1 → 1 | 1 / 1 / 2 | Output-only analyser, not a grid block |
| Scene MIDI (MIDI) | — | 1 → 0 | 0 / 0 / 4 | **Missing**: medium |
| Send (SND) | `fbsend` | 1 → 1 | 1 / 0 / 0 | Done (Cathode adds a send level) |
| Synth (SYN) | `synth` | 1 → 1 | 🆕 19 / 0 / 1 | 🆕 Done: three voices with tracking modes, noise shapes, per-voice filter, duty and pan |
| Ten-Tap Delay (TTD) | `multidelay` type Ten Tap | 2 → 1 | 7 / 3 / 7 | Ten Tap works; Rhythm Tap and per-tap levels missing |
| Tone Match (TMA) | `tonematch` | 1 → 1 | 2 / 0 / 0 | Done (the guide defers to a separate manual) |
| Tremolo/Panner (TRM) | `tremolo` | 5 → 5 | 🆕 12 / 0 / 1 | 🆕 Done: width to 200 %, trigger, crossover slope; start phase on engage (P1) left |
| Vocoder (VOC) | `vocoder` | 1 → 1 | 🆕 18 / 0 / 0 | 🆕 Done: 4–24 bands, range, resonance, shift, freeze, per-band level and pan, analysis from a row |
| Volume/Pan (VOL) | `volpan` | 1 → 1 | 🆕 10 / 0 / 0 | Done in this pass |
| Wah (WAH) | `wah` | 9 → 🆕 10 | 🆕 13 / 1 / 0 | 🆕 Done: five more voicings, drive, inductor bias, EQ page; min/max frequency on the filter voicings only |

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

**Stream P3 (modulation, pitch and synthesis), second pass.** Every ❌ and ◐ row of §6.1, 6.8, 6.9, 6.21, 6.22, 6.25, 6.27, 6.28, 6.31, 6.34, 6.35 and 6.37 is now 🆕 ✅, except what belongs to stream P1 (Global Mix, and LFO Bypass Reset and Tremolo Start Phase, which wait for the "on engage" hook; `IEngageAware` is not on master yet), the Pitch block's Learn and custom-scale rows (covered by Key Source Auto and the Custom Shifter tables) and the Wah's Min/Max Frequency on the two component-level circuits (their range is set by their parts; Inductor Bias moves it). Defaults leave every block's sound as it was: the golden renders still match. Shared pieces, all new files: `LfoConditioner` (LFO quantise, hicut, auto depth), `SoftDrive`, `FirstOrderCuts`, `HilbertPair` (single sideband), `AllpassDiffuser`, and in `Pitch/` the `PhaseVocoderShifter` (peak-locked, for the polyphonic Whammy), plus a splice length on `SpliceShifter`, a variable grain on `GrainShifter`, `Scales.JustRatio`, `WahCircuit.Trim` (inductance and R7) and an optional shelf-Q limit on `SvfCoefficients.Design`. The Vocoder and the Pitch block now implement `IRowInputBlock`, so a chosen grid row can be their analysis or detector signal (docs/02 §5.3). The Synth's parameters are regrouped per voice and the Pitch block's grew past its old layout (no preset compatibility is kept). Also from this pass, outside the stream's rows: Input Select on the Rotary, Input Gain on the Pitch block, and Stereo Spread on the Chorus, Flanger and Rotary (§3).

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

Audited in detail in [06 §1](06-amp-modelling-spec.md#1-axe-fx-iii-amp-block-parameter-parity); this pass did not touch the Amp blocks (they are being changed by other streams). Current state of `circuitamp` (52 parameters, 26 types; catalogue in [07](07-amp-catalogue.md)):

| Page | Present | Missing | Effort for the gaps |
|---|---|---|---|
| Type, Tone | Type, Gain, Bass, Middle, Treble, Master, Presence, Depth, Cut, Bright, model switches and second controls, Input Trim | Presence Shift as a generic switch, Overdrive on all models | cheap per model |
| Preamp | Preamp tube type, Bright Cap, Preamp Bias, Preamp Sag | Input Boost (type, level), Saturation switch and drive, Fat and Cut as generic switches, High Treble | medium (boost = reuse Drive circuits), large (saturation stage) |
| Input EQ | Input select, Low Cut | Input filter (type, freq, Q, gain), Definition | cheap |
| Output EQ | 5 EQ bands (`eq1`–`eq5`) | EQ type (3–8 bands, passive, Mark), EQ location (input, pre-PA, output) | medium |
| Power supply | Sag, B+ time constant, supply type, rectifier, Variac | AC line frequency, screen frequency/Q | cheap / model data |
| Power amp | Power amp on/off, power tube type, bias, negative feedback, transformer matching, LF/HF, drive, speaker impedance | Power tube hardness, bias tremolo | medium |
| Speaker | Speaker curve, LF resonance freq/Q/peak, HF, impedance | Cabinet resonance, speaker drive, thump, speaker compression and its time constant, compliance | medium–large |
| Dynamics | — | Input dynamics, output compression (type, threshold, clarity), master bias excursion | medium (reuse Compressor) |
| Advanced | Tonestack frequency, master volume trim, presence/depth frequency, cathode time constant | Tonestack type substitution and location, triode plate frequencies, preamp hardness, cathode follower controls, PI/power tube bias excursion, plate suppressor diodes, master volume location/cap, output mode (FRFR / SS PA + cab), auto DynaCab impedance | medium–large; several are emergent in a circuit model ([06 §1.4](06-amp-modelling-spec.md#14-parity-summary)) |
| Output | Level | 🆕 Balance not added (Amp untouched in this pass), bypass mode handled by the engine | cheap |

The capture tier (`nam`) has input, output, normalise and a four-band post EQ; the guide has no counterpart (Fractal's NAM block arrived with firmware 33).

## 5. Cab block (pp. 19–24)

Not changed in this pass. `cab` (legacy, 2 IR slots) and `dynacab` (4 synthesised or user-grid mic slots) together:

| Guide parameter | Cathode | Note | Effort |
|---|---|---|---|
| Cab Mode (Legacy / DynaCab) | ✅ | Two blocks rather than a mode |  |
| Legacy: Bank and Cab Number, cab picker | ◐ | Any IR from the asset store; no factory banks (licensing) | — |
| Legacy: 4 IRs per channel | ◐ | 2 in `cab`, 4 in `dynacab` | medium (4 slots in `cab`) |
| Mic Distance (align, sub-ms delay) | ◐ | DynaCab per-slot delay and Auto Delay; not in `cab` | cheap |
| Proximity, Proximity Freq | ◐ | DynaCab Proximity (global); no frequency | cheap |
| Smoothing | ❌ |  | cheap–medium (cepstral or 1/n-octave smoothing at IR load) |
| DynaCab: cab type, mic type, position, distance | ✅ | Synthesised models; 10 speakers, 11 mics, 7 cabinets |  |
| Level per IR (relative, normalised mix), Mute, Solo, Pan | ✅ | DynaCab; `cab` has level only | cheap |
| Per-IR low/high cut and slopes | ◐ | Global low and high cut; no per-IR filters or slopes | cheap |
| IR Length | ❌ |  | cheap |
| Preamp page: Drive, Saturation, Bass/Mid/Treble, Mode, Type (Tube, FET, Tape…) | ❌ | Mic preamp simulation | medium |
| Master low/high cut and slopes | ◐ | No slope choice | cheap |
| Room page: level, size, shape, LF/HF damping, mic spacing, floor reflections, diffusion | ◐ | DynaCab Room (4 early reflections); no size/shape controls | medium |
| Align page | ◐ | Per-slot delay; no visual alignment view | medium (editor) |
| Mix page: Input Mode (L, R, Sum, Stereo), Air, Air Frequency | ❌ |  | cheap |
| FullRes long IRs (1.37 s) | ✅ | IR Player up to 680 ms; `cab` partitioned convolution | — |

## 6. Effect blocks

Each table lists the guide's parameters page by page. Unless noted, every block also has the common Mix/Level/Balance and bypass parameters covered in §3.

### 6.1 Chorus (pp. 25–26) → `chorus`

Types: guide 17 (digital mono/stereo, analog mono/stereo, Japan CE-2, warm stereo, 80's style, triangle, 8-voice, vintage tape, dimension 1–3, 4/8-voice analog, tri-chorus, dual) → Cathode 1 algorithm whose knobs now cover them: voices up to 8, the Dual ratios, BBD drive and the 🆕 Dimension matrix. The named types themselves are presets (§8 item 7, stream P1).

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
| Expert | Stereo Spread | 🆕 ✅ | −100…100 % on the wet (as Delay and Reverb), besides LFO Phase |  |
| Expert | Dimension Mode (off, low, med, high) | 🆕 ✅ | One voice per line, lines swept in antiphase, each side's wet minus 30/50/70 % of the other's: wide and still, little pitch movement in mono |  |
| Expert | Right Time Ratio (Dual) | 🆕 ✅ | 0.5–2× |  |
| Mix | Global Mix | ❌ | §3 (stream P1) | medium |

### 6.2 Compressor (pp. 27–29) → `compressor`

Types: guide 16 (Studio FF/FB compressor and sustainer, analog compressor/sustainer, compander, DynaComp, dynamics processor, JFET compressor/sustainer, optical compressor/sustainer, pedal 1/2, tube) → Cathode 4 (Pedal, Studio, Opto, 🆕 Sustainer). Feedback topologies, JFET, tube and the compander are missing algorithms.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Threshold, Ratio, Attack, Release, Knee, Mix | ✅ |  |  |
| Config | Compression (single-knob types) | ◐ | Ratio covers it | — |
| Config | Dynamics (processor: compress ↔ expand) | ❌ |  | cheap |
| Config | Ratio ∞ (limiter) | ◐ | Ratio up to 20:1 | cheap |
| Config | Auto Attack/Release | ❌ | Program-dependent times | cheap |
| Config | Look Ahead | ❌ | Needs latency reporting that depends on a parameter; the graph compiler only reads latency at compile time | medium |
| Config | Detector Type (RMS, Peak, RMS+Peak) | ◐ | Tied to the type | cheap |
| Config | Light Type (optical) | ❌ |  | cheap |
| Config | Auto Makeup | 🆕 ✅ | Half the reduction at 0 dBFS |  |
| Config | Makeup | ✅ | Cathode extra |  |
| Sidechain | Source (block, row, input) | ❌ | Needs a second input into the block | medium |
| Sidechain | Low Cut, High Cut | 🆕 ✅ | Detector only |  |
| Sidechain | Emphasis, Emphasis Frequency; filter type/freq/Q/gain | ❌ |  | cheap |
| Mix | Input Level (instrument/line) | ❌ | Detector scaling | cheap |

### 6.3 Crossover (p. 30) → `crossover`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Frequency (LR4) | ✅ | 40 Hz–16 kHz covers the ×10 multiplier |  |
| Frequency multiplier | — | Not needed with the wider range |  |
| Lo/Hi Level, left and right | ◐ | One level per band for both sides | cheap |
| Low/Hi Pan for the left and right input bands | ◐ | One pan per band | cheap |
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
| Basic | Per-type extra knobs (e.g. Bass/Treble, Presence) | ◐ | One tone control per circuit | medium per circuit |
| EQ 1 | Low Cut, High Cut | ❌ | | cheap |
| EQ 1 | Bass, Mid (with frequency), High Mid, Treble ±12 dB | ❌ |  | cheap |
| EQ 2 | 10-band 2/3-octave graphic EQ, Pre/Post/Off | ❌ | Reuse GEQ bands | cheap |
| Advanced | Clip Type (4558/diode, FET, rectifier, Ge, hard, HV/LV tube, LED, op-amp, Si, soft, variable, null) | ◐ | Implied by the circuit; the three voicings have fixed clippers | medium |
| Advanced | Clip Shape | ❌ |  | cheap (for the voicings) |
| Advanced | Diode ± type and quantity | ❌ | Circuit parameters in WDF models | medium |
| Advanced | Bass Response | ❌ | Input coupling cap per circuit | cheap |
| Advanced | Dry Level (dry inside the circuit) | ❌ |  | cheap |
| Advanced | Bias | ❌ |  | cheap |
| Advanced | Slew Rate | ◐ | Modelled only in the RAT's op-amp | medium |
| Advanced | Bit Reduce, Sample Rate | ❌ |  | cheap |
| — | Input Vol (guitar volume knob) | ✅ | Cathode extra |  |

### 6.6 Dynamic Distortion (p. 40) → `dyndist`

Cathode's block distorts by envelope (drive follows the level) rather than through the guide's frequency-shaped filter. Types 7 presets → 3 clip shapes.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Preset page | ◐ | Type selects the clip shape | — |
| Filter Type (high shelf, low shelf, peaking), Frequency, Q, Gain | ❌ | The frequency-dependent part of the guide's design | cheap–medium |
| Clip Shape | ✅ | Type |  |
| Bias | ❌ |  | cheap |
| Drive | ✅ |  |  |
| Mono/Stereo | ❌ |  | cheap |
| Level, Pan | ✅ | Level and 🆕 Balance |  |
| — | Dynamics, Threshold, Attack, Release, Mix | ✅ | Cathode extras |  |

### 6.7 Enhancer (p. 41) → `enhancer`

Types: Modern, Classic, Stereoizer → Cathode Modern, Classic (a delayed-mid side signal, mono compatible), Haas (the guide's Classic: right channel delayed). The Stereoizer (high-order filter decorrelation) is missing: **cheap–medium**.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Modern: Width, Depth | ◐ | Width only | cheap |
| Modern: Low Cut, High Cut | ✅ |  |  |
| Classic: Width (0–20 ms) | ✅ | Time (Haas) |  |
| Classic: Invert | 🆕 ✅ |  |  |
| Classic: Pan L, Pan R | 🆕 ✅ |  |  |
| Classic: Balance | 🆕 ✅ |  |  |
| Stereoizer: Width, Depth | ❌ |  | cheap–medium |

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

Types: guide 31 named presets → Cathode 1 algorithm, now with 🆕 thru-zero and the VCO responses, so the named types are presets of its knobs (§8 item 7, stream P1).

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
| Expert | LFO Bypass Reset | ❌ | Needs the engine to tell a block it was engaged (§8 item 12, stream P1) | medium |
| Expert | LFO Hicut, LFO Quantize | 🆕 ✅ | `LfoConditioner`: one-pole on the LFO (0.5–50 Hz, 50 = off), 2–16 levels |  |
| Expert | Auto Depth | 🆕 ✅ | As on the Chorus |  |
| Expert | VCO Response, Exponent | 🆕 ✅ | Linear (in delay) or Exponential (in ratio, notches move evenly in octaves); Exponent 0.25–4 bends the LFO position |  |
| Mix | Stereo Spread | 🆕 ✅ | −100…100 % on the wet, besides LFO Phase |  |
| Mix | Global Mix | ❌ | §3 (stream P1) | medium |

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

Types: Classic Expander, Classic Gate, Modern Expander, Modern Gate → Cathode Expander, 🆕 Gate (the "modern" ballistics are missing: **cheap**).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Threshold, Ratio, Attack, Hold, Release | ✅ |  |  |
| Attenuation (gate types) | 🆕 ✅ |  |  |
| Knee Type (modern expander) | ❌ |  | cheap |
| Sidechain Select (block, row, input) | ❌ | Needs a second input | medium |
| Detector Type (peak, RMS) | 🆕 ✅ |  |  |
| Sidechain Low Cut, High Cut | 🆕 ✅ |  |  |
| Level, Balance | 🆕 ✅ |  |  |

### 6.12 Graphic EQ (p. 48) → `geq`

Types: guide 16 (10-band, 10-band 2/3-octave, 8-, 7- and 5-band, each constant or variable Q, plus passive 5-band Mark, 5-, 4- (JMPre-1) and 3-band, 3-band console) → Cathode 🆕 2 (10-band constant Q, 10-band variable Q).

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| 10 octave bands, ±12 dB | ✅ |  |  |
| Outermost bands as shelves | ❌ | Ours are peaks; a type option keeps the global output EQ unchanged | cheap |
| Band layouts (10 at 2/3 oct, 8, 7, 5) | ❌ | `typeLabels` can rename and hide bands per type, as the Circuit Amp does | medium (ids, editor check) |
| Variable Q | 🆕 ✅ |  |  |
| Passive types (Mark, 5/4/3-band, console) | ❌ | Passive networks; the Mark EQ also belongs in the amp | medium |
| Master Q | 🆕 ✅ |  |  |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.13 IR Player (p. 49) → `irplayer`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Two IR slots (bank, number) | ◐ | One IR | medium |
| Length (MAX…256, OFF) | ❌ |  | cheap |
| Per-IR Level, Pan | ❌ | With the second slot | cheap |
| Mix | ✅ |  |  |
| Low Cut, High Cut | ✅ |  |  |
| Filter Slope | ❌ |  | cheap |
| Input Select, Mode (stereo, parallel, series) | ❌ |  | cheap (with two slots) |
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
| Detector (RMS/peak) | ❌ |  | cheap |
| Mute per band (solo by muting two) | ❌ |  | cheap |
| Crossover Slope (12/24 dB) | ◐ | LR4 only | cheap |
| Knee Type | ✅ | Knee (dB) |  |
| Auto Attack/Release | ❌ |  | cheap |
| Level, Balance | ✅ / 🆕 |  |  |

### 6.19 Multiplexer (p. 56) → `mux`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Input Select: rows 1–6 | ✅ | Plus "All" |  |
| Input Select: inputs 1–4, USB 5/6 and 7/8 | ❌ | Needs multi-channel input | medium |
| Level, Balance | ✅ / 🆕 |  |  |
| Gapless switching | ✅ | 10 ms crossfade |  |

### 6.20 Parametric EQ (p. 63) → `peq`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Type: Shelving, Peaking | ✅ | Bands 1 and 5 shelf or peak; 2–4 peak |  |
| Type: Blocking (high-pass, low-pass) | 🆕 ✅ | Bands 1 and 5 |  |
| Type: Shelving 2, Peaking 2 | ❌ |  | cheap |
| Frequency, Q, Gain (±12 dB; ours ±24) | ✅ |  |  |
| Slope for blocking bands (6–36 dB/oct) | ◐ | 12 dB/oct | cheap |
| Solo | ❌ | Editor-side feature | cheap |

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
| LFO Reset on Bypass | ❌ | Needs an engage signal (§8 item 12, stream P1) | medium |
| Barberpole: Sweep Direction, Filter Q | 🆕 ✅ | Two chains of second-order allpasses (Q 0.3–10) sweeping the range on a sawtooth half a cycle apart, crossfaded by sin² windows that sum to one: a Shepard-style endless rise or fall. Coefficients on an 8-sample grid |  |
| Global Mix | ❌ | §3 (stream P1) | medium |

### 6.22 Pitch (pp. 66–76) → `pitch`

Types: 16 (dual/quad detune, dual/quad chromatic and quad chromatic delay, dual/quad diatonic and quad diatonic delay, custom shift, arpeggiator, classic/advanced whammy, octave divider, crystal echoes, virtual capo) → Cathode 9 (Detune, Chromatic, Diatonic, Whammy, Octave Divider, Crystals, Arpeggiator, Virtual Capo, Custom Shifter) with four voices. The dual/quad and "delay" variants are voice counts, voice delays and 🆕 per-voice feedback. 🆕 The new parameters carry `typeLabels`, so the editor shows each only on the types it applies to.

| Page | Parameter | Cathode | Note | Effort |
|---|---|---|---|---|
| Config | Input Mode (mono, stereo) | 🆕 ✅ | Stereo: voices 1 and 3 shift the left input, 2 and 4 the right (two PSOLA analysis buffers); the single-voice types keep the sides apart. The detector always hears both |  |
| Config | LFO rate/tempo/depth on detune | 🆕 ✅ | A sine sways each voice's detune by up to ±50 cents, the voices half or a quarter cycle apart |  |
| Master | Master Level, Pan, Feedback, Pitch, Delay | 🆕 ✅ | Scale every voice's level (dB), pan, feedback, shift (Detune and Chromatic) and delay |  |
| Detector | Source (block, input 1–4) | 🆕 ✅ | Detector Source: the block input or grid row 1–6. The block takes its inputs by row (`IRowInputBlock`); the chosen row feeds only the tracker. Hardware inputs 1–4 are not grid rows here | — |
| Detector | Pitch tracking (fast, smooth, off) | 🆕 ✅ | Smooth (0.15-semitone hysteresis, the old behaviour), Fast (0.05, shorter hold), Off (free splices, harmony holds) |  |
| Detector | Tracking (splice length) | 🆕 ✅ | Splice Length 8–40 ms (the shifters' minimum splice) |  |
| Detector | Temperament (equal, just) | 🆕 ✅ | Just: five-limit ratios on the key for Diatonic and Custom Shifter (`Scales.JustRatio`) |  |
| Detune | Detune 1–4 | 🆕 ✅ | Four voices, each ±50 cents, own shifter; silent voices cost nothing |  |
| Detune | Delay 1–4 | ✅ | Voice delays |  |
| Detune | Feedback mode, Feedback 1–4 | 🆕 ✅ | ±90 %. Delay mode repeats a voice through its own delay line; Shifter mode feeds it back into its shifter (the harmony voices share one input, their sum capped at 95 %) so each repeat shifts again |  |
| Chromatic | Shift 1–4, Level 1–4, Pan 1–4, Delay 1–4 | ✅ |  |  |
| Chromatic | Delay Tempo 1–4 | 🆕 ✅ | A tempo division replaces the voice's delay (up to 1 s) |  |
| Diatonic | Key, Scale, Harmony 1–4 | ✅ | Plus key from MIDI or auto |  |
| Diatonic | Learn | ◐ | Key Source "Auto" | — |
| Diatonic | Glide Time | 🆕 ✅ | 0–1000 ms, in log frequency |  |
| Diatonic | Custom scale (notes, tonic) | ◐ | Custom Shifter tables (12 per voice) | — |
| Arpeggiator | Run, Steps, Tempo, Stage shifts | 🆕 ✅ | 16 steps |  |
| Arpeggiator | Repeats, Amplitude/Pan shape and alpha | 🆕 ✅ | Repeats infinite or 1–8 (restarting on each new note, the playing rising through −40 dBFS); amplitude Flat, Decay, Swell, Triangle with alpha as the curve; pan Center, Alternate, Ramp, Random with alpha as the width |  |
| Whammy | Mode (classic), Start/Stop shift, Control | ✅ | Start, End, Pedal |  |
| Whammy | Tracking mode (mono, poly, off) | 🆕 ✅ | Mono (period-synchronous splices), Off (free splices) and Poly: a peak-locked phase vocoder (`PhaseVocoderShifter`, 2048-point frames at 48 kHz, 43 ms wet delay) that shifts chords cleanly; transients smear by up to a frame |  |
| Crystals | Feedback type, splice time/tempo, direction, diffusion | 🆕 ✅ | Shifted or unshifted feedback, splice 20–200 ms or a tempo division, reverse or forward grains, a four-allpass diffuser in the loop |  |
| Custom Shifter | Scale 1/2, Key | ✅ |  |  |
| Virtual Capo | Shift, Detune | 🆕 ✅ | Capo Detune ±50 cents |  |
| Mix | Input Gain | 🆕 ✅ | The level into the shifters (the dry and the detector are unaffected) |  |
| Mix | Low Cut, High Cut (wet) | 🆕 ✅ | `WetCuts` |  |
| Mix | Global Mix | ❌ | §3 (stream P1) | medium |

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
| Master Level, Pan, Feedback, Q | 🆕 ✅ | Master Level (dB), Master Pan and Master Feedback (scale every voice), Master Q (scales the band-passes); Decay and Damping remain the loop's design |  |
| Input Mode (mono, stereo) | 🆕 ✅ | Stereo runs a second set of combs: left rings left, right rings right, pans become balances |  |
| Frequency 1–4 | ✅ |  |  |
| Feedback 1–4 | 🆕 ✅ | Scale each voice's loop gain (100 % = what the decay asks for) |  |
| Filter Location 1–4, Filter Q 1–4 (band-pass) | 🆕 ✅ | Off, Pre (only the input near the pitch excites the comb) or Post (the comb rings with its fundamental); Q 0.5–20 |  |
| Level 1–4, Pan 1–4 | ✅ |  |  |
| Input Gain | ✅ |  |  |
| Global Mix | ❌ | §3 (stream P1) | medium |

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
| Low Rate Multiplier | ◐ | Separate drum rates | — |
| Low/Hi Time Constant | ✅ | Accel and decel for each |  |
| LF and HF Mic Spacing | 🆕 ✅ | Separate angles for the drum and horn mics |  |
| Mic Distance | 🆕 ✅ | 100 % is the far field (the old model); closer uses the exact path √(k² + 1 − 2k cos θ) and 1/r level, so the near pass is louder and the sweep lopsided |  |
| Stereo Spread | 🆕 ✅ | −100…100 % on the output, besides the mic spacing |  |
| Input Select | 🆕 ✅ | L+R, Left, Right |  |

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
| Global Mix | ❌ | §3 (stream P1) | medium |

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

Types: Tremolo, Panner, Bias Trem, Harmonic Trem, Optical Trem → all five.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Rate, Tempo | ✅ |  |  |
| Depth / Width (beyond 100 % for the panner) | 🆕 ✅ | 0–200 %; named Width on the Panner, where past 100 % the swing dwells at the sides; the tremolo types stop at 100 % |  |
| LFO Type, LFO Duty Cycle | ✅ |  |  |
| LFO Phase | ✅ |  |  |
| Start Phase | ❌ | Needs an engage signal (§8 item 12, stream P1) | medium |
| Trigger Threshold (restart on playing) | 🆕 ✅ | Trigger Level (−80 dB = off … 0 dB): the LFO restarts when the playing rises through it, re-arming 6 dB below |  |
| Crossover Frequency (harmonic) | ✅ | One-pole split |  |
| Crossover Slope | 🆕 ✅ | 6 dB/oct (complementary one-pole, the default), 12 and 24 dB/oct (Linkwitz–Riley, summing to an allpass) |  |
| Pan Center | ✅ |  |  |
| Level, Balance | ✅ |  |  |

### 6.35 Vocoder (p. 96) → `vocoder`

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Analysis Source (an input) | 🆕 ✅ | Block Input or grid row 1–6: the block takes its inputs by row (`IRowInputBlock`), the chosen row feeding only the analysis and the others summed as the block input (where an input carrier comes from) | — |
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

Types: FAS Wah, Clyde, Cry Babe, VX846, Color-Tone, Funk Wah, Mortal, VX845, Paragon → Cathode Classic, Vox, Wide, 🆕 Clyde, Colorsound, Morley, V845, Parapedal (filter voicings) and Cry Baby, V846 (component-level circuits). The five new voicings follow the pedals' known character (range and how vocal the peak is), not measurements; component-level circuits for them remain **medium** each.

| Parameter | Cathode | Note | Effort |
|---|---|---|---|
| Wah Control | ✅ | Position |  |
| Control Taper | ✅ | Linear, log, reverse log |  |
| Resonance Q | 🆕 ✅ | Filter types scale their Q; 🆕 the circuits scale R7, the resistor that damps the resonator |  |
| Q Tracking | 🆕 ✅ | Less peak toward the toe; 🆕 on the circuits too, through R7 |  |
| Low Cut Frequency | ✅ |  |  |
| Minimum / Maximum Frequency | 🆕 ◐ | 50–200 % of the filter voicing's heel and toe frequencies; the circuits' range is set by their parts (Inductor Bias moves it) | — |
| Drive | 🆕 ✅ | Filter types saturate ahead of the filter; circuits get up to 18 dB more input (and as much less output), so their transistors clip |  |
| Inductor Bias | 🆕 ✅ | DC through the winding lowers the inductance (to 40 % at full bias), raising the sweep by up to 1.6×: L1 on the circuits (`WahCircuit.Trim`), the matching shift on the filter types. The core's saturation curve is not modelled |  |
| Fat (dry blend up to 50/50) | ✅ | Mix |  |
| EQ page (8-band graphic) | 🆕 ✅ | Octave bands 63 Hz–8 kHz, ±12 dB; flat bands cost nothing |  |

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
| Graphic EQ modes | `geq` | 10-band only; band layouts and passive types missing (§6.12) | medium |
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
9. **Graphic EQ layouts** (medium). Type with 10 (2/3 oct), 8, 7 and 5 bands using `typeLabels` to rename and hide bands, shelving outer bands, then the passive types (Mark 5-band, 4-band, 3-band, console). Keep the global output EQ on the plain 10-band type.
10. **Compressor and Gate sidechain input** (medium). A sidechain source (row or input) needs a second input into a block: the same `IRowInputBlock` routing can deliver a chosen row into a side buffer.
11. **Look-ahead compressor** (medium). The compiler reads latency at compile time; a parameter-dependent latency needs a recompile on change or a fixed maximum look-ahead always reported.
12. **"On engage" behaviour** (medium). LFO Bypass Reset (Flanger, Phaser) and Tremolo Start Phase need the engine to tell a block it was engaged; add an optional `IEngageAware` the compiled graph calls when a bypass fade starts.
13. 🆕 **Done** (stream P3): Phaser VCR models and Barberpole; Flanger thru-zero; Chorus Dimension.
14. 🆕 **Done** (stream P3): Pitch completion, including polyphonic Whammy tracking (a peak-locked phase vocoder). Left: formant correction, and Detector Source on the hardware inputs (rows only).
15. **Drive EQ pages and expert controls** (medium). The EQ 1 page (low/high cut, bass/mid/high-mid/treble) and EQ 2 (the GEQ bands pre/post) are generic; diode type/count and bias need per-circuit WDF parameters. New circuits continue as in Phase 6 (medium each).
16. **Looper as a block** (medium), with its Config-page features (cheap): once, trims, threshold start, overdub level, crossfade, quantise, 300 s.
17. 🆕 Ring Modulator SSB and pitch tracking, Synth third voice and tracking modes, Vocoder band controls: **done** (stream P3). **IR Player second slot; Cab preamp, air and input mode** (cheap to medium each).
18. **Amp gaps** follow [06 §5](06-amp-modelling-spec.md#5-recommended-plan): input boost (reuse Drive circuits), saturation stage, output EQ types and location, output compressor and input dynamics, speaker drive and compression.
