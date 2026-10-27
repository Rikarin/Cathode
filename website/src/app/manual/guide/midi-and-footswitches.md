With a MIDI foot controller you can play Cathode hands-free: change presets and scenes, switch blocks on and off, tap the tempo, run the looper and the tuner, and sweep a wah or a volume with an expression pedal. This chapter covers connecting a controller, mapping its messages to actions, footswitch layouts with LEDs, expression pedals, and the MIDI that scenes send to other gear.

Everything here is on the **Global** page, in the **MIDI** and **Footswitches** sections, and applies to every preset.

## A little MIDI

MIDI controllers send short messages. The three kinds Cathode uses:

| Message                 | What it carries                                       | Typical use                                                                                              |
| ----------------------- | ----------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| **PC** (program change) | A number, 0 to 127                                    | Changing presets                                                                                         |
| **CC** (control change) | A controller number (0 to 127) and a value (0 to 127) | Footswitches (often 127 when pressed, 0 when released) and expression pedals (0 to 127 as you rock them) |
| **Note**                | A note number and a velocity                          | Some controllers send notes from their switches                                                          |

Each message travels on one of 16 **MIDI channels**, so several devices can share a cable.

Cathode treats a PC as a press. A CC or note counts as **pressed** at a value or velocity of 64 or more, and released below that.

## Connecting a controller

1. Connect the controller to the computer: by USB if it is a class-compliant USB MIDI device, or through a MIDI interface (many audio interfaces have MIDI sockets).
2. That's all: Cathode listens to every MIDI input it finds, and picks up devices plugged in later within a couple of seconds.
3. Open **Global** and look at the **Monitor** box in the **MIDI** section. Press a switch: a line such as `CC 20 = 127 (ch 1)` appears. If nothing appears, Cathode is not receiving anything (see [Troubleshooting](/manual/troubleshooting#midi-does-nothing)).

### Receive channel

**Global ▸ MIDI ▸ Receive channel** is **All** by default. Set it to **Channel 1** to **Channel 16** to make Cathode ignore every message on other channels, mappings and footswitches alike. Set your controller to send on the same channel.

### Send to

Cathode sends MIDI of its own: footswitch LEDs and scene messages. **Global ▸ MIDI ▸ Send to** chooses where they go. **All outputs** (the default) sends them to every MIDI output Cathode finds. Choose one output to send them only there, for example when a controller echoes what it receives back to Cathode, or when the LEDs should not reach a synth on another port.

If the chosen output is unplugged, Cathode sends nothing until it is back; the list shows it as **(not connected)**. The list is read when you open **Global**, so reopen the page after plugging in a new device.

## Mappings and MIDI learn

A **mapping** connects one MIDI message to one **action**. The table in the MIDI section lists them: the message and its channel, the action, its target and its value. The bin removes one.

<!-- screenshot wanted: Global page, MIDI section: the mappings table, the learn row (action, target and value lists, MIDI learn) and the Monitor showing a few messages -->

To add a mapping, use **MIDI learn**:

1. Under the table, choose the action from the first list.
2. If the action needs a target or takes a value, more lists appear beside it: pick the block, bank, looper command or layout, and the scene, channel, slot, Manual, External or view. The table below says which. Blocks are listed by name with their id, from the loaded preset.
3. For `scene` and `presetSlot`, the value list starts at **(from the message)** or **(the program number)**: the CC's value picks the scene, or the program change picks the slot. Pick a scene or slot instead to make the mapping always select that one.
4. Click **MIDI learn**. The button changes to **Move a control…**.
5. Press the switch or move the pedal. Cathode creates the mapping from the first message it receives and confirms with a message such as "Learned CC 20 = 127 (ch 1)".

While MIDI learn waits, a notice stays in the corner of every page saying what the next control will map, and **Learning:** beside the button says it too. Click **Cancel** in the notice, or **Cancel learn** beside the button, to stop without mapping anything. The wait is kept by Cathode, not by the page: after a reload, or in another editor connected to the same Cathode, the notice is still there until something is learnt or it is cancelled. Clicking **MIDI learn** again replaces what was waiting.

A new mapping replaces any old mapping of the same message on the same channel. The table names each mapping's target: the block, bank or layout by name.

> **Tip:** Block ids are shown in the left column of each block's page, under its tile (for example `delay1`, `amp1`), and in the tile's tooltip on the grid. The lists show them after the block's name, which tells two blocks of the same kind apart.

### Actions

| Action                           | Target                                                                         | Value                        | What it does                                                                                                                                   |
| -------------------------------- | ------------------------------------------------------------------------------ | ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `scene`                          |                                                                                | Scene, **1 to 8**            | Selects the scene. Without a value, a CC's value picks the scene (0 to 7, so 0 is Scene 1), and a value above 63 steps to the next scene.      |
| `sceneNext`, `scenePrevious`     |                                                                                |                              | Steps through the eight scenes, wrapping around.                                                                                               |
| `presetSlot`                     | A bank                                                                         |                              | Mapped to a PC: any program change loads that slot of the bank. PC 0 is slot 1, PC 1 is slot 2, and so on.                                     |
| `presetNext`, `presetPrevious`   |                                                                                |                              | Loads the next or previous preset in bank order, as the editor's arrows do: see [Loading presets](/manual/presets-and-scenes#loading-presets). |
| `bypass`                         | A block                                                                        |                              | Toggles the block on a press. From a CC, values of 64 and up engage it and lower values bypass it.                                             |
| `channel`                        | A block                                                                        | Channel, **1 to 4** (1 is A) | Switches the block to that channel.                                                                                                            |
| `external`                       |                                                                                | External, **1 to 8**         | The CC's value drives External 1 to 8, a modifier source. See [Expression pedals](#expression-pedals).                                         |
| `manual`                         |                                                                                | Manual, **1 to 4** (1 is A)  | The CC's value sets Manual A to D.                                                                                                             |
| `tap`                            |                                                                                |                              | Taps the tempo.                                                                                                                                |
| `tuner`                          |                                                                                |                              | Turns the tuner on and off. From a CC, 64 and up is on.                                                                                        |
| `metronome`                      |                                                                                |                              | Turns the metronome on and off. From a CC, 64 and up is on.                                                                                    |
| `mute`                           |                                                                                |                              | Mutes or unmutes every output. From a CC, 64 and up mutes.                                                                                     |
| `looper`                         | `record`, `play`, `stop`, `overdub`, `undo`, `reverse`, `halfSpeed` or `clear` |                              | Runs the looper.                                                                                                                               |
| `sceneLevelUp`, `sceneLevelDown` |                                                                                |                              | Raises or lowers the active scene's level by 1 dB and saves the preset.                                                                        |
| `save`                           |                                                                                |                              | Saves the working copy.                                                                                                                        |
| `layout`                         | A footswitch layout                                                            |                              | Makes that footswitch layout active.                                                                                                           |
| `view`                           |                                                                                | View, **1** for the first    | Shows that view of the active layout.                                                                                                          |
| `viewNext`, `viewPrevious`       |                                                                                |                              | Steps through the active layout's views.                                                                                                       |

> **Note:** Scene, channel, manual, view and External numbers all start at **1**: Scene 1, Channel B, Slot 1. Only a CC's own value, when a `scene` mapping has no value of its own, counts from 0, as MIDI does. Earlier versions of the editor counted scenes, channels, manuals and views from 0: your switches and mappings still do what they did, and now read one higher.

A target that is not in the lists any more (a block of another preset, a deleted bank or layout) is shown as its id followed by **(not found)** and kept until you pick another. Footswitch layouts are global, so a `bypass` switch made with another preset loaded may name a block this preset does not have.

### Changing presets by program change

1. In **Preset ▸ Manage presets…**, make a bank and put your presets in its slots in the order you want.
2. On the Global page, choose the action `presetSlot`, pick the bank by name, leave the slot at **(the program number)**, and click **MIDI learn**.
3. Send any program change from your controller.

From then on, every program change loads the preset in the matching slot (PC 0 loads slot 1). Many controllers number their programs from 1, so "program 1" on the controller is often PC 0.

Turn on **Global ▸ Presets and scenes ▸ Ignore redundant PC** if your controller resends the current program, for example when you switch its banks: a program change for the preset already loaded then does nothing.

## Expression pedals

An expression pedal plugged into a MIDI controller sends a CC whose value follows the pedal. In Cathode, it drives one of the **External 1 to 8** controllers, and modifiers follow the External.

**Map the pedal once:**

1. On the Global page, choose the action `external` and pick the External, for example **External 1**.
2. Click **MIDI learn** and rock the pedal.

**Then use it in any preset:** right-click a knob, choose **Add modifier…**, and pick **External 1** as the source. See [Controllers and modifiers](/manual/modifiers).

> **Tip:** Use External 1 for your main expression pedal. The factory presets' wahs and whammies expect it there, and rest at the heel until it is mapped. Without a pedal, move **External 1** under **Controllers ▸ External (live)** instead.

### Learn MIDI controller on a knob

Right-click a knob and choose **Learn MIDI controller**. Cathode picks the first free External and asks you to move a pedal or knob on your controller. That CC is then mapped to the External, globally, and the knob gets a modifier from that External over its whole range. Until you move something, the knob is left as it was: **Cancel** in the notice stops the learn and changes nothing.

An External is free when no mapping or footswitch on the Global page drives it and the current preset does not use it. Mappings are global, so an External learned in another preset is taken: each pedal you learn gets an External of its own. When all eight are taken, Cathode says so and changes nothing.

> **Tip:** To make a knob follow a pedal that is already mapped (your main expression pedal on External 1, say), don't learn it again: choose **Add modifier…** and pick that External as the source.

## Footswitch layouts

Mappings work for a few switches. For a whole foot controller, use **footswitch layouts**: a layout describes every switch on the controller, what each does when tapped or held, and whether Cathode lights its LED.

### Creating a layout

1. In **Global ▸ Footswitches**, click **New layout**. It becomes the **Active layout**.
2. Click **Add switch** for each switch on your controller.
3. For each switch, fill in its row:

| Column             | What it is                                                                                                                                                                                                                                                                           |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Switch**         | A label for the switch.                                                                                                                                                                                                                                                              |
| **Sends**          | The message the switch sends: **cc**, **note** or **pc**, its number, and its MIDI channel (0 means any channel). Use the Monitor to find out what your switch sends.                                                                                                                |
| **Tap**            | The action for a press, picked from lists (see below).                                                                                                                                                                                                                               |
| **Hold**           | Optional: the action for holding the switch for half a second.                                                                                                                                                                                                                       |
| **LED**            | Whether Cathode lights the switch's LED.                                                                                                                                                                                                                                             |
| **Then layout**    | Optional: a layout to switch to after the tap action.                                                                                                                                                                                                                                |
| **In this preset** | Optional: a different tap action for this switch while the loaded preset is active. It is saved with the preset. Once it has one, two more fields give it a hold action of its own (**(none)** for none, whatever the layout holds) and a label of its own (empty for the layout's). |

The bin at the end of a row removes the switch. A new switch starts as CC 20 upwards on channel 1, selecting the scene of its number.

<!-- screenshot wanted: Global page, Footswitches section: a layout with two views and four switches filled in (Sends, Tap, Hold, LED, Then layout) -->

### Choosing actions

Each action is picked from lists, as for MIDI learn: first the action, from the names in the [table above](#actions), then, when it needs them, its target and its value. **Hold** and **In this preset** also offer **(none)**. For example:

| You pick                     | It does                                     |
| ---------------------------- | ------------------------------------------- |
| `scene`, Scene 1             | Scene 1                                     |
| `sceneNext`                  | The next scene                              |
| `bypass`, Delay (delay1)     | Toggles the block `delay1`                  |
| `channel`, Amp (amp1), B     | Switches `amp1` to channel B                |
| `presetSlot`, a bank, a slot | Loads that slot (the list shows its preset) |
| `looper`, record             | Looper: record                              |
| `layout`, a layout           | Makes that layout active                    |
| `view`, a view               | Shows that view of this switch's layout     |
| `tuner`                      | Tuner on and off                            |
| `presetNext`                 | The next preset                             |

A newly picked action starts on its first target and value (Scene 1, the first block, Slot 1), so it does something straight away.

### Tap and hold

A switch **without** a hold action acts as soon as you press it. A switch **with** a hold action waits: release it within half a second for the tap action, or keep it down for half a second for the hold action (then nothing happens on release).

Example: tap for `scene`, Scene 1; hold for `tuner`.

### LEDs

When **LED** is on, Cathode sends the switch's LED state back to the controller as a CC (or a note, for a note switch) on the switch's number and channel: 127 for on, 0 for off. Whether the LED lights depends on your controller accepting that message. The LED is on when the switch's tap action is in effect:

- `scene`: that scene is active;
- `bypass`: that block is engaged;
- `tuner`, `metronome`, `mute`: on;
- `looper record`, `looper play`, `looper overdub`: the looper is recording, playing, or overdubbing.

### Views

A layout can have several **views**, pages of switches for the same controller: one view for scenes, another for turning effects on and off, another for the looper. Each switch belongs to one view.

1. Click **View** (with the +) to add a view. Click a view's button to edit its switches.
2. Click **Make active** to make the view you are editing the one in use.
3. Give a switch the action `viewNext` (or `view 2` for the second view) to change views from the floor.

The view in use is marked **(active)**. Only the switches of the active view respond.

### Several layouts

Make more layouts with **New layout**, and choose which one is in use with **Active layout**. To jump between layouts from the floor, give a switch a **Then layout**: after its own action, that layout becomes active, on its first view.

## Scene MIDI

Each scene can send up to eight MIDI messages to other gear when it is selected, or when its preset loads on it: switch a synth's patch, a light controller, or a second effects unit along with your scenes.

1. Open **Controllers** and select the scene.
2. In **Scene N MIDI**, click **Add message**.
3. Choose **Program** or **CC**, the **Channel** (1 to 16), the **Number**, and for a CC its **Value**.

The messages go to the output chosen in **Global ▸ MIDI ▸ Send to** (every MIDI output Cathode finds, by default), and they are saved with the preset.

## MIDI clock

To follow the tempo of a drum machine, sequencer or DAW, set **Global ▸ Tempo and metronome ▸ Tempo from** to **MIDI clock** and send MIDI clock to Cathode.
