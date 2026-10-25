# Changelog

Cathode follows [semantic versioning](https://semver.org). From 1.0 on, a preset saved by any release keeps loading
in every later one: the preset format carries a schema version (`schemaVersion`, now 1), and a release that changes
the format upgrades older presets as it reads them.

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
