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
| Input Boost / Boost Type / Boost Level | on/off; pedal voicings; dB (0–20 *unverified*) | off | A built-in boost pedal | **S** in the amp: use a Drive block in front. Revisit for parity later |
| Saturation Switch / Saturation Drive | OFF / ON AUTH / ON IDEAL; 0–10 | model | The common "saturation" mod between preamp and stack (adds a clipping stage) | **E** later; needs a diode/transistor clipper stage (Friedman Sat uses MPSA06 + diodes) |
| Fat Switch | off/on | off | Moves the tone stack's centre frequency down | **E** (scale the stack's mid cap; Friedman has a real Fat switch) |
| Cut Switch | off/on | off | Low cut at the input | **E** (a smaller input coupling cap) |
| Preamp Tube Type | enum (12AX7 variants, EF86, …) | model | Triode characteristics | **E** (12AX7, 12AY7, 5751, 12AT7, 12AU7) |
| Bright Cap | pF | model | Bright-cap value | **E** (we have only on/off; make the value overridable) |
| High Treble | 0–10 | model | Extra treble control after the preamp | **S** use the EQ |

**Input EQ and Output EQ pages**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Input EQ filters (Type/Freq/Q/Gain) | — | flat | Parametric EQ before the amp | **S** use a PEQ block in front |
| Low Cut | Hz (about 10–2000 *unverified*) | model | Input high-pass ("tightening") | **D** one cheap first-order HPF before V1 |
| High Cut | Hz | model | Low-pass between preamp and power amp; deprecated in fw 33.00 b2 | **S** (deprecated upstream) |
| Definition | bipolar tilt | 0 | Tilt EQ at the input | **D** cheap; optional |
| Graphic EQ bands | dB (±12 *unverified*) | 0 | Built-in graphic EQ | **D** later; the Mark 5-band EQ must be **M** (it is part of the circuit) |
| EQ Type / EQ Location / EQ On | enum; INPUT, PRE PA, OUTPUT | OUTPUT | Number of bands; where the EQ sits | **D** later. PRE PA matters (the Mark EQ sits there) |

**Power Supply page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Supply Sag | 0–10 | model | Supply source impedance, i.e. B+ droop | **✓** Sag |
| B+ Time Constant | ms | model | Reservoir RC speed | **E** (scale reservoir C; already in `PowerSupplyDesign`) |
| Supply Type | AC / DC | model | Rectified AC with ripple, or ideal DC | **E** with rectifier ripple (not modelled yet) |
| AC Line Frequency | 50 / 60 Hz | 60 *(unverified)* | Ripple frequency | **E** global setting rather than per amp |
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
| Bias Trem Frequency / Depth | Hz / % | depth 0 | Real bias tremolo | **E** later (Princeton/Deluxe vibrato is bias-modulation in the Princeton; it is a real circuit feature) |

**Speaker page (impedance load and speaker nonlinearity)**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| LF Resonance Frequency | Hz (real speakers 50–150) | model | Bass impedance peak | **E** (exists as `SpeakerDesign.Resonance`) |
| LF Resonance Q | Q | model | Peak sharpness | **E** (`SpeakerDesign.Q`) |
| LF Resonance (amount) | dB | model | Peak height | **E** (`PeakImpedance`) |
| HF Resonance Frequency / amount | Hz (about 1–10 k) | model | Voice-coil inductance against transformer capacitance | **E** |
| HF Slope | 3.0–4.5 typical, about 3.7 median | model | Voice-coil semi-inductance (eddy losses) | **E** (we have a "lossy voice coil": expose its exponent) |
| Cabinet Resonance | % | model | Scales the cabinet part of the impedance curve | **S** for now; fold into LF amount |
| Speaker Impedance Curve | enum (82+ measured curves) | model | Measured load curves | **M** a small set of parametric presets (open-back 1×12, closed 4×12, 2×12 combo, bass 8×10). Do not copy Fractal's measured curves |
| Auto Dyna-Cab Impedance | off/on | — | Curve follows the Cab block | **S** (couples blocks; later) |
| Speaker Drive | 0–10 | model | Cone distortion | **S** belongs in the Cab block |
| Speaker Thump | 0–10 | 2.5 on reset | Subsonic cone nonlinearity | **S** Cab block |
| Speaker Compression / Speaker Time Constant | dB / ms | about 3 dB | Voice-coil thermal compression | **S** Cab block |
| Speaker Compliance | % | 50 % | Suspension nonlinearity | **S** Cab block |
| Transformer LF / HF | Hz | model | OT bandwidth (primary inductance; leakage and capacitance) | **E** (scale magnetising and leakage inductance in `OutputTransformerDesign`) |
| Transformer Drive | 0–10 | model | Core saturation | **E** after transformer saturation exists (not modelled yet) |

**Dynamics page**

| Parameter | Range | Default | Physical meaning | Cathode |
|---|---|---|---|---|
| Input Dynamics | bipolar | 0 | Input compander | **S** not a circuit feature |
| Output Compression / Type / Threshold / Clarity | — | 0 | Output compressor (Output, Feedback, Gain Enhancer) | **S** use a Compressor block |
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
| Level / Balance / Bypass | dB / L-R / bool | 0 | Common | **✓** Level; the rest are generic block features |

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

**Hiwatt DR103** (tier 2) — PI: https://ampbooks.com/mobile/classic-circuits/hiwatt-phase-inverter; stack (CP103): https://ampbooks.com/mobile/classic-circuits/cp103-tonestack/; https://hiwatt.org/tech2.html. PI is an LTP with **fixed bias from a CF** (220k cathode, about 70 V at the PI grids), about 87k plates, 2.2k bias, 22k tail [src]. NFB 10k from the 16Ω tap; presence 470Ω, 1000 pF, 0.1 µ; fixed treble boost 470Ω + 0.01 µ [src]. 4 × EL34, −36 V, about 496 V plates [src]. Whether the DR103 uses the CP103 stack is *(unverified)*.

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

**Bad Cat Hot Cat 30** (tier 2) — power amp only: https://ampbooks.com/mobile/classic-circuits/bad-cat/. 2 × EL34 push-pull class A, **separate 270Ω cathode resistor per tube** (about 24 V, 75 mA), 1k screens, 376 V plates, no NFB; OT about 4k *(unverified)* [src].

**Dumble Overdrive Special** — ampgarage threads (hand-drawn, with known errors): https://ampgarage.com/forum/viewtopic.php?p=205801, https://ampgarage.com/forum/viewtopic.php?p=220309, stacks: https://ampgarage.com/forum/viewtopic.php?p=130645, https://ampgarage.com/forum/viewtopic.php?p=129816
- Topology: blackface-derived clean preamp into the stack; overdrive is extra cascaded stages taken from it; later amps have an HRM stack after the clippers.
- Stack, "pre-classic" [forum]: treble 500k-L + 510 pF, slope 100k, mid 250k-A + 0.01 µ, bass 1M-A + 0.1 µ, with 0.02 µ and 0.002 µ across bass-pot lugs and a 10k tail. "Classic" stack [forum, *unverified*]: 250k pots, 100k or 150k slope, 270 or 330 pF, 0.1 µ and 0.05 µ, Jazz/Rock switch.
- Clipper stage 2: 500 pF across a 220k grid resistor [forum].
- PI 12AT7 LTP, 2 × 6L6, SS rectifier *(all unverified)*. Treat this as the lowest-confidence tier-1 model; GuitarML's `DumbleKit_*_DirectOut` (§4.2) is a kit build, not an original.

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

### 3.5 Bass and solid state (tier 2)

- **Ampeg SVT**: 300 W, 6 × 6550 (6146B in 1969–70) in two groups of three, each driven by a DC-coupled 12BH7 CF that also sets the bias [https://en.wikipedia.org/wiki/Ampeg_SVT; https://www.talkbass.com/threads/ampeg-svt-1979-power-amp-problem.519028/]. B+ about 660 V and OT impedance *(unverified)*. Needs CF-driven grids, 6550 tables, and a bass speaker load (8 × 10).
- **Roland JC-120**: 2 × 60 W solid state, 2N3773 outputs on 1970s units [https://music-electronics-forum.com/forum/schematic-requests/23735-roland-jc-120-serial-410546]. Not a tube circuit; model as a clean op-amp/transistor preamp with its EQ and a near-ideal power amp; the chorus belongs in the Chorus block. Low priority for the circuit tier.

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
