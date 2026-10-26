The order of your blocks changes the sound as much as their settings. A fuzz after a wah sounds different from a wah after a fuzz; a delay before a distorted amp turns into mush, after it stays clear. This chapter explains where each kind of effect usually goes and why, so you can follow the rules on purpose and break them on purpose.

How to place, move and wire blocks on the grid is in [The grid](/manual/the-grid). This chapter is about what goes where.

![A preset on the grid: Input, Drive, Amp, Cab, Reverb, Output](screenshots/grid.webp)

## The classic order

A typical guitar rig, from left to right:

1. **Input**, with its noise gate
2. **Wah**, **Compressor**, **Pitch** (filters and dynamics that want the clean guitar)
3. **Drive** blocks (boosts, overdrives, fuzz)
4. **Vibe**, **Phaser** (some players put these here, in front of the amp)
5. **Amp**
6. **Cab**
7. **EQ** (Parametric or Graphic)
8. **Chorus**, **Flanger**, **Phaser**, **Tremolo/Panner**, **Rotary** (modulation)
9. **Delay**
10. **Reverb**
11. **Output**

The reasoning is simple: anything that should be distorted goes before the amp; anything that should stay clear goes after it. Delay and reverb come last because in a real room the echo and the reverb happen to the whole sound.

## Before the amp

### The noise gate

High gain amplifies hiss and hum along with the guitar. The gate silences the signal when you stop playing.

- **At the Input**: the grid's **Input** block has a gate (**Gate Type**: Classic, Intelligent or Noise Reducer). Gating here, before the gain, removes noise from the guitar and cable before the amp multiplies it. **Intelligent** ignores mains hum when it decides whether you are playing, so a buzzing single-coil does not hold it open; **Noise Reducer** only acts above about 2 kHz, so it fades hiss without cutting notes. Start with **Threshold** about -60 dB and raise it until the noise stops between notes without chopping the decay of sustained chords.
- **After the amp**: a **Gate/Expander** block after the amp or cab removes the amp's own hiss. Set its **Sidechain** to **Grid Input** so it opens and closes on the clean guitar, not on the noisy amp output: that tracks your playing precisely and gives the tight, abrupt stops of modern metal. Its **Modern Gate** and **Modern Expander** types add hysteresis so a decaying note does not make it chatter.

The Factory bank's driven presets use the Intelligent gate at the Input. For extreme gain, use both.

### Wah

A wah in front of the amp is the classic sound: the amp distorts the swept filter, so the vowel sound is thick and vocal. After the amp it sounds thinner and more synthetic, which is sometimes the goal. Put it **before** drives for the usual sound; a fuzz in front of a wah gives a squelchier, more extreme sweep. A wah needs an expression pedal on its **Position** (see [Controllers and modifiers](/manual/modifiers)).

### Compressor

A compressor in front of the amp evens out your picking, adds sustain and makes a clean amp sound tighter and more polished: the sound of country, funk and clean pop. Set its **Input Level** to **Instrument**. Into a distorted amp it adds sustain and noise; most high-gain players do not need one.

After the cab, set to **Line**, a compressor acts as a mix tool: it controls peaks without changing your touch. The Factory presets with clean scenes have one after the cab that only catches the peaks of hard pick attacks.

### Pitch

Octavers, whammy and harmonisers track best on the clean guitar, so put them first, before any drive.

### Vibe and phaser

The classic late-1960s rig put a Uni-Vibe-style vibe and a phaser in front of the amp, so the amp's distortion smears the sweep. Cathode's **Vibe** block (Chorus or Vibrato mode) works well there. The Factory preset "Machine Gun" puts its vibe between the fuzz and the amp. After the amp, a phaser sounds cleaner and more obvious, the way a studio would add it.

## The drive as a boost

The most useful trick in high-gain playing: put a **Drive** block in front of an already distorted amp with its **Drive** low (0 to 2), **Level** up a few dB, and **Tone** a little above noon.

It does not add much distortion. It shapes what the amp distorts: a Tube Screamer-style circuit (**TS808 OD**, **TS9 OD**) cuts the bass and lifts the mids on the way in, so the amp's distortion is tighter, the low strings less flubby, and palm-mutes chunkier. The extra level pushes the amp's first stages a little harder for more sustain.

- **TS808 OD** and **TS9 OD**: the classic boost for Marshall, Mesa and 5150-style amps.
- **Treble Booster**: for vintage Marshalls and Vox, the 1960s and 1970s lead sound.
- **FET Boost**, **Micro Boost**, **Esoteric Boost**: cleaner boosts with less EQ change.
- **Chiron OD**, **Transparent OD**: transparent overdrives that thicken without changing the voice much.

The amp also has its own **Boost Type** on its Preamp page (TS808 OD, Super OD, Chiron OD, Treble Booster, Esoteric Boost, FET Boost), if you would rather not use a separate block. And the Amp's **Low Cut** (Preamp page) or **Input Cut** switch tightens the low end in the same spirit, without the mid lift.

### Drives into clean amps

A drive into a clean amp provides the distortion itself: the "pedal platform" approach. Use a clean Fender or a Hiwatt, set the drive to taste, and set its **Level** so on and off are roughly equally loud. Stacking two drives (a low-gain one into a higher-gain one) gives smoother, more compressed sounds; the first one sets the character.

> **Tip:** Fuzz pedals with a germanium or vintage input (**Face Fuzz**, **NKT Face Fuzz**) react strongly to what feeds them. Put them first, straight after the Input, and clean up the sound with your guitar's volume knob.

## After the cab

### EQ

An EQ after the cab shapes the final sound, as an engineer would on a mixing desk: cut a harsh peak, trim the low end for the band, lift presence for a solo. An EQ before the amp shapes what gets distorted, which acts more like a different amp. Try both: a **Graphic EQ** before the amp with the low bands down tightens; the same EQ after the amp only changes the colour.

### Modulation

**Chorus**, **Flanger**, **Phaser**, **Tremolo/Panner** and **Rotary** usually go after the cab, where they modulate the whole amp sound cleanly and can work in stereo. Before a distorted amp, chorus and flanger tend to sound muddy.

The amp's own vibrato or tremolo (on the models that have one) is part of the circuit, before the power amp. A tremolo after the cab is cleaner and can be synced to the tempo; the Factory "How Soon Is Now" uses an **Optical Trem** locked to sixteenths.

### Delay

Delay goes after the amp so each echo is a clean copy of the distorted sound. In front of a distorted amp the echoes are distorted and compressed too, and pile up into mush, though that is the right sound for some vintage tape-echo setups into a small cranked amp.

Use the delay's **Low Cut** and **High Cut** to keep the repeats out of the way of what you play. The **Ducker** (on its Ducking page) turns the repeats down while you play and lets them bloom when you stop: great for leads.

### Reverb

Reverb almost always goes last: it is the room the whole rig is in. A **Spring** reverb is the exception that proves the rule: on a real Fender it sits inside the amp, before the power amp. For that sound, you can put a Spring reverb before the Amp block on a clean Fender model.

## Parallel paths

The grid lets you split the signal into rows and mix them back together (see [The grid](/manual/the-grid)). Uses:

- **Parallel delay and reverb.** Put the delay and the reverb on two rows fed from the same point, so the reverb does not smear the echoes. Turn on **Kill Dry** in both (on their Advanced page) so the dry signal comes only from the main row and is not doubled.
- **Two amps.** Split before two Amp blocks with their own Cabs, and mix them: a Fender for the clean shimmer and a Marshall for the crunch, or a tight amp for attack and a loose one for body.
- **A clean blend.** Mix a little clean signal with a fuzz for clarity, or use a drive's own **Mix** and **Dry Level**.
- **Wet/dry setups.** Keep the amp sound on one row and the effects 100 % wet on another, so the effects never colour the core tone.

The **Mixer** block brings rows back together, with a gain and balance per row. The Output block also has a level and balance for each row.

## Stereo

Cathode is stereo from end to end. The Amp processes mono (left, right or both: its **Input Select**) and sends the same signal to both sides; stereo begins with the blocks after it.

- **Cab**: pan two mics left and right for a wide cab, or use **Room Mic Spacing**.
- **Chorus**: its **LFO Phase** and **Width** spread the sweep across the sides.
- **Delay**: **Ping-Pong** and **Dual** (separate left and right times) make wide echoes.
- **Reverb**: most types are stereo; **Stereo Spread** sets the width.
- **Tremolo/Panner**: **Panner** moves the sound between the speakers.

Stereo effects only work if the outputs are stereo. Check the output's **Mode** on the Global page, and remember that a mono PA will sum both sides: a very wide, out-of-phase effect can get thinner in mono.

## The four-cable method with the FX Loop block

If you own a tube amp with an effects loop, the **four-cable method** lets you use its preamp and power amp with Cathode's effects in front of and inside the amp. Four cables:

1. Guitar → your interface's Input 1.
2. An interface output (the **FX loop send** channels) → the amp's guitar input.
3. The amp's effects loop **send** → an interface input (the **FX loop return** channels).
4. An interface output (Output 1) → the amp's effects loop **return**.

Choose the send and return channels in **Settings ▸ Audio setup** (**FX loop send** and **FX loop return**). Then on the grid:

- **Input** → wah, compressor, drives → **FX Loop** block → modulation, delay, reverb → **Output**.
- Leave out the Amp and Cab blocks: your real amp is the amp, your real cab the cab.

<!-- screenshot wanted: a four-cable-method preset on the grid: Input, Wah, Drive, FX Loop, Chorus, Delay, Reverb, Output -->

The FX Loop block sends its input out to the amp's front end and returns what comes back from the amp's preamp. Set its **Send Level** and **Return Level** for clean levels in both directions, and enter the round-trip delay in **Loop Latency** (measure it with the loopback test in Audio setup, with the send cabled to the return) so parallel paths stay in time.

The FX Loop block also works for a single pedal: put your favourite real drive pedal in the loop between Cathode's blocks.

> **Note:** For a real cab through a solid-state power amp, keep Cathode's Amp but set its **Output Mode** to **SS Power Amp + Cab**, and turn on **Cab modeling off** on the Global page. To feed a real tube power amp from Cathode's preamp, switch the Amp's **Power Amp** off.

## Spillover

Spillover lets delay and reverb tails ring on when you switch.

- **Between presets**: **Preset spillover** on the Global page. On, the old preset's delay and reverb tails ring out under the new one; off, it cuts cleanly.
- **Between scenes, or when you bypass a block**: set the delay's or reverb's **Bypass mode** to **Mute FX In (spillover)**. Bypassing it then stops new signal going in but lets the tail finish. The default, **Thru**, cuts the effect at once.

Spillover works naturally when the delay and reverb stay on and only their input changes. For long ambient tails, keep them in every scene and change only their mix or the blocks before them.
