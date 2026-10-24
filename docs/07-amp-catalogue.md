# Amp catalogue

Every real amp we model, plan to model, or might model, with where it stands in the Circuit Amp block. Use it to find
**reference captures** (to match a model against) and **cab IRs** (to pair with it).

- **Amp captures** (NAM or GuitarML, *direct out / amp only*, no cab or mic) are what the reference harness matches a
  circuit model against. Note the knob settings and the input calibration (dBu at 0 dBFS) with every capture you find.
- **Cab IRs** go in the Cab block after the amp. The "Typical cab" column says what the amp is usually heard through.
- Add a capture you found to `engine/tests/assets/references/manifest.json` (see `docs/06-amp-modelling-spec.md` §4).
  Tone3000 ("T3K" licence) and CC BY-NC files are local-only: never commit them.

Last updated: 2026-10-24 (after the four family streams and the Prometheus merged).

## Status key

| Status | Meaning |
|---|---|
| ✅ Done | A Circuit Amp type exists and is in master |
| 🔨 In progress | A family stream is building it now |
| 📋 Planned | On the list (spec tier 1 or 2), schematic available |
| ⏸ Waiting | Wanted, but no reliable schematic yet: a circuit model would be guesswork. Find a schematic first |
| 💤 Not planned | Popular, not researched yet. Candidates for later |

"References we have" lists what is in the manifest now: **GPL** files are fetched by `cathode-references fetch`;
**T3K** files you download by hand (`cathode-references status` prints the pages and file names).

---

## Fender

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Blackface (Deluxe/Twin-style, generic) | ✅ Done | Blackface (0) | — | — | 1x12 / 2x12 open back |
| Deluxe Reverb AB763 | ✅ Done | Deluxe Reverb (12) | T3K: '68 Custom Deluxe (modified circuit) | Stock AB763 Deluxe, direct out, volume sweep | 1x12 open, Jensen C12K/C12N |
| Twin Reverb AB763 | ✅ Done | Twin Reverb (13) | GPL: Twin Normal Bright, Twin Vibrato Bright | Settings stated; Normal without Bright | 2x12 open, JBL D120 / Jensen C12N |
| Super Reverb AB763 | ✅ Done | Super Reverb (14) | T3K: '64 Super Reverb, 5 settings | More volume steps | 4x10 open, Jensen P10R/P10Q |
| Princeton Reverb AA1164 | ✅ Done | Princeton Reverb (15) | GPL: Princeton clean (year unknown); T3K: '68 Custom Princeton | Stock AA1164, volume sweep | 1x10 open, Jensen P10R |
| Bassman 5F6-A | ✅ Done | Bassman 5F6-A (16), Jumped (17) | — | Any 5F6-A / reissue, jumpered and single channel | 4x10 open, Jensen P10Q |
| Tweed Deluxe 5E3 | ✅ Done | Tweed Deluxe 5E3 (18), Jumped (19) | T3K: 5E3 kit clone | Volume sweep, both inputs | 1x12 open, Jensen P12R/P12Q |
| Hot Rod Deluxe | ⏸ Waiting | — | — | Readable schematic (tone stack unknown); captures | 1x12 open, Celestion G12P-80 |
| Champ 5F1 | 💤 Not planned | — | — | Schematic, captures | 1x8 open |
| Vibrolux / Vibroverb AB763 | 💤 Not planned | — | — | | 2x10 / 1x15 open |
| Bandmaster AB763 | 💤 Not planned | — | — | | 2x12 closed |
| Tweed Twin 5F8-A / High Power Tweed Twin | 💤 Not planned | — | — | | 2x12 open |

## Marshall and British

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Plexi 1959 Super Lead (50 W, generic) | ✅ Done | Plexi (1) | T3K: 1959 Plexi | Jumpered and single-input captures, volume sweep | 4x12 closed, Greenback G12M |
| Plexi 100 W / jumpered | ✅ Done | Plexi 100W (10) | T3K: 1959 Plexi | | 4x12 closed, Greenback |
| JTM45 | ✅ Done | JTM45 (9) | — | Any JTM45 / reissue | 4x12 or 2x12 (Bluesbreaker), Greenback / Alnico |
| JCM800 2203 | ✅ Done | JCM800 2203 (7) | T3K: 2203 (modified) | Stock 2203, gain sweep | 4x12 closed, G12T-75 / Vintage 30 |
| JCM800 2204 | ✅ Done | JCM800 2204 (8) | — | Stock 2204 | 4x12 closed, G12T-75 |
| Silver Jubilee 2555 | ⏸ Waiting (relay and diode networks need a closer read) | — | — | Rhythm clip and lead modes | 4x12 closed, G12T-75 / Vintage 30 |
| Friedman BE-100 | 📋 Planned (readable trace found; next Marshall-family model) | — | — | BE and HBE channels, Fat/Tight settings | 4x12 closed, Vintage 30 / Greenback |
| JCM900 4100 / 4502 | ⏸ Waiting (op-amp values unreadable in the scan) | — | GPL: JCM900 4502 channel A gain 4–10, channel B gain 8–20 | | 4x12 closed, G12T-75 / Vintage 30 |
| Laney GH100L | ✅ Done | Laney GH100L (11) | GPL: Laney GH100S ×3 | | 4x12 closed, G12H-100 |
| Hiwatt DR103 | ⏸ Waiting (partial data) | — | — | Normal/Brilliant, jumpered | 4x12 closed, Fane |
| Orange OR50 / Rockerverb | ⏸ Waiting | — | — | Reliable schematic (MkI only found) | 2x12 / 4x12 closed, Vintage 30 |
| Marshall JVM410H (OD) | ⏸ Waiting | — | — | Readable factory values | 4x12 closed, Vintage 30 |
| Splawn Quickrod | ⏸ Waiting | — | — | Schematic | 4x12 closed, Vintage 30 |
| Suhr Badger / SL68 | ⏸ Waiting | — | — | Schematic | 2x12 / 4x12 |
| Marshall JMP 2204 / 2203 (1970s) | 💤 Not planned | — | — | | 4x12 closed |
| Marshall 18 W (1974X) | 💤 Not planned | — | — | | 2x10 / 1x12 open |
| Marshall Major 1967 | 💤 Not planned | — | — | | 4x12 closed |
| Marshall DSL / JCM2000 | 💤 Not planned | — | — | | 4x12 closed |
| Marshall Bluesbreaker 1962 | 💤 Not planned | — | — | | 2x12 open |
| Hiwatt Custom 50 / DR504 | 💤 Not planned | — | — | | 4x12 closed, Fane |

## Vox, class A and boutique

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| AC30 Top Boost | ✅ Done (JMI OS/010, OS/065 values) | AC30 (2) | T3K: AC30S1 gain sweep | AC30 TB / C2 / HW direct out | 2x12 open, Celestion Blue / Alnico |
| AC30 Normal channel | ✅ Done | AC30 (2), Switch 1 "Normal Ch" | — | | 2x12 open, Blue |
| AC15 (EF86) | ✅ Done (mixing and cut assumed) | AC15 (3) | GPL: AC15 Top Boost; T3K: AC15C1 Normal (6 settings) and Top Boost | Vintage EF86 channel captures | 1x12 open, Blue / G12M |
| Matchless DC-30 | ✅ Done | DC-30 (4) | GPL: Matchless SC30 channel 1 | DC-30 both channels | 2x12 open, G12H30 / Vintage 30 |
| Dumble Overdrive Special | ⏸ Waiting (only partial, error-prone traces) | — | GPL: Dumble kit low/high, Dumble clone clean/medium/high | Real ODS or Two-Rock, settings stated | 1x12 / 2x12 open, EV12L |
| Two-Rock (Dumble-style) | ⏸ Waiting (needs the ODS work) | — | (Dumble captures above) | | 1x12 / 2x12 open |
| Bad Cat Hot Cat / Black Cat | ⏸ Waiting | — | — | Preamp schematic (only power amp found) | 2x12 open |
| Dr Z Maz 18 | ⏸ Waiting | — | — | Schematic | 1x12 / 2x12 open |
| Morgan AC20 / SW | ⏸ Waiting | — | — | Schematic | 1x12 open |
| Matchless Chieftain | 💤 Not planned | — | — | | 2x12 open |
| Divided by 13 | 💤 Not planned | — | — | | 1x12 / 2x12 |
| Budda Superdrive | 💤 Not planned | — | — | | 2x12 |
| Trainwreck Express | 💤 Not planned | — | — | | 2x12 / 4x12 |
| Supro (1960s) | 💤 Not planned | — | — | | 1x12 open |
| Gibson GA-40 Les Paul | 💤 Not planned | — | — | | 1x12 open |
| Magnatone (Custom 280 / 15) | 💤 Not planned | — | — | | 2x12 open |

## High gain

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Soldano SLO-100 | ✅ Done | SLO-100 (20): OD channel; Switch 1 the normal channel (Crunch, Bright) | GPL: Soldano crunch and high (model not stated); T3K: SLO-100 pack | Normal channel clean and crunch captures; OD gain sweep | 4x12 closed, Vintage 30 / G12T-75 |
| Mesa/Boogie Mark IIC+ | ✅ Done | Mark IIC+ (21), lead | GPL: IIC+ Drive-knob model, IIC+ Drive 8; T3K: IIC+ pack | EQ in and out, Lead Drive sweep | 1x12 / 4x12 closed, EVM12L / Vintage 30 |
| Peavey 5150 / 6505 | ✅ Done (lead); rhythm ⏸ | 5150 (22), lead | GPL: 5150 block letter, 6505+ red (two sources) | Rhythm channel: its network is on the factory "Initial Preamp Stage" page, but the relay states (K1, K2, K4) per channel and crunch setting are on a page the excerpts lack; a full factory set or a trace of the switching logic | 4x12 closed, Sheffield / Vintage 30 |
| EVH 5150III | ✅ Done | 5150III (24), red | T3K: 5150III OB1 v2 | Blue and red channels | 4x12 closed, G12EVH / Vintage 30 |
| Mesa/Boogie Dual Rectifier | ✅ Done | Dual Rectifier (23), red | GPL: Mini Rectifier (not the same amp) | Dual/Triple Rec, modern and vintage modes, tube vs SS rectifier | 4x12 closed, Vintage 30 |
| Bogner Ecstasy | ⏸ Waiting (only a modded-amp redraw) | — | — | Blue/red channels; a better schematic | 4x12 closed, Vintage 30 / Greenback |
| Diezel VH4 (ch 3) | ⏸ Waiting (redraws disagree) | — | — | Channel 3 and 4 | 4x12 closed, Vintage 30 |
| Mesa/Boogie Mark IV (lead) | ✅ Done | Mark IV (25), lead | GPL: Mark IV | Settings stated | 1x12 / 4x12 closed |
| Carvin Legacy VL100 | ✅ Done (factory schematic) | Legacy VL100 (26): lead; Switch 1 the clean channel | — | Any VL100 capture, settings stated (Vai: Drive 7.5, Bass 6, Mid 4, Treble 8, Presence 8) | 4x12 / 2x12 closed, Vintage 30 |
| Mesa/Boogie Lone Star | 📋 Planned | — | — | Clean/edge channels | 1x12 / 2x12 open |
| Bogner Uberschall | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Diezel Herbert | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Engl Powerball | ⏸ Waiting | — | — | Schematic | 4x12 closed, Vintage 30 |
| Engl Savage 120 | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Revv Generator 120 | ⏸ Waiting (nothing published: no schematic, trace or redraw found; specs only, 5 × 12AX7, 4 × EL34) | — | — | Schematic | 4x12 closed |
| Mesa/Boogie Mark V / Triple Rectifier / Rect-o-Verb | 💤 Not planned | — | — | | 4x12 / 2x12 closed |
| Fryette (VHT) Deliverance / Sig:X | ⏸ Waiting (Fryette releases no schematics; only a modified 2-channel D60 drawing as a PedalPCB forum attachment of unknown origin, and a "retro-engineered Deliverance" mentioned on MEF whose file was not found; nothing for the 120 or Sig:X) | — | — | Schematic or trace | 4x12 closed |
| Friedman Dirty Shirley | ⏸ Waiting (a hand-drawn layout and a corrected schematic exist as ampgarage forum attachments, login only; not read) | — | — | Read the ampgarage trace (viewtopic.php?p=397086); NAM captures of a Dirty Shirley Mini exist in pelennor2170/NAM_models | 4x12 closed |
| Cameron CCV | ⏸ Waiting (gut photos only; no schematic or trace found) | — | — | Schematic or trace | 4x12 closed |
| Bogner Shiva / XTC | 💤 Not planned | — | — | | 4x12 closed |
| PRS Archon | ⏸ Waiting (no service schematic found on PRS's site or elsewhere; specs only, 6 × 12AX7, 4 × 6L6 or 2 × 6CA7) | — | — | Schematic; the Clean/Lead/Bright variants | 4x12 closed |
| Krank Revolution | ⏸ Waiting (a reverse-engineered Rev 1 preamp and power amp are MEF attachments, thread 987426, login only; not read, and not factory) | — | — | Read the MEF trace | 4x12 closed |
| Victory Kraken / Sheriff | ⏸ Waiting (nothing found; Victory publishes none) | — | — | Schematic | 4x12 closed |
| Randall Satan / RG100 | ⏸ Waiting (Satan: nothing found. RG100: a legible factory scan of the solid-state RG80/RG100 on el34world, but it is 4558 op-amps and JFETs, which the stage library cannot build yet) | — | — | Satan schematic; op-amp and JFET network stages for the RG100 | 4x12 closed |
| Hughes & Kettner TriAmp | ⏸ Waiting (owner manuals only; a MkII service manual is mentioned on forums but not found) | — | — | Service manual | 4x12 closed |
| Wizard Modern Classic | ⏸ Waiting (an MCII schematic and turret layout by a forum member are ampgarage attachments, login only, viewtopic.php?p=398776; the thread text gives only the inverter and feedback values) | — | — | Read the ampgarage trace | 4x12 closed |
| Lichtlaerm Audio Prometheus (lead and clean channels) | ✅ Done, **topology inferred** (no schematic published) | Prometheus, Prometheus Clean | T3K: Prometheus TEASER-PACK (pipppriss; 5 captures, +18 dBu, knob settings not stated) | A schematic or gut shots; captures with knob settings | 4x12 closed, Mesa OS / Vintage 30 |

### Lichtlaerm Audio Prometheus: what is known and what is guessed

A current boutique amp (Lichtlaerm Audio, Berlin; prototype shown November 2023, shipping since 2024, about €2,900).
No schematic, layout or gut shot is published, so **both Circuit Amp types are plausible circuits, not copies**:
built from the documented features and fitted to captures.

- **Documented** ([maker's page](https://lichtlaermaudio.com/shop/prometheus), owners on
  [Rig-Talk](https://www.rig-talk.com/forum/threads/lichtlaerm-audio-prometheus.329616/) and
  [The Gear Forum](https://thegearforum.com/threads/new-amp-day-lichtlaerm-audio-prometheus.4898/), the
  [fan wiki](https://lichtlaerm-audio.fandom.com/wiki/Prometheus)): "2.5 channels". Clean channel with Warm (after a
  Verellen Meatsmoke) and Brilliant (Twin plus a touch of Hiwatt) voices, Bass/Middle/Treble/Volume. Lead channel with
  Gain, Bass/Middle/Treble/Volume, Vintage/Modern, a three-way Bite switch (high-mid bump / off / low-mid bump) and a
  Cut knob (a low cut before the gain stages). Global Master, Presence and Depth (power-amp shelves), a gate,
  a tube-buffered loop. 100–140 W, four EL34 stock, external bias, 4/8/16 Ω. Owners compare it with Engl, Marshall,
  Diezel, Driftwood, Wizard and the Rectifier.
- **Inferred** (`engine/src/Cathode.Dsp/Amp/Models/Prometheus.cs`, `PrometheusClean.cs`): the lead is a
  Soldano/Driftwood-style cascade (V1A, Cut network and gain pot into V1B with the Bite caps on its cathode, the cold
  clipper V2A, V2B, a cathode follower into a Marshall-type stack). Modern is a smaller coupling cap into the cold
  clipper plus a bypassed V2B cathode. The clean channel is Fender-style: V1A with the stack on its plate, the volume,
  V2A. Both run into a 12AX7 long-tail pair, four EL34 at 470 V and feedback from the 16 Ω tap. The triodes behind high
  source resistances carry their 1.7 pF grid-plate capacitance: the Miller roll-off was the largest single improvement
  against the captures. Every value is a guess within the usual ranges for this kind of amp.
- **Not modelled**: the channel volumes (folded into Master), the gate, the loop, the preamp-bypass ("Defeat") switch,
  the low-gain/high-gain lead option if it is separate from Vintage/Modern, and the line out.
- **Captures**: the Tone3000 [teaser pack](https://www.tone3000.com/tones/lichtlaerm-audio-prometheus-teaser-pack-100836)
  (NAM 0.7 "SlimmableContainer" WaveNets, which the pinned NeuralAmpModelerCore v0.6.0 runs at full size). It is
  calibrated at +18 dBu but states no knob settings, so the matched settings are fitted (see the manifest notes).
- **Match** (fitted knobs; ESR / pre-emphasised ESR / MRSTFT): clean edge 0.047 / 0.125 / 0.73 (before: 0.44 / 0.71 /
  1.81). Lead captures 0.31–0.45 / 0.64–0.81 / 1.14–1.28 (before: 0.43–0.51 / 1.03–1.21 / 1.27–2.32). The lead misses
  the 0.15 high-gain target: the fits push Middle and Cut to their ends. The real amp's saturated waveform is rounder,
  with more low-mid weight, than this cascade's. The likely cause is the preamp structure (stage count, the stack's
  position), which needs a schematic to settle.

## Bass and solid state

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Ampeg SVT | 📋 Planned (tier 2, later) | — | — | Direct-out captures, Ultra Hi/Lo switches | 8x10 closed |
| Roland JC-120 | 📋 Planned (needs a transistor/op-amp model) | — | — | Direct-out captures | 2x12 open |
| Ampeg B-15 | 💤 Not planned | — | — | | 1x15 closed |
| Fender Bassman 100 / 135 (bass) | 💤 Not planned | — | — | | 4x12 / 2x15 |
| Marshall Valvestate | 💤 Not planned | — | — | | 4x12 closed |

---

## Counts

| Status | Amps |
|---|---|
| ✅ Done | 24 amps (26 Circuit Amp types, 0–25) |
| 🔨 In progress | 0 |
| 📋 Planned | 5 |
| ⏸ Waiting on a schematic or better data | 20 |
| 💤 Not planned yet | 31 |

Match quality against the references is in `docs/06-amp-modelling-spec.md` (as-built notes per family) and in the
manifest's targets. No model reaches the spec's ESR targets yet except the Prometheus clean channel; references with
stated knob settings (the Tone3000 packs you fetch by hand) are the main thing that would tighten them.

---

## Added from the Axe-Fx III amp list (firmware 12.05)

Cross-checked against a community-made list of the Axe-Fx III's 275 amp models at firmware 12.05, built on 132 real
amps ([archive.axefx.fr](https://archive.axefx.fr/AxeFX%20III/Docs/FAS_Amps%20Models%20Axe%20III%20Ares12.05.pdf)).
Firmware 12 is old: the current Axe-Fx III has over 300 models, so amps added since are not in it. Fractal's own
"FAS" designs (FAS Modern, FAS Lead, FAS Brootalz, FAS Skull Crusher, FAS Bass and the like) are not real amps and are
left out, as are pure variants of amps already listed (for example "Big Hair", a JCM800 mod, or the Van Halen Plexi).

Every amp below was missing from the catalogue. The Axe model names are given so you can find captures of the same amp.

### Fender

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Fender Bassman AB165 (blackface) | 💤 Not planned | — | 65 Bassguy Bass / Normal | Both channels, bright on | 2x12 closed |
| Fender Concert 6G12 (brownface) | 💤 Not planned | — | 6G12 Concert | | 4x10 open |
| Fender Super 6G4 (brownface) | 💤 Not planned | — | 6G4 Super | | 2x10 open |
| Fender Tremolux AA763 | 💤 Not planned | — | Tremolo Lux | | 2x10 closed |
| Fender Vibro-King | 💤 Not planned | — | Vibra-King, Vibra-King Fat | Fat switch | 3x10 open |
| Fender Blues Junior | 💤 Not planned | — | JR Blues, JR Blues Fat | Fat switch | 1x12 open |
| Fender Princeton 5F2 / AA964 | 💤 Not planned (variants of the Princeton) | — | Prince Tone 5F2, Prince Tone AA964 | | 1x10 open |
| Fender Vibro-Champ (Eric Clapton) | 💤 Not planned (5F1 variant with bypassed cathode) | — | 5F1 Tweed EC | | 1x8 open |

### Marshall and British

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Marshall 1959SLP reissue | 💤 Not planned (Plexi reissue) | — | 1959SLP Jump / Normal / Treble | | 4x12 closed, Greenback |
| Marshall 1987X reissue | 💤 Not planned (50 W Plexi reissue) | — | 1987X Jump / Normal / Treble | | 4x12 closed |
| Marshall JMP 1959T Super Lead (Tremolo) | 💤 Not planned | — | Brit Super | | 4x12 closed |
| Marshall JMP-1 preamp | 💤 Not planned (op-amp/tube MIDI preamp) | — | Brit Pre, JMPRE-1 OD1 / OD2 (+ bass) | | 4x12 closed |
| Marshall AFD100 (Slash) | 💤 Not planned | — | Brit AFS100 1 / 2 | | 4x12 closed |
| Marshall JVM410HJS (Satriani) | 💤 Not planned (JVM variant) | — | JS410 Crunch / Lead | | 4x12 closed, Vintage 30 |
| Friedman Small Box | 💤 Not planned | — | Friedman Small Box | | 2x12 / 4x12 closed |
| Cornford MK50H II | 💤 Not planned | — | Corncob M50 | Overdrive channel | 4x12 closed |
| Orange A30 / AD30 | 💤 Not planned | — | Citrus A30 Clean / Dirty | | 2x12 closed |
| Orange Tiny Terror | 💤 Not planned | — | Citrus Terrier | | 1x12 / 2x12 closed |
| Orange AD200B (bass) | 💤 Not planned | — | Citrus Bass 200 | | 4x10 / 8x10 closed |
| Blankenship Leeds 21 | 💤 Not planned | — | Blanknshp Leeds | | 1x12 / 2x12 |
| Vox AC30HW (hand-wired) | 💤 Not planned (AC30 variant) | — | Class-A 30w, 30W Bright, 30w Hot | | 2x12 open, Alnico Blue |

### Boutique and class A

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Dumble ODS (HRM and Robben Ford versions) | ⏸ Waiting (see the Dumble ODS row) | — | ODS-100 Clean / HRM / HRM Mid / Ford 1, 2, Mid | | 1x12 / 2x12 open |
| Fuchs Overdrive Supreme | 💤 Not planned | — | Fox ODS, Fox ODS Deep | | 1x12 / 2x12 open |
| Two-Rock Jet 35 | 💤 Not planned | — | Two Stone J35 1 / 2 | | 1x12 open |
| Bludotone Ojai | 💤 Not planned | — | Bludojai Clean / Lead 1 / Lead 2 | | 1x12 / 2x12 open |
| Carr Rambler | 💤 Not planned | — | Car Roamer, FAS Class-A | | 1x12 / 2x12 open |
| Budda Twinmaster | 💤 Not planned | — | Buttery | | 2x12 open |
| Divided by 13 CJ 11 / FTR 37 | 💤 Not planned (see Divided by 13 row) | — | Div/13 CJ, CJ Boost, FT37 High / Low | | 1x12 / 2x12 |
| Dr Z Route 66 / MAZ 8 / MAZ 38 SR | 💤 Not planned (see Dr Z row) | — | Mr Z Hwy 66, MZ-8, MZ-38 | | 1x12 / 2x12 open |
| Morgan AC20 Deluxe | 💤 Not planned (see Morgan row) | — | AC-20 12AX7 / EF86, Bass / Treble | | 1x12 open |
| Bad Cat Hot Cat 30 | 💤 Not planned (see Bad Cat row) | — | Hot Kitty | | 2x12 open |
| Matchless Chieftain | 💤 Not planned (see Matchless Chieftain row) | — | Boutique 1 / 2 | | 2x12 open |
| Trainwreck Liverpool | 💤 Not planned | — | Wrecker Liverpool | | 2x12 |
| Trainwreck Rocket | 💤 Not planned | — | Wrecker Rocket | | 1x12 / 2x12 |
| Komet 60 / Concorde | 💤 Not planned | — | Comet 60, Comet Concourse | | 2x12 / 4x12 |
| Swart Atomic Space Tone | 💤 Not planned | — | Nuclear-Tone | | 1x12 open |
| Paul Ruby Rocket | 💤 Not planned | — | Ruby Rocket, Ruby Rocket Bright | | 1x12 open |
| Carol-Ann OD-2 / Triptik / Tucana 3 | 💤 Not planned | — | Carol-Ann OD-2, Triptik, Tucana | | 2x12 / 4x12 |
| Gibson Scout | 💤 Not planned | — | Gibtone Scout | | 1x10 / 1x12 open |

### High gain

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Mesa/Boogie TriAxis preamp | 💤 Not planned | — | USA Pre Clean / LD1 / LD2 | Lead 1 and Lead 2 modes | 4x12 closed |
| Mesa/Boogie JP-2C | 💤 Not planned (IIC+ reissue) | — | USA JP IIC+ Green / Yellow / Red | | 4x12 closed |
| Mesa/Boogie Triple Crown TC-100 | 💤 Not planned | — | Triple Crest 2 / 3 | | 4x12 closed |
| Mesa/Boogie Subway Blues | 💤 Not planned | — | USA Sub Blues | | 1x10 open |
| Mesa/Boogie Dual Rectifier, 2- and 3-channel revisions | 💤 Not planned (variants of Dual Rectifier) | Dual Rectifier (23) covers one | Recto1 / Recto2 Orange / Red, Thordendal | | 4x12 closed, Vintage 30 |
| Soldano X88R preamp | ⏸ Waiting (a legible single-sheet redraw, "SOLDANO X88R" 13 Jul 2002, on el34world, Soldano_x88r.pdf; its origin is unknown and owners doubt it; not built) | — | Solo 88 Clean / Rhythm / Lead | A second source to confirm the redraw | 4x12 closed |
| Soldano/Caswell X99 preamp | ⏸ Waiting (a legible scan of a hand-labelled drawing on el34world, Soldano_x99.pdf, no title block; Fractal says its rhythm channel is the SLO-100's; not built) | — | Solo 99 Clean / Lead | Confirm the drawing; a power amp to pair it with (Fractal uses the SLO's) | 4x12 closed |
| CAE 3+ SE preamp | ⏸ Waiting (a schematic of unknown origin is a GroupDIY attachment, thread 51582, which refused automated access; not read) | — | CA3+ Clean / Rhythm / Lead | Read the GroupDIY drawing | 4x12 closed |
| Bogner Fish preamp | 💤 Not planned | — | Bogfish Brown / Strato | | 4x12 closed |
| Cameron Atomica | ⏸ Waiting (descriptions only: a Super Lead with a Jose-style master and a diode-clipping switch; the Ceriatone Molecular is a clone, also without a schematic) | — | Atomica High / Low | Schematic | 4x12 closed |
| Hook Captain 34 | ⏸ Waiting (nothing found) | — | Capt Hook 1A–3B | Schematic | 4x12 closed |
| Splawn Nitro | ⏸ Waiting (nothing for the Nitro; Splawn says it has no schematics; a Quick Rod schematic circulates behind member-only sites) | — | Spawn Nitrous 1 / 2 | Schematic | 4x12 closed |
| Fryette D60 | ⏸ Waiting (see Deliverance: only a modified 2-channel D60 drawing of unknown origin, PedalPCB forum post 322300) | — | Fryette D60 Less / More | Schematic or trace | 4x12 closed |

### Bass

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Mesa/Boogie Bass 400 | 💤 Not planned | — | USA Bass 400 1 / 2 | | 8x10 / 4x10 closed |

Already in the catalogue and in the Axe list (no row added): Bassman 5F6-A, Champ 5F1, Tweed Twin 5F8, Bandmaster,
Tweed Deluxe 5E3, Deluxe Reverb, Twin Reverb, Super Reverb, Princeton Reverb, Vibrolux, Vibroverb, Supro 1624T, JTM45,
the Plexi 50 W and 100 W, JMP 2204, JCM800 2203 and 2204, Silver Jubilee, JVM410H, Friedman BE-100 and Dirty Shirley,
Hiwatt DR103, Orange Rockerverb, Splawn Quick Rod, Suhr Badger, AC15, AC30 Top Boost, Matchless DC-30, Trainwreck
Express, SLO-100, Mark IIC+, Mark IV, Dual Rectifier, Lone Star, 5150, 6505+, 5150III, Bogner Ecstasy, Überschall and
Shiva, Diezel VH4 and Herbert, Engl Powerball and Savage, Carvin Legacy, Cameron CCV, Ampeg SVT and B-15, Roland JC-120.

### Added from Fractal's current wiki list (October 2026)

Fractal's wiki page "Amp models" lists every model in current firmware (Axe-Fx III, FM3, FM9, AM4, ICONS): about 330
models. Against the catalogue above it adds these real amps:

| Amp | Family | Status | Cathode type | Axe models | Typical cab |
|---|---|---|---|---|---|
| Fender Deluxe 6G3 (brownface) | Fender | 💤 Not planned | — | Deluxe 6G3 | 1x12 open |
| Fender Vibrolux 6G11 (brownface, 1962/63) | Fender | 💤 Not planned | — | Vibrato Lux 6G11 | 2x10 open |
| Supro 1695T Black Magick | US vintage | 💤 Not planned | — | Supremo Black Magick | 1x12 open |
| Supro 1964T | US vintage | 💤 Not planned | — | Supremo 1964T | 1x12 open |
| Marshall SV20H (Studio Vintage, 20 W Plexi) | Marshall | 💤 Not planned | — | Plexi Studio 20 | 2x12 / 4x12 closed |
| Marshall SC20H (Studio Classic, 20 W JCM800) | Marshall | 💤 Not planned | — | Brit 800 Studio 20 | 2x12 / 4x12 closed |
| Diamond Del Fuego | Modern high gain | ⏸ Waiting (nothing found; specs: 3 × 12AX7, 2 × EL84, 6CA4 rectifier, 22 W, designed with Roy Blankenship) | — | Del Fuego | 4x12 closed |

The wiki also names variants of amps already listed, which can be covered by switches or extra types once the base amp
exists:
- **Fender:** a '59 Bassman LTD reissue, a 1971 silverface Twin Reverb, and old and Custom reissue Vibroverbs.
- **Marshall:** Plexis dated 1967, 1969, 1970 and 1972, a 50 W Plexi with 6CA7 tubes, and JCM800 2203/2204 High and Low inputs.
- **High gain:**
  - EVH: the 5150III Stealth (100S).
  - Mesa: Mark IIC+ Bright/Deep, the Mark IV rhythm and mid-gain voicings, the Mark V IIC+/MKIV/XT modes, and JP-2C Shred.
  - Others: Ecstasy 20th Anniversary, Diezel Herbert MkIII, Carvin VL300, Revv Generator Green/Purple/Red, PRS Archon Clean/Lead/Bright, Fryette D60 Less/More.
- **Boutique:** Two-Rock and ODS PAB switch variants.

For each model the wiki page also gives the original amp's controls, power tubes, matching speakers and Fractal's comments.
That is useful when building a model: check the controls and tubes against the schematic.
