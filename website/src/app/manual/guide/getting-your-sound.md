Good tones are rarely found by turning every knob at once. They come from a short, repeatable routine: get the level right, pick an amp that already sounds close, fit a cabinet to it, set the gain, then shape and season. This chapter walks through that routine from an empty preset (or a Factory preset) to a finished sound, then lists the problems players hit most often and what to turn to fix each one.

The chapters after this one go deeper into each step: [gain staging](/manual/gain-staging), [amps](/manual/choosing-an-amp), [cabs and mics](/manual/cabs-and-mics), [effect order](/manual/signal-chain), [style recipes](/manual/tone-recipes) and [captures and IRs](/manual/nam-and-irs).

## Before you start

Three things decide whether your tweaking means anything:

- **Your input level.** Set your interface gain so hard playing never lights the **IN** clip mark in the top bar, then calibrate Input 1 in **Settings ▸ Audio setup**. The amp and drive models read your guitar in volts from that calibration, so they respond as a real amp would to your pickups. See [Gain staging](/manual/gain-staging#calibrate-input-1).
- **What you listen on.** Studio monitors or a flat, full-range speaker (often called FRFR, "full range, flat response") at a sensible level, or good headphones. A guitar combo's own speaker colours everything you hear.
- **The volume you will play at.** Your ears hear bass and treble differently at different volumes (more on that [below](#it-sounds-different-at-low-and-high-volume)). Dial at roughly the level you will use.

> **Tip:** Use the guitar you will play the preset with. A bright single-coil and a hot humbucker want different amp settings, often very different gain.

## A method, step by step

### 1. Start from something close

You have two good starting points.

- **A Factory preset.** The Factory bank has 70 presets recreating well-known tones, each with scenes and a level matched to the rest of the bank. Find the one nearest to what you hear in your head in the [Library](/manual/library) and save your own copy (factory presets are read-only, so saving one stores a copy). [Tone recipes](/manual/tone-recipes) lists which Factory preset fits each style.
- **An empty preset.** Press **New preset**, then double-click an empty cell on the grid to add a block. An Amp and a Cab between the Input and the Output make a complete rig. Keep it that simple until the core sound is right.

### 2. Choose the amp

Select the Amp block and pick a **Type**. Choose by the family that suits the music, not by the most famous name: a Fender-style clean platform, a Marshall-style crunch, a Mesa or 5150-style high-gain amp. [Choosing and dialling an amp](/manual/choosing-an-amp) describes each family.

Start with the tone controls at noon (5 on a 0 to 10 knob) and the gain where the amp just begins to break up when you dig in. Then play the riff or chords you actually play.

> **Note:** Changing an amp's Type puts every control back to that model's defaults, except **Level**, **Balance** and **Input Select**. Try types first and fine-tune after.

### 3. Fit the cabinet

The cabinet and microphone shape the sound more than any EQ knob. In the Cab block, pick a cabinet and speaker that suit the amp (a closed 4x12 with Greenbacks or Vintage 30s for a Marshall, an open 1x12 or 2x12 with a Jensen or JBL for a Fender), keep one SM57 near the edge of the dust cap at about an inch from the grille, and listen.

On the Amp's **Speaker** page, set **Speaker load** to **Follow Cab**: the amp's power section then sees the electrical load of the speaker and cabinet you chose, as a real amp would. See [Cabs, speakers and microphones](/manual/cabs-and-mics).

### 4. Set the gain

Set the gain for the part you play, not for the sound of a single chord. Most players use more gain than they need: a riff that sounds huge on its own turns to mush in a mix. A good test: lower the gain until the sound starts to feel weak, then add a little back.

On a master-volume amp, the **Master** sets how hard the power section works. Higher values add the compression and harmonics of power-amp breakup; lower values give a tighter, more preamp-driven sound. Turn the amp's **Level** to make up the loudness either way.

### 5. Shape the tone

Now the tone stack. Turn one knob at a time through its whole range so you know what it does on this model: a Fender's Middle behaves nothing like a Mesa's. Then set the **Presence** (and **Depth**, where the amp has it) for the top and bottom of the power section.

After that, use the Cab block's **Low Cut** and **High Cut** to trim what no speaker in a room would give you: a low cut between 60 and 100 Hz and a high cut between 7 and 10 kHz are common starting points.

### 6. Add effects, then level

Add the drives, modulation, delay and reverb you need in the order described in [Building a signal chain](/manual/signal-chain). Keep delay and reverb mixes lower than feels right on their own; they always sound bigger in a room or a mix.

Finally, open **Preset Leveling** (the equaliser icon in the top bar, or ⌘L / Ctrl+L), press **Measure**, play as you would on stage, press **Stop** and **Apply**. Every preset you level to the same target plays at the same loudness. See [Matching preset loudness](/manual/gain-staging#matching-preset-loudness).

### 7. Compare and check

- Use **Compare A/B** under the top bar to keep two versions of the preset and switch between them. Switching back and forth is the fastest way to hear whether a change helped.
- Rest your ears. After half an hour of tweaking at volume, everything sounds dull and you start adding treble. Come back later and check.
- Check against a reference: a recording you love, at a matched level. The **Tone Match** block can learn the difference between your rig and a recording and apply it as a filter; it is a good way to see how far off you are, even if you turn its **Amount** down afterwards.
- Check in context: play along with a backing track or your band's rehearsal recording.

> **Tip:** Save often, and save variations as scenes or channels instead of new presets. A rhythm and a lead version of the same amp belong in one preset. See [Presets, scenes and channels](/manual/presets-and-scenes).

## What's wrong, and how to fix it

Each problem below lists the likely causes in the order worth checking. Change one thing at a time.

### Fizzy

A buzzy, papery top end on distorted sounds, like a bee in the speaker.

- **Too much gain.** Fizz is mostly high-order distortion. Lower the amp's gain, or the drive in front of it.
- **The cabinet.** Move the mic outward from the cap (raise **Position**), angle it (**Angle**), or try a darker mic such as a ribbon. Speakers differ too: the Celestion G12T-75 and Vintage 30 have strong upper-mid peaks; a Greenback or Creamback is smoother.
- **No high cut.** Set the Cab block's **High Cut** around 6 to 8 kHz with a 12 or 24 dB/oct slope.
- **Treble and presence too high.** Pull the amp's **Presence** down before the **Treble**.
- **A fuzz or distortion pedal into a high-gain amp.** Stacked distortion multiplies the fizz. Use a clean boost instead (see [the drive as a boost](/manual/signal-chain#the-drive-as-a-boost)).

### Harsh

Piercing, tiring upper mids (roughly 2 to 5 kHz), the kind that makes you turn down.

- **The mic sits on the cap.** Move it towards the edge or angle it 15 to 30 degrees off axis.
- **Bright switches and bright caps.** Switch off **Bright** (or the model's equivalent) and check the Bright Cap on the Advanced page is at 100 %.
- **The speaker's peak.** Find it with a narrow cut on a Parametric EQ after the cab: boost a band by 8 dB, sweep it between 2 and 5 kHz until the harshness jumps out, then turn the boost into a cut of 2 to 4 dB.
- **Fletcher-Munson.** You dialled the tone quietly and now play it loud. See [below](#it-sounds-different-at-low-and-high-volume).

### Muddy

Notes run together and chords lose definition, mostly from too much low and low-mid energy before the distortion.

- **Bass into the gain.** Distortion turns low end into mush. Cut lows before the amp: the amp's **Low Cut** on its Preamp page, the **Input Cut** switch, or a drive pedal in front with its drive low (it cuts bass and lifts mids on the way in).
- **Too much gain.** Again.
- **Tone stack.** Lower **Bass**; try more **Middle**, not less. A deep mid scoop sounds big alone and vanishes in a band.
- **Cabinet and mic.** An open-back cab, a closer mic (more proximity bass), or a ribbon all add low end. Use a closed 4x12 and an SM57 for tight rhythm sounds.
- **Low-mids around 200 to 400 Hz.** A gentle cut there on a Parametric EQ after the cab clears the fog.

### Thin

Weak, small, no body.

- **Not enough mids.** Raise **Middle**. Body lives between about 300 Hz and 1 kHz.
- **Mic placement.** Move the mic towards the edge, closer to the grille, or blend in a ribbon (see [mixing mics](/manual/cabs-and-mics#mixing-microphones)). Check the polarity of blended mics: two mics out of phase sound hollow and thin.
- **Cuts set too high.** A low cut at 150 Hz or above, or the amp's **Input Cut**, may be too much for this sound.
- **Small cabinet.** A 1x10 or 1x12 open back sounds smaller than a closed 4x12.
- **Depth and Resonance.** Where the amp has them, raise **Depth** (or the model's name for it, such as **Resonance** or **Deep**).

### Boomy

The low end swells and blooms on palm-mutes and low notes.

- **Depth or Resonance too high.** Turn it down first.
- **Speaker load.** The amp's power section boosts its output at the speaker's resonance. With **Speaker load** on Follow Cab the resonance matches the cab; if it still booms, lower **Load peak** on the Speaker page, or use **Find the cab resonance…** below the amp's controls to put the amp's resonance where the cab's really is.
- **Proximity effect.** Close mics, and ribbons most of all, boost lows. Back the mic off a little or lower the slot's **Proximity**.
- **Room modes at home.** Your room may boom at one note. Check on headphones; if the boom is only in the room, fix it with the output's global EQ (see [Gain staging](/manual/gain-staging#outputs-boostpad-and-global-eq)), not in every preset.
- **Low cut.** Set the Cab block's **Low Cut** to 80 to 100 Hz.

### Buried in the mix

You can hear yourself alone, but the band swallows you.

- **Mids.** The guitar's place in a mix is roughly 500 Hz to 3 kHz. Add **Middle**, and avoid scooped EQ.
- **Less gain, less reverb, less delay.** Each one pushes the guitar back.
- **Low cut.** Bass guitar and kick drum own the bottom. A higher low cut (100 to 150 Hz) makes room and makes you clearer, not quieter.
- **Level.** Use **Scene Level** on the Controllers page, or level the preset to a louder target, for the parts where you must stand out.

### Too compressed

The sound does not move with your picking; everything comes out at the same volume.

- **Gain.** High gain is compression. Lower it, and let the guitar's volume knob work.
- **Sag.** Lower **Sag** on the Power Amp page for a stiffer, faster supply. A **Rectifier** choice of Silicon also stiffens it.
- **Master.** A lower Master means less power-amp compression.
- **Compressors.** Check the Compressor block's amount and the Amp's **Output Comp** on the Dynamics page. The Cab's **Motor Drive** and the Amp's **Speaker Compression** compress too.
- **Input Dynamics.** On the Amp's Dynamics page, a positive **Input Dynamics** expands the input, giving back some of the difference between soft and hard picking.

### Too much gain

The sound is saturated even with the gain knob low.

- **Input level.** If Input 1 is calibrated wrongly (or you raised the interface gain afterwards), every amp gets more volts than a real one would. Recalibrate.
- **A hot pickup.** Lower **Input 1 Gain** on the Global page for every preset, or the amp's **Input Trim** on its Preamp page for one amp.
- **Drives left on.** A drive block's **Level** above 0 dB boosts the amp's input.
- **The model.** Some amps have little clean headroom. Pick a model with more headroom, or use its clean channel switch where it has one (such as **Clean Ch** or **Normal Ch**).

### Sounds great alone but not in the band

This is the most common complaint, and it is about context, not about the amp model.

- A tone dialled alone tends to have too much bass, too much gain, a mid scoop and too much reverb. Each sounds great solo and fights the band. Reverse all four a little.
- Dial with the band, or with a recording of the band at rehearsal volume.
- Compare with a record in the same style at matched loudness.
- Cut what the bass player plays: raise the Cab block's **Low Cut**.

### It sounds different at low and high volume

Your ears are less sensitive to low and high frequencies at low volume than to the middle; this is described by the equal-loudness curves, often called the Fletcher-Munson effect after the researchers who first measured them. As you turn up, bass and treble seem to grow faster than the mids.

So a tone dialled quietly at home, with bass and treble added to sound full, becomes boomy and harsh on stage. A tone dialled loud sounds thin and dull when you turn it down.

- Dial at the volume you will play. If you cannot, dial a little louder than comfortable and check at your real level.
- Keep two versions if you must: a scene or a channel for home and one for the stage.
- For a different room or PA, change the **output's global EQ** or **Boost/Pad** on the Global page instead of every preset. They apply to every preset on that output.
- The amp's own volume is not the problem here: Cathode's amp models behave the same whatever your monitor level, because the **Master** and **Level** are separate. Only your ears change.

> **Note:** Your monitors' volume knob changes what you hear, not what Cathode does. The amp's breakup is set by its **Gain** and **Master**; the loudness by **Level** and the output.
