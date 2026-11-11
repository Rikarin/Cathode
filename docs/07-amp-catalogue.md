# Amp catalogue

Every real amp we model, plan to model, or might model, with where it stands in the Circuit Amp block. Use it to find
**reference captures** (to match a model against) and **cab IRs** (to pair with it).

- **Amp captures** (NAM or GuitarML, *direct out / amp only*, no cab or mic) are what the reference harness matches a
  circuit model against. Note the knob settings and the input calibration (dBu at 0 dBFS) with every capture you find.
- **Cab IRs** go in the Cab block after the amp. The "Typical cab" column says what the amp is usually heard through.
- **Speaker load vs cab.** The Circuit Amp's *Speaker load* is the speaker as the power amp's electrical load (its
  impedance peak and HF rise, which shape the power amp's response, clipping and sag); the Cab block is what the
  speaker, box and mic sound like. Set *Speaker load* to **Follow Cab** and the amp takes the load of the speaker and
  cabinet chosen in the next Cab block (from that speaker's Thiele–Small data), so a "Typical cab" picked there sets
  both. See `docs/06-amp-modelling-spec.md` §1, speaker page.
- Add a capture you found to `engine/tests/assets/references/manifest.json` (see `docs/06-amp-modelling-spec.md` §4).
  Tone3000 ("T3K" licence) and CC BY-NC files are local-only: never commit them.

Last updated: 2026-11-10 (88 types, through the Quick Rod, AFD100, DSL100 and JMP-1; duplicate rows merged and the counts redone; a row for each amp issue #357 to #509, a status for each variant the wiki names, and the AC15 and Dr Z Route 66 drawings read).

## Status key

| Status | Meaning |
|---|---|
| ✅ Done | A Circuit Amp type exists and is in master |
| 🔨 In progress | A stream is building it now |
| 📋 Planned | On the list (spec tier 1 or 2), schematic available |
| ⏸ Waiting | Wanted, but no reliable schematic yet: a circuit model would be guesswork. Find a schematic first |
| 💤 Not planned | Popular, not researched yet. Candidates for later |
| ↪ See … | The amp has its own row elsewhere in this file and is counted there; this row only adds the Axe model names |

"References we have" lists what is in the manifest now: **GPL** files are fetched by `cathode-references fetch`;
**T3K** files you download by hand (`cathode-references status` prints the pages and file names).

---

## Fender

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Blackface (generic AB763 Super Reverb-style channel: two 6L6GC, 4.2k, with an added master and presence) | ✅ Done | Blackface | — | — | 4x10 open back |
| Deluxe Reverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Deluxe Reverb | T3K: '68 Custom Deluxe (modified circuit) | Stock AB763 Deluxe, direct out, volume sweep | 1x12 open, Jensen C12K/C12N |
| Twin Reverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Twin Reverb | GPL: Twin Normal Bright, Twin Vibrato Bright | Settings stated; Normal without Bright | 2x12 open, JBL D120 / Jensen C12N |
| Super Reverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Super Reverb | T3K: '64 Super Reverb, 5 settings | More volume steps | 4x10 open, Jensen P10R/P10Q |
| Princeton Reverb AA1164 | ✅ Done | Princeton Reverb | GPL: Princeton clean (year unknown); T3K: '68 Custom Princeton | Stock AA1164, volume sweep | 1x10 open, Jensen P10R |
| Bassman 5F6-A | ✅ Done (the stack's treble wiper straight into the inverter: no master; presence pot as the tail's shunt) | Bassman 5F6-A, Jumped | — | Any 5F6-A / reissue, jumpered and single channel | 4x10 open, Jensen P10Q |
| Tweed Deluxe 5E3 | ✅ Done (6V6s on the drawing's shared 250 Ω cathode resistor) | Tweed Deluxe 5E3, Jumped | T3K: 5E3 kit clone | Volume sweep, both inputs | 1x12 open, Jensen P12R/P12Q |
| Hot Rod Deluxe (III, Fender drawing 0079604000 rev B) | ✅ Done | Hot Rod Deluxe | — | Any direct-out capture (Drive and Normal, More Drive) | 1x12 open, Celestion G12P-80 |
| Champ 5F1 | ✅ Done | Champ 5F1 | — | Volume sweep, direct out | 1x8 open |
| Champ AA764 / Vibro Champ AA764 | ✅ Done, the Champ (Fender drawing AA764, code I-FD: the blackface stack with a fixed 15k, 2.7k feedback into V1B's 47 Ω, 6V6 on 470 Ω at 360 V). The Vibro Champ is the same amp with a tremolo that swings V1B's cathode, which the stage library does not have: not built | Champ AA764 | — | Volume sweep, direct out | 1x8 open |
| Vibroverb AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Vibroverb AB763 | — | Direct out, both channels | 1x15 open |
| Vibrolux Reverb AA964 (there is no AB763 Vibrolux Reverb) | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Vibrolux Reverb AA964 | — | Direct out, both channels | 2x10 open |
| Bandmaster AB763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Bandmaster AB763 | — | Direct out | 2x12 closed |
| Tweed Twin 5F8-A / High Power Tweed Twin | ✅ Done (presence pot as the tail's shunt) | Tweed Twin 5F8-A, Jumped | — | Bright, Normal, jumped | 2x12 open |

## Marshall and British

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Plexi 1959 Super Lead (50 W, generic) | ✅ Done (the Marshall inverter's 470 Ω bias; a switched 470 pF bright cap; no depth) | Plexi | T3K: 1959 Plexi | Jumpered and single-input captures, volume sweep | 4x12 closed, Greenback G12M |
| Plexi 100 W / jumpered | ✅ Done | Plexi 100W | T3K: 1959 Plexi | | 4x12 closed, Greenback |
| JTM45 | ✅ Done (presence pot as the tail's shunt) | JTM45 | — | Any JTM45 / reissue | 4x12 or 2x12 (Bluesbreaker), Greenback / Alnico |
| JCM800 2203 | ✅ Done | JCM800 2203 | T3K: 2203 (modified) | Stock 2203, gain sweep | 4x12 closed, G12T-75 / Vintage 30 |
| JCM800 2204 | ✅ Done | JCM800 2204 | — | Stock 2204 | 4x12 closed, G12T-75 |
| Marshall JCM800 2210 / 2205 (split channel) | ✅ Done, both channels (Switch 1 Normal, off = Boost), from the factory drawings "2210 Preamp" issue 2 (10-5-88, `2210PRE.DGM`, PCB JMB3) and "2210 STD Output Stage & PSU" issue 2 (4-5-88), DrTube `2210prem.gif` / `2210pwrm.gif`, and the 2205's of the same issues (`2205prem.gif` / `2205pwrm.gif`): the Normal channel's two-knob tone network and V2, the Boost channel's dual-gang Gain, V1B on its cathode diode, the W005 bridge + 1N4007 clipper, V3A and its tone stack, the passive mix of both channels at the Master, the send's 470k / 10k and V4A, the inverter as drawn (22k tail, 330k leak) and the feedback from the 4 Ω tap. The muted channel loads the playing one as in the amp. The drawings have no voltages: B+, idle current and output transformer are the 2203's / 2204's (guessed), the preamp node computed from the 10k dropper. Reverb, effects loop and DI not modelled. The 2205 differs by its power amp, supply cans and 47 pF for 220 pF on V3A's grid | JCM800 2210, JCM800 2205 | — | Any 2210 / 2205 capture with the channel and knobs stated | 4x12 closed, G12T-75 |
| Silver Jubilee 2555 | ✅ Done (factory drawing 2555 STD iss 3; relay, shunt feedback and both clippers modelled) | Silver Jubilee | — | Rhythm clip and lead (Boost) modes | 4x12 closed, G12T-75 / Vintage 30 |
| Friedman BE-100 | ✅ Done (rev 1.5 "BE-100 v2" trace, 2014; two-way Voice as traced, later amps have three; presence pot as the tail's shunt) | BE-100, BE-100 HBE | — | BE and HBE channels, Fat/C45/Sat settings | 4x12 closed, Vintage 30 / Greenback |
| JCM900 4100 / 4502 | ✅ Done (preamp: vector factory drawing JMP52-02 iss 11, el34world `Marshall_jcm900_dual_reverb_jmp52_02_preamp_iss11.pdf`; power amps: 4100-60-02 iss 11 (1k screens) and the 50 W CDD112 iss 10 for 2500–4502 (2k2 screens), el34world scans; both channels) | JCM900 4502, JCM900 4100 | GPL: JCM900 4502 channel A gain 4–10, channel B gain 8–20 (now on the JCM900 4502 type) | | 4x12 closed, G12T-75 / Vintage 30 |
| Laney GH100L | ✅ Done | Laney GH100L | GPL: Laney GH100S ×3 | | 4x12 closed, G12H-100 |
| Hiwatt DR103 | ✅ Done (Mark Huss's hiwatt.org redraws: mid-70s four-input preamp, 100 W output; both halves of the presence pot and its fixed 470 Ω + 10 nF feedback branch) | Hiwatt DR103 | — (T3K: DR103 1974, local only, not mapped) | Normal/Brilliant, jumpered | 4x12 closed, Fane |
| Orange OR50 / Rockerverb | ⏸ Waiting (only the Rockerverb 50 MkI factory sheet, 2004, marginal scan; nothing for MkII/MkIII or the OR50 reissue, MEF requests unanswered; Orange gives schematics to authorised techs only) | — | — | Reliable schematic (MkI only found) | 2x12 / 4x12 closed, Vintage 30 |
| Marshall JVM410H | ✅ Done, all four channels in their Green, Orange and Red modes (DrTube jvm410-60-02 iss 2, -61-02 iss 1, -67-02 iss 2, vector; el34world `Marshall_jvm410_sch.pdf`). The relay states per mode are firmware and on no drawing or forum: deduced from the relays' contacts and the handbook's gain-structure descriptions (see `JVM410H.cs`) | JVM410H OD, JVM410H Clean/Crunch | GPL: pelennor "Phillipe P JVM-OD1/OD2 ... NoDrive" (not added: amp and rig not stated) | Mode-labelled captures | 4x12 closed, Vintage 30 |
| Splawn Quickrod | ✅ Done, both channels (the preamp drawing "Splawn QuickRod", sheet 1/1, a clean CAD drawing, file `Splawn Quickrod Preamp Schematic.pdf`: the two-channel amp with three 12AX7s in the preamp, four with the inverter; OD1/OD2, the 1st/2nd and 3rd gear switches, the solo boost and the clean channel; V1B and the paralleled V3 on one cathode as drawn. The drawing ends at the inverter: the power amp is the JCM800 2203's, guessed, as are the switch positions and the pot tapers. Over the high-gain CPU budget by choice: the overdrive channel's four triodes in one netlist, −55 to −58 dB from the uncut circuit, cost 17.6 / 35.0 % (3rd gear 19.7 / 34.0 %) against 16 / 28 %; the user chose accuracy, as for the Silver Jubilee's noise case) | Quick Rod | Spawn Q-Rod 1st / 2nd / 3rd Gear | A Quick Rod capture with stated settings; the power amp's drawing | 4x12 closed, Vintage 30 |
| Suhr Badger / SL68 | ⏸ Waiting (an unofficial Badger 18 drawing on MEF is called inaccurate there: the real amp has a dual-ganged gain pot and a plate-driven loop; nothing for the SL68) | — | — | Schematic | 2x12 / 4x12 |
| Marshall JMP 2204 / 2203 (1970s) | ✅ Covered by JCM800 2203 / 2204 (Marshall's 1978 drawing 78331-1, master-volume column, lists the JCM800 preamp's values) | JCM800 2203, JCM800 2204 | — | US (Unicord, 6550) versions | 4x12 closed |
| Marshall 18 W (1974X) | ✅ Done (PedalMonkey rev 5 drawing with voltages; the tremolo: V3B oscillating in the channel's netlist, Speed, and Depth as the Intensity rheostat in the shared cathode) | 18 W 1974X | — | Both channels, volume sweep | 1x12 open, Greenback G12M |
| Marshall 1992 Super Bass 100 | ✅ Done (#366; Unicord drawing 70-13-11, July 1970, DrTube `1992u.gif`: both channels on the shared, bypassed V1 cathode, no bright caps, 0.022 µF coupling on both, the 250 pF / 56k stack, 0.1 µF from the inverter, the Super Lead's power amp, presence pot as the tail's shunt. The sheet prints no voltages: B+ guessed as the Plexi 100W's, the preamp rails computed from the drawn 20k / 10k / 10k droppers) | Super Bass 100 | — (Quad Cortex British Bass 100, TONEX Super Bass 100W) | Guitar and bass captures, both channels, jumpered | 4x12 closed, Greenback; 4x15 for bass |
| Marshall Major 1967 | ✅ Done (Unicord drawing 70-02-12, July 1970, DrTube `1967u.gif`; both channels, V2A and its tone stack inside the feedback loop via `LoopStageDesign`, cathodyne plus ECC82 push-pull driver via `CathodyneDriverDesign`, ultralinear KT88s; B+, idle, OT and UL tap guessed. No redistributable capture found: GuitarML ToneLibrary and pelennor2170/NAM_models have none) | Major 1967 | — | Any capture | 4x12 closed |
| Marshall DSL / JCM2000 | ✅ Done, DSL100 both channels with Clean/Crunch and Lead 1/Lead 2, Tone Shift and Deep (DrTube JCM2-60-02 iss 9 main board and JCM2-61-00 iss 5 front panel, vector; the boards' wiring traced from the connector cross-references printed on both sheets, confirmed by JCM2-60-00 iss 1 of 1997; JCM2-68-02 was not needed) | DSL100 | GPL: pelennor "Tim R JCM2000 Clean / Crunch / L2 G6 / L2 G8" (DSL or TSL not stated; not added) | A DSL direct-out capture with stated settings | 4x12 closed |
| Marshall JCM2000 TSL100 | ✅ Done, Clean, Crunch and Lead with Mid Boost, Tone Shift and Deep (DrTube TL10-60-02 iss 7 main board, TL10-61-02 iss 4 overdrive front board, TL10-65-02 iss 4 Clean board, TL10-66-01 iss 2 Crunch board, TL10-62-02 iss 6 rear board, TL10-63-02 iss 4 reverb board, vector, and the 2004 handbook; these sheets print no connector cross-references, so the wiring between the boards is inferred from pin counts, grounds and function; no voltages printed, the rails derived from the drawn supply chain under a guessed 470 V B+; #368) | TSL100 | GPL: pelennor "Tim R JCM2000" captures (DSL or TSL not stated; not added) | A TSL direct-out capture with stated settings | 4x12 closed |
| Marshall Bluesbreaker 1962 | ✅ Done (reissue drawing "1962 STD" issues 3 and 4, DrTube `1962ri89.gif` and `1962ri93.gif`: both channels and the jumper; the tremolo on Speed and Intensity: a J174 FET on the Intensity pot's wiper chops Channel II at the rate of the drawing's oscillator, 2.0 to 4.7 Hz, #291; the RV2 preset's setting, which the drawing does not give, is taken where the FET is open at rest; presence pot as the tail's shunt) | Bluesbreaker 1962 | — | Any capture (no GPL one found) | 2x12 open |
| Hiwatt Custom 50 / DR504 | ✅ Done (the DR103's four-input preamp redraw with hiwatt.org's 50 W output and supply redraws; `DR504OL.gif` is a complete 1983 two-input DR504 with voltages, a later circuit with an extra gain stage, used only for the rails) | Hiwatt DR504 | — | Normal/Brilliant, jumpered | 4x12 closed, Fane |

## Vox, class A and boutique

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| AC30 Top Boost | ✅ Done (JMI OS/010, OS/065 values) | AC30 | T3K: AC30S1 gain sweep | AC30 TB / C2 / HW direct out | 2x12 open, Celestion Blue / Alnico |
| AC30 Normal channel | ✅ Done | AC30, Switch 1 "Normal Ch" | — | | 2x12 open, Blue |
| AC15 (EF86) | ✅ Done (mixing and cut assumed, and no vibrato-tremolo channel. Three AC15 drawings are on schematicheaven's Vox index, read 2026-11-10: `voxamps/ac151960.pdf` is JMI's "Vox AC15 amplifier circuit No. 3", drawing OA/031 of 25-4-60, the amp this type models: EF86 Normal channel with the Brilliance switch, 12AX7 inverter, cut, two EL84 on a shared 130 Ω, 8000 Ω anode to anode, EZ81, and the Vib/Trem channel on a 12AX7, a 12AU7 and a 12AX7. The scan is rough: the main path can be read with care, parts of the Vib/Trem section are smudged. `voxamps/ac15.pdf` is the later "Vox AC-15 Amplifier No. V-1-5" sheet with American tube numbers, what looks like the same circuit redrawn cleanly, but scanned at 596 × 743 pixels, so its values are at the edge of legible; the two together may settle most of them. `voxamps/ac151959.pdf` is the earlier "AC/15 Amplifier No. 2" (OS/005, 4-12-59) with the ECF82 Vibravox unit, another circuit. The type's assumed mixing and cut have not yet been checked against OA/031: [#452](https://github.com/Rikarin/cathode/issues/452)) | AC15 | Stand-ins, not this amp: GPL: AC15 Top Boost (version not stated; mapped to the AC30 type, the AC15 type having no top-boost channel); T3K: AC15C1 Normal (6 settings, on the type's 12AX7 channel) and Top Boost (the AC15C1 is a current circuit with no EF86) | Vintage EF86 channel captures | 1x12 open, Blue / G12M |
| Matchless DC-30 | ✅ Done | DC-30 | GPL: Matchless SC30 channel 1 | DC-30 both channels | 2x12 open, G12H30 / Vintage 30 |
| Dumble Overdrive Special | ✅ Done (a 1997 ODS 100, traced: clean and overdrive; the 1970s ODS with Rock/Jazz and Deep is its own type, see the Dumble ODS row in the additions below) | ODS 97, ODS 70s | GPL: Dumble kit low/high, Dumble clone clean/medium/high (all mapped to ODS 97, knobs fitted) | Real ODS captures with settings; an HRM schematic | 1x12 / 2x12 open, EV12L |
| Two-Rock (Dumble-style) | ⏸ Waiting (no schematic published; the ODS work does not carry over without one) | — | (Dumble captures above) | Schematic or trace | 1x12 / 2x12 open |
| Bad Cat Hot Cat / Black Cat | ✅ Done (Hot Cat 30, both inputs; its Brilliance on the Presence knob, clockwise brighter; the master read as a divider, the drawing having it as a dead rheostat; Black Cat ⏸: no schematic, Bad Cat never published one) | Hot Cat 30 | — | Hot Cat 30 captures, Gain and Clean inputs | 2x12 open |
| Dr Z Maz 18 | ⏸ Waiting (searched 2026-10: no schematic or trace; specs only: 2 x EL84 cathode bias, 5AR4). For the Route 66 there is a trace, read 2026-11-10: prowessamplifiers.com `schematics/images/Route66.jpg`, a redrawn sheet titled "ROOT 666" and signed "The Doctor", Rev 1.0, 4/15/2005. It is not a factory drawing and names no source amp, but every value on it is legible: an EF86 input stage (220k plate, 1M screen, 1.5k cathode with 25 µF), Bass and Treble around a 150k / 100k divider, Volume, a 12AX7 long-tailed-pair inverter (82k and 91k plates, 1.2k and 47k in the tail), two fixed-bias KT66 (−50 V, 1.5k grid stoppers, 470 Ω screens), a 5AR4, and the supply (445 / 353 / 306 V) with a table of measured voltages. The output transformer's primary is not given. Enough to build the Route 66 as a trace-based type ([#384](https://github.com/Rikarin/cathode/issues/384)) | — | — | Schematic (Maz); a factory drawing or a second trace to check the Route 66 one against | 1x12 / 2x12 open |
| Morgan AC20 / SW | ⏸ Waiting (searched 2026-10: no schematic; specs only: EF86 or 12AX7 channel, 2 x EL84, GZ34) | — | — | Schematic | 1x12 open |
| Matchless Chieftain | ✅ Done (factory drawing 1994 plus a measured trace; its Brilliance on the Presence knob, clockwise brighter) | Chieftain | — | Captures with settings | 2x12 open, G12H30 |
| Divided by 13 | ⏸ Waiting (searched 2026-10: no schematic or trace for the JRT 9/15, CJ 11 or FTR 37) | — | — | Schematic | 1x12 / 2x12 |
| Budda Superdrive | ⏸ Waiting (searched 2026-10: no schematic or trace) | — | — | Schematic | 2x12 |
| Trainwreck Express | ✅ Done (redraw A1a of the schematicheaven collection, hearsay: medium confidence; presence pot as the tail's shunt) | Express | — | Captures with settings; a measured trace | 2x12 / 4x12 |
| Supro 1624T Dual-Tone (1960s) | ⏸ Waiting (values read from a clean redraw, el34world `Supro_1624t_schematic_263.pdf`, power amp confirmed by AmpBooks; a floating paraphase inverter is in the library now (`FloatingParaphaseDesign`, built for the B-15N) but has not been checked against the 1624T's; there is no 6973 output-tube table) | — | — | 6973 curves; the inverter checked against the drawing; B+ and OT (AmpBooks models 7k) | 1x12 open |
| Gibson GA-40 Les Paul (5879 version) | ⏸ Waiting (factory drawing with a voltage table, el34world `Gibson_ga_40_les_paul_with_5879.pdf`, read; its self-balancing paraphase inverter can be built now (`FloatingParaphaseDesign`); still needs a 5879 pentode table) | — | — | 5879 curves; OT primary (about 10k on the 1954 parts list) | 1x12 open, Jensen P12Q |
| Magnatone Custom 280 (280-A / 280-B) and Custom 15 | ⏸ Waiting (factory drawings read, el34world `Magnatone_280a.pdf`, `Magnatone_280b.png`; the dry path runs through the varistor vibrato network, which needs a varistor element and the varistors' V-I curve, not on the drawings; 6CZ5 curves missing) | — | — | Varistor data: Tim Robbins's "Magnatone vibrato design" (dalmura.com.au, 2015, revised 2023) is about these varistors and has not been read; 6CZ5 curves; OT primary. Custom 15 drawings exist (`Magnatone_m15a_revf.png`) but are not read | 2x12 open |

## High gain

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Soldano SLO-100 | ✅ Done | SLO-100: OD channel; Switch 1 the normal channel (Crunch, Bright) | GPL: Soldano crunch and high (model not stated); T3K: SLO-100 pack | Normal channel clean and crunch captures; OD gain sweep | 4x12 closed, Vintage 30 / G12T-75 |
| Mesa/Boogie Mark IIC+ | ✅ Done (presence: the rheostat in series with the feedback, as drawn) | Mark IIC+, lead (Switch 1 Treble Shift, Switch 2 Deep, Switch 3 Bass Shift; Bright is Volume 1's and the Lead Master's pull together); Mark IIC+ Rhythm, the rhythm channel from the factory scan (the same amp without the lead circuit and the lead master) | GPL: IIC+ Drive-knob model, IIC+ Drive 8; T3K: IIC+ pack | EQ in and out, Lead Drive sweep | 1x12 / 4x12 closed, EVM12L / Vintage 30 |
| Peavey 5150 / 6505 | ✅ Done (lead and rhythm; the supply page unread, its preamp rails derived from the assumed chain) | 5150: lead channel; Switch 1 the rhythm channel (Switch 2 Crunch, Bright) | GPL: 5150 block letter; 6505+ red (two sources: the 6505+ is the 5150 II, a later amp with a changed preamp and now types of its own, the row below; the two captures were fitted on the 5150 type and stay there until they are refitted) | The rhythm channel (#201) is read from the factory "Initial Preamp Stage" and "Intermediate Preamp Stages" pages: K2A takes V1B's input from the Clean Pre (pin 3) or the Ultra Pre (pin 4), K2B the post gain; K4 is Crunch (pin 3: R19 470k ‖ 270 pF in series and R23 39k + 0.1 µF to ground at the Clean Pre's top; pin 4: R43 2.2M ‖ 470 pF and no shunt, 22 dB more at 1 kHz in the model); the Bright switch S1C puts C10 470 pF across R21 1M; K1 grounds C26 (22 µF on V1B's cathode) and R20 (220k at V2A's grid). The switching page is still not among the excerpts; that K1 is closed on the rhythm channel and open on the lead is read from the scan's relays all being drawn on pins 3 and 6 (K2's are the Clean wipers), from Peavey's 5150 combo sheet (99057001, 1995: the pre-gain relay and the relay grounding the 220k at V2A's grid have their coils in series with the lead LED) and from a forum note on what R20 and C26 are for (music-electronics-forum threads 909929 and 24157). Read with doubt: R23, C63, and C27 (180 pF across the Clean Pre). Still wanted: the head's supply and switching pages; captures of a 5150 or 6505 (not a 6505+) with settings, both channels | 4x12 closed, Sheffield / Vintage 30 |
| Peavey 5150 II / 6505+ | ✅ Done (both channels, from Peavey's factory set: the 2001 preamp drawing P9700041 with its bill of materials, and the power board P9000115; the drawing prints no voltages, so B+, the idle current and the output transformer are the 5150 model's, and the rails follow from the drawn dropping chain) | 6505+, lead; 6505+ Rhythm, the rhythm channel (Switch 1 Crunch, Bright) | GPL: 6505+ red, two sources, still fitted on the 5150 type (see that row) | The two 6505+ red captures refitted on the 6505+ type; a voltage chart (B+, the three preamp rails, the bias); rhythm and crunch captures with settings | 4x12 closed, Sheffield / Vintage 30 |
| Peavey Classic 30 | ✅ Done (factory drawing 81501580, 24 March 1994, schematicheaven `peavey_classic30.pdf`: Normal and Lead channels, the Boost switch, the cathodyne and four EL84, V3A inside the feedback loop; reverb and effects loop left out; OT primary and idle current not on the sheet). The Classic 50 (long-tail pair, presence, master) and the Delta Blues (this circuit with a tremolo) are not built | Classic 30: Lead channel; Switch 1 the Normal channel, Switch 2 Boost | — | Direct out, both channels, Boost on and off; the OT's primary impedance | 1x12 open (Delta Blues: 1x15 or 2x10 open) |
| EVH 5150III | ✅ Done. Red: drawn from a redraw of the 100S Stealth's channel 3, which stands in for the standard 100 W head's red channel. Green and blue (#306): the standard head's channels one and two, read from Fender's service diagram for the 5150-III main PCA (drawing 0070922000 rev A, 4 August 2006, sheet 1 of 3; thetubestore `Fender-EVH-5150-III-Schematic.pdf`), whose printed test voltages the netlists meet stage by stage (docs/06 §3.4). The MARKTHERENCE "Channel 1" and "Channel 2" redraws of the Stealth are not used: the Channel 2 file cannot be built as drawn (its coupling cap into the gain pot is deleted) | 5150III, red; 5150III Clean/Crunch: channel one (green), Switch 1 channel two (blue) | T3K: 5150III OB1 v2 | Blue and red channel captures with settings. The service diagram also draws the standard head's red channel, which differs from the Stealth redraw (39k on the cold clipper's cathode for 5.1k, a 22 nF bass cap for 47 nF, 100k for 220k on the plate ahead of the follower): the red type could be redrawn from it, or the Stealth kept as a type of its own. Sheets 2 and 3 (power supply, presence and resonance, switching) were not found | 4x12 closed, G12EVH / Vintage 30 |
| Mesa/Boogie Dual Rectifier | ✅ Done (factory two-channel drawing, 1993, both channels as two types; Modern takes the feedback out) | Dual Rectifier (red), Dual Rectifier Orange (Bright = Clean, Switch 1 = Modern) | GPL: Mini Rectifier (not the same amp) | Dual/Triple Rec, modern and vintage modes, tube vs SS rectifier | 4x12 closed, Vintage 30 |
| Bogner Ecstasy | ⏸ Waiting (only a modded "83K" redraw and an OD-channel-only redraw of unknown version; no 100B/101B or 20th Anniversary drawing found) | — | GPL: Ecstasy Blue *pedal* (not the amp) | Blue/red channels; a stock 100B/101B schematic | 4x12 closed, Vintage 30 / Greenback |
| Diezel VH4 (ch 3) | ⏸ Waiting (a 2005 factory sheet gives the topology but no values; an owner's trace gives the stacks only; the clone redraws disagree) | — | — | Channel 3 and 4 values | 4x12 closed, Vintage 30 |
| Mesa/Boogie Mark IV | ✅ Done, all three channels (presence: the rheostat in series with the feedback, as drawn, with the lead and Rhythm 2 pull shifts on the Presence Shift switch) | Mark IV, lead; Mark IV Rhythm 1 (clean; Bright) and Mark IV Rhythm 2 (crunch; Switch 1 Fat), from the factory scan's preamp sheets | GPL: Mark IV | Settings stated | 1x12 / 4x12 closed |
| Carvin Legacy VL100 | ✅ Done (factory schematic) | Legacy VL100: lead; Switch 1 the clean channel | — | Any VL100 capture, settings stated (Vai: Drive 7.5, Bass 6, Mid 4, Treble 8, Presence 8) | 4x12 / 2x12 closed, Vintage 30 |
| Mesa/Boogie Lone Star | ✅ Done (factory drawing, its node voltages included) | Lone Star, both channels (Switch 1 "Channel 2", Switch 2 "Drive Mode") | — | Clean/edge channels, 50 W mode | 1x12 / 2x12 open |
| Bogner Uberschall | ⏸ Waiting (two redraws that disagree on the stack topology and several caps) | — | — | A factory drawing or a third trace | 4x12 closed |
| Diezel Herbert | ⏸ Waiting (nothing found; the service manual gives only the tube functions) | — | — | Schematic | 4x12 closed |
| Engl Powerball | ✅ Done, channel 2 Lo/Hi with its noise gate and channel 1 Clean/Crunch, with the Bottom switches. Two sources: the factory drawing E645-1, whose preamp is known to be wrong (as drawn it has 25 dB more gain than a 5150 and motorboats), and a copy of it corrected by hand from photographs of a disassembled amp ([guitar-gear.ru](https://guitar-gear.ru/forum/topic/4085-cхема-engl-powerball/), post 22, 2016: one person's trace, with its own "?" marks), whose interstage networks the model follows (docs/06 §3.4, §3.7) | Powerball: lead; Switch 3 "Channel 1", Switch 2 "Gain Hi" (Hi Lead / Crunch), Fat "Bottom", Cut "Gate Threshold" | — | A second trace or a service manual to confirm the corrected copy (its "?" values, channel 1's treble caps, the feedback network); Powerball II | 4x12 closed, Vintage 30 |
| Engl Savage 120 | ✅ Done (factory drawing E610-2; no voltages printed, the preamp rails derived from its drawn chain; 4 × 6550 per Engl's E610 specification): channel 2 (Lead and Crunch 2, each Rough or Smooth, with Hi Balance) and channel 1 (Clean and Crunch 1, with Bright, Preshape and Contour) | Savage 120: lead, Rough; Switch 3 "Smooth", Bright "Crunch 2", Cut "Hi Balance". Savage 120 Clean (type 92): Clean; Switch 3 "Crunch 1", Switch 2 "Preshape" | — | Captures with settings (the pelennor "Savage" file is an Axe-Fx capture: never use it); the LT1011 optocoupler's on resistance (Smooth's pad); which position of the Preshape switch is "on", and whether Smooth works in Crunch 2 (the relay logic on sheet 2 was not traced) | 4x12 closed |
| Engl Fireball 60 | ✅ Done (factory drawing 625, one vector sheet, 2003: two 6L6GC, so the 60), the Ultra channel and the clean channel; the presence is the drawn rheostat in series with the feedback. The drawing leaves parts out: V2A's grid is drawn grounded, so the lower leg of its divider is **assumed** (470k, as on the Powerball's drawing); it motorboats as drawn, so the Powerball's **assumed** two 10k / 22 µF supply nodes below CE3 are added (docs/06 §3.7); the Depth switch is drawn across a wire and is ⏸; no voltages below B+ (the rails derived from the drawn chain); idle current and output transformer typical. Fireball 100 (E635) ⏸: no drawing found | Fireball 60: Ultra; Switch 3 "Clean" | — | A second source for the V2A divider, the V1/V2 supply filtering and the Depth switch; a Fireball 100 drawing; captures with settings | 4x12 closed, Vintage 30 |
| Revv Generator 120 | ⏸ Waiting (nothing published: no schematic, trace or redraw found; specs only, 5 × 12AX7, 4 × EL34) | — | — | Schematic | 4x12 closed |
| Fryette (VHT) Deliverance / Sig:X | ⏸ Waiting (Fryette releases no schematics; only a modified 2-channel D60 drawing as a PedalPCB forum attachment of unknown origin, and a "retro-engineered Deliverance" mentioned on MEF whose file was not found; nothing for the 120 or Sig:X) | — | — | Schematic or trace | 4x12 closed |
| Friedman Dirty Shirley | ⏸ Waiting (a hand-drawn layout and a corrected schematic exist as ampgarage forum attachments, login only; not read) | — | — | Read the ampgarage trace (viewtopic.php?p=397086); NAM captures of a Dirty Shirley Mini exist in pelennor2170/NAM_models | 4x12 closed |
| Cameron CCV | ⏸ Waiting (gut photos only; no schematic or trace found) | — | — | Schematic or trace | 4x12 closed |
| Bogner Shiva / XTC | ⏸ Waiting (only hobbyist traces, behind a CAPTCHA on elektrotanya; the Music Electronics Forum XTC attachments need a login) | — | — | Download those traces by hand and compare | 4x12 closed |
| PRS Archon | ⏸ Waiting (no service schematic found on PRS's site or elsewhere; specs only, 6 × 12AX7, 4 × 6L6 or 2 × 6CA7) | — | — | Schematic; the Clean/Lead/Bright variants | 4x12 closed |
| Krank Revolution | ⏸ Waiting (a reverse-engineered Rev 1 preamp and power amp are MEF attachments, thread 987426, login only; not read, and not factory) | — | — | Read the MEF trace | 4x12 closed |
| Victory Kraken / Sheriff | ⏸ Waiting (nothing found; Victory publishes none) | — | — | Schematic | 4x12 closed |
| Randall Satan / RG100 | ⏸ Waiting (Satan: nothing found. RG100: a legible factory scan of the solid-state RG80/RG100 on el34world; it is 4558 op-amps and JFETs. Op-amp stages can be built now (`StageNetlist.OpAmp`, as in the JC-120, Valvestate 8080 and JCM900), and so can JFET stages, one per network (`StageNetlist.Jfet`, as in the GK 800RB's boost stage)) | — | — | Satan schematic; a JFET element in the stage library for the RG100 | 4x12 closed |
| Hughes & Kettner TriAmp | ⏸ Waiting (nothing read yet. Found November 2026 and not opened: a 1999 TriAmp service document with schematics on manualmachine.com, `/hk/triamp/5152155-schematic`, and a 29-page service manual on elektrotanya; which mark they cover is not checked. A MkII service manual is mentioned on forums but not found) | — | — | Read the service document; MkII and Mark 3 drawings | 4x12 closed |
| Wizard Modern Classic | ⏸ Waiting (an MCII schematic and turret layout by a forum member are ampgarage attachments, login only, viewtopic.php?p=398776; the thread text gives only the inverter and feedback values) | — | — | Read the ampgarage trace | 4x12 closed |
| Mesa/Boogie Mark V | ✅ Done (factory drawing), all three channels with their modes, the loop's recovery stage and the per-channel presence | Mark V (channel 3: IIC+ / Mark IV / Extreme), Mark V Clean (channel 1: Clean / Fat / Tweed, Bold), Mark V Crunch (channel 2: Edge / Crunch / Mark I) | GPL (not usable): ToneLibrary SmartAmp Mark 5 high gain (WaveNet format the harness does not run; settings and rig unstated) | 45 W / 10 W modes (a legend for the power amp sheet's relays, and a power amp that can drop a pair); which modes drive the JFET bypasses; the presence rheostats' values | 4x12 / 1x12 closed |
| Mesa/Boogie Triple Rectifier | ✅ Done as a variant (no schematic; the three-channel Dual Rectifier with six 6L6), all three channels | Triple Rectifier (channel 3), Triple Rectifier Orange (channel 2), Triple Rectifier Clean (channel 1) | — | A Triple Rectifier drawing | 4x12 closed, Vintage 30 |
| Mesa/Boogie Rect-o-Verb | ✅ Done (factory Solo 50 / Rectoverb drawing, first series), both channels (Switch 1 Rhythm, Switch 2 Pushed), the loop stage V4 and the Output Level (Control 1); the reverb is left to the Reverb block | Rect-o-Verb | — | Series II drawing | 1x12 / 2x12 closed |
| Sunn Model T | ✅ Done (Sunn drawing D-1029 "Model T" rev A, ECN 441, 25 September 1973, schematicheaven `sunn_model_t.pdf`, with its voltage chart: Brite and Normal channels on a shared cathode, a gain stage and cathode follower into the stack, a master volume, a 12AX7 long-tail pair, four ultralinear 6550. All tubes are 12AX7: no 6AN8 on this drawing. The preamp, inverter and supply rest points match the chart; the idle current, OT primary, UL tap and choke resistance are guessed, the supply's source resistance fitted to the chart's 519 → 481 V at 150 W. The "Model T Super" of rev C (1975) is a different preamp and is not built) | Sunn Model T (Switch 1 Both In, Switch 2 Normal Input) | — | Any capture with stated settings; the OT's primary impedance and tap | 4x12 closed / 6x12 |
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
| Ampeg SVT (1970, 6550) | ✅ Done (both channels, each a type; power-amp nodes and OT assumed) | SVT, SVT Channel 2 | — (no free capture found) | Direct-out captures with settings, Ultra Lo, each Mid Freq | 8x10 closed |
| Ampeg SVT-CL (1994) | ✅ Done (SLM factory drawings: preamp 07S519-11, power amp 07S419 and tube board 07S419-41, alternate speaker board 07P319-14; both inputs, Ultra Hi / Ultra Lo, the five-way Frequency switch on Control 1, Master. The coil's tap inductances, the output transformer's impedance (1.85k from the drawing's 372 V rms a plate at 300 W) and the supply's source resistance are not on the drawings. The SVT-2 Pro is not built: same power amp, but its preamp adds a Drive stage, op-amp buffers and a nine-band graphic EQ) | SVT-CL | — (no free capture found) | Direct-out captures with settings, Ultra Lo, each Frequency position | 8x10 closed |
| Ampeg B-15-N Portaflex (1968) | ✅ Done (one channel, the two being the same circuit, and both jumpered as a second type; on the 8 × 10 speaker load: no data for its own cabinet) | Portaflex B-15N, Portaflex B-15N Jumped | — | Direct-out captures, Ultra Lo / Hi | 1x15 ported (Thiele, 1968) |
| Gallien-Krueger 800RB (1982 boards) | ✅ Done (GK's service manual, el34world `Gallien_krueger_800rb_sm1.pdf`: the 1982 preamp 60045 A, power amps 60044 A and supply 60048 A; full-range mode into the 300 W amp; four-band EQ, Mid Contour, Lo Cut, Hi Boost, −10 dB, the J113 Boost stage; the bi-amp crossover and the 100 W amp not modelled; solid state) | GK 800RB | — | Direct-out and speaker-out captures with settings, Boost sweep | 4x10 / 1x15 |
| Roland JC-120 (1984 board) | ✅ Done (the drawing's one channel, reverb off, with its Distortion knob and diode clipper on Switch 1, Distortion On; solid state; its chorus is the Chorus block's JC-120 type) | JC-120 | — | Direct-out captures, volume sweep, Distortion sweep | 2x12 open |
| Fender Bassman 100 / 135 (bass) | ✅ Done (the 135, from CBS drawing 014283 with the 100's drawing 010303 for the small print: Bass and Normal channels, Deep, Bright, Master, 12AT7 LTP, 4 × 6L6GC ultralinear; the OT and its screen taps assumed; the 100 is the same preamp with pentode screens) | Bassman 135 | — (no free capture found) | Direct-out captures with settings | 4x12 / 2x15 |
| Marshall Valvestate 8080 / 8100 | ✅ Done (Marshall drawings PC0689 issue 5 and PC0689P issue 9, vector PDFs at schematicheaven `marshallamps/valvestate80_80w_8080_8100_8412.pdf`; both channels, solid-state power amp; its current feedback not modelled; the preamp-tube parameters act on the boost channel's ECC83) | Valvestate 8080 | — | Captures | 1x12 / 4x12 closed |

No free (GPL / CC) direct-out capture of any of these was found: GuitarML ToneLibrary and pelennor2170/NAM_models
hold none (searched October 2026; ToneLibrary's Bassman files are the 5F6-A), so the five models are schematic-only. Sources, readings and guesses are in
`docs/06-amp-modelling-spec.md` §3.5 and in each model file. The bass cabs (8 × 10, 1 × 15) are not Cab-block IRs yet:
the B-15 runs on the Circuit Amp's closed 8 × 10 speaker curve.

---

## Counts

Counted over every table in this file, including the two sections added from Fractal's lists and the issues table at
the end (recounted 2026-11-10). Each row is counted once by its status; the ↪ rows (seven in the Axe-Fx sections, and
the issues whose amp has a row above) point at an amp counted in its own row, and the wiki's variants are not counted. A row can stand for
more than one type (a Jumped variant, a second channel) and two rows can share one, so the Done count is not the
number of types.

| Status | Rows |
|---|---|
| ✅ Done (79, one of them partly), done as a variant of another model (1), or covered by another type (1) | 81 |
| 🔨 In progress | 0 |
| 📋 Planned (schematic found, not built yet), 67 of them rows of the issues table | 67 |
| ⏸ Waiting on a schematic or better data, 16 of them rows of the issues table | 70 |
| 💤 Not planned (not researched yet) | 2 |

### Circuit Amp types by index

The Circuit Amp block has 98 types. The "Cathode type" column above names them; this is their order in the Type list.

| # | Type | # | Type | # | Type |
|---|---|---|---|---|---|
| 0 | Blackface | 30 | Express | 60 | Bluesbreaker 1962 |
| 1 | Plexi | 31 | Lone Star | 61 | Hiwatt DR504 |
| 2 | AC30 | 32 | Mark V | 62 | Cornford MK50H II |
| 3 | AC15 | 33 | Rect-o-Verb | 63 | Tiny Terror |
| 4 | DC-30 | 34 | Triple Rectifier | 64 | Liverpool |
| 5 | Prometheus | 35 | Powerball | 65 | ODS 70s |
| 6 | Prometheus Clean | 36 | Savage 120 | 66 | Dual Rectifier 3-Ch |
| 7 | JCM800 2203 | 37 | SVT | 67 | Subway Blues |
| 8 | JCM800 2204 | 38 | Portaflex B-15N | 68 | TriAxis Rhythm |
| 9 | JTM45 | 39 | JC-120 | 69 | TriAxis Lead 1 |
| 10 | Plexi 100W | 40 | Hot Rod Deluxe | 70 | TriAxis Lead 2 |
| 11 | Laney GH100L | 41 | Champ 5F1 | 71 | JCM900 4502 |
| 12 | Deluxe Reverb | 42 | Vibroverb AB763 | 72 | JCM900 4100 |
| 13 | Twin Reverb | 43 | Vibrolux Reverb AA964 | 73 | Valvestate 8080 |
| 14 | Super Reverb | 44 | Bandmaster AB763 | 74 | JVM410H OD |
| 15 | Princeton Reverb | 45 | Tweed Twin 5F8-A | 75 | Concert 6G12 |
| 16 | Bassman 5F6-A | 46 | Tweed Twin 5F8-A Jumped | 76 | Super 6G4 |
| 17 | Bassman 5F6-A Jumped | 47 | Tremolux AA763 | 77 | Deluxe 6G3 |
| 18 | Tweed Deluxe 5E3 | 48 | Princeton 5F2-A | 78 | Vibrolux 6G11 |
| 19 | Tweed Deluxe 5E3 Jumped | 49 | Princeton AA964 | 79 | Bassman 135 |
| 20 | SLO-100 | 50 | Vibro-King | 80 | Scout GA-17RVT |
| 21 | Mark IIC+ | 51 | Blues Junior | 81 | Major 1967 |
| 22 | 5150 | 52 | BE-100 | 82 | Bass 400 |
| 23 | Dual Rectifier | 53 | BE-100 HBE | 83 | JVM410H Clean/Crunch |
| 24 | 5150III | 54 | Silver Jubilee | 84 | DSL100 |
| 25 | Mark IV | 55 | 18 W 1974X | 85 | JMP-1 |
| 26 | Legacy VL100 | 56 | Hiwatt DR103 | 86 | Quick Rod |
| 27 | Chieftain | 57 | 1959SLP | 87 | AFD100 |
| 28 | Hot Cat 30 | 58 | 1987X | 88 | Mark IIC+ Rhythm |
| 29 | ODS 97 | 59 | 1959T Tremolo | 89 | Mark IV Rhythm 1 |
|  |  |  |  | 90 | Mark IV Rhythm 2 |
|  |  |  |  | 91 | Dual Rectifier 3-Ch Orange |
|  |  |  |  | 92 | Dual Rectifier Orange |
|  |  |  |  | 93 | Dual Rectifier 3-Ch Clean |
|  |  |  |  | 94 | Triple Rectifier Orange |
|  |  |  |  | 95 | Triple Rectifier Clean |
|  |  |  |  | 96 | Mark V Clean |
|  |  |  |  | 97 | Mark V Crunch |
|  |  |  |  | 92 | SVT Channel 2 |
|  |  |  |  | 93 | Portaflex B-15N Jumped |
|  |  |  |  | 92 | Champ AA764 |
|  |  |  |  | 92 | TSL100 |

Types 81 to 93, the newest, are:

| # | Type | Family | Modelled after |
|---|---|---|---|
| 81 | Major 1967 | Marshall | Marshall Major (Unicord 70-02-12, 200 W, four KT88), Treble and Normal channels |
| 82 | Bass 400 | Mesa/Boogie | Mesa/Boogie Bass 400 (1985 factory scan) |
| 83 | JVM410H Clean/Crunch | Marshall | Marshall JVM410H, the Clean/Crunch channel (the OD channels are type 74) |
| 84 | DSL100 | Marshall | Marshall JCM2000 DSL100 (Dual Super Lead), Clean/Crunch and Lead 1/2 with Tone Shift and Deep |
| 85 | JMP-1 | Marshall | Marshall JMP-1 MIDI valve preamp (Clean I/II, OD I/II) into a 100 W EL34 power amp |
| 86 | Quick Rod | Splawn | Splawn Quick Rod, the two-channel version with three 12AX7s |
| 87 | AFD100 | Marshall | Marshall AFD100 (Slash), both modes and the Low input |
| 88 | Mark IIC+ Rhythm | Mesa/Boogie | Mesa/Boogie Mark IIC+, the rhythm channel (the lead channel is type 21) |
| 89 | Mark IV Rhythm 1 | Mesa/Boogie | Mesa/Boogie Mark IV, Rhythm 1, the clean channel (the lead channel is type 25) |
| 90 | Mark IV Rhythm 2 | Mesa/Boogie | Mesa/Boogie Mark IV, Rhythm 2, the crunch channel |
| 91 | Dual Rectifier 3-Ch Orange | Mesa/Boogie | Mesa/Boogie Dual Rectifier (3-channel), channel 2 |
| 92 | SVT Channel 2 | Ampeg | Ampeg SVT (1970, 6550), channel two (channel one is type 37) |
| 93 | Portaflex B-15N Jumped | Ampeg | Ampeg B-15-N Portaflex (1968), both channels jumpered (one channel is type 38) |
| 92 | Champ AA764 | Fender | Fender Champ AA764 (blackface and silverface, 5 W, one 6V6GT), Volume, Treble and Bass |
| 92 | TSL100 | Marshall | Marshall JCM2000 TSL100 (Triple Super Lead), Clean, Crunch and Lead with Mid Boost, Tone Shift and Deep |

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

Every amp below was missing from the catalogue, except the ↪ rows, which repeat an amp from the tables above to give
its Axe model names. The Axe model names are given so you can find captures of the same amp.

### Fender

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Fender Bassman AB165 (blackface) | ⏸ Waiting (the only scan, schematicheaven `bassman_ab165_schem.pdf`, is too poor for the Bass channel's stack and the bias network; topology and power section read) | — | 65 Bassguy Bass / Normal | A legible AB165 drawing | 2x12 closed |
| Fender Concert 6G12 (brownface) | ✅ Done (factory drawing, schematicheaven `concert_6g12_schem.pdf`: both channels, the harmonic vibrato as a stage with Speed and Intensity on the tremolo controls and the pedal on Switch 2; OT primary assumed) | Concert 6G12 | 6G12 Concert | OT primary (45249, 2 Ω secondary); captures | 4x10 open |
| Fender Super 6G4 (brownface) | ✅ Done (factory drawing `super_6g4_schem.pdf`: the 6G12's circuit with its harmonic vibrato on the tremolo controls, 10k feedback, GZ34) | Super 6G4 | 6G4 Super | OT primary (45216); captures | 2x10 open |
| Fender Tremolux AA763 | ✅ Done (optical vibrato with Speed and Intensity, the Intensity pot's 50k track in the mixer) | Tremolux AA763 | Tremolo Lux | Direct out | 2x10 closed |
| Fender Vibro-King | ✅ Done (rev F drawing; the optical vibrato from rev A, the Intensity pot's 250k track on the stack's output) | Vibro-King | Vibra-King, Vibra-King Fat | Direct out, Fat on and off | 3x10 open |
| Fender Blues Junior | ✅ Done (III drawing) | Blues Junior | JR Blues, JR Blues Fat | Direct out (GuitarML's Blues Jr files are miked) | 1x12 open |
| Fender Pro Junior | ✅ Done (factory drawing 047420 rev C, schematicheaven `pro_jr.pdf`: both V1 stages, the Volume and Tone network, the inverter, two EL84 and the supply; its DC and AC test points checked; OT primary and idle current assumed) | Pro Junior | — | OT primary (041781); captures | 1x10 open |
| Fender Princeton 5F2 / AA964 | ✅ Done | Princeton 5F2-A, Princeton AA964 | Prince Tone 5F2, Prince Tone AA964 | Direct out | 1x10 open |
| Fender Vibro-Champ (Eric Clapton) | ⏸ Waiting (no schematic published: Fender's product pages, schematicheaven and the forums searched; Fractal's note says cathode bypass caps were added to the 5F1, but not where) | — | 5F1 Tweed EC | Fender's EC Vibro-Champ schematic | 1x8 open |

### Marshall and British

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Marshall 1959SLP reissue | ✅ Done (factory drawing 1959-01-60-02 issue 2, DrTube; presence pot as the tail's shunt) | 1959SLP | 1959SLP Jump / Normal / Treble | | 4x12 closed, Greenback |
| Marshall 1987X reissue | ✅ Done (factory drawing 1987-01-60-02 iss 4, as drawn with the 33k slope; the "tone-stack mod" is not documented anywhere found; presence pot as the tail's shunt) | 1987X | 1987X Jump / Normal / Treble | | 4x12 closed |
| Marshall JMP 1959T Super Lead (Tremolo) | ✅ Done (Unicord drawing 70-11-11, July 1970, DrTube `1959tu.gif`; the tremolo at rest, its load on the normal volume modelled; the tremolo itself is not built (#291): the oscillator and the base network read on the scan, but the switching transistor's type and the oscillator's supply node do not, and they decide whether the switch closes at all; presence pot as the tail's shunt) | 1959T Tremolo | Brit Super | | 4x12 closed |
| Marshall JMP-1 preamp | ✅ Done, Clean I/II and OD I/II with Bass Shift and the active four-band tone circuit, into the JCM900 4100's documented 100 W EL34 power amp (DrTube `jmp1-61-04.pdf`, 6 sheets, iss 10, vector; the TC9176/TC9170 digital pots as 10k pots at the knob position, their value not drawn; the preamp on its own supply, so the power amp's sag never reaches its valves) | JMP-1 | Brit Pre, JMPRE-1 OD1 / OD2 (+ bass) | A JMP-1 capture with stated settings | 4x12 closed |
| Marshall AFD100 (Slash) | ✅ Done, both modes (Switch 1 AFD, off = #34) and the Low input, from the main-board drawing AFD100-60-02 iss 1 (10-6-10, vector, `marshall_afd100_main_sch.pdf`: the whole preamp, inverter, four 6550 and supply). The modes are relays RL1/RL3 (#34 at rest: V6B in the chain; AFD: V6B skipped, V6A's cathode bypassed); which relay state the AFD LED means is inferred. B+, idle current, OT and feedback tap guessed (2203 values); the Power control (screen regulator) at full. Over the high-gain CPU budget by choice (accuracy first): #34 18.8 / 38.6 %, AFD 22.2 / 52.0 %, the AFD chain up to the send solved as one five-triode circuit | AFD100 | Brit AFS100 1 / 2 | Any capture with the mode stated | 4x12 closed |
| Marshall JVM410HJS (Satriani) | ⏸ Waiting (DrTube jvm410hjs-60-02 iss 2, -61-02 iss 1, -62-02 iss 1, vector with text, fetched and read in part: the same channel networks and three-path cascade as the JVM410H but five main-board relays (RL101, RL103 B/C, RL104, RL105) where the JVM410H has three, an input buffer and the THAT4301 gate in the path, and mid-shift relays on the stacks. Which relays each of the nine gain structures sets is firmware; unlike the JVM410H's, the HJS handbook describes the modes' voicings ("Inspired by a Master Volume, JMP", "A lot of Gain") but not their gain stages, so they cannot be deduced) | — | JS410 Crunch / Lead | A measured relay table, or captures per mode | 4x12 closed, Vintage 30 |
| Friedman Small Box | ⏸ Waiting (nothing found; GuitarML's `FriedmanSmallBox_GainKnob` may be the pedal and is undocumented) | — | Friedman Small Box | | 2x12 / 4x12 closed |
| Cornford MK50H II | ✅ Done (Martin Kidd's own drawing, 2011, 5 sheets: both channels, one of the two masters; no voltages on it) | Cornford MK50H II | Corncob M50 | Overdrive channel | 4x12 closed |
| Orange A30 / AD30 | ⏸ Waiting (factory sheet cd0115 issue 5, 2000, is the main board only: V1's two stages, the inverter, four EL84 and the supply, with test-point voltages; the gain, tone and channel circuits are on the front-panel board, reached through the GAIN and EQ connectors, and its sheet is not in the file. el34world, schematicheaven and thetubestore have nothing else; their AD15 file is a different amp) | — | Citrus A30 Clean / Dirty | Front-panel board sheet | 2x12 closed |
| Orange Tiny Terror | ✅ Done (vector redraw, not a factory sheet: el34world `Orange/Orange_tiny_terror.pdf`; 15 W; its Tone on the Presence knob, clockwise brighter) | Tiny Terror | Citrus Terrier | | 1x12 / 2x12 closed |
| Orange OR120 / OR80 Graphic Mk II ("Pics Only") | ✅ Done (#359; the factory drawing of 1972, schematicheaven `orange_mkii1972.pdf`, with B. Hickmott's 2004 redraw `orange_graphic_mkii_or120.pdf`: Bass, Treble, Volume, the six-way FAC on Control 1 and the HF Drive on the Presence knob; V2A with the feedback at its cathode inside the loop, a cathodyne, four or two EL34. Rails from the 1974 sheet's readings; B+ at the plates, idle current and both output transformers guessed. The HF Drive acts on the feedback only, not on V2A's own degeneration, and its 2 mH choke is left out) | OR120, OR80 | — | OT primaries; captures with stated knob settings | 4x12 closed, Greenback / G12H |
| Orange AD200B (bass) | ⏸ Waiting (no schematic found: searched schematicheaven, el34world, MEF and retailer pages; the MkIII is 2 × ECC83, 1 × ECC81, 4 × 6550, Gain/Bass/Middle/Treble/Volume) | — | Citrus Bass 200 | Any schematic or trace | 4x10 / 8x10 closed |
| Blankenship Leeds 21 | ⏸ Waiting (nothing found) | — | Blanknshp Leeds | | 1x12 / 2x12 |
| Vox AC30HW (hand-wired) | ⏸ Waiting (no service schematic found, only the owner's manual; the AC30 (2) is the JMI circuit it reissues) | — | Class-A 30w, 30W Bright, 30w Hot | Service schematic | 2x12 open, Alnico Blue |

### Boutique and class A

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Dumble ODS (HRM and Robben Ford versions) | ✅ Done as the 1970s ODS (schematicheaven `dumbleamps/` preamp, overdrive, supply and output redraws: Rock/Jazz, Deep, Bright, Accent; no HRM drawing found, so no HRM stack; no Master: none is drawn on the sheets, and Guitar Player's piece on the 1977 combo no. 0077, which has the same bright, deep and accent switches, describes its master volume as a control added later on the rear panel, so the model has none, #195) | ODS 70s (Switch 1 Overdrive, Switch 2 Jazz, Switch 3 Deep, Presence = Accent) | — (the GPL Dumble kit and clone captures stay on ODS 97: their circuits are not stated) | HRM schematic; captures of a 1970s ODS | 1x12 / 2x12 open |
| Fuchs Overdrive Supreme | ⏸ Waiting (searched 2026-10: no schematic) | — | Fox ODS, Fox ODS Deep | Schematic | 1x12 / 2x12 open |
| Two-Rock Jet 35 | ↪ See the Two-Rock row (⏸ Waiting; searched 2026-10: no schematic) | — | Two Stone J35 1 / 2 | Schematic | 1x12 open |
| Bludotone Ojai | ⏸ Waiting (searched 2026-10: no schematic; said to follow Robben Ford's "Tan" Dumble) | — | Bludojai Clean / Lead 1 / Lead 2 | Schematic | 1x12 / 2x12 open |
| Carr Rambler | ⏸ Waiting (searched 2026-10: no schematic) | — | Car Roamer, FAS Class-A | Schematic | 1x12 / 2x12 open |
| Budda Twinmaster | ⏸ Waiting (searched 2026-10: no schematic) | — | Buttery | Schematic | 2x12 open |
| Divided by 13 CJ 11 / FTR 37 | ↪ See the Divided by 13 row (⏸ Waiting) | — | Div/13 CJ, CJ Boost, FT37 High / Low | Schematic | 1x12 / 2x12 |
| Dr Z Route 66 / MAZ 8 / MAZ 38 SR | ↪ See the Dr Z row (⏸ Waiting) | — | Mr Z Hwy 66, MZ-8, MZ-38 | Schematic | 1x12 / 2x12 open |
| Morgan AC20 Deluxe | ↪ See the Morgan row (⏸ Waiting) | — | AC-20 12AX7 / EF86, Bass / Treble | Schematic | 1x12 open |
| Bad Cat Hot Cat 30 | ↪ See the Bad Cat row (✅ Done) | Hot Cat 30 | Hot Kitty | Captures | 2x12 open |
| Matchless Chieftain | ↪ See the Matchless Chieftain row (✅ Done) | Chieftain | Boutique 1 / 2 | Captures | 2x12 open |
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
| Mesa/Boogie TriAxis preamp | ✅ Done (factory audio sheets rev 1.08 with the LDR mode table; LDR ranges taken from the Mark IIC+, the Dynamic Voice LDR 1M so that 0 is flat; presence LDR fixed) into a generic power amp (the Dual Rectifier model's 4 × 6L6) | TriAxis Rhythm (Switch 1 Yellow), TriAxis Lead 1, TriAxis Lead 2 (Switch 1 Yellow, Switch 2 Red; Control 1 lead drive, Control 2 Dynamic Voice) | — | Captures with settings | 4x12 closed |
| Mesa/Boogie JP-2C | ⏸ Waiting (no drawing found; the Mark IIC+ model covers its channel 3 circuit) | — | USA JP IIC+ Green / Yellow / Red | | 4x12 closed |
| Mesa/Boogie Triple Crown TC-100 | ⏸ Waiting (no drawing found) | — | Triple Crest 2 / 3 | | 4x12 closed |
| Mesa/Boogie Subway Blues | ✅ Done, partly (factory drawings read; the potted "Toning Module" is not drawn and is left out) | Subway Blues (Switch 1 Fat) | — | The toning module's contents | 1x10 open |
| Mesa/Boogie Dual Rectifier, 2- and 3-channel revisions | ✅ Done (both factory scans: the two-channel 1993 RF-1F set now drives the Dual Rectifier type, which the redraw had wrong; the three-channel Solo Head set is a new type and the Triple Rectifier's base) | Dual Rectifier (2-channel, red), Dual Rectifier Orange (2-channel, orange; Bright = Clean, Switch 1 = Modern), Dual Rectifier 3-Ch (channel 3; Bright = Raw), Dual Rectifier 3-Ch Orange (channel 2; Bright = Raw), Dual Rectifier 3-Ch Clean (channel 1; Switch 1 = Pushed), Triple Rectifier and its Orange and Clean | — | Raw vs Vintage captures; which way the J175 across V2a's 475k switches on the three-channel's channel 1 (read as on, as the Rect-o-Verb wires it); a second source for the presence relays' modes (the drawing's handwritten notes and its printed relay driver disagree) | 4x12 closed, Vintage 30 |
| Soldano X88R preamp | ⏸ Waiting (a legible single-sheet redraw, "SOLDANO X88R" 13 Jul 2002, on el34world, Soldano_x88r.pdf; its origin is unknown and owners doubt it; not built) | — | Solo 88 Clean / Rhythm / Lead | A second source to confirm the redraw | 4x12 closed |
| Soldano/Caswell X99 preamp | ⏸ Waiting (a legible scan of a hand-labelled drawing on el34world, Soldano_x99.pdf, no title block; Fractal says its rhythm channel is the SLO-100's; not built) | — | Solo 99 Clean / Lead | Confirm the drawing; a power amp to pair it with (Fractal uses the SLO's) | 4x12 closed |
| CAE 3+ SE preamp | ⏸ Waiting (a schematic of unknown origin is a GroupDIY attachment, thread 51582, which refused automated access; not read) | — | CA3+ Clean / Rhythm / Lead | Read the GroupDIY drawing | 4x12 closed |
| Bogner Fish preamp | ⏸ Waiting (not on el34world or schematicheaven, which hold only the Ecstasy OD, Triple Giant and Uberschall redraws) | — | Bogfish Brown / Strato | | 4x12 closed |
| Cameron Atomica | ⏸ Waiting (descriptions only: a Super Lead with a Jose-style master and a diode-clipping switch; the Ceriatone Molecular is a clone, also without a schematic) | — | Atomica High / Low | Schematic | 4x12 closed |
| Hook Captain 34 | ⏸ Waiting (nothing found) | — | Capt Hook 1A–3B | Schematic | 4x12 closed |
| Splawn Nitro | ⏸ Waiting (nothing for the Nitro; Splawn says it has no schematics; a Quick Rod schematic circulates behind member-only sites) | — | Spawn Nitrous 1 / 2 | Schematic | 4x12 closed |
| Fryette D60 | ↪ See the Fryette row (⏸ Waiting: only a modified 2-channel D60 drawing of unknown origin, PedalPCB forum post 322300) | — | Fryette D60 Less / More | Schematic or trace | 4x12 closed |

### Bass

| Amp | Status | Cathode type | Axe models | Look for | Typical cab |
|---|---|---|---|---|---|
| Mesa/Boogie Bass 400 | ✅ Done (factory 1985 scan, el34world `Boogie_bass400.pdf`, rendered with the PDF's page transforms, which makes the volume return (ground) and V3's 62k loads readable; both inputs, Bass/Treble Shift, the six-band LC graphic EQ (five EQ bands plus Control 2), cascaded differential driver via `DifferentialDriverDesign`, six 6550; V4's plate resistors, the EQ's 10k input/feedback resistors and the OT guessed. No redistributable capture found) | Bass 400 | — | Any capture | 8x10 / 4x10 closed |

Already in the catalogue and in the Axe list (no row added): Bassman 5F6-A, Champ 5F1, Tweed Twin 5F8, Bandmaster,
Tweed Deluxe 5E3, Deluxe Reverb, Twin Reverb, Super Reverb, Princeton Reverb, Vibrolux, Vibroverb, Supro 1624T, JTM45,
the Plexi 50 W and 100 W, JMP 2204, JCM800 2203 and 2204, Silver Jubilee, JVM410H, Friedman BE-100 and Dirty Shirley,
Hiwatt DR103, Orange Rockerverb, Splawn Quick Rod, Suhr Badger, AC15, AC30 Top Boost, Matchless DC-30, Trainwreck
Express, SLO-100, Mark IIC+, Mark IV, Dual Rectifier, Lone Star, 5150 / 6505 (the Axe list's 6505+ is the 5150 II, which
has its own row and the types 6505+ and 6505+ Rhythm), 5150III, Bogner Ecstasy, Überschall and
Shiva, Diezel VH4 and Herbert, Engl Powerball and Savage, Carvin Legacy, Cameron CCV, Ampeg SVT and B-15, Roland JC-120.

### Added from Fractal's current wiki list (October 2026)

Fractal's wiki page "Amp models" lists every model in current firmware (Axe-Fx III, FM3, FM9, AM4, ICONS): about 330
models. Against the catalogue above it adds these real amps:

| Amp | Family | Status | Cathode type | Axe models | Typical cab |
|---|---|---|---|---|---|
| Fender Deluxe 6G3 (brownface) | Fender | ✅ Done (factory drawing `deluxe_6g3_schem.pdf`: Normal and Bright channels, Tone, the split-load second stage; the bias tremolo with Speed and Intensity) | Deluxe 6G3 | Deluxe 6G3 | 1x12 open |
| Fender Vibrolux 6G11 (brownface, 1962/63) | Fender | ✅ Done (factory drawing `vibrolux_6g11_schem.pdf`: Normal and Bright channels; the drawing has a bias tremolo, not the harmonic vibrato: built, with Speed and Intensity; OT assumed; a 1 x 12 combo) | Vibrolux 6G11 | Vibrato Lux 6G11 | 1x12 open |
| Supro 1695T Black Magick | US vintage | ⏸ Waiting (a 2016 reissue-era design by Absara; no schematic published: only specifications, 2 × 6973 cathode-biased, 25 W) | — | Supremo Black Magick | 1x12 open |
| Supro 1964T | US vintage | ⏸ Waiting (no schematic for a "1964T" found; the 2014 reissues' S6420 / S6424 schematics may be related) | — | Supremo 1964T | 1x12 open |
| Marshall SV20H (Studio Vintage, 20 W Plexi) | Marshall | 💤 Not planned (not searched yet) | — | Plexi Studio 20 | 2x12 / 4x12 closed |
| Marshall SC20H (Studio Classic, 20 W JCM800) | Marshall | 💤 Not planned (not searched yet) | — | Brit 800 Studio 20 | 2x12 / 4x12 closed |
| Diamond Del Fuego | Modern high gain | ⏸ Waiting (nothing found; specs: 3 × 12AX7, 2 × EL84, 6CA4 rectifier, 22 W, designed with Roy Blankenship) | — | Del Fuego | 4x12 closed |

The wiki also names variants of amps already listed, which can be covered by switches or extra types once the base amp
exists. They are not counted in the Counts table: each belongs to a row above or to an issue in the next section.
"Schematic" is what the issue's search found (2026-11), not a reading of the drawing.

| Variant | Status | Issue |
|---|---|---|
| Fender '59 Bassman LTD reissue | 📋 Planned (schematic: available) | [#404](https://github.com/Rikarin/cathode/issues/404) |
| Fender Twin Reverb, 1971 silverface | 📋 Planned (schematic: available) | [#403](https://github.com/Rikarin/cathode/issues/403) |
| Fender Vibroverb, old (6G16) and Custom reissue | 📋 Planned (schematic: available) | [#479](https://github.com/Rikarin/cathode/issues/479) |
| Marshall Plexis dated 1967, 1969, 1970 and 1972, and the 50 W Plexi with 6CA7 tubes | 📋 Planned (schematic: available) | [#420](https://github.com/Rikarin/cathode/issues/420) |
| Marshall JCM800 2203 / 2204 High input | ✅ Done (the JCM800 2203 and JCM800 2204 types) | — |
| Marshall JCM800 2203 / 2204 Low input | 📋 Planned (schematic: available; the types have no Low input yet) | [#419](https://github.com/Rikarin/cathode/issues/419) |
| EVH 5150III, the standard 100 W head | ⏸ Waiting (the 5150III type is drawn from the 100S Stealth's channel 3, the only drawing found, so it is the Stealth that is covered and the standard amp that would be the variant) | — |
| Mesa Mark IIC+ Bright and Deep | ✅ Done (the Mark IIC+ type's Bright and Deep switches; its rhythm channel is the Mark IIC+ Rhythm type, from `Boogie_mkiic_plus.pdf`) | — |
| Mesa Mark IV rhythm and mid-gain voicings | ✅ Done (the Mark IV Rhythm 1 and Rhythm 2 types, from `Boogie_mkiv.pdf`; the mid-gain voicing is Rhythm 2) | — |
| Mesa Mark V IIC+ / MKIV / XT modes | ✅ Done (the Mark V type's mode switches) | — |
| Mesa JP-2C Shred | ⏸ Waiting (schematic: none found) | [#500](https://github.com/Rikarin/cathode/issues/500) |
| Bogner Ecstasy 20th Anniversary | ⏸ Waiting (schematic: partial; no 20th Anniversary drawing) | [#413](https://github.com/Rikarin/cathode/issues/413) |
| Diezel Herbert MkIII | ⏸ Waiting (schematic: none found) | [#490](https://github.com/Rikarin/cathode/issues/490) |
| Carvin VL300 | ⏸ Waiting (schematic: partial) | [#496](https://github.com/Rikarin/cathode/issues/496) |
| Revv Generator Green / Purple / Red | ⏸ Waiting (schematic: none found) | [#491](https://github.com/Rikarin/cathode/issues/491) |
| PRS Archon Clean / Lead / Bright | ⏸ Waiting (schematic: none found) | [#493](https://github.com/Rikarin/cathode/issues/493) |
| Fryette D60 Less / More | ⏸ Waiting (schematic: none found) | [#492](https://github.com/Rikarin/cathode/issues/492) |
| Two-Rock PAB switch variants | ⏸ Waiting (schematic: partial) | [#460](https://github.com/Rikarin/cathode/issues/460) |
| ODS PAB switch variants | ⏸ Waiting (the Overdrive Special types have no PAB switch; the sources are the forum traces described in the Dumble row) | — |

The same list as the wiki gives it:
- **Fender:** a '59 Bassman LTD reissue, a 1971 silverface Twin Reverb, and old and Custom reissue Vibroverbs.
- **Marshall:** Plexis dated 1967, 1969, 1970 and 1972, a 50 W Plexi with 6CA7 tubes, and JCM800 2203/2204 High and Low inputs.
- **High gain:**
  - EVH: the standard 5150III 100 W head. The 5150III type is drawn from the 100S Stealth's channel 3, the only
    drawing found when it was built, so its red channel is the Stealth's. The standard head's channels one and two
    are the 5150III Clean/Crunch type, from Fender's service diagram (#306), which also draws the standard red
    channel; that one is not built yet.
  - Mesa: Mark IIC+ Bright/Deep, the Mark IV rhythm and mid-gain voicings, the Mark V IIC+/MKIV/XT modes (now the
    Mark V type's mode switches), and JP-2C Shred. The IIC+ rhythm channel is the Mark IIC+ Rhythm type and the
    Mark IV's are the Mark IV Rhythm 1 and Rhythm 2 types, read from the factory scans (Boogie_mkiic_plus.pdf,
    Boogie_mkiv.pdf); the Mark IV's mid-gain voicing is its Rhythm 2.
  - Others: Ecstasy 20th Anniversary (no drawing found), Diezel Herbert MkIII (no drawing found), Carvin VL300, Revv Generator Green/Purple/Red, PRS Archon Clean/Lead/Bright, Fryette D60 Less/More.
- **Boutique:** Two-Rock and ODS PAB switch variants.

For each model the wiki page also gives the original amp's controls, power tubes, matching speakers and Fractal's comments.
That is useful when building a model: check the controls and tubes against the schematic.

## Amps asked for in issues

The 1.0.0-rc.5 audit compared the Circuit Amp types with this file and with the model lists of the Axe-Fx III, Helix,
Quad Cortex, TONEX and AmpliTube, and filed one issue per missing amp, variant or channel: 153 issues, #357 to
#509, labelled `amp model`. Each issue gives the circuit in a line, where a schematic was found, what the engine would
need beyond a netlist, and the typical cab. [#510](https://github.com/Rikarin/cathode/issues/510) lists 126 further
low-priority candidates in one issue; they have no rows here.

One row per issue, as filed. "Schematic" is the issue's own finding: *available* means a drawing was found or listed
(most were seen on a site's index and not opened: check that it can be read before building), *partial* a trace of
unknown origin or part of the amp, *not confirmed* a lead nobody could check, *none found* nothing. The status follows
from it: 📋 Planned with a schematic available, ⏸ Waiting otherwise. An amp that already has a row in the tables above
keeps its status there (↪) and is not counted twice; the 83 others are counted in the Counts table. When
an issue is built, give the amp a row in its family's table above, with the type's name, and mark it here.

| Issue | Amp, variant or channel | Family | Kind | Priority | Schematic | Status | Typical cab |
|---|---|---|---|---|---|---|---|
| [#357](https://github.com/Rikarin/cathode/issues/357) | Ampeg SVT-CL / SVT-2 Pro | Bass | New amp | high | available | 📋 Planned | 8x10 closed |
| [#358](https://github.com/Rikarin/cathode/issues/358) | Gallien-Krueger 800RB | Bass | New amp | high | available | 📋 Planned | 4x10 / 1x15 |
| [#359](https://github.com/Rikarin/cathode/issues/359) | Orange OR120 / OR80 Graphic ('Pics Only') | British | New amp | high | available | 📋 Planned | 4x12 closed, Greenback / G12H |
| [#360](https://github.com/Rikarin/cathode/issues/360) | Fender Pro Junior | Fender | New amp | high | available | 📋 Planned | 1x10 open, Eminence/Jensen |
| [#361](https://github.com/Rikarin/cathode/issues/361) | Fender Champ AA764 / Vibro Champ AA764 | Fender | New amp | high | available | 📋 Planned | 1x8 open |
| [#362](https://github.com/Rikarin/cathode/issues/362) | Peavey 6505+ / 5150 II | High gain | New amp | high | available | ↪ See its row above | 4x12 closed, Sheffield / Vintage 30 |
| [#363](https://github.com/Rikarin/cathode/issues/363) | Peavey Classic 30 / Classic 50 (and Delta Blues) | High gain | New amp | high | available | 📋 Planned | 1x12 / 4x10 open |
| [#364](https://github.com/Rikarin/cathode/issues/364) | ENGL Fireball 60 / 100 | High gain | New amp | high | available | 📋 Planned | 4x12 closed, Vintage 30 |
| [#365](https://github.com/Rikarin/cathode/issues/365) | Sunn Model T | High gain | New amp | high | available | 📋 Planned | 4x12 closed / 6x12 |
| [#366](https://github.com/Rikarin/cathode/issues/366) | Marshall Super Bass 100 (1992) | Marshall | New amp | high | available | 📋 Planned | 4x12 closed / 4x15 |
| [#367](https://github.com/Rikarin/cathode/issues/367) | Marshall JCM800 2210 / 2205 (split channel) | Marshall | New amp | high | available | 📋 Planned | 4x12 closed, G12T-75 |
| [#368](https://github.com/Rikarin/cathode/issues/368) | Marshall JCM2000 TSL100 | Marshall | New amp | high | available | 📋 Planned | 4x12 closed |
| [#369](https://github.com/Rikarin/cathode/issues/369) | Randall RG100ES (and RG80 / RG100 Classic) | Solid state | New amp | high | available | ↪ See its row above | 4x12 closed |
| [#370](https://github.com/Rikarin/cathode/issues/370) | Supro 1624T Dual-Tone | US vintage | New amp | high | available | ↪ See its row above | 1x12 open, Jensen C12Q |
| [#371](https://github.com/Rikarin/cathode/issues/371) | Supro Thunderbolt S6420 | US vintage | New amp | high | available | 📋 Planned | 1x15 open |
| [#372](https://github.com/Rikarin/cathode/issues/372) | Silvertone 1484 Twin Twelve | US vintage | New amp | high | available | 📋 Planned | 2x12 closed (separate cab), Jensen C12Q |
| [#373](https://github.com/Rikarin/cathode/issues/373) | Vox AC15C1 (Custom) | Vox/class A | New amp | high | available | 📋 Planned | 1x12 open, Greenback / Alnico Blue |
| [#374](https://github.com/Rikarin/cathode/issues/374) | Mesa/Boogie Bass 400+ | Bass | Variant | medium | partial | ⏸ Waiting | 8x10 / 2x15 |
| [#375](https://github.com/Rikarin/cathode/issues/375) | Ampeg SVT-4 Pro / SVT-3 Pro | Bass | New amp | medium | available | 📋 Planned | 8x10 / 4x10 |
| [#376](https://github.com/Rikarin/cathode/issues/376) | Ampeg V-4B | Bass | New amp | medium | available | 📋 Planned | 2x15 / 8x10 |
| [#377](https://github.com/Rikarin/cathode/issues/377) | Ampeg B-15N 1964 (B-15NC) and 1966 (B-15NF) versions | Bass | Variant | medium | available | 📋 Planned | 1x15 closed |
| [#378](https://github.com/Rikarin/cathode/issues/378) | Acoustic 360 / 361 | Bass | New amp | medium | partial | ⏸ Waiting | 1x18 folded horn |
| [#379](https://github.com/Rikarin/cathode/issues/379) | Trace Elliot GP12 / AH250 | Bass | New amp | medium | available | 📋 Planned | 4x10 |
| [#380](https://github.com/Rikarin/cathode/issues/380) | Aguilar DB750 / DB751 | Bass | New amp | medium | partial | ⏸ Waiting | 4x10 / 8x10 |
| [#381](https://github.com/Rikarin/cathode/issues/381) | Sunn Coliseum 300 (bass) | Bass | New amp | medium | available | 📋 Planned | 2x15 |
| [#382](https://github.com/Rikarin/cathode/issues/382) | Sunn 200S / 2000S | Bass | New amp | medium | available | 📋 Planned | 2x15 |
| [#383](https://github.com/Rikarin/cathode/issues/383) | Fender Bassman 300 Pro / Super Bassman | Bass | New amp | medium | available | 📋 Planned | 8x10 / 4x10 |
| [#384](https://github.com/Rikarin/cathode/issues/384) | Dr Z Route 66 | Boutique | New amp | medium | available (read 2026-11-10, see the Dr Z row) | ↪ See its row above | 2x12 open |
| [#385](https://github.com/Rikarin/cathode/issues/385) | Tone King Imperial (MkI / MkII / 20th Anniversary) | Boutique | New amp | medium | partial | ⏸ Waiting | 1x12 open |
| [#386](https://github.com/Rikarin/cathode/issues/386) | Orange Rockerverb 50 MkI (and MkII / MkIII) | British | New amp | medium | partial | ↪ See its row above | 2x12 / 4x12 closed, Vintage 30 |
| [#387](https://github.com/Rikarin/cathode/issues/387) | Orange AD30 (A30) | British | New amp | medium | partial | ↪ See its row above | 2x12 closed |
| [#388](https://github.com/Rikarin/cathode/issues/388) | Orange Thunderverb 200 / 50 | British | New amp | medium | partial | ⏸ Waiting | 4x12 closed |
| [#389](https://github.com/Rikarin/cathode/issues/389) | Hiwatt DR201 (200 W) | British | New amp | medium | available | 📋 Planned | 4x12 / 4x15, Fane |
| [#390](https://github.com/Rikarin/cathode/issues/390) | Laney Supergroup 100 (LA100BL, Mk1) | British | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#391](https://github.com/Rikarin/cathode/issues/391) | Laney AOR Pro-Tube 50 / 100 | British | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#392](https://github.com/Rikarin/cathode/issues/392) | Laney VH100R | British | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#393](https://github.com/Rikarin/cathode/issues/393) | Sound City 50 Plus / 120 | British | New amp | medium | available | 📋 Planned | 4x12 closed, Fane |
| [#394](https://github.com/Rikarin/cathode/issues/394) | Fender Blues Deluxe / Blues DeVille (and Hot Rod DeVille) | Fender | New amp | medium | available | 📋 Planned | 1x12 / 2x12 / 4x10 open |
| [#395](https://github.com/Rikarin/cathode/issues/395) | Fender Super-Sonic 22 / 60 | Fender | New amp | medium | available | 📋 Planned | 1x12 open |
| [#396](https://github.com/Rikarin/cathode/issues/396) | Fender Showman / Dual Showman AB763 (and Dual Showman Reverb AA769) | Fender | Variant | medium | available | 📋 Planned | 1x15 / 2x15 closed, JBL D130F |
| [#397](https://github.com/Rikarin/cathode/issues/397) | Fender Pro Reverb AA165 | Fender | Variant | medium | available | 📋 Planned | 2x12 open |
| [#398](https://github.com/Rikarin/cathode/issues/398) | Fender Deluxe 5C3 / 5D3 (TV and wide panel) | Fender | New amp | medium | available | 📋 Planned | 1x12 open |
| [#399](https://github.com/Rikarin/cathode/issues/399) | Fender Bandmaster 5E7 (tweed, 3x10) | Fender | New amp | medium | available | 📋 Planned | 3x10 open |
| [#400](https://github.com/Rikarin/cathode/issues/400) | Fender Super 5F4 (tweed, 2x10) | Fender | New amp | medium | available | 📋 Planned | 2x10 open |
| [#401](https://github.com/Rikarin/cathode/issues/401) | Fender Bassman 6G6-B (brownface/blonde piggy-back) | Fender | New amp | medium | available | 📋 Planned | 2x12 closed |
| [#402](https://github.com/Rikarin/cathode/issues/402) | Fender Showman 6G14-A / Twin 6G8-A (brownface/blonde, harmonic vibrato) | Fender | Variant | medium | available | 📋 Planned | 1x15 closed JBL / 2x12 open |
| [#403](https://github.com/Rikarin/cathode/issues/403) | Fender Twin Reverb, silverface (1971 AA270 / master-volume 100 W / 135 W ultralinear) | Fender | Variant | medium | available | ↪ See its row above | 2x12 open, JBL D120F |
| [#404](https://github.com/Rikarin/cathode/issues/404) | Fender '59 Bassman LTD / '59 Bassman reissue | Fender | Variant | medium | available | ↪ See its row above | 4x10 open, Jensen P10R |
| [#405](https://github.com/Rikarin/cathode/issues/405) | Fender Bassman AB165 (blackface) | Fender | New amp | medium | partial | ↪ See its row above | 2x12 closed |
| [#406](https://github.com/Rikarin/cathode/issues/406) | Fender '68 Custom Deluxe Reverb (modified silverface reissue) | Fender | Variant | medium | not confirmed | ⏸ Waiting | 1x12 open, Celestion G12V-70 |
| [#407](https://github.com/Rikarin/cathode/issues/407) | Fender Prosonic | Fender | New amp | medium | available | 📋 Planned | 2x10 open |
| [#408](https://github.com/Rikarin/cathode/issues/408) | EVH 5150III 100S Stealth (and 50 W, LBX) | High gain | Variant | medium | partial | ↪ See its row above | 4x12 closed |
| [#409](https://github.com/Rikarin/cathode/issues/409) | Peavey JSX | High gain | New amp | medium | partial | ⏸ Waiting | 4x12 closed |
| [#410](https://github.com/Rikarin/cathode/issues/410) | Peavey Triple XXX / 3120 | High gain | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#411](https://github.com/Rikarin/cathode/issues/411) | Peavey Invective 120 | High gain | New amp | medium | partial | ⏸ Waiting | 4x12 closed |
| [#412](https://github.com/Rikarin/cathode/issues/412) | Soldano Hot Rod 50 / 100 (and Avenger) | High gain | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#413](https://github.com/Rikarin/cathode/issues/413) | Bogner Ecstasy 100B / 101B (and 20th Anniversary) | High gain | New amp | medium | partial | ↪ See its row above | 4x12 closed |
| [#414](https://github.com/Rikarin/cathode/issues/414) | Bogner Uberschall | High gain | New amp | medium | partial | ↪ See its row above | 4x12 closed |
| [#415](https://github.com/Rikarin/cathode/issues/415) | Diezel VH4 | High gain | New amp | medium | partial | ↪ See its row above | 4x12 closed, Vintage 30 |
| [#416](https://github.com/Rikarin/cathode/issues/416) | ENGL Ritchie Blackmore Signature E650 | High gain | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#417](https://github.com/Rikarin/cathode/issues/417) | Hughes & Kettner TriAmp MkI | High gain | New amp | medium | available | ↪ See its row above | 4x12 closed |
| [#418](https://github.com/Rikarin/cathode/issues/418) | Carvin X100B | High gain | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#419](https://github.com/Rikarin/cathode/issues/419) | Marshall JCM800 2203 / 2204: Low input is not modelled | Marshall | Missing channel or mode | medium | available | ↪ See its row above | 4x12 closed |
| [#420](https://github.com/Rikarin/cathode/issues/420) | Marshall Plexi year variants (1967, 1969, 1970, 1972) and 50 W with 6CA7 | Marshall | Variant | medium | available | ↪ See its row above | 4x12 closed, Greenback / G12H |
| [#421](https://github.com/Rikarin/cathode/issues/421) | Marshall JTM50 / 'black flag' (1966-67, GZ34 EL34 transitional) | Marshall | Variant | medium | partial | ⏸ Waiting | 4x12 closed, Greenback |
| [#422](https://github.com/Rikarin/cathode/issues/422) | Marshall 1986 Bass 50 W (and Park 75) | Marshall | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#423](https://github.com/Rikarin/cathode/issues/423) | Marshall 6100 30th Anniversary (and 6100LM) | Marshall | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#424](https://github.com/Rikarin/cathode/issues/424) | Marshall 2061X / 2061 Lead & Bass 20 | Marshall | New amp | medium | partial | ⏸ Waiting | 2x12 closed |
| [#425](https://github.com/Rikarin/cathode/issues/425) | Marshall JCM900 SL-X 2100 / 2500 | Marshall | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#426](https://github.com/Rikarin/cathode/issues/426) | Marshall 2203 with the Jose Arredondo mod (and 'SIR #36') | Marshall | Variant | medium | partial | ⏸ Waiting | 4x12 closed |
| [#427](https://github.com/Rikarin/cathode/issues/427) | Mesa/Boogie Mark I | Mesa | New amp | medium | available | 📋 Planned | 1x12 closed, Altec 417 |
| [#428](https://github.com/Rikarin/cathode/issues/428) | Mesa/Boogie Mark III | Mesa | New amp | medium | partial | ⏸ Waiting | 1x12 / 4x12 closed |
| [#429](https://github.com/Rikarin/cathode/issues/429) | Mesa/Boogie Trem-O-Verb | Mesa | New amp | medium | available | 📋 Planned | 2x12 / 4x12 closed |
| [#430](https://github.com/Rikarin/cathode/issues/430) | Mesa/Boogie Stiletto Deuce / Ace | Mesa | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#431](https://github.com/Rikarin/cathode/issues/431) | Mesa/Boogie Road King II / Roadster | Mesa | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#432](https://github.com/Rikarin/cathode/issues/432) | Mesa/Boogie Studio Preamp | Preamp | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#433](https://github.com/Rikarin/cathode/issues/433) | Mesa/Boogie Quad Preamp | Preamp | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#434](https://github.com/Rikarin/cathode/issues/434) | ENGL E530 preamp | Preamp | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#435](https://github.com/Rikarin/cathode/issues/435) | Alembic F-2B preamp | Preamp | New amp | medium | partial | ⏸ Waiting | any |
| [#436](https://github.com/Rikarin/cathode/issues/436) | Marshall Lead 12 (3005 / 5005) | Solid state | New amp | medium | available | 📋 Planned | 1x10 / mini stack |
| [#437](https://github.com/Rikarin/cathode/issues/437) | Sunn Beta Lead / Beta Bass | Solid state | New amp | medium | partial | ⏸ Waiting | 4x12 closed |
| [#438](https://github.com/Rikarin/cathode/issues/438) | Peavey Bandit 112 (TransTube) | Solid state | New amp | medium | partial | ⏸ Waiting | 1x12 Sheffield |
| [#439](https://github.com/Rikarin/cathode/issues/439) | Ampeg VH-140C / SS-140C | Solid state | New amp | medium | available | 📋 Planned | 4x12 closed |
| [#440](https://github.com/Rikarin/cathode/issues/440) | Polytone Mini-Brute | Solid state | New amp | medium | available | 📋 Planned | 1x12 / 1x15 closed |
| [#441](https://github.com/Rikarin/cathode/issues/441) | Lab Series L5 | Solid state | New amp | medium | available | 📋 Planned | 2x12 open |
| [#442](https://github.com/Rikarin/cathode/issues/442) | Supro S6616 (Trojan Tremolo) | US vintage | New amp | medium | available | 📋 Planned | 1x10 / 6x9 open |
| [#443](https://github.com/Rikarin/cathode/issues/443) | Supro Model 24 S6424 | US vintage | New amp | medium | available | 📋 Planned | 1x12 open |
| [#444](https://github.com/Rikarin/cathode/issues/444) | Gibson GA-40 Les Paul (5879) | US vintage | New amp | medium | available | ↪ See its row above | 1x12 open, Jensen P12Q |
| [#445](https://github.com/Rikarin/cathode/issues/445) | Gibson GA-5 Les Paul Junior / Skylark | US vintage | New amp | medium | available | 📋 Planned | 1x8 open |
| [#446](https://github.com/Rikarin/cathode/issues/446) | Gibson GA-20 | US vintage | New amp | medium | available | 📋 Planned | 1x12 open |
| [#447](https://github.com/Rikarin/cathode/issues/447) | Magnatone Custom 280 (and Custom 15 / M15) | US vintage | New amp | medium | available | ↪ See its row above | 2x12 open |
| [#448](https://github.com/Rikarin/cathode/issues/448) | Music Man HD-130 / RD-50 | US vintage | New amp | medium | available | 📋 Planned | 2x12 / 4x10 |
| [#449](https://github.com/Rikarin/cathode/issues/449) | Ampeg V-4 / VT-22 (guitar) | US vintage | New amp | medium | available | 📋 Planned | 4x12 / 2x12 |
| [#450](https://github.com/Rikarin/cathode/issues/450) | Ampeg Reverberocket R-12R | US vintage | New amp | medium | available | 📋 Planned | 1x12 open |
| [#451](https://github.com/Rikarin/cathode/issues/451) | Vox AC30CC / AC30C2 (Custom Classic / Custom) | Vox/class A | Variant | medium | available | 📋 Planned | 2x12 open, Greenback / Alnico Blue |
| [#452](https://github.com/Rikarin/cathode/issues/452) | Vox AC15 (1960): the vibrato/tremolo channel is not modelled | Vox/class A | Missing channel or mode | medium | available (read 2026-11-10, see the AC15 row) | ↪ See its row above | 1x12 open, Alnico Blue |
| [#453](https://github.com/Rikarin/cathode/issues/453) | Vox AC30/4 and AC30/6 Normal (1960, fawn, non-top-boost) | Vox/class A | Variant | medium | available | 📋 Planned | 2x12 open, Alnico Blue |
| [#454](https://github.com/Rikarin/cathode/issues/454) | Vox AC10 (1960s) | Vox/class A | New amp | medium | available | 📋 Planned | 1x10 / 2x10 open |
| [#455](https://github.com/Rikarin/cathode/issues/455) | Vox AC4 | Vox/class A | New amp | medium | partial | ⏸ Waiting | 1x8 open |
| [#456](https://github.com/Rikarin/cathode/issues/456) | Vox AC50 | Vox/class A | New amp | medium | available | 📋 Planned | 2x12 closed, Alnico Blue + horn |
| [#457](https://github.com/Rikarin/cathode/issues/457) | Vox AC100 | Vox/class A | New amp | medium | available | 📋 Planned | 4x12 closed + horns |
| [#458](https://github.com/Rikarin/cathode/issues/458) | Matchless Lightning 15 | Vox/class A | New amp | medium | available | 📋 Planned | 1x12 / 2x12 open |
| [#459](https://github.com/Rikarin/cathode/issues/459) | Orange AD200B MkIII (bass) | Bass | New amp | low | none found | ↪ See its row above | 8x10 / 4x10 closed |
| [#460](https://github.com/Rikarin/cathode/issues/460) | Two-Rock (Jet 35, Classic Reverb, Traditional Clean; PAB variants) | Boutique | New amp | low | partial | ↪ See its row above | 1x12 / 2x12 open |
| [#461](https://github.com/Rikarin/cathode/issues/461) | Dr Z Maz 18 Jr (and Maz 8 / Maz 38) | Boutique | New amp | low | none found | ↪ See its row above | 1x12 / 2x12 open |
| [#462](https://github.com/Rikarin/cathode/issues/462) | Morgan AC20 Deluxe / SW22 / SW50 | Boutique | New amp | low | none found | ↪ See its row above | 1x12 open |
| [#463](https://github.com/Rikarin/cathode/issues/463) | Divided by 13 JRT 9/15, CJ 11, FTR 37 | Boutique | New amp | low | none found | ↪ See its row above | 1x12 / 2x12 |
| [#464](https://github.com/Rikarin/cathode/issues/464) | Budda Superdrive / Twinmaster | Boutique | New amp | low | none found | ↪ See its row above | 2x12 |
| [#465](https://github.com/Rikarin/cathode/issues/465) | Fuchs Overdrive Supreme | Boutique | New amp | low | none found | ↪ See its row above | 1x12 / 2x12 open |
| [#466](https://github.com/Rikarin/cathode/issues/466) | Bludotone Ojai | Boutique | New amp | low | none found | ↪ See its row above | 1x12 / 2x12 open |
| [#467](https://github.com/Rikarin/cathode/issues/467) | Carr Rambler (and Mercury) | Boutique | New amp | low | none found | ↪ See its row above | 1x12 / 2x12 open |
| [#468](https://github.com/Rikarin/cathode/issues/468) | Trainwreck Rocket | Boutique | New amp | low | partial | ↪ See its row above | 1x12 / 2x12 |
| [#469](https://github.com/Rikarin/cathode/issues/469) | Komet 60 / Concorde | Boutique | New amp | low | partial | ↪ See its row above | 2x12 / 4x12 |
| [#470](https://github.com/Rikarin/cathode/issues/470) | Swart Atomic Space Tone | Boutique | New amp | low | none found | ↪ See its row above | 1x12 open |
| [#471](https://github.com/Rikarin/cathode/issues/471) | Paul Ruby Rocket | Boutique | New amp | low | not confirmed | ↪ See its row above | 1x12 open |
| [#472](https://github.com/Rikarin/cathode/issues/472) | Carol-Ann OD-2 / Triptik / Tucana 3 | Boutique | New amp | low | not confirmed | ↪ See its row above | 2x12 / 4x12 |
| [#473](https://github.com/Rikarin/cathode/issues/473) | Dumble ODS with the HRM stack | Boutique | Variant | low | partial | ↪ See its row above | 1x12 / 2x12 open |
| [#474](https://github.com/Rikarin/cathode/issues/474) | Orange OR50 reissue / OR15 | British | New amp | low | partial | ↪ See its row above | 2x12 / 4x12 closed |
| [#475](https://github.com/Rikarin/cathode/issues/475) | Suhr Badger 18 / 30 and SL68 | British | New amp | low | none found | ↪ See its row above | 2x12 / 4x12 |
| [#476](https://github.com/Rikarin/cathode/issues/476) | Friedman Dirty Shirley | British | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#477](https://github.com/Rikarin/cathode/issues/477) | Friedman Small Box | British | New amp | low | none found | ↪ See its row above | 2x12 / 4x12 closed |
| [#478](https://github.com/Rikarin/cathode/issues/478) | Blankenship Leeds 21 | British | New amp | low | not confirmed | ↪ See its row above | 1x12 / 2x12 |
| [#479](https://github.com/Rikarin/cathode/issues/479) | Fender Vibroverb 6G16 (brown, 1963) and '63 Vibroverb reissue | Fender | Variant | low | available | ↪ See its row above | 2x10 open |
| [#480](https://github.com/Rikarin/cathode/issues/480) | Fender Bassman 100 (pentode-screen version of the Bassman 135) | Fender | Variant | low | available | ↪ See its row above | 4x12 closed |
| [#481](https://github.com/Rikarin/cathode/issues/481) | Fender Eric Clapton EC Vibro-Champ | Fender | Variant | low | none found | ↪ See its row above | 1x8 open |
| [#482](https://github.com/Rikarin/cathode/issues/482) | Cameron CCV | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#483](https://github.com/Rikarin/cathode/issues/483) | Cameron Atomica | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#484](https://github.com/Rikarin/cathode/issues/484) | Splawn Nitro | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#485](https://github.com/Rikarin/cathode/issues/485) | Hook Captain 34 | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#486](https://github.com/Rikarin/cathode/issues/486) | Wizard Modern Classic | High gain | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#487](https://github.com/Rikarin/cathode/issues/487) | Victory Kraken / Sheriff (and V30 Countess, Super Kraken) | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#488](https://github.com/Rikarin/cathode/issues/488) | Diamond Del Fuego | High gain | New amp | low | not confirmed | ↪ See its row above | 4x12 closed |
| [#489](https://github.com/Rikarin/cathode/issues/489) | Bogner Shiva / XTC Classic | High gain | New amp | low | none found | ↪ See its row above | 4x12 / 2x12 closed |
| [#490](https://github.com/Rikarin/cathode/issues/490) | Diezel Herbert (and MkIII) | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#491](https://github.com/Rikarin/cathode/issues/491) | Revv Generator 120 (Green / Purple / Red) | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#492](https://github.com/Rikarin/cathode/issues/492) | Fryette (VHT) Deliverance / D60 / Sig:X / Pittbull | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#493](https://github.com/Rikarin/cathode/issues/493) | PRS Archon (Clean / Lead / Bright) and MT15 | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#494](https://github.com/Rikarin/cathode/issues/494) | Krank Revolution / Krankenstein | High gain | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#495](https://github.com/Rikarin/cathode/issues/495) | Randall Satan / Thrasher / Diavlo / Warhead | High gain | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#496](https://github.com/Rikarin/cathode/issues/496) | Carvin Legacy 3 VL300 | High gain | Variant | low | partial | ↪ See its row above | 4x12 closed |
| [#497](https://github.com/Rikarin/cathode/issues/497) | Marshall SV20H Studio Vintage | Marshall | Variant | low | partial | ↪ See its row above | 2x12 / 4x12 closed |
| [#498](https://github.com/Rikarin/cathode/issues/498) | Marshall SC20H Studio Classic | Marshall | Variant | low | partial | ↪ See its row above | 2x12 / 4x12 closed |
| [#499](https://github.com/Rikarin/cathode/issues/499) | Marshall JVM410HJS (Satriani) | Marshall | New amp | low | partial | ↪ See its row above | 4x12 closed, Vintage 30 |
| [#500](https://github.com/Rikarin/cathode/issues/500) | Mesa/Boogie JP-2C | Mesa | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#501](https://github.com/Rikarin/cathode/issues/501) | Mesa/Boogie Triple Crown TC-100 / TC-50 | Mesa | New amp | low | none found | ↪ See its row above | 4x12 closed |
| [#502](https://github.com/Rikarin/cathode/issues/502) | Soldano X88R preamp | Preamp | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#503](https://github.com/Rikarin/cathode/issues/503) | Soldano / Caswell X99 preamp | Preamp | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#504](https://github.com/Rikarin/cathode/issues/504) | Bogner Fish preamp (and Bogner/Hafler Triple Giant) | Preamp | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#505](https://github.com/Rikarin/cathode/issues/505) | CAE 3+ SE preamp | Preamp | New amp | low | partial | ↪ See its row above | 4x12 closed |
| [#506](https://github.com/Rikarin/cathode/issues/506) | Supro 1964T / 1695T Black Magick (2014+ reissues) | US vintage | New amp | low | none found | ↪ See its row above | 1x12 open |
| [#507](https://github.com/Rikarin/cathode/issues/507) | Vox AC30HW / AC30HW2 (hand-wired) | Vox/class A | Variant | low | none found | ↪ See its row above | 2x12 open, Alnico Blue |
| [#508](https://github.com/Rikarin/cathode/issues/508) | Vox AC30: the Vib-Trem channel is not modelled | Vox/class A | Missing channel or mode | low | available | ↪ See its row above | 2x12 open, Alnico Blue |
| [#509](https://github.com/Rikarin/cathode/issues/509) | Bad Cat Black Cat 30 | Vox/class A | New amp | low | none found | ↪ See its row above | 2x12 open |
