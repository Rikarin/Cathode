Cathode is a guitar amp and effects processor that runs on your computer. You plug your guitar into an audio interface, and Cathode turns the computer into a full rig: amps modelled from their schematics, speaker cabinets with microphones you can place, drives, delays, reverbs, modulation, pitch effects, EQs, dynamics, a looper and a tuner. You arrange them on a grid, save them as presets, and switch sounds while you play with scenes, a MIDI foot controller or the keyboard.

It is free and open source (GPL-3.0-or-later). It borrows its ideas from the Fractal Audio Axe-Fx III, so if you have used one, the grid, scenes, channels and modifiers will feel familiar. It is not affiliated with Fractal Audio, and it does not try to copy every feature of that unit.

> **Note:** This manual describes Cathode 1.0.0-rc.3, a release candidate. Presets you save from 1.0 on keep loading in later releases.

## Two ways to run it

**The desktop app** is for a Mac or PC with a screen. It contains the editor you see and the audio engine that does the processing. This is where almost everyone starts.

**The headless server** is for a small Linux computer that lives in your rig, such as a Raspberry Pi 5 or a mini PC, with no screen. You edit it from a browser on a phone, tablet or laptop, or from the desktop app. See [The headless server](/manual/headless-server).

## What you need

- **A computer.** A Mac with Apple Silicon, a Windows 10 or 11 PC (x64), or a Linux PC (x64). The headless server runs on Linux x64 or ARM64.
- **An audio interface** with an instrument input (often marked _Hi-Z_, _Inst_ or with a guitar symbol). The interface turns your guitar's signal into numbers the computer can work with, and back into sound. Cathode needs one device that has both inputs and outputs.
- **Something to listen on.** Headphones, studio monitors, or a full-range speaker (FRFR, see the [glossary](/manual/glossary)). A guitar amp works too, with care: see [What to plug into](/manual/audio-setup#what-to-plug-into).
- **Optional:** a MIDI foot controller and expression pedals for playing live.

### How ready each platform is

| Platform                                    | Audio and MIDI                                                | Status                                                                                      |
| ------------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| macOS (Apple Silicon)                       | CoreAudio and CoreMIDI                                        | Ready                                                                                       |
| Windows 10 and 11 (x64)                     | WASAPI or ASIO, and Windows MIDI                              | Early: WASAPI tested on shared-mode output only; ASIO not yet tested with a real interface. |
| Linux (x64 desktop, x64 and ARM64 headless) | ALSA or JACK (including PipeWire's JACK), ALSA sequencer MIDI | Early: tested in containers, not yet on a real interface.                                   |

If Cathode cannot open an audio device, it runs on a silent "null" backend. You can still build and edit presets, but you will not hear them.

## The ideas in one minute

- **Block:** one effect or tool, such as an Amp, a Cab, a Drive or a Reverb.
- **Grid:** six rows of fourteen cells where you place blocks and connect them with cables. Sound flows from left to right.
- **Preset:** one complete rig: the grid, every block's settings, and its scenes. You load one preset at a time.
- **Channel:** each block holds up to four complete sets of settings, A to D. A Drive can be a light boost on A and a fuzz on D.
- **Scene:** a snapshot of which blocks are on or off, and which channel each one uses. A preset has eight. Switching scenes is instant and gapless, so it is how you change sounds within a song.
- **Modifier:** a link that moves a knob automatically, from an LFO, an envelope, an expression pedal or another controller.

## How this manual is organised

- **Start here** gets you from download to playing: [Getting started](/manual/getting-started), [Audio setup](/manual/audio-setup) and [A tour of the editor](/manual/the-editor).
- **Using Cathode** explains each part of the app: [the grid](/manual/the-grid), [presets, scenes and channels](/manual/presets-and-scenes), [the Library](/manual/library), [controllers and modifiers](/manual/modifiers), [MIDI and footswitches](/manual/midi-and-footswitches), [playing live](/manual/live-performance) and [the headless server](/manual/headless-server).
- **Finding your sound** is about tone: [getting your sound](/manual/getting-your-sound), [gain staging](/manual/gain-staging), [amps](/manual/choosing-an-amp), [cabs](/manual/cabs-and-mics), [signal chains](/manual/signal-chain), [tone recipes](/manual/tone-recipes) and [NAM captures and IRs](/manual/nam-and-irs).
- **Reference** has the [keyboard shortcuts](/manual/keyboard-shortcuts), [troubleshooting](/manual/troubleshooting), the [glossary](/manual/glossary), and a page for every block that explains each of its knobs.

## Conventions

- Keyboard shortcuts are written the Mac way: **⌘** is Command, **⇧** is Shift, **⌥** is Option. On Windows and Linux, read ⌘ as **Ctrl** and ⌥ as **Alt**.
- Names of buttons, menus and fields are in **bold**, spelled as they appear in the app.
- "The editor" is the window you work in. "The engine" is the part that processes audio. They are separate programs that the desktop app starts together.
