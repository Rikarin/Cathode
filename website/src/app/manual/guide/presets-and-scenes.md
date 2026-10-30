Cathode has three levels of "sound": **presets**, **scenes** and **channels**. A preset is a whole rig. Scenes switch between variations of that rig instantly, without a gap. Channels give each block up to four sets of settings that scenes can choose between. Used together, one preset can cover a whole song, or a whole set.

|             | What it holds                                                                                                           | How many                                            | Switching                                                                  |
| ----------- | ----------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------- | -------------------------------------------------------------------------- |
| **Preset**  | The grid, every block with all its channels, the scenes, modifiers and controller settings, the tempo, its Perform page | As many as you like                                 | Loads a new rig. Fast, and tails can ring over, but it is a bigger change. |
| **Scene**   | For each block: engaged or bypassed, and which channel. Plus a level, four controller values, MIDI messages and a name. | 8 per preset                                        | Instant and gapless.                                                       |
| **Channel** | A complete set of one block's settings                                                                                  | Up to 4 per block (A to D); a few blocks have fewer | Gapless crossfade.                                                         |

A good habit: build one preset per song (or per style), use scenes for the parts of the song (verse, chorus, solo), and use channels when a block needs to sound different between those parts.

## Presets

### The working copy

What you hear is the **working copy** of the loaded preset. Every edit changes the working copy at once, from the editor, a MIDI controller or a footswitch alike. Nothing reaches the stored preset until you save. A dot after the preset's name in the preset bar means the working copy has unsaved changes.

**Undo** (⌘Z) and **Redo** (⇧⌘Z or ⌘Y) step through your edits to the working copy.

### Saving

- **Save** (⌘S, or the save button): stores the working copy over the preset. A preset that has never been saved (a new one) has nothing to store over yet, so Save asks for its name first, as **Save as…** does.
- **Save as…** (⇧⌘S, or **Preset ▸ Save as…**): stores it as a new preset under a name you type, and keeps working on the new one.

The scene you are on when you save is remembered: by default, the preset loads on that scene next time.

### Factory presets are read-only

The 70 presets of the **Factory** bank cannot be overwritten, renamed or deleted. Play them and change them as much as you like: when you save, Cathode asks for a name and saves **your own copy**, leaving the original as it was.

### Preset information

**Preset ▸ Name, author, tags and notes…** sets the preset's **Name**, **Author**, **Tags** (separated by commas) and **Description**. Click **Apply** to change the working copy, then save. The Library and the preset picker search all of these.

### Loading presets

There are several ways to load a preset:

- **The preset picker:** press **P**, or click the preset name. Type to search by name, author or tag, use **↑** and **↓**, and press **↵**. Click the star on an entry to make it a favourite, and the star button at the top to list favourites only. With the **pin** on, the picker stays open after loading, so you can try one preset after another.
- **A bank slot:** in the preset picker, choose a bank at the top and type a slot number (1 to 128). The line under the field shows what is in that slot; press **↵** to load it.
- **Next and previous:** the arrows beside the preset name, or **⌘PgDn** and **⌘PgUp**. They step through the banks slot by slot, as a foot controller steps through numbered presets: the rest of the current bank (empty slots are skipped), then on into the next bank, in the order the bank list shows them (**Factory** first), and from the last bank back round to the first. Presets that are in no bank come after the banks, in alphabetical order, so every preset can be reached. The number before the name is the preset's slot in its bank, and the bank's name is beside the preset; a preset in no bank shows **---**. Next and previous go on from the slot you loaded the preset from; a preset loaded by name counts as being in the first slot that holds it. To play from another bank, click the bank's name and choose it: its first preset loads, and next and previous go on through that bank.
- **Within a setlist:** after a song of a setlist is loaded (from the Perform page, with **⇧⌘PgDn**, or by a `songNext` footswitch), next and previous go through that setlist's songs instead, each on its own scene, and from the last song back round to the first. Loading a preset any other way leaves the setlist. The number is then the song's place in the setlist. To play a set, song next and previous are usually better: see [Preparing a set](/manual/live-performance#preparing-a-set).
- **Recent presets:** the clock button in the preset bar lists the last eight presets you loaded in this editor.
- **The Library:** double-click a preset, or select it and click **Load**.
- **MIDI:** a program change or a footswitch. See [MIDI and footswitches](/manual/midi-and-footswitches).

If the working copy has unsaved changes, Cathode asks before replacing it, and so do **New preset** (+) and **New from template…**. The desktop app asks too before it quits (or, on Windows and Linux, when you close its window) while it runs the engine on this computer, since the working copy goes with it. You can turn this off with **Global ▸ Presets and scenes ▸ Confirm discarding edits**.

### Other preset commands

All in the **Preset** menu, except where noted:

| Command                | What it does                                                                                                   |
| ---------------------- | -------------------------------------------------------------------------------------------------------------- |
| **Revert to saved**    | Throws away unsaved changes and goes back to the stored preset. Only available when there are changes.         |
| Reload (**F5**)        | Loads the stored preset again, as a fresh load. This also clears the undo history.                             |
| **Clear preset…**      | Empties the grid and the name, after a confirmation. Undo brings it back.                                      |
| **Save as template…**  | Stores the working copy as a template under a name.                                                            |
| **New from template…** | Starts a new preset from a template. The dialog also deletes templates.                                        |
| **Take a snapshot**    | Stores the working copy as it is now, unsaved changes included, with a time stamp. One click, nothing to name. |
| **Snapshots…**         | Lists this preset's snapshots, to **Load** one into the working copy or delete it.                             |
| **Manage presets…**    | Opens the page for organising banks. See [Banks](/manual/library#banks-and-manage-presets).                    |

**Templates** are starting points: a favourite routing with your usual gate, amp, cab and delay, ready to build on. **Snapshots** are save points while you experiment: take one before trying something drastic, and load it if you change your mind. Both are stored with your other data and shared by every editor connected to the same engine.

### A/B compare

The **Compare** switch in the preset bar keeps two versions of the working copy:

1. While you are on **A**, click **B**. B starts as a copy of A.
2. Change B.
3. Click **A** and **B** in turn to hear the difference. Each keeps its own edits.
4. Save the one you prefer while it is active.

Loading another preset clears both.

### Setting a value in other presets

Right-click any control and choose **Set in other presets…** to copy its current value into the same block of other presets: choose the current channel or **All channels**, tick the presets, and click **Set**. Presets that do not have that block (same id and type, such as `amp1`) are skipped and named. Useful after you change your guitar or your speakers and want to adjust a setting everywhere at once.

## Scenes

A scene stores, for every block in the preset, whether it is **engaged or bypassed** and **which channel** it uses. Switching scenes changes all of those at once, in a single step, with no gap in the sound. Blocks that stay engaged carry on as they were, tails included. For a block the new scene bypasses, its [bypass mode](/manual/the-grid#bypass-modes) decides whether its tail rings out (**Mute FX In**) or stops.

Each scene also has:

- a **name**;
- a **Scene Level** (−60 to +12 dB), applied after the whole grid: use it to lift a solo scene;
- four **scene controllers**, values a [modifier](/manual/modifiers#scene-controllers) can follow, so a knob can sit at a different position in each scene;
- up to eight **MIDI messages** sent to other gear when the scene is selected (see [Scene MIDI](/manual/midi-and-footswitches#scene-midi)).

### Switching scenes

- Click **1** to **8** next to **Scene** in the preset bar.
- Press **⌘1** to **⌘8**.
- Use the arrows beside the scene name, which wrap around from 8 to 1.
- From MIDI or a footswitch.

### Editing a scene

There is no separate edit mode: whatever you change about bypass and channels is stored in the **active** scene.

1. Select the scene.
2. Engage or bypass blocks (Space) and choose their channels (A to D).
3. Select the next scene and set it up the same way.
4. Save the preset.

To rename the scene, click its name in the preset bar, type, and press **↵**.

To set a block's state in **every** scene at once, right-click it on the grid and choose **Engage in all scenes** (⇧Space), **Bypass in all scenes** (⇧⌘Space) or **Channel in all scenes**. New blocks start engaged on channel A in every scene.

### The Scenes page

**Scenes** in the navigation bar shows a table with a row per block and a column per scene. The active scene's column is highlighted.

<!-- screenshot wanted: the Scenes page: the block-by-scene table with engaged dots and channel letters, the active scene highlighted -->

- Click a block's dot to engage (green) or bypass it in that scene.
- Click a channel letter to choose that channel in that scene.
- An asterisk after a channel letter means Scene Ignore is on for it (see below).

At the top, choose two scenes and click **Copy first onto second** to duplicate a scene, or **Swap** to exchange them. Copying a scene is the quickest way to make a variation: copy your rhythm scene onto scene 2, then turn on the boost and change the delay there.

### Scene Revert

**Global ▸ Presets and scenes ▸ Scene Revert** decides what happens to changes you make while playing:

- **Off** (the default): if you bypass a block or change a channel in a scene, that change stays with the scene while the preset is loaded. Leave the scene and come back, and it is as you left it.
- **On**: selecting a scene always brings it back as it was last saved.

### Which scene a preset starts on

**Global ▸ Presets and scenes ▸ Scene on preset load** is **As saved** by default (the scene the preset was on when it was saved). Choose **Scene 1** to **Scene 8** to always start on a fixed scene.

### Scene Ignore

**Scene Ignore** makes a block keep whatever state it is in when the scene changes, as long as it is on a particular channel. Turn it on with the **Scene Ignore on** switch under the channel buttons in the block's left column; it applies to the channel shown.

Example: your wah is on channel A in every scene, with Scene Ignore on for A. Kick it on with a footswitch in the middle of a verse, change to the chorus scene, and it stays on.

## Channels

Most blocks have four **channels**, A, B, C and D. Each channel is a complete, independent set of the block's settings, including its type or model. An Amp can be a clean Fender model on A, a crunchy Marshall on B and a high-gain model on C. A few blocks have fewer channels (two, or one).

The block's tile on the grid shows the channel it is on in the top-right corner.

### Choosing a channel

- Click **A**, **B**, **C** or **D** under **Channel** in the block's left column.
- With the block selected, press **A**, **B**, **C** or **D**, or **X** for the next channel.
- Right-click the block on the grid and choose a channel.

Choosing a channel stores it in the active scene, just like bypass.

### Editing channels

Every edit on a block's page goes to the channel it is on now. To work on channel C, switch the block to C first.

The **⋯** menu in the block's left column has the channel tools:

- **Copy channel X to** another channel: start a new channel from an existing one.
- **Swap channel X with** another.
- **Reset channel X** (also **⌘I**): back to the defaults.
- **Reset block (all channels)**.

To keep a channel's settings for other presets, save it to the [Block Library](/manual/library#the-block-library) (⌘B).

### Channels or more blocks?

Use **channels** when one block needs different settings in different scenes and only one of them is heard at a time: a drive that is a light boost in the verse and a fuzz in the solo. Use **separate blocks** when you want two sounds at the same time, or when you need to fade between them (two amps into a Mixer). A channel change is a crossfade on the same block, so a delay's repeats or a reverb's tail continue, but they take on the new channel's settings.

## Putting it together: a song in one preset

1. Build the chain: gate, drive, amp, cab, delay, reverb.
2. Set the amp's channel A to a clean sound and channel B to a driven one. Set the delay's channel A to a short slap and channel B to a long ambient delay.
3. **Scene 1, "Verse":** amp on A, drive bypassed, delay on A.
4. Copy scene 1 onto scene 2 on the Scenes page. **Scene 2, "Chorus":** amp on B.
5. Copy scene 2 onto scene 3. **Scene 3, "Solo":** drive engaged, delay on B, Scene Level +2 dB on the Controllers page.
6. Save. Map three footswitches to scenes 1 to 3, and you can play the song with three buttons.
