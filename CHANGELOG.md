# Changelog

Cathode follows [semantic versioning](https://semver.org). From 1.0 on, a preset saved by any release keeps loading
in every later one: the preset format carries a schema version (`schemaVersion`, now 2), and a release that changes
the format upgrades older presets as it reads them.

## 1.0.0-rc.7 — unreleased

### Fixed

- Amp: the Savage 120's lead and Crunch 2 get the four small caps between their stages that Engl's drawing leaves out,
  as the drawings of the Screamer 50, the Blackmore, the Powerball and the Fireball 60 do, so they no longer send full
  bass into every clipping stage and clean up as the playing softens (a 0.1 V note plays 36 dB above a 0.2 mV one, was
  21). The values are guessed from those amps. The lead has 29 dB of gain at 220 Hz with stock knobs (was 46) and 39 dB
  with Lead at 7.5, a high-gain amp's (#526).
- Amp: the Savage 120 and Savage 120 Clean run on the supply voltages Engl's hand-drawn sheet prints (545 V plates,
  355 V screens, 340 V preamp), from the two stacked rectifiers both drawings show, with the screens and the preamp on
  the lower one. The screens and the preamp had sat near a 560 V B+, which gave the power amp about twice its rated
  120 W before it clipped (#526).
- MIDI: a footswitch that listens to a program change and has a Hold action taps on every press. A program change
  carries no switch position, so such a switch never releases: the first press ran the hold action instead of the tap,
  and every press after it did nothing at all until the layout or the preset changed (#527).
- Metronome: a tempo change moves only the beats after it. The grid was counted from sample 0 with the current beat
  length, so a change after a minute of playing fired a click 794 samples after the one before it and moved the accent
  to another beat of the bar (#528).
- Server: a preset load the engine rejects puts the Scene Ignore held states back, not just the preset and the scene.
  A block held out of the scenes silently fell back to the active scene's bypass and channel (#529).
- Host: the allocation-free setParams path checks the values as well as the ids before it applies any of them. A frame
  carrying a value that is not a number had the entries before it applied, then went to the slow path to be applied
  again, with the block refresh skipped (#530).
- Audio I/O: the JACK backend reports the frame count its callbacks really deliver, clamped to 4096, rather than the
  server's period: a server at 8192 frames had the engine built for twice the callback size (#531).
- Audio I/O: the JACK error for a target with no physical playback ports formats its message before the stream is
  freed, so it names the target instead of reading freed memory (#532).
- Engine: an engage switch in Position mode with an off delay no longer engages for a value inside the hysteresis band
  that never reached its threshold (#533).
- Server: a store id ending in a newline is refused. In .NET `$` also matches just before a trailing newline, so
  `abc\n` passed as an id and wrote `abc\n.json`; a preset package's entry names were anchored the same way (#534).
- Server: an undo or redo step stays on its stack when taking the snapshot of the current state throws, instead of
  being lost from both stacks (#535).
- Server: swapping a channel of a block with itself changes nothing, instead of listing its Scene Ignore entry twice
  and leaving the channel ignoring scenes after Scene Ignore had been turned off again (#536).
- Engine: a fully bypassed Thru block's peak meter reads 0, as the telemetry documents, rather than its input level
  (#537).
- Engine: `CathodeEngine.Dispose` frees the graphs the engine still holds, the current and pending ones, and not only
  the retired and spilled ones (#538).

## 1.0.0-rc.6 — 2026-11-11

The fixes below came out of a second full audit of the code (issues #163 to #356), and from the first seventeen amps of its missing-amp list (#357 to #373).

### Added

- Amp: five amps from the missing-amp inventory, eight Circuit Amp types, each read from its factory drawing: **Blues
  Deluxe** and **Blues DeVille** (Fender's 047019 rev D and 047003 rev E; both channels, the Volume on the Normal
  channel and the Drive and Master on the Drive one, as the relays wire them; #394), **Mark I** (Mesa's hand-drawn
  sheet: the four-stage preamp with the Gain Boost footswitch; #427), **JCM900 SL-X 2100** and **2500** (Marshall
  SLXa-61-02 iss 5 and 2100-60-34 iss 10: five ECC83 stages and both master volumes, the second on Switch 1; #425),
  **Hot Rod 100** and **Hot Rod 50** (Soldano's three 1996 sheets: the single-channel SLO with its Normal channel on
  Switch 1; #412) and **VH100R** (Laney 2385 iss 6 and 2386 iss 4: the Overdrive and Clean channels, each with its
  own stack; #392). What each model infers or guesses is in its own remarks, in `docs/06-amp-modelling-spec.md` and
  in the amp catalogue.
- Editor: the amp's Type list is in alphabetical order and has a filter field: type part of a name to find a model
  among the hundred and more.
- Editor: the access token no longer stays in the browser's address bar once it is remembered; Settings ▸ Audio setup
  ▸ Home Screen shortcut puts it there on request for a shortcut or bookmark that opens signed in (#189).
- Docs: the ODS 70s model's notes and the amp catalogue state that the source drawings show no Master volume, so the
  model has none (#195).
- Amp: the Mark IIC+ has its rhythm channel as a new type, Mark IIC+ Rhythm, read from the factory scan (#202).
- Amp: the Mark IV has its Rhythm 1 and Rhythm 2 channels as new types, and Presence Shift on the Mark IV is the amp's
  own pull shift (#203).
- Amp: on Dual Rectifier 3-Ch and Triple Rectifier the Presence pot is in the feedback in Raw and Vintage and a preamp
  control in Modern, as the relays wire it; new Dual Rectifier 3-Ch Orange type (channel 2) (#205)
- Security: the packaged desktop app sets Electron's fuses (no ELECTRON_RUN_AS_NODE, NODE_OPTIONS or --inspect; code
  only from the integrity-checked app.asar) and ignores the development overrides CATHODE_EDITOR_URL,
  CATHODE_EDITOR_DIR and CATHODE_ENGINE_DIR (#209).
- Desktop: on macOS Cathode asks for microphone access before the engine opens an interface, and Audio setup says when
  it is refused, with a button to the Microphone privacy pane (#210)
- Modifiers: a second source (add or multiply) and a response with Start, Mid, End, Slope, Scale and Offset, in the
  modifier dialog and the API (#219)
- Amp NAM: Size runs a slimmable (A2) capture at Full or Lite, about a seventh of the CPU (#220)
- Server: preset next/previous, the preset list and asset usage come from an in-memory index that re-reads only
  changed files, instead of parsing every preset and bank file each time (#248).
- Editor: nine unused @xui packages (data-table, divider, label, numeric-input, omnibar, scroll-area, slider,
  statistic, upload) are no longer dependencies (#270).
- Server: the editor it serves carries a Content-Security-Policy (own scripts only, no framing), nosniff and
  no-referrer (#271).
- Preset menu: Export saves the working copy, unsaved changes included, as a .cthpreset file (#273).
- Amp: solver faults (unconverged samples, non-finite samples, failed recomputes) are reported as the Amp block's
  meters in telemetry, for bug reports (#278).
- Bassman 5F6-A and Jumped: the inverter takes the tone stack through its 0.02 µF input cap and draws grid current
  under drive, as on the Tweed Twin; PI Bias Excursion now applies to them (#283).
- Amp: the Princeton Reverb, Princeton AA964, Scout GA-17RVT and Subway Blues use a 10-inch speaker's impedance curve
  as their Stock speaker load (was the 1x12 curve) (#285).
- Docs: the Bluesbreaker 1962 and 1959T entries say why their own tremolo is not built and what it still needs (#291).
- Amp: where an amp's three-position switch is shown as two switches (Mark V modes, Raw / Modern on the three-channel
  Rectifiers, Ultra Lo / Bass Cut on the SVT, Bite on the Prometheus), turning both on no longer gives a voicing the
  amp does not have; the second of the pair wins (#301).
- Amp: the SLO-100, Mark IIC+/IV/V, 5150, 5150III, the Rectifiers, TriAxis and Prometheus share the "4x12 Closed"
  speaker load as their Stock load (resonance 105 Hz instead of 95 Hz), like the other closed 4x12 models (#302).
- Amp: the Rect-o-Verb is built from the Dual Rectifier's shared stage netlists; no change in sound (its middle
  rheostat was checked against the Solo 50 / Rectoverb drawing and is right) (#303).
- Amp: Rect-o-Verb gains its rhythm channel (Switch 1 Rhythm, Switch 2 Pushed), with that channel's presence pot in
  the feedback (#305)
- Amp Powerball: Switch 3 plays channel 1 (Clean, or Crunch with Gain Hi) with its Bright switch; Switch 2 is now
  "Gain Hi" and Control 2 "Channel Volume" (#307)
- Amp Savage 120: Switch 3 is Smooth, the lead's second voice (V3A after the tone stack); the mode it always had is
  correctly named Rough (#308)
- Docs: the SVT model's notes say that channel two and the feedback's 120 pF are not modelled, and record channel
  two's circuit from the drawing (#309)
- Docs: the Portaflex B-15N's two channels are the same circuit, so the model is either channel (#310)
- Amp JC-120: the distortion switch is named Distortion On, and the Distortion knob is shown only while it is on
  (#311)
- Desktop: the app and both engine processes now write log files (main.log, host.log, server.log), and Help ▸ Show
  Logs opens their folder for bug reports (#316).
- Desktop: .cthpreset files are associated with Cathode; opening one (double-click, the Dock icon, the command line)
  imports it into the library (#317)
- Desktop: the app checks GitHub for a newer release at start and once a day and offers its page in the menu; Check
  for Updates Automatically turns it off (#318)
- Amp: the DSL100's Deep is a switch with the amp's own gyrator network across the feedback (53 Hz) instead of the
  generic Depth knob (#331).
- Amp: Master Location gains Pre-Triode (the master ahead of the last preamp gain stage); Tonestack Location End is
  documented as left out on purpose (#332).
- Audio I/O: Inputs 2 to 4 have their own gain trim and calibration, in Global and Audio setup (#333)
- *Formant:** all ten Peterson-Barney vowels; IH, AE, OO, UH and ER join A, E, I, O, U (#334)
- Controllers: the sequencer has up to 32 stages, and there are Manual A-E and External 1-16 (#335)
- Amp NAM: EQ Location puts the four tone controls before the capture (Pre) or after it (Post) (#336)
- *Amp:** a Preamp switch; off, the block is the model's power amp alone, so an Amp NAM capture of a preamp ahead of
  it gets a working Master, Presence, Depth and Sag (#337)
- *Filter:** Ladder LP 12 and 24 types with Drive, and an Envelope page that moves the frequency with the block's own
  input level (#338)
- Reamp: a DI recording renders offline through the current preset, faster than real time and without the interface
  (`POST /live/render`, Preset ▸ Reamp a recording…) (#339)
- MIDI: Cathode can send MIDI clock at its tempo (Global ▸ MIDI ▸ Send MIDI clock) (#340)
- Library: the whole library (settings, presets, banks, setlists, block presets, templates, snapshots, IRs and
  captures) backs up to one `.cthbackup` file and restores from it (`GET /library/backup`, `POST /library/restore`,
  Library ▸ Backup) (#341)
- Telemetry: each block reports its measured CPU share, shown on the block's page (#342)
- Editor: the Amp NAM, IR Player and Cab file pickers explain that Cathode ships no captures or IRs while the library
  has none, and what to do (#355)
- Amp NAM: Size shows only for a capture that has more than one size (NAM A2); the Server reads it from the file and
  sends it on the asset (#220).
- Amp NAM: Stack Type puts a Fender, Marshall or Vox passive tone stack on Bass, Middle and Treble, and Bright adds a
  treble shelf; both sit before or after the capture with EQ Location (#336).
- Modifiers: each source has a Scale, two sources can subtract, Update Rate spaces the parameter writes, auto-engage can
  be off above its threshold, and PC Reset holds the stored value after a preset load until the external controller
  moves (#219).
- Drive: Heavy Metal, a Boss HM-2 with its two-knob gyrator EQ (Color Mix L on the Bass Pot, Color Mix H on Tone), is a
  new type after the existing ones (#343).
- 18 W 1974X: Depth is now the amp's Intensity rheostat in the shared cathode, with the oscillator at full swing while
  the tremolo runs; 0 is still the footswitch (#330).
- Bluesbreaker 1962: the amp's own tremolo on Speed and Intensity, a J174 chopping Channel II at 2.0 to 4.7 Hz; the
  1959T's tremolo is still not built, and its notes say what the scan does not give (#291).
- Amp: the 5150 has its rhythm channel on Switch 1 ("Rhythm Ch"), with Crunch on Switch 2 and the Bright switch, read
  from the Peavey factory scan (#201).
- Amp: new type 5150III Clean/Crunch, the EVH 5150III's green channel with the blue channel on Switch 1, read from
  Fender's service diagram of the standard 100 W head (#306).
- Amp: the Powerball has its lead channel's noise gate (Gate Threshold, on the Cut knob; off at 0) and its Bottom switch
  (#307).
- Amp: the Savage 120 has Crunch 2 (Bright switch) and a Hi Balance knob for Smooth, and its clean and Crunch 1 channel
  is the new Savage 120 Clean type (#308).
- Amp: new type SVT Channel 2, the Ampeg SVT's second channel, with its own Volume, Bass, Treble, Ultra Hi and Ultra Lo
  (#309).
- Amp: new type Portaflex B-15N Jumped, both channels of the B-15N played at once, each with its own volume, tone
  controls and Ultra switches (#310).
- Chorus: new Type choice; JC-120 is the Roland Jazz Chorus's own chorus (one bucket-brigade line, dry on the left,
  chorus on the right) (#311).
- Amp: new types Dual Rectifier Orange (two-channel orange channel: Normal / Clean / Modern), Dual Rectifier 3-Ch Clean
  (channel 1, Clean / Pushed), Triple Rectifier Orange and Triple Rectifier Clean, all from the factory drawings (#205).
- Amp: new types Mark V Clean (channel 1: Clean / Fat / Tweed, Bold) and Mark V Crunch (channel 2: Edge / Crunch / Mark
  I); all Mark V types gain the loop's recovery stage V6B, the drawn inverter tail and the series presence network with
  one rheostat per channel, so Mark V drives its power amp about 15 dB harder and its Presence behaves as on the Mark IV
  (#204).
- Circuit Amp: new type Pro Junior, the Fender Pro Junior (two EL84, Volume and Tone) modelled from the factory drawing
  047420 rev C (#360).
- Server: setlists and banks carry a revision, and a save made on a stale one is refused (412) instead of silently
  replacing another editor's change; the editor re-reads and, for bank slots, merges (#178).
- Packaging: the desktop app is also built for Intel Macs, Windows on ARM and ARM64 Linux as extra targets, and as a
  `.deb` on Linux once a maintainer is named (#327).
- Factory content: eight impulse responses rendered from the cab model come with every library, read-only and marked
  Factory (#355).
- Editor: on a touch screen, double-tap a knob or its value to type a value (#186).
- Shortcuts: preset and song stepping (PgUp/PgDn, Shift+PgUp/PgDn), the scenes (1-8) and Preset Leveling (L) also work
  without Cmd/Ctrl, so they work in a browser that keeps those keys for its tabs; in a browser the tooltips name these
  keys (#260).
- Amp: new Circuit Amp type Champ AA764, the blackface and silverface Fender Champ (single-ended 6V6GT, Volume, Treble,
  Bass), modelled from Fender's AA764 drawing (#361).
- Amp: new Circuit Amp types OR120 and OR80, the early-1970s Orange Graphic Mk II ("Pics Only") from the 1972 factory
  drawing, with Bass, Treble, the six-position FAC (Control 1) and HF Drive (Presence) (#359).
- Amp: new Circuit Amp type Super Bass 100, the Marshall 1992 Super Bass (100 W) from Unicord drawing 70-13-11: the
  Plexi 100W's circuit without bright caps, darker and fuller, for guitar or bass (#366).
- Amp: new type GK 800RB, the Gallien-Krueger 800RB solid-state bass head from its 1982 service drawings (four-band EQ,
  Contour, Lo Cut, Hi Boost, -10 dB, FET Boost, 300 W power amp); network stages can now hold a JFET (#358).
- Circuit Amp: new type SVT-CL, the Ampeg SVT-CL from the factory drawings, with the 0 dB and -15 dB inputs, Ultra Hi,
  Ultra Lo, the five-position midrange Frequency switch, Gain and Master (#357).
- Amp: new Circuit Amp type Sunn Model T (first generation, 150 W, four ultralinear 6550), Brite and Normal channels
  with Both In, from Sunn drawing D-1029 rev A and its voltage chart (#365).
- Amp: new Circuit Amp type Classic 30 (Peavey Classic 30, from factory drawing 81501580): Lead channel with Pre and
  Post, Normal channel with Volume (Normal Ch switch), Boost switch, four EL84 (#363).
- Amp: new types 6505+ and 6505+ Rhythm, the Peavey 5150 II / 6505+ lead and rhythm channels (Crunch and Bright
  switches) from the factory drawing, with the amp's own presence and resonance networks (#362).
- Amp: new Circuit Amp type Thunderbolt S6420, the 1966 Supro Thunderbolt 1 x 15 bass combo (Volume and Tone, a fixed
  paraphase inverter, two cathode-biased 6L6GC, no feedback), read from four traces of original amps (#371).
- Amp: new Circuit Amp type Fireball 60 (Engl Fireball 60, E625), the Ultra channel and the clean channel (Switch 3
  "Clean"), from the factory drawing 625; the presence is the drawn rheostat in series with the feedback. One divider
  resistor and two supply filter nodes the drawing leaves out are assumed from the Powerball (#364).
- Amp: new Circuit Amp type TSL100, the three-channel Marshall JCM2000 (Clean, Crunch and Lead with Mid Boost, Tone
  Shift and Deep), from the factory drawings (#368).
- Amp: new Circuit Amp type RG100ES, the solid-state Randall RG100ES from its factory schematic: both channels (Switch 1
  Channel 1), the Sustain Boost (Switch 2), Presence, and JFET gain stages in the amp engine (#369).
- Amp: new type Twin Twelve 1484, the Silvertone 1484 from Sears' factory drawing (both channels, Volume, Bass, Treble;
  6CG7 driver and cathodyne on a new 6CG7 tube table; its tremolo and reverb are not built) (#372).
- Amp: new Circuit Amp type Supro 1624T (Dual-Tone): both channels with Standard and Treble inputs, a fixed-divider
  paraphase inverter, a 6973 output-tube table fitted to RCA's sheet, and the second channel's tremolo on Speed and
  Intensity (#370).
- Amp: new Circuit Amp type AC15C1, the current Vox AC15 from the factory service manual: Top Boost and Normal inputs
  (Switch 1), Tone Cut and a master volume, two EL84 (#373).
- Amp: two new Circuit Amp types, JCM800 2210 (100 W) and JCM800 2205 (50 W), Marshall's split-channel JCM800 from the
  1988 factory drawings: the diode-clipped Boost channel and the clean Normal channel (Switch 1), with Boost Volume on
  Control 1 (#367).

### Changed

- **Amp NAM uses the capture's input level** (#13): a NAM file that records the level it was made at
  (`input_level_dbu`) is driven by the difference to Input 1's calibration, as the NAM plugin's Calibrate Input does,
  so it reacts to your guitar as the real amp did. The new **Calibrated Input** switch is on by default. Presets with
  such a capture now drive it at the calibrated level: at the default 1 V calibration a file made at +12 dBu gets
  about 13 dB less drive than before. Turn the switch off, or raise **Input**, for the old drive.
- **The engine runs on .NET 11** (SDK 11.0.100, runtime and ASP.NET Core 11.0.0): the self-contained server and the
  NativeAOT host carry the .NET 11 runtime, and building from source needs the .NET 11 SDK.

### Internal

- **The factory presets are files** (`engine/src/Cathode.Server/Factory`): one `.cthpreset` each, as the editor
  exports it, with `bank.json` giving the order and each scene's loudness lift. They are embedded in the Server, so the
  app and the headless package carry them. A preset changes by replacing its file with an exported one; the leveling
  test rewrites the levels in the files. The C# recipes are gone.
- Tests: a conformance test now checks that every smoothed parameter changed while a block or the rig runs reaches its
  target (#171).
- Releases are built with the current .NET servicing release (SDK 10.0.403, runtime and ASP.NET Core 10.0.14), and a
  release whose SDK pin is behind is not published (#208).
- CI: the packaged desktop app is started and tested on macOS, Windows and Linux (it runs the engine it carries,
  serves its editor and stops cleanly) before its installers are uploaded or released (#211)
- Tests: every block's golden render now holds processing, each block type has a golden, and a golden equal to the dry
  input fails (#214)
- Tests: the MIDI tap tempo test taps a tempo other than the default, so it fails if taps or their sample times are
  ignored (#215).
- Tests: the legacy corpus now holds data folders written by 1.0.0-rc.4 and rc.5 (schema 2), and a release without a
  corpus folder fails the build (#216).
- Tests: the capture-match tests are named a drift guard, also guard harmonic and response distance, and the nightly
  summary counts the captures that reach the docs/06 ship target (#217)
- Tests: end-to-end specs drive Setlists, Perform, Scenes, Controllers, footswitch layouts, MIDI learn, templates,
  Revert, Clear, Batch Set, Leveling and the Amp page tools in the desktop app (#218)
- Docs: docs/03 marks the test tools and gates that are not built and describes the ones that are (#221)
- Docs: the parity tracker is current as of rc.5: no unverified rows, and the rows about removed Amp parameters, model
  counts, the Tiny Terror's Tone and the telemetry layout are corrected (#222)
- Engine: LFOs, envelopes, the sequencer and modifiers update every 64 frames at most, so modulation no longer gets
  coarser and later at large audio buffers (#232).
- Docs: docs/02 describes the MIDI path, the modifiers and the command ring as built (#233)
- Tests: the host's IPC error paths (malformed, oversize and cut-short frames, throwing handlers), looper export
  failures and the MIDI clock follower are now covered (#241).
- Server: an If-Match that is not one revision (a weak tag, a list, text) is refused with 400 instead of applying the
  edit unguarded; docs/02's API table and conventions match the Server (#249)
- Tests: the A/B compare store, the Block Library store, the Audio setup page, the Save to the Library dialog and the
  Settings dialog's tabs have unit tests (#272).
- Amp: the Major 1967's loop stage no longer rebuilds its network on every sub-block while an unrelated knob moves;
  solver leftovers and three stale comments cleaned up (#279).
- Docs: the Fender notes in docs/06 and docs/07 match the models (6G11 and Scout tremolos built, 5F6-A inverter caps,
  Super Reverb feedback 29 % below a 4 Ω tap, Blackface and ODS rows) (#288)
- Docs: docs/06 §3.2 gives the Marshall type indices of the Type list and marks the statements later streams overtook
  (#292)
- Amp: the 5150III's presence cap is the redraw's two 68 nF in parallel (136 nF, was 68 nF), so Presence reaches an
  octave lower; the docs and manual now say the type is drawn from the 100S (Stealth) (#297).
- Docs: docs/06 lists the Powerball, Savage 120 and Legacy as built from factory drawings and gives the Mark IIC+
  treble caps and the SLO cold clipper's grid network as the models have them (#298)
- Amp: the Mark IV's third switch is named Mid Gain, as on the amp (off is Harmonics); it was "Mid Boost" (#304).
- CI: Dependabot now opens weekly update pull requests and a nightly audit fails on a known vulnerability in a shipped
  package; Storybook, Nx, Vite, the Angular build tools, wrangler and the ASP.NET Core and xunit packages are updated,
  clearing the tool chain's critical and high advisories (#312).
- CI: a pull request that changes the engine runs the desktop end-to-end tests, and one that changes the desktop app's
  packaging or the native build builds the installers (#319)
- CI: every push and pull-request job has a timeout, and a new push to a pull request cancels the run it supersedes
  (#320)
- CI: release.yml builds nothing for a tag that is not on master, and only a release run (not a manual run from a
  branch) gets the signing secrets and attests its installers (#321)
- CI: the .NET install scripts are committed with the setup-dotnet action instead of downloaded and run in every job,
  and the SDK is cached (#322)
- Docs: the release document gives the preset schema version as 2, since 1.0.0-rc.4 (#325).
- CI: desktop builds from master carry a snapshot version (the package version plus the commit) instead of the
  release's name; the editor README names the release workflow as the one that publishes (#326).
- Tests: POST /live/tap, PUT /live/external/{index} and GET /catalog/rig are now covered (#328).
- Tests: the analysis toolkit's aliasing and octave-band measures are checked against signals with known answers
  (#329)
- Docs: the repository layout in docs/02 matches the repository (#344)
- Docs: docs/02 no longer names RTNeural or per-preset MIDI mappings; AIDA-X models run in managed code (#345)
- Docs: docs/04's checklist shows what is left before 1.0 (#346)
- Docs: docs/06 §1.3 names the Amp parameter for every Axe-Fx row that has one, and §1.4 no longer lists built
  parameters as gaps (#347)
- Docs: docs/08's summary counts are current (88 amp models, 11 reverb types, every guide block covered) and it no
  longer lists the removed Triode 1 Plate Freq, Master Cap or a B bypass key (#348)
- Docs: ADR-0003 and ADR-0004 are amended to say that AIDA-X models and shared-memory telemetry have shipped (#349).
- Docs: amp model comments no longer cite a missing library tremolo or the removed Master Cap and Triode 1 Plate Freq
  parameters (#350).
- Manual: the keyboard-shortcuts and grid chapters cover K and ⇧K (drawing and selecting cables from the keyboard),
  and the website check fails when an editor shortcut is missing from the chapter (#351)
- Docs: the README counts 45 block types, as the website does, and no longer lists the tuner as a block (#353).
- Manual: Live performance has an "Adjusting to the room" section (Effects Mix, the noise gate offset, the output EQ
  and Boost/Pad), linked from every block's Global Mix (#354)
- Docs: docs/07 has one row per amp, a Counts table that adds up, and blockers that match the stage library (paraphase
  inverter, op-amp stages) (#356)
- ODS 70s: stays without a Master, as its sheets and a 1977 example have it; the model notes and the amp catalogue now
  give the evidence (#195).
- Tests: two conformance sweeps cover every parameter of every block type (no dead knobs; decay with one parameter at an
  extreme), nightly on every amp and drive model. Knobs are hidden where they did nothing (Input gate on Off, Vol/Pan
  swell knobs on Volume, Graphic EQ Q controls on passive layouts, Compressor Auto Makeup on Sustainer, Pitch Mix/Global
  Mix/Kill Dry on Whammy and Virtual Capo, Pitch Tracking on Octave Divider and Crystals, Delay LFO Phase on Mono Tape);
  a resonant Multitap Delay voice filter no longer howls; Power Amp off no longer leaves a DC offset on the Princeton
  5F2-A and Liverpool (#171).
- Tests: the looper's Erase and Undo are timed on a 300 s loop, a loop export is refused while recording and held to an
  allocation budget, and the callback-stall watchdog rule is tested without a device (#241).
- Tests: a capture's drift guard holds all four measures or does not exist; ESR and MRSTFT guards sit 15 % above what
  the matches measure, and an unmatched local-only capture fails with the guard to record once its file is fetched
  (#217).
- Docs: the amp catalogue lists the Rect-o-Verb with the closed 4 x 12 its model (the head) is loaded with (#302).
- Docs: the amp catalogue lists every amp issue with its status, gives each wiki variant a status, and records what the
  AC15 and Dr Z Route 66 drawings are (#356).
- Manual: ten new screenshots cover the Power Amp page, the Cab's Align view, Amp NAM, Manage presets, Controllers,
  MIDI, footswitches, the four-cable method and Scenes (#352).
- Tests: the editor shell's Save flow and shortcuts, the transport and looper, the preset picker, the tuner dialog,
  network access, Batch Set and the block page now have unit specs (#272).
- Tests: the desktop end-to-end suite now drives Reset Amp blocks, the IR list, the Block Library, the Cab's Align view,
  Recent presets, the preset picker, the global Perform page, footswitch layout links and per-preset overrides, Preset
  Leveling on a real signal, drag-to-swap and the mono hint (#218).
- Website: Angular updated to 22.2.5 (#514).
- Website: marked 18.1.3, tslib 2.9.1, Tailwind CSS 4.3.5, postcss 8.5.32 and prettier-plugin-packagejson 3.1.0 (#516).
- Website: @types/node updated to 26.6.6 (#518).
- Website: @ng-icons/core updated to 36.1.0 (#519).
- Editor: Angular updated to 22.2.5 and the CDK to 22.2.6 (#515).
- Editor: 48 minor and patch dependency updates, among them xui 3.1.0, Tailwind 4.3.5, Playwright 1.65.0 and
  electron-builder 26.18.0 (#521).
- Editor: @types/node updated from 24 to 26.6.6 (#522).
- Editor: jsdom updated from 27 to 31 for the unit tests (#523).
- Desktop: qrcode-generator updated from 1.4.4 to 2.0.4; the network QR code is unchanged (#524).
- CI: pnpm/action-setup 6.1.1, actions/download-artifact 8.0.2 and github/codeql-action/upload-sarif 4.39.3 (#513).
- Engine: Microsoft.AspNetCore.OpenApi and Microsoft.AspNetCore.Mvc.Testing 10.0.15 (#517).
- Engine tests: CsCheck 5.0.1 (#520).
- Tests: the template end-to-end test confirms the delete, which now asks first.
- Packaging: the Server's publish finds the .NET licence files on Linux and Windows, so the desktop installers build
  there again.
- CI: the packaged-app tests start a copy of the app that takes --inspect, and check that the shipped binary's fuses are
  set.
- Tests: the REST tap tempo test bounds each tap by the engine clock on both sides of its request, so a slow runner no
  longer fails it.

### Fixed

- **The high-gain factory presets** close their noise gate at the end of a note: their gates ran at the default 2:1
  ratio, which the amp's gain undid, so a single note's tail and the guitar's hiss came out as a crackling fizz,
  nearly as loud as the note on some. They now use 10:1 at the same thresholds.
- **AIDA-X models** apply their `in_gain`, as the AIDA-X plugin does: Cathode read it but left the input unscaled.
- Plex: a resonant Low/High Pass or a Peak boost in the loop Filter no longer makes the network oscillate above full
  scale; the filter's peak decays in the Decay Time and the rest faster (#163).
- Flanger: Drive no longer adds gain inside the feedback loop, so the flange dies away after the playing stops at any
  Drive and Feedback instead of howling (#164).
- Controllers: ADSR 1 and 2 now follow their Attack, Decay, Sustain, Release and Threshold settings; they were stuck
  at the defaults (#165).
- Looper: exporting the loop while it records no longer crashes the audio engine; Export WAV is available while the
  loop plays or is stopped, and a long loop's export no longer allocates the whole loop in the engine (#166).
- Editor: New setlist, Duplicate setlist, New bank, copying a preset in Manage presets and saving to the Block Library
  work in a browser that reaches the rig over plain http (#167).
- Input block: Pickup Inductance, Resistance, Capacitance and Cable Capacitance now take effect while the preset runs,
  not only after a reload (#168).
- Looper: an overdub made at half speed is no longer 6 dB too loud, and one made at double speed no longer leaves the
  loop gritty when the speed is set back (#169).
- Looper: Erase and Undo on a long loop no longer cause a dropout; both take effect at once and do their work in small
  slices (#170).
- Host: a request that throws, or a frame that is not a valid request, is answered with an error instead of killing
  the host or dropping the link; a failed telemetry reading no longer stops telemetry, and a graph that fails to build
  no longer leaks its blocks (#172).
- MIDI: a controller that is unplugged and plugged back in quickly, or swapped for another, is reconnected within a
  couple of seconds instead of staying dead until another device change or a restart (#173).
- macOS: a MIDI controller plugged in after Cathode started is picked up within a couple of seconds (the CoreMIDI
  client now runs on a thread with a run loop) (#174).
- Server: undo and redo no longer bring back the preset's old id, colour, favourite or Library name, so Save As New
  followed by an undo and a save no longer overwrites the original preset (#175).
- Server: a pedal on Manual A-D or a dragged knob of a linked global block no longer writes global.json and publishes
  globalChanged for every message; the value reaches the engine at once and the file and event follow at most twice a
  second (#176).
- Global blocks: choosing an IR or capture, or Copy Channel, on a linked block is written to the global block and the
  preset's other linked blocks, so it is no longer lost at the next load (#177).
- Server and editor: deleting, duplicating or importing presets, saving setlists and banks, and changing assets, block
  presets, snapshots or templates now shows up in every connected editor at once (libraryChanged event) (#178, events
  only; setlists and banks still have no revision).
- Server: PUT /global refuses a startup preset or active setlist id that is not an id and a malformed output EQ or
  global block; a global.json with nulls or such ids is repaired on load instead of stopping the Server or failing
  every graph sync (#179).
- Security: a Server without an access token refuses requests from other sites' pages and requests addressed to a host
  name that is not a loopback one (cross-site POSTs, uploads and DNS rebinding) (#180).
- Server: one non-finite meter, scope or RTA value no longer stops the meters, tuner and looper display for every
  client; it is shown as 0 and logged (#181).
- Block Library: a .cthblock file exported by 1.0.0-rc.3 or earlier imports again, with its Amp entries upgraded so
  they sound the same; a file from a newer Cathode is refused with a message (#182).
- A/B compare: both versions are forgotten when another preset is loaded from a footswitch, MIDI or another editor,
  and on Revert, Clear preset and a snapshot load, so switching A/B can no longer put the previous preset's content
  into the new one (#183).
- Settings, the preset picker and the tuner no longer run in the background while closed: the Audio setup page stops
  polling the Server every 2 s, and the desktop app no longer lists audio devices at start-up (#184).
- Shortcuts no longer act on the rig behind an open dialog: only the Tuner, Settings, Keyboard shortcuts, mute and
  clip-light keys work there (#185).
- Editor: on a touch screen, touch and hold opens the right-click menu of a knob, switch or choice, the Engaged
  switch, a bank and a setlist; a knob's menu has Reset to default (#186).
- Editor: an open browser editor asks for the new access token when the Server refuses the old one (after New token),
  instead of retrying for ever with 401 errors (#187).
- Manage presets: a Save that fails part-way keeps the changes not stored yet and names the step that failed; a preset
  a setlist holds is not deleted and the message says where it is used (#188).
- Amp NAM: every capture runs with fast tanh, as the benchmark always did; a WaveNet capture costs less than half the
  CPU it did (#190)
- Amp: a NaN or infinite input sample no longer silences the Circuit Amp until the preset is reloaded (#191)
- Amp: on the Champ 5F1 and Princeton 5F2-A, Xfmr Drive no longer saturates the core at rest or spikes the output far
  over full scale when Xfmr Low Freq, Bias, Variac or another Power Tube is set (#192).
- Twin Reverb, Super Reverb and Blackface: the preamp runs on the drawings' 410 V supply node (270 V is the plate
  voltage, not the rail) and the inverter on 450 V, for more clean headroom (#193).
- Tweed Deluxe 5E3: the supply has the drawing's 5k and 22k dropping resistors, so the screens rest higher and sag
  less, and the preamp runs on the 233 V that follows from them (#194).
- Amp, Tone Stack, Graphic EQ: the Marshall-family and Bassman 5F6-A tone stacks have the middle cap on the middle
  pot's wiper, as drawn; low Bass and Middle settings keep their low end and the amps no longer go silent with all
  three tone knobs at 0 (#196).
- Amp Plexi 100W: the power amp's feedback is the drawing's 47k into the 5k presence pot (about 6.6 dB more feedback,
  the presence law of the 1959SLP and 1959T); Purple Haze and Machine Gun re-levelled (#197).
- Amp JCM800 2203/2204: the preamp and inverter rails follow from the drawing's 10k droppers (about 387 / 374 V, not
  310 / 290 V): more preamp headroom, and Preamp Sag works against the right nominal (#198).
- SLO-100: the overdrive channel now has the 330k to ground at the node where the normal channel joins, so the stage
  after the cold clipper is driven about 2.5 dB less and loses its sub-bass below 22 Hz (#199).
- Amp: the phase inverter of the SLO-100, 5150, 5150III, Rect-o-Verb, Powerball, Savage 120, Mark IIC+ and Mark IV
  idles on its tail resistor plus the feedback shunt under it, as the Dual Rectifiers already did, instead of 15 to 30
  % hot (#200).
- Packaging: the desktop installers and the headless package carry Cathode's licence, THIRD-PARTY-NOTICES.md and the
  licence texts of the third-party code in a licenses folder; the desktop app opens it from Help > Open Source
  Licences (#206)
- Desktop: Electron is at 45.2.3, the newest patch of the 45 line, with its Chromium and Node security fixes (#207)
- Linux: the packages state their minimum system (glibc 2.39: Ubuntu 24.04, Debian 13, Raspberry Pi OS based on it),
  the release build checks it, the NAM library no longer needs the system's libstdc++, and the headless installer
  refuses a system the engine cannot start on before changing anything (#212).
- Headless rig: channel options in `CATHODE_HOST_ARGS` with an input map (`--input-map 1;2`) no longer stop the
  engine; the line is split at spaces and never read as shell (#213).
- Looper: a loop recorded and stopped keeps its first notes when you play it later; the seam crossfade no longer fades
  unrelated playing into its start (#223).
- Delay lines: fractional reads keep full resolution on long lines (the Delay's 16 s), removing a low-level granular
  noise on modulated and tape repeats, worse at 96 and 192 kHz (#224).
- Delay, Filter, Multi Delay, Plex: L/R Time Ratio, Head 2 Ratio, Right Post Delay, Comb Time and Pre-Delay glide per
  sample instead of stepping once per audio callback (#225).
- Pitch: the Voices, Whammy, Octave, Capo, Crystals, Arpeggiator, Key, Scale and Custom Shifter table controls are
  shown only on the types that use them (#226).
- Tremolo, Enhancer, Delay: Pan Center, LFO Phase, Enhancer Time and side cuts, and Bit Reduction on Vintage Digital
  are hidden on the types where they did nothing (#227).
- Gate/Expander: the soft Knee now works above the threshold with any Hold, and on Modern Expander, where it did
  nothing (#228).
- Delay: Repeat Hold now takes the loop filters out of the loop, so held repeats keep their level and highs on Analog,
  Tape, Mono Tape and Vintage Digital; Stack still filters every pass (#229).
- Tuner: low strings (bass E and B, 7/8-string guitars) now read correctly at 88.2, 96, 176.4 and 192 kHz (#230).
- MIDI clock: when an external clock pauses and resumes (or sends Start, Continue or Stop), the tempo holds until a
  full beat of new ticks is in instead of dropping to 30 BPM for a beat (#231).
- Host CLI: out-of-range or malformed --rate, --buffer, --seconds, --tail, --length, --level and --size, an unwritable
  --out and a missing or malformed --graph give a one-line error and exit code 1 instead of a crash; --ir-capture no
  longer hangs when the device stops mid-sweep (#234).
- Host: losing the audio device on macOS exits with code 3 as documented, even when stopping the vanished device fails
  (#235).
- Host: a starting host removes the telemetry files left next to its socket by hosts that crashed or were killed
  (#236).
- Host: a parameter update with a block or parameter id longer than 64 bytes is applied (through the slow path)
  instead of being reported as an unknown parameter after part of it was set (#237).
- FX Loop: a mono send (one --loop-out channel) carries both sides of the send bus folded to mono instead of only the
  right side (#238).
- IPC: the Server no longer keeps an entry for every engine request that timed out, was cancelled or failed to send
  (#239).
- MIDI out: the host refuses anything that is not one complete short MIDI message, and on macOS system messages
  (clock, start, song position) are sent as valid packets (#240).
- Assets: an upload that is cancelled, cut off or over the size limit no longer leaves its temporary file in the
  assets folder, and files left by an earlier crash are removed at start-up (#242).
- MIDI: footswitch LEDs and the bypass toggle read a published copy of the block states instead of the scene's
  dictionary while the rig writes it, and a failed LED refresh is logged instead of ending the Server (#243).
- MIDI: a note-on of any velocity above 0 now counts as a press (soft pads and keys fire their actions); a note-off is
  never learnt (#244).
- MIDI: Scene Level Up/Down stores only the scene level in the saved preset instead of saving the whole working copy;
  on a factory or unsaved preset it stays an unsaved edit, makes no copy and keeps the bank position (#245).
- Server: a block can no longer be given the reserved ids rig or global/global2-4 (its knobs went to the scene level,
  tempo or a global EQ), a client-chosen block id must be 1-64 letters, digits and hyphens, and a block whose id
  merely starts with shunt keeps its meter (#246).
- Server: the EQ chart, speaker resonance, Cab Align page and cab IR export describe the channel a block is actually
  running when Scene Ignore holds it, and Save All Blocks stores the live channel's values instead of scene 1's
  (#247).
- Preset import: a damaged or hand-made .cthpreset package answers with a reason instead of a server error, and a
  refused package no longer leaves its IRs and captures in the library (#250).
- Server: PUT /live clamps tempo, scene levels, scene controllers and block parameter values and refuses invalid scene
  MIDI or scene channels with 400 (a null channel used to be a 500) (#251).
- Server: POST /live/save and the MIDI save action no longer store a preset with an empty name (after Clear); the save
  is refused until the preset is named (#252).
- Cmd/Ctrl+C copies selected text (the rig's address, the access token) instead of the block, and each network address
  has a Copy button (#253).
- Audio setup: the engine log has a Copy button, and the log, the latency test's output and the IR capture's output
  can be selected (#254).
- Manage presets: leaving the page with unsaved changes (Close, the navigation bar, Back, a reload) asks before
  discarding them (#255).
- Setlists: two edits made in quick succession are both kept; before, the second could overwrite the first (#256).
- Knob: a typed value is read in every unit the engine uses, with a decimal comma or as -∞, and text that holds no
  value keeps the field open and marked instead of being dropped silently (#257).
- Knob: Escape in the typed-value field keeps the value, returns focus to the knob and no longer closes the dialog
  around it (#258).
- Tooltips, menus and hints show Ctrl, Alt and Shift on Windows and Linux instead of the macOS key symbols (#259).
- T opens the tuner, as well as Cmd/Ctrl+T, which a browser keeps for a new tab (#260).
- Controllers page: one drag of an LFO, ADSR, envelope follower, pitch follower or sequencer control is one undo step,
  and the next drag is another (#261).
- Editor: meters, clip marks, the tuner and the looper readout no longer freeze at their last values when the
  connection drops or the engine stops (#262).
- Grid: a cable drag interrupted by a scroll or system gesture is dropped instead of connecting on the next tap
  (#263).
- Save to the Library: the dialog stays open, with the name and folder as typed, when the save is refused (#264).
- Scene name: a rename is applied to the scene whose name was clicked, even if another scene becomes active while the
  field is open (#265).
- IR capture: a sweep level of 0 dBFS can be entered, and the sweep length, level and IR length are limited to their
  ranges (#266).
- Preset menu: deleting a snapshot or a template asks for confirmation first (#267).
- Preset and IR pickers: Tab no longer walks through two hidden buttons per entry; a star and a colour button under
  the list favourite and tag the highlighted entry (#268).
- Editor: a block's page tabs and the list of setlists are one tab stop each and move with the arrow keys, Home and
  End; the page panel is named by its tab (#269).
- Amp: a type change back to an amp, a channel switch and Tonestack Location no longer re-solve every stage's
  operating point on the audio thread (channel reset about 0.5 ms down to 5-20 us on the SVT, Subway Blues and
  JVM410H) (#274).
- Amp: switching Power Amp off while playing no longer leaves the preamp on a sagged supply; it runs on the supply at
  rest, and still follows Variac, Sag and Bias (#275).
- Amp: Cathode Time Const, Master Trim, Bright Cap, Stack Freq, Fat, Preamp Sag, Preamp Tube and Overdrive are hidden
  on models where they have nothing to act on; the Portaflex B-15N no longer shows a Middle knob (#276)
- Amp: Preamp Tube also replaces stages that run both halves of a 12AX7 in parallel (Chieftain, DC-30, 18 W 1974X,
  AFD100, Quick Rod) (#280)
- Tweed Deluxe 5E3: the Tone pot is wired as on the drawing, so the 500 pF bright cap fades out as Tone is turned down
  instead of staying across the Instrument volume (#281).
- Blackface: built from the AB763 parts (470 Ω inverter bias, 34k input stopper, 0.047 µF and 1 nF into the inverter,
  10K-A mid pot), so it has more low end and its Middle knob reads like the Twin's and Super's; the Tone Stack block's
  Fender type and Prometheus Clean take the same audio-taper mid pot (#282).
- Amp: the Princeton Reverb and Princeton AA964 use the 125A10B output transformer's 8.5k primary (was the Deluxe's
  6.6k): a little less clean power and an earlier clip (#284).
- Hot Rod Deluxe: the Presence knob follows the amp's reverse-audio 25k pot, so it lifts more of the top at noon and
  is spread more evenly along its travel (#286).
- Amp: Master Location is available on models whose master sits inside a channel switch (Bassman 135, Hot Rod Deluxe,
  ODS 97, Hot Cat 30) and acts while that channel plays (#287)
- Amp JTM45: the screens hang on the drawing's shared 1k 2 W dropper, for more screen sag at full power (#289).
- Amp 18 W 1974X: the Stock speaker load is the open-back 1 x 12 of the 1974 combo, not a 2 x 12 (#290).
- Amp: Triode 2 Plate Freq is available on the Rectifiers, Rect-o-Verb, Mark IIC+, Mark IV, Legacy, Prometheus and
  other models with a cap across a plate resistor (#293)
- Amp: Presence Shift is shown only on models whose power amp has a presence circuit (#294)
- Amp: Depth Freq and Presence Freq are hidden on models with no depth control or presence circuit for them to move
  (#295)
- SLO-100, Powerball: the preamp supply nodes rest at the rails the stages are built for (the SLO-100 at the drawing's
  497 V B+; the Powerball's stages on their chain's own voltages), so Preamp Sag is scaled from the right rest point
  (#296).
- Bass 400: the tone stack's 47 nF bass cap and 0.1 µF middle cap are confirmed against the 1985 drawing (the reverse
  of the Mark guitar amps) and documented; no change in sound (#299).
- Desktop: a settings.json with values of the wrong shape (a remote Server without a valid address, a bad port, audio
  fields that are not lists) no longer leaves a blank window or a failed engine; the bad parts take their defaults
  (#313).
- Desktop: switching to a remote Server or quitting during a latency test or IR capture no longer leaves an engine
  holding the audio interface or a sweep playing after the app has gone (#314).
- Desktop: the engine status no longer says running while the host is crash-looping on a bad device; a host that keeps
  exiting is shown as failed with its last log line, and restarts after an occasional crash stay fast over a long
  session (#315).
- Windows: the audio and NAM libraries carry the Visual C++ runtime, so the engine loads on a machine without the
  Visual C++ Redistributable (#323).
- Headless rig: `uninstall.sh` also removes the server's cache (`/var/cache/cathode`) and the socket folder, and an
  update clears the previous version's unpacked files (#324).
- Amp: the Friedman BE-100 (both channels) and the Laney GH100L wire the Middle pot as their drawings do, with the
  middle cap on the wiper, so Bass and Middle interact correctly at low settings and the amps no longer go silent with
  all three tone knobs at 0 (#196).
- Amp: the Powerball's preamp follows a hand-corrected copy of Engl's drawing, so its lead has a high-gain amp's gain
  instead of a fuzz's, cuts the lows between its stages and no longer needs the assumed supply filter (#512).
- Amp: the Fireball 60's preamp follows a redraw of the E625 with the parts Engl's drawing leaves out, so Ultra has a
  high-gain amp's gain and a note gets quieter as it dies away, instead of staying a square wave; the clean channel is
  much quieter than Ultra, as its 22k divider makes it. Its Depth switch works (on the Fat switch), and the assumed
  resistor and supply filter are gone (#525).
- Amp: the power amp's phase inverter takes the feedback of the sample it plays, not of the one before, where its input
  network's grid leak returns to the feedback. The delay had cut the top end of the high-feedback models at 44.1 and
  48 kHz (up to 2.4 dB at 8 kHz and 5 dB at 14 kHz on the Savage 120 and the Powerball) and made them sound different
  at 192 kHz; every model with that input network (54 of them, most Fenders, Marshalls, Mesas and Engls) now sounds
  the same at every sample rate, with a little more treble than before on the ones with the most feedback.
- Amp: the Bass 400 is rated 300 W, not the 400 of its name; its 1.2k output transformer is kept, now checked against
  what its six 6550 deliver (#300).
- Amp: the Mark IIC+ and Mark IV run on the factory sheets' supply voltages (B+ 480 V, 397 V and 392 V preamp rails) and
  their decoupling chain rests at the rails the stages are built for (#296).
- Diagnostics: the Drive and Wah blocks report their circuit solvers' fault counts like the Amp, the Server logs a block
  whose count rises, and the block page shows a Solver faults line when one is not zero (#278).
- Amp: the documentation says how far a cranked power amp's flyback passes full scale (up to about 5 dB for a few
  samples), and a test pins it (#277).
- Amp: a type change, a tube swap, the triode switch and Tonestack Location are settled on the control thread, so the
  audio thread's switch no longer solves the operating points (#274).
- Amp Rect-o-Verb: the effects loop's tube stages and the Output Level (Control 1) follow the channel masters, as on the
  amp; the type drives its power amp about 15 dB harder at the same settings (#305).
- Packaging: a release links the Linux engine on Debian 12, so the AppImage and the headless rig run on glibc 2.36 and
  newer (Debian 12, Raspberry Pi OS bookworm, Ubuntu 24.04) instead of 2.39 (#212).

## 1.0.0-rc.5 — 2026-10-30

### Added

- **Setlists** (#28): a Setlists page makes them from the library, each song a preset on its starting scene with an
  optional name, reordered by drag or ⌥↑ and ⌥↓. The Perform page plays the chosen setlist song by song, showing the
  current and next song; ⇧⌘PgUp and ⇧⌘PgDn and the new `songPrevious` and `songNext` MIDI actions step through it,
  stopping at either end and passing over a missing preset. The setlist being played survives loading other presets
  and restarts.
- **Gain reduction you can see** (#29): the Compressor, Multiband Compressor and Gate show how much they turn the level
  down, with a one-second peak hold; the Multiband Compressor has a meter per band.
- **The Compressor's transfer curve**: a chart of what it does to each level, beside its meter on the Basic page, with
  a dot riding the line where your playing is right now.
- **EQ response charts**: the Graphic EQ, the Drive's Graphic EQ page and the Amp's Output EQ draw their frequency
  response, measured on the engine's own equaliser, so the passive layouts show how their circuits really behave. The
  Server gives it at `GET /api/v1/live/blocks/{id}/response`.
- **A strobe tuner**, tunings with per-string offsets (Standard, drops, opens, 7- and 8-string, bass) and the A4
  reference (430–450 Hz) in the tuner's dialog; the reference also reaches the engine's pitch detection.
- **Engage modifiers** have a **Moving** mode (on as soon as the source leaves where it rested), a threshold and an
  off delay, so an auto-engaging wah comes on at a touch and stops flickering. Presets without them work as before.
- **The Controllers page** marks the sequencer's stage as it plays, and the LFOs and ADSRs show where they are.
- **A bank menu beside the preset**: it names the bank next and previous step through, and choosing another bank
  loads its first preset and plays on from there.
- **Footswitch layouts can be deleted** and **views renamed** (Global ▸ Footswitches). Deleting a layout makes the
  first one left active and unlinks switches that went on to it.
- **⌘K resets every clip light** at once: the blocks' on the grid and the top bar's meters.
- **The looper's dialog** says which looper it drives, the Looper block in the preset or the rig's own.
- **Input 1's calibration** can be reset to the 1 V default from Audio setup.

### Changed

- **A new look**: the editor takes a Logic Pro style theme. Graphite panes with header bars, bezelled buttons,
  macOS-style menus, pop-up buttons, switches, checkboxes and alerts, an LCD preset and scene display, knobs with a
  turned-metal dial, and blocks drawn like Logic regions. Shunts are plain wires, and a block bypassed in a mode that
  lets the signal through shows the cable arching over it. The Library, Scenes, Controllers, Perform, Setlists, Global
  and Manage presets pages, the Settings dialog, the Cab's microphone and Align charts and the Analyser follow it.
  Text fields are sunken as macOS's, every control shares one focus ring, number fields step with the preset
  display's arrows instead of the browser's, the MIDI monitor reads on LCD glass, and delete buttons turn red only
  under the pointer.
- **Own icons** for the metronome, looper and tuner.
- **A block's level** runs along its tile's bottom edge, and its clip light shows only once the block has clipped.
- **A new app icon**: a glowing blue C with a waveform.
- **Block pages** list their pages as tabs down the panel's edge and fit eight knobs to a row on a 1440-pixel window
  instead of six. The Parametric EQ's curve and the dynamics meters sit beside the knobs instead of above them.
- **Typing a value** selects the current one, so typing replaces it: double-clicking a knob, and clicking the tempo.
- **The tuner's** strobe and needle are larger.
- **The looper's dialog** groups its buttons as Logic's transport: Record, Play and Stop as symbols lit in their colour,
  then Overdub and Undo, then Reverse and Half speed.
- **Audio setup** shows the engine and latency readings on LCD glass, and its cards span the dialog. The Settings
  dialog is taller, and each tab's scrollbar sits at its edge instead of against the cards.
- **The editor's address keeps the access token**, so a page saved to an iPad's Home Screen or bookmarked opens
  connected. Treat such a link like the token itself.

### Fixed

- **Knobs and the sequencer's steps** stop following the pointer when the button comes up over the desktop app's
  title bar.
- **Reloading the editor** no longer warns that it is not connected to a Cathode server while it connects.
- **The block picker:** Escape closes it at once, so Enter can reopen it straight away; and typing no longer
  highlights the tile that happens to land under a pointer that has not moved.
- **The Server** counts a client's socket before its handshake completes, so an editor that reconnects keeps its tuner,
  scope or RTA on.
- **Manage presets** marks the bank shown before you pick one, and its Library list and the Setlists page's no longer
  scroll sideways with a long preset name.
- **The New from template and Snapshots dialogs** no longer push their buttons past the dialog's edge.
- **Shortcut letters after a click** (#39 follow-up): clicking a top-bar button such as the metronome no longer stops
  M, P and the other letter shortcuts working, or rings the button at the next key. Space, Delete and Backspace still
  go to a focused control.
- **Dialogs opened from the keyboard** no longer start with a focus ring on their Close button.
- **A block's clip light** can be reset: the tile ignored the click.
- **Escape** cancels a typed tempo instead of setting it.
- **Footswitches:** the Sends message select is no longer squeezed, and a select's list grows to show its entries
  whole.
- **The navigation bar** no longer cuts off the focus ring of its first and last page.
- **Input 1's interface maximum level** field always shows the value in use, also after a reset or a measurement.
- **No MIDI output** is no longer an engine error: with no output chosen and none connected, scene MIDI and footswitch
  LEDs have nowhere to go, and Audio setup stopped showing "MIDI output is not available." for it.

### Internal

- **Telemetry** (layout version 8) carries the controllers' state and up to four meters per block.

## 1.0.0-rc.4 — 2026-10-27

### Added

- **ASIO on Windows.** Installed ASIO drivers are listed as devices beside the WASAPI endpoints (`asio:` uids), with
  their channel names, buffer sizes and latencies. Only one ASIO driver can be open at a time.
- **MIDI output destination** (#111): Global ▸ MIDI ▸ Send to picks the port scene MIDI and footswitch LEDs go to.
- **External 1–8 from the editor** (#108): the Controllers page moves them, as a pedal over MIDI does.
- **Perform page and footswitch overrides** (#107): controls are renamed and reordered, and a preset's switch
  override has its own hold action and label.
- **Tone Match amount and smoothing** (#109) are chosen in its panel.
- **Amp Oversampling** (#88): a Circuit Amp's Advanced page can run the whole circuit at 4x (High) for 7–11 dB less
  aliasing on the high-gain models, at about twice the CPU. Standard stays the default and sounds as before.
- **Per-model defaults** (#48): a Circuit Amp model can ship its own knob defaults; a new Quick Rod starts with its
  Solo Boost off, so Master 0 is silent. Saved Quick Rods keep the boost they had.
- **Knobs shown by another knob's setting** (#30): the Compressor shows Threshold and Ratio, or Compression, by its
  Controls choice, and the Vibe shows Mix only in Chorus mode.
- **MIDI learn you can see and cancel** (#51), after a reload and from any editor; a knob's learn adds its External
  modifier only once something is learnt.
- **Footswitch and MIDI actions are picked by name** (#18): blocks, banks, slots, scenes and layouts from lists.

### Changed

- **Opening the tuner mutes the output** (#26), with a short fade, as players expect. A global setting below the
  tuner's needle, **Output while tuning**, chooses Mute (the default), Bypass (the guitar dry, no blocks) or Pass
  through (the preset keeps sounding, as before). It applies however the tuner is opened: editor, footswitch or MIDI.
- **Long lists stay quick** (#157): the Library's preset, NAM amp and IR cab tables and Manage presets' Library list
  render only the rows in view, so searching and selecting in a library of hundreds of IRs no longer slows down. A
  preset's tags now sit on its name's line.
- **Undo** (#123) keeps at most 100 steps and 32 MB per stack, and repeated changes to one knob are one step only
  while they come within 2 s of each other.
- **Pasting or ⌥-copying a block** (#154) is one change and one undo step, its settings and assets placed with it.
- **Obsessive OD** (#142) clips with two MOSFETs and a germanium diode in series with one, as the Fulltone OCD does,
  and is re-levelled.
- **The Server rejects malformed grids** (#116) with a reason (size, positions, overlapping blocks, cables), and a
  preset holds at most 256 modifiers, so telemetry never drops a meter or a modifier.
- **Removed API routes** (#110) that no client used and that duplicated others: `GET /catalog/blocks/{typeId}`,
  `GET /block-presets/{id}`, `GET /banks/{id}`, `PUT /presets/{id}` and `PUT /live/banks/{id}/slots/{slot}`.
- **Desktop settings** (#131) are written atomically, owner-only, with the network and remote tokens encrypted at
  rest where the system offers it.
- **Tube models match their datasheets** (#86): the 12AX7, 12AT7 and EL34 reproduce their published operating
  points, which re-voices every amp a little (most by well under 1 dB; Liverpool, Subway Blues and Silver Jubilee
  by 2–3 dB at low gain). The factory presets are re-levelled.
- **Tone stacks** (#87) load the stage before them with their real, knob-dependent output resistance.
- **Tiny Terror, Chieftain and Hot Cat** (#96): their Tone and Brilliance sit on the Presence knob under their own
  names and turn the right way (clockwise brighter). Saved presets are converted and sound the same.
- **JTM45** (#95) is rated 30 W on a 6.6k primary, as the KT66 originals were.
- **Undo** follows knob gestures (#72): one drag is one step however long it lasts, and the next drag is another.
- **Concurrent editors** (#75): `/global` carries a revision, and a knob change made before another preset loaded
  is dropped instead of landing on the new preset.
- **Next and previous preset** (#25) follow the banks' slot order (and the setlist a preset was loaded from), not
  the alphabet; the number before the preset's name is its slot.
- **Mark IIC+ and Mark IV presence** (#102) is the 250k rheostat in series with the feedback, as Mesa drew it:
  clockwise is brighter, and the loop stays stable at both ends.

### Fixed

- **Hiwatt DR103 and DR504:** the fixed 470 Ω and 10 nF across the feedback shunt are now modelled, so less
  feedback above about 6 kHz gives these amps their stock treble shelf at any Presence setting (#44). The factory
  presets on a Hiwatt are re-levelled.
- **Savage 120:** the power section is four 6550, two a side into 1.7k, as Engl specifies it, instead of two (#49).
- **Rotary:** with Tempo set, Slow and Fast still switch the horn: the fast horn turns once per division and Slow
  keeps its ratio to it (#9).
- **Synth:** a drone voice's (Track Off) filter follows the playing envelope instead of staying fully opened by Env
  Amount (#12).
- **Pitch:** the Tonic knob, which changed nothing, is hidden; the custom scale is built on, and named in, Key (#6).
- **Editor:** the leveling dialog and the Perform page no longer do telemetry-rate work while idle (#155).
- **IR Player:** clearing its last IR fades the wet signal out instead of cutting it (#144).
- **Tuner:** detection keeps its window in a ring buffer instead of shifting it for every sample (#145).
- **Meters** resolve their theme colours once per theme change, not on every frame (#150).
- **Hot Cat 30:** its Master turns the drive channel down to silence instead of by about 1 dB (#94).
- **Mark IIC+ and Mark IV:** the power stage has Mesa's 470 Ω screens, 2.2k stoppers, 0.1 µF coupling caps and the
  inverter's 470 Ω / 22k tail (#102).
- **The engine host** plays the test tone again in the two-process app: it never handed it to its graph compiler
  (found by #53's two-process test).
- **The block picker** clears its search as it closes, so a query typed at once is never wiped before Enter.

### Internal

- **The website is checked in CI** (#160): formatting, the generated catalogue and manual against the engine, every
  knob described, and the prerendered build.
- **The editor's stateful core has unit tests** (#159): LiveStore, EventStream, shortcut dispatch and the grid's
  drop, paste and wire.
- **CI** (#132–#135): workflows run with read-only tokens and pinned actions; a release waits for the engine and
  editor tests and carries build provenance and an attested `SHA256SUMS`; a nightly workflow fuzzes block
  parameters and runs the native audio path under RealtimeSanitizer.
- **Layering** (#117): the IPC contract depends on nothing, and the Server links no engine host, audio or native
  code. The in-process engine mode is gone: run `cathode-host --serve --null-audio` and point the Server at it.
- **Docs:** the architecture's audio backends (#130) and API table (#112), and the Formant block's five vowels
  (#147), describe what is built.
- **Amp tests:** reference-circuit chain comparisons for the Mark IIC+, Mark IV, Lone Star, Prometheus (#106),
  1987X, DR504, 18 W, Major, Plexi, JCM800 and Laney (#99); solver verification for inductor ports, sources in
  adaptors, limiters, AC ripple, rate changes, tube slopes and DenseLu (#89). The Hiwatt preamp, the Marshall
  two-channel input and the AB763 fixed-mid stack are shared instead of copied (#98, #91).
- **Capture matches run nightly** (#81) against the fetched GPL references; a missing one fails.
- **The headless systemd units run sandboxed** (#137).
- **More test suites** (#53): every block at 44.1–192 kHz against its 48 kHz render, SIMD against scalar in two
  processes, a legacy corpus of rc.1–rc.3 libraries that must load and play, and a real two-process Server and host
  run with a host restart. Stale comments and docs/07's amp index (#16) are corrected.
- **Skala's baseline is burnt down** (#161): from 4,321 accepted findings to 134, all of them duplicated-block
  groups between data (amp schematics, presets, catalogues) or code kept apart on purpose, each kind named in
  `skala.jsonc`. The rest were fixed, or their rule was switched off in `.editorconfig` with the reason it does not
  fit. CI writes the baseline's size to the run's summary and fails if it grows. No sound changes: the refactored
  DSP renders bit-identically.

## 1.0.0-rc.3 — 2026-10-26

### Added

- **Global mute.** Every output fades out, from the mute button in the navbar, the M key or a footswitch (the MIDI
  action "Mute", with its LED). The mute is kept across preset changes.
- **Knobs follow their modifiers live**, as in Axe-Edit: a knob driven by an LFO, an envelope or an expression
  pedal turns with the value it is getting.

### Changed

- **The block picker is one grid** of every block, without category headings, sized so you can pick a block
  without scrolling.
- **The editor window** opens at 1440 × 1094 (also its minimum), and the bottom drawer takes half the height.
- **The block page:** the left column and the controls scroll on their own, and "Find the cab resonance" sits below
  the amp's controls.
- **Controllers:** LFO 1 and 2, ADSR 1 and 2 and the two envelope followers sit side by side.

### Internal

- The engine is checked by [Skala](https://github.com/Rikarin/Skala) in CI: formatting, analysis and duplication,
  with its findings in GitHub code scanning. Its 139 errors are fixed; the other 4,321 findings (171 warnings,
  4,150 notes) were accepted in a committed baseline, which only kept new code from adding to them. No sound changes.

## 1.0.0-rc.2 — 2026-10-25

### Fixed

- **High-gain amps no longer hum after you stop playing.** Nine amps could lock into a 35-70 Hz oscillation once a
  loud chord stopped, depending on the moment the playing stopped: the 5150, the SLO-100, the Dual, Triple and
  3-Channel Rectifiers, the Rect-o-Verb, the TriAxis Rhythm, the Savage 120 and the Powerball. Every preamp stage
  now runs on its own filtered supply node, as in the real circuits (read from the schematics for these nine; a
  typical 10k / 22 µF chain elsewhere). Amps with three or more gain stages sound subtly different (at most a
  -26 dB difference, under 0.15 dB in level); the others are unchanged.
  - The Powerball's supply as drawn cannot hold its gain, so it has two extra filter nodes for V5 and V6 that are
    not on the drawing: an assumption, documented in the model.
  - The Powerball's inverter grid leak, its Open and Focused middle controls and two loads are corrected from the
    drawing; the middle control changes its tone at the mid settings.
- **The headless server starts in seconds.** Under systemd it took one to three minutes before the editor answered:
  it watched the whole disk for a settings file Cathode does not have.

## 1.0.0-rc.1 — 2026-10-25

The first release candidate: everything below is new.

### Amps and cabinets

- **88 amp models built from their schematics**, stage by stage, including the tone stack, the power amp, the output
  transformer and the speaker load. Each amp has its own front-panel controls, plus expert settings: tubes, bias, sag,
  supply and variac. New since the last preview builds: the Marshall AFD100 (both modes) and the Splawn Quick Rod.
- **A faster circuit solver** with the same output to the bit. Amps are also built only when first chosen, which
  takes start-up from about 22 s to under half a second.
- **Neural captures:** NAM (`.nam`) and AIDA-X models in the Amp NAM block.
- **Cab block:** 10 speakers, 11 microphones and 7 cabinets, with mic position, distance and angle. It also loads your
  own impulse responses (`.wav`, `.cabgrid`), mixes and trims them, and captures new ones with a sine sweep.

### Blocks and the grid

- **49 block types:** drives, delays, reverbs, modulation, pitch, synth, vocoder, dynamics, EQ, filters, a looper, a
  tuner, mixers and FX loops.
- **A grid like the hardware's:** six rows of fourteen cells, with parallel paths and feedback loops.
  - A cable dragged across empty cells is carried by shunts.
  - Block controls are grouped into pages.
- **Global blocks:**
  - Make a block global (G) and link blocks in other presets to it, from the block's right-click menu.
  - Linked blocks follow each other's edits, in the same preset too.
  - The Library lists global blocks, to rename or delete them.

### Presets and the Library

- **The Factory bank:** 70 read-only presets recreating well-known guitar tones, from blues and classic rock to modern
  metal, jazz and shoegaze.
  - Each has scenes, a description and a level matched to the rest of the bank.
  - Driven presets have a noise gate; clean presets have a compressor that catches a hot pickup's peaks.
  - Saving a factory preset stores your own copy.
- **One Library** for presets, banks and setlists, block presets, global blocks, NAM amps and IR cabs. Presets have
  favourites, colours, tags and a description.
- **The preset and scene display:** names and up/down steppers. P opens the preset picker, by name or bank slot.

### Playing live

- Eight scenes per preset and four channels per block.
- Modifiers driven by LFOs, envelopes, a sequencer or MIDI.
- A Perform page, MIDI control, footswitch layouts, a metronome and tap tempo.
- Keyboard shortcuts:
  - A, B, C and D pick a block's channel.
  - Space bypasses a block.
  - ? lists every shortcut.

### Platforms

- **Desktop app** for macOS (Apple Silicon), Windows (x64) and Linux (x64 AppImage).
- **Headless rig package** (`cathode-server`, Linux x64 and ARM64): runs on a small computer without a display and
  serves the editor to any browser on the network, with an access token.
- **Audio:**
  - macOS: CoreAudio and CoreMIDI.
  - Windows: WASAPI and WinMM MIDI.
  - Linux: ALSA (direct), JACK or PipeWire, and ALSA sequencer MIDI.

### Fixed

- Resetting a block that is off the signal path no longer restarts the rest of the preset. The arpeggiator, delays
  and reverbs used to restart mid-note; this was heard as a swell or a dropout.

### Known limitations

- **macOS:** builds are not notarised; on first launch, right-click the app and choose Open.
- **Windows:**
  - Builds are not signed, so SmartScreen asks you to confirm.
  - WASAPI exclusive mode and inputs have not been tried on real interfaces yet, and there is no ASIO.
- **Linux:** audio has been tested in containers, not yet on a real interface.
- **Factory presets:** a wah or whammy rests at the heel until you assign an expression pedal (External 1).
- **CPU at high gain:** the Marshall AFD100 and the Silver Jubilee use more CPU than the budget at high gain.
