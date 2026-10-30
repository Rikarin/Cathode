Gain staging means setting the level at each point of the chain so that every block gets the signal it expects: enough to work as designed, not so much that something clips by accident. In a real rig the amp's input decides how hard the preamp tubes are driven, so level is part of the tone, not just loudness. Cathode models amps and drives by voltage, so the same is true here.

This chapter follows the signal from the guitar to the speakers: the interface input, Input 1's calibration, the blocks in between, the outputs, and finally how to make all your presets equally loud.

## How level flows through Cathode

1. Your **audio interface** turns the guitar's voltage into numbers. Its gain knob sets how many volts reach full scale.
2. **Input 1's calibration** tells Cathode what that full scale is in volts, so the amp and drive models see the voltage a real guitar jack would see.
3. **Input 1 Gain** on the Global page trims what every preset receives.
4. The grid's **Input** block has its own **Input Level** for this preset, and its noise gate.
5. Each **block** has a **Level** (and many a **Mix**). Drives and amps react to how much they get; most other effects do not care.
6. The **Output** block's **Main Level** and row levels, then each output's **Boost/Pad** and global EQ on the Global page, set what reaches your interface's outputs.

Cathode passes audio between blocks as floating-point numbers, so a hot signal between two blocks does not clip on its own. What it does is drive the next block harder, which matters a great deal for drives, amps and compressors. The places where clipping really happens are the interface's input and the outputs.

## The interface input

Set your interface's input gain first, before any preset work:

- Plug in the guitar you play most, on its hottest pickup, volume up.
- Play as hard as you ever do: big open chords, hard pick attack.
- Raise the interface gain until the **IN** meter in the top bar is healthy, and stop well before its caption turns into a red **CLIP** mark. The mark lights when the input comes within 0.5 dB of full scale.

Leave headroom: a digital input that clips sounds like nothing an amp does. A few decibels below full scale on your hardest playing is plenty; the amp models do not need a hot input, because the calibration below gives them the right voltage anyway.

![The Audio setup page: the engine and latency readings, Input 1's calibration and the audio interface](screenshots/setup.webp)

## Calibrate Input 1

Open **Settings ▸ Audio setup**. Under **Input 1**, Cathode shows its full scale in volts and in dBu. The Circuit Amp and the Drive block scale their input by this figure, so a model of a 1960s amp sees the same voltage your pickups would put into the real one. With the default of 1 V, they read the input as if full scale were 1 V.

You can calibrate in two ways:

- **From the data sheet.** Find your interface's maximum input level for its instrument (Hi-Z) input at the gain you use, enter it in **Interface maximum input level** (in dBu, or in V peak), and press **Use this level**. Many interfaces state this only at minimum gain; in that case calibrate at minimum gain, or measure.
- **By measurement.** Feed a steady sine of known level (from a signal generator, or a DI box checked with a multimeter) into Input 1, enter its level in volts RMS, and press **Measure for 2 s**.

> **Warning:** If you change the interface's gain knob after calibrating, the calibration is wrong: every amp gets more (or less) drive than it should. Recalibrate after any change, or mark the knob's position.

> **Note:** The **Amp NAM** block does not use the calibration. A neural capture reacts to the level it gets, so set its own **Input** knob (see [Neural captures and impulse responses](/manual/nam-and-irs#input-and-output-levels)).

## Input 1 Gain, and the Input block

- **Input 1 Gain** (Global page, -20 to +20 dB) trims what every preset gets, after the DI out. Use it for a guitar with very hot or very weak pickups, so you do not have to touch every preset.
- The grid's **Input** block has an **Input Level** for this preset only, and the noise gate (**Gate Type**: Off, Classic, Intelligent or Noise Reducer). The Factory bank's driven presets use the Intelligent gate, which ignores mains hum when it decides whether you are playing. See [Building a signal chain](/manual/signal-chain#the-noise-gate).
- The Input block can also model your pickup's loading by the input impedance and the cable (**Input Impedance**, **Pickup**, **Cable Capacitance**). These change the top end the way a real cable and amp input would; leave them off if you are unsure.

## How hard to drive each block

### Drives

A Drive block's **Level** is in decibels, 0 dB by default; turning it up pushes the amp harder, exactly like a pedal's volume knob. For a **boost** (a drive in front of a distorted amp), the usual setting is **Drive** low, **Level** up a few dB, and the **Tone** a little above noon. For a pedal that provides the distortion itself into a clean amp, set **Level** so the pedal on and off are about equally loud.

### The amp's input

The amp's **Gain** (or **Volume**, **Drive**, **Preamp** and so on, depending on the model) sets the preamp's drive. The **Input Trim** on the Preamp page changes the level at the amp's input jack without changing its tone, as a pedal's level knob would; use it to make one amp react as though you had hotter or weaker pickups. The amp's built-in **Boost Type** on the Preamp page is a drive pedal in front of the input, with its own **Boost Drive**, **Boost Tone** and **Boost Level**.

### The amp's output

On a master-volume amp the **Master** sets how hard the power section is driven, which changes the tone; the amp's **Level** then sets the loudness. Level does not change the tone. Always balance loudness with **Level**, not with **Master** or **Gain**.

Every amp has its own **Level** on each channel, so a crunch and a clean channel can be balanced against each other.

### Compressors

The Compressor's **Input Level** switch tells it what to expect: **Instrument** in front of the amp, **Line** after the amp or cab. Its threshold then sits in a sensible place for that signal.

### Effects after the amp

Delay, reverb, modulation and EQ do not distort, so their input level only matters for their output. Keep their **Level** near 0 dB and set the effect with **Mix**.

### The Cab's mic preamp

The Cab block's **Preamp** page models a mic preamp, with **Preamp Drive**. While a Preamp Type is chosen, the **Preamp in** meter above the mic slots shows its input, with 0 dB where that preamp starts to clip.

## Meters and clipping

- **The top bar** has an **IN** meter for Input 1 and an **OUT** meter for Output 1. Each caption turns into a red **CLIP** mark and holds for a moment when the input comes within 0.5 dB of full scale, or when an output reaches -1 dBFS. Hover the mark to see which output.
- **Every block tile on the grid** shows its output peak along its bottom edge, so you can see where a level jumps. When a block's output reaches 0 dB, a red dot lights in the tile's corner and stays until you click it; **⌘K** resets every clip light at once, the blocks' and the meters' in the top bar.
- **The Cab block** shows the mic preamp's input and Motor Drive's gain reduction.
- **The Compressor, Multiband Compressor and Gate/Expander** show their gain reduction on their pages: how many dB they are taking off right now (one meter per band on the Multiband Compressor), with the deepest reading held for a second. A bypassed block reads 0.
- **The Analyser** (the chart icon in the top bar) has an oscilloscope and an RTA (a real-time spectrum display) of the output.

If the **OUT** clip mark lights, lower the preset's level (the amp's **Level** or the Output block's **Main Level**) or the output's **Boost/Pad**. Clean sounds clip the outputs more easily than distorted ones, because a clean amp passes on the full peak of every pick attack. That is why each Factory preset with a clean scene has a compressor after the cab that only catches those peaks.

## Outputs: Boost/Pad and global EQ

On the **Global** page, each of Outputs 1 to 4 has a **Mode**, a **Boost/Pad** (-18 to +18 dB) and a ten-band global EQ. These apply to every preset on that output. Use them for the playback system, not for the tone:

- **Boost/Pad** matches Cathode's output to what follows: pad it down for a hot mixer input, boost it for a quiet power amp.
- **Global EQ** corrects for a room or a speaker: a bit less low end in a boomy room, a touch less treble on a bright PA.

## Matching preset loudness

Presets that jump in volume when you switch are a gig problem. Cathode has a loudness meter for this: **Preset Leveling**, the equaliser icon in the top bar (⌘L, or Ctrl+L).

It measures loudness as broadcast meters do (ITU-R BS.1770, in LUFS: loudness units relative to full scale), weighting frequencies roughly as your ears hear them, rather than reading peaks. Two presets at the same LUFS sound about equally loud even when one is a clean chime and the other a wall of distortion.

To use it:

1. Load the preset and open **Preset Leveling**.
2. Pick a **Target**: -23, -18 or -14 LUFS. The Factory bank is levelled to -18 LUFS, so use -18 to sit alongside it.
3. Press **Measure** and play the preset as you would on stage, for at least the length of a verse. Pauses between phrases are ignored.
4. Press **Stop**. Cathode shows the measured loudness and the change it suggests.
5. Press **Apply**.

<!-- screenshot wanted: the Preset Leveling dialog after a measurement, with Momentary, Short-term and Measured readings, the target set to -18 and the suggested amp Level change -->

The change goes to the **Level** of the preset's amp (the first Amp or Amp NAM block) on the channel in use, or to the Output block's **Main Level** if the preset has no amp. Level each amp channel by playing a scene that uses it.

> **Tip:** For a solo, make the lead scene a couple of decibels louder than the rhythm: raise its **Scene Level** on the Controllers page. Many Factory presets do exactly that.

## Headroom

Headroom is the space between your loudest peak and the point of clipping. You need it in two places:

- **At the input:** hard playing must never reach full scale. Calibration makes this free, because the amp's drive does not depend on how hot the digital signal is.
- **At the outputs:** keep your loudest scene's peaks below -1 dBFS, so the **OUT** clip mark never lights. If you level presets to -18 LUFS, most distorted sounds peak far below that; very dynamic clean sounds are the ones to watch.

Turn the volume up on your monitors, amplifier or mixer, not inside Cathode. Running everything inside the box hot and the speakers low gains nothing and risks clipping.
