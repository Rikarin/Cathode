This chapter takes you from nothing to playing through Cathode: download and install it, connect your guitar and your interface, choose the interface in Cathode, try a factory preset, then build and save a simple rig of your own. Allow ten to twenty minutes.

## Before you start

You need:

- a Mac with Apple Silicon, a Windows 10 or 11 PC, or a Linux PC (x64);
- an audio interface with an instrument input, connected to the computer and switched on;
- headphones or speakers plugged into the interface (not into the computer's own headphone socket);
- a guitar and a cable.

> **Warning:** Turn your speakers or headphones down before you first play. A high-gain preset at full volume is loud, and a wrong setting can cause a burst of feedback.

## 1. Download Cathode

Get the latest build from the [Releases page](https://github.com/Rikarin/cathode/releases). Pick the file for your computer:

| File                                                   | For                                                                                                       |
| ------------------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| `Cathode-<version>-mac-arm64.dmg`                      | macOS on Apple Silicon (M1 and later). A `.zip` of the same app is there too.                             |
| `Cathode-<version>-win-x64.exe`                        | Windows 10 and 11                                                                                         |
| `Cathode-<version>-linux-x86_64.AppImage`              | Linux desktops (x64)                                                                                      |
| `cathode-server-<version>-linux-<x64 or arm64>.tar.gz` | The headless server, for a computer without a screen. See [The headless server](/manual/headless-server). |

Each release also lists a `SHA256SUMS` file. If you want to check that your download is complete and unchanged, compare the file's SHA-256 checksum with the line for it there.

## 2. Install it

### macOS

1. Open the `.dmg` and drag **Cathode** into **Applications**.
2. Open Cathode from Applications.

Cathode's Mac builds are not notarised by Apple yet, so the first time you open it macOS refuses with a message that it cannot check the app. To allow it:

1. Try right-clicking (or Control-clicking) **Cathode** in Applications and choosing **Open**, then **Open** again in the dialog.
2. If macOS still refuses (recent macOS versions no longer offer Open there), open **System Settings**, go to **Privacy & Security**, scroll down to the message about Cathode and click **Open Anyway**. Confirm with your password if asked.

You only need to do this once. If macOS asks whether Cathode may use the microphone, allow it: that is how macOS asks for access to your interface's inputs, and without it Cathode hears nothing.

### Windows

1. Run `Cathode-<version>-win-x64.exe`.
2. The installer is not signed yet, so Windows SmartScreen may warn that it does not recognise the app. Click **More info**, then **Run anyway**.
3. Follow the installer. You can choose the folder it installs to.
4. Start Cathode from the Start menu.

### Linux

1. Make the AppImage executable: `chmod +x Cathode-<version>-linux-x86_64.AppImage`, or tick "Allow executing file as program" in your file manager's properties.
2. Run it.

## 3. Connect your guitar and speakers

1. Plug your guitar into the interface's **first input**, the one marked as an instrument input (_Hi-Z_, _Inst_ or a guitar symbol). If the input has a Hi-Z or Inst switch, turn it on.
2. Plug headphones into the interface's headphone socket, or monitors into its main outputs 1 and 2.
3. Set the interface's input gain knob low to start with, and the output volume low.
4. If your interface has a "direct monitoring" knob or switch, turn it off or fully towards the computer side. Otherwise you will hear your dry guitar mixed with Cathode's sound.

## 4. Open Cathode and choose your interface

When Cathode opens it shows a spinner while it starts its engine, then the editor. The first time, the engine runs with **no audio device**, so you will not hear anything yet.

1. Open **Settings**: press **⌘,** (Ctrl+, on Windows and Linux), or choose **Cathode ▸ Settings…** on a Mac, **File ▸ Settings…** on Windows and Linux.
2. Click the **Audio setup** tab.
3. Under **Audio interface**, open **Device** and pick your interface. Each one shows its number of inputs and outputs.
4. Set **Sample rate** to **48 k** and **Buffer** to **64**.
5. Click **Apply and restart engine**. The engine restarts on your interface within a few seconds.
6. Look at the **Engine** figures at the top of the tab. **Backend** should name your system's audio layer (not "null" or "disconnected"), and the sample rate and buffer should match what you chose.

![Settings, Audio setup: the engine's figures and latency, the input calibration, and the interface, sample rate and buffer](screenshots/setup.webp)

> **Tip:** If you hear crackles or pops later, come back here and choose a larger buffer (128 or 256). [Audio setup](/manual/audio-setup) explains buffer size and latency in detail.

Close Settings with the **×**, or press **Esc**.

## 5. Set your input level

The top bar of the editor has two small meters labelled **IN** and **OUT**.

1. Play your guitar hard, with your loudest pickup and strumming as hard as you ever will.
2. Watch the **IN** meter. Raise the interface's input gain until the meter moves well, but never reaches the end.
3. If the **IN** label turns into a red **CLIP** mark, the input is overloading. Turn the interface gain down until it stays away.

You should now hear your guitar, clean and dry: a new preset passes the input straight to the output.

> **Note:** Cathode's amp models react to how many volts your guitar actually puts out, so they need to know how loud your interface's input is. For now the default works. When you have time, calibrate it as described in [Input level and calibration](/manual/audio-setup#input-level-and-calibration): it makes the amps respond to your pick the way the real ones would.

## 6. Play a factory preset

Cathode comes with a bank of 70 factory presets, from blues and classic rock to modern metal, jazz and ambient.

1. Press **P** to open the preset picker (or click the preset name in the bar under the navigation bar).
2. Type part of a name, a style or a tag, for example `blues` or `metal`.
3. Use **↑** and **↓** to move through the list and **↵** (Return) to load one.
4. Play. Turn your volume up slowly to a comfortable level.

Each factory preset has several **scenes**, variations such as a rhythm and a lead sound. Click the numbers **1** to **8** next to **Scene** in the bar, or press **⌘1** to **⌘8**, to switch between them while you play.

> **Note:** A factory preset with a wah or a whammy rests at the heel of the pedal until you connect an expression pedal and map it to External 1. See [Expression pedals](/manual/midi-and-footswitches#expression-pedals).

You can also browse the presets in the **Library**: click **Library** in the navigation bar, choose **Factory** at the top, and double-click a preset to load it.

## 7. Build your first preset

Now make a simple rig of your own: an amp, a cabinet and a little reverb.

1. Click the **+** (**New preset**) button at the right of the preset bar. The grid shows a fresh preset: an **Input** block at the left of the third row, an **Output** block at the right, and a dashed line of **shunts** (empty connectors) joining them.
2. Double-click the cell just right of the Input, on that line. The block picker opens.
3. Type `amp`, make sure **Amp** is highlighted, and press **↵**. The Amp block takes the place of the shunt, already connected.
4. Double-click the next cell to the right, type `cab`, and press **↵** to add a **Cab** block.
5. Double-click the next cell, type `reverb`, and press **↵**.

![The grid with Input, Drive, Amp, Cab and Reverb in a row, and the Amp's page open in the drawer below](screenshots/grid.webp)

Now shape the sound:

1. Click the **Amp** block. Its page opens in the drawer below the grid.
2. In the left column, open **Type** and choose an amp model. Each model shows its own front panel of knobs.
3. Turn the knobs: drag up and down on a knob, or scroll over it. Double-click a knob to type a value. ⌥-click (Alt-click) a knob to return it to its default.
4. Click the **Cab** block and try a few speakers and microphone positions.
5. Click the **Reverb** and turn its **Mix** down until it sits behind the guitar.

> **Tip:** If the sound is fizzy and harsh, check that the Cab block is engaged. An amp without a cab, played through full-range speakers, sounds like a broken speaker. If it is buzzy at rest with high gain, add a gate: see [the Input block's gate](/manual/blocks/input).

## 8. Save it

1. Click the **Preset** menu at the right of the preset bar and choose **Save as…** (or press **⇧⌘S**).
2. Type a name and click **Save**.

From then on, **⌘S** (or the save button) saves your changes to the same preset. A dot after the preset's name means it has unsaved changes.

> **Note:** **⌘S** on a brand-new preset asks for a name too, the first time: it opens the same dialog as **Save as…**.

If you change a factory preset, saving it never overwrites the original: Cathode asks for a name and saves your own copy.

## Where to go next

- [Audio setup](/manual/audio-setup): latency, calibration, and what to plug Cathode into.
- [A tour of the editor](/manual/the-editor): what every button does.
- [The grid](/manual/the-grid): parallel paths, moving blocks and cables.
- [Presets, scenes and channels](/manual/presets-and-scenes): building a preset you can play a whole song with.
- [Getting your sound](/manual/getting-your-sound): a method for dialling in a tone.
