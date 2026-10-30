This chapter is about using Cathode on stage and in rehearsal: the Perform page, preparing and playing a setlist, the tuner, global mute, the looper, keeping presets at the same loudness, and a checklist for the gig.

## The Perform page

The **Perform** page shows the handful of controls you need while playing, large and on one page: a drive's gain, a delay's mix, a reverb's decay, a cab's microphone. It is easier to hit on a small screen or a touch screen than a knob in the block drawer.

![The Perform page: the setlist bar, and compressor, drive, amp, cab and reverb controls, each shown as a large knob, switch or choice](screenshots/perform.webp)

**To add a control:** right-click any knob, switch or choice on a block's page and choose:

- **Add to the preset's Perform page:** the control is saved with this preset.
- **Add to the global Perform page:** the control shows in every preset that has a block with that id (for example `amp1`).

**This preset** and **Global** at the top of the Perform page switch between the two. A global control whose block the loaded preset does not have shows as "not in this preset".

Each control works like the original: drag, scroll or type a value. A control driven by a modifier turns with it. The bar along the top of each control arranges the page:

- **Rename** (the pencil) gives the control a caption of its own, such as "Crunch" instead of "Amp Gain". Press Enter to keep it, Escape to cancel; clear the text to go back to the block and parameter name.
- **Move earlier** and **Move later** (the arrows) swap it with its neighbour. You can also drag a control by its grip (the dotted handle on the left) and drop it on another control's place.
- **×** removes it from the page (the block itself is not touched).

Captions and order are saved with the page: with the preset for **This preset**, in the global settings for **Global**.

> **Tip:** Put the same controls on the global Perform page with blocks of the same ids in all your presets (`amp1` for the main amp, `delay1` for the main delay), and the page works the same in every song.

## Preparing a set

A **setlist** is the running order of a gig: a list of songs, each one a preset from your library and the scene the song opens on. The same preset can appear as often as you like, for example once for a song's verse sound and again, on its lead scene, for another song. Setlists live on the **Setlists** page in the navigation bar.

### Making a setlist

1. On the **Setlists** page, click **+** beside **Setlists**. Type a name for the gig and click **Rename**.
2. Add songs from the **Library** list on the right: double-click a preset, focus it and press **↵**, click the **+** that appears when you point at it, or drag it onto the song list (between two songs to put it there).
3. For each song, choose the **scene** it starts on. Give it a **name** of its own if the preset's name is not the song's ("Opener", "Encore"); leave it empty to show the preset's name.
4. Put the songs in order: drag a song by its grip (the dotted handle on the left), use its arrow buttons, or focus it and press **⌥↑** or **⌥↓**. **×**, or **Delete** on a focused song, takes it out of the setlist; the preset stays in the library.

Every change is saved as you make it: there is no Save button. Right-click a setlist in the list (or use the buttons above its songs) to **Rename**, **Duplicate** or **Delete** it. A copy is a good start for a second set or a shorter version of the same gig.

A song whose preset is no longer in the library (its file was deleted or moved outside Cathode) shows **Missing preset** with a warning sign. Cathode does not let you delete a preset a setlist still uses, so this only happens when files change behind its back. Remove the song, or put the preset back. While it is missing, playing the set skips that song.

### Playing a setlist

Click **Play on the Perform page** on the Setlists page, or pick the setlist from the **Setlist** list at the top of the **Perform** page. The bar shows the song playing (its number, its name, and its preset and scene), the next song, and every song of the set as a button.

- **Next song** (the button on the right, **⇧⌘PgDn**, or a footswitch with the `songNext` action) loads the next song on its scene. Just after you pick a setlist, nothing is playing yet and Next song loads the first one.
- **Previous song** (the button on the left, **⇧⌘PgUp**, or `songPrevious`) goes back one song.
- Click a song's button to jump straight to it.

The set does not wrap round: at the last song, Next song does nothing, so an extra press at the end of the night cannot take you back to the opener. Unsaved changes to the preset playing are handled as for any preset change: Cathode asks first, unless you have turned that off.

Cathode remembers which setlist is playing and which song you are on in its global settings, so the place is kept if you load another preset in between (to fix something during a break), on every editor connected to the same Cathode, and after a restart. Next song always carries on from the last song the set loaded.

To step through a set from a foot controller, map two switches or MIDI messages to `songNext` and `songPrevious` (see [MIDI actions](/manual/midi-and-footswitches#actions)). They work whichever page the editor shows, and with no editor open at all.

> **Tip:** Preset next and previous (**⌘PgDn**, **⌘PgUp** and `presetNext`, `presetPrevious`) also follow a setlist, but only while the preset you loaded from it is still loaded, and they wrap round from the last song to the first. Song next and previous are the ones to use on stage.

If you would rather call presets up by number, a **bank** still works as a running order: put the songs' presets in its first slots in set order on **Preset ▸ Manage presets…**, then load them with the preset picker (**P**, the bank, the slot number) or with program changes (see [Changing presets by program change](/manual/midi-and-footswitches#changing-presets-by-program-change)). The **Recent presets** button (the clock in the preset bar) also gives you the last eight presets you loaded, which helps at rehearsal.

### Switching presets mid-song

Changing preset is fast, and with **Global ▸ Presets and scenes ▸ Preset spillover** on (the default), the old preset's delay and reverb tails ring out under the new one. Turn it off for a clean cut. Within a song, prefer [scenes](/manual/presets-and-scenes#scenes): they switch with no gap at all.

## The tuner

Open the tuner with **⌘T**, the tuner button in the navigation bar, or a footswitch with the `tuner` action. Play a single string. The tuner shows the note and octave, how far off it is (as a needle or a strobe), the frequency and the deviation in cents. The note and the display turn green within ±2 cents of the target.

The tuner listens to the input, before any effects, so a wah or a pitch shifter does not confuse it.

The settings below the display are global: they are kept for every preset, and every editor connected to the rig shares them.

### Needle or strobe

**View** switches between two displays:

- **Needle**: a needle on a scale from −50 to +50 cents. Easy to read from a distance, and quick to show which way to turn the peg.
- **Strobe**: two bands of marks that drift sideways. They move right when the string is sharp and left when it is flat, faster the further off it is, and stand still when it is in tune. A strobe shows errors far smaller than a needle can, so use it for the final touch: turn the peg until the marks stop.

If your system asks apps to reduce motion (an accessibility setting of macOS and Windows), the needle shows instead of the strobe, and a note under the display says so.

### Tunings

**Tuning** chooses what the tuner aims for:

- **Chromatic** (the default): the nearest of the twelve notes, whatever you play.
- A guitar or bass tuning: Standard, Half step down, Whole step down, Drop D, Drop C, DADGAD, Open G, Open D, Open E, 7-string standard, 8-string standard, Bass 4-string or Bass 5-string. The tuner picks the string of that tuning closest to what you play and shows its number under the note (string 1 is the highest), with the cents to that string. A string far out of tune still reads against its own target rather than the nearest note: a low E string tuned down a whole step shows as string 6, 200 cents flat, in Standard, and in tune in Drop D.

**A4** sets the reference pitch, the frequency of the A above middle C, from 430 to 450 Hz. It is 440 Hz, concert pitch, unless you change it, for example to match a piano tuned a little sharp. Every note and every string of a tuning moves with it.

### Offsets

Some players tune certain strings a few cents away from pitch so that chords in common positions sound sweeter on their guitar. With a tuning other than Chromatic selected, **Offsets** sets this up:

- **None** (the default): every string is tuned to its exact pitch.
- **Custom**: a box per string, in cents from −20 to +20 (in half cents). A positive offset tunes that string sharp, a negative one flat. The tuner then shows a string as in tune when it is at its pitch plus its offset.

The offsets belong to the string numbers, not to a tuning, so the same set applies when you change from Standard to Drop D. Typical values are small, a cent or two on the B and G strings for example; there is no right set, so try a few and listen to the chords you play most.

### Output while tuning

While the tuner is open, **Output while tuning** sets what every output plays:

| Option       | While tuning                                                                                       |
| ------------ | -------------------------------------------------------------------------------------------------- |
| Mute         | Silence, with a short fade out and back in. The default, so you can tune on stage without a sound. |
| Bypass       | Your guitar dry, straight from the input with no blocks, as a tuner pedal's true bypass would.     |
| Pass through | The preset keeps sounding, as if the tuner were not open.                                          |

The setting applies however the tuner was opened, from the editor, a footswitch or a MIDI message.

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

- [ ] Every preset of the set is saved, and the set is a setlist with no missing presets (or a bank in set order).
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
