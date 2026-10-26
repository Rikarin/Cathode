Cathode's own amps and cabs are modelled from circuits and data sheets. You can also bring in sounds measured from real gear: neural captures of amps and pedals (NAM and AIDA-X models), and impulse responses of cabinets, rooms and acoustic bodies. And you can measure your own cabinet with a sine sweep.

This chapter covers loading and levelling captures, finding them, loading and mixing IRs, and capturing an IR.

## Neural captures: NAM and AIDA-X

A neural capture is a small neural network trained to reproduce one piece of gear at one setting: someone plays test signals through the real amp or pedal, records what comes out, and trains a model to match. The result can be very close to the real thing at that setting.

Cathode plays two formats in the **Amp NAM** block:

- **NAM** (Neural Amp Modeler) models, `.nam` files.
- **AIDA-X** models, `.aidax` or `.json` files.

### What a capture can and cannot do

A capture is a snapshot. It reproduces the gear with every knob where it was when it was captured, so:

- There is no real gain knob. The **Input** knob changes how hard you drive the capture, which changes the gain somewhat, but it is not the amp's own gain control.
- The tone controls on the block (**Bass**, **Middle**, **Treble**, **Presence**) are an EQ after the capture, not the amp's tone stack: a shelf at 120 Hz, a peak at 650 Hz, a shelf at 2.5 kHz and a peak at 5 kHz.
- For another setting of the same amp, load another capture. Captures are usually sold or shared in packs covering several settings.

If you want an amp whose knobs interact as on the real thing, use the Circuit Amp. If you want the exact sound of a particular amp at a particular setting, a good capture is hard to beat. Both work with the same cabs and effects.

### Loading a capture

1. Open the **Library** and its **NAM amps** tab, press **Upload…** and choose your `.nam`, `.aidax` or `.json` files. (You can also pick a file straight from the block.)
2. Add an **Amp NAM** block where the amp goes on the grid.
3. Select it, and choose the model in its **Capture** picker. A tag shows whether it is a **NAM** or an **AIDA-X** model.

<!-- screenshot wanted: the Amp NAM block with a capture loaded, its NAM tag, and the Input, Output, Normalize and EQ knobs -->

![The Library: presets, and tabs for the Block Library, global blocks, NAM amps and IR cabs](screenshots/library.webp)

A preset can hold up to two Amp NAM blocks. The block is mono: it takes the left input and sends the result to both sides.

### Is it amp-only or a full rig?

Captures come in two kinds, and the file name or the pack's description usually says which:

- **Amp only** (also "direct out", "DI" or "no cab"): the amp's output before any speaker. Put a **Cab** block after it, as you would after a Circuit Amp.
- **Full rig** (also "with cab", "mic'd"): the amp, cabinet and microphone together. Do not add a Cab: you would hear two cabinets.

Pedal captures (an overdrive, a boost) go in front of an amp, like a Drive block.

### Input and output levels

A capture reacts to the level it gets, like the real amp. Two things set that level:

- Your interface's input gain.
- The block's **Input** knob (-24 to +24 dB).

The Amp NAM block does **not** use Input 1's calibration (which the Circuit Amp and Drive blocks use), and Cathode does not read the input level that some capture files record. So match the drive by ear:

1. Start with **Input** at 0 dB.
2. Play hard. If the capture is much cleaner or much dirtier than you expect from its description, move **Input** in steps of 3 dB.
3. When it reacts the way the capture's maker describes (the clean ones clean, the crunch ones breaking up when you dig in), stop.

On the way out, **Normalize** (on by default) brings models that record their loudness to a common level, so switching captures does not jump in volume. Captures without that information play at their own level. **Output** then sets the block's loudness; use it, or [Preset Leveling](/manual/gain-staging#matching-preset-loudness), to match your other presets.

> **Tip:** When you compare a capture with a Circuit Amp model of the same amp, level them first. The louder one always sounds better.

### Where to find captures

Players share and sell NAM captures in large numbers. TONE3000 (formerly ToneHunt) is the largest free library of NAM captures; many commercial capture makers sell packs of their own. AIDA-X models are shared by the AIDA DSP project and its users.

When you choose, check:

- **Amp only or full rig**, as above.
- **The settings** the capture was made at: a capture of a cranked amp will not clean up much.
- **The licence**: some captures are free to use, some only for personal use.

## Impulse responses

An impulse response (IR) describes how a speaker, cabinet, microphone and room colour a sound, captured by measuring them with a test signal. Cathode plays IRs in two blocks:

- The **Cab** block, for cabinet IRs, in any of its four mic slots.
- The **IR Player** block, for anything else: an acoustic guitar body, a room, a mic correction, an effect captured as an IR.

### Importing IRs

Open the **Library**, go to **IR cabs**, and press **Upload…** to add `.wav` IRs and `.cabgrid` IR packs.

Before uploading, **IR tools ▸ Import settings…** chooses how `.wav` files are processed:

- **Processing**: **As is**; **Auto-trim (remove leading silence)**, which removes the silence before the IR starts; or **Minimum phase**, which keeps the IR's frequency response but moves its energy as early as possible, so IRs line up without any delay.
- **Stereo files**: keep **Both** channels, or take **Left**, **Right** or **Sum (L+R)**.
- **Max length**: cut long IRs on import (0 keeps them whole).
- **Correct polarity**: makes each IR's main peak positive, so blended IRs do not cancel. Leave it on.

The Cab block resamples IRs to the engine's rate, normalises their loudness and keeps up to about 1.37 seconds.

### Using an IR in the Cab block

1. Add a **Cab** block after the amp.
2. Pick a mic slot (**Mic 1** to **Mic 4**) and set its **Source** to **IR**.
3. Pick the IR from the list that appears.

The slot keeps all its mixer controls: **Level**, **Pan**, **Delay**, **Invert**, its own low and high cut, **IR Length**, **Proximity** and **Smoothing**. The cab's response plot shows the IR's frequency response.

- **IR Length** trims the IR. Cabinet IRs rarely need more than 100 to 200 ms; shorter saves nothing in sound, and trimming a roomy IR removes its room.
- **Proximity** adds a mic's close-up bass boost; **Smoothing** (up to an octave) irons out a spiky response.

### IR packs (.cabgrid)

An IR pack is one speaker and mic captured at a grid of positions (cap to edge) and distances. Set a slot's **Source** to **IR Pack** and pick the pack: you can then move the mic across the speaker and away from it as with a DynaCab, and Cathode interpolates smoothly between the captured points.

A `.cabgrid` is an archive with a `manifest.json` that lists every IR with its `position` (0 at the cap's centre to 100 at the edge, in percent) and `distance` (inches from the grille). The points must form a full grid: every position at every distance. A single `.wav` works as a one-point pack.

### Mixing IRs

Two ways:

- **In the Cab block**: put IRs (or an IR and a DynaCab) in several slots. Their levels set the blend. See [Mixing microphones](/manual/cabs-and-mics#mixing-microphones).
- **As a new file**: **IR tools ▸ Mix IRs…** in the Library blends up to eight IRs, each with a **Level (dB)**, a **Delay (ms)** and **Invert**, into a new IR. **Align peaks** lines up each IR's main peak before its delay applies, so the blend starts in phase; **Normalise** evens out the result's level; **Length (ms)** sets how long it is. The mix is saved to the library as a 48 kHz IR.

The Cab block can also turn its current mix (DynaCab slots, IRs, cuts and all) into an IR file: **Export IR** downloads it, **Save as IR** adds it to the library. Use that to freeze a complex cab into one slot.

### The IR Player

The **IR Player** block plays two IRs that are not a guitar cab. Unlike the Cab, it keeps each file's own gain.

- **Mode**: **Parallel** sums the two IRs, **Stereo** plays IR 1 on the left and IR 2 on the right, **Series** plays one after the other.
- **Mix** blends the IRs with the dry signal; **Low Cut** and **High Cut** (on its EQ page) shape the wet signal.
- Each IR has its own **Length**, **Level** and **Pan**.

Use it for an acoustic-guitar body IR on a piezo or magnetic pickup, a room or hall IR as a reverb, or a correction IR for a speaker.

> **Note:** **Cab modeling off** on the Global page makes every Cab and IR Player block pass the signal through. Turn it off again if your IRs seem to do nothing.

## Capturing an IR with a sine sweep

You can measure your own cabinet (or room, or speaker) and use it as an IR. Cathode plays a sine sweep through the speaker, records it with your microphone, and works out the impulse response from the recording.

This needs the **desktop app** with a real audio interface: the capture uses the interface directly, so it is not available from a browser connected to a headless rig.

### What you need

- An audio interface with at least one input and two outputs.
- A way to play the sweep through the cabinet: a power amp (a solid-state one is best, because it adds the least colour) driving the cab, or a powered speaker.
- A microphone on a stand in front of the cabinet, into the interface's **Input 1**, with a mic preamp (phantom power for a condenser).
- A quiet room, and hearing protection or a careful hand on the volume: the sweep is loud.

### Steps

1. Cable the interface's **outputs 1 and 2** to the power amp or powered speaker, and the mic to **input 1**.
2. Place the mic where you want it: the same choices as in [Placing the microphone](/manual/cabs-and-mics#placing-the-microphone).
3. Turn the power amp or speaker **down**.
4. In the **Library**, go to **IR cabs**, open **IR tools** and choose **Capture an IR…**.
5. Set the sweep:
   - **Sweep (s)**: 1 to 30 seconds; 5 is the default. Longer sweeps capture less noise.
   - **Level (dBFS)**: the sweep's level, -18 by default.
   - **IR length (ms)**: how much of the response to keep, 500 by default. 200 ms is plenty for a close-miked cab; keep more for a roomy IR.
6. Press **Capture**. Cathode stops the engine while it measures (you hear nothing else), plays the sweep and records it, then starts the engine again.

<!-- screenshot wanted: the Capture an IR dialog with its sweep settings and a successful report -->

7. Read the report. It shows the recorded peak. If it warns that the recording clipped, lower the level or the mic preamp's gain and capture again. If it says nothing came back on input 1, check the cables and the mic preamp.
8. On success, the IR is saved to the library as **Capture** followed by the date and time. Rename it in the library, then load it in a Cab slot.

Bring the power amp up gradually between attempts until the recorded peak is healthy but not clipping. The method separates the speaker's distortion from its linear response and drops it, so a little speaker breakup during the sweep does no harm, but a clipping recording does.

> **Warning:** Start quiet. A sine sweep at full level through a power amp can damage speakers and hearing. Never point the speaker at your ears.

> **Note:** The IR includes everything in the loop: your power amp, the speaker and cabinet, the mic, the mic preamp, the interface and the room. That is the point, but it means a coloured power amp or a boomy room ends up in the IR too.

### Tips for good captures

- Capture several mic positions and distances, then compare or blend them.
- Use Auto-trim or Minimum phase when blending your captures with other IRs, so they line up.
- Close-miked captures depend little on the room. For a "room" IR, move the mic back and keep a longer IR length.
