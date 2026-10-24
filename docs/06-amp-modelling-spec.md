# 06 — Circuit amp modelling spec

This is the research spec for [Phase 7](04-implementation-plan.md#phase-7-circuit-amp-modelling-research-track-starts-during-p2-612-months) now that we have no physical amps. It replaces the R1 measurement rig with data found on the internet: published schematics, analytic references, and NAM captures of real amps made by other people. It covers:

1. Parity with the Axe-Fx III Amp block's parameters, and what Cathode should do with each one.
2. The amps to model, in two tiers.
3. Where each amp's circuit is published, and the component values we could verify.
4. Reference data for matching without hardware, and what the licences allow.
5. How to split the work into parallel streams, the stage-library work that has to come first, the CPU budget, and the matching method.

**How sure we are.** Values marked **[src]** were read from the cited page as text or from a factory drawing. Values marked **[2-src]** agree in two independent sources. Values marked *(unverified)* are commonly quoted, come from hobbyist redraws, or come from memory, and must be checked against a factory schematic before they go into an `AmpDesign`. The Fractal wiki (wiki.fractalaudio.com) answered 403 to every automated request, so the parameter list below comes from the official *Blocks Guide* and the firmware release notes, and most numeric ranges are *(unverified)*.

**What exists today** (`Cathode.Dsp.Amp`, `CircuitAmpBlock`): three models (Blackface 2 × 6L6GC, Plexi 2 × EL34, AC30 4 × EL84), and 14 parameters: Type, Gain, Bass, Middle, Treble, Presence, Depth, Cut, Master, Sag, Bias, Feedback, Bright, Level. Tubes available: 12AX7, 12AT7, 12AU7, EL34, 6L6GC, EL84. Everything runs at 2 × the sample rate. Cost is 8–11 % of one core on guitar and 19.8 % worst case.

---

## 1. Axe-Fx III Amp block parameter parity

### 1.1 Sources and status

| Source | What it gave |
|---|---|
| *Fractal Audio Blocks Guide* (June 2023, Cygnus X-2 era), Amp block section, pp. 9–18: https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf | Parameter names, pages and meanings. This is the primary source. It gives almost no ranges. |
| Firmware release notes: [1.16](https://forum.fractalaudio.com/goto/post?id=1696203), [5.00](https://forum.fractalaudio.com/goto/post?id=1765574), [16 beta](https://forum.fractalaudio.com/goto/post?id=2051871), [20.01](https://forum.fractalaudio.com/goto/post?id=2281582), [23.00](https://forum.fractalaudio.com/goto/post?id=2482901), [25.00](https://forum.fractalaudio.com/goto/post?id=2542380), [27.00](https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-27-00-release.207928), [29.00](https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-29-00-release.214479/), [31.01](https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-31-01-release.217475/), [32.00](https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-32-00-public-beta.219113/), [33.00 b2](https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-33-00-public-beta-beta-2.222956/) | Additions and removals. Firmware 25 to 33 added **no** new Amp parameters; they changed algorithms and many model defaults. |
| wiki.fractalaudio.com | Not readable (HTTP 403, bot check). Ranges below marked *(unverified)* should be checked against Axe-Edit or the wiki by hand. |

**Default semantics.** On the Axe-Fx III almost every parameter's default is *the amp model's own value*, loaded when the Type changes (or on a reset). Cathode should copy this: each `AmpDesign` carries its stock values, and an "expert" parameter is stored as an offset or multiplier from stock, so 0 (or 1×, or 50 %) always means "as the schematic". The existing Bias, Sag and Feedback parameters already work this way.

### 1.2 Recommendation codes

| Code | Meaning |
|---|---|
| **K** | Front-panel knob, generic across all models (the model may hide it if the real amp lacks it). |
| **E** | Expert parameter: a per-model fixed value from the schematic, exposed as an override that defaults to stock. |
| **M** | Model data only: lives in `AmpDesign`, not exposed (or only in a "designer" view later). |
| **D** | Generic DSP outside the circuit (EQ, compressor, boost): implement once, reuse existing blocks' code. |
| **S** | Skip, with the reason given. |
| **✓** | Already covered by the current Circuit Amp block. |

### 1.3 Parameters by page

**Type and Tone page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Amp Type | enum (300+ models) | — | Selects the circuit and loads its defaults | **✓** Type |
| Gain (Input Drive) | 0–10 | model | Preamp volume/gain pot at its real place in the circuit, with its taper and the bright-cap "treble peaker" | **✓** Gain |
| Bass / Mid / Treble | 0–10 | model | Passive tone stack, exact | **✓** Bass, Middle, Treble |
| Master Volume | 0–10 | model; 10 on amps without one | Power-amp drive (pre-PI master) | **✓** Master |
| Presence | 0–10 | model | High-frequency shaping of the negative feedback | **✓** Presence |
| Depth | 0–10 | model | Low-frequency shaping of the feedback (resonance) | **✓** Depth |
| Hi-Cut (Cut) | 0–10 | model | Vox-style cut across the PI plates; every model has it since Cygnus | **✓** Cut |
| Bright Switch | off/on | model | Bright cap across the gain pot | **✓** Bright |
| Input Trim | about 0.1–10 × *(unverified)* | 1.0 | Input gain with no tone change | **K** cheap, useful for matching pickup level |
| Overdrive / Lead Master / Ratio | 0–10 | model, some models only | A model's extra gain control (e.g. Mark lead drive, JCM800 2203's preamp vs master) | **K** model-specific second gain pot |
| Presence Shift | off/on | model, some models | Mark-series pull-shift | **E** model-specific switch |
| Model-specific knobs (Authentic mode) | varies | model | E.g. Plexi channel jumpering, Fat, Contour | **K** per model, as switches/pots in `AmpDesign` |

**Preamp page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Input Boost / Boost Type / Boost Level | on/off; pedal voicings; dB (0–20 *unverified*) | off | A built-in boost pedal | 🆕 **✓** `boost` (six Drive circuits at the amp's 2x rate), `boostdrive`, `boosttone`, `boostlevel` (docs/08 §4) |
| Saturation Switch / Saturation Drive | OFF / ON AUTH / ON IDEAL; 0–10 | model | The common "saturation" mod between preamp and stack (adds a clipping stage) | 🆕 **✓** `saturation` and `satdrive`: a diode-style clipper between preamp and power amp (generic, not each model's own mod) |
| Fat Switch | off/on | off | Moves the tone stack's centre frequency down | **E** (scale the stack's mid cap; Friedman has a real Fat switch) |
| Cut Switch | off/on | off | Low cut at the input | **E** (a smaller input coupling cap) |
| Preamp Tube Type | enum (12AX7 variants, EF86, …) | model | Triode characteristics | **E** (12AX7, 12AY7, 5751, 12AT7, 12AU7) |
| Bright Cap | pF | model | Bright-cap value | **E** (we have only on/off; make the value overridable) |
| High Treble | 0–10 | model | Extra treble control after the preamp | **S** use the EQ |

**Input EQ and Output EQ pages**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Input EQ filters (Type/Freq/Q/Gain) | — | flat | Parametric EQ before the amp | 🆕 **✓** `infilter` (shelves, peak, high/low-pass), `inffreq`, `infq`, `infgain` |
| Low Cut | Hz (about 10–2000 *unverified*) | model | Input high-pass ("tightening") | **D** one cheap first-order HPF before V1 |
| High Cut | Hz | model | Low-pass between preamp and power amp; deprecated in fw 33.00 b2 | **S** (deprecated upstream) |
| Definition | bipolar tilt | 0 | Tilt EQ at the input | 🆕 **✓** `definition`: a treble/bass tilt around 650 Hz into the preamp, ±4 dB |
| Graphic EQ bands | dB (±12 *unverified*) | 0 | Built-in graphic EQ | **D** later; the Mark 5-band EQ must be **M** (it is part of the circuit) |
| EQ Type / EQ Location / EQ On | enum; INPUT, PRE PA, OUTPUT | OUTPUT | Number of bands; where the EQ sits | 🆕 **✓** `outeq` (every Graphic EQ layout), `outeqloc` (input, pre power amp, output), `outeq1`–`outeq10` |

**Power Supply page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Supply Sag | 0–10 | model | Supply source impedance, i.e. B+ droop | **✓** Sag |
| B+ Time Constant | ms | model | Reservoir RC speed | **E** (scale reservoir C; already in `PowerSupplyDesign`) |
| Supply Type | AC / DC | model | Rectified AC with ripple, or ideal DC | **E** with rectifier ripple (not modelled yet) |
| AC Line Frequency | 50 / 60 Hz | 60 *(unverified)* | Ripple frequency | **✓** Supply Type AC 60 Hz / AC 50 Hz |
| Variac | % of line (about 50–150 *unverified*) | 100 % | Mains voltage; level-compensated | **E** cheap: scales every supply node; the "brown sound" |
| Screen Frequency / Screen Q | Hz / Q | model | Resonance of the screen supply's filter | **M** (falls out of the supply network) |

**Power Amp page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Power Amp Modeling | off/on | on | Bypass the power amp for use with a real tube power amp | **K** (preamp-only output; also the "R5 hybrid" building block) |
| Power Tube Type | 6AQ5, 6CA7, 6L6GB, 6L6GC, 6V6, 300B, 5881, 6550, 6973, EL34, EL84, KT66, KT77, KT88, Transistor (fw 32) | model | Tube curves | **E** (add 6V6, 5881, KT66, KT88, 6550, KT77, 6146B as needed by the models) |
| Power Tube Grid Bias | % | model | Bias point, class B to class A | **✓** Bias |
| Negative Feedback | 0–10 | model | Global feedback amount | **✓** Feedback |
| Power Tube Hardness | 0–10 | model | Knee sharpness | **S** our tubes come from Koren curves; a "hardness" knob has no circuit meaning. Revisit if matching needs it |
| Transformer Matching | ratio (about 0.5–2 *unverified*) | model | OT impedance ratio vs stock | **E** (scales `PlateToPlate`) |
| Speaker Impedance | × nominal | 1.0 | Load mismatch (4 Ω speaker on the 8 Ω tap) | **E** (scales the speaker load) |
| Bias Trem Frequency / Depth | Hz / % | depth 0 | Real bias tremolo | 🆕 **✓** `tremrate`, `tremdepth`: a sine on the Bias control (64-frame steps) |

**Speaker page (impedance load and speaker nonlinearity)**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| LF Resonance Frequency | Hz (real speakers 50–150) | model | Bass impedance peak | **E** (exists as `SpeakerDesign.Resonance`) |
| LF Resonance Q | Q | model | Peak sharpness | **E** (`SpeakerDesign.Q`) |
| LF Resonance (amount) | dB | model | Peak height | **E** (`PeakImpedance`) |
| HF Resonance Frequency / amount | Hz (about 1–10 k) | model | Voice-coil inductance against transformer capacitance | **E** |
| HF Slope | 3.0–4.5 typical, about 3.7 median | model | Voice-coil semi-inductance (eddy losses) | **E** (we have a "lossy voice coil": expose its exponent) |
| Cabinet Resonance | % | model | Scales the cabinet part of the impedance curve | 🆕 ◐ `cabres`, the frequency of the Thump boost |
| Speaker Impedance Curve | enum (82+ measured curves) | model | Measured load curves | **M** a small set of parametric presets (open-back 1×12, closed 4×12, 2×12 combo, bass 8×10). Do not copy Fractal's measured curves |
| Auto Dyna-Cab Impedance | off/on | — | Curve follows the Cab block | **S** (couples blocks; later) |
| Speaker Drive | 0–10 | model | Cone distortion | 🆕 **✓** `spkdrive`: soft saturation of the band below 150 Hz at the amp's output |
| Speaker Thump | 0–10 | 2.5 on reset | Subsonic cone nonlinearity | 🆕 **✓** `thump`: up to +12 dB at `cabres` (50–250 Hz) |
| Speaker Compression / Speaker Time Constant | dB / ms | about 3 dB | Voice-coil thermal compression | 🆕 **✓** `spkcomp`, `spkcomptc`: voice-coil heating, an RMS power detector with its time constant |
| Speaker Compliance | % | 50 % | Suspension nonlinearity | **S** Cab block |
| Transformer LF / HF | Hz | model | OT bandwidth (primary inductance; leakage and capacitance) | **E** (scale magnetising and leakage inductance in `OutputTransformerDesign`) |
| Transformer Drive | 0–10 | model | Core saturation | **E** after transformer saturation exists (not modelled yet) |

**Dynamics page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Input Dynamics | bipolar | 0 | Input compander | 🆕 **✓** `indyn`: expands or compresses the input by its envelope around −20 dBV, within ±12 dB |
| Output Compression / Type / Threshold / Clarity | — | 0 | Output compressor (Output, Feedback, Gain Enhancer) | 🆕 **✓** `comptype` (Output, Feedback), `compamount`, `compthresh`, `compclarity` (detector high-pass) |
| Master Bias Excursion | % | 100 % mostly | Scales all bias-excursion effects | **S** in a circuit model, bias excursion is a consequence of the coupling caps and grid current, not a knob |

**Advanced page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Input Select | L / R / L+R | L+R | Mono input source | **K** trivial (block now takes Left) |
| Output Mode | FRFR / SS power amp + cab | FRFR | Leaves speaker compression to a real cab | **S** (we do no speaker compression in the amp) |
| Tonestack Type | enum (any model's stack, ACTIVE) | model | Swap stacks | **E** later; easy since stacks are data (`ToneStackDesign`) |
| Tonestack Frequency | Hz | model | Stack centre frequency | **E** (scale stack caps) |
| Tonestack Location | PRE / MID / POST / END | model | Where the stack sits | **M** (fixed by the circuit; END has no circuit meaning) |
| Preamp Sag | off/on | model | Preamp sees B+ sag (integrated head) | **E** (preamp node already exists in the supply) |
| Preamp Tube Hardness | 0–10 | model | Triode saturation sharpness | **S** (as Power Tube Hardness) |
| Triode 1 / Triode 2 Plate Freq | Hz | model | Corner of a cap across the plate resistor of the last two triodes | **M** (a plate-bypass cap in `TriodeStageDesign`, e.g. 1 nF on the SLO cold clipper) |
| Preamp Bias | bipolar | model | Bias of the last triode (even harmonics) | **E** (scale the cathode resistor of the last gain stage) |
| Preamp Bias Excursion | % | model | Grid-current bias shift ("blocking") | **S** emerges from the circuit (grid current through coupling caps) |
| CF Compression / CF Harmonics | 0–10 | model | Cathode follower behaviour | **S** emerges from the CF model once grid current into the CF is in |
| Master Vol Trim | × | 1.0 | Scales the master's range | **E** |
| Master Vol Cap | pF | model | Bright cap on the master pot | **M** |
| Master Vol Location | PRE PI / POST PI / PRE-TRIODE | model | Where the master sits | **M** (circuit fact per amp) |
| Presence Freq / Depth Freq | × | model | Shift presence and depth frequencies | **E** (scale presence and depth caps) |
| P.A. Cathode Resistance / Time Const | Ω / ms | model | Cathode-bias resistor and bypass on cathode-biased amps | **✓** partly, through Bias (cathode amps scale the resistor). Time constant **E** (scale bypass C) |
| PI Bias Excursion | % | model | PI bias shift; since fw 32 also PI distortion | **S** emerges once grid current into the PI is modelled |
| Power Tube Bias Excursion / Excursion Time / Recovery Time | % / ms | model | Coupling-cap charging when the power grids conduct | **S** already emerges (grid networks and grid conduction are in `PowerAmp`) |
| Dynamic Damping | — | model | Plate impedance | **S** emerges from the tube model |
| Plate Suppressor Diodes | off/on | model | Flyback diodes | **E** later (cheap clamp on the primary) |
| Level / Balance / Bypass | dB / L-R / bool | 0 | Common | **✓** Level, 🆕 Balance; bypass is generic |

### 1.4 Parity summary

- About **90 distinct Axe-Fx III Amp parameters** once duplicates across pages are removed (the boost, the EQ bands and the compressor counted as a few each).
- **Covered now: 13** (Type, Gain, Bass, Mid, Treble, Master, Presence, Depth, Cut, Bright, Supply Sag, Grid Bias, Negative Feedback) plus Level.
- **Recommended additions:** about 25 expert parameters (**E**), of which the most valuable first are Preamp Tube Type, Power Tube Type, Bright Cap value, Transformer Matching, Speaker Impedance, the three LF-resonance values, Transformer LF/HF, Presence/Depth Freq, Variac, B+ Time Constant, Preamp Sag, Preamp Bias, Master Vol Trim, Tonestack Frequency, and a Power Amp Modeling off switch. Add model-specific panel controls (**K**): second gain pot (lead drive), channel jumpering, Fat, Contour, Presence Shift.
- **Generic DSP (D):** input Low Cut, Definition, and later a graphic EQ with a PRE PA location.
- **Skip (S), about 30:** the "emergent" parameters (bias excursions, CF compression and harmonics, hardness, damping), because in a circuit model they are consequences of component values, not knobs; and the speaker-cone and compressor parameters, which belong to the Cab and Compressor blocks. This is a deliberate difference: Cathode's expert parameters are component values, Fractal's are partly behavioural.

**Gaps that block parity, in order:** transformer saturation (Transformer Drive), rectifier ripple (Supply Type), grid current into the phase inverter and cathode followers (needed for the "emergent" parameters to emerge at all), tube types beyond the six we have, and a pre-power-amp EQ slot for the Mark series.

---

## 2. Amps to model

Chosen from the amps Fractal models (the Axe-Fx III list is built on the same real amps), from how often each appears in forum presets and in NAM capture libraries, and from how well each one is documented. Fractal model names are given only as a cross-reference and are *(unverified)*: the wiki "Amp models list" could not be read.

**Tier 1: must have (22).** These carry most of the use, and most of them are well documented.

| # | Real amp | Family | Power section | Why | Data quality |
|---|---|---|---|---|---|
| 1 | Fender Deluxe Reverb AB763 | Fender BF | 2 × 6V6, GZ34 | The reference clean amp | Good |
| 2 | Fender Twin Reverb AB763 | Fender BF | 4 × 6L6, SS rectifier | Loud clean; pedal platform | Good |
| 3 | Fender Super Reverb AB763 | Fender BF | 2 × 6L6, GZ34 | Nearly free given #1/#2; best capture set found | Good |
| 4 | Fender Princeton Reverb AA1164/AB1270 | Fender BF | 2 × 6V6, cathodyne PI | Studio clean/edge | Good |
| 5 | Fender Bassman 5F6-A | Fender tweed | 2 × 5881, GZ34 | Ancestor of the Marshall; analytic references exist | Very good |
| 6 | Fender Tweed Deluxe 5E3 | Fender tweed | 2 × 6V6 cathode bias, 5Y3, no NFB | Classic breakup; simple | Good |
| 7 | Marshall JTM45 | Marshall | 2 × KT66, GZ34 | Bluesbreaker tone | Good |
| 8 | Marshall 1959 Super Lead (Plexi) | Marshall | 4 × EL34, SS | **Exists** (as 50 W); extend to 100 W and jumpering | Good |
| 9 | Marshall JCM800 2203 / 2204 | Marshall | 4 / 2 × EL34, SS | The rock amp | Very good |
| 10 | Marshall Silver Jubilee 2555 | Marshall | 4 × EL34 | Diode-clipping lead; non-standard stack | Good (factory drawing) |
| 11 | Vox AC30 Top Boost | Vox | 4 × EL84 cathode bias, no NFB | **Exists** | Medium (stack values unverified) |
| 12 | Vox AC15 (EF86) | Vox | 2 × EL84 cathode bias | EF86 pentode preamp | Good |
| 13 | Matchless DC-30 | Class A boutique | 4 × EL84, GZ34 | EF86 + top-boost channels | Medium |
| 14 | Mesa/Boogie Mark IIC+ | Mesa | 2 or 4 × 6L6 (Simul: + EL34) | The metal/lead classic | Medium–good |
| 15 | Mesa/Boogie Dual Rectifier | Mesa | 4 × 6L6, 5U4 or SS | Modern high gain | Medium |
| 16 | Soldano SLO-100 | US high gain | 4 × 6L6GC, SS | Template for modern high gain | Good |
| 17 | Peavey 5150 / 6505 | US high gain | 4 × 6L6 | Metal standard | Medium |
| 18 | EVH 5150III | US high gain | 4 × 6L6GC | Modern metal standard | Medium (redraw only) |
| 19 | Friedman BE-100 | Marshall-derived | 4 × EL34, SS | Most-used modern hot-rodded Marshall | Medium (trace) |
| 20 | Bogner Ecstasy (red/blue) | US high gain | 4 × EL34 | Popular, versatile | Low–medium |
| 21 | Diezel VH4 (ch 3) | EU high gain | 4 × EL34 | Modern classic | Low–medium |
| 22 | Dumble Overdrive Special | Boutique | 2 × 6L6 *(unverified)* | Very popular style ("ODS", Two-Rock) | Low (forum traces with known errors) |

**Tier 2: should have (22).**

| Real amp | Family | Note |
|---|---|---|
| Fender Hot Rod Deluxe | Fender modern | Popular, but the schematic is a scan only; tone stack unknown |
| Marshall JCM900 4100 | Marshall | Op-amp and diode front end (hybrid); good GPL capture set |
| Marshall JVM410H (OD) | Marshall | Factory PDF exists but values are unreadable |
| Mesa Mark IV (lead) | Mesa | Shares the IIC+ front end |
| Mesa Lone Star | Mesa | Clean/edge Mesa |
| Bogner Uberschall | EU/US high gain | No schematic found |
| Diezel Herbert | EU high gain | No schematic found |
| Engl Powerball | EU high gain | No schematic found |
| Engl Savage 120 | EU high gain | No schematic found |
| Orange Rockerverb / OR50 | British | MkI schematic only |
| Hiwatt DR103 | British | Fixed-bias PI driven by a CF; partial data |
| Dr Z Maz 18 | Class A boutique | No schematic found |
| Bad Cat Hot Cat / Black Cat | Class A boutique | Power amp data only |
| Two-Rock (Dumble-style) | Boutique | Use the ODS work |
| Carvin Legacy | US high gain | Legacy V3 redraw only |
| Splawn Quickrod | Marshall-derived | No schematic found |
| Suhr Badger / SL68 | Marshall-derived | No schematic found |
| Revv Generator 120 | Modern high gain | No schematic found |
| Morgan (AC20 / SW) | Boutique | No schematic found |
| Laney GH100L | British | Factory drawing; JCM800-like with a cold clipper |
| Ampeg SVT | Bass | 6 × 6550 with CF drivers; complex, high B+ |
| Roland JC-120 | Solid state | Not a tube amp: needs a separate transistor/op-amp model; the chorus is a separate block |

Tier 2 amps with no schematic should wait: a "circuit" model built on guesses is not a circuit model. Where a capture exists but no schematic, the NAM capture tier already serves the user.

---

## 3. Per-amp circuit data

Abbreviations: "1.5k‖25µ" is a cathode resistor bypassed by that cap; "CF" is a cathode follower; "LTP" is a long-tail-pair phase inverter; "slope" is the tone stack's slope resistor; NFB is negative feedback.

Main schematic archives (all scanned images, so values have to be read by eye):
- el34world: https://el34world.com/charts/Schematics/files/ (Marshall, Fender, Mesa_boogie, Orange, Laney directories)
- thetubestore schematic library: https://www.thetubestore.com/lib/thetubestore/schematics/
- ampwares Fender index: http://www.ampwares.com/fender.asp
- schematicheaven: https://www.schematicheaven.net/

Analysis pages with values in text (the most reliable sources we can read automatically):
- Rob Robinette: https://robrobinette.com/
- AmpBooks (Richard Kuehnel): https://ampbooks.com/mobile/classic-circuits/

Hobbyist circuit redraws (LiveSPICE `.schx`, readable as XML, not factory documents, often round values; licences not checked, but component values are facts):
- https://github.com/Yahiake/LiveSPICE-Amp-Collection
- https://github.com/MARKTHERENCE/LiveSPICE-MK-Circuits

### 3.1 Fender

**Deluxe Reverb AB763** — https://robrobinette.com/How_The_AB763_Deluxe_Reverb_Works.htm, https://robrobinette.com/AB763_Model_Differences.htm

| Part | Values |
|---|---|
| Input | 1M grid leak; 34k stopper (2 × 68k) on Hi, 136k on Lo [src] |
| Stages | Vibrato channel 3 stages, normal 2 [src] |
| V1A/V2A | 100k plate, 1.5k‖25µ [src] |
| V1B/V2B | share one 820Ω‖25µ [src] |
| Tone stack | Fender TMB **after V1A**, before the volume: slope 100k, treble 250 pF, bass 0.1 µ, mid 0.047 µ, mid pot replaced by a fixed 6.8k [src]; treble 250k-A, bass 250k-A, volume 1M-A *(unverified)* |
| Bright | 47 pF on the vibrato channel's volume [src] |
| Coupling | normal 0.047 µ, vibrato 0.02 µ; 0.1 µ after V4B [src] |
| Mixer and reverb | 220k mixing resistors; reverb mix 3.3M‖10 pF; 470k; V4B 220k grid leak, 25 µ bypass [src]. Reverb driver 12AT7 paralleled 2.2k‖25µ *(unverified)* |
| PI | 12AT7 LTP: 82k/100k plates, 1M grid resistors, 470Ω, 22k tail [src, forum]; input cap 0.001 µ (combo) [src] |
| Power | 2 × 6V6GT fixed bias about −55 V; 220k grid leaks, 1.5k stoppers, 470Ω screens [src]; 22 W |
| Supply | 330-0-330 V, GZ34; nodes about 420 / 415 / 325 / 280 V; first filter 2 × 16 µ [src] |
| NFB | 820Ω from the 8Ω tap into 47Ω; no presence [src] |
| OT | 125A1A, 6.6k : 8Ω [src] |

**Twin Reverb AB763** — same preamp and PI as the Deluxe; mid pot on both channels (10k-A *unverified*), mid cap 0.047 µ; 4 × 6L6 fixed bias, 460 V plates, preamp 270 V; **solid-state rectifier**; NFB 820Ω/100Ω; OT 125A29A, **2k : 4Ω** [src: Model Differences page above].

**Super Reverb AB763** — mid control on the vibrato channel only, **mid cap 0.022 µ**; 2 × 6L6 fixed bias, 460 V; GZ34; NFB 820Ω/100Ω from a **2Ω** tap (about 41 % less feedback voltage); OT 125A9A, **4.2k : 2Ω** [src]. The current "Blackface" model is a Super Reverb-style channel without reverb and vibrato.

**Princeton Reverb AA1164 / AB1270** — https://robrobinette.com/AA1164_Princeton_Reverb.htm, https://fenderguru.com/amp-models/princeton-reverb/
- Tubes: V1 12AX7, V2 12AT7 reverb driver, V3 12AX7 recovery and mixer, V4 12AX7 tremolo and PI, 2 × 6V6 [src].
- Three fully bypassed preamp stages; 3.3M reverb mix [src].
- Stack: Fender, fixed 6.8k mid [src]; 250 pF / 0.1 µ / 0.047 µ *(unverified)*.
- **Cathodyne PI** on half a 12AX7: 56k plate, 56k cathode, 1k bias, 0.1 µ input [src, mod pages].
- No grid stoppers or screen resistors from the factory; tremolo modulates the power-tube bias [src].
- NFB 2.7k [src]. No choke; 20/20/20/20 µ can [src]. Rectifier 5U4GB (AA1164), GZ34 from AA764 [src]. 12–15 W. OT impedance not found.

**Bassman 5F6-A** — https://robrobinette.com/5F6A_Modifications.htm, https://ampbooks.com/mobile/classic-circuits/fender-bassman-5F6-A/, Yeh & Smith DAFx-06, Dunkel et al. DAFx-16 (§4.3)
- V1 **12AY7**: 100k plates, shared 820Ω‖250µ; bright channel 100 pF bright cap; coupling 0.02–0.022 µ [src].
- 270k mixing resistors [src]. V2A 12AX7: 100k, 820Ω **unbypassed** [src]. V2B CF DC-coupled, 100k cathode [src].
- Stack (Bassman TMB) after the CF: treble 250k, bass 1M, mid 25k, treble cap 250 pF, slope 56k, caps 0.02 µ on the schematic; Rob says production used 100k slope and 0.1 µ bass cap [src]. Pick one and record it.
- PI 12AX7 LTP: 82k/100k, 470Ω, 10k tail (later 6.8k), 0.02 µ outputs [src].
- 2 × 5881, fixed bias −48 V; 220k grid leaks, 470Ω screens [src]. GZ34, 325-0-325 V; B+ 442 V unloaded, 411 V loaded; 4.7k to the preamp [src].
- NFB 27k from the 2Ω tap; presence 5k-L, cap value not found [src]. OT about 4k : 2Ω [src].

**Tweed Deluxe 5E3** — https://robrobinette.com/How_The_5E3_Deluxe_Works.htm, https://ampbooks.com/mobile/classic-circuits/fender-deluxe-5e3/
- V1 12AY7: 100k, shared 820Ω‖25µ; 1M volumes [src]. V2A 12AX7: 100k, 1.5k‖25µ [src]. Coupling 0.1 µ [src].
- Tone: a single 1M pot with a 500 pF bright cap across the bright volume and 0.005 µ to ground [src]. The two volumes interact (channel-interaction is part of the sound).
- **Cathodyne PI** on V2B: 56k plate, 56k cathode, 1.5k bias, 220k grid leaks, 0.1 µ [src].
- 2 × 6V6 **cathode bias**, shared 250Ω, about 38 mA each; bypass cap value not in text; no screen resistors [src].
- 5Y3; B+ about 370 / 295 / 250 V [src]. **No NFB** [src]. OT 8k : 8Ω [src].

**Hot Rod Deluxe** (tier 2) — scans only: https://www.thetubestore.com/lib/thetubestore/schematics/Fender/Fender-Hot-Rod-Deluxe-Schematic.pdf, https://www.thetubestore.com/lib/thetubestore/schematics/Fender/Fender-Hotrod-Deluxe-III-Schematic-Rev-B.pdf. 3 × 12AX7, 2 × 6L6, about 40 W. NFB 47k with 4.7k from the 4Ω tap [src: https://www.mixonline.com/technology/ask-eddie-guitar-tone-part-2-370371]. PI, rectifier, B+ and stack *(unverified)*.

**As built (stream F1).** Eight types appended to `AmpDesign.All` (files in `Amp/Models`; each marks what is sourced and what is assumed): **Deluxe Reverb**, **Twin Reverb**, **Super Reverb** (AB763: Normal/Vibrato on Switch 1, the Vibrato channel's V4B behind the 3.3M ‖ 10 pF / 470k ‖ 220k reverb-mix divider, the idle channel loading the mix node, 1 nF bootstrapped into the 12AT7 LTP with grid current; Deluxe 47 pF fixed on Vibrato and fixed 6.8k mid, Twin/Super 120 pF bright switch, Super's mid pot on Vibrato only; 6V6/6L6 at 415/460 V, GZ34 or silicon; 820 Ω into 47/100 Ω from the 8/4/2 Ω tap; no master, presence or depth on the panel), **Princeton Reverb** (AA1164: three stages, cathodyne 56k/56k/1k, 2.7k feedback into 47 Ω under V3B, folded into the power amp's tap gain as V3B's gain of 55), **Bassman 5F6-A** and **Bassman 5F6-A Jumped** (12AY7 V1, 270k mixers, V2A unbypassed, DC-coupled CF into Yeh and Smith's stack values, 27k feedback into the 5k presence pot), **Tweed Deluxe 5E3** and **Tweed Deluxe 5E3 Jumped** (volumes wired input-to-wiper so the idle volume loads the played one, 500 pF bright, tone control reconstructed from descriptions, cathode-biased 6V6 on 250 Ω, 5Y3, no feedback). The single-input tweeds run the played V1 half as one triode and the idle half as its 21k plate impedance (about 9 % CPU); the jumped types hold both halves on their shared cathode exactly (about 13–14 %). The second stages carry the 12AX7's 1.7 pF grid-plate capacitance (Miller roll-off). CPU on guitar 8.3–9.6 % (jumped tweeds 12.9–13.9 %), loud noise 10.8–13.8 % (jumped 17.5–20.4 %), measured on a loaded machine.

Shared changes: `PowerAmp` now finds the supply's rest point with the screen node settled too (the loop used to stop after one pass with the screen node computed from currents at B+, so amps with a large dropping resistor reset off their operating point and thumped; the class-A goldens moved by −75 to −80 dB and were regenerated) and calibrates B+ with the screen node below it. The harness aligns to a fraction of a sample (half a sample alone costs a bright amp ESRpre 0.78).

Matching: the gain staging was checked stage by stage against Robinette's AB763 figures (V1A 43×, V1B 57×, V4B 33× with pots at half resistance) and AmpBooks' 5F6-A and 5E3 figures; the input convention, plate loads, bypasses, tapers, B+ and output scaling hold, and no library cause for "circuit amps distort more" was found. The Twin captures' bright, low-volume response is fitted to 0.8–1.1 dB RMS with the knobs, but their low-level second harmonic is about 18 dB under a 12AX7 first stage at any calibration that also matches the drive, so the calibration fit goes to its −20 dBu edge; the captures' reamp level and knob settings are unknown. Achieved (ESRpre / ESR / MRSTFT): `pel-twin-normal-bright` 0.365 / 0.123 / 0.871, `pel-twin-vibrato-bright` 0.599 / 0.238 / 0.899, `tl-princeton-clean` 1.12 / 0.50 / 1.56 (a cab or mic is in that capture). The 0.05 target is not met by any Fender reference; the Super Reverb T3K set (known knobs) is the one to close it with.

**As built (stream F5, Fender and US vintage).** Twelve types appended to `AmpDesign.All`, each from a factory drawing read by eye (sources and every unverified value in the model files): **Hot Rod Deluxe** (the III, drawing 0079604000 rev B: both input networks on V1A with the relay on Switch 1 "Normal Ch", the Drive channel the default; the 130k-slope stack with 220k on the bass pot; More Drive's J111 bypasses on Switch 2 and the Normal channel's bright bypass, each gated by the channel; V2B and the 30 % audio Master on the Drive channel; the effects loop as a wire, 47k / 4.7k feedback from the 4 Ω tap, 4.25k : 4 Ω), **Champ 5F1** and **Princeton 5F2-A** (single-ended 6V6, 22k feedback into V1B's cathode with V1B's cathode gain of 25 folded into the tap gain; the 5F2-A's tone network), **Princeton AA964** (V1B inside the 2.7k / 47 Ω loop, folded as on the Princeton Reverb), **Vibroverb AB763** and **Vibrolux Reverb AA964** (the AB763 reverb mixer; 47 Ω or 100 Ω), **Bandmaster AB763** and **Tremolux AA763** (no reverb: `Ab763.VibratoMixer`, 500 pF inverter input; the Tremolux's 100k/100k, 27k inverter), **Tweed Twin 5F8-A** and **Jumped** (the Bassman's input stages at 295 V, a follower with the mid cap on the mid pot's wiper, 4 × 5881, 2.1k : 4 Ω), **Vibro-King** (rev F: input cathode follower, Fat on Switch 1, 150 pF stack, no feedback) and **Blues Junior** (III: Fat, the mid cap on the wiper, 50k master, EL84 fixed bias, fixed presence shelf). The Hot Rod Deluxe's and Blues Junior's stage outputs are checked against the AC test points on Fender's drawings (Blues Junior within 3.1 dB; Hot Rod Deluxe within 6 dB, the drawing's V1B gain being lower than its stack loss implies). No GPL direct-out capture of any of these amps exists (GuitarML's Blues Junior files are miked; pelennor has none), so all ship schematic-only. CPU (guitar / loud noise, on a machine with a load average of 30–40, where the Deluxe Reverb measured 9.6 / 14.0): Hot Rod Deluxe 12.2 / 18.7, Champ 6.8 / 9.4, Vibroverb 9.6 / 13.4, Vibrolux Reverb 10.0 / 13.9, Bandmaster 7.6 / 11.0, Tweed Twin 9.5 / 17.3 (jumped 13.8 / 24.4), Tremolux 8.2 / 12.2, Princeton 5F2-A 6.5 / 10.2, Princeton AA964 6.4 / 8.9, Vibro-King 8.5 / 13.8, Blues Junior 10.1 / 17.6.

Shared changes: `Pot` takes an audio taper's midpoint (Fender marks it in the part number: "250k15A", "100k30A"; 10 % stays the default, bit for bit). Single-ended power amps: the feedback's sign is flipped (the input drives the grid directly, so the tap is wired the other way round; no model used single-ended feedback before, and it was positive), the Transformer Drive knee moves toward the idle flux instead of below it (it saturated the core at rest), and the block's output scale uses B+ rather than twice B+ as full scale. Waiting on the library: the Supro 1624T and GA-40 need a paraphase inverter (and the GA-40 a 5879 table), the Magnatone 280 its varistor vibrato network; the Concert 6G12 and Super 6G4 are read but their dry path runs through the harmonic vibrato's mixer pair.

**As built (stream C: brownface, Fender bass, Gibson).** Six types appended to `AmpDesign.All`, each from a factory drawing read by eye (sources, readings and every guess in the model files):
- **Concert 6G12** and **Super 6G4** (`Brownface.cs`; schematicheaven `concert_6g12_schem.pdf`, `super_6g4_schem.pdf`, the same drawing family): Normal or Vibrato channel (Switch 1). Each channel: a 7025 half (100k, the two first halves on one 820 Ω ‖ 25 µF), 0.05 µF into the brownface tone network (250 pF to the 250k linear Treble, 100k to its far end, the 250k audio Bass from there to ground with its wiper on 0.01 µF and 10k), the 500k linear Volume (47 pF across it on the Vibrato channel). Normal: the second half on 220k, 0.05 µF and 1M to the inverter's grid. Vibrato: the second half on Fender's **split plate load** (100k over 6.8k, the signal from the junction: a sixteenth of the plate's swing), then the **harmonic vibrato** as a reusable stage (`HarmonicVibratoStageDesign`): 0.05 µF into a crossover (470k into 5 nF, then 10 nF to the bass grid line; 220k / 220k and 250 pF to the treble grid line), each grid line on 1M to its LFO source and 1M to the paraphase splitter's grid (an AC ground through 0.05 µF), the mixer pair (a 7025, 100k plates at 355 V, one 4.7k ‖ 25 µF cathode: 320 V and 3.3 V as drawn), the plates mixed through 470k each. The LFO (`VibratoOscillatorDesign`): its rate solved from the phase-shift ladder (0.02 / 0.01 / 0.01 µF, the 4M reverse-audio Speed with 100k, 1M, 1M: 2.6 to 9.4 Hz); its depth from the network from the oscillator's plate to the 10M Intensity pot's wiper (4.7M + 5 nF, 0.1 µF, 0.05 µF, the wiper loaded by the two 1M feeds: 2.4 V peak at full intensity); the two grid lines driven in antiphase (the splitter taken as exact) as supply rails a network stage now lets another stage move per sample (`IRailModulation`); the pedal (Switch 2) stops it, with a 30 ms smoother. Assumed: the oscillator's plate swing (100 V peak at the fundamental; it clips, and its amplitude is not solved). For CPU the stage runs as two single-triode bands (two 2-port Newton solves instead of one 4-port one: about half the cost), each half on its own cathode held still at the LFO's rate (the pair's currents move in antiphase, so the shared cathode does), the splitter's grid an AC ground, the plates mixed as Thévenin sources; against the whole circuit as one netlist (`HarmonicVibratoStageDesign.Network`) it is −43 dB at 0.3 V and −31 dB at 3 V at the stage's input, −40 / −36 dB as part of the whole channel. At full intensity a 100 Hz and a 3 kHz tone each swing about 15 dB, in antiphase (correlation −0.83). Inverter: 7025, 82k / 100k, 6.8k tail, 820 Ω, 1 nF in; feedback 6.8k from the 2 Ω tap (Concert) or 10k from the 4 Ω tap (Super) into 1.5k with the 5k Presence and 0.1 µF; 6L6GC at 460 V, silicon or GZ34. Guessed: the OTs (4.2k), the 6L6 idle current (35 mA: the drawing's −55 V gives 21 mA on the Koren curves, which the dissipation check rejects as too cold).
- **Deluxe 6G3**: Normal or Bright channel; V1 halves on 220k from 270 V (0.003 µF across the Normal plate's resistor), one 1.5k ‖ 50 µF; 0.02 µF into the 1M audio Volume with the Tone pot's wiper on its top (one end 0.01 / 0.02 µF to ground, the other 500 pF to the Volume's wiper); 220k mixers with the idle channel's Volume loading the mix; V2A on a 100k / 15k split load; 12AX7 LTP (82k / 100k, 6.8k, 820 Ω); 2 × 6V6GT at 375 V on a GZ34; the 125A1A (6.6k : 8 Ω, sourced); 56k feedback into 1.5k. The 6V6 idle is 30 mA (the drawing's −26 V would be 43 mA, 16 W, on the Koren curves).
- **Vibrolux 6G11**: Normal or Bright channel; V1 100k / 1.5k ‖ 25 µF (0.003 µF on the Normal plate) driving the 6G11 network from the plate (250 pF to the Treble, 100k to a node with 0.1 µF to the Treble's 70k tap and 0.05 µF to the Bass rheostat's far end with 6.8k to ground, the Treble's bottom on 5 nF); the Treble's wiper is limited to the tap's upper 180k (a fixed netlist cannot let a wiper cross a tap); 1M audio Volume (47 pF on Bright); V2 on one 820 Ω ‖ 25 µF, 220k mixers; 12AX7 LTP; 2 × 6L6GC at 365 V, GZ34; 56k feedback. **The drawing has a bias tremolo, not the harmonic vibrato** (the catalogue had it as the 6G12's): not modelled, as with the 6G3. Guessed: OT 4k, idle 35 mA.
- **Bassman 135** (CBS 014283 rev 1, with the Bassman 100's 010303 rev F for the small print: the same preamp): Bass or Normal channel (Switch 1). Bass: V1A (100k, 1.5k ‖ 5 µF) into a 500 pF / 100k / 0.1 µF / 0.047 µF stack with a 10k Middle rheostat, 0.0047 µF in series into the Volume, **Deep** (Switch 2: 0.047 µF from the slope's bottom to ground). Normal: 250 pF, 0.047 µF into the Volume, the 120 pF **Bright**. V1B and V2B share 820 Ω ‖ 5 µF (twice the resistor for the operating point; at audio the idle triode's cathode beside it, as an AC branch), 220k mixers, 0.022 µF into the Master; 12AT7 LTP (47k / 47k, 22k tail, 470 Ω, 330k grids, 0.01 µF in); **4 × 6L6GC ultralinear** at 500 V (470 Ω from each screen to the OT's screen taps), 1.5k stoppers, 820 Ω feedback from the 4 Ω tap into 100 Ω. The drawing's AC test points (6 mV in, every control up): V1B's grid 50 mV against 80 mV, its plate 2.6 V against 4 V, the mix 1.24 V against 1.8 V: V1B and the mix are within 0.7 dB of the drawing's ratios, the first stage and stack 4 dB short (unresolved; the pot tapers are guesses). Guessed: the OT (2k), the ultralinear tap (40 %), the idle current (30 mA).
- **Scout GA-17RVT** (Gibson, with DC voltages): V2A (½ 6EU7, 470k, 4.7k ‖ 20 µF), 0.001 µF into a bridged-T notch (220k + 220k, 3 nF to ground, 500 pF across), the 500k audio Volume (the reverb's 2M drive pot across it, at 0), 470k into V2B (470k, 4.7k with only 0.1 µF), a 6EU7 cathodyne (100k, 2.7k + 100k, the grid's 470k to the junction), 2 × 6AQ5 on a shared 270 Ω ‖ 20 µF at 300 V, EZ81 (6CA4), no feedback. **New tubes**: `TriodeModel.SixEU7` (μ 100 held; Ex 1.0, K_g1 988, K_p 503, K_vb 4240, fitted to RCA's tabulated points and 17 points read off its plate curves: 11 % RMS in log terms, where Koren's 12AX7 is off by a factor of 2.5 at low current; the stages then sit at 119 V and 122 V against the drawing's 120 V and 130 V) and `PentodeModel.SixAQ5` (μ 9.5, Ex 1.35, K_g1 2257, K_g2 7733, K_p 56.4, K_vb 32.9, fitted to GE's two class-A points and the triode connection: currents within 2 %, transconductance within 2 %). Guessed: the OT (the datasheet's 10k push-pull load). Reverb and tremolo off.
- **Library changes** (small): `HarmonicVibratoStageDesign` / `VibratoOscillatorDesign` / `HarmonicVibratoStage` (new file); `IRailModulation` on `NetworkStage` (sets one supply rail per sample: an array write) and `StageNetlist.DcBlock` (a 1 µF / 100M block for a stage whose output rests at a plate's voltage: the power amp's input network wants an input resting at 0 V, else it thumps at reset); the two tube tables; the idle-dissipation test knows the 6AQ5's 12 W.
- **Validation**: every new stage against the reference simulator (−54 to −82 dB; the vibrato's two bands against its whole netlist as above); each preamp as a whole chain with no Thévenin cut, −43 to −68 dB (`AmpStageTests.Brownface.cs`); knob sweeps at full drive converge with no NaN, every control audible, volume and master monotonic, the vibrato's rate, depth and antiphase, no allocation (`BrownfaceAmpTests`).
- **References**: none. GuitarML ToneLibrary (only 5F6-A Bassmans) and pelennor2170/NAM_models hold no capture of any of the six (searched October 2026).
- **CPU** (48 kHz, 64 frames, Gain 7, Master 6, best of three passes, on a machine with a load average of 45 to 230, where the Plexi read 9.0–10.0 / 15.1–16.8 % in the same runs): Concert 6G12 and Super 6G4 Normal 8.8 / 15.6, Vibrato channel 11.0 / 20.7 (pedal on, Intensity 7: 11.1 / 20.8), at the clean budget's edge; Deluxe 6G3 9.0 / 11.3 (Bright 9.4 / 17.0); Vibrolux 6G11 9.1 / 12.8 (Bright 9.1 / 15.6); Bassman 135 10.0 / 12.8 (Normal 10.0 / 14.0); Scout GA-17RVT 8.0 / 12.3 (guitar / loud noise, %).

### 3.2 Marshall and British

**JTM45** — Marshall reissue drawing JTM45.DGM issue 7: https://el34world.com/charts/Schematics/files/Marshall/Marshall_jtm45_readable.pdf
- Inputs 68k stoppers, 1M. V1A/V1B 100k plates, shared 820Ω‖250µ [src].
- 0.02 µ into two 1M-A volumes; one channel 500 pF across the pot and 500 pF across its 270k mixing resistor; both mixers 270k [src].
- V2A 100k, 820Ω unbypassed; V2B CF DC-coupled, 100k [src].
- Stack: slope 56k, treble cap 270 pF, bass and mid 0.02 µ; treble 250k-B, bass 1M-A, mid 25k-B; fed from the CF [src].
- PI LTP: 0.02 µ in, 1M grids, 470Ω, 10k tail, 82k/100k, 47 pF between plates, 0.1 µ out, 220k leaks [src].
- NFB 27k from the 16Ω tap; presence 5k-B + 0.1 µ [src].
- 2 × KT66 (historically; not on this drawing), fixed bias; GZ34 *(type unverified)*, 20 H choke, 32 µ filters; droppers 8k2 and 10k [src]. OT impedance not on the drawing.

**1959 Super Lead (1968–69)** — Unicord drawing 70-6-11: https://el34world.com/charts/Schematics/files/Marshall/Marshall_jmp_superlead_100w_1959.pdf; AmpBooks JMP50 1987: https://ampbooks.com/mobile/classic-circuits/JMP50-1987/
- V1: 100k plates; normal half 820Ω‖250µ, bright half 2.7k‖0.68µ [src].
- Coupling: normal 0.022 µ, bright 0.0022 µ, into 1M volumes; 0.005 µ across the bright volume [src].
- Mixers 470k + 470k, 500 pF across the bright mixer [src].
- V2A 100k, 820Ω (1k on some) ‖ 0.68 µ; V2B CF 100k [src].
- Stack: slope 33k, treble cap 500 pF, bass and mid 0.022 µ; treble 250k on the drawing (220k later *unverified*), bass 1M, mid 25k [src]. Earlier amps: 56k slope.
- PI: 0.022 µ in, 1M, 10k, 470Ω, 82k/100k, 47 pF, 0.022 µ out, about 220k leaks [src].
- Presence 5k + 0.1 µ, 4.7k at the tail bottom; NFB "47K (100K)", tap *(unverified)*; AmpBooks gives 47k at 8Ω for the 1987 [src].
- 4 × EL34 (6550 on US units), 5.6k stoppers, 1k screens; SS bridge, 100 µ stacked; c.1967 voltages 460 / 375 / 300 / 270 V [src]. OT not shown; the current Plexi model uses 3.4k.
- Note: the existing Plexi model's values match this drawing except the treble cap and coupling; check them.

**JCM800 2203 / 2204** — 2203: https://el34world.com/charts/Schematics/files/Marshall/Marshall_jcm800_lead_mstvol_100w_2203.pdf; 2204: https://el34world.com/charts/Schematics/files/Marshall/Marshall_jcm800_lead_mstvol_50w_2204.pdf; https://robrobinette.com/How_the_Marshall_JCM800_Works.htm; https://ampbooks.com/mobile/classic-circuits/marshall-jcm800-2204-power-amp/

| Part | Values [src unless marked] |
|---|---|
| V1A | 68k stopper, 1M; 100k plate; 2k7‖0.68 µ; 100 pF at V1A |
| Attenuator | 470k‖470 pF, then the 1M-log preamp volume with a **1 nF bright cap** |
| V1B cold clipper | 100k plate, **10k unbypassed** |
| Interstage | 0.022 µ, 470k‖470 pF over 470k |
| V2A | 100k, 820Ω unbypassed |
| V2B | CF DC-coupled, 100k |
| Stack | slope 33k, 470 pF, 0.022 µ, 0.022 µ; treble 220k-lin, bass 1M-log, **mid 22k-lin**; master 1M-log after it |
| PI | 0.022 µ in, 1M/1M, 470Ω, 10k tail, 82k/100k, 47 pF, 0.022 µ out, 220k leaks |
| NFB / presence | 100k from the 4Ω tap; presence 22k-lin + 0.1 µ, 4k7 at the tail bottom |
| Power (2203) | 4 × EL34 (6550 US), 5k6 stoppers, 1k 5 W screens; 1N4007 bridge, 50+50 µ, choke |
| Power (2204) | 2 × EL34; 345-0-345 V, 488 V unloaded, about 460 V plates, 450 V screens, −40 V bias, 40 mA; 40 H choke, 100 µ; **OT 3.2k p-p** (AmpBooks) |

**Silver Jubilee 2555** — Marshall "2555 STD" issue 3: https://el34world.com/charts/Schematics/files/Marshall/Marshall_25aniv_silverjubilee_50_100w_2555.pdf
- V1A 100k, 2k7‖0.68 µ; gain pot 1M-log with 1 nF bright cap; V1B 100k, 1k5‖10 µ [src].
- **Lead clipping network**: LED pair and 1N4007 strings with 1 nF, into a 220k Lead Master; **rhythm-clip** (pull) with 1N4007 diodes and 2n2 [src]. Needs a diode clipper stage.
- V2A 100k, 1k5 unbypassed; loop; V2B 220k plate, 470Ω [src].
- **Non-standard stack** (not the classic TMB): 4n7, 220 pF, 100k, 10k, 0.1 µ, 0.01 µ, 470 pF, 2n2, 100k; treble 220k-lin, mid 100k-log, bass 1M-log [src]. Needs the general passive-network stack.
- PI: 0.022 µ, 1M, 470Ω, 10k, 82k/100k, 22n out, 220k leaks [src]. NFB 100k, presence 22k + 0.1 µ, 4k7 [src].
- 4 × EL34, 5k6 stoppers, 1k5 screens; High/Low switch (triode/pentode on the screens) [src]. B+ and OT not on the sheet.

**JCM900 4100** (tier 2) — https://el34world.com/charts/Schematics/files/Marshall/Marshall_jcm900_dualrev_100w_4100.pdf. **Hybrid**: op-amp front end (M5201) with a gain op-amp, a diode-bridge clipper around V1A, then the Marshall stack (33k, 470 pF, 22n, 22n; 220k/1M/22k) and an op-amp buffer [src]. PI 0.022 µ, 1M, 1k, 15k tail, 82k/100k, 47n out, 150k leaks [src]. 4 × 5881 (US) or EL34 (export); NFB 56k‖10n (US) or 100k (Dual Reverb export) [src]. Needs op-amp stages (we have op-amp models in the drive catalogue).

**JVM410H** (tier 2) — factory sheet JVM410-60-02: https://el34world.com/charts/Schematics/files/Marshall/Marshall_jvm410_sch.pdf; values unreadable automatically. 5 × ECC83, 4 × EL34, presence and resonance in the NFB.

**Friedman BE-100** — third-party trace, rev 1.5: https://music-electronics-forum.com/filedata/fetch?id=853106&d=1591761457 (thread https://music-electronics-forum.com/forum/amplification/guitar-amps/maintenance-troubleshooting-repair/49720-?p=758061). All *(unverified)*:
- V1A 100k, 2.7k‖0.68 µ, 500 pF; 2.2 nF coupling; 1M/68k divider. V2A 100k, 2.7k‖0.68 µ; 2.2 nF. Fat switch 22 nF. V2B 100k, 2.7k‖0.68 µ; 470k‖500 pF. V3A 100k, 820Ω‖0.68 µ plus switched 470 µ ("Voice"). V3B CF 100k.
- Sat switch: MPSA06 / diode clipping at the stack input.
- Stack 500 pF, 33k, 22n, 22n. PI 0.022 µ, 1M, 470Ω, 10k, 82k/100k, 100 pF, 220k. 4 × EL34, 1.5k stoppers, 1k screens; SS rectifier. Presence/depth network 47k, 220k, 4.7 nF.

**Orange Rockerverb 50 MkI** (tier 2) — ORA-CD204/CD206: https://el34world.com/charts/Schematics/files/Orange/Orange_rockreverb_50w.pdf. Four cascaded dirty stages (1k5‖10µ, 1k0‖10µ, 2k2‖10µ, 1k5 unbypassed, all 100k plates), stack fed from a plate (no CF): 560 pF, slope 39k, 22n, 22n; treble 250k-B, bass 500k-A, mid 25k-B [src]. PI 680Ω, 15k tail, 82k/100k [src]. No presence. The drawing labels 4 × 6V6, which contradicts the EL34 spec: check. No MkIII schematic found.

**Laney GH100L** (tier 2) — https://el34world.com/charts/Schematics/files/Laney/Laney_gh100l.pdf. JCM800-like with a 10k cold clipper (V2A), 33k / 470 pF / 22n / 22n stack (one drawing says 220n: conflict), presence 22k + 100n, NFB 100k switchable, **resonance** 100k + 100n; 4 × EL34 or 5881, 460 V [src].

**As built (stream F2).** Models in `Amp/Models` (Type index in brackets), shared parts in `MarshallParts.cs`; every value in each file's comments with its source, guesses marked:
- **JCM800 2203** [5] and **2204** [6], from the factory drawings above (read by eye; they agree with the table). V1A with the High/Low jacks as one network stage (Low on switch 1 takes the guitar past V1A into the 470k/470 pF attenuator, as the jack's contact does), V1B cold clipper, V2A, CF on the JCM800 stack (22k mid), master; PI with its input network and grid current. 2203: 4 × EL34, 1.7k OT, 470 V *(unverified)*; 2204: AmpBooks' 460 V, 40 mA, 3.2k. Preamp nodes 290/310 V *(unverified)*.
- **JTM45** [7]: both channels and the jumper (switch 1) and normal input (switch 2), Control 1 the normal volume. The two V1 triodes share a 250 µF-bypassed cathode, so they are one triode driving whichever volumes are active (an idle channel's cap hangs on the idle plate's rest voltage behind 100k ‖ r_p). KT66, GZ34, B+ 440 V, 3.4k OT *(unverified)*. No PI input network: with it, the power amp's Newton missed convergence under full-scale square drive.
- **Plexi 100W** [8], the 1959 from drawing 70-6-11 (the existing **Plexi** [1] is unchanged): both channels with separate cathodes as one two-triode network stage, jumper and normal input as on the JTM45; 0.0022 µF bright coupling with 5 nF across its volume; no master; 4 × EL34 on the c.1967 nodes. No PI input network either (CPU: the two-triode stage costs as much as two gain stages). Feedback "47K (100K)" taken as 100k at 16 Ω *(unverified)*.
- **Laney GH100L** [9], drive channel, from the factory CAD drawing 2188 (vector PDF, with DC voltages: the model's V1b, V2a and V2b plates are within 4 % of them; V3b sits at 184 V against 150 V). Its effects loop (two op-amps, ×0.033 overall) is a passive divider with the same ratio, corner and load. Not modelled: the clean channel relay and the Resonance switch.
- Not built: **Silver Jubilee** (the relay, the V1B/V2A shunt feedback and the LED/diode networks need a closer read of the scan than this pass had), **Friedman BE-100** (the forum trace is a clean CAD drawing and readable; next), **JCM900** (the drawing's op-amp values are unreadable at the scan's resolution), **Hiwatt** and **Orange** (partial data).
- Matching: the three Laney captures now map to the GH100L, with gain, drive and master fitted at +8 dBu (their settings are not stated). The fitted calibrations come out at 6–11 dBu instead of pinned at −20 dBu; pre-emphasised ESR 0.48 / 1.04 / 0.91 and MRSTFT 1.03 / 1.30 / 1.16 (after break-up / crunch / Hi mid; with the Plexi stand-in 0.67 / 1.23 / 0.88 and 1.05 / 1.33 / 1.15); targets tightened to just above these. A tolerance fit (bias, OT match, B+) gains 4 % and ends on its bounds, so it is not applied. The "distorts more at equal input" of the earlier runs was, for these, the stand-in at master 10 against captures at lower settings; the F2 stages' plate loads, cathodes and tapers are as drawn, and the Laney's DC voltages agree. The JCM900 captures stay on the Plexi stand-in (the JCM800 2204 fits channel A only with implausible knob settings, and channel B worse than the Plexi).
- Library: a network stage now recomputes its Thévenin output resistance when a pot or switch moves (it was fixed at the last reset, so the next stage's grid current saw a stale source after a volume or input switch moved).

**Hiwatt DR103** (tier 2) — PI: https://ampbooks.com/mobile/classic-circuits/hiwatt-phase-inverter; stack (CP103): https://ampbooks.com/mobile/classic-circuits/cp103-tonestack/; https://hiwatt.org/tech2.html. PI is an LTP with **fixed bias from a CF** (220k cathode, about 70 V at the PI grids), about 87k plates, 2.2k bias, 22k tail [src]. NFB 10k from the 16Ω tap; presence 470Ω, 1000 pF, 0.1 µ; fixed treble boost 470Ω + 0.01 µ [src]. 4 × EL34, −36 V, about 496 V plates [src]. Whether the DR103 uses the CP103 stack is *(unverified)*.

**As built (stream F2b, Marshall and British, phase 7 second pass).** Five types appended to `AmpDesign.All`; each file
cites its drawing and marks its guesses (mostly the rails, idle currents, output transformers and feedback taps, which
none of the drawings give):
- **BE-100** and **BE-100 HBE** (`FriedmanBE100.cs`), from the rev 1.5 "BE-100 v2" trace (2014). Read from it, against the
  unverified list above: V1A and V2A have **220k plus 100k with 500 pF across the 100k**, not a plain 100k; the "C45"
  switch chooses 68k series with 68k to ground or 560k ‖ 680 pF series with 220k to ground before V2B's 33k stopper; the
  Fat switch adds 22 nF to V2A's 2.2 nF; V3A has 100k with 500 pF from the rail and 820 ‖ 0.68 µF, plus 470 µF with
  Voice (a two-way switch on this trace; later amps have three positions); V3B follows into a 500 pF / 33k / 22 nF /
  22 nF stack (250k-B, 1M-A, 25k-B) and 220k into 1M and the master. HBE adds V1A through 2.2 nF into a 1M/68k divider.
  Sat is 100 nF and 10k from the follower's cathode onto two MPSA06 used as zeners (a symmetric clamp of about 6.8 V
  here; the trace's wiring would clamp one polarity: unsettled). The feedback is 47k in series with 220k ‖ 4.7 nF into
  the 5k presence pot. The loop (two LND150 followers) is taken as transparent.
- **Silver Jubilee** (`SilverJubilee.cs`), from 2555 STD issue 3. The relay contact L either grounds V1B's 220k grid
  leak and 220k plate load (Boost, the relay at rest: full gain into the clipping network) or joins them to the
  clipping network's top T, which makes the 220k shunt feedback around V1B and feeds V2A's grid through 10k + 100k beside
  the Lead Master (rhythm). The LED strings at T are an LED and two 1N4007 one way, an LED and one 1N4007 the other.
  "Rhythm Clip" joins L to the 1N4007 pair that otherwise hangs on V2A's grid through 47k and 2.2 nF. V2A has 1M of AC
  shunt feedback from its coupled plate; a 100k/10k send divider; V2B (220k, 470) drives the Jubilee's own tone network
  (a tandem Bass pot, 220 pF / 470 pF / 4.7 nF / 10 nF / 0.1 µF / 2.2 nF; values in the file) and the Output Master.
  High/Low is the power amp's triode switch.
- **18 W 1974X** (`Marshall18W.cs`), PedalMonkey's rev 5 drawing with its node voltages: the Normal channel's two
  paralleled triodes into its tone and volume network, or the Tremolo channel with the tremolo off (the footswitch
  grounds the oscillator's grid, so it idles on the shared cathode and its plate current sets the input triode's supply);
  the 47k-tail long-tail pair is the mixer; two EL84 on 125 Ω, no feedback, an EZ81.
- **Hiwatt DR103** (`HiwattDR103.cs`), Mark Huss's hiwatt.org redraws (mid-70s four-input preamp rev 1.4, 100 W output
  v1.1): V1 with 220k plates on a shared 1k1 ‖ 47 nF (both channels, jumper and normal input as on the Marshalls), V2A
  (100k, 1k ‖ 47 nF) driving the Hiwatt network from its plate (1 nF, 220 pF, 220k, 22k, Middle 100k ‖ 1 nF, Bass 470k-A,
  100k with 47 nF and 47 nF), 22k into the 220k-A master, V2B (100k, 1k5), a 12AT7 long-tail pair (82k/91k) biased by a
  cathode follower at about 50 V (here the library pair with the same rest point: 22.6k tail, 1.6k bias), four EL34 with
  22k stoppers and 100 Ω screens, 10k of feedback from the 16 Ω tap into 2k2 and the presence.
- **JCM900 4100/4500**: not built. The vector reissue of drawing JMP52-02 (issue 11, 1997) is fully legible and was
  read: input 10k, 100 pF, 0.1 µF, 1M; IC1 (M5201, a dual op-amp whose sections the channel switch mutes) channel A
  non-inverting 1 + 100k ‖ 22 pF over 15k + 0.1 µF, channel B 1 + 100k ‖ 470 pF over 4k7 + 47 nF; channel A: 47 nF, 22k
  to ground, 12k into IC4a, an inverting stage whose feedback is the 220k-B Preamp pot's lower part with 47 pF and two
  LEDs each way (the upper part with 1 nF is its input arm); channel B: 0.22 µF, 10k into IC6b, inverting, the 1M-A Gain
  rheostat with 220 pF as feedback, 0.22 µF and 4k7 onto a W005F bridge with a 1N4007 across its AC terminals (an
  asymmetric clamp to ground), then a pre-EQ ladder (4k7, 4.7 nF, 0.22 µF, 0.22 µF, 10k, 10k, 100k, 4k7) into IC4b
  (1 + 10k/22k); both into 2k2 and 68k to V1a (150k, 1k5, 22 pF grid to cathode) with **220k of shunt feedback** from the
  stack's input; the V1b follower (100k) through 0.1 µF onto a 470 pF / 33k / 22 nF / 22 nF stack (220k-B, 1M-A, 22k-B);
  470k into the send buffer; the return op-amp into V2a (100k, 2k2 with the 10k-B Presence and 470 Ω + 0.22 µF across
  it); V2b (100k, 1k2) with 470k shunt feedback mixing the reverb; the two masters are op-amp stages (220k in, 1M-A
  rheostat feedback). It needs an op-amp element in `StageNetlist` (the `OpAmp` device at a network stage's root) and the
  return op-amp's input resistor, which is not on the sheet. The pelennor captures stay on the Plexi stand-in.
- **Library changes** (small): `FeedbackDesign.LowFeedback`, a fixed low shelf in the global feedback (default 1, no
  change for the other models), for the BE-100's 220k ‖ 4.7 nF; network stages accept two diode clippers beside a triode
  or two triodes ("DD", "TDD", "TTDD"), for the Jubilee.
- **Validation**: the preamp chains against the reference simulator with no cuts (`British_chains_match_the_reference`):
  BE-100 −41 dB at 2 mV and −19 dB at 50 mV, BE-100 HBE −71 dB, Hiwatt −59 dB at 2 mV and 50 mV, Jubilee −14 dB at 2 mV
  (the cut at V2A's grid against its 1M feedback; with V2A inside the clipping circuit it was −27 dB at half again the
  cost; at higher drive the reference simulator itself fails to converge on the LED strings). Knob sweeps at full drive
  converge, every panel control moves the sound, gain and master are monotonic (`BritishAmpTests`).
- **CPU** (48 kHz, guitar / loud noise, `Gain` 7, `Master` 6, measured beside other streams on a loaded machine where
  Plexi read 8.6–11.0 / 14.4–21.3 %): BE-100 15.9 / 25.3, BE-100 HBE 16.6 / 26.7 (high-gain budget); 18 W 10.0 / 13.5,
  Tremolo channel 16.5 / 25.4; Hiwatt DR103 12.5 / 30.8; Silver Jubilee 17.1 / 35.3 (Boost 17.9 / 32.0). The Jubilee is
  over its crunch budget: its V1B circuit (a triode and two clippers in the relay's feedback loop) takes many Newton
  iterations on noise. Its LED strings are three and two fitted junctions (3.0 V and 2.0 V): five and four silicon
  junctions fit the parts better but left Newton unconverged on a few dozen samples at full drive. The Hiwatt is over
  on noise (the 0.25 feedback fraction and the two-triode input).
- **CPU pass (stream P; §5.3 has the general findings).** Interleaved with the old code, minimum of three passes,
  thread CPU time on a loaded machine where the Plexi read 8.4–8.5 / 14.5–14.9 %, guitar chord / loud noise at the
  default panel: Hiwatt DR103 13.1 / 26.9 → 10.2 / 19.5 (Jumper 10.9 / 19.0), inside the crunch budget: the one-port
  safeguard takes its power amp from 8.1 to 6.0 iterations a sample on noise. 18 W Tremolo channel 11.7 / 18.6 → 9.0 /
  14.4 (Normal 7.3 / 10.3, unchanged), crunch: its two-triode V3 runs the cheaper multi-port Newton. Silver Jubilee, high
  gain: Boost 16.4 / 38.4 → 13.9 / 31.5, Rhythm 19.1 / 35.0 → 15.6 / 28.2 (Boost Clip 14.6 / 29.0, Rhythm Clip 15.2 /
  28.2): inside on guitar, still 0.2–3.5 over on noise. Only bit-exact changes reach its V1B: that stage leaves about 45
  samples a second of loud noise unconverged at its 32-iteration limit in Boost, and letting them converge moves the
  output by −29 dB, so any Newton shortcut there fails the −60 dB comparison against the old renders. The next step for
  the Jubilee is to make that stage converge (and accept the change of sound), then use chord steps there. Residual
  against the old renders (every channel, five knob settings, DI, hot DI, chord, noise, sweep): Hiwatt −140 dB, Jubilee
  −165 dB, 18 W bit-exact.
- **References**: none of the four amps has a GPL direct-out capture; the JCM900 captures stay on the Plexi stand-in.

**As built (stream B, British vintage and boutique).** Seven types appended to `AmpDesign.All`; each file cites its
drawing and marks its guesses (mostly the rails, idle currents and output transformers, which none of the drawings give):
- **1959SLP** and **1987X**, from the factory reissue drawings 1959-01-60-02 issue 2 and 1987-01-60-02 issue 4 (DrTube):
  `MarshallParts.TwoChannelInput` with the reissues' values (both V1 cathodes 820 Ω, 330 µF on the Inputs 2 half and
  680 nF on the Inputs 1 half; Inputs 1 couples through 3n3 on the 1959SLP, 22 nF on the 1987X, with 4n7 across its
  volume and 470 pF across its 470k mixer), V2A with 680 nF on the 1959SLP and unbypassed on the 1987X, the follower on a
  470 pF / 33k / 22 nF / 22 nF stack (B250K, A1M, B25K; `MarshallVintageParts.ReissueStack`), 1k5 stoppers, and 47k
  (1959SLP, four EL34) or 100k (1987X, two EL34) of feedback from the 16 Ω tap into the B5K presence pot alone.
- **1959T Tremolo** (Unicord 70-11-11, 1970): the 1968–70 Super Lead with the tremolo channel's load, 0.1 µF and 93k
  (10k, the 50k Intensity, 33k) from the normal volume's wiper to ground, which the oscillator's transistor chops when the
  tremolo runs; here the tremolo is off (no library tremolo), so the load is fixed: the normal channel's first stage is
  5.5 dB below the Super Lead's at noon. Feedback 47k (the drawing's "47K (100K)") into the 5k presence pot.
- **Bluesbreaker 1962** (reissue drawing "1962 STD", issues 3 and 4): V1a (Channel II, **150k** plate) and V1b
  (Channel I, 100k) on a shared 820 Ω with 330 µF as one two-triode network (the JTM45's one-triode trick needs equal
  plates); 100 pF across both A1M volumes; Channel I's mixer 470k with 220 pF; Channel II's wiper reaches the mixing node
  through 470k and through 100k plus 470k, the tremolo's 0.22 µF and 320k (FET off, a guess) hanging on the junction;
  V2a 100k with 1k unbypassed, the follower on 220 pF / 56k / 22 nF / 22 nF (B220K, A1M, B22K), 0.1 µF into 220k leaks,
  two 5881 (6L6 curves) with 470 Ω screens and no stoppers, a GZ34, 27k of feedback into the 4k7 presence pot.
- **Hiwatt DR504** (Custom 50): the DR103's four-input preamp, copied with its rails as parameters into
  `HiwattParts` (the DR103's file is unchanged), with hiwatt.org's 50 W output and supply redraws: two EL34 with 22k
  stoppers, 100k leaks and 1k5 screens, 460 V from a doubler. `DR504OL.gif` on hiwatt.org is a complete two-input DR504
  of 1983 with DC voltages and an extra 220k gain stage before the tone network: a later circuit, used only to scale the
  guessed rails (355 / 395 / 450 V).
- **Cornford MK50H II** (Martin Kidd's own five sheets, 2011): both channels on Switch 1 via `ChannelSwitchStageDesign`.
  V1's plate load is 180k under 39k; the Clean channel's stack (330 pF, 68k, 0.1 µF, 33 nF; 250k-L / 250k-A / 25k-L)
  hangs on that junction with its 1M-A volume (on the Overdrive channel it would move V1's plate load by about 6 % and
  is left out, for CPU); the Overdrive takes 4.7 nF and
  100k onto 4k7 and the 250k-A Gain as a variable resistor, V2 (100k, 1k5 ‖ 2.2 µF), 680k ‖ 470 pF onto the 1M-A
  Overdrive, V3 a 15k cold clipper, 470k ‖ 470 pF over 470k; V4 (220k, 1k5) DC into the V5 follower (cut at V4's
  plate, as the Marshalls' followers), which drives the Overdrive's stack (220 pF in series with 100k as the treble
  cap, 43k slope, a 1M-A bass rheostat with 1 nF at its top, 33 nF to the middle's wiper) with 470k into its 25k-L
  Volume, or the Clean channel's fixed 100k / 15k pad (each channel's network carries only its own path); the loop's
  return V6 (220k) into 220k and the 1M-A Master as a rheostat; 91k / 100k inverter, two EL34, 10k stoppers, feedback
  from the 8 Ω tap through 47k and the 1M-A Resonance with 4.7 nF into 4k7 and the 25k Presence. Panel names from
  Fractal's notes (Gain, Overdrive, Volume, Resonance). Simplified: the Resonance is the library's depth shelf at
  400 Hz; one master; the loop as a wire.
- **Tiny Terror** (a vector redraw, not a factory sheet): V1A, then 1 nF and 68k onto 470k and the dual 500k-A Gain's
  first half (100 pF bright) into V1B (470 pF across its 100k plate load), 68k and 47 nF onto 220k and the Gain's second
  half into the 12AX7 pair (47k tail), two EL84 on 120 Ω, no feedback. Simplified: the dual 500k Volume between the
  inverter's coupling caps and the grids is the library's master across the inverter's outputs (`ShuntMaster`), and the
  Tone (2.2 nF and 500k-L across the grids) is the library's cut across the inverter's plates, which turns the other
  way (shown as Cut).
- **Not built**: the **Marshall Major** (Unicord 70-02-12 read: V2B is a cathodyne into an ECC82 push-pull voltage
  amplifier that drives four KT88 through coupling caps, with no followers, which `DriverChainDesign` assumes; and the
  feedback returns to V2A's cathode with the tone stack inside the loop: it needs a driver-chain variant and feedback
  into a preamp stage); the **Orange AD30** (the cd0115 sheet is the main board only: the gain, tone and channel
  circuits on the front-panel board are not in it).
- **Library**: no shared code changed. New model-side parts files: `MarshallVintageParts` (the two stacks, a two-channel
  input with a load on the normal volume's wiper, the Bluesbreaker's input) and `HiwattParts` (a copy of the DR103's
  preamp stages).
- **Validation** (`BritishVintageAmpTests`): the preamps as whole chains against the reference simulator with no cuts:
  1959SLP −63 dB, 1959T −63 dB, Bluesbreaker −47 dB (2 mV and 50 mV), Tiny Terror −58 / −57 dB, Cornford Overdrive
  V1–V5 −48 dB, Clean V1, V4, V5 −53 dB. Knob sweeps at full drive converge with no non-finite samples and peak below +1 dBFS;
  every panel control moves the sound; gain and master are monotonic.
- **References**: no GPL direct-out capture of any of these amps was found (pelennor's list and GuitarML's ToneLibrary
  searched; ToneLibrary's "PlexiBreed" is a pedal); all ship schematic-only.
- **CPU** (48 kHz, guitar / loud noise, Gain 7, Master 6, the minimum of three to four passes on a machine at a load
  average of 30–180, where Plexi read 9.1 / 15.1 % and Plexi 100W 14.4 / 24.0 %): 1959SLP 14.6 / 22.7, 1987X 14.0 /
  22.2, 1959T 14.5 / 23.1, Bluesbreaker 14.0 / 23.9, Hiwatt DR504 13.1 / 27.6 (DR103 13.5 / 31.9 in the same runs),
  Cornford Overdrive 15.5 / 23.2 (21.4 / 31.0 before the V4 cut and the per-channel networks), Clean 10.9 / 13.4,
  Tiny Terror 9.4 / 13.8. The four Marshalls are a little over the crunch budget, as the Plexi 100W they share the
  two-triode first stage with; the DR504 is over on noise, as the DR103.

**As built (stream A, modern Marshall, op-amp and channel-switching designs).** Four types appended to `AmpDesign.All`;
each file cites its drawings and marks its guesses (mostly the rails, which none of these drawings give, the output
transformers and the feedback taps):
- **JCM900 4502** and **JCM900 4100** (`JCM900.cs`), both channels, from JMP52-02 issue 11 as read above, with these
  corrections: the W005F bridge has the 1N4007 across its DC terminals, which makes a symmetric clamp of three junctions
  each way; channel A's IC4a, channel B's IC6b and IC4b, both IC1 sections and both masters are M5201 switching op-amps
  whose sections share one output; the two masters are the same circuit, so one Master knob turns the playing one;
  Presence is V2a's cathode network (470 Ω + 0.22 µF and the 10k pot across 2k2), a preamp control on Control 1; the
  return op-amp's input resistor (not drawn) is taken as 100k. Power amps from the scans 4100-60-02 issue 11 (4 × EL34,
  47 nF out) and CDD112 issue 10 for the 50 W models (2 × EL34, 2k2 screens, 22 nF out): 82k ‖ 100 pF / 100k, 1k bias,
  15k tail, 150k leaks, 1k5 stoppers, 100k of feedback into 4k7 (the Dual Reverbs' presence cap is "not fitted").
- **Valvestate 8080** (`Valvestate8080.cs`), both channels, from PC0689 issue 5 and PC0689P issue 9 (vector,
  schematicheaven): boost IC1a ×22, IC3b (Gain as a feedback rheostat, OD1/OD2 on Switch 2) onto two LEDs, the ECC83 as
  a shunt-feedback gain stage (220k + 100 nF from its follower to its grid) driving a Marshall stack with a 5 nF bass
  shunt, the Contour network and IC5b ×11; Normal: IC1b with the Crunch treble boost and IC2 with the Crunch LEDs (Switch
  3), its own stack and IC5a ×2; both into the loop's summing stage, IC8a and the Master, then the solid-state power amp
  (`AmpDesign.SolidState`: 33k over 1k of voltage feedback, BDV65/64 Darlingtons with 0.33 Ω, ±39 V on 2200 µF). Its
  current feedback (0.33 Ω in the speaker return) is not modelled.
- **JVM410H OD** (`JVM410H.cs`), OD1 and OD2 (Switch 1) in the full-cascade mode, from JVM410-60-02 issue 2 and
  JVM410-61-02 issue 1 (vector, el34world): V6b, the front panel's OD1 (3.3 nF, 470k, 1 nF) or OD2 (1 nF, 470k, 1.5 nF)
  gain network, V6a (220k, 2k7 ‖ 680 nF), V7b (82k + 22k with 1 nF to the rail, 10k unbypassed), V7a (220k, 820 Ω), the
  V8b follower into both OD stacks in parallel (470 pF, 33k or 47k, 22 nF, 22 nF; 39k into 1M volumes, 330k onto a bus
  the idle channels load), the V8a follower, the bypassed loop's mix node, V9b as a shunt-feedback mixer (4M7 over 1M),
  the V9a follower, the 200k Master, a 82k / 100k long-tail pair with a 10k + 4k7 tail and 82k of feedback, four EL34.
  Not built: Clean/Crunch and the Green/Orange modes (the drawings name the relays but not the states each mode sets).
- **Not built**: the DSL100 (main board JCM2-60-02 iss 9 and front panel JCM2-61-00 iss 5 fetched from DrTube and read in
  part; the boards' connectors still to be traced), the JMP-1 (digital pots and an active tone sheet), the JVM410HJS (a
  different main board with a THAT4301 gate in the path and different front-panel values: its own read). docs/07 lists
  what was found.
- **Library changes** (`NetworkStage`, additive): `StageNetlist.Amplifier`, an op-amp that never reaches its rails as a
  linear voltage-controlled source inside the adaptor (no device, no Newton; the DC solve stamps it), used for every op-amp
  whose signal stays inside its rails or whose clipping a diode clamp right after it masks (on loud noise, under the
  same load, the JCM900's channel A stage with its LEDs went from 2.8 to 0.5 µs a sample and the master stage from 3.4
  to 0.2 µs); and one device combination, an op-amp with a diode clipper (`DO`, the Valvestate's Normal IC2). The test
  reference appends an amplifier as a VCVS.
- **Validation**: whole preamp chains against the reference simulator (`Modern_marshall_chains_match_the_reference`):
  JCM900 channel A and B and the Valvestate's Normal channel below −30 dB, the Valvestate's boost channel −26 dB (the
  cut after its LED clamp), the JVM's OD1 and OD2 below −30 dB once its cuts sit at the next grids (at the gain pot's
  reactive wiper they were −21 dB). Knob sweeps at full drive converge with no NaN, every control is audible, gain and
  master are monotonic on both channels, peaks stay under +6 dBFS (`ModernMarshallTests`).
- **References**: the eight pelennor JCM900 4502 captures (Tim R, GPL) now map to the JCM900 4502 type (channel B's
  0–20 gain halved onto the knob; other knobs unstated, left at 5). Against the Plexi stand-in, pre-emphasised ESR /
  MRSTFT: channel A gain 4 0.603 / 1.006 → 0.296 / 0.783, 6 0.548 / 1.048 → 0.406 / 0.940, 8 0.565 / 1.195 →
  0.602 / 1.006, 10 0.937 / 1.215 → 0.826 / 1.107; channel B gain 8 0.869 / 1.169 → 0.458 / 0.869, 12 1.106 / 1.339 →
  0.625 / 1.072, 16 1.098 / 1.509 → 0.691 / 1.205, 20 1.187 / 1.373 → 0.788 / 1.341 (MRSTFT better everywhere, ESR
  everywhere but channel A at 8). With the M5201s as op-amp devices instead of linear amplifiers channel A was 0.07–0.33
  better in ESR, channel B the same; the difference was not traced. Channel A's fitted calibration still pins at −20 dBu
  (the unstated master and tone are the likely cause; no fit of them was run). No GPL direct-out
  capture of a Valvestate or a JVM410H was found (the pelennor JVM files state neither amp nor rig).
- **CPU** (48 kHz, 64-frame blocks, `Gain` 7, `Master` 6, guitar chord / loud noise, process CPU time, minimum of three
  passes on a machine with a load average of 150–350, where the Plexi read 9.1 / 15.3 %): JCM900 4502 channel A
  15.6 / 29.6 %, channel B 19.3 / 26.4 % (the 4100 the same), over the high-gain budget on guitar for B and on noise for
  A (the V1 pair with its 220k feedback around the stack is 6–10 % of it); Valvestate 8080 boost 15.3 / 24.2 %, Normal
  11.4 / 16.5 %; JVM410H OD 17.2 / 24.7 %, a little over on guitar.

### 3.3 Vox and class-A boutique

**AC30 Top Boost** — https://www.ampbooks.com/mobile/classic-circuits/vox-ac30/, https://www.voxac30.org.uk/vox_ac30_top_boost_circuit.html (JMI OS/010 drawing images on that site)
- First stage 12AX7: 220k plate, shared 1.5k‖25µ, 275 V [src]; 500k volume; normal coupling 0.047 µ; brilliant channel 100 pF bright cap [src].
- Top-boost gain stage unbypassed (about 30 dB) and CF (output about 612Ω) driving the stack [src].
- Stack: 1M-log treble and bass, no mid; maximum scoop about 480 Hz [src]; slope 100k, 10k, treble cap 50 pF (some say 56 pF), 22n bass and mid *(unverified)*. Read the JMI drawing to fix these.
- PI 12AX7 LTP, about 47k tail, 0.1 µ out [src]. 4 × EL84, **cathode bias 47–50Ω shared, no bypass cap mentioned** [src]; the current model uses a bypass: check. Plates 345 V, screens 320 V [src]. No NFB [src]. OT about 4k [src]. GZ34 *(unverified)*.
- AC30C2 (modern): 3 × 12AX7, 4 × EL84, master volume and tone cut [https://www.long-mcquade.com/GearHunter/901304/Vox-AC30C2.htm].

**AC15 (EF86)** — https://ampbooks.com/mobile/classic-circuits/vox-ac15/
- **EF86 pentode stage**: 220k plate, 1M screen resistor with 0.1 µ screen bypass, 2.2k‖25µ, plate about 90 V, 500k volume; 0.01 µ coupling, 250 pF in series with the brilliance switch open [src].
- PI LTP: 100k/100k, 220k leaks, 1.2k bias, 47k tail, 0.01 µ out, plates about 220 V [src].
- 2 × EL84 cathode bias, shared 130Ω, 100Ω screens; plates about 310 V; 22k dropper [src]. OT 8k p-p [src]. Rectifier (EZ81) *(unverified)*.

**Matchless DC-30** — https://ampbooks.com/mobile/classic-circuits/matchless-pentode/, https://www.vintageguitar.com/21904/matchless-dc-30/
- Channel 2 EF86: 330k plate, 2.2M screen resistor, 2.2k cathode (bypass value not given), about 320 V; **six-way switched coupling cap** as the tone control: 330 p / 560 p / 1.2 n / 2.2 n / 4.7 n / 10 n; 1M volume with 470k in parallel [src].
- Channel 1: paralleled 12AX7 into a top-boost-style stack; values not found [src].
- 4 × EL84 cathode bias, no NFB, GZ34, high-treble cut [src]. OT not found.

**As built (stream F3).** Read by eye from the JMI drawings OS/010 (top-boost unit, 1961) and OS/065 (AC30/6, 1960–64; https://www.voxac30.org.uk/vox_ac30_circuit_diagrams.html) and from a hand-drawn DC-30 trace (thetubestore `Matchless-DC30-Old-Schematic.pdf`, with measured supply voltages); each model file marks what is read, inferred or assumed.
- **AC30 (index 2, changed)**: the top-boost network is *not* a TMB stack. From the CF's cathode (56k): 50 pF to the treble pot (1M log, wiper out) and 100k to a node M; the treble pot's far end via 22n to M; M via 22n to the bass wiper with 10k to ground; the bass pot (1M log) from the treble pot's far end to ground [src: OS/010]. The top-boost gain stage is 100k with 1.5k‖25µ (bypassed on OS/010), from its own 290 V node; the Brilliant half of V1 couples through **500 pF** into its 500k log volume, with the unit's 100 pF across it. The channels mix through 220k each into the inverter's grid (47n, 1M bootstrapped): the inverter is the mixer. PI 100k/100k, 1.2k, **47k**; 0.1 µ out (OS/065 reads ".15"), 220k leaks, 1.5k stoppers; cut **250k log + 4.7 nF**; 4 × EL84 on **50 Ω with 250 µF** (bypassed on the JMI drawings; 10 V at idle), 100 Ω screens, GZ34. The old model's stack (TMB, linear treble, 10k at noon), 22k tail, 47n Brilliant coupling, 100k CF, 2.2 nF/1M cut and its added master were all off: the sound changes, intentionally. No master (hidden), Switch 1 = Normal channel (V1's other half, 47n into its 500k log volume).
- **AC15 (index 3)**: the EF86 channel (AmpBooks values) with the Brilliance switch on the panel's Bright, or (Switch 1) a 12AX7 channel. Assumed, not drawn: the mixing (as the AC30), the 12AX7 channel (the AC30's Normal channel), the cut (as the AC30), 1.5k stoppers, 50 µ cathode bypass, EZ81 with an 8 H choke. The EF86's Koren fit is redone so the stage sits where Vox measured it (plate 91 V, screen 105 V, cathode 2.41 V; AmpBooks 90/105/2.4) while keeping the datasheet's 3.0 mA / 0.6 mA point; the DC-30's EF86 then lands at 132 V / 67 V against AmpBooks' 125 V / 60 V.
- **DC-30 (index 4)**: channel 1 (paralleled 12AX7, 560 pF into a 500k log volume with 180 pF, a 100k/1.5k‖25µ stage, a 56k CF into a 56 pF / 100k / 22n / 22n network with 1M log pots, drawn with the bass wiper on its ground end, i.e. a TMB stack with its middle at zero; 220k into 100k) or (Switch 1) channel 2 (EF86 330k/2.2M/2.2k‖25µ, the six-way tone rotary on Control 1, 1M log volume with 180 pF and 470k). Each channel drives its own inverter grid (0.01 µ, 1M), 0.047 µ out, cut 250k log + 2.2 nF and the **master as a 1M rheostat across the grids** (`PowerAmpDesign.ShuntMaster`); 4 × EL84 on 62 Ω‖250 µ (the Hi position), GZ34, 350/338 V. Assumed: the OT (4k, the trace gives only the part number), the rotary's direction, the Hi/Lo wiring. The paralleled V1 is one triode of twice the current (`TriodeModel.Paralleled`). The three amps' cut pots are log tracks wired to come in early (`PotTaper.ReverseAudio`): a reading, not drawn.
- **Library changes**: a channel switch stage (two preamp chains, only the playing one ticks); a rotary capacitor selector in the network stage; a master across the inverter's outputs; an explicit cathode resistor for cathode-biased power amps; the EF86 refit. Two shared fixes: the long-tail pair's rest point was found by a fixed-point iteration that does not converge for a 47k tail (AC30: grids 1.8 V low; the 22k-tail Blackface and Plexi moved by a fraction of a volt, −77 dB on their render), now bisected; and the cut's filter now includes the grid leaks' loading (AC30 power amp −21 dB → −59 dB against the reference). The inverter's input network starts at its rest (grid current through the leak) instead of drifting from zero.
- **References** (knobs unstated, so fitted as settings; then bias ±15 %, OT ±20 %, B+ ±5 % gained nothing): `tl-matchless-sc30-ch1` against DC-30 channel 1, ESR (pre-emphasised) 0.977 → 0.332, MRSTFT 2.279 → 1.022, its calibration now fitted at 10.4 dBu instead of pinned at −20; `pel-ac15-topboost` against the AC30's top boost, 1.114 → 0.896 and 1.776 → 1.693 (no AC15 top-boost channel exists to compare). Far from ESR 0.05: both captures are darker than the schematics' amps. The T3K AC15C1 Normal sweep now maps to the AC15's 12AX7 channel (not fetched here).
- **CPU** (48 kHz, one core, under load): AC30 Brilliant 9.7 % guitar / 18.9 % noise, Normal 7.6 / 10.9; AC15 EF86 9.7 / 14.8, 12AX7 9.5 / 10.3; DC-30 channel 1 9.4 / 19.6, channel 2 9.7 / 16.0. The exact EF86 device is affordable as is; the block's worst case (`BlockCosts`) is now 19.6 %.
- Not modelled: the AC30's and AC15's vibrato/tremolo channels and the AC15 drawings (none readable), the DC-30's 5.1M pop-suppression resistors, the AC30's unbypassed-cathode variant (the JMI amps are bypassed). **Dumble ODS, Two-Rock, Bad Cat, Dr Z, Morgan: not built.** The ODS sources are forum traces behind logins, hand-drawn with acknowledged errors and give only the stack; the GPL references are a kit and a clone with unknown settings. A model from them would be a guess, not a circuit model; Two-Rock depends on it; the others have no schematic.

**Dr Z Maz 18** (tier 2) — 2 × EL84 cathode bias, 5AR4, 18 W, cut in the power section [src: https://www.thetubestore.com/maz-18-jr-nr-dr-z-package]. No schematic found.

**Bad Cat Hot Cat 30** (tier 2) — power amp: https://ampbooks.com/mobile/classic-circuits/bad-cat/; the whole amp with DC voltages: schematicheaven `newamps/badcat_hotcat_30.pdf` (stream F5, below). 2 × EL34 push-pull class A, **separate 270Ω cathode resistor per tube** (about 24 V, 75 mA), 1k screens, 376 V plates, no NFB; OT about 4k *(unverified)* [src].

**Dumble Overdrive Special** — ampgarage threads (hand-drawn, with known errors): https://ampgarage.com/forum/viewtopic.php?p=205801, https://ampgarage.com/forum/viewtopic.php?p=220309, stacks: https://ampgarage.com/forum/viewtopic.php?p=130645, https://ampgarage.com/forum/viewtopic.php?p=129816
- Topology: blackface-derived clean preamp into the stack; overdrive is extra cascaded stages taken from it; later amps have an HRM stack after the clippers.
- Stack, "pre-classic" [forum]: treble 500k-L + 510 pF, slope 100k, mid 250k-A + 0.01 µ, bass 1M-A + 0.1 µ, with 0.02 µ and 0.002 µ across bass-pot lugs and a 10k tail. "Classic" stack [forum, *unverified*]: 250k pots, 100k or 150k slope, 270 or 330 pF, 0.1 µ and 0.05 µ, Jazz/Rock switch.
- Clipper stage 2: 500 pF across a 220k grid resistor [forum].
- PI 12AT7 LTP, 2 × 6L6, SS rectifier *(all unverified)*. Treat this as the lowest-confidence tier-1 model; GuitarML's `DumbleKit_*_DirectOut` (§4.2) is a kit build, not an original.
- Superseded by a better source (stream F5, below): schematicheaven's `dumbleamps/` folder (https://www.schematicheaven.net/dumble.html) has a CAD redraw of a measured 1997 ODS 100 with each value marked confirmed or probable and DC voltages, the visit's notes, and 1970s ODS preamp, overdrive and power redraws (12AX7 LTP, EL34 on the 1997 amp, silicon rectifier).

**As built (stream F5, boutique and class A).** Four types appended to `AmpDesign.All` (26–29 before the merge); each file cites its drawing and marks what is read, inferred or assumed:
- **Chieftain** (Matchless Chieftain Reverb): the factory drawing (Sampson, 1994) and a hand trace of a real amp with voltages, both in thetubestore's `Matchless-Chieftan-Schematic.pdf`. V1 and V2 are each a paralleled 12AX7 (`TriodeModel.Paralleled`); between them a passive bass network (2.2 nF beside a 1M reverse-log pot and 10k) and a bridged-T middle notch (150k + 150k, 560 pF across, 10 nF and the 250k middle pot from the junction); the "treble" is a 4.7 nF + 1M high cut on V2's plate; 250k volume with 180 pF; 12AX7 LTP; brilliance (a Vox cut, 4.7 nF + 500k) and the master as a 500k rheostat across the grids (`ShuntMaster`); two EL34 on **separate** 270 Ω cathodes (`SeparateCathodes`), 415 V, no feedback, 5AR4. The bass pot is drawn so that it cuts bass as it turns up; it is wired to add bass (a new `falling` rheostat, below).
- **Hot Cat 30** (Bad Cat): a third-party CAD redraw with DC voltages (schematicheaven `newamps/badcat_hotcat_30.pdf`). Gain input: a **zero-bias** V1a (cathode grounded), gain pot, V2b (1.5k ‖ 100 nF), the Edge high cut (10 nF + 1M rheostat, Control 1), V2a, the Level pot (Control 2) into V3b (1k unbypassed), a DC-coupled CF into the AC30 top-boost network (1M5 bass), a master (220k into a 100k rheostat, which as drawn moves the level about 1 dB); or the Clean input (Switch 1). 12AX7 LTP, brilliance across the grids, two EL34 on separate 270 Ω, no feedback, GZ34 or (Switch 2) silicon. Not modelled: the panel's Mid-range switch (not on the drawing).
- **ODS 97** (Dumble Overdrive Special): the 1997 ODS 100 redraw above. V1B (220k, 3.3k ‖ 5 µF) drives a plate-fed "skyline" stack (330 pF, 150k, 0.1 µF / 10 nF; the bass pot with 1 nF across it into 9.8k), the volume, 220k ‖ 390 pF into V1A; clean master (47 pF bright, from the notes), or (Switch 1) the overdrive: V2A (180k), the Drive pot (Control 1), V2B (120k), a fixed Marshall-topology stack (330 pF, 33k, 22 nF, 22 nF; trimmers at half) and the OD Level (Control 2), returned in the master's place as drawn. 12AX7 LTP (91k / 110k, 820 Ω, 24k), four EL34 at 465 V, 1.73k OT (the notes' TF-170), feedback 4.7k into 390 Ω with the 2k presence pot and 1 µF, silicon. The drawing's V1A plate resistor (150k, marked confirmed) disagrees with its measured voltages (they fit 220k); the drawn value is kept.
- **Express** (Trainwreck): version A1a of schematicheaven's ten Express redraws (`newamps/trainwreck_express.pdf`), which its notes take as the most reliable ("from someone who has seen the inside of a real Express"); hearsay, not a measured trace. A Marshall-style stack on V1's plate (500 pF, 1M treble, 100k, 22 nF, 22 nF), the volume with the three-way bright switch (Bright = 500 pF, Switch 1 = 100 pF "Clarity"; Fractal's notes confirm off / 100 pF / 500 pF), V2, 2 nF into a cold V3 (10k unbypassed), 0.1 µF into the LTP with no grid stopper, two EL34, 6.6k OT (the drawing's "similar to Hammond 1650H"), feedback 100k from 8 Ω into the 5k presence pot. Node voltages below 410 V are estimated.
- **Library change**: `StageNetlist.Rheostat(..., falling: true)` puts the pot's other part in circuit, so turning the knob up takes resistance out (`PotPart.Upper` on its own). No other shared code changed; `CircuitAmpBlockTests`' idle-dissipation test now allows cathode-biased EL34 pairs 1.5 × the rating (both class-A drawings measure 24 V across 270 Ω, about 31 W a tube).
- **Validation** (`BoutiqueAmpTests`): the front ends as whole chains against the reference simulator (Chieftain V1–V2 −51 dB; Hot Cat V1a–V3b −33 to −35 dB; ODS V1B–V1A −36 dB, with the overdrive's V2A −26 dB; Express V1–V3 −37 dB). Two cuts were moved to get there: the Chieftain's tone networks sit in V2's stage (a cut at V2's grid was −22 dB: the networks' impedance is reactive), and the Hot Cat's V2b stage ends on V2a's grid (V2b's partly bypassed cathode doubles its output impedance below 1 kHz and the Edge network loads it; a cut at its plate was −9 dB; one circuit with both triodes was exact but cost 19.5 % on guitar). Operating points against the drawings' voltages: within 10 % (Hot Cat V2b 157 / 145 V, V2a 232 / 225 V, V3b 199 / 193 V; ODS V1B 206 / 210 V, V2A 232 / 228 V; Chieftain 220 V with the inferred 330 V node), except the Hot Cat's zero-bias V1a (88 / 99 V) and the ODS's V1A (232 / 206 V, the drawing's own inconsistency).
- **References**: the five GuitarML Dumble captures now map to the ODS 97 (the clean and kit-low ones to the clean channel, the rest to the overdrive), their unstated knobs fitted (ToleranceFit, four knobs each): ESR (pre-emphasised) / MRSTFT 0.84 / 1.45 (clone clean), 0.64 / 2.08 (kit low), 1.00 / 2.13 (clone medium), 0.99 / 1.93 (clone high), 1.08 / 2.49 (kit high), from 0.86–1.15 / 1.76–2.57 at the starting settings. A kit and a clone of unknown circuit against a traced ODS: no closer match should be expected without captures of a known circuit at known settings. No captures were found for the Chieftain, Hot Cat or Express.
- **CPU** (48 kHz, guitar / loud noise, best of several runs on a machine under heavy load, Plexi 8.9 / 15.0 % in the same run): Chieftain 9.5 / 15.6 %, Hot Cat 30 13.9 / 18.2 % (Clean input 7.5 / 11.4 %), ODS 97 clean 8.2 / 16.5 %, overdrive 11.4 / 19.3 %, Express 12.7 / 17.4 %.
- **Not built** (no reliable schematic: searched October 2026): Two-Rock, Fuchs ODS, Bludotone, Divided by 13, Budda Superdrive and Twinmaster, Dr Z, Morgan, Carr Rambler, Swart, Paul Ruby Rocket, Carol-Ann, Komet and the Trainwreck Rocket (DIY drafts behind the ampgarage login), the Bad Cat Black Cat. Found but not built: the Trainwreck Liverpool (a builder's drawing of two amps on schematicheaven), the 1970s ODS redraws, the Gibson Scout's factory GA-17RVT drawing (needs 6EU7 and 6AQ5 tables).

### 3.4 High gain

**Mesa/Boogie Mark IIC+ (lead)** — factory scan https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_mkiic_plus.pdf; annotated redraw https://cdn.boogieforum.com/attach/6/6850-Mark-IIC-Schematic-FINAL-1-.pdf; YA `Mark IIC+(++) Preamp_GEQ.schx`
- V1A [2-src]: 150k plate, 1.5k with 22 µ (15 µ on some) ‖ 0.47 µ, 1M grid leak.
- **Stack directly after V1A, Fender-style** [2-src]: treble 250k, bass 250k, mid 10k (fixed on some), slope 100k, treble 750 pF, bass 0.1 µ, mid 0.047 µ; Treble Shift adds 250 pF; Volume 1 1M with a 180 pF pull-bright.
- V1B: 100k, 1.5k‖15–22 µ. Lead drive 1M; 680k series resistor (1M on "++") into a cascade with a 3.3k cathode stage, 120 pF grid–cathode cap on V3A [src: https://music-electronics-forum.com/forum/amplification/guitar-amps/theory-design/10102-weird-cap-in-mesa-lead-channel?p=155804], lead-brightness 220k + 250 pF. Exact wiring *(unverified)*.
- **5-band GEQ** in the "to EQ" position after the preamp, before the PI: 80 / 240 / 750 / 2200 / 6600 Hz (the redraw labels 60 Hz); inductors 1.0 / 0.39 / 0.22 / 0.068 / 0.033 H, caps 3.3 / 0.47 / 0.22 / 0.15 / 0.033 µ, 50k sliders (YA). Pairing them in order gives 88 / 372 / 724 / 1573 / 4824 Hz, which does not match the panel: the topology needs reading from the factory scan *(unverified)*.
- PI LTP 82.5k/90.9k; NFB 100k with 120 pF (10k on non-EQ models); presence 250k.
- Simul-Class: 2 × 6L6 pentode + 2 × EL34 with screens to the plate (**triode mode**); 60/100 W versions 2 or 4 × 6L6; 2.2k stoppers, 470Ω screens; fixed bias −47 to −67 V; SS rectifier. OT unknown.

**Mesa/Boogie Mark IV (lead)** (tier 2) — https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_mkiv.pdf. Same V1A and stack as the IIC+; extra V3A stage (100k, 1.5k‖15 µ); lead master 100k; same GEQ; 85 W Simul-Class (4 × 6L6) [*unverified*, YA].

**Mesa/Boogie Dual Rectifier** — https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_dualrectifier.pdf, https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_3ch_dual_rectifier.pdf; analysis series https://warpedmusician.wordpress.com/2015/09/14/mesa-rectifier-design-concepts-pt-1-introduction-input-stage-and-clean-channel/ (and parts 2–3); revisions https://en.wikipedia.org/wiki/Mesa/Boogie_Rectifier
- V1A: 220k plate, 1.8k‖1 µ (corner about 95 Hz) [src].
- Channel 2/3 voicing into the gain pot (3-channel): Vintage/Modern 2.2M‖2.2M‖680k with 2 nF; Raw 1.1M with 82 pF; gain 250k with 1 nF bright [src].
- V2A: 100k, 1.8k with 1 µ in series with 100Ω; 475k stopper with 20 pF; 0.022 µ; 475k/1M divider [src].
- **V2B cold clipper**: 39k unbypassed; plate 100k (schx) or 330k (analysis): conflict [src].
- Next stage 220k, 1.8k‖1 µ; **CF** 100k driving the stack [schx].
- **Stack after the CF (post-gain)**: treble 250k, bass 1M, mid 25k, **slope 47k**, 500 pF, 22n, 22n; Modern adds 180 pF to the treble cap [schx; https://forum.pedalpcb.com/threads/boogie-monster-mods.8620/latest] *(unverified vs factory)*.
- 4 × 6L6, 100 W; rectifier switch: 2 × 5U4 or silicon; Bold/Spongy (a lower supply). Presence 25k *(unverified)*. OT unknown.

**Mesa Lone Star** (tier 2) — https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_lonestar.pdf. Mark-style stack after V1 (250k/250k/10k, 100k, 500 or 250 pF, 0.1 µ, 0.047 µ); 4 × 6L6 or EL34, 100/50/10 W modes; about 455 V, −53 V [MK schx, *unverified*].

**Soldano SLO-100 (overdrive)** — https://robrobinette.com/How_the_Saldano_SLO-100_Works.htm, https://ampbooks.com/mobile/classic-circuits/soldano-slo/, YA schx, https://music-electronics-forum.com/forum/amplification/guitar-amps/theory-design/28269-slo-100-grid-and-screen-resistors

| Part | Values |
|---|---|
| Stage 1 [2-src] | 220k plate, 1.8k‖1 µ (AmpBooks says 2000 pF: conflict), 68k stopper, 360 V |
| To stage 2 | 0.02 µ; 470k‖2 nF; OD gain 500k with 1 nF bright |
| Stage 2 | 100k, 1.8k with 1 µ in series with 100Ω; 0.02 µ; 470k/1M divider |
| Stage 3 cold clipper [2-src] | **39k unbypassed**, 100k plate with a **1 nF plate bypass**, 220k stopper, 330k leak |
| Stage 4 | 220k, 1.8k‖1 µ, then CF DC-coupled |
| Loop | send 100k/1k (about −40 dB); return stage 220k, 2.2k‖25 µ; second CF drives the stack |
| Stack [2-src] | treble 250k, bass 1M, mid 25k, **slope 47k**, 470 pF, 22n, 22n; master 1M pre-PI |
| PI | LTP 0.022 µ, 470Ω, 10k, 47 pF |
| Power | 4 × 6L6GC, 1k stoppers and screens; SS rectifier; NFB 39k from the 4Ω tap with presence and depth; OT 2.2k *(unverified, Robinette)* |

**Peavey 5150 / 6505 (lead)** — factory schematics https://www.thetubestore.com/peavey-5150evh-schematic, https://www.thetubestore.com/peavey-5150-ii-schematic; notes https://music-electronics-forum.com/forum/amplification/guitar-amps/theory-design/44578-high-gain-cold-bias, https://music-electronics-forum.com/forum/amplification/guitar-amps/theory-design/31446-peavey-6505-112-vs-5150-mk1. About five cascaded 12AX7 stages, then stack, CF and Post gain; cathodes 1.8k or 2.2k ‖ 1 µ except one **39k cold stage** (third of six); 0.002 µ into and 0.02 µ out of it [forum]. Stack values not found: read the factory scan. 4 × 6L6, B+ 485–500 V, fixed non-adjustable bias, 11–14 mA idle; resonance and presence values not found. The 6505 is electrically the 5150.

**EVH 5150III** — MK `EVH 5150 III 100S Stealth` schx (preamp and power amp) *(all unverified)*. Red: V1 220k, 1.8k‖1 µ, bright network 270/470/560 pF; gain 1M; 100k/1.8k‖1 µ; **100k / 5.1k unbypassed (cold)**; then 220k/1.8k‖1 µ, 220k/2.2k, 220k/2.2k; CF. Red stack: treble 250k, mid 25k, bass 1M, slope 47k, 470 pF, **47n bass**, 22n mid. Blue: 220k/1.5k‖1 µ then three 100k/1.5k stages; stack 250k/25k/250k, slope 43k, 470 pF / 0.1 µ / 22n. Power: 4 × 6L6GC, −48.6 V, 476–485 V; 82k/100k PI; 1.5k stoppers, 100Ω screens; presence 10k with 6.8 nF; NFB 39k.

**Bogner Ecstasy (red)** — no factory schematic; MK `Bogner Ecstasy 83K Mod` schx is a **modded** amp *(unverified)*: V1 100k, 820Ω‖0.68 µ; cascade with 4.7k / 10k / 1k cathodes; gain 500k; stack 250k/25k/1M, slope 33k, 470 pF, 22n, 22n. Stock 101B: 4 × EL34 [https://www.rig-talk.com/forum/threads/101b-preamp-tube-positions.144915/]. Depth control is inductive in the NFB loop (1 H / 0.5 H in the schx). Without a factory schematic this is a weak tier 1: consider swapping it for Hiwatt or the JCM900 if it stays unverified.

**Diezel VH4 (ch 3)** — MK `Diezel VH4 - CH3 [2000-2001]` and `[2011]` schx *(unverified, drawn from schematics of unclear provenance)*. 2000–01: V1 120k, 680Ω‖22 µ; gain 1M; 120k/764Ω‖2.2 µ; 120k/764Ω; **cold stage 120k/39k**; stack 250k/25k/1M, 470 pF, 22n, 22n. 2011: V1 2.2k‖47 µ; stack 200k/22k/500k, 560 pF. Power: 4 × EL34 stock; PI 120k/120k; presence 25k; **Deep** 10k with 0.1–0.22 µ in the NFB; 10k stoppers, 1k screens; about 463 V.

**Tier 2 high gain with no usable data:** Bogner Uberschall (6 × 12AX7, 4 × KT88 MkII or EL34, Depth/Presence/Density), Diezel Herbert (6 × KT77, Deep about 120 Hz, Presence above about 3 kHz [https://forum.fractalaudio.com/goto/post?id=1360452]), Engl Powerball (4 × 6L6GC, Depth Punch), Engl Savage 120 (6550/KT88), Revv Generator 120 (4 × 6L6GC), Splawn Quickrod, Suhr Badger. Carvin Legacy V3 has an MK schx only.

**As built (stream F4).** Six models, appended to `AmpDesign.All`: **SLO-100** (OD channel; Robinette/AmpBooks text plus the Yahiake redraw), **Mark IIC+** (lead channel; Yahiake redraw; graphic EQ in the EQ slot; Simul-Class), **5150** (lead; read by eye from the Peavey "EVH 5150" factory scan, which settles §3.4's open values: V1A 220k/1.82k‖1 µF; 470k‖2.2 nF into the 1M pre gain with 1M across it and a 1 nF bright; 470k into V1B; 470k/1M into the 39k cold clipper; 330k‖1 nF and 220k into V2B 220k; 1M and 22 nF into V5B 220k/2.2k with a 100k leak; V5A 100k/2.2k with 1M plate-to-grid feedback; **470k into a 33k (and 10 nF + 47k) pad ahead of a 47k/470 pF/22 nF/22 nF stack, 250k-B/1M-A/50k-B**; post stage V3A 68k with 3.3k‖4.7 nF; PI 82k/100k, 10k tail, 470 Ω, 4.7k shunt, 39k feedback; 2.2k stoppers, 100 Ω screens; presence 10k-A), **Dual Rectifier** (red channel; Yahiake redraw: the cold clipper's plate is 100k there; Modern/Vintage switch the cathode bypasses, the 680k‖2 nF voicing branch and the 180 pF treble cap; panel switches for a 5U4 pair against silicon and Spongy, via a new `SupplySwitchDesign`), **5150III** (red; MARKTHERENCE redraw, every value unverified), **Mark IV** (lead; Yahiake redraw). Each file marks its guesses (power sections are mostly typical values; no OT primaries were found). Not modelled: the Bogner Ecstasy (the only redraw is of a modded "83K" amp) and the Diezel VH4 ch 3 (two redraws of unclear provenance that disagree on V1 and the stack), the Carvin Legacy (tier 2, redraw only), the channels other than the lead ones (SLO normal/Crunch, 5150 rhythm). Library changes: a one-port Newton that cycles across a kink now bisects the bracket of its iterates (deep global feedback at hard drive, 5150 and 5150III, had 22–62 unconverged samples in the full-drive test); a network-stage pot built at an end stop no longer throws; supply switches on the panel. Validation: the first four stages (to the stage after the cold clipper) as one reference circuit with no Thévenin cuts agree to −23 to −30 dB (5150, SLO at 2 mV) and −60 dB (Rectifier); a cold clipper with a plate bypass cap feeding a stage that draws grid current is the cuts' weak spot (−17 dB SLO, −9 dB 5150III at 50 mV); a `TriodeStage`'s Thévenin resistance treats a partly bypassed cathode as grounded (−29 dB linear error into a high-impedance next stage; a network stage uses the 1 kHz impedance). Matching: no capture states its calibration, and the GuitarML ones pin the fit at −20 dBu, so unknown knobs were fitted (gain, master, bass, treble, bias within ±15 %); pre-emphasised ESR stays 0.85–1.2 and MRSTFT 1.0–2.2, nowhere near 0.15: the captures' settings, rigs and loads are unknown, and on clipped signals ESR mostly measures phase. Aliasing (signal to in-band aliases, 0.3 V sine): 21–24 dB at 2 kHz, 22–34 dB at 880 Hz, 38–72 dB at 110 Hz; 4 × rate gains only 7–13 dB for twice the cost, so everything stays at 2 ×. CPU (48 kHz, guitar performance / white noise, on the machine where Plexi is 9.9 / 14.3 %): SLO-100 17.7 / 20.8, Mark IIC+ 17.7 / 21.7, 5150 17.8 / 22.6, Dual Rectifier 14.6 / 19.4, 5150III 16.9 / 21.8, Mark IV 17.6 / 21.1 %.

**As built (modern US high-gain stream).** Two additions; every other amp of the stream has no usable schematic (docs/07 records what was searched for each). **Legacy VL100** (Carvin, Steve Vai; new type, lead channel with Switch 1 for the clean channel), read from Carvin's own vector schematic "Legacy 100W Tube Series" (PCB 30-32101 rev D, 1998/99, distributed by thetubestore), so nearly every value is a reading: V1B 220k/910 Ω ‖ 1 µF shared; lead: 2.2 nF into the 500k Drive (560 pF wiper to ground), V1A 220k ‖ 560 pF/1.5k ‖ 1 µF, 2.2 nF/1M/470k ‖ 560 pF/4.7 nF into V2B (68k leak; 150k ‖ 1.12 nF, 3.3k unbypassed), 470k ‖ (220k + 560 pF) and 2.2 nF into V2A (220k ‖ 560 pF, 2.2k ‖ 47 µF), then a **James (Baxandall-type) stack**, as Fractal also describes it: 100k into the 1M bass pot with 10 nF across its upper and 11.1 nF across its lower half over the 25k Mid rheostat, 100k from the bass wiper to the treble wiper, 250 pF into the 1M treble pot over 2.2 nF; 33 nF/47k into Volume 1, a preamp **Presence** (500k rheostat into 2.2 nF on Volume 1's wiper; Control 1); clean: the same stack with 4.7/22 nF, a Presence switch (330 pF across 470k; Switch 2; 8.8 dB at 10 kHz, the schematic says 9), Volume 2 (Control 2) into V3B (220k, 1.5k); both channels: a 180k/33k pad into the transistor loop buffer, V3A (100k, 910 Ω ‖ 10 µF), 100k into the inverter grid, a 100k/100k LTP with a 560 Ω + 44k tail, four EL34 (4.7k stoppers, 350 Ω screens, 25 mA idle) at 455 V. Guessed: the OT primary (1.7k) and supply source resistance. Simplified: the real feedback is shunt feedback into the inverter's driven grid (360k from the 8 Ω tap); here the 360k loads the preamp and the series feedback is sized to the schematic's "12 dB" (11.8 dB measured). With that, the return-to-speaker gain is about 12 dB above the schematic's "240 mV in for 25 V at 8 Ω", so the power section's open-loop gain is probably too high (the OT is unknown). DC: plates within 15 % of the printed voltages except V2B (printed 185 V, which 150k/3.3k cannot reach; 271 V here). **Cuts**: every plate has a cap across its load and V1B's cathode is partly bypassed, so the plates' output impedances are reactive; cut at V1B's plate the chain was −21 dB from the reference in the linear range, so V1B and V1A end at the next grid (−68 dB), while V2B ends at its plate so that V2A's 2.2 nF coupling cap charges under grid current (cut at the grid it was 9 dB worse driven hard). First four stages: −24 dB at 2 mV, −17 dB at 50 mV; clean chain −48 dB. **SLO-100 normal channel** (Switch 1, with Crunch on Switch 2 and the Bright switch), read from the 1996 CAD redraw on el34world (Soldano_slo100.pdf) and checked against Robinette's divider figures (Crunch 52 %, Clean 4 %, 13 % after the second stage): the Crunch switch measures 22.9 dB (Robinette's figures give 23). The redraw draws a short across the OD path's 470k ‖ 2 nF and a Bright switch that seems to replace the 470k with 470 pF; both are taken as drafting errors. The Soldano crunch capture still matches the OD channel at low gain better than the normal channel's Crunch (MRSTFT 1.94 against 2.10–2.50). Not built though legible: the 5150 rhythm channel (the factory excerpts lack the page with the relay states), the Soldano X88R and X99 (redraws of unknown origin). CPU (48 kHz, guitar / white noise, Plexi 9.8 / 14.6 %): Legacy VL100 lead 15.4 / 17.8 %, clean 9.9 / 14.0 %; SLO-100 normal 13.6 / 21.0 % (OD unchanged, 17.6 / 20.9 %).

**As built (stream F4b: Mesa, Bogner, Diezel, Engl).** Six types, appended to `AmpDesign.All`, four of them read by eye from factory drawings, which el34world hosts for Mesa (index: https://el34world.com/charts/Schematics/Files/Mesa_boogie/Mesa_boogie_Schematics.htm) and Engl:
- **Lone Star**: both channels from the Mesa drawing set (Dec 2003, LS1A1). V1B 100k/1.5k‖15 µF into each channel's Mark-style stack (250 pF, 100k, 0.1 µF, 47 nF; 250k-A/250k-A/10k-L) and 1M-A gain with 120 pF + 82k bright. Channel 2's drive stage (5 nF, 1M, 220k into the 1M-A drive, 220k leak; V1A 100k/1.5k‖15 µF) and its Thick cap (250 pF stack-bypass). Shared V2B, the reverb mixer (2.2M‖20 pF into 680k‖470k) and V2A, the master with a passive presence (1 nF into a 1M-A rheostat ‖ 120 pF). LTP 82k/100k, 470 Ω, 15k tail, 820 Ω/100 Ω feedback; 4 × 6L6 at 456 V; a 5U4G/silicon switch. Panel: Channel 2, Drive Mode and Tube Rectifier switches, Thick on Bright, Drive and Presence on Control 1/2.
- **Mark V**: channel 3 from the factory set (Mar 2009, MARKV-1). The supply sheet names the relays (M7 IIC+, M8 Mark IV, M9 Extreme): Mark IV mode adds a 4.7k pad at V6A's grid, IIC+ mode puts 4.4 µF on V6A's cathode in place of 15 µF. The JFET bypasses on V3A are not mapped to modes on the drawing; the model guesses IIC+ unbypassed, Mark IV 2.2 µF, Extreme 100 µF. Panel: IIC+ Mode and Mark IV Mode switches (both off is Extreme), Bright, channel master on Control 1, Output on Master, the graphic EQ.
- **Rect-o-Verb**: the lead channel of the factory "Solo 50 / Rectoverb" set (first series). It is the Dual Rectifier recipe with readable values: V1A 220k/1.8k‖1 µF, 680k‖2 nF into the 250k-A gain, V1B with 15 µF through 22k, a 39k cold clipper with 100k‖1 nF, V3A 220k, a CF into a 500 pF/47k/20 nF/20 nF stack, a preamp presence shunt (22k + 3 nF + rheostat) on the treble wiper; 2 × 6L6 at 454 V.
- **Triple Rectifier**: the Dual Rectifier model with six 6L6 (no Triple drawing exists online; forum service threads point to the three-channel Dual Rectifier drawing).
- **Powerball**: channel 2, Lo/Hi and Open/Focused, from the Engl drawing E645-1. V5A, V5B, V6A (330k plate) and V6B are all fully bypassed (very high gain: the chain clips below 1 mV at full gain). The stack has 470 pF (+470 pF Focused), 100k slope, a 220 nF bass cap and separate Open (22 nF, 20k) and Focused (47 nF, 20k) middles; a CF loop send and a return stage follow.
- **Savage 120**: the lead channel from the Engl drawing E610-2. V2B is 330k with 3.3k unbypassed unless Lead Boost; the stack is 470 pF (+220 pF Contour), 47k, 47 nF, 22 nF; 2 × 6550. Neither Engl drawing prints rail voltages, so B+ and the preamp rails are typical values.

Each file marks its guesses (mostly OT primaries, idle currents, and the Engl rails). Simplified feedback networks: the Mark V's (relay-switched presence per channel) and the Savage's (presence in series with the feedback) are folded into the standard shunt network.

Not modelled, with the reasons recorded in docs/07:
- Uberschall: an MEF trace (with measured voltages) and the el34world redraw disagree on the tone-stack topology and several caps.
- Ecstasy: only the modded 83K redraw and an OD-channel-only redraw exist.
- Diezel VH4: a 2005 Diezel factory sheet (milas.spb.ru, via the Wayback Machine) gives the full topology but no values, and an owner's trace gives only the stacks.
- Herbert, Shiva, XTC: no usable schematics.
- The Mark IIC+, Mark IV and Dual Rectifier rhythm channels: the factory scans show them; there was no time.

Library changes: none.

Validation: four-stage chains against the reference simulator with no cuts (`Mesa_and_Engl_chains_match_the_reference`):
- Savage: −87 dB at 2 mV, −8 dB at 50 mV.
- Powerball: −28 / −13 dB.
- Rect-o-Verb: −18 / −13 dB.
- Mark V: −19 / −20 dB.

These cascades already clip at 2 mV, so the error is the known Thévenin cut behind clipping stages. It is worst behind the Savage's unbypassed 330k stage and behind cold clippers with a plate cap. The two open issues (a `TriodeStage`'s output resistance with a partly bypassed cathode, and the cut behind plate-bypassed cold clippers) were left alone: converting the Mark V's partly bypassed V1B to a network stage changed its chain error by 0.2 dB, because the clipping stages dominate.

References: no free direct-out capture of these amps exists in a format the harness runs. The ToneLibrary "SmartAmp" Mark 5 file is a WaveNet JSON with unstated settings, and the pelennor "Engl Savage" file is an Axe-Fx capture, excluded.

CPU (48 kHz, default settings, guitar-like chord bursts / white noise, measured on a heavily loaded machine where Plexi read 10.4 / 16.0 % against its usual 9.9 / 14.3 %): Lone Star 11.6 / 22.1, Mark V 15.0 / 27.0, Rect-o-Verb 11.6 / 18.5, Triple Rectifier 11.6 / 19.8, Powerball 15.2 / 25.9, Savage 120 15.5 / 27.0 %. In the same run, Mark IV read 16.8 / 24.8 and Dual Rectifier 11.5 / 19.8.

**As built (stream D: Mesa and US boutique).** Seven types appended to `AmpDesign.All`, one existing model corrected; each file cites its drawing and marks what is read, inferred or assumed.
- **Dual Rectifier (corrected)** and **Dual Rectifier 3-Ch** (new), from Mesa's factory scans (el34world `Boogie_dualrectifier.pdf`, preamp RF-1F of 6-93 with the LDR table per mode; `Boogie_3ch_dual_rectifier.pdf`, the three-channel Solo Head set). **The Yahiake redraw the old model followed was wrong on the modes**: on the two-channel drawing both red modes fully bypass V1A, V2A and V3A (LDR3, LDR10 on) and keep the 680k ‖ 2 nF branch (LDR4 on); Modern and Vintage differ only in the global feedback (LDR19: on in Vintage, off in Modern) and a treble roll-off on the idle orange channel. The red treble cap is 680 pF in both modes, the cold clipper has 1 nF from plate to supply, the middle pot takes its 20 nF on the wiper, and the red presence is a preamp shunt (22k, 3 nF, 25k) on the treble wiper (now Control 2). The three-channel revision differs: 250k (not 1M) gain, 100 Ω under each 1 µF bypass, 475k stoppers, 500 pF + 180 pF in Modern (RY7a), Modern = no feedback (RY7b), Raw = no 680k ‖ 2 nF (RY6a, on the Bright switch). The Triple Rectifier now derives from the three-channel type (its own notes said Mesa built it on that preamp). Values: V1A 220k / 1.8k ‖ 1 µF; V2A 100k; V2B 39k / 100k ‖ 1 nF; V3A 220k; CF 100k; LTP 82k / 91k, 470 Ω, 10k + 4.7k, feedback 47k + 0.1 µF into 4.7k; 4 × 6L6 at 460 V (2-ch) / 475 V (3-ch), 1k screens, 1.5k stoppers, 220k leaks.
- **Subway Blues**: Mesa's factory set (preamp, power amp, supply, block diagram) read whole, except the potted "Toning Module" between the reverb mix and the loop buffer, which no drawing shows and which is left out (the 10 nF drives the buffer directly). A low-voltage preamp (168 V node): V1a 100k / 1.6k ‖ 3.3 µF into a Mark stack (250 pF + 750 pF Fat, 150k, 0.1 µF, 47 nF; 250k-A / 250k-A / 25k-L), the 1M volume (120 pF), V1b, V2a, a heavy reverb-mix pad, Q1 (an ideal buffer), V2b, into a long-tail pair with unequal plates (68k / 120k) whose tail returns to -15 V and whose feedback enters the second grid (56k ‖ (33k + 5 nF) over 1.6k), and two fixed-bias EL84 (-11 V) through 220k series resistors. The negative-tail inverter is mapped onto the block's ground-referenced pair with the same currents (15k tail, 1.2k bias, 350 V); the 33k + 5 nF is the feedback network's fixed shelf (`LowFeedback` 0.37 at 965 Hz).
- **TriAxis Rhythm / Lead 1 / Lead 2**: the factory audio sheets (rev 1.08, 1992) with the LDR switching matrix and its mode table, el34world `Boogie_triaxis.pdf`. Each type sets the LDRs its mode lights; Switch 1 Yellow and Switch 2 Red pick the voicing. Lead 2's overdrive (V4A / V4B) is the Mark IIC+ / IV lead circuit value for value. The "Dynamic Voice" is the Mesa graphic EQ with its sliders fixed: one TL072 (a network stage with an op-amp and five inductors), four LCR branches on the inverting side (89, 335, 1,570, 4,820 Hz) and one on the non-inverting side (720 Hz) to a common node that its LDR grounds: flat at 0, the "V" at 10 (Control 2). The LDRs' ranges are not printed; the Mark IIC+'s pot values are used (marked), the presence LDR is fixed (the block has no third free control), the bright cap printed "C75P" is read as 75 pF. Followed by a generic power amp: the Dual Rectifier model's (four 6L6GC, factory values) with its feedback on and no panel switches; Mesa's own TriAxis power amps are Simul-Class, but the Mark IIC+ model's Simul-Class stage cost about 3 % more.
- **ODS 70s** (Dumble, 1970s, Robben Ford era): schematicheaven's `dumbleamps/` preamp, overdrive, supply and output redraws (CAD, voltages, no stated provenance). V1 100k / 1.5k ‖ 5 µF into a plate-fed stack (330 pF to 250k-A treble, 100k, 0.1 µF to a 250k-A bass with 1.8k at its foot, 50 nF into a 100k-L middle rheostat) with **Rock/Jazz** (Jazz lifts the treble pot's foot onto 5 nF and ties the bass wiper to the treble wiper through 100k) and **Deep** (the 270k + 270k bass-wiper-to-volume-wiper path's junction to 1M ‖ 10 nF + 10k), the 1M volume with a 500 pF Bright, V2 with 22M + 50 nF plate-to-grid feedback; the overdrive is two 100k / 1.5k ‖ 5 µF stages with 500 pF plate to cathode, the Overdrive pot between them and an output trim (OD Level); 12AX7 LTP (100k, 820 Ω, 24k), two 6L6GC (not named on the drawing) at 457 V, 3.9k into 390 Ω with the Accent cap (2.2 µF) on the Presence knob. No HRM drawing exists, so no HRM type.
- **Liverpool** (Trainwreck): Mark Abbott's hand drawings of two amps (schematicheaven `newamps/trainwreck_liverpool.pdf`) with measured nodes: the Express front end with a 50 pF treble cap, V1b 2.7k ‖ 22 µF behind a 2.2k grid stopper, the "tuned" coupling (R7 68k, C8 2.2 nF, his recommendation), a cold V2 (10k), LTP (1M leaks, 1k bias, 39k + 4.7k), four EL84 on a shared 50 Ω ‖ 220 µF, 1k screens, 331 V.
- **Not built: the Bass 400** (Mesa, 1985 factory scan). Read: two inputs, V1A 100k with 1.5k ‖ 15 µF and V1B 150k, 1M volumes with 120 pF Bright switches mixed through 1.5M and 470k; V2A 100k with 1k ‖ 0.15 µF and Bass Shift (15 µF + 33k); a Mark stack (250 pF + 750 pF Treble Shift, 100k, 47 nF, 0.1 µF; 250k bass, 10k mid); V2B as a cathode follower (33k) with 3.3M bias, 0.1 µF and 15k into the 250k master; a discrete six-band graphic EQ (50k sliders, 470 Ω + 1 H + 22 µF, 1 H + 2.2 µF, 0.35 H + 1 µF, 0.22 H + 0.22 µF, 0.068 H + 0.15 µF, 680 Ω + 0.033 H + 47 nF) on four transistors; 6 × 6550 (or 6L6) at 540 V, 2.2k stoppers, 2.7k screens, -62 V. The driver is two differential stages in cascade (V3: 33k plates at 350 V, the tail 33k to -68 V; V4: grids on 470k / 220k dividers, a shared 470 Ω cathode, 0.1 µF into 100k bias feeds), which none of the library's inverters covers, and the volume mix's return (to V2A's cathode as drawn?) and V3B's 62k wiring are not readable.
- **Shared library changes**: (1) `PowerAmpDesign.FeedbackOffSwitch`: a panel switch that takes the global feedback out (Mesa's Modern); `CircuitAmpBlock` sets the feedback control to zero while it is on (two lines). (2) **A bug fix in `NetworkStage`** (and the reference circuit builder in the tests): a switched resistor whose *on* value is 0 was a short (1 µΩ), while the documentation (and the off value) treat 0 as an open contact; now 0 is open in either position. This changes two existing models whose authors relied on the documented meaning: the Mark V's V3A bypass (the 100 µF "Extreme" bypass was connected in every mode) and the Powerball's Lo/Hi and Open/Focused switches (both paths were connected when the switch was on), and the AB763 vibrato mixer (Bandmaster, Tremolux), whose Vibrato channel was shorted to ground and now plays (its two channels then differ only in their coupling caps, so `FenderUsAmpTests` allows -35 dB of residual for that switch). (3) `MesaParts.Stack`, the Mark plate-fed stack as a helper (Subway Blues, TriAxis).
- **Sharing with the Mark models**: the TriAxis stack, the Subway Blues stack and the Bass 400 stack are the Mark IIC+ stack (250 pF / 750 pF shift, 100k or 150k, 0.1 µF, 47 nF) and now share `MesaParts.Stack`; the Mark files keep their inline stacks (not touched). The TriAxis Lead 2 stages are the Mark lead stages with identical values, but each Mark model's lead netlist has its own switches (Shift, Lead Bright, the IIC+'s 680k drive feed), so they are not factored. The Mark-series **graphic EQ is not shared**: the Mark models use the behavioural `EqualizerStageDesign`. The TriAxis Dynamic Voice and the Bass 400 drawing show Mesa's real topology (slider ends on the op-amp's two inputs, the wiper through a series LCR to ground; the Bass 400's branches pair 1 H / 22 µF, 1 H / 2.2 µF, 0.35 H / 1 µF, 0.22 H / 0.22 µF, 0.068 H / 0.15 µF, 0.033 H / 47 nF), which settles §3.6 item 2's pairing question: a network-stage GEQ for the Marks could use it (not done here; the Mark IIC+ values in §3.4 pair the same way).
- **Validation** (`MesaBoutiqueAmpTests`): chains against the reference simulator with no cuts: Liverpool V1a-V2 -33 dB (2 mV and 50 mV), Subway Blues V1a-V2a -31 dB, TriAxis Rhythm V2A-V3A -42 dB, Lead 1 V1A-V3A -46 dB, Lead 2 V2A-V3A through the overdrive -19 dB (2 mV) / -13 dB (50 mV), ODS 70s V1-V2 -34 dB and through the overdrive -23 dB, Dual Rectifier V1A-V3A -16 dB and 3-Ch -16 dB at 2 mV (a cold clipper with a plate cap behind clipping stages: the known weak spot of the cuts). Panels, sweeps converging at full drive, every control audible, gain and master monotonic, levels below +6 dBFS (worst +2.8 dBFS, TriAxis Rhythm), and Modern taking the feedback out (+9 dB open loop on the 3-Ch).
- **References**: none. No free direct-out capture of these amps was found (GuitarML ToneLibrary, pelennor2170/NAM_models searched); the Dumble kit and clone captures stay on ODS 97.
- **CPU** (48 kHz, default settings, guitar-like chord bursts / white noise, best of three passes on a machine with a load average of 100 to 360, where Plexi read 9.1 / 15.2 %): Liverpool 10.8 / 16.8, ODS 70s clean 8.7 / 13.0, overdrive 12.3 / 17.9, Dual Rectifier 13.4 / 21.7 (Modern 13.3 / 21.7), Dual Rectifier 3-Ch 14.8 / 23.7, Triple Rectifier 13.9 / 24.1, Subway Blues 11.3 / 18.2, **TriAxis Rhythm 15.4 / 23.2, Lead 1 16.1 / 26.4, Lead 2 18.9 / 31.9 %: over budget** (Rhythm against the clean class, Lead 2 against high gain). Per stage at 96 kHz the Dynamic Voice (one op-amp, five LCR branches) costs about 2.4 % and V5A 1.2 %; the TriAxis runs eight stages (Lead 2) where a Mark runs six. Next: an ideal-op-amp element (the R-type adaptor already has VCVS ports) so that the Dynamic Voice is a linear network with no Newton device, or the Dynamic Voice bypassed at zero.

**As built (stream M2, power-amp driver variants, round 3).** Two types appended to `AmpDesign.All`, and three library
additions (new types; every existing inverter and power amp renders bit-identical, pinned by `InverterBitIdentityTests`):
- **Library**: `CathodyneDriverDesign` (a cathodyne whose plate and junction each AC-couple into one grid of a triode pair
  with a shared unbypassed cathode, no followers; outputs the pair's plates); `DifferentialDriverDesign` (a long-tail pair
  on a negative tail, each plate onto a coupling load and a divider into a second pair with a shared cathode); and
  `LoopStageDesign` on `PowerAmpDesign.Loop`: a common-cathode triode taking the feedback at the bottom of its cathode
  circuit, followed by a linear RC network with pots and switches (`StageNetlist`, resistors and capacitors), run inside
  the power amp's scalar Newton as a table plus a trapezoidal state-space whose output is affine in the sample's input,
  so the tone stack sits inside the loop with no delay. The loop stage inverts, so the feedback is taken with the other
  sign; the knobs reach it through `PowerAmp.ConfigureLoop` (called from `AmpChannel.Configure`) and the preamp's sag through
  `PowerAmp.SetLoopSupply` (per sample, from `AmpChannel.Tick`; the table scaled with the supply). Also `PotRole.Equalizer1`
  to `Equalizer5`: a network-stage pot turned by the block's EQ band gains (±12 dB as the slider's travel).
- **Major 1967** (Unicord 70-02-12): both V1 channels and the volume mix as one stage; V2A (bypassed 470 above the 470 Ω
  feedback shunt, 47k from the 16 Ω tap, 5k / 0.68 µF presence) and the stack (500 pF, 33k, 22 nF to the bass, 22 nF to
  the middle pot's *wiper*, 250k / 1M / 25k) in the loop; V2B cathodyne 100k / 2.7k / 100k; ECC82 47k plates, shared
  1.5k, 270k leaks (printed "270"); four KT88 ultralinear, 68k feeds, 5.6k stoppers. The Treble volume's 2.2 nF line is
  read as crossing the mix line (joined, the pot would move the level 2 dB). Guessed: B+ 600 V, the nodes, 40 mA idle,
  2.5k primary, 43 % taps. The feedback flattens the stack: treble 0 → 10 moves 3 kHz 4.4 dB in the loop, 9.5 dB open.
- **Bass 400** (Mesa 1985, rendered from the PDF with its page transforms, which made docs' unreadable parts readable:
  the volumes return to ground, V3B's grid and its 62k sit on the ground line): V1A / V1B with their volumes (Switch 1
  picks input 2); V2A + stack + V2B as one stage (V2B's grid biases itself by grid current from 3.3M and loads the stack:
  a Thévenin cut there was −13 dB off the reference); the six-band LC graphic EQ on a linear op-amp (five EQ bands and
  Control 2 for 4 kHz; ±19–21 dB at the extremes); V3 33k / 33k tail to −68 V, 62k and 470k / 220k into V4 (47k guessed,
  plates 279 V against the marked 276 V), shared 470 Ω; six 6550 at 45 mA (the
  library's 6550 then wants −67 V where the sheet marks −63 V; matching the mark would be 35 W a tube); no feedback.
- **Validation**: the driver tables against exact DC sweeps (`Cathodyne_driver_table_matches_the_reference_dc_sweep`,
  `Differential_driver_table_matches_the_reference_dc_sweep`: < 0.05 V wherever no grid draws current); whole power amps
  in the time domain with a stiff supply (`Driver_power_amps_match_the_reference_in_the_time_domain`: Major −32 dB with the
  loop closed, −38 dB open, Bass 400 −40 dB; the closed loop's residue is the presence branch, which the model leaves out
  of V2A's degeneration); the preamps as whole chains (`Driver_variant_preamps_match_the_reference`: Major −78 dB, Bass 400
  −46 dB). Knob sweeps at full drive converge with peaks below 0 dBFS (`DriverVariantAmpTests`).
- **References**: none found (GuitarML ToneLibrary, pelennor2170/NAM_models).
- **CPU** (48 kHz, best of three, Plexi 8.4 / 15.0 % in the same passes): Major 10.0 / 16.5 %, Bass 400 13.0 / 21.4 % (over
  the clean budget, within crunch: five triodes, a twelve-reactance EQ and six output tubes; a looser Newton tolerance
  did not help).

### 3.5 Bass and solid state (tier 2)

- **Ampeg SVT**: 300 W, 6 × 6550 (6146B in 1969–70) in two groups of three, each driven by a DC-coupled 12BH7 CF that also sets the bias [https://en.wikipedia.org/wiki/Ampeg_SVT; https://www.talkbass.com/threads/ampeg-svt-1979-power-amp-problem.519028/]. B+ about 660 V and OT impedance *(unverified)*. Needs CF-driven grids, 6550 tables, and a bass speaker load (8 × 10).
- **Roland JC-120**: 2 × 60 W solid state, 2N3773 outputs on 1970s units [https://music-electronics-forum.com/forum/schematic-requests/23735-roland-jc-120-serial-410546]. Not a tube circuit; model as a clean op-amp/transistor preamp with its EQ and a near-ideal power amp; the chorus belongs in the Chorus block. Low priority for the circuit tier.

**As built (stream B, bass and solid state).** Three types appended to `AmpDesign.All`:
- **SVT** (channel one of the 1970 6550 SVT): Joe Piazza's redraws of the 1972 preamp and 1970 power amp (drtube.com, schematicheaven) read by eye. 12DW7 pins 6-8 are the 12AX7 section. V1A 220k / 3.3k, the 12AU7 follower, the Bass Cut / Ultra Lo network (Bass Cut a 2 nF + 2 nF bridge; Ultra Lo a 150k / 2 nF / 68k twin-T near 530 Hz, read with the 820k shorted), the 1M volume with its 500 pF Ultra Hi cap, V3A (12AU7, 47k), a passive Baxandall, the **midrange loop** (V3B and V4A, 12AX7, into the V4B follower, whose 56k returns to V3B's cathode; the Mid-Range pot between the two ends of the loop with a series LC at its wiper, tuned to the manual's 220 Hz / 800 Hz / 3 kHz by a selector), the 6C4 follower and the power amp's input diode clamp. Power amp: a new **`DriverChainDesign`** inverter (12AX7 input stage taking the feedback at its cathode, a 12AU7 cathodyne, 12BH7 voltage amps and 12BH7 followers DC-coupled through 47k to the grids; 12BH7 on 12AU7 curves), 6 × 6550 at 680 V with 350 V screens from their own winding (stand-in below), 47k ‖ 120 pF / 220 Ω feedback. Guessed: preamp node 300 V, the power amp's small-signal nodes, the OT (2k), the feedback tap, the midrange inductor (3.5 H, 300 Ω). Ultra Lo notches about 18 dB where the manual says 10. Channel two is not modelled.
- **Portaflex B-15N** (1968, channel one): Piazza's redraw; V1 6SL7 (on 5751 curves: the library has no 6SL7) with Ultra Lo / Ultra Hi, a passive Baxandall, the 270k mix into a new **`FloatingParaphaseDesign`** inverter (120k plates, 470k / 510k divider into the second grid, shared bypassed 1k, 220 Ω where 10k of feedback from the 16 Ω tap enters), 2 × 6L6GC at 450 V, −48 V bias (the drawing: −50 V), 5AR4, OT-214 6.5k (Ampeg's 4/69 service manual via MEF). The paraphase's large cross output impedance (plate B moves −36k per ampere drawn from plate A) is left out: with it the power amp's Newton cycled under hard drive.
- **JC-120** (1984 board, effect channel with the effects off; Roland's own circuit diagram, schematicheaven): IC2b, IC2a, a Fender-type stack with the Hi-Treble path, IC3a, the IC3b pad and follower (an ideal buffer: it cannot clip), IC1a, and one channel of the class-AB power amp. Roland's signal-voltage annotations are reproduced stage by stage within 1 dB except across the tone stack: the drawn stack loses 15 dB at 1 kHz with every control up where the annotations imply 7 dB (unresolved: either the annotation or my reading of the stack is off).

Library changes (all additive):
- `NetworkStage`: three triodes in one stage (`TTT`); **op-amps** in a netlist (`StageNetlist.OpAmp`, the existing `Wdf.OpAmp` macro-model on regulated rails; one or two per stage); a per-stage Newton iteration limit (`NetworkStageDesign.MaxIterations`, the SVT's loop needs 128 at hard drive). The device factory now takes the sample rate (op-amps have a pole).
- `PhaseInverter`: the table's span is per inverter (`Range`, 60 V as before; 16 V for the SVT chain, whose gain of 100 needs a finer grid); new `DriverChainDesign` / `DriverChain` and `FloatingParaphaseDesign` / `FloatingParaphase`.
- `PowerAmpDesign.ScreenSupply` (default 0, as before): the screen node's idle voltage for the stock bias where screens run far below B+ (the SVT).
- **Solid-state power amp** (`SolidStatePowerDesign`, `SolidStatePowerAmp`; `AmpDesign.SolidState`): a one-pole voltage amplifier with feedback (closed-loop gain, its DC corner), a class-AB output stage of Ebers-Moll junction stacks with emitter resistors into the nominal load, unregulated rails that sag through their source resistance; one scalar Newton per sample, bracketed. Its output impedance is milliohms, so the speaker settings do nothing. Such a model sets `Power` to `AmpDesign.SolidStateTubePlaceholder` (built, never run) and `AmpPanel.HasTubes = false`, which hides the tube-only parameters (bias, feedback, tubes, transformer, speaker, rectifier, presence/depth frequency, preamp bias/sag, bright cap); Sag, Variac and B+ Time Const act on the rails; Power Amp off outputs the preamp scaled to the power amp's input.
- `BufferStageDesign`: an ideal unity follower between two networks.

Tests: `BassAmpTests` (panels, sweeps converging, volume monotonic, the SVT's Ultra switches and midrange against the manual, the B-15's switches, the JC-120 against Roland's annotations, the solid-state amp's load independence, sag and clipping), and the three preamps as whole chains against the reference simulator (−37 to −42 dB). CPU (48 kHz, 64-frame blocks, guitar chord / loud noise, a quiet machine): B-15N 7.6 % / 10.2 %, JC-120 11.0 % / 16.0 %, **SVT 23.4 % / 44.6 %, over budget**: its midrange loop (three triodes and four reactances in one six-port Newton solve) is 60 % of it. Next for the SVT: a cheaper loop (one triode linearised, or the resonant network folded into the adaptor) and channel two; captures with settings for any of the three.

**SVT CPU pass (stream P; §5.3 has the general findings).** Interleaved with the old code, minimum of three passes, thread CPU time on a loaded machine where the Plexi read 8.4–8.5 / 14.5–14.9 %: **24.0 / 44.8 → 13.9 / 28.8 %** at the default panel (1.64 / 1.93 × the Plexi), and 23.6 / 57.6 → 13.7 / 37.0 % with Ultra Hi, Ultra Lo and Bass Cut on. Class: high gain (16 / 28): inside on guitar, at the noise budget within the machine's spread (28.0 % scaled to the old Plexi figure). The midrange loop went from 13–14 / 25 % to about 4 / 11 %: the cheaper multi-port Newton (bit-exact), chord steps on the last factorisation (`ReuseFactors`) and a step tolerance of 1e-5 in every preamp stage; the power amp gains from the one-port safeguard. Residual against the old renders (both switch sets, five knob settings and three midrange settings, DI, hot DI, chord, noise, sweep): **−100 dB** at worst. What is left: with the Ultra switches on, loud noise drives the loop into samples that take 9 to 70 Newton iterations (a grid jumps a hundred volts positive in the first step and Newton wanders back); limiting the grid's rise turned those into cycles. Source stepping (splitting a struggling sample's input change in halves) is the next thing to try there.

### 3.6 Data gaps to close by reading scans by eye

Automated reading of schematic images failed in most cases. Before each model is built, someone reads the factory scan and fills its table. The highest-value reads:

1. Vox AC30 JMI top-boost stack values and the power-tube cathode bypass: read from OS/010 and OS/065 (stream F3, §3.3: the network is not a TMB stack; the cathode is bypassed).
2. Mesa IIC+ GEQ topology (inductor/cap pairing).
3. Mesa Rectifier stack and V2B plate value (100k vs 330k).
4. Peavey 5150 stack, presence and resonance values.
5. AB763 pot values and PI output caps.
6. Output transformer primary impedances: only the 2204 (3.2k), AB763 Deluxe (6.6k), Twin (2k), Super (4.2k), Bassman (about 4k), 5E3 (8k), AC15 (8k), AC30 (about 4k) are sourced. For the rest, transformer vendors' replacement catalogues (Mercury Magnetics, Heyboer, Classic Tone) list the primary of each "replacement for" part and are the best next source.

---

## 4. Reference data without hardware

### 4.1 Exact references (no licence issue)

| Reference | Use | URL |
|---|---|---|
| Yeh & Smith, "Discretization of the '59 Fender Bassman Tone Stack", DAFx-06 | Closed-form 3rd-order stack transfer function with 5F6-A values; check our stacks to machine precision | https://ccrma.stanford.edu/~dtyeh/tonestack/ |
| Duncan Amps Tone Stack Calculator | Fender, Marshall, Vox, Hiwatt and others; frequency response per knob setting | https://duncanamps.com/tsc/ |
| Dunkel, Rest, Werner, Olsen, Smith, "The Fender Bassman 5F6-A Family of Preamplifier Circuits — A WDF Case Study", DAFx-16 | Whole 5F6-A preamp as a WDF with R-type adaptor, validated against SPICE: the closest published work to our engine | https://www.dafx.de/paper-archive/2016/dafxpapers/37-DAFx-16_paper_53-PN.pdf |
| ACME.jl (Holters & Zölzer, EUSIPCO 2015), MIT | Independent nonlinear state-space simulator for cross-checking whole-amp netlists, besides our own MNA reference | https://github.com/HSU-ANT/ACME.jl |
| Koren, "Improved vacuum tube models for SPICE simulations" (1996) | Triode and pentode parameter tables (12AX7, EL34, 6L6, 6V6, KT88 …) | https://www.normankoren.com/Audio/Tubemodspice_article.html |
| Dempwolf & Zölzer, "A Physically-Motivated Triode Model", DAFx-11 | 12AX7 with grid current, fitted to measured tubes | https://dafx.de/paper-archive/details/MGMeIyM6_9oAzOgKCuzEqw |
| Cohen & Hélie, "Measures and models of real triodes", Acoustics 2012 | Measured 12AX7 curves | https://hal.archives-ouvertes.fr/hal-00811215 |
| Cohen & Hélie, "Real-Time Simulation of a Guitar Power Amplifier", DAFx-10 | Pentode/beam-tetrode Koren parameters; OT model from datasheets | https://dafx.de/paper-archive/details/yZNxN3FPA2HJUN5vKmCG0A |
| Mačák, PhD thesis, Brno 2012; DAFx-11 push-pull paper | Preamp and push-pull models; whether it has measured amp data is *(unverified)* | https://dspace.vut.cz/items/a3f364a1-8637-4d3d-bede-5928f142b99b, https://dafx.de/paper-archive/details/IV9AdFRgtfFewuNXM7JAfA |
| Yeh, PhD thesis, Stanford 2009 | DK method, tube circuit models | https://ccrma.stanford.edu/~dtyeh/papers/DavidYehThesisdoublesided.pdf |
| Werner, PhD thesis, Stanford 2016 | R-type WDF theory | https://ccrma.stanford.edu/papers/virtual-analog-modeling-of-audio-circuitry-using-wave-digital-filters |
| Pakarinen & Yeh, Computer Music Journal 33(2), 2009 | Review; no data | https://muse.jhu.edu/article/266412 |
| Rob Robinette and AmpBooks pages | Published DC operating points (plate and cathode voltages) per stage: check our operating points | §3 |

Measured real-amp datasets in the literature are scarce. Wright et al. (Applied Sciences 2020, CC BY article) measured a Blackstar HT-5 and a Mesa 5:50 Plus, but no public dataset was found; the Blackstar HT-1 pairs in https://github.com/Alec-Wright/Automated-GuitarAmpModelling (repo GPL-3.0, data licence not stated) are a small amp we do not model. Damskägg et al. (ICASSP 2019) used SPICE, not hardware.

### 4.2 NAM and other captures of real amps

Tone3000 (ToneHunt merged into it) is the main library. **Every tone page checked shows "License: T3K"**: users may use a file in software and publish the results, but may not redistribute the file without the author's permission. The Terms (https://www.tone3000.com/terms, 2025-03-12) also forbid bulk or systematic downloading and bundling. GitHub has two GPL-3.0 collections.

**Best candidates** (amp-only through a load box unless noted; knob settings as stated by the author):

| Amp | Capture | Licence | Rig | Settings | Value |
|---|---|---|---|---|---|
| Vox AC15C1 Normal | https://www.tone3000.com/tones/vox-ac15c1-normal-channel-115dbu-6153 (outmodedelectronics) | T3K | Amp only, Suhr Reactive Load, **calibrated 11.5 dBu** | **36 models: 9 volumes × 4 Cut positions** | Best sweep found; tests Cut and EL84 cathode-bias power amp. Note the C1's Normal channel is a 12AX7 channel, not the EF86 |
| Vox AC15C1 Top Boost | https://www.tone3000.com/tones/vox-ac15c1-top-boost-channel-115dbu-6154 | T3K | Same | 7 volumes *(unverified)* | Top-boost stack with EL84 × 2 |
| Fender Super Reverb 1964 (all original) | https://www.tone3000.com/tones/1964-fender-super-reverb-all-original-57914 (MaestrodiMusica) | T3K | Amp only, load not stated | **20 models**: Normal, Vibrato, Bright; V3–6, T4–6, M5–6, B3–5 | Best Fender set; true AB763 |
| Vox AC30S1 | https://www.tone3000.com/tones/ac30s1-head-only-10-pack-eq-neutral-55221 (jtmulcahey) | T3K | Amp only, line out across the real speaker | 10 models, Gain 1–10 | Gain sweep; the S1 is a budget single-channel AC30, not the JMI circuit |
| Fender '68 Custom Deluxe Reverb | https://www.tone3000.com/tones/fender-68-custom-deluxe-reverb-61037 (tannerc) | T3K | Amp only, real speaker load | 8 models, Vol 3/4/5/6/10 | Volume sweep; the '68 Custom is a modified AB763 (different voicing): use for power-amp breakup, not exact preamp |
| Fender '68 Custom Princeton Reverb | https://www.tone3000.com/tones/fender-68-custom-princeton-reverb-81456 (mayer763) | T3K | Amp only, Fryette PS-2A | 7 settings | Same caveat |
| Tweed Deluxe 5E3 (kit clone) | https://www.tone3000.com/tones/tweed-deluxe-5e3-75305 (madhatteramplab) | T3K | Amp only, Suhr RL | 2 free models, settings not listed (paid pack 17) | Weak without settings |
| Marshall 1959 Plexi Super Lead | https://www.tone3000.com/tones/-marshall-1959-plexi-super-lead--80875 (tonecast) | T3K | Amp only | 3 models, knobs not listed | Weak |
| Marshall JCM800 2203 (modified) | https://www.tone3000.com/tones/marshall-jcm800-2203-modified-el34-community-pack-44209 (2dor) | T3K | Power-amp output with a real 4 × 12 as load | Several | Modified amp |
| Marshall JVM410H ch 3 | https://www.tone3000.com/tones/marshall-jvm410h-channel-3-di-sample-pack-71032 | T3K | DI, Captor X+ | Low/mid/high gain *(unverified)* | Tier 2 |
| EVH 5150III (OB1 v2) | https://www.tone3000.com/tones/ob1-version-2-5150-iii-amp-only-nam-v2-capture-pack-72300 (Obijuan) | T3K | Amp only, Fryette PS-2 | 8 models, Blue/Red, **each with a pedal in front** | Usable only with that pedal modelled; weak |
| Soldano SLO-100 (6L6) | https://www.tone3000.com/tones/soldano-slo-100-6l6-community-pack-amp-head-64436 (2dor) | T3K | Head, real 4 × 12 load | not checked | Check settings |
| Mesa Mark IIC+ | https://www.tone3000.com/tones/mesa-boogie-mark-iic-36384 | T3K | Head *(unverified)* | not checked | Check |
| Bogner Ecstasy XTC (24 captures) | https://www.tone3000.com/tones/tl-bgr-ecstasy-xtc-82088 | T3K | *(unverified)* | 24 | Check |
| Hiwatt DR103 (1974) | https://www.tone3000.com/tones/hiwatt-custom-100-dr-103-1974-6233 | T3K | Head *(unverified)* | little detail | Tier 2 |
| **GPL** Mesa IIC+ Drive-knob conditioned | https://github.com/GuitarML/ToneLibrary `Mesa_IICplus_DriveKnob_DirectOut` | GPL-3.0 | Direct out | **Conditioned on the Drive knob** (one model covers the sweep) | Proteus LSTM JSON, not `.nam`: needs a small LSTM runner in the test harness (or RTNeural, BSD-3) |
| **GPL** Peavey 6505+ Red | ToneLibrary `6505Plus_Red_DirectOut` | GPL-3.0 | Direct out | one setting | Good for 5150 |
| **GPL** Matchless SC30 ch 1 | ToneLibrary `MatchlessSC30_Ch1_DirectOut` | GPL-3.0 | Direct out | one setting | SC30 is the single-channel sibling of the DC-30 |
| **GPL** Dumble kit low/high gain | ToneLibrary `DumbleKit_{Low,High}G_DirectOut` | GPL-3.0 | Direct out | 2 | A kit build, not an original |
| **GPL** Bogner Ecstasy Blue gain knob | ToneLibrary `Bogner_EcstasyBlue_GainKnob` | GPL-3.0 | *(unverified)* | Gain-conditioned | Check if direct |
| **GPL** JCM900 50 W Dual Reverb | https://github.com/pelennor2170/NAM_models (Tim R) | GPL-3.0 | not stated which files are DI | **Gain 4/6/8/10 (A) and 8/12/16/20 (B)** | Gain sweep for tier-2 JCM900 |
| **GPL** Fender Twin Reverb | pelennor (Tim R) | GPL-3.0 | not stated | Normal Bright, Vibrato Bright | Check DI |
| **GPL** 5150 Block Letter / 6505+ | pelennor (Helga B) | GPL-3.0 | not stated | green/red, with/without boost | 5150 |
| **GPL** Mesa Mark IV | pelennor (Roman A) | GPL-3.0 | not stated | — | Tier 2 |

Caution on the pelennor collection: it also contains captures of Axe-Fx presets (e.g. "Tom C Axe FX 2 Orange Rockerverb / Engl Savage"). Never use those: they are models of models, and matching them would clone Fractal's work. Contributor consent to GPL is not documented per file.

Not found as amp-only real-amp captures: JTM45 (full-rig only), Friedman BE-100, Diezel VH4, Engl, Orange Rockerverb, Ampeg SVT, JC-120, Matchless DC-30, Dumble original. The Bassman 5F6A "(Amp Only)" pack (https://www.tone3000.com/tones/fender-bassman-5f6a-series-amp-only-26378) is a capture of the author's own circuit simulation, not a real amp: a peer to compare against, nothing more.

### 4.3 Licence implications

| Licence | Local test reference, not committed | Committed to the GPL-3.0 repo |
|---|---|---|
| **T3K** (Tone3000) | Allowed for files fetched by hand. No scripted or bulk fetching (Terms) | **Not allowed** without each author's written permission |
| **GPL-3.0** (ToneLibrary, pelennor) | Fine | Allowed; keep notices and COPYING; prefer files whose contributor is identified |
| **CC BY 4.0 / CC BY-SA 4.0** | Fine | Allowed with attribution (FSF lists CC BY 4.0 as GPL-compatible; CC BY-SA 4.0 is one-way compatible with GPLv3: https://wiki.creativecommons.org/wiki/ShareAlike_compatibility:_GPLv3) |
| **CC BY-NC / ND** | Private use | **No** (non-free; GPLv3 forbids further restrictions) |
| None stated | Private use | **No** (all rights reserved) |

**Rule for the repo:** commit a **reference manifest** (`engine/tests/assets/references/manifest.json`): per capture the URL, author, licence, amp, settings, rig (DI or miked, load type), calibration level, and SHA-256. Test tools read the files from a local cache outside the repo (`~/.cache/cathode/references`, or `$CATHODE_REFERENCES`). Tests that need a missing file are skipped, not failed, so CI stays green; the matching dashboard runs on developer machines. GPL-3.0 captures may be committed if they are small, but keeping all references in the cache keeps one code path.

**As built (stream R).** `cathode-references` (`engine/tools/Cathode.References`) is the only code that touches the network: `fetch` downloads the GPL-compatible entries and checks their SHA-256; `status` lists the local-only (T3K) entries with the page, the model name and the file name to save it under. The harness itself is `Cathode.Testing.References`: a managed runner for GuitarML Proteus/NeuralPi LSTM JSON (checked against the AIDA-X runner and an independent numpy render), NAM through `cth_nam` (skipped when it is not built), input calibration in volts (NAM convention: X dBu is the RMS of a 0 dBFS sine; fitted per capture when unstated), latency and gain alignment, ESR (plain and pre-emphasised), MRSTFT (256/1024/4096), the harmonic profile of a ~200 Hz sine at 0.03–1 V, and the third-octave small-signal response. `match` prints a table and writes JSON/HTML to `engine/artifacts/references/`; `fit` runs `ToleranceFit` (Nelder–Mead over named physical tolerances, never EQ). `Cathode.References.Tests` runs every comparable reference in the cache against the targets in the manifest.

Using captures only to *measure* our models is not redistribution, and the resulting component values are our own work. We must not ship a model whose parameters were fitted so tightly to a T3K capture that it is effectively a copy of it; fitting a few tolerance parameters (below) is fine.

---

## 5. Recommended plan

### 5.1 Shared stage-library extensions (first, before the family streams)

These come out of §3. Each is a configuration or a small addition to `Cathode.Dsp.Amp`, validated against the reference MNA simulator as before.

| # | Extension | Needed by |
|---|---|---|
| L1 | **Tube tables**: 12AY7, 5751 (preamp); 6V6GT, 5881/6L6GB, KT66, KT88, 6550, KT77, 6146B (power); **EF86** pentode as a preamp stage (with its screen network) | 5F6-A, 5E3, Deluxe, Princeton, JTM45, AC15, DC-30, SVT, high-gain options |
| L2 | **Cold-clipper stage** behaviour: an unbypassed 10k–39k cathode with grid current from the previous stage and an optional **plate bypass cap** (1 nF on the SLO). The `TriodeStage` likely covers it already; it needs a test at the operating point and with grid conduction | JCM800, SLO, Recto, 5150, 5150III, VH4, Laney |
| L3 | **Grid current into cathode followers and the PI** (named as missing in Phase 7), plus a **CF not driving a stack** (Recto/SLO loop CF, Hiwatt PI bias CF, SVT grid drivers) | All high gain; Hiwatt; SVT; parity "emergent" params |
| L4 | **Cathodyne (split-load) PI** | 5E3, Princeton |
| L5 | **General passive network stage**: an arbitrary RC netlist with pots, solved as a linear WDF/MNA block (covers non-TMB stacks: Jubilee, Dumble, Hiwatt, Mark lead network, Recto voicing, Matchless 6-way cap) | Jubilee, Dumble, Hiwatt, Mesa, DC-30 |
| L6 | **Mark 5-band GEQ** with real inductors (or the general network with inductors) in a pre-PI slot | IIC+, Mark IV |
| L7 | **Diode and transistor clippers** inside the preamp (LED/1N4007 strings; MPSA06 "Sat"); **op-amp stages** reusing the drive catalogue's op-amp models | Jubilee, Friedman, JCM900 |
| L8 | **Power section variants**: cathode bias with separate resistors per tube (Bad Cat), unbypassed shared cathode (AC30 check), **triode-strapped pair plus pentode pair (Simul-Class)**, 6 tubes per side (SVT), Hi/Low screen switching (Jubilee), cathode-bias **bias tremolo** (Princeton) | Mark, Bad Cat, Jubilee, SVT, Princeton |
| L9 | **Rectifier models**: tube rectifier (GZ34, 5Y3, 5U4, EZ81) as a voltage drop rising with current plus its own resistance, versus silicon; **mains ripple** at 2 × line frequency; **variac** scaling; Mesa rectifier switch and Bold/Spongy | All; parity Supply Type, Variac |
| L10 | **Output transformer saturation** (core flux from the integral of primary voltage; hysteresis-lite) | Parity Transformer Drive; tweed and small amps |
| L11 | **Speaker impedance presets**: parametric curves for open-back 1 × 12, 2 × 12 combo, closed 4 × 12, 4 × 10 (Super/Bassman), 8 × 10 bass; LF peak, HF semi-inductance exponent | All; parity Speaker page |
| L12 | **Expert parameter plumbing**: per-model stock values with offset/multiplier overrides; model-specific panel controls (lead drive, jumpering, Fat, voicing switches); Input Trim, Low Cut, Power Amp Modeling off | All |

### 5.2 Streams

Six streams. **L** and **R** start first (about 4–6 weeks); the four family streams start as soon as the extensions they need land (Fender can start immediately on the existing library).

| Stream | Scope | Needs | Tier-1 models |
|---|---|---|---|
| **L — Stage library** | §5.1 L1–L12, each validated against the MNA reference | — | — |
| **R — Reference harness** | Manifest and local cache; NAM host in tests (already in the engine); a Proteus/RTNeural LSTM runner for ToneLibrary files; input-level calibration; ESR/MRSTFT dashboard; knob-sweep and operating-point reports; ACME.jl netlist cross-check | — | — |
| **F1 — Fender** | Deluxe, Twin, Super (refit of "Blackface" with reverb/vibrato path and V4B mixer), Princeton, 5F6-A, 5E3; tier 2 Hot Rod Deluxe | L1, L4, L9, L11 | 6 |
| **F2 — Marshall and British** | JTM45, Plexi (100 W, jumpering), JCM800 2203/2204, Jubilee, Friedman BE-100; tier 2 JCM900, JVM, Laney, Hiwatt, Orange | L1, L2, L3, L5, L7 | 5 |
| **F3 — Class A and boutique** | AC30 (fix the stack from the JMI drawing), AC15 EF86, DC-30, Dumble ODS; tier 2 Maz 18, Bad Cat, Two-Rock | L1 (EF86), L5, L8, L9 | 4 |
| **F4 — High gain** | Mark IIC+, Dual Rectifier, SLO-100, 5150/6505, 5150III, Ecstasy, VH4; tier 2 Mark IV, Lone Star, Uberschall, Engl, Diezel Herbert | L2, L3, L5, L6, L8, L9 | 7 |

Bass (SVT) and solid-state (JC-120) are deferred until the four families are done. Each family stream starts with the best-documented, best-referenced amp (F1: Super Reverb; F2: JCM800; F3: AC15; F4: SLO-100) so that its tooling is proven on an amp with good data.

### 5.3 CPU budget

Measured today: 8–11 % of one core on guitar, 19.8 % worst case (loud white noise), for 3–4 triode stages and a push-pull pair at 2 × the sample rate.

| Class | Typical (guitar) | Worst case (loud noise) |
|---|---|---|
| Clean and edge (Fender, Vox, 3–4 triodes) | ≤ 11 % | ≤ 20 % |
| Crunch (Marshall, 4–5 triodes, one cold clipper) | ≤ 13 % | ≤ 22 % |
| High gain (6–7 triodes, GEQ or loop, 4 power tubes) | ≤ 16 % | ≤ 28 % |

These are proposals, not measurements; they keep two Circuit Amp instances (the block's `maxInstances`) plus a cab and effects inside one core with headroom. If a high-gain amp goes over:
- run the stages before the first strong nonlinearity at 1 × rate only when tests show no audible aliasing;
- merge linear networks (tone stacks, GEQ, voicing nets) into the neighbouring root, as the tone stack already is;
- precompute 2-D tables for stages that see little B+ movement;
- offer a "lite" variant (Phase 7 R5 hybrid or a NAM capture) as the risk register already plans.

Every model gets a row in the existing benchmark, and the block's CPU estimate (Phase 8) reads from it.
`dotnet run -c Release --project tools/Cathode.Bench -- --amps [model ...] [--set index=value ...]` times models one by
one on the chord and on noise, with the Plexi in the same passes as a yardstick; on a loaded machine compare the ratios
to the Plexi, not the raw percentages.

**What the CPU pass over the SVT, Jubilee, Hiwatt and 18 W found** (stream P, all within −60 dB of the code before it):
- *Where a network stage's time goes.* Device evaluations are a small part (a FastTriode is about 20 ns); with three or
  more ports the linear algebra around them is most of it: building J = I − F (∂i/∂v − G_c), factoring it and solving.
  Bit-exact changes that paid: the system built over the devices' Jacobian blocks only (`INonlinearDevice.BlockStart`),
  F copied when the matrix changes instead of every sample, the last converged sample's factors reused for the next
  sample's linearised start, the unfactored system rebuilt only when `Constrain` needs it, `DenseLu` on references with a
  one-column solve (twice as fast at 6 × 6), and the supply rails of a network stage applied from a list (the sag moves
  them every sample). Together about a third off a six-port stage, every model rendering the same to the bit.
- *Chord steps* (`NewtonSolver.ReuseFactors`, opt-in per stage): try the last factorisation before building a new one,
  accepting the step only when it converges unlimited and |g| is already within ten tolerances. On guitar most samples of
  the SVT's loop then skip the factorisation. It moves the solution within the tolerance, so it is not on by default.
- *Looser tolerances* (`NetworkStageDesign.Tolerance`): 1e-5 instead of 1e-6 saves a fraction of an iteration a sample
  and stayed below −100 dB on the SVT; 1e-4 was −74 dB.
- *One-port safeguard* (`NewtonSolver.SafeguardAfter`, 8, library-wide): after eight plain iterations a step that would
  leave the bracket of the iterates bisects it instead, and the sample carries on rather than restarting in the general
  solver. Power amps under deep feedback cycled across the grid-conduction kink on a few samples in a hundred for 25 to
  60 iterations (the Hiwatt: 8.1 → 6.0 iterations a sample on noise). Every model stays within −139 dB.
- *What did not help:* limiting a triode grid's rise per Newton step (fewer wild iterates in the SVT loop but cycles
  instead), lower line-search thresholds, starting from extrapolation instead of the linearised step, and expanding a
  junction's downward steps (the Jubilee's LED string comes down one n V_t per iteration, but expanding overshoots).
- *An unconverged stage cannot be tuned against its own goldens.* The Jubilee's V1B leaves about 45 samples a second of
  loud noise unconverged at its 32-iteration limit (Boost); their fallback values differ by −30 dB from the converged
  solution, so any change to that stage's Newton path other than a bit-exact one moves the output by −30 to −40 dB.
  **Decision (October 2026): the Jubilee keeps its current sound** and stays slightly over the high-gain budget on loud
  noise (28–31.5 %; within it on guitar). Letting V1B converge, and then using chord steps there, would bring it in at the
  cost of that −30 dB change; revisit only if a capture shows the converged solution is closer to the real amp.

### 5.4 Matching method

Order of evidence, strongest first:

1. **Exact component checks.** Tone stacks against Yeh & Smith and the Duncan TSC presets (target: within 0.01 dB; we are at 0.001 dB). Whole preamp netlists against our MNA reference and ACME.jl. DC operating points against the voltages on the schematics and on Rob Robinette's and AmpBooks' pages (target: within 5 %, which is tube and component tolerance).
2. **Captures at known settings.** For each capture in the manifest:
   - Render the same test signal (NAM's standard v3 input file, or a sine sweep plus guitar DI phrases) through the NAM model and through our model at the capture's knob settings.
   - **Calibrate the input level**: use the capture's stated calibration (e.g. 11.5 dBu on the AC15C1 set; newer `.nam` files may carry input/output level metadata *(verify)*); otherwise fit one input-gain value per capture set, the same for all settings in the set.
   - **Align**: remove latency by cross-correlation; fit one output gain.
   - **Match the load**: compare with our speaker load set to what the capture used (reactive load box, real speaker, or resistive); correct for the load box's line-out response if it is published.
   - Score **ESR** (time domain, after a pre-emphasis filter as in Wright et al.) and **MRSTFT** (multi-resolution STFT, e.g. FFT sizes 256/1024/4096). Report per setting.
   - Fit only a few **tolerance parameters** within physical bounds: pot taper offset (the "5" on a real amp is rarely 50 %), triode µ and power-tube bias within ±15 %, B+ within ±5 %, OT primary within ±20 %. Record the fitted values in the model's notes; never fit arbitrary EQ.
3. **Knob-sweep sanity checks** (no reference needed): every knob monotonic where the real circuit is; no clicks or instability over full sweeps at 1 kHz and with noise; gain structure plausible (clean headroom on a Twin, breakup on a 5E3 at "4"); harmonic profile from a 1 kHz sine at several levels (even/odd ratio, crossover distortion on cathode-biased amps at low levels); sag shown by a tone-burst envelope.
4. **Listening**: an ABX panel against the captures (Phase 7 gate).

**Targets (proposal):** ESR ≤ 0.05 and MRSTFT within 1.5× of the NAM capture's own training error on clean and crunch settings; ESR ≤ 0.15 on high gain, where ESR is less meaningful and MRSTFT leads. A model ships when it meets the target at every captured setting in its set, plus the sanity checks. Where no capture exists, a model can ship as "schematic-only" with steps 1 and 3 done, and is labelled that way in the descriptor.

---

## 6. Summary of decisions to take

1. Adopt the **E/K/M/D/S** parity scheme above (component-value expert parameters, not behavioural ones).
2. Tier 1 is the 22 amps in §2; the Bogner Ecstasy and Dumble ODS stay tier 1 only if a better schematic turns up during stream F3/F4.
3. Reference files stay out of the repo; a manifest goes in.
4. Start streams L and R now; start F1 on the existing library in parallel.
