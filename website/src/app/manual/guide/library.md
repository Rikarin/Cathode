The Library is where everything you keep lives: your presets and the factory bank, banks for organising them, the Block Library of saved block settings, global blocks shared between presets, and the NAM amp captures and IR cabinet files your blocks use. Open it with **Library** in the navigation bar. The buttons at the top switch between its sections: **Presets**, **Block Library**, **Global blocks**, **NAM amps** and **IR cabs**.

![The Library's Presets section: presets with favourites, colour tags, author and date, and a search field](screenshots/library.webp)

## Presets

The **Presets** section lists every preset: yours and the factory bank's.

- **All**, **Mine** and **Factory** at the top right choose which presets to list.
- The **search** field matches the name, author, tags and description.
- Favourites come first, then your presets by name, then the factory bank in its own order.
- Click the **star** to make a preset a favourite. A coloured dot before the name is its colour tag. Factory presets carry a **Factory** tag.

Click a preset to select it. The bar at the bottom shows its name and description, and three buttons:

- **Load** (or double-click the preset, or press **↵**): opens it in the editor. If your working copy has unsaved changes, Cathode asks first.
- **Export**: saves it as a `.cthpreset` file (see [below](#exporting-and-importing-presets)).
- **Delete**: removes one of your presets, after a confirmation. Factory presets cannot be deleted.

> **Note:** A preset that sits in a bank (or a setlist) cannot be deleted from here, so that no program change or setlist entry is left pointing at nothing. Instead of asking to delete it, Cathode lists where it is used, for example "bank “Gig”, slot 3", and **Manage presets** takes you to its banks. Remove it from them there first, or delete it there.

At the top right, **Import…** adds a `.cthpreset` file, and **Export list…** saves a text file listing your presets.

### The factory bank

Cathode comes with **70 factory presets** that recreate well-known guitar tones, from blues and classic rock to modern metal, jazz and shoegaze. Each has several scenes and a description, and they are levelled to play at about the same loudness. Driven presets have a noise gate; clean ones have a compressor to catch a hot pickup's peaks.

Factory presets are **read-only**. You can load them, change them and play them, but saving always stores your own copy under a new name. They are the quickest way to learn how a sound is put together: load one, open each block, and see what it does.

> **Note:** Factory presets with a wah or a whammy rest at the heel until an expression pedal drives **External 1**. See [Expression pedals](/manual/midi-and-footswitches#expression-pedals).

### Colours and favourites

Favourites and colour tags help you find presets fast. Set a favourite with the star in the Library or in the preset picker. Set colours in the preset picker (each entry has a colour menu) or on the Manage presets page. The colours are Red, Orange, Yellow, Green, Blue and Purple. Factory presets can take favourites and colours too.

## Exporting and importing presets

A **`.cthpreset`** file is one preset, packaged with every IR and NAM capture it uses, ready to back up or give to someone else.

**To export:** in **Library ▸ Presets**, select the preset and click **Export**. Your browser or the app saves the file.

**To import:** click **Import…** and choose a `.cthpreset` file. The preset is added to your presets, and any IRs and captures it carries are added to the NAM amps and IR cabs sections (files you already have are not duplicated). An import never overwrites a preset you have: if the same preset already exists, the imported one is added as a new preset beside it. An exported factory preset comes back as your own.

> **Note:** A preset saved by a newer version of Cathode than yours is refused, with a message that says so. Update Cathode to load it. Presets from older versions always load.

## Banks and Manage presets

A **bank** is an ordered list of up to **128 slots**, each holding a preset. Banks are how you put presets in the order you want, for a set list or for a MIDI controller that selects presets by number. A preset can be in several banks, and a bank can leave slots empty. The factory bank is the first bank, and it is read-only.

To organise banks, choose **Preset ▸ Manage presets…** in the preset bar. The page has the list of **Banks** and your **Library** of presets on the left, and the slots of the selected bank on the right.

<!-- screenshot wanted: the Manage presets page: the Banks list, the Library list, and a bank's slots with a few selected, coloured and favourited -->

- **New bank:** the **+** beside **Banks**. Rename a bank by double-clicking it, or right-click it for **Rename…** and **Delete…**.
- **Put a preset in a slot:** drag it from the **Library** list onto a slot.
- **Rearrange:** drag a slot onto another to swap them. Hold **⌘** while dragging to move instead, **⇧** to copy. Drop onto a bank in the list to put it in that bank's first empty slot.
- **Select slots:** click one; **⇧-click** to select a range; **⌘-click** to add or remove one.
- With slots selected: **Rename…** (one preset), **Duplicate**, **Colour**, **Favourite** and **Delete**.
- **Reset Amp blocks…**: puts every Amp setting of the selected presets back to its default, except each amp's own front-panel knobs and switches. Useful after changing your speakers or your guitar, when the expert settings you tuned before no longer fit.

Your changes to slots, names, colours and favourites are held, and marked with a dot, until you click **Save**. **Close** goes back to the editor and discards anything not saved. Creating, renaming and deleting **banks** happens at once, and **Reset Amp blocks** changes the stored presets at once after a confirmation.

From the factory bank you can drag presets into your own banks. **⇧-drag** makes a copy you can change.

> **Note:** Deleting a bank does not delete its presets. A MIDI mapping that loads presets from that bank stops working.

### Setlists

A setlist puts presets in the order of a gig, each song on the scene it starts on. Setlists have a page of their own, **Setlists** in the navigation bar, where you make, rename, duplicate and delete them and fill them from the library; the Perform page plays them. See [Preparing a set](/manual/live-performance#preparing-a-set).

## The Block Library

The **Block Library** keeps settings for a single block, so you can reuse a drive, a delay or an amp channel in any preset. Cathode includes factory entries for many block types, and you add your own.

### Saving a block

1. Select the block on the grid.
2. Press **⌘B**, or right-click it and choose **Save to the Library…**, or use **Save to the Block Library…** in the **⋯** menu of its page.
3. Type a **Name**, and optionally a **Folder** (pick an existing one or type a new name).
4. For a block with several channels, choose what to save: **Channel X** (only the current channel), or **Whole block (all channels, modifiers)**.
5. Click **Save**.

**Save all blocks of this preset**, at the top of the Block Library section, saves every block of the loaded preset at once.

### Recalling a block

1. Select a block of the same type.
2. In the left column of its page, open **Block Library** and pick an entry. Your own entries are marked "(mine)".
3. To fill all of the block's channels from a whole-block entry, turn on **Into all channels** first.

### Organising and sharing

The Block Library section lists your own entries in folders, with their block type and whether each is a **Channel** or a **Whole block**. **Move…** puts an entry in another folder (or a new one), and the bin deletes it.

Tick entries and click **Export** to save them as a **`.cthblock`** file. **Import…** adds `.cthblock` files, and you can also drop them onto the section, or onto a grid cell to use them there straight away.

## Global blocks

A **global block** is one block's settings shared by every preset linked to it. Edit it in any of those presets and all of them follow, including other blocks linked to it in the same preset. Use it for things that should be the same everywhere: your main amp's speaker settings, the cab, or the reverb you use in every song.

**To make one:** select the block and press **G**, or right-click it and choose **Make global** (also in the **⋯** menu of its page). It is named after its type and id, for example "Reverb (reverb1)". The block's page then shows a **Global** tag with its name, and Block Info (⌥ or F1) marks its tile **GLOBAL**.

**To link another block to it:** in another preset, right-click a block of the same type and choose **Link to global**, then the global block's name. The **⋯** menu of the block's page offers the same as **Use "…"**. The block takes the global block's settings.

**To unlink:** right-click the block and choose **Unlink from global**. It keeps the settings it had, as its own.

The **Global blocks** section of the Library lists them all, with their type and which block uses each in the loaded preset. Rename one by editing its name (press **↵** or click away to save), or delete it. Deleting a global block never changes a sound: the blocks linked to it keep its settings as their own.

## NAM amps and IR cabs

These two sections hold the files your blocks load:

- **NAM amps:** neural amp captures, `.nam` (Neural Amp Modeler) and `.aidax` (AIDA-X) files, for the [Amp NAM](/manual/blocks/nam) block.
- **IR cabs:** impulse responses, `.wav` files and `.cabgrid` packs, for the [Cab](/manual/blocks/cab) and [IR Player](/manual/blocks/irplayer) blocks.

**To add files:** click **Upload…**, or drop files anywhere on the list. A file you already have is stored only once. You can also upload from a block's page with its **Upload…** button.

Each list shows the **Name**, **Type**, which stored presets use it (**Used in**), the **Size** and when it was **Added**. Search it with the field at the top, and click **Favourites** to list favourites only. Select one or more files to **Rename…**, **Colour** or **Delete** them. Before deleting, Cathode tells you which stored presets use the files.

In the IR cabs section, **IR tools** has three more tools:

- **Import settings…**: how `.wav` files are processed as you upload them (trimming or minimum phase, polarity, which channel of a stereo file, a maximum length).
- **Mix IRs…**: blends several IRs into a new one, with a level, delay and polarity for each.
- **Capture an IR…** (desktop app only): measures your own cabinet and microphone with a sine sweep.

[Neural captures and impulse responses](/manual/nam-and-irs) explains all of these in depth.

## Where the Library is stored

Everything in the Library is kept in Cathode's data folder (see [Where things are kept](/manual/the-editor#where-things-are-kept)). To back up your whole Library, copy that folder while Cathode is closed. To move a few presets to another computer, export them as `.cthpreset` files.
