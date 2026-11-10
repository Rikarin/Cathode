A **modifier** makes a knob move by itself, following a **controller**: an LFO that sweeps back and forth, an envelope that follows how hard you play, an expression pedal under your foot, a value that changes with the scene. This chapter covers adding and editing modifiers, every controller Cathode has, and the tempo that LFOs and delays can follow.

![The modifier dialog on a compressor's Threshold: LFO 1 as the source, its minimum, maximum, attack and release, the channels it applies on, and an exponential curve](screenshots/modifiers.webp)

## How a modifier works

A modifier connects a **source** (a controller), or two sources combined, to one **parameter** (a knob). Every source produces a value between 0 and 1. The modifier turns that into a knob position:

1. It multiplies each source by its **scale** (100 % unless you change it). With a **Second source**, it then adds the two values (the sum stops at 1), multiplies them, or subtracts the second from the first (the result stops at 0).
2. It applies the **response**: Start, Mid, End, Slope, Scale and Offset (see [below](#the-response)). At their defaults they change nothing.
3. It bends the result through a **curve**.
4. It scales that between the **Min** and **Max** you set, in the knob's own units (dB, Hz, %).
5. It smooths the movement with **Attack** (how fast it rises) and **Release** (how fast it falls).

While the modifier runs, it decides the knob's value; the value you set by hand is kept for when the modifier is removed or does not apply. Set **Min** higher than **Max** to make the knob move the opposite way to the source: for example, a volume that falls as a pedal goes down.

Modifiers work on continuous knobs. Switches and choices cannot take one, with one exception: a block's engage switch (see [below](#engaging-a-block-from-a-modifier)). A knob takes one modifier, and a preset holds up to 256 of them.

## Adding a modifier

1. Right-click a knob on a block's page and choose **Add modifier…**.
2. In the dialog, choose the **Source**. To drive the knob from two controllers, choose a **Second source** too and whether the two **Add**, **Multiply** or **Subtract**.
3. Set **Min** and **Max**: the knob's value when the source is at 0 and at 1. They start at the knob's full range.
4. Set **Attack** and **Release** (0 to 2000 ms) if the movement should be smoothed. Leave them at 0 for a modifier that follows the source exactly.
5. Under **Applies on**, keep **All channels**, or pick one channel (**A** to **D**) so the modifier only acts while the block is on that channel. On the other channels the knob keeps that channel's own value.
6. Choose a **Curve**. The chart shows how the source maps to the value.
7. Leave **Update rate** on **Fast** unless the modifier costs too much CPU (see [below](#update-rate)).
8. Click **Save**.

A small dot appears beside the knob, and the knob now turns with the value it is getting. A mark on its track shows the value you set by hand. Hover the dot to see the source.

To change or remove a modifier, right-click the knob again and choose **Edit modifier…** or **Remove modifier**, or use **Remove** in the dialog.

### Curves

| Curve       | Shape                                          | Good for                                                                       |
| ----------- | ---------------------------------------------- | ------------------------------------------------------------------------------ |
| **Linear**  | Straight: the value follows the source evenly. | Most things.                                                                   |
| **Exp**     | Slow at first, fast at the end.                | Volume swells, where most of the change should happen at the top of the pedal. |
| **Log**     | Fast at first, slow at the end.                | Making the first part of a pedal's travel do most of the work.                 |
| **S-curve** | Slow at both ends, fast in the middle.         | Smooth sweeps without abrupt starts and stops.                                 |

Knobs with a logarithmic scale (frequencies, for example) are swept on that scale, so an LFO on a filter's frequency sounds even across its range.

### Two sources

With a **Second source**, the knob follows both controllers:

- **Add**: the two values are summed, and the sum stops at 100 %. An LFO added to a pedal wobbles around wherever the pedal is.
- **Multiply**: one value scales the other. A pedal multiplied with an LFO sets how deep the LFO goes: at the heel the knob rests at Min, at the toe the LFO sweeps the whole range.
- **Subtract**: the second value is taken from the first, and the result stops at 0. A pedal less an envelope follower backs the knob off as you play harder.

Each source has a **scale** beside it, 0 to 100 %, which its value is multiplied by before the two are combined. **Source scale** at 50 % makes a pedal reach only half way; **Source 2 scale** at 20 % with Add lays a shallow LFO wobble over a pedal's full travel. The first one works with a single source too.

### The response

Six knobs between the sources and the range shape how the knob answers the source. All are in percent, and at their defaults the source passes through unchanged.

| Setting    | Default | What it does                                                                                                                                           |
| ---------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Start**  | 0 %     | The output with the source at rest (0).                                                                                                                |
| **Mid**    | 50 %    | The output with the source half way.                                                                                                                   |
| **End**    | 100 %   | The output with the source at full travel (1). Between the three points the output moves in straight lines.                                            |
| **Slope**  | 50 %    | Bends the source around its centre before Start, Mid and End: above 50 % the middle of the travel does more and the ends less, below 50 % the reverse. |
| **Scale**  | 100 %   | Multiplies the output, up to 1000 %. The result stops at 100 %, so 200 % reaches the top at half travel.                                               |
| **Offset** | 0 %     | Added to the output after Scale, −100 to 100 %. The result stays within 0 to 100 %.                                                                    |

Some uses: Start 100 % and End 0 % turn a pedal round. Mid 0 % keeps the knob at Min for the first half of the travel. Scale 50 % with Offset 25 % makes the source move only the middle half of the range. The chart in the dialog shows the result as you turn them.

### Update rate

**Update rate** sets how often the modifier writes the knob: **Fast** (every 64 samples or less, about 750 times a second at 48 kHz), **Medium** (at most every 5 ms) or **Slow** (at most every 20 ms). Some knobs make their block do real work each time they change (a filter recalculates, an amp's circuit is solved again), so a slower rate lowers the CPU a modulated block takes. The price is a coarser movement: if you hear steps ("zipper noise") in a sweep, go back to Fast. Attack and Release are timed the same on every rate. An engage modifier has no update rate.

### After a preset load (PC Reset)

An expression pedal keeps its position when you change presets, so a modifier on **External 1** to **16** normally puts its knob where the pedal is the moment the preset loads. For a modifier with an External source the dialog has **After a preset load**:

- **Follow the controller** (the default): the knob follows the pedal's last position at once.
- **Hold the stored value until it moves**: the preset loads with the knob at the value saved in it (and, for a modifier on the engage switch, with the block on or off as the scene stores it). The modifier takes over the moment the pedal, or whatever drives that External, sends a new value.

Use it for a pedal you share between presets: a volume pedal left at the heel does not load the next preset silent. The hold starts only when a preset loads; editing the preset does not start or end it. The setting does nothing for the other sources, which do not keep a position between presets.

### The list of modifiers

**Controllers ▸ Modifiers**, at the bottom of the Controllers page, lists every modifier in the preset by block and parameter, with its source and the channels it applies on. Change **Applies on** there too.

## Engaging a block from a modifier

A modifier can switch a whole block on and off:

1. On the block's page, right-click the **Engaged** switch in the left column.
2. Choose **Engage from a modifier…**, pick a source, and set how it engages the block (below).
3. Click **Save**.

The value the switch looks at is the modifier's output, from 0 to 100 %: the source after the response, the curve, Min, Max, Attack and Release. Below the curve, the dialog has four more settings:

| Setting        | What it does                                                                                                                                                                                                                                                                            |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Engages by** | **Position**: the block is on while the value is at the threshold or above it, and off below it. **Moving**: the block comes on the moment the value moves away from where it was resting, wherever that is, and goes off once it is back below the threshold.                          |
| **Off**        | Which side of the threshold is off. **Below the threshold** (the default) is what the rest of this section describes. **Above the threshold** turns everything round: the block is on at the threshold and below it and goes off above it, for a block that should drop out at the toe. |
| **Threshold**  | Where "off" begins, 0 to 100 % (50 % unless you change it). In Position mode it is the switch-over point. In Moving mode it marks the resting end of the travel, such as a pedal's heel.                                                                                                |
| **Off delay**  | 0 to 5000 ms (0 unless you change it). The block goes off only after the value has stayed below the threshold for this long without a break; coming back above the threshold starts the wait again. Engaging never waits.                                                               |

The chart marks the threshold with a dashed line, and a note under the switch on the block's page sums the settings up.

A few details keep the switch steady:

- With an off delay, or in Moving mode, the value has to drop a little (2 %) below the threshold before the wait starts, and the wait only resets once the value is back at the threshold. A pedal resting right on the threshold cannot flick the block on and off.
- In Moving mode a move counts once the value has gone more than 3 % from where it rested, so the jitter of a pedal lying still does not engage it. A value that keeps moving below the threshold (your foot rocking near the heel) keeps the block on, since each move starts the off delay again. Without an off delay, moves below the threshold do nothing, and only a move above it engages the block.
- When a preset loads, or a channel-scoped engage modifier starts to apply, the first value only tells the switch where the source is. Position mode takes the state that value gives, without waiting for the off delay. Moving mode starts off, even with the pedal halfway up, and comes on at the first move.

With **Position**, **50 %** and **0 ms**, the settings a new engage modifier starts with, the block simply switches at the halfway point, as it did before these settings existed. While a modifier engages the block, it wins over the scene's bypass state: a scene cannot turn on a block the modifier has off, or the other way round.

### Recipe: a wah that comes on when you touch the pedal

Put an expression pedal (for example **External 1**) on the wah's position, then make the same pedal engage the wah:

1. Right-click the wah's **Engaged** switch, choose **Engage from a modifier…** and pick **External 1**. Leave Min and Max at their full range.
2. Set **Engages by** to **Moving**.
3. Set **Threshold** just above where the pedal rests at the heel, such as 5 %.
4. Set **Off delay** to around 300 ms, so the wah stays on while your foot rocks near the heel and only goes off once the pedal has been back there for a moment.

Touch the pedal and the wah is on, wherever the pedal is. Park it at the heel and the wah drops out a moment later. Prefer the wah to come on only when the pedal goes past a point? Keep **Position** and set the threshold to that point, with an off delay of 100 to 200 ms so the wah does not stutter as you sweep through it.

## The controllers

Open **Controllers** in the navigation bar to set them up. Their settings are stored with the preset, except **Manual A to E**, which are global.

<!-- screenshot wanted: the Controllers page: scene level and controllers, Manual A-E, LFO 1 and 2, ADSR 1 and 2 and the followers side by side, the sequencer -->

### Scene controllers

The **Scene** section at the top shows the active scene's **Scene Level** and **Controller 1** to **4** (0 to 100 %). Each scene has its own values.

Use a scene controller as a modifier source when a knob should sit at a different position in each scene: assign **Scene Controller 1** to the delay's mix with Min 0 % and Max 50 %, then set Controller 1 to 20 % in the verse scene and 60 % in the solo scene. Unlike a channel, this needs no copy of the whole block, and the change glides with the modifier's Attack and Release.

### Manual A to E

Five global knobs (0 to 100 %) on the Controllers page, the same in every preset. A MIDI controller or footswitch can set them too (the **manual** action). Use them for anything you want to control by hand the same way everywhere.

### External 1 to 16

The sources an expression pedal or a MIDI CC drives (see [Expression pedals](/manual/midi-and-footswitches#expression-pedals)). **External (live)** on the Controllers page has a knob for each, so you can move a wah, a whammy or any External modifier without a pedal. These are live values only: they are not saved, the knobs show what you last set here, and a pedal moved afterwards takes over.

### LFO 1 and LFO 2

Low-frequency oscillators: sources that cycle up and down on their own.

| Setting   | What it does                                                                                                                      |
| --------- | --------------------------------------------------------------------------------------------------------------------------------- |
| **Rate**  | Speed, 0.05 to 20 Hz. Used when Tempo is Off.                                                                                     |
| **Shape** | Sine, Triangle, Square, Saw Up, Saw Down, Random, Log, Exponential, Trapezoid or Astable. Random jumps to a new level each cycle. |
| **Tempo** | Off, or a note value (1/4, 1/8 dotted, 1/16 triplet…) that locks the cycle to the tempo.                                          |

A synced LFO keeps its phase with the tempo across preset changes, so it stays in time with the metronome and synced delays.

### ADSR 1 and ADSR 2

Envelope generators triggered by your playing. When the guitar's level rises above the **Threshold** (−80 to 0 dB), the ADSR rises over the **Attack** time, falls over the **Decay** time to the **Sustain** level (0 to 1), and holds there while you keep playing. When your level drops back below the threshold, it falls to zero over the **Release** time.

Use it for swells, or a filter that opens on each new phrase.

### Envelope follower

A source that follows the guitar's level as you play, from silence (0) to loud (1).

| Setting         | What it does                                                                                |
| --------------- | ------------------------------------------------------------------------------------------- |
| **Attack**      | How fast it rises when you play harder (0.1 to 500 ms).                                     |
| **Release**     | How fast it falls when you play softer (1 to 5000 ms).                                      |
| **Sensitivity** | Gain before the follower (0 to 40 dB). Raise it until your hardest playing reaches the top. |

Use it for an auto-wah (envelope on a filter's frequency), or for a delay that gets louder as you stop playing (envelope on the delay mix, with Min higher than Max).

### Pitch follower

A source that follows the pitch of the note you play. **Low** and **High** (in Hz) set the range: a note at Low gives 0, a note at High gives 1. The defaults, 82 Hz and 1320 Hz, cover a guitar in standard tuning from the low E to high up the neck.

### Sequencer

A step sequencer: a source that steps through 2 to 32 fixed levels in a loop.

1. Set **Stages** (how many steps).
2. Set **Rate** (0.1 to 20 Hz), or **Tempo** to a note value to step in time.
3. Drag each stage's slider to set its level.

**Rate** counts steps, not whole patterns: at 4 Hz the sequencer moves to the next stage four times a second, however many stages there are. With **Tempo** set, each stage lasts one note of that value, so eight stages at 1/8 make one bar of 4/4.

While audio is running, the stage playing is marked on the Controllers page: its bar takes a faint glow and a small light shows under it, so you can watch the pattern go round as you shape it. The marker stops when the audio does. The **LFO** and **ADSR** cards show their controller's current value in the same way, as a small bar beside the card's title.

Put it on a filter for a rhythmic, synth-like pattern, or on a volume for a gated chop.

### Mapping an External

Values from your MIDI controller: an expression pedal, a knob or a fader sending CC messages. You connect a CC to an External number on the **Global** page, and from then on any modifier with that External as its source follows it. See [Expression pedals](/manual/midi-and-footswitches#expression-pedals).

**Learn MIDI controller** in a knob's right-click menu does both steps at once: it picks a free External (one that no MIDI mapping or footswitch drives and this preset does not use), asks you to move a pedal or knob on your MIDI controller to bind it, and then gives the knob a modifier from that External over its whole range. Cancelled before anything is moved, it changes nothing.

## Tempo

Many things in Cathode can follow a tempo: synced LFOs, the sequencer, and the time controls of delays and other blocks that offer a tempo setting.

The current tempo is shown in the navigation bar, for example **120 BPM**.

- **Type it:** click the BPM and enter a value from 30 to 300.
- **Tap it:** click **TAP** in time, at least twice. A footswitch or MIDI message can tap too (the **tap** action).
- **Metronome:** the button beside TAP turns a click on and off. Set its level with **Click level** on the Global page. The click plays on Output 1 only.

**Global ▸ Tempo and metronome ▸ Tempo from** decides where the tempo comes from:

| Choice                   | Meaning                                                           |
| ------------------------ | ----------------------------------------------------------------- |
| **Preset** (the default) | Each preset has its own tempo, saved with it.                     |
| **Global**               | One tempo for every preset, set with the **Global tempo** knob.   |
| **MIDI clock**           | Cathode follows MIDI clock from a drum machine, sequencer or DAW. |

### Tempo divisions

The **Tempo** choices of the LFOs and the sequencer (and of delays and other blocks that sync) are note values: **1/1** (a whole note), **1/2**, **1/4** (one beat), **1/8**, **1/16**, **1/32**, **1/64**, their dotted versions (**1/4.**, a beat and a half) and triplets (**1/4T**, two thirds of a beat), and **2/1**, **3/1** and **4/1** for very slow cycles. **Off** uses the Rate in Hz instead.

## Recipes

**Expression pedal on volume.** Map your pedal to External 1 (see [MIDI and footswitches](/manual/midi-and-footswitches#expression-pedals)). Add a [Volume/Pan](/manual/blocks/volpan) block after the amp, and give its [Volume](/manual/blocks/volpan#knob-volume) a modifier from External 1, Min 0 %, Max 100 %. The block's own Taper setting shapes the pedal's response.

**Tremolo in time.** On the Volume/Pan block's [Level](/manual/blocks/volpan#knob-level), add a modifier from LFO 1 with Min −12 dB and Max 0 dB. Set LFO 1's Tempo to 1/8 and its Shape to Sine or Square.

**Auto-wah.** On a [Wah's Position](/manual/blocks/wah#knob-position) or a [Filter's Frequency](/manual/blocks/filter#knob-freq), add a modifier from the Envelope follower. Set Sensitivity so a hard pick reaches the top, with a short Attack and a longer Release.

**Ducked delay.** On the delay's [Mix](/manual/blocks/delay#knob-mix), add a modifier from the Envelope follower with Min 40 % and Max 5 %: the repeats drop while you play and come up when you stop. Give it a Release of a few hundred milliseconds so it swells back smoothly.
