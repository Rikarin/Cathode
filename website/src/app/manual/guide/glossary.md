The words used in this manual and in amp and effects talk, in alphabetical order.

## A

**A/B compare.** Two versions of the working copy you can switch between while tweaking, with the **Compare** switch in the preset bar. See [A/B compare](/manual/presets-and-scenes#ab-compare).

**ADSR.** An envelope generator with four stages: Attack (rise), Decay (fall), Sustain (the level held), Release (the fall when it stops). In Cathode, ADSR 1 and 2 are modifier sources triggered by your playing. See [ADSR 1 and ADSR 2](/manual/modifiers#adsr-1-and-adsr-2).

**AIDA-X.** A format of neural amp captures (`.aidax` files), loaded in the Amp NAM block like NAM files.

**ALSA.** The Linux kernel's sound system. Cathode can use it directly, without a sound server.

**ASIO.** A low-latency audio driver standard on Windows, supplied by the interface maker. Cathode lists each installed ASIO driver as a device of its own, next to WASAPI.

**Attack, Release.** How fast something reacts when a signal rises (attack) and when it falls (release). Used by gates, compressors, envelope followers and modifiers.

## B

**Balanced cable.** A cable with two signal conductors and a shield (XLR, or TRS jack) that rejects hum over long runs. Use it from the interface's line outputs to monitors and the PA.

**Bank.** An ordered list of up to 128 preset slots, for organising presets and loading them by number. See [Banks](/manual/library#banks-and-manage-presets).

**Block.** One processing unit on the grid: an Amp, a Cab, a Delay, a Mixer. Each block type is described in the block reference.

**Block id.** A block's name within a preset, such as `amp1` or `delay2`. Shown under the block's tile on its page. MIDI mappings and footswitches use it.

**Block Info.** A view of the grid that shows each block's short code, channel and global mark. Hold ⌥ or press F1.

**Block Library.** Saved settings for single blocks, to reuse in any preset. See [The Block Library](/manual/library#the-block-library).

**BPM.** Beats per minute: the tempo.

**Buffer.** The chunk of samples the engine processes at a time. Smaller buffers mean less latency but more risk of dropouts. See [Buffer size and latency](/manual/audio-setup#buffer-size-and-latency).

**Bypass.** Switching a block off. What a bypassed block passes on depends on its **bypass mode**. See [Bypass modes](/manual/the-grid#bypass-modes).

## C

**Cab, cabinet.** The speaker enclosure of a guitar amp. The Cab block models a cabinet, its speaker and the microphone in front of it.

**Cab modeling off.** A global switch that makes every Cab and IR Player block pass the signal through, for playing through a real guitar cabinet.

**Calibration.** Telling Cathode how many volts at the input reach full scale, so the amps and drives respond as they would to your real guitar. See [Input level and calibration](/manual/audio-setup#input-level-and-calibration).

**CC (control change).** A MIDI message with a controller number and a value from 0 to 127. Used by footswitches and expression pedals.

**Channel (block).** One of up to four complete sets of a block's settings, A to D. Scenes choose which channel each block uses. See [Channels](/manual/presets-and-scenes#channels).

**Channel (MIDI).** One of 16 lanes a MIDI message can travel on, so several devices can share a cable.

**Clipping.** When a signal goes past the top of the range a converter or a digital system can represent, and its peaks are cut flat. It sounds harsh, and the IN and OUT meters show **CLIP**.

**Controller.** A source of movement for modifiers: an LFO, an ADSR, an envelope or pitch follower, the sequencer, a scene controller, Manual A to D, or External 1 to 8 from MIDI.

**CoreAudio, CoreMIDI.** The macOS audio and MIDI systems Cathode uses.

**CPU.** In the navigation bar, how much of each buffer's time the engine needed (99.9th percentile). Near 100 % means dropouts.

**Curve.** The shape a modifier bends its source through: Linear, Exp, Log or S-curve.

## D

**dB (decibel).** A ratio of levels on a logarithmic scale. +6 dB is roughly twice the voltage; −6 dB half.

**dBFS.** Decibels relative to digital full scale. 0 dBFS is the loudest a digital signal can be; everything else is negative.

**dBu.** Decibels relative to 0.775 volts RMS: the unit interface specifications use for maximum input and output levels.

**DI, DI out.** Direct injection: the dry guitar signal, unprocessed. Cathode's DI out sends it to its own interface channels for recording and re-amping.

**Drawer.** The lower half of the Editor page, which shows the selected block's controls.

**Dropout.** A click, pop or gap in the sound when the computer does not finish a buffer in time.

## E

**Engine.** The part of Cathode that processes audio in real time. It runs as its own program, apart from the editor.

**Envelope follower.** A modifier source that follows how loud you play.

**Expression pedal.** A rocking pedal that sends a continuous value, usually through a MIDI controller. In Cathode it drives one of External 1 to 8.

**External 1 to 8.** Modifier sources set by MIDI CCs from your controller, mapped on the Global page.

## F

**Factory bank.** The 70 read-only presets that come with Cathode.

**Feedback Send, Feedback Return.** A pair of blocks that carries a signal from a later column back to an earlier one. See [Feedback loops](/manual/the-grid#feedback-loops).

**FRFR.** Full-range, flat-response: a speaker that reproduces the whole audio range evenly, like a studio monitor or PA speaker, as opposed to a guitar speaker, which colours the sound heavily. Cathode with its Cab blocks on sounds right through FRFR speakers.

**Full scale.** The largest signal a converter can represent (0 dBFS). An input's full scale in volts is what calibration sets.

**FX loop.** A send and return that put external equipment (a pedal, a rack unit) into the signal chain. Cathode's FX Loop block sends to and returns from interface channels you choose.

## G

**Gain staging.** Keeping the level sensible at each point in the chain, so nothing clips and nothing is buried in noise. See [Gain staging and levels](/manual/gain-staging).

**Gate.** A block that silences the signal below a threshold, to cut hiss and hum between notes.

**Global.** Shared by every preset. The **Global** page holds settings for all presets; a **global block** shares one block's settings across presets. See [Global blocks](/manual/library#global-blocks).

**Grid.** The six rows by fourteen columns where you place and connect blocks. See [The grid](/manual/the-grid).

## H

**Headless.** Running without a screen. The headless server runs Cathode on a small Linux computer you edit from another device. See [The headless server](/manual/headless-server).

**Hi-Z.** High impedance: an input designed for a passive guitar pickup, which needs a high input impedance to keep its treble.

## I

**Impulse response (IR).** A recording of how a speaker cabinet and microphone (or a room) respond to a single click. Applying it to a signal makes it sound as if played through that cabinet. Cathode loads IRs in the Cab and IR Player blocks.

**Input block.** Where a hardware input enters the grid, with its own noise gate and input level.

**Instance.** One copy of a block type in a preset. `drive2` is the second Drive instance.

## J

**JACK.** A low-latency audio server for Linux. PipeWire provides a JACK interface too.

## L

**Latency.** The delay between playing a note and hearing it. See [Buffer size and latency](/manual/audio-setup#buffer-size-and-latency).

**Layout (footswitch).** A description of your foot controller's switches: what each sends and what it does. See [Footswitch layouts](/manual/midi-and-footswitches#footswitch-layouts).

**LFO.** Low-frequency oscillator: a source that cycles up and down slowly, used to sweep a parameter. Cathode has LFO 1 and 2.

**Looper.** Records what you play and plays it back in a loop for you to play over. See [The looper](/manual/live-performance#the-looper).

**LUFS.** Loudness units relative to full scale: a measure of how loud something sounds to the ear, used by Preset Leveling.

## M

**Manual A to D.** Four global knobs on the Controllers page, usable as modifier sources and settable from MIDI.

**MIDI.** The standard for musical devices to send control messages to each other: program changes, control changes, notes, clock.

**MIDI clock.** A stream of MIDI timing messages that lets one device follow another's tempo.

**MIDI learn.** Creating a mapping by choosing an action and then moving the control you want to use.

**Mixer.** A block that mixes up to six rows with their own level and balance.

**Modifier.** A link that makes a knob follow a controller. See [Controllers and modifiers](/manual/modifiers).

**Mono, stereo.** One channel of audio, or two (left and right). Some blocks sum stereo to mono; the grid marks where stereo width is lost with a **MONO** tag.

**Multiplexer.** A block that picks one of the rows cabled into it, to switch between parallel paths.

**Mute.** Silencing every output at once, with **M** or the navigation bar's mute button.

## N

**NAM (Neural Amp Modeler).** An open format for neural network captures of amps and pedals (`.nam` files), loaded in the Amp NAM block.

**Null backend.** The silent stand-in the engine uses when it has no audio device. Everything works but nothing is heard.

## O

**Output block.** Where the grid's signal leaves for an interface output. Output 1 is the main output; Outputs 2 to 4 are optional.

**Oversampling.** Processing at a multiple of the sample rate inside a block, to keep distortion clean of digital artefacts. Cathode's amp and drive blocks do it internally.

## P

**Parallel path.** Two or more rows of blocks that carry the signal side by side and join later.

**PC (program change).** A MIDI message with a number from 0 to 127, usually used to change presets.

**Perform page.** A page of large controls you choose for playing live. See [The Perform page](/manual/live-performance#the-perform-page).

**Pitch follower.** A modifier source that follows the pitch of the note you play.

**Preset.** A complete rig: the grid, every block's settings and channels, eight scenes, modifiers and controllers. See [Presets](/manual/presets-and-scenes#presets).

**Preset Leveling.** A tool that measures how loud a preset plays and sets its level to a target loudness.

## R

**Re-amping.** Playing a recorded dry guitar (DI) back through an amp or processor later, to change the sound after recording.

**Round trip.** The total delay from the input jack, through the computer, to the output jack.

**RTA.** Real-time analyser: a display of a signal's level across frequencies. Cathode has one in the Analyser dialog and as a block.

## S

**Sample rate.** How many times per second the audio signal is measured. 48 kHz is Cathode's standard.

**Scene.** One of eight variations within a preset: which blocks are engaged and which channel each uses, plus a level. Switching scenes is gapless. See [Scenes](/manual/presets-and-scenes#scenes).

**Scene controller.** One of four values per scene that modifiers can follow.

**Scene Ignore.** A per-channel switch that makes a block keep its state when the scene changes.

**Scene Level.** A level for each scene, applied after the whole grid.

**Scene Revert.** A global setting: when on, selecting a scene always restores it as saved.

**Sequencer.** A modifier source that steps through 2 to 16 fixed levels in a loop.

**Server.** The part of Cathode that keeps presets and settings and connects the editor to the engine. The desktop app runs one for you; a headless rig runs one for the network.

**Shunt.** An empty connector on the grid that passes the signal through. See [Shunts](/manual/the-grid#shunts).

**Snapshot.** A quick save point of the working copy, unsaved changes included.

**Spillover.** Letting delay and reverb tails ring out after a block is bypassed or the preset changes, instead of cutting them.

**Strobe.** A tuner display of marks that drift sideways while a string is out of tune and stand still when it is in tune. See [The tuner](/manual/live-performance#the-tuner).

## T

**Tap tempo.** Setting the tempo by tapping a button or footswitch in time.

**Template.** A preset stored as a starting point for new ones.

**Tempo division.** A note value (1/4, 1/8 dotted, 1/16 triplet…) that ties a time or a rate to the tempo.

**Token (access token).** The password-like key a remote editor needs to control a headless rig.

**Tuner.** Shows the pitch of the string you play, against the nearest note or a string of a tuning such as Drop D. Open it with ⌘T.

## V

**View (footswitch).** One page of switches within a footswitch layout.

## W

**WASAPI.** The Windows audio system Cathode uses, in exclusive mode when the device allows it.

**Wet, dry.** The processed signal of an effect (wet) and the unprocessed signal (dry). A block's **Mix** sets the balance.

**Working copy.** The loaded preset as it is now, with your unsaved changes. It is what you hear. Saving stores it.
