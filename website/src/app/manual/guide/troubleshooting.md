Most problems have a simple cause: a setting in the wrong place, a cable, or a level. Find your symptom below and work through the checks in order.

## Installing and starting

### macOS says Cathode cannot be opened

The Mac builds are not notarised by Apple yet. Right-click Cathode in Applications and choose **Open**. On recent macOS versions, open **System Settings ▸ Privacy & Security** instead, find the message about Cathode near the bottom, and click **Open Anyway**. You only do this once. See [Install it](/manual/getting-started#2-install-it).

### Windows SmartScreen blocks the installer

The installer is not signed yet. Click **More info**, then **Run anyway**.

### The Linux AppImage does not start

- Make it executable: `chmod +x Cathode-<version>-linux-x86_64.AppImage`.
- Run it from a terminal to see any error. AppImages need FUSE; some recent distributions do not install the version they need by default, and the error message names what is missing.

### "Cannot reach the Cathode server"

The editor could not talk to the engine's server.

- Click **Try again**. The engine can take a few seconds to start, longer on the first run.
- **Desktop app:** quit Cathode completely and open it again.
- **Connected to a remote rig:** check that the rig is on and on the same network, and that the address is right (see [below](#the-browser-cannot-reach-the-rig)).

If the desktop app is set to a remote rig that does not answer within about ten seconds, the screen names the rig's address and offers a way out: **Use this computer** goes back to the computer's own engine, and the **Server** section below it changes the rig's address or access token (**Connect**). A rig that refuses the access token says so at once.

### The window is bigger than my screen

The desktop window's minimum size is 1440 × 1094 pixels, so on smaller displays part of it is off the screen. Use a larger external display, or choose a higher-resolution display setting ("More Space" on a Mac, a lower scaling percentage on Windows).

## No sound

Work through these in order:

1. **Is the engine running?** The status light at the right of the navigation bar is green only while it is. Amber means it is restarting or has stopped sending meters; in the desktop app, **Settings ▸ Audio setup ▸ Show engine log** shows why.
2. **Is the engine on your interface?** Open **Settings ▸ Audio setup**. **Backend** must name an audio system, not "null" or "disconnected". If it says null, pick your interface under **Device** and click **Apply and restart engine**. The first time you run Cathode, no device is chosen.
3. **Is Cathode muted?** The mute button in the navigation bar is red when muted. Press **M**.
4. **Does the IN meter move when you play?** If not: the guitar is in the wrong input (Input 1 reads the interface's channel 1 unless you changed the **Input map**), the cable is faulty, or on a Mac Cathode is not allowed to use the microphone (**System Settings ▸ Privacy & Security ▸ Microphone**).
5. **Does the OUT meter move?** If not, the sound stops somewhere on the grid:
   - Is there an unbroken line of cables from the **Input** to the **Output**? A block that is not cabled in is not heard.
   - Is a block bypassed with the **Mute** bypass mode, or a Multiplexer set to a row with nothing on it?
   - Is a Volume/Pan, a block's Level or the **Scene Level** (Controllers page) turned right down?
6. **The OUT meter moves but you hear nothing:** check that your speakers are on the interface's outputs 1 and 2 (or whichever channels **Outputs 1-4** in Audio setup names), and the interface's own output volume. Check **Boost/Pad** for that output on the Global page.
7. **Only some presets are silent:** a preset that has a wah or volume pedal block driven by an expression pedal may rest at silence or at the heel until the pedal is mapped. See [Expression pedals](/manual/midi-and-footswitches#expression-pedals).

### I hear my dry guitar under the processed sound

Your interface's **direct monitoring** is on. Turn it off, or turn its mix knob fully towards the computer.

### Other apps go silent while Cathode runs (Windows)

Cathode opens the interface in exclusive mode when it can. Set the environment variable `CTH_WASAPI_SHARED=1` before starting Cathode to use shared mode instead, at a higher latency.

## Crackles, pops and dropouts

The computer is not finishing each buffer in time.

1. Open **Settings ▸ Audio setup** and watch **Engine overruns** and **Driver overloads** while you play.
2. Raise the **Buffer** one step (64 to 128, then 256) and click **Apply and restart engine**.
3. Check **Est.** and **CPU** in the navigation bar. If one preset is near 100 %, lighten it: fewer amps, NAM captures, pitch shifters or long reverbs, or one of each instead of two. The Marshall AFD100 and Silver Jubilee amp models are known to need more than the budget at high gain.
4. Use **48 k** rather than 88.2 or 96 k.
5. Close other programs, especially other audio software and browsers playing video. Keep a laptop on mains power and in a high-performance power mode.
6. Connect the interface directly to the computer, not through a USB hub.

## Too much latency

What you feel is the round trip: interface converters, two buffers, and any latency in the preset.

- Lower the **Buffer** to 64 or 32, as far as it runs without dropouts.
- Check the **Latency** report in Audio setup. **Preset** latency names the blocks that add delay. **Global ▸ Oversampling ▸ Linear phase** adds about 2 ms; **Minimum phase** adds none.
- Measure the real round trip with **Measure latency…** (see [Measuring the real latency](/manual/audio-setup#measuring-the-real-latency)). A large figure at small buffers comes from the interface's driver and USB path; a different interface is the only cure.
- On Windows, pick the interface's ASIO driver if it has one (see [ASIO on Windows](/manual/audio-setup#asio-on-windows)); WASAPI's shared mode adds latency.

## Clipping

**IN shows CLIP:** the interface's input is overloading. Turn its gain knob down. If you calibrated the input, calibrate again afterwards (see [Input level and calibration](/manual/audio-setup#input-level-and-calibration)).

**OUT shows CLIP:** an output reached −1 dBFS. Hover the mark to see which. Lower the preset's level (the amp's Level, or the Output block's Main Level), the Scene Level, or that output's Boost/Pad on the Global page. See [Gain staging and levels](/manual/gain-staging).

## Hum, buzz and noise

- **Noise when you stop playing with a high-gain sound:** add or tighten a gate. The [Input block](/manual/blocks/input) has a noise gate, and there is a separate [Gate/Expander](/manual/blocks/gate) block. Set the Input block's [Mains Frequency](/manual/blocks/input#knob-mains) to your country's mains (50 Hz in Europe and most of the world, 60 Hz in North America).
- **A low hum that is there even with the guitar's volume down:** often a ground loop. Plug the computer, the interface and any amplification into the same power outlet or strip, try a different USB port, and use balanced cables to powered speakers and the PA.
- **Buzz that changes when you turn:** single-coil pickups pick up interference from screens and power supplies. Face away from the computer, or move away from it.

## The sound is wrong

**Fizzy, harsh, like a broken speaker.** Usually an amp without a cab. Check that a Cab block (or IR Player) is in the chain after the amp and engaged, and that **Global ▸ Cab modeling off** is off when you listen through headphones, monitors or a PA.

**Muffled and dark through a guitar cabinet.** The opposite: Cab simulation on top of a real cabinet. Turn **Cab modeling off** on.

**Far too much or too little gain compared with the real amp.** The input calibration does not match your interface. See [Why calibrate](/manual/audio-setup#why-calibrate).

**Thin, muddy, boomy, or lost in the band.** See [Getting your sound](/manual/getting-your-sound), [Choosing and dialling an amp](/manual/choosing-an-amp) and [Cabs, speakers and microphones](/manual/cabs-and-mics).

**Stereo effects sound narrow.** Look for an amber **MONO** tag on the grid: a block after them sums to mono. Move the stereo effects after it. Also check that the output's **Mode** on the Global page is **Stereo**.

**The volume jumps between presets.** Level them with **Preset Leveling** (⌘L). See [Matching levels between presets](/manual/live-performance#matching-levels-between-presets).

**A scene change cuts my delay or reverb tail.** Set that block's **Bypass mode** to **Mute FX In (spillover)**. See [Bypass modes](/manual/the-grid#bypass-modes).

**A knob will not stay where I put it.** A modifier drives it: a dot beside the knob shows this. Right-click it and choose **Edit modifier…** or **Remove modifier**.

**A block will not stay bypassed.** A modifier may engage it: a note under its **Engaged** switch says so. Or **Scene Ignore** is on for its channel.

## Presets and the Library

**I can't save over a factory preset.** That is by design: Cathode saves your own copy under a new name.

**A preset is called "(untitled)".** Its name is empty. Give it one in **Preset ▸ Name, author, tags and notes…** and save again.

**I can't delete a preset.** It is in a bank or a setlist: clicking **Delete** lists which ones, and which slots. Remove it from the bank on the Manage presets page first. Factory presets cannot be deleted.

**I lost my changes.** Try **Undo** (⌘Z) first. If you took a snapshot (**Preset ▸ Snapshots…**), load it.

**An imported preset is refused.** It was saved by a newer version of Cathode. Update Cathode.

## MIDI does nothing

1. Open **Global** and watch the **Monitor** in the MIDI section while you press a switch. If no line appears, Cathode receives nothing: check the cable, the controller's power, and that the controller sends on its USB or MIDI port (some send only to one of them).
2. If lines appear but nothing happens: check **Receive channel**. If it is set to one channel, messages on other channels are ignored.
3. Check that the message in the Monitor matches a mapping in the table, or a switch's **Sends** column in the active footswitch layout, and that the switch is on the active **view**.
4. A CC or note counts as pressed only at a value or velocity of 64 or more. Set your controller's switches to send 127 when pressed.

### A footswitch action field will not accept my text

The field only accepts known action names, written as in [Writing actions](/manual/midi-and-footswitches#writing-actions). Scene, channel, manual and view numbers start at 1, as Externals do: `scene 1` is Scene 1. A number outside the range is clamped to it.

### Program changes load the wrong preset

`presetSlot` loads slot 1 for PC 0. If your controller numbers programs from 1, its "program 1" is PC 0 and loads slot 1, which is usually what you want; if everything is one slot off, check how your controller counts.

### The wah or whammy in a factory preset does nothing

They follow **External 1**. Map your expression pedal to it: action `external`, value `1`. See [Expression pedals](/manual/midi-and-footswitches#expression-pedals).

## The tuner shows nothing

The tuner listens to the interface's Input 1 channel. Check the **IN** meter moves, play one string at a time, and let the note ring.

## The headless rig

### The browser cannot reach the rig

- Use the address the installer printed, including the port: `http://<address>:5180/`. Check the rig's address with `hostname -I` on the rig.
- Make sure your phone or laptop is on the same network as the rig.
- Check the services are running: `systemctl status cathode-host cathode-server`.
- If the rig runs a firewall, allow port 5180.

### It keeps asking for the access token

The token was typed wrong, or it changed. Read it on the rig with `sudo grep Cathode__Token /etc/cathode/cathode.env`.

### The rig makes no sound

`CATHODE_DEVICE` is empty or wrong, so the engine runs on the null backend. See [Choose the audio interface](/manual/headless-server#choose-the-audio-interface), and check the log with `journalctl -u cathode-host -f`.

## Still stuck?

Note what you did, what you expected and what happened, your system and interface, and Cathode's version, and open an issue on the [project's GitHub page](https://github.com/Rikarin/cathode/issues).
