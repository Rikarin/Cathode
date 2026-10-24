# Amp catalogue

Every real amp we model, plan to model, or might model, with where it stands in the Circuit Amp block. Use it to find
**reference captures** (to match a model against) and **cab IRs** (to pair with it).

- **Amp captures** (NAM or GuitarML, *direct out / amp only*, no cab or mic) are what the reference harness matches a
  circuit model against. Note the knob settings and the input calibration (dBu at 0 dBFS) with every capture you find.
- **Cab IRs** go in the Cab block after the amp. The "Typical cab" column says what the amp is usually heard through.
- Add a capture you found to `engine/tests/assets/references/manifest.json` (see `docs/06-amp-modelling-spec.md` §4).
  Tone3000 ("T3K" licence) and CC BY-NC files are local-only: never commit them.

Last updated: 2026-10-24.

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
| Deluxe Reverb AB763 | 🔨 In progress | — | T3K: '68 Custom Deluxe (modified circuit) | Stock AB763 Deluxe, direct out, volume sweep | 1x12 open, Jensen C12K/C12N |
| Twin Reverb AB763 | 🔨 In progress | — | GPL: Twin Normal Bright, Twin Vibrato Bright | Settings stated; Normal without Bright | 2x12 open, JBL D120 / Jensen C12N |
| Super Reverb AB763 | 🔨 In progress | — | T3K: '64 Super Reverb, 5 settings | More volume steps | 4x10 open, Jensen P10R/P10Q |
| Princeton Reverb AA1164 | 🔨 In progress | — | GPL: Princeton clean (year unknown); T3K: '68 Custom Princeton | Stock AA1164, volume sweep | 1x10 open, Jensen P10R |
| Bassman 5F6-A | 🔨 In progress | — | — | Any 5F6-A / reissue, jumpered and single channel | 4x10 open, Jensen P10Q |
| Tweed Deluxe 5E3 | 🔨 In progress | — | T3K: 5E3 kit clone | Volume sweep, both inputs | 1x12 open, Jensen P12R/P12Q |
| Hot Rod Deluxe | ⏸ Waiting | — | — | Readable schematic (tone stack unknown); captures | 1x12 open, Celestion G12P-80 |
| Champ 5F1 | 💤 Not planned | — | — | Schematic, captures | 1x8 open |
| Vibrolux / Vibroverb AB763 | 💤 Not planned | — | — | | 2x10 / 1x15 open |
| Bandmaster AB763 | 💤 Not planned | — | — | | 2x12 closed |
| Tweed Twin 5F8-A / High Power Tweed Twin | 💤 Not planned | — | — | | 2x12 open |

## Marshall and British

| Amp | Status | Cathode type | References we have | Look for | Typical cab |
|---|---|---|---|---|---|
| Plexi 1959 Super Lead (50 W, generic) | ✅ Done | Plexi (1) | T3K: 1959 Plexi | Jumpered and single-input captures, volume sweep | 4x12 closed, Greenback G12M |
| Plexi 100 W / jumpered | 🔨 In progress | — | T3K: 1959 Plexi | | 4x12 closed, Greenback |
| JTM45 | 🔨 In progress | — | — | Any JTM45 / reissue | 4x12 or 2x12 (Bluesbreaker), Greenback / Alnico |
| JCM800 2203 | 🔨 In progress | — | T3K: 2203 (modified) | Stock 2203, gain sweep | 4x12 closed, G12T-75 / Vintage 30 |
| JCM800 2204 | 🔨 In progress | — | — | Stock 2204 | 4x12 closed, G12T-75 |
| Silver Jubilee 2555 | 🔨 In progress | — | — | Rhythm clip and lead modes | 4x12 closed, G12T-75 / Vintage 30 |
| Friedman BE-100 | 🔨 In progress | — | — | BE and HBE channels, Fat/Tight settings | 4x12 closed, Vintage 30 / Greenback |
| JCM900 4100 / 4502 | 🔨 In progress | — | GPL: JCM900 4502 channel A gain 4–10, channel B gain 8–20 | | 4x12 closed, G12T-75 / Vintage 30 |
| Laney GH100L | 🔨 In progress | — | GPL: Laney GH100S ×3 | | 4x12 closed, G12H-100 |
| Hiwatt DR103 | 🔨 In progress | — | — | Normal/Brilliant, jumpered | 4x12 closed, Fane |
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
| Soldano SLO-100 | 🔨 In progress | — | GPL: Soldano crunch and high (model not stated); T3K: SLO-100 pack | Overdrive channel, gain sweep | 4x12 closed, Vintage 30 / G12T-75 |
| Mesa/Boogie Mark IIC+ | 🔨 In progress | — | GPL: IIC+ Drive-knob model, IIC+ Drive 8; T3K: IIC+ pack | EQ in and out, Lead Drive sweep | 1x12 / 4x12 closed, EVM12L / Vintage 30 |
| Peavey 5150 / 6505 | 🔨 In progress | — | GPL: 5150 block letter, 6505+ red (two sources) | Rhythm channel, crunch | 4x12 closed, Sheffield / Vintage 30 |
| EVH 5150III | 🔨 In progress | — | T3K: 5150III OB1 v2 | Blue and red channels | 4x12 closed, G12EVH / Vintage 30 |
| Mesa/Boogie Dual Rectifier | 🔨 In progress | — | GPL: Mini Rectifier (not the same amp) | Dual/Triple Rec, modern and vintage modes, tube vs SS rectifier | 4x12 closed, Vintage 30 |
| Bogner Ecstasy | 🔨 In progress (assumptions marked) | — | — | Blue/red channels; a better schematic | 4x12 closed, Vintage 30 / Greenback |
| Diezel VH4 (ch 3) | 🔨 In progress (assumptions marked) | — | — | Channel 3 and 4 | 4x12 closed, Vintage 30 |
| Mesa/Boogie Mark IV (lead) | 🔨 In progress | — | GPL: Mark IV | Settings stated | 1x12 / 4x12 closed |
| Carvin Legacy | 🔨 In progress | — | — | | 4x12 closed, Vintage 30 |
| Mesa/Boogie Lone Star | 📋 Planned | — | — | Clean/edge channels | 1x12 / 2x12 open |
| Bogner Uberschall | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Diezel Herbert | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Engl Powerball | ⏸ Waiting | — | — | Schematic | 4x12 closed, Vintage 30 |
| Engl Savage 120 | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Revv Generator 120 | ⏸ Waiting | — | — | Schematic | 4x12 closed |
| Mesa/Boogie Mark V / Triple Rectifier / Rect-o-Verb | 💤 Not planned | — | — | | 4x12 / 2x12 closed |
| Fryette (VHT) Deliverance / Sig:X | 💤 Not planned | — | — | | 4x12 closed |
| Friedman Dirty Shirley | 💤 Not planned | — | — | | 4x12 closed |
| Cameron CCV | 💤 Not planned | — | — | | 4x12 closed |
| Bogner Shiva / XTC | 💤 Not planned | — | — | | 4x12 closed |
| PRS Archon | 💤 Not planned | — | — | | 4x12 closed |
| Krank Revolution | 💤 Not planned | — | — | | 4x12 closed |
| Victory Kraken / Sheriff | 💤 Not planned | — | — | | 4x12 closed |
| Randall Satan / RG100 | 💤 Not planned | — | — | | 4x12 closed |
| Hughes & Kettner TriAmp | 💤 Not planned | — | — | | 4x12 closed |
| Wizard Modern Classic | 💤 Not planned | — | — | | 4x12 closed |

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
| ✅ Done | 6 (Blackface, Plexi, AC30 with Normal channel, AC15, DC-30) |
| 🔨 In progress | 24 |
| 📋 Planned | 3 |
| ⏸ Waiting on a schematic | 15 |
| 💤 Not planned yet | 31 |

The 🔨 rows become ✅ as the family streams merge. Some of them may end as ⏸ if their streams find the data too weak.
This file will then be updated with each model's Cathode type index.
