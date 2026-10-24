# Amp catalogue

Every real amp we model, plan to model, or might model, with where it stands in the Circuit Amp block. Use it to find
**reference captures** (to match a model against) and **cab IRs** (to pair with it).

- **Amp captures** (NAM or GuitarML, *direct out / amp only*, no cab or mic) are what the reference harness matches a
  circuit model against. Note the knob settings and the input calibration (dBu at 0 dBFS) with every capture you find.
- **Cab IRs** go in the Cab block after the amp. The "Typical cab" column says what the amp is usually heard through.
- Add a capture you found to `engine/tests/assets/references/manifest.json` (see `docs/06-amp-modelling-spec.md` §4).
  Tone3000 ("T3K" licence) and CC BY-NC files are local-only: never commit them.

Last updated: 2026-10-24 (after the second round of family streams merged).

## Status key

| Status | Meaning |
|---|---|
| ✅ Done | A Circuit Amp type exists and is in master |
| 🔨 In progress | A stream is building it now |
| 📋 Planned | On the list (spec tier 1 or 2), schematic available |
| ⏸ Waiting | Wanted, but no reliable schematic yet: a circuit model would be guesswork. Find a schematic first |
| 💤 Not planned | Popular, not researched yet. Candidates for later |

"References we have" lists what is in the manifest now: **GPL** files are fetched by `cathode-references fetch`;
**T3K** files you download by hand (`cathode-references status` prints the pages and file names).

---

## Fender

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Blackface (Deluxe/Twin-style, generic) | ✅ Done | Blackface | — | — | 1x12 / 2x12 open back |
| Deluxe Reverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Deluxe Reverb | T3K: '68 Custom Deluxe (modified circuit) | Stock AB763 Deluxe, direct out, volume sweep | 1x12 open, Jensen C12K/C12N |
| Twin Reverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Twin Reverb | GPL: Twin Normal Bright, Twin Vibrato Bright | Settings stated; Normal without Bright | 2x12 open, JBL D120 / Jensen C12N |
| Super Reverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Super Reverb | T3K: '64 Super Reverb, 5 settings | More volume steps | 4x10 open, Jensen P10R/P10Q |
| Princeton Reverb AA1164 | ✅ Done | Princeton Reverb | GPL: Princeton clean (year unknown); T3K: '68 Custom Princeton | Stock AA1164, volume sweep | 1x10 open, Jensen P10R |
| Bassman 5F6-A | ✅ Done | Bassman 5F6-A, Jumped | — | Any 5F6-A / reissue, jumpered and single channel | 4x10 open, Jensen P10Q |
| Tweed Deluxe 5E3 | ✅ Done | Tweed Deluxe 5E3, Jumped | T3K: 5E3 kit clone | Volume sweep, both inputs | 1x12 open, Jensen P12R/P12Q |
| Hot Rod Deluxe (III, Fender drawing 0079604000 rev B) | ✅ Done | Hot Rod Deluxe | — | Any direct-out capture (Drive and Normal, More Drive) | 1x12 open, Celestion G12P-80 |
| Champ 5F1 | ✅ Done | Champ 5F1 | — | Volume sweep, direct out | 1x8 open |
| Vibroverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Vibroverb AB763 | — | Direct out, both channels | 1x15 open |
| Vibrolux Reverb AA964 (there is no AB763 Vibrolux Reverb) | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Vibrolux Reverb AA964 | — | Direct out, both channels | 2x10 open |
| Bandmaster AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Bandmaster AB763 | — | Direct out | 2x12 closed |
| Tweed Twin 5F8-A / High Power Tweed Twin | ✅ Done | Tweed Twin 5F8-A, Jumped | — | Bright, Normal, jumped | 2x12 open |

## Marshall and British

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Plexi 1959 Super Lead (50 W, generic) | ✅ Done | Plexi | T3K: 1959 Plexi | Jumpered and single-input captures, volume sweep | 4x12 closed, Greenback G12M |
| Plexi 100 W / jumpered | ✅ Done | Plexi 100W | T3K: 1959 Plexi | | 4x12 closed, Greenback |
| JTM45 | ✅ Done | JTM45 | — | Any JTM45 / reissue | 4x12 or 2x12 (Bluesbreaker), Greenback / Alnico |
| JCM800 2203 | ✅ Done | JCM800 2203 | T3K: 2203 (modified) | Stock 2203, gain sweep | 4x12 closed, G12T-75 / Vintage 30 |
| JCM800 2204 | ✅ Done | JCM800 2204 | — | Stock 2204 | 4x12 closed, G12T-75 |
| Silver Jubilee 2555 | ✅ Done (factory drawing 2555 STD iss 3; relay, shunt feedback and both clippers modelled) | Silver Jubilee | — | Rhythm clip and lead (Boost) modes | 4x12 closed, G12T-75 / Vintage 30 |
| Friedman BE-100 | ✅ Done (rev 1.5 "BE-100 v2" trace, 2014; two-way Voice as traced, later amps have three) | BE-100, BE-100 HBE | — | BE and HBE channels, Fat/C45/Sat settings | 4x12 closed, Vintage 30 / Greenback |
| JCM900 4100 / 4502 | ✅ Done (preamp: vector factory drawing JMP52-02 iss 11, el34world `Marshall_jcm900_dual_reverb_jmp52_02_preamp_iss11.pdf`; power amps: 4100-60-02 iss 11 and the 50 W CDD112 iss 10 for 2500–4502, el34world scans; both channels) | JCM900 4502, JCM900 4100 | GPL: JCM900 4502 channel A gain 4–10, channel B gain 8–20 (now on the JCM900 4502 type) | | 4x12 closed, G12T-75 / Vintage 30 |
| Laney GH100L | ✅ Done | Laney GH100L | GPL: Laney GH100S ×3 | | 4x12 closed, G12H-100 |
| Hiwatt DR103 | ✅ Done (Mark Huss's hiwatt.org redraws: mid-70s four-input preamp, 100 W output) | Hiwatt DR103 | — (T3K: DR103 1974, local only, not mapped) | Normal/Brilliant, jumpered | 4x12 closed, Fane |
| Orange OR50 / Rockerverb | ⏸ Waiting (only the Rockerverb 50 MkI factory sheet, 2004, marginal scan; nothing for MkII/MkIII or the OR50 reissue, MEF requests unanswered; Orange gives schematics to authorised techs only) | — | — | Reliable schematic (MkI only found) | 2x12 / 4x12 closed, Vintage 30 |
| Marshall JVM410H | ✅ Done, all four channels in their Green, Orange and Red modes (DrTube jvm410-60-02 iss 2, -61-02 iss 1, -67-02 iss 2, vector; el34world `Marshall_jvm410_sch.pdf`). The relay states per mode are firmware and on no drawing or forum: deduced from the relays' contacts and the handbook's gain-structure descriptions (see `JVM410H.cs`) | JVM410H OD, JVM410H Clean/Crunch | GPL: pelennor "Phillipe P JVM-OD1/OD2 ... NoDrive" (not added: amp and rig not stated) | Mode-labelled captures | 4x12 closed, Vintage 30 |
| Splawn Quickrod | ⏸ Waiting (no schematic: the schems.com link is dead, an elektrotanya file is said to be the 4 × 12AX7 version; Splawn reportedly has none) | — | — | Schematic | 4x12 closed, Vintage 30 |
| Suhr Badger / SL68 | ⏸ Waiting (an unofficial Badger 18 drawing on MEF is called inaccurate there: the real amp has a dual-ganged gain pot and a plate-driven loop; nothing for the SL68) | — | — | Schematic | 2x12 / 4x12 |
| Marshall JMP 2204 / 2203 (1970s) | ✅ Covered by JCM800 2203 / 2204 (Marshall's 1978 drawing 78331-1, master-volume column, lists the JCM800 preamp's values) | JCM800 2203, JCM800 2204 | — | US (Unicord, 6550) versions | 4x12 closed |
| Marshall 18 W (1974X) | ✅ Done (PedalMonkey rev 5 drawing with voltages; the tremolo: V3B oscillating in the channel's netlist, Speed and Depth) | 18 W 1974X | — | Both channels, volume sweep | 2x10 / 1x12 open |
| Marshall Major 1967 | ✅ Done (Unicord drawing 70-02-12, July 1970, DrTube `1967u.gif`; both channels, V2A and its tone stack inside the feedback loop via `LoopStageDesign`, cathodyne plus ECC82 push-pull driver via `CathodyneDriverDesign`, ultralinear KT88s; B+, idle, OT and UL tap guessed. No redistributable capture found: GuitarML ToneLibrary and pelennor2170/NAM_models have none) | Major 1967 | — | Any capture | 4x12 closed |
| Marshall DSL / JCM2000 | ✅ Done, DSL100 both channels with Clean/Crunch and Lead 1/Lead 2, Tone Shift and Deep (DrTube JCM2-60-02 iss 9 main board and JCM2-61-00 iss 5 front panel, vector; the boards' wiring traced from the connector cross-references printed on both sheets, confirmed by JCM2-60-00 iss 1 of 1997; JCM2-68-02 was not needed) | DSL100 | GPL: pelennor "Tim R JCM2000 Clean / Crunch / L2 G6 / L2 G8" (DSL or TSL not stated; not added) | A DSL direct-out capture with stated settings | 4x12 closed |
| Marshall Bluesbreaker 1962 | ✅ Done (reissue drawing "1962 STD" issues 3 and 4, DrTube `1962ri89.gif` and `1962ri93.gif`: both channels and the jumper; the tremolo channel with the tremolo at rest, its load modelled; the tremolo itself (a J174 FET on the Intensity wiper, its gate drive set by the RV2 preset, whose setting the drawing does not give) is not built) | Bluesbreaker 1962 | — | Any capture (no GPL one found) | 2x12 open |
| Hiwatt Custom 50 / DR504 | ✅ Done (the DR103's four-input preamp redraw with hiwatt.org's 50 W output and supply redraws; `DR504OL.gif` is a complete 1983 two-input DR504 with voltages, a later circuit with an extra gain stage, used only for the rails) | Hiwatt DR504 | — | Normal/Brilliant, jumpered | 4x12 closed, Fane |

## Vox, class A and boutique

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| AC30 Top Boost | ✅ Done (JMI OS/010, OS/065 values) | AC30 | T3K: AC30S1 gain sweep | AC30 TB / C2 / HW direct out | 2x12 open, Celestion Blue / Alnico |
| AC30 Normal channel | ✅ Done | AC30, Switch 1 "Normal Ch" | — | | 2x12 open, Blue |
| AC15 (EF86) | ✅ Done (mixing and cut assumed; no vibrato-tremolo: no AC15 drawing could be read) | AC15 | GPL: AC15 Top Boost; T3K: AC15C1 Normal (6 settings) and Top Boost | Vintage EF86 channel captures | 1x12 open, Blue / G12M |
| Matchless DC-30 | ✅ Done | DC-30 | GPL: Matchless SC30 channel 1 | DC-30 both channels | 2x12 open, G12H30 / Vintage 30 |
| Dumble Overdrive Special | ✅ Done (a 1997 ODS 100, traced: clean and overdrive) | ODS 97 | GPL: Dumble kit low/high, Dumble clone clean/medium/high (all mapped, knobs fitted) | Real ODS captures with settings; the 1970s ODS (Rock/Jazz, Deep) and HRM circuits | 1x12 / 2x12 open, EV12L |
| Two-Rock (Dumble-style) | ⏸ Waiting (no schematic published; the ODS work does not carry over without one) | — | (Dumble captures above) | Schematic or trace | 1x12 / 2x12 open |
| Bad Cat Hot Cat / Black Cat | ✅ Done (Hot Cat 30, both inputs; Black Cat ⏸: no schematic, Bad Cat never published one) | Hot Cat 30 | — | Hot Cat 30 captures, Gain and Clean inputs | 2x12 open |
| Dr Z Maz 18 | ⏸ Waiting (searched 2026-10: no schematic or trace; specs only: 2 x EL84 cathode bias, 5AR4) | — | — | Schematic | 1x12 / 2x12 open |
| Morgan AC20 / SW | ⏸ Waiting (searched 2026-10: no schematic; specs only: EF86 or 12AX7 channel, 2 x EL84, GZ34) | — | — | Schematic | 1x12 open |
| Matchless Chieftain | ✅ Done (factory drawing 1994 plus a measured trace) | Chieftain | — | Captures with settings | 2x12 open, G12H30 |
| Divided by 13 | ⏸ Waiting (searched 2026-10: no schematic or trace for the JRT 9/15, CJ 11 or FTR 37) | — | — | Schematic | 1x12 / 2x12 |
| Budda Superdrive | ⏸ Waiting (searched 2026-10: no schematic or trace) | — | — | Schematic | 2x12 |
| Trainwreck Express | ✅ Done (redraw A1a of the schematicheaven collection, hearsay: medium confidence) | Express | — | Captures with settings; a measured trace | 2x12 / 4x12 |
| Supro (1960s) | ⏸ Waiting (1624T values read; needs a paraphase phase inverter in the library) | — | — | | 1x12 open |
| Gibson GA-40 Les Paul | ⏸ Waiting (factory drawing read; needs a 5879 tube table and a paraphase inverter) | — | — | | 1x12 open |
| Magnatone (Custom 280 / 15) | ⏸ Waiting (drawings read; the dry path runs through the varistor vibrato, and 6CZ5 curves are missing) | — | — | | 2x12 open |
| Supro 1624T Dual-Tone | ⏸ Waiting (values read from a clean redraw, el34world `Supro_1624t_schematic_263.pdf`, power amp confirmed by AmpBooks; its floating paraphase inverter is not in the library) | — | — | A paraphase inverter in `PowerAmp`; B+ and OT (AmpBooks models 7k) | 1x12 open |
| Gibson GA-40 Les Paul (5879 version) | ⏸ Waiting (factory drawing with a voltage table, el34world `Gibson_ga_40_les_paul_with_5879.pdf`, read; needs a 5879 pentode table and a self-balancing paraphase inverter) | — | — | 5879 curves; OT primary (about 10k on the 1954 parts list) | 1x12 open, Jensen P12Q |
| Magnatone Custom 280 (280-A / 280-B) | ⏸ Waiting (factory drawings read, el34world `Magnatone_280a.pdf`, `Magnatone_280b.png`; the dry path runs through the varistor vibrato network, whose static resistance is not given; 6CZ5 curves missing) | — | — | Varistor data; OT primary. Custom 15 drawings exist (`Magnatone_m15a_revf.png`) but are not read | 2x12 open |

## High gain

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Soldano SLO-100 | ✅ Done | SLO-100: OD channel; Switch 1 the normal channel (Crunch, Bright) | GPL: Soldano crunch and high (model not stated); T3K: SLO-100 pack | Normal channel clean and crunch captures; OD gain sweep | 4x12 closed, Vintage 30 / G12T-75 |
| Mesa/Boogie Mark IIC+ | ✅ Done | Mark IIC+, lead | GPL: IIC+ Drive-knob model, IIC+ Drive 8; T3K: IIC+ pack | EQ in and out, Lead Drive sweep | 1x12 / 4x12 closed, EVM12L / Vintage 30 |
| Peavey 5150 / 6505 | ✅ Done (lead); rhythm ⏸ | 5150, lead | GPL: 5150 block letter, 6505+ red (two sources) | Rhythm channel: its network is on the factory "Initial Preamp Stage" page, but the relay states (K1, K2, K4) per channel and crunch setting are on a page the excerpts lack; a full factory set or a trace of the switching logic | 4x12 closed, Sheffield / Vintage 30 |
| EVH 5150III | ✅ Done | 5150III, red | T3K: 5150III OB1 v2 | Blue and red channels | 4x12 closed, G12EVH / Vintage 30 |
| Mesa/Boogie Dual Rectifier | ✅ Done (factory two-channel drawing, 1993, red channel; Modern takes the feedback out) | Dual Rectifier, red | GPL: Mini Rectifier (not the same amp) | Dual/Triple Rec, modern and vintage modes, tube vs SS rectifier | 4x12 closed, Vintage 30 |
| Bogner Ecstasy | ⏸ Waiting (only a modded "83K" redraw and an OD-channel-only redraw of unknown version; no 100B/101B or 20th Anniversary drawing found) | — | GPL: Ecstasy Blue *pedal* (not the amp) | Blue/red channels; a stock 100B/101B schematic | 4x12 closed, Vintage 30 / Greenback |
| Diezel VH4 (ch 3) | ⏸ Waiting (a 2005 factory sheet gives the topology but no values; an owner's trace gives the stacks only; the clone redraws disagree) | — | — | Channel 3 and 4 values | 4x12 closed, Vintage 30 |
| Mesa/Boogie Mark IV (lead) | ✅ Done | Mark IV, lead | GPL: Mark IV | Settings stated | 1x12 / 4x12 closed |
| Carvin Legacy VL100 | ✅ Done (factory schematic) | Legacy VL100: lead; Switch 1 the clean channel | — | Any VL100 capture, settings stated (Vai: Drive 7.5, Bass 6, Mid 4, Treble 8, Presence 8) | 4x12 / 2x12 closed, Vintage 30 |
| Mesa/Boogie Lone Star | ✅ Done (factory drawing) | Lone Star, both channels (Switch 1 "Channel 2", Switch 2 "Drive Mode") | — | Clean/edge channels, 50 W mode | 1x12 / 2x12 open |
| Bogner Uberschall | ⏸ Waiting (two redraws that disagree on the stack topology and several caps) | — | — | A factory drawing or a third trace | 4x12 closed |
| Diezel Herbert | ⏸ Waiting (nothing found; the service manual gives only the tube functions) | — | — | Schematic | 4x12 closed |
| Engl Powerball | ✅ Done (factory drawing E645-1), channel 2 Lo/Hi | Powerball | — | Channel 1 clean/crunch; Powerball II | 4x12 closed, Vintage 30 |
| Engl Savage 120 | ✅ Done (factory drawing E610-2), lead channel | Savage 120 | — | Captures with settings (the pelennor "Savage" file is an Axe-Fx capture: never use it) | 4x12 closed |
| Revv Generator 120 | ⏸ Waiting (nothing published: no schematic, trace or redraw found; specs only, 5 × 12AX7, 4 × EL34) | — | — | Schematic | 4x12 closed |
| Fryette (VHT) Deliverance / Sig:X | ⏸ Waiting (Fryette releases no schematics; only a modified 2-channel D60 drawing as a PedalPCB forum attachment of unknown origin, and a "retro-engineered Deliverance" mentioned on MEF whose file was not found; nothing for the 120 or Sig:X) | — | — | Schematic or trace | 4x12 closed |
| Friedman Dirty Shirley | ⏸ Waiting (a hand-drawn layout and a corrected schematic exist as ampgarage forum attachments, login only; not read) | — | — | Read the ampgarage trace (viewtopic.php?p=397086); NAM captures of a Dirty Shirley Mini exist in pelennor2170/NAM_models | 4x12 closed |
| Cameron CCV | ⏸ Waiting (gut photos only; no schematic or trace found) | — | — | Schematic or trace | 4x12 closed |
| Bogner Shiva / XTC | ⏸ Waiting (only hobbyist traces, behind a CAPTCHA on elektrotanya; the Music Electronics Forum XTC attachments need a login) | — | — | Download those traces by hand and compare | 4x12 closed |
| PRS Archon | ⏸ Waiting (no service schematic found on PRS's site or elsewhere; specs only, 6 × 12AX7, 4 × 6L6 or 2 × 6CA7) | — | — | Schematic; the Clean/Lead/Bright variants | 4x12 closed |
| Krank Revolution | ⏸ Waiting (a reverse-engineered Rev 1 preamp and power amp are MEF attachments, thread 987426, login only; not read, and not factory) | — | — | Read the MEF trace | 4x12 closed |
| Victory Kraken / Sheriff | ⏸ Waiting (nothing found; Victory publishes none) | — | — | Schematic | 4x12 closed |
| Randall Satan / RG100 | ⏸ Waiting (Satan: nothing found. RG100: a legible factory scan of the solid-state RG80/RG100 on el34world, but it is 4558 op-amps and JFETs, which the stage library cannot build yet) | — | — | Satan schematic; op-amp and JFET network stages for the RG100 | 4x12 closed |
| Hughes & Kettner TriAmp | ⏸ Waiting (owner manuals only; a MkII service manual is mentioned on forums but not found) | — | — | Service manual | 4x12 closed |
| Wizard Modern Classic | ⏸ Waiting (an MCII schematic and turret layout by a forum member are ampgarage attachments, login only, viewtopic.php?p=398776; the thread text gives only the inverter and feedback values) | — | — | Read the ampgarage trace | 4x12 closed |
| Mesa/Boogie Mark V | ✅ Done (factory drawing), channel 3 with its IIC+ / Mark IV / Extreme modes | Mark V | GPL (not usable): ToneLibrary SmartAmp Mark 5 high gain (WaveNet format the harness does not run; settings and rig unstated) | Channels 1 and 2; 45 W / 10 W modes | 4x12 / 1x12 closed |
| Mesa/Boogie Triple Rectifier | ✅ Done as a variant (no schematic; the three-channel Dual Rectifier with six 6L6) | Triple Rectifier | — | A Triple Rectifier drawing | 4x12 closed, Vintage 30 |
| Mesa/Boogie Rect-o-Verb | ✅ Done (factory Solo 50 / Rectoverb drawing, first series), lead channel | Rect-o-Verb | — | Rhythm channel; Series II drawing | 1x12 / 2x12 closed |
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
| Ampeg SVT (1970, 6550) | ✅ Done (channel one; power-amp nodes and OT assumed) | SVT | — (no free capture found) | Direct-out captures with settings, Ultra Lo, each Mid Freq | 8x10 closed |
| Ampeg B-15-N Portaflex (1968) | ✅ Done (channel one) | Portaflex B-15N | — | Direct-out captures, Ultra Lo / Hi | 1x15 closed |
| Roland JC-120 (1984 board) | ✅ Done (effect channel, effects off; solid state) | JC-120 | — | Direct-out captures, volume sweep | 2x12 open |
| Fender Bassman 100 / 135 (bass) | ✅ Done (the 135, from CBS drawing 014283 with the 100's drawing 010303 for the small print: Bass and Normal channels, Deep, Bright, Master, 12AT7 LTP, 4 × 6L6GC ultralinear; the OT and its screen taps assumed; the 100 is the same preamp with pentode screens) | Bassman 135 | — (no free capture found) | Direct-out captures with settings | 4x12 / 2x15 |
| Marshall Valvestate 8080 / 8100 | ✅ Done (Marshall drawings PC0689 issue 5 and PC0689P issue 9, vector PDFs at schematicheaven `marshallamps/valvestate80_80w_8080_8100_8412.pdf`; both channels, solid-state power amp; its current feedback not modelled) | Valvestate 8080 | — | Captures | 1x12 / 4x12 closed |

No free (GPL / CC) direct-out capture of any of these was found: GuitarML ToneLibrary and pelennor2170/NAM_models
hold none (searched October 2026; ToneLibrary's Bassman files are the 5F6-A), so the four models are schematic-only. Sources, readings and guesses are in
`docs/06-amp-modelling-spec.md` §3.5 and in each model file. The bass cabs (8 × 10, 1 × 15) are not Cab-block IRs yet:
the B-15 runs on the Circuit Amp's closed 8 × 10 speaker curve.

---

## Counts

Counted over every table in this file, including the two sections added from Fractal's lists.

| Status | Rows |
|---|---|
| ✅ Done | 76 |
| 📋 Planned (schematic found, not built yet) | 4 |
| ⏸ Waiting on a schematic or better data | 64 |
| 💤 Not researched yet | 2 |

### Circuit Amp types by index

The Circuit Amp block has 81 types. The "Cathode type" column above names them; this is their order in the Type list.

| # | Type | # | Type | # | Type |
|---|---|---|---|---|---|
| 0 | Blackface | 27 | Chieftain | 54 | Silver Jubilee |
| 1 | Plexi | 28 | Hot Cat 30 | 55 | 18 W 1974X |
| 2 | AC30 | 29 | ODS 97 | 56 | Hiwatt DR103 |
| 3 | AC15 | 30 | Express | 57 | 1959SLP |
| 4 | DC-30 | 31 | Lone Star | 58 | 1987X |
| 5 | Prometheus | 32 | Mark V | 59 | 1959T Tremolo |
| 6 | Prometheus Clean | 33 | Rect-o-Verb | 60 | Bluesbreaker 1962 |
| 7 | JCM800 2203 | 34 | Triple Rectifier | 61 | Hiwatt DR504 |
| 8 | JCM800 2204 | 35 | Powerball | 62 | Cornford MK50H II |
| 9 | JTM45 | 36 | Savage 120 | 63 | Tiny Terror |
| 10 | Plexi 100W | 37 | SVT | 64 | Liverpool |
| 11 | Laney GH100L | 38 | Portaflex B-15N | 65 | ODS 70s |
| 12 | Deluxe Reverb | 39 | JC-120 | 66 | Dual Rectifier 3-Ch |
| 13 | Twin Reverb | 40 | Hot Rod Deluxe | 67 | Subway Blues |
| 14 | Super Reverb | 41 | Champ 5F1 | 68 | TriAxis Rhythm |
| 15 | Princeton Reverb | 42 | Vibroverb AB763 | 69 | TriAxis Lead 1 |
| 16 | Bassman 5F6-A | 43 | Vibrolux Reverb AA964 | 70 | TriAxis Lead 2 |
| 17 | Bassman 5F6-A Jumped | 44 | Bandmaster AB763 | 71 | JCM900 4502 |
| 18 | Tweed Deluxe 5E3 | 45 | Tweed Twin 5F8-A | 72 | JCM900 4100 |
| 19 | Tweed Deluxe 5E3 Jumped | 46 | Tweed Twin 5F8-A Jumped | 73 | Valvestate 8080 |
| 20 | SLO-100 | 47 | Tremolux AA763 | 74 | JVM410H OD |
| 21 | Mark IIC+ | 48 | Princeton 5F2-A | 75 | Concert 6G12 |
| 22 | 5150 | 49 | Princeton AA964 | 76 | Super 6G4 |
| 23 | Dual Rectifier | 50 | Vibro-King | 77 | Deluxe 6G3 |
| 24 | 5150III | 51 | Blues Junior | 78 | Vibrolux 6G11 |
| 25 | Mark IV | 52 | BE-100 | 79 | Bassman 135 |
| 26 | Legacy VL100 | 53 | BE-100 HBE | 80 | Scout GA-17RVT |

Match quality against the references is in `docs/06-amp-modelling-spec.md` (as-built notes per family) and in the
manifest's targets. Only the Prometheus clean channel reaches the spec's ESR target; captures with stated knob settings
(the Tone3000 packs you fetch by hand) are the main thing that would tighten the others. Most models added in the second
round have no free capture at all and rest on their schematics alone.

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
| Fender Bassman AB165 (blackface) | ⏸ Waiting (the only scan, schematicheaven `bassman_ab165_schem.pdf`, is too poor for the Bass channel's stack and the bias network; topology and power section read) | — | 65 Bassguy Bass / Normal | A legible AB165 drawing | 2x12 closed |
| Fender Concert 6G12 (brownface) | ✅ Done (factory drawing, schematicheaven `concert_6g12_schem.pdf`: both channels, the harmonic vibrato as a stage with Speed, Intensity and the pedal; OT primary assumed) | Concert 6G12 | 6G12 Concert | OT primary (45249, 2 Ω secondary); captures | 4x10 open |
| Fender Super 6G4 (brownface) | ✅ Done (factory drawing `super_6g4_schem.pdf`: the 6G12's circuit, 10k feedback, GZ34) | Super 6G4 | 6G4 Super | OT primary (45216); captures | 2x10 open |
| Fender Tremolux AA763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Tremolux AA763 | Tremolo Lux | Direct out | 2x10 closed |
| Fender Vibro-King | ✅ Done (rev F drawing; the optical vibrato from rev A, the Intensity pot's 250k track on the stack's output) | Vibro-King | Vibra-King, Vibra-King Fat | Direct out, Fat on and off | 3x10 open |
| Fender Blues Junior | ✅ Done (III drawing) | Blues Junior | JR Blues, JR Blues Fat | Direct out (GuitarML's Blues Jr files are miked) | 1x12 open |
| Fender Princeton 5F2 / AA964 | ✅ Done | Princeton 5F2-A, Princeton AA964 | Prince Tone 5F2, Prince Tone AA964 | Direct out | 1x10 open |
| Fender Vibro-Champ (Eric Clapton) | ⏸ Waiting (no schematic published: Fender's product pages, schematicheaven and the forums searched; Fractal's note says cathode bypass caps were added to the 5F1, but not where) | — | 5F1 Tweed EC | Fender's EC Vibro-Champ schematic | 1x8 open |

### Marshall and British

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Marshall 1959SLP reissue | ✅ Done (factory drawing 1959-01-60-02 issue 2, DrTube) | 1959SLP | 1959SLP Jump / Normal / Treble | | 4x12 closed, Greenback |
| Marshall 1987X reissue | ✅ Done (factory drawing 1987-01-60-02 iss 4, as drawn with the 33k slope; the "tone-stack mod" is not documented anywhere found) | 1987X | 1987X Jump / Normal / Treble | | 4x12 closed |
| Marshall JMP 1959T Super Lead (Tremolo) | ✅ Done (Unicord drawing 70-11-11, July 1970, DrTube `1959tu.gif`; the tremolo at rest, its load on the normal volume modelled; the tremolo itself is not built: the switching transistor's type and its base network are illegible on the scan) | 1959T Tremolo | Brit Super | | 4x12 closed |
| Marshall JMP-1 preamp | ✅ Done, Clean I/II and OD I/II with Bass Shift and the active four-band tone circuit, into the JCM900 4100's documented 100 W EL34 power amp (DrTube `jmp1-61-04.pdf`, 6 sheets, iss 10, vector; the TC9176/TC9170 digital pots as 10k pots at the knob position, their value not drawn) | JMP-1 | Brit Pre, JMPRE-1 OD1 / OD2 (+ bass) | A JMP-1 capture with stated settings | 4x12 closed |
| Marshall AFD100 (Slash) | ⏸ Waiting (no public schematic; only the manual) | — | Brit AFS100 1 / 2 | | 4x12 closed |
| Marshall JVM410HJS (Satriani) | ⏸ Waiting (DrTube jvm410hjs-60-02 iss 2, -61-02 iss 1, -62-02 iss 1, vector with text, fetched and read in part: the same channel networks and three-path cascade as the JVM410H but five main-board relays (RL101, RL103 B/C, RL104, RL105) where the JVM410H has three, an input buffer and the THAT4301 gate in the path, and mid-shift relays on the stacks. Which relays each of the nine gain structures sets is firmware; unlike the JVM410H's, the HJS handbook describes the modes' voicings ("Inspired by a Master Volume, JMP", "A lot of Gain") but not their gain stages, so they cannot be deduced) | — | JS410 Crunch / Lead | A measured relay table, or captures per mode | 4x12 closed, Vintage 30 |
| Friedman Small Box | ⏸ Waiting (nothing found; GuitarML's `FriedmanSmallBox_GainKnob` may be the pedal and is undocumented) | — | Friedman Small Box | | 2x12 / 4x12 closed |
| Cornford MK50H II | ✅ Done (Martin Kidd's own drawing, 2011, 5 sheets: both channels, one of the two masters; no voltages on it) | Cornford MK50H II | Corncob M50 | Overdrive channel | 4x12 closed |
| Orange A30 / AD30 | ⏸ Waiting (factory sheet cd0115 issue 5, 2000, is the main board only: V1's two stages, the inverter, four EL84 and the supply, with test-point voltages; the gain, tone and channel circuits are on the front-panel board, reached through the GAIN and EQ connectors, and its sheet is not in the file. el34world, schematicheaven and thetubestore have nothing else; their AD15 file is a different amp) | — | Citrus A30 Clean / Dirty | Front-panel board sheet | 2x12 closed |
| Orange Tiny Terror | ✅ Done (vector redraw, not a factory sheet: el34world `Orange/Orange_tiny_terror.pdf`; 15 W) | Tiny Terror | Citrus Terrier | | 1x12 / 2x12 closed |
| Orange AD200B (bass) | ⏸ Waiting (no schematic found: searched schematicheaven, el34world, MEF and retailer pages; the MkIII is 2 × ECC83, 1 × ECC81, 4 × 6550, Gain/Bass/Middle/Treble/Volume) | — | Citrus Bass 200 | Any schematic or trace | 4x10 / 8x10 closed |
| Blankenship Leeds 21 | ⏸ Waiting (nothing found) | — | Blanknshp Leeds | | 1x12 / 2x12 |
| Vox AC30HW (hand-wired) | ⏸ Waiting (no service schematic found, only the owner's manual; the AC30 (2) is the JMI circuit it reissues) | — | Class-A 30w, 30W Bright, 30w Hot | Service schematic | 2x12 open, Alnico Blue |

### Boutique and class A

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Dumble ODS (HRM and Robben Ford versions) | ✅ Done as the 1970s ODS (schematicheaven `dumbleamps/` preamp, overdrive, supply and output redraws: Rock/Jazz, Deep, Bright, Accent; no HRM drawing found, so no HRM stack) | ODS 70s (Switch 1 Overdrive, Switch 2 Jazz, Switch 3 Deep, Presence = Accent) | — (the GPL Dumble kit and clone captures stay on ODS 97: their circuits are not stated) | HRM schematic; captures of a 1970s ODS | 1x12 / 2x12 open |
| Fuchs Overdrive Supreme | ⏸ Waiting (searched 2026-10: no schematic) | — | Fox ODS, Fox ODS Deep | Schematic | 1x12 / 2x12 open |
| Two-Rock Jet 35 | ⏸ Waiting (searched 2026-10: no schematic) | — | Two Stone J35 1 / 2 | Schematic | 1x12 open |
| Bludotone Ojai | ⏸ Waiting (searched 2026-10: no schematic; said to follow Robben Ford's "Tan" Dumble) | — | Bludojai Clean / Lead 1 / Lead 2 | Schematic | 1x12 / 2x12 open |
| Carr Rambler | ⏸ Waiting (searched 2026-10: no schematic) | — | Car Roamer, FAS Class-A | Schematic | 1x12 / 2x12 open |
| Budda Twinmaster | ⏸ Waiting (searched 2026-10: no schematic) | — | Buttery | Schematic | 2x12 open |
| Divided by 13 CJ 11 / FTR 37 | ⏸ Waiting (see the Divided by 13 row) | — | Div/13 CJ, CJ Boost, FT37 High / Low | Schematic | 1x12 / 2x12 |
| Dr Z Route 66 / MAZ 8 / MAZ 38 SR | ⏸ Waiting (see the Dr Z row) | — | Mr Z Hwy 66, MZ-8, MZ-38 | Schematic | 1x12 / 2x12 open |
| Morgan AC20 Deluxe | ⏸ Waiting (see the Morgan row) | — | AC-20 12AX7 / EF86, Bass / Treble | Schematic | 1x12 open |
| Bad Cat Hot Cat 30 | ✅ Done | Hot Cat 30 | Hot Kitty | Captures | 2x12 open |
| Matchless Chieftain | ✅ Done | Chieftain | Boutique 1 / 2 | Captures | 2x12 open |
| Trainwreck Liverpool | ✅ Done (Mark Abbott's hand drawings of the two Liverpools he saw, schematicheaven `newamps/trainwreck_liverpool.pdf`; R7/C8 at his recommended 68k / 2.2 nF) | Liverpool (Switch 1 Clarity, Bright 500 pF) | — | Captures with settings | 2x12 |
| Trainwreck Rocket | ⏸ Waiting (only DIY drafts behind the ampgarage login) | — | Wrecker Rocket | Schematic | 1x12 / 2x12 |
| Komet 60 / Concorde | ⏸ Waiting (drawings only behind the ampgarage login; Komet restricts them) | — | Comet 60, Comet Concourse | Schematic | 2x12 / 4x12 |
| Swart Atomic Space Tone | ⏸ Waiting (searched 2026-10: no schematic) | — | Nuclear-Tone | Schematic | 1x12 open |
| Paul Ruby Rocket | ⏸ Waiting (searched 2026-10: no schematic) | — | Ruby Rocket, Ruby Rocket Bright | Schematic | 1x12 open |
| Carol-Ann OD-2 / Triptik / Tucana 3 | ⏸ Waiting (searched 2026-10: no schematic) | — | Carol-Ann OD-2, Triptik, Tucana | Schematic | 2x12 / 4x12 |
| Gibson Scout | ✅ Done (GA-17RVT drawing with voltages, schematicheaven `gibsonamps/ga17rvt.pdf`; new 6EU7 and 6AQ5 tables fitted to the RCA and GE datasheets; the bias tremolo with Speed and Depth; reverb off) | Scout GA-17RVT | Gibtone Scout | Captures | 1x10 / 1x12 open |

### High gain

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Mesa/Boogie TriAxis preamp | ✅ Done (factory audio sheets rev 1.08 with the LDR mode table; LDR ranges taken from the Mark IIC+; presence LDR fixed) into a generic power amp (the Dual Rectifier model's 4 × 6L6) | TriAxis Rhythm (Switch 1 Yellow), TriAxis Lead 1, TriAxis Lead 2 (Switch 1 Yellow, Switch 2 Red; Control 1 lead drive, Control 2 Dynamic Voice) | — | Captures with settings | 4x12 closed |
| Mesa/Boogie JP-2C | ⏸ Waiting (no drawing found; the Mark IIC+ model covers its channel 3 circuit) | — | USA JP IIC+ Green / Yellow / Red | | 4x12 closed |
| Mesa/Boogie Triple Crown TC-100 | ⏸ Waiting (no drawing found) | — | Triple Crest 2 / 3 | | 4x12 closed |
| Mesa/Boogie Subway Blues | ✅ Done, partly (factory drawings read; the potted "Toning Module" is not drawn and is left out) | Subway Blues (Switch 1 Fat) | — | The toning module's contents | 1x10 open |
| Mesa/Boogie Dual Rectifier, 2- and 3-channel revisions | ✅ Done (both factory scans: the two-channel 1993 RF-1F set now drives the Dual Rectifier type, which the redraw had wrong; the three-channel Solo Head set is a new type and the Triple Rectifier's base) | Dual Rectifier (2-channel, red), Dual Rectifier 3-Ch (channel 3; Bright = Raw), Triple Rectifier | — | Orange / channel 1-2 voicings; Raw vs Vintage captures | 4x12 closed, Vintage 30 |
| Soldano X88R preamp | ⏸ Waiting (a legible single-sheet redraw, "SOLDANO X88R" 13 Jul 2002, on el34world, Soldano_x88r.pdf; its origin is unknown and owners doubt it; not built) | — | Solo 88 Clean / Rhythm / Lead | A second source to confirm the redraw | 4x12 closed |
| Soldano/Caswell X99 preamp | ⏸ Waiting (a legible scan of a hand-labelled drawing on el34world, Soldano_x99.pdf, no title block; Fractal says its rhythm channel is the SLO-100's; not built) | — | Solo 99 Clean / Lead | Confirm the drawing; a power amp to pair it with (Fractal uses the SLO's) | 4x12 closed |
| CAE 3+ SE preamp | ⏸ Waiting (a schematic of unknown origin is a GroupDIY attachment, thread 51582, which refused automated access; not read) | — | CA3+ Clean / Rhythm / Lead | Read the GroupDIY drawing | 4x12 closed |
| Bogner Fish preamp | ⏸ Waiting (not on el34world or schematicheaven, which hold only the Ecstasy OD, Triple Giant and Uberschall redraws) | — | Bogfish Brown / Strato | | 4x12 closed |
| Cameron Atomica | ⏸ Waiting (descriptions only: a Super Lead with a Jose-style master and a diode-clipping switch; the Ceriatone Molecular is a clone, also without a schematic) | — | Atomica High / Low | Schematic | 4x12 closed |
| Hook Captain 34 | ⏸ Waiting (nothing found) | — | Capt Hook 1A–3B | Schematic | 4x12 closed |
| Splawn Nitro | ⏸ Waiting (nothing for the Nitro; Splawn says it has no schematics; a Quick Rod schematic circulates behind member-only sites) | — | Spawn Nitrous 1 / 2 | Schematic | 4x12 closed |
| Fryette D60 | ⏸ Waiting (see Deliverance: only a modified 2-channel D60 drawing of unknown origin, PedalPCB forum post 322300) | — | Fryette D60 Less / More | Schematic or trace | 4x12 closed |

### Bass

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Mesa/Boogie Bass 400 | ✅ Done (factory 1985 scan, el34world `Boogie_bass400.pdf`, rendered with the PDF's page transforms, which makes the volume return (ground) and V3's 62k loads readable; both inputs, Bass/Treble Shift, the six-band LC graphic EQ (five EQ bands plus Control 2), cascaded differential driver via `DifferentialDriverDesign`, six 6550; V4's plate resistors, the EQ's 10k input/feedback resistors and the OT guessed. No redistributable capture found) | Bass 400 | — | Any capture | 8x10 / 4x10 closed |

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
| Fender Deluxe 6G3 (brownface) | Fender | ✅ Done (factory drawing `deluxe_6g3_schem.pdf`: Normal and Bright channels, Tone, the split-load second stage; the bias tremolo with Speed and Intensity) | Deluxe 6G3 | Deluxe 6G3 | 1x12 open |
| Fender Vibrolux 6G11 (brownface, 1962/63) | Fender | ✅ Done (factory drawing `vibrolux_6g11_schem.pdf`: Normal and Bright channels; the drawing has a bias tremolo, not the harmonic vibrato: built, with Speed and Intensity; OT assumed) | Vibrolux 6G11 | Vibrato Lux 6G11 | 2x10 open |
| Supro 1695T Black Magick | US vintage | ⏸ Waiting (a 2016 reissue-era design by Absara; no schematic published: only specifications, 2 × 6973 cathode-biased, 25 W) | — | Supremo Black Magick | 1x12 open |
| Supro 1964T | US vintage | ⏸ Waiting (no schematic for a "1964T" found; the 2014 reissues' S6420 / S6424 schematics may be related) | — | Supremo 1964T | 1x12 open |
| Marshall SV20H (Studio Vintage, 20 W Plexi) | Marshall | 💤 Not planned (not searched yet) | — | Plexi Studio 20 | 2x12 / 4x12 closed |
| Marshall SC20H (Studio Classic, 20 W JCM800) | Marshall | 💤 Not planned (not searched yet) | — | Brit 800 Studio 20 | 2x12 / 4x12 closed |
| Diamond Del Fuego | Modern high gain | ⏸ Waiting (nothing found; specs: 3 × 12AX7, 2 × EL84, 6CA4 rectifier, 22 W, designed with Roy Blankenship) | — | Del Fuego | 4x12 closed |

The wiki also names variants of amps already listed, which can be covered by switches or extra types once the base amp
exists:
- **Fender:** a '59 Bassman LTD reissue, a 1971 silverface Twin Reverb, and old and Custom reissue Vibroverbs.
- **Marshall:** Plexis dated 1967, 1969, 1970 and 1972, a 50 W Plexi with 6CA7 tubes, and JCM800 2203/2204 High and Low inputs.
- **High gain:**
  - EVH: the 5150III Stealth (100S).
  - Mesa: Mark IIC+ Bright/Deep, the Mark IV rhythm and mid-gain voicings, the Mark V IIC+/MKIV/XT modes (now the
    Mark V type's mode switches), and JP-2C Shred. The IIC+ and Mark IV rhythm channels are on the factory scans
    (Boogie_mkiic_plus.pdf, Boogie_mkiv.pdf) but not modelled yet.
  - Others: Ecstasy 20th Anniversary (no drawing found), Diezel Herbert MkIII (no drawing found), Carvin VL300, Revv Generator Green/Purple/Red, PRS Archon Clean/Lead/Bright, Fryette D60 Less/More.
- **Boutique:** Two-Rock and ODS PAB switch variants.

For each model the wiki page also gives the original amp's controls, power tubes, matching speakers and Fractal's comments.
That is useful when building a model: check the controls and tubes against the schematic.
