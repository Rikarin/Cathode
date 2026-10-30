The grid is where you build a preset's signal chain. You place blocks in its cells and join them with cables, and the signal flows from left to right through them, from the Input to the Output. This chapter covers how the grid works, adding and arranging blocks, cables and shunts, parallel paths, mixers and feedback loops, stereo, and what a bypassed block does.

![The grid: Input, Drive, Amp, Cab and Reverb in a row to the Output, with the Amp selected](screenshots/grid.webp)

## How the grid works

The grid has **6 rows** and **14 columns**, like the Axe-Fx III's. Each cell holds one block, a shunt, or nothing.

- **Signal flows left to right.** A cable always runs from a block to a block in a column further right. There is no way to send a signal back to the left, except through a [feedback loop](#feedback-loops).
- **Rows are just places.** A block on row 1 and a block on row 5 behave the same. Rows let you lay out parallel paths side by side.
- **The Input block** is where your guitar enters. **The Output block** (Output 1) is where the sound leaves for the interface. Neither can be bypassed.
- A block that is not connected to anything on the path from an Input to an Output is not heard.

A new preset has an **Input** in the first column of row 3, an **Output** in the last column, and a line of **shunts** joining them. A shunt is a plain connector: it passes the signal through and does nothing else. Placing a block on a shunt replaces the shunt and keeps its cables, so the block is wired in straight away.

### Block limits

Each block type has a maximum number per preset. For example, a preset can hold up to four Drives and four Delays, two Amps, two Cabs and two Reverbs, but one Looper and one Synth. When you reach the limit, the block picker greys the type out and marks it **max** with the number.

A preset can have up to six **Input** blocks (each reads one of the interface's input sources) and one each of **Output 1** to **Output 4** (each plays on its own pair of interface channels; see [Routing inputs and outputs](/manual/audio-setup#routing-inputs-and-outputs)).

## Selecting and moving around

- **Click** a cell to select it. Selecting a block opens its page in the drawer below the grid. Selecting an empty cell or a shunt closes it.
- With the grid focused (click it once), the **arrow keys** move the selection from cell to cell.
- The selected block has an outline around its tile.

## Adding a block

Any of these opens the **block picker** on the selected cell, if it is empty or holds a shunt:

- **double-click** the cell;
- press **↵** (Return);
- press **Q**;
- **right-click** it and choose **Add block…**.

![The block picker: a search field over one grid of every block type, each tile in its category's colour](screenshots/effects.webp)

The picker shows every block type as a coloured tile, in signal-chain order (drives and amps first, then cabs, filters, modulation, delays, reverbs, and the routing and utility blocks). Hover a tile to see its name and category.

1. Start typing to filter by name, category or type: `rev`, `delay`, `mod`.
2. The best match is highlighted. Use the **arrow keys** to move between tiles.
3. Press **↵** or click a tile to add it. **Esc** closes the picker.

The new block is selected, so its page opens below the grid.

## Replacing a block

To swap a block for another type while keeping its place and its cables:

1. Select the block.
2. Press **Q**, or right-click it and choose **Replace with…**.
3. Pick the new type. The picker's title says what it replaces.

The Input and Output cannot be replaced.

## Moving, copying and swapping

Drag a block by its tile:

| Drag onto                | Plain drag                                           | With ⌥ (Alt)         | With ⌘ (Ctrl)                                                             |
| ------------------------ | ---------------------------------------------------- | -------------------- | ------------------------------------------------------------------------- |
| An empty cell or a shunt | Moves the block there                                | Places a copy there  | Moves the block there                                                     |
| Another block            | Swaps the two blocks (each takes the other's cables) | Swaps the two blocks | Replaces that block with the dragged one, which takes its cell and cables |

When a block moves out of a signal path, it leaves a shunt behind so the path stays whole. Dropped onto a shunt, it takes the shunt's cables. Any of its own cables that no longer join neighbouring columns are removed, so check the wiring after a move.

The Input and Output can be moved, but not copied or used to replace another block.

### Cut, copy and paste

| Command                           | Keys | What it does                                                                                      |
| --------------------------------- | ---- | ------------------------------------------------------------------------------------------------- |
| Cut                               | ⌘X   | Copies the selected block and removes it.                                                         |
| Copy                              | ⌘C   | Copies the selected block: its type, all four channels and any IR or capture it uses.             |
| Paste on an empty cell or a shunt | ⌘V   | Places a copy of the block.                                                                       |
| Paste on a block of the same type | ⌘V   | Copies the settings across, all channels (the menu calls this **Paste settings (all channels)**). |

The clipboard lives in the editor and works across presets: copy a delay you like, load another preset, and paste it there.

### Removing

Select a block and press **Delete** or **⌫** (Backspace), or right-click it and choose **Remove**. A block in a signal path leaves a shunt behind, so the path stays connected.

> **Warning:** You can remove the Input or the Output too. Without them the preset is silent. Undo (⌘Z) brings them back.

### Moving whole rows and columns

Right-click a block and choose **Move row or column**, then **Row up**, **Row down**, **Column left** or **Column right**. The whole row or column moves one step, and the ones in between shift to make room. Right-clicking an empty cell offers **Move row up** and **Move row down**.

A row keeps all its cables. A column move rewires so that every signal path stays connected, adding shunts where it needs to.

## Cables

Every block (except an Output) has a small round **outlet** at the middle of its right edge.

1. Press on the outlet and drag. A dashed cable follows the pointer.
2. While you drag, every block you can connect to is outlined with a dashed line. The one under the pointer is outlined solid.
3. Release over it.

The rules:

- A cable runs to a block in a **later column**.
- Nothing enters an Input block, and nothing leaves an Output block.
- To reach a block more than one column away, the cells in between must be empty or hold shunts, on the source's row or the target's. Cathode fills them with shunts for you.

To remove a cable, click it (it turns to the highlight colour), then press **Delete** or **⌫**. To remove all of a block's cables at once, select it and press **⌘D**, or right-click it and choose **Disconnect all cables**.

### Shunts

Select an empty cell and press **⌘U**, or right-click it and choose **Add shunt**, to place a shunt by hand. Cable into and out of it like any block. A shunt is drawn as a plain wire across its cell, like a cable; its cell's outline shows only when you hover over it or select it. Delete one like a block.

## Parallel paths

A path splits wherever one block has cables to more than one block, and joins wherever one block receives cables from more than one.

**Splitting.** Drag a second cable from the same outlet to a block on another row. Both paths get the same signal.

**Joining.** Cable both paths into the same block in a later column. What arrives is added together. Three blocks give you more control over the join:

- **[Mixer](/manual/blocks/mixer):** mixes up to six rows, each with its own level and balance, into stereo or mono.
- **[Multiplexer](/manual/blocks/mux):** picks one of the rows cabled into it (or all of them), with a short crossfade. Put it on a scene or a footswitch to switch between two amps, or a clean and a dirty chain.
- **Output:** the Output block is a mixer too, with a level and balance for each row, then a main level and balance.

Cathode lines up parallel paths in time: if one path holds a block that adds latency, the others are delayed to match, so the paths join without comb filtering.

### Example: a wet/dry/wet rig

A common way to keep delays and reverbs from muddying the amp:

1. Row 3: Input, Drive, Amp, Cab, then a shunt to a Mixer near the right.
2. From the Cab's outlet, drag a cable to a Delay on row 2 and another to a Reverb on row 4.
3. Cable the Delay and the Reverb into the Mixer as well, then the Mixer into the Output.
4. Set the Delay and Reverb **Mix** to 100 % (only the effect), and balance the three rows on the Mixer.

The dry signal stays untouched in the middle, with the effects beside it. [Building a signal chain](/manual/signal-chain) has more layouts.

## Feedback loops

To send a signal back to an earlier column, use a pair of blocks:

1. Place a **[Feedback Send](/manual/blocks/fbsend)** where you want to pick the signal up, and cable into it. It has no outlet.
2. Place a **[Feedback Return](/manual/blocks/fbreturn)** in an earlier column, where you want the signal to come back in, and cable it onwards. Whatever reaches the send is added to the return's own input.

A preset can have two pairs; each send feeds the return with the same number (`fbsend2` feeds `fbreturn2`). The loop delays the signal by one engine block, as on the hardware. The returned signal passes a soft limiter at +18 dBFS, so a loop with too much gain saturates instead of running away.

Use it for a delay that feeds a reverb that feeds back into the delay, or for any effect you want to repeat through another.

## Stereo and mono

Every cable carries stereo. Some blocks make a stereo image (stereo delays, reverbs, chorus, flanger, phaser, rotary, tremolo and panner, pitch, enhancer). Others sum their input to mono: the **Amp** and **Amp NAM** blocks always, and a Cab, IR Player or Volume/Pan whose input is set to one side or the sum, or a Dynamic Distortion or Mixer in mono mode.

If an engaged stereo effect sits **before** a block that sums to mono, its width is thrown away. The grid marks such a block with an amber **MONO** tag; hover it to see which effects lose their stereo. Move the stereo effects after it to keep their width.

## Bypassing blocks

Select a block and press **Space**, or use its **Engaged** switch in the drawer, or right-click it and choose **Bypass** or **Engage**. A bypassed block turns grey on the grid. In the bypass modes that let the signal through (**Thru**, **Mute FX In** and **Mute FX Out**), the cable runs on through the block and arches over its icon, so you can see the signal skipping it; the muting modes show just the grey tile.

Bypass state belongs to the scene: bypassing a block changes it in the current scene only. To set it in every scene, right-click the block and choose **Engage in all scenes** (⇧Space) or **Bypass in all scenes** (⇧⌘Space). See [Scenes](/manual/presets-and-scenes#scenes).

### Bypass modes

Each block's **Bypass mode** (in the left column of its page) decides what happens to the signal while it is bypassed:

| Mode                       | When bypassed                                                                                                                  |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **Thru**                   | The input passes straight through, as if the block were not there. The default.                                                |
| **Mute**                   | Silence. Useful on a parallel row you want to drop out completely.                                                             |
| **Mute In**                | The block's input is muted; only its tail (repeats, reverb decay) comes out, and no dry signal.                                |
| **Mute FX In (spillover)** | The dry signal passes and the block's tail rings out. Use it on delays and reverbs so a scene change does not cut their tails. |
| **Mute Out**               | The block keeps running on its input, but its output is muted, so its tail is already there when you engage it. No dry signal. |
| **Mute FX Out**            | As Mute Out, with the dry signal passing in its place.                                                                         |

> **Tip:** For a delay or reverb on the main path, choose **Mute FX In (spillover)**. For one on its own parallel row at 100 % mix, choose **Mute In**, so bypassing it does not add a second copy of the dry signal.

## Global blocks

A block can be shared by several presets, so that changing it in one changes it everywhere: press **G** with the block selected, or right-click it and choose **Make global**. To link a block in another preset to it, right-click a block of the same type there and choose **Link to global**. See [Global blocks](/manual/library#global-blocks).

## Dropping files on the grid

Drag a `.cthblock` file from your computer onto a cell to import it into the Block Library and use it there: on an empty cell it adds a new block with those settings; on a block of the same type it recalls them into all its channels.

## The right-click menu at a glance

On a block:

- **Bypass** or **Engage** (Space)
- **Channel A** to **D** (A to D)
- **Engage in all scenes** (⇧Space), **Bypass in all scenes** (⇧⌘Space), **Channel in all scenes**
- **Cut** (⌘X), **Copy** (⌘C), **Paste** or **Paste settings (all channels)** (⌘V)
- **Replace with…** (Q)
- **Disconnect all cables** (⌘D)
- **Save to the Library…** (⌘B)
- **Make global** (G), or **Unlink from global**; **Link to global** when there are global blocks of its type
- **Move row or column**
- **Remove** (⌫)

On an empty cell: **Add block…** (↵), **Add shunt** (⌘U), **Paste** (⌘V), **Move row up**, **Move row down**.
