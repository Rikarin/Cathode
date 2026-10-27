The editor is the window you build and play your presets in. This chapter walks through it from top to bottom: the navigation bar, the preset bar, the pages, the grid and the block drawer, the dialogs, and the settings that change how the editor behaves. Later chapters go into each part in depth.

![The Cathode editor: the navigation bar, the preset bar, the grid with a chain of blocks, and the selected Amp's page in the drawer below](screenshots/grid.webp)

The desktop app's window opens at 1440 × 1094 pixels, which is also its minimum size, so the grid and a block's page fit without scrolling.

## The navigation bar

The top bar holds the pages on the left and the tools you need on every page on the right. On a Mac it is also the window's title bar: drag it to move the window.

### Pages

| Button          | What it opens                                                                                                                                                                              |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Editor**      | The grid and the selected block's page. Where you build sounds.                                                                                                                            |
| **Library**     | Presets, the Block Library, global blocks, NAM amps and IR cabs. See [The Library](/manual/library).                                                                                       |
| **Scenes**      | A table of every block in every scene. See [Presets, scenes and channels](/manual/presets-and-scenes#the-scenes-page).                                                                     |
| **Controllers** | The scene level and scene controllers, scene MIDI, Manual A to D, the LFOs, ADSRs, followers and sequencer, and the list of modifiers. See [Controllers and modifiers](/manual/modifiers). |
| **Perform**     | Big controls you picked for playing live. See [Playing live](/manual/live-performance#the-perform-page).                                                                                   |
| **Global**      | Settings for every preset: tempo, metronome, preset and scene behaviour, outputs, input, MIDI and footswitches.                                                                            |

One more page, **Manage presets**, opens from the **Preset** menu (see below).

### Tools

From left to right:

| Control                   | What it does                                                                                                                                                     |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Undo, Redo                | Step back and forward through your edits (⌘Z, ⇧⌘Z or ⌘Y). Every edit, from a knob turn to a moved block, is one step.                                            |
| **120 BPM**               | The current tempo. Click it to type a new one (30 to 300), then press ↵.                                                                                         |
| **TAP**                   | Tap tempo: click it in time with the music.                                                                                                                      |
| Metronome                 | Turns the click on and off. Its level is on the Global page.                                                                                                     |
| Looper                    | Opens the looper. See [The looper](/manual/live-performance#the-looper).                                                                                         |
| Tuner                     | Opens the tuner (⌘T).                                                                                                                                            |
| Analyser                  | An oscilloscope and a real-time analyser (**Scope** and **RTA**) of the output.                                                                                  |
| Preset Leveling           | Measures how loud the preset plays and suggests a level change. See [Gain staging and levels](/manual/gain-staging).                                             |
| Mute                      | Silences every output (M). It turns red while muted.                                                                                                             |
| **IN** and **OUT** meters | The input level and the output level. The caption turns into a red **CLIP** mark when the input comes within 0.5 dB of full scale, or an output reaches −1 dBFS. |
| **CPU**                   | How much of each buffer's time the engine needs (the 99.9th percentile).                                                                                         |
| **Est.**                  | An estimate of how heavy the current preset is, from its blocks' measured costs.                                                                                 |
| Status light              | Green while the engine runs. Amber while connecting, while the engine restarts or when its meters stop; red when disconnected. Hover for the details.            |
| Settings                  | Only when the editor runs in a web browser. In the desktop app, Settings is in the app menu instead.                                                             |

## The preset bar

Under the navigation bar is a second bar for the preset you are playing.

- **Compare A / B:** two versions of the preset to switch between while you tweak. Click **B** to start a second version from the current one, change it, then flick between **A** and **B** to compare. Both keep their edits until you load another preset.
- **Scene 1 to 8:** click to switch scenes, or press ⌘1 to ⌘8.
- **The preset and scene display:** two lines, each with up and down arrows.
  - The top line shows the preset's number and name. A dot after the name means unsaved changes. Click it (or press **P**) to open the preset picker. The arrows step to the next or previous preset (⌘PgDn, ⌘PgUp).
  - The bottom line shows the scene's number and name. Click the name to rename the scene. The arrows step through the eight scenes.
- **Recent presets:** the clock icon lists the last presets you loaded in this editor.
- **New preset** (+): starts an empty preset. If the working copy has unsaved changes, Cathode asks first.
- **Save** (⌘S): saves the preset. For a factory preset it asks for a name and saves your own copy.
- **Preset** menu:
  - **Name, author, tags and notes…**
  - **Save as…** (⇧⌘S)
  - **New from template…** and **Save as template…**
  - **Take a snapshot** and **Snapshots…**
  - **Revert to saved**
  - **Clear preset…**
  - **Manage presets…**

[Presets, scenes and channels](/manual/presets-and-scenes) explains each of these.

## The Editor page

The Editor page is split in two: the **grid** on top and the **drawer** below. Drag the divider between them to give either more room.

### The grid

Six rows of fourteen cells. Each block sits in a cell, and cables join them from left to right. Each block's tile shows its name in its category's colour, a small level meter along the bottom, the channel it is on (A to D) in the top-right corner, and a number in the top-left when there is more than one of its type. A bypassed block is greyed out. [The grid](/manual/the-grid) explains how to work with it.

Hold **⌥** (Alt), or press **F1** to toggle, for **Block Info**: every tile shows a short code (AMP, DRV, CAB…), its channel and a **GLOBAL** mark if it is a global block, and a line under the grid lists the amps in use with their model on each channel and the scenes that use it.

### The block page

Click a block to open its page in the drawer.

![The Amp block's page: the tile, channel and type on the left, the amp's own front panel of knobs, and its pages listed beside them](screenshots/amp.webp)

The left column holds the block's identity and state:

- The block's **tile** and its **id** (for example `amp1`). The id is how MIDI mappings and footswitches name the block.
- The **⋯** menu (**Block actions**): **Save to the Block Library…**, **Copy channel … to**, **Swap channel … with**, **Reset channel …**, **Reset block (all channels)**, and **Make global**, **Use "…"** or **Unlink from global**.
- A **Global** tag if the block is linked to a global block.
- **Engaged** / **Bypassed** and its switch. Right-click it to let a modifier engage the block (see [Engaging a block from a modifier](/manual/modifiers#engaging-a-block-from-a-modifier)).
- **Channel** A to D, and **Scene Ignore on** the current channel.
- **Type**, for blocks that have models or types (amp models, drive pedals, reverb types…).
- For the Amp: **Controls**, **Authentic** or **Ideal** (see [Preferences](#preferences)).
- **Block Library** (when the Library holds entries for this block type): recall a saved setting, with **Into all channels** to fill every channel at once.
- **Bypass mode:** what the block does when bypassed (see [Bypass modes](/manual/the-grid#bypass-modes)).

The right side holds the controls. Some blocks have a panel of their own on top (the Cab's microphones, the Parametric EQ's curve, the Amp NAM's capture, the IR Player, Tone Match, Pitch). Below it, the block's knobs, switches and choices are grouped into pages listed on the left (**Basic**, **Tone**, **Advanced**…); the page you last opened is remembered for each block type. The **Output** group on the right holds the block's level, mix, balance and pan.

Every knob, switch and choice is documented on the block's own page in this manual, for example [the Amp](/manual/blocks/circuitamp).

### Working with knobs

- **Drag** up and down to turn a knob. Hold **⇧** for fine steps.
- **Scroll** over a knob to turn it (you can reverse or turn this off in Preferences).
- With a knob focused, the **arrow keys** move it by 1 % of its travel (0.1 % with ⇧), **Page Up** and **Page Down** by 10 %, **Home** and **End** to the ends.
- **Double-click** a knob or its value to type a value.
- **⌥-click** (Alt-click) resets it to its default.
- **Right-click** a knob for **Add modifier…**, **Learn MIDI controller**, **Add to the preset's Perform page**, **Add to the global Perform page** and **Set in other presets…**. Switches and choices offer the last three.

A small dot beside a knob means a modifier drives it. While it runs, the knob turns with the value it is getting, and a mark on its track shows the value you set.

## Dialogs

| Dialog          | Opened from                                    | What it holds                                                                                                   |
| --------------- | ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Block picker    | Double-click an empty cell, ↵ or Q on the grid | Every block type, with a search field.                                                                          |
| Presets         | P, or a click on the preset name               | Search by name, author or tag, or type a bank slot number. A pin keeps it open to try one preset after another. |
| Tuner           | ⌘T, or the tuner button                        | A needle tuner, and what the outputs play while tuning (mute, bypass or pass through).                          |
| Looper          | The looper button                              | Record, play, overdub, stop, undo, reverse, half speed, clear, and **Export WAV**.                              |
| Analyser        | The analyser button                            | **Scope** shows the output's waveform; **RTA** its spectrum, with bands, decay and window settings.             |
| Preset Leveling | ⌘L, or its button                              | Loudness measurement and a suggested level.                                                                     |
| Settings        | ⌘,                                             | Three tabs: **Preferences**, **Audio setup** and **Keyboard shortcuts**.                                        |

Press **Esc** to close any dialog.

## Settings

In the desktop app, open Settings from **Cathode ▸ Settings…** on a Mac or **File ▸ Settings…** on Windows and Linux, or press **⌘,**. In a browser, use the gear button at the right of the navigation bar.

- **Preferences:** how the editor behaves for you (below).
- **Audio setup:** the interface, sample rate, buffer, routing, latency and calibration, and which server to use. See [Audio setup](/manual/audio-setup).
- **Keyboard shortcuts:** every shortcut. Press **?** to open this tab. See [Keyboard shortcuts](/manual/keyboard-shortcuts).

### Preferences

| Preference    | Choices                                                                                                                                                                                                               |
| ------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Knob mode     | **Linear**: drag up and down. **Circular**: drag around the knob, as you would turn a real one.                                                                                                                       |
| Mouse wheel   | **Normal**, **Reverse**, or **Off** (the wheel then scrolls the page).                                                                                                                                                |
| Meter refresh | **60 Hz**, **30 Hz**, **15 Hz** or **Off**. Lower rates save a little power.                                                                                                                                          |
| Amp controls  | **Authentic** shows only the controls the modelled amp really has. **Ideal** shows every control the Amp block offers, under a generic name where the amp has none. You can switch per page in the Amp's left column. |
| Tooltips      | On or off.                                                                                                                                                                                                            |

Preferences are kept on this computer, in this editor. A browser on another device has its own.

## Where things are kept

Presets, banks, block presets, IRs, captures and global settings belong to the engine's server, not to the editor:

| System          | Folder                                  |
| --------------- | --------------------------------------- |
| macOS           | `~/Library/Application Support/Cathode` |
| Windows         | `%APPDATA%\Cathode`                     |
| Linux desktop   | `~/.config/Cathode`                     |
| Headless server | `/var/lib/cathode`                      |

Back this folder up to keep everything. Preferences, the recent presets list and remembered access tokens are kept by the editor itself, per computer and browser.

## The desktop app and the browser

The desktop app starts its own engine and server when it opens and stops them when you quit. The same editor also runs in a web browser when it is served by a [headless server](/manual/headless-server). The browser version is the same app, with two differences: Settings has a button in the navigation bar, and **Audio setup** cannot choose the interface (a rig's interface is set in its settings file). Capturing a cab IR with a sweep also needs the desktop app.

The desktop app can also edit a remote rig instead of its own engine: see [Connecting the desktop app](/manual/headless-server#connect-from-the-desktop-app).
