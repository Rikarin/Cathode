# Changelog

Cathode follows [semantic versioning](https://semver.org). From 1.0 on, a preset saved by any release keeps loading
in every later one: the preset format carries a schema version (`schemaVersion`, now 2), and a release that changes
the format upgrades older presets as it reads them.

## Unreleased

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
