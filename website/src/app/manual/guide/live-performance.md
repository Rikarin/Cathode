This chapter is about using Cathode on stage and in rehearsal: the Perform page, preparing a set, the tuner, global mute, the looper, keeping presets at the same loudness, and a checklist for the gig.

## The Perform page

The **Perform** page shows the handful of controls you need while playing, large and on one page: a drive's gain, a delay's mix, a reverb's decay, a cab's microphone. It is easier to hit on a small screen or a touch screen than a knob in the block drawer.

![The Perform page: drive, amp, compressor, reverb, gate and cab controls, each shown as a large knob or choice](screenshots/perform.webp)

**To add a control:** right-click any knob, switch or choice on a block's page and choose:

- **Add to the preset's Perform page:** the control is saved with this preset.
- **Add to the global Perform page:** the control shows in every preset that has a block with that id (for example `amp1`).

**This preset** and **Global** at the top of the Perform page switch between the two. A global control whose block the loaded preset does not have shows as "not in this preset".

Each control works like the original: drag, scroll or type a value. A control driven by a modifier turns with it. The **×** in a control's corner removes it from the page (the block itself is not touched).

> **Tip:** Put the same controls on the global Perform page with blocks of the same ids in all your presets (`amp1` for the main amp, `delay1` for the main delay), and the page works the same in every song.

## Preparing a set

The editor has no setlist page yet, so use a **bank** as your set list:

1. Open **Preset ▸ Manage presets…** and click **+** beside **Banks** to make a bank. Double-click it to name it after the gig.
2. Drag your songs' presets from the **Library** list onto the first slots, in the order of the set. Leave an empty slot between sets if you like.
3. Click **Save**.

Then, to load the songs in order:

- **From the computer:** press **P**, choose the bank at the top of the preset picker, type the song's slot number and press **↵**.
- **From a foot controller:** map program changes to the bank with the `presetSlot` action (see [Changing presets by program change](/manual/midi-and-footswitches#changing-presets-by-program-change)), and step through the programs on your controller.

The **Recent presets** button (the clock in the preset bar) also gives you the last eight presets you loaded, which helps at rehearsal.

### Switching presets mid-song

Changing preset is fast, and with **Global ▸ Presets and scenes ▸ Preset spillover** on (the default), the old preset's delay and reverb tails ring out under the new one. Turn it off for a clean cut. Within a song, prefer [scenes](/manual/presets-and-scenes#scenes): they switch with no gap at all.

## The tuner

Open the tuner with **⌘T**, the tuner button in the navigation bar, or a footswitch with the `tuner` action. Play a single string. The tuner shows the note and octave, a needle spanning ±50 cents, the frequency and the deviation in cents. The note and needle turn green within ±2 cents of the note.

The tuner listens to the input, before any effects, so a wah or a pitch shifter does not confuse it.

> **Warning:** Opening the tuner does **not** mute Cathode. To tune silently, mute first (**M**, or the mute button), or pull back an expression pedal that drives a Volume/Pan block (the tuner listens before it, so it still hears you).

## Global mute

**Mute** silences every output at once with a short fade: press **M**, or click the speaker button in the navigation bar, which turns red while muted. Press again to unmute.

- The mute holds across preset changes, so you can change presets silently and unmute when you are ready.
- It silences everything, including the metronome and the looper.
- It is never saved with a preset or a setting: when you quit and reopen Cathode, it starts unmuted.

You can also put mute on a footswitch or a MIDI controller with the `mute` action (see [Actions](/manual/midi-and-footswitches#actions)).

## The looper

Click the **looper** button in the navigation bar (the loop icon) to open the looper. Its icon turns red while recording.

The dialog shows the looper's **state** (empty, recording, playing, overdubbing, stopped, or armed when it waits for you to start playing), the position and the loop length in seconds, and tags for **reverse** and **half speed** when they are on.

| Button         | What it does                                  |
| -------------- | --------------------------------------------- |
| **Record**     | Starts recording a loop.                      |
| **Play**       | Plays the loop.                               |
| **Overdub**    | Records on top of the loop while it plays.    |
| **Stop**       | Stops playback.                               |
| **Undo**       | Takes back the last overdub.                  |
| **Reverse**    | Plays the loop backwards.                     |
| **Half speed** | Plays the loop at half speed, an octave down. |
| **Clear**      | Erases the loop.                              |
| **Export WAV** | Saves the loop as `loop.wav`.                 |

Each of these is also a MIDI and footswitch action: `looper record`, `looper play`, `looper overdub`, `looper stop`, `looper undo`, `looper reverse`, `looper halfSpeed` and `looper clear`. Footswitch LEDs can show recording, playing and overdubbing.

### The rig looper and the Looper block

Without a Looper block on the grid, the buttons drive the **rig looper**: up to 60 seconds of stereo, recorded at the very end of the chain, after the whole preset and the scene level. It is not part of any preset, so the loop keeps playing when you change preset, and what you play over it goes through the new preset.

To control where the loop sits in the chain, add a **[Looper](/manual/blocks/looper)** block to the grid. While the loaded preset has one, the buttons and actions drive that block instead, and the rig looper stops. The block can hold up to 300 seconds, and has more options: record once, threshold recording (it waits, armed, until you play), what a second press of Record does, quantising the loop to the tempo, playback speed, and filters on the playback. Place it before your delays and reverbs, for example, so they ring over the loop's ends.

When you change to another preset that also has a Looper block, the loop moves across and keeps playing. Change to a preset without one, and the loop stops.

## Tempo and the metronome

Set the tempo with **TAP** in the navigation bar, by typing it into the BPM field, or with a `tap` footswitch. The metronome button turns a click on and off; it plays on Output 1 only, so with your front-of-house feed on Output 2 the audience does not hear it. See [Tempo](/manual/modifiers#tempo).

## Matching levels between presets

Presets built on different days rarely play at the same loudness, and a jump in level between songs is the most common live problem. Use **Preset Leveling** (⌘L, or its button in the navigation bar) on each preset:

1. Open it and click **Measure**.
2. Play the preset as you would on stage, for twenty seconds or so, then click **Stop**.
3. Choose a **Target** loudness: **−18** is the factory bank's level.
4. Click **Apply** to change the amp's **Level** (or the Output's **Main Level**, in a preset without an amp) by the suggested amount, then save. It changes the channel the amp is on now, so level each channel you use.

Use **Scene Level** (on the Controllers page) for differences between scenes, such as a lift for the solo. [Gain staging and levels](/manual/gain-staging) explains loudness in more detail.

## Keeping it stable

- **Use a safe buffer size.** If you hear a single click in a long rehearsal, go up one buffer size. A dropout on stage is worse than a millisecond of latency. Watch **Engine overruns** in Settings ▸ Audio setup.
- **Check the CPU of your heaviest preset.** The **Est.** and **CPU** figures in the navigation bar should stay well below 100 % on every preset of the set.
- **Leave Confirm discarding edits on** (Global ▸ Presets and scenes), so a mis-click cannot throw away a tweak, and save tweaks during sound check.
- **Set a startup preset.** **Global ▸ Presets and scenes ▸ Startup preset** chooses the preset the engine loads when it starts, so a restart puts you back on your first song.
- **Turn off sleep and notifications** on the computer for the gig, and keep it on mains power.

## Gig checklist

Before you leave:

- [ ] Every preset of the set is saved, in a bank in set order.
- [ ] Presets are levelled, and you have played each one at stage volume.
- [ ] You have a backup: the presets exported as `.cthpreset` files, or a copy of Cathode's [data folder](/manual/the-editor#where-things-are-kept).
- [ ] The MIDI controller, its cable and its power supply are packed; the layout and mappings work.
- [ ] Spare cables for the interface's inputs and outputs, and its power supply.

At the venue:

- [ ] Connect everything with the volume down. Open Cathode and check that **Backend** in Audio setup names your interface, not "null".
- [ ] Check the **IN** meter with your hardest playing: no **CLIP**.
- [ ] Check the **OUT** meter: no **CLIP**. Set the output's **Boost/Pad** (Global ▸ Outputs) to give the sound engineer a sensible level, and **Sum L+R** if they give you one channel.
- [ ] If you play into a guitar amp's power amp and cab, check that **Cab modeling off** is on; if into the PA, that it is off.
- [ ] Tune (⌘T, then mute while you tune).
- [ ] Run through each preset and each scene you will use, and every footswitch.
- [ ] Load the first song.
