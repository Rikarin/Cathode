import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A noise gate and downward expander. When you stop playing, it turns the signal down so the hiss and hum of a
high-gain sound disappear between notes. A **Gate** closes to a fixed depth below the threshold; an **Expander** turns
the level down gradually, by its Ratio, so decays fade more naturally. The **Modern** versions add hysteresis so a note
dying away across the threshold does not chatter.

The detector can listen to a different signal from the one it gates (the **Sidechain** page), so a gate placed after the
amp can follow your clean guitar.`,

  usage: `**Where it goes:** there are two common places, and they can be combined.

- **Before the amp** (or before the drives): the gate hears your clean guitar, so it tracks your playing accurately and
  stops noise before the amp amplifies it. This is the usual place for a single gate. The [Input](/manual/blocks/input)
  block also has a gate built in for this job.
- **After the amp** (before time effects): it catches the noise the amp itself adds. Set **Sidechain** to Grid Input (or
  the input your guitar is on) so it still follows the clean guitar rather than the distorted signal, which stays loud
  long after you stop.

Keep it in front of delays and reverbs, or it will chop off their tails.

**Starting point:** Type Modern Gate, Threshold −60 dB, Attack 0.5 ms, Hold 20 ms, Release 100 ms. Play, stop, and raise
Threshold until the noise vanishes when you mute the strings, then back it off until sustained notes stop being cut
short. For a softer action, switch to Modern Expander with Ratio 2 to 4.`,

  params: {
    threshold: `The level the detector compares against. Above it the gate is open; below it the gate closes (or the
expander starts turning down). Too high and it cuts sustain and soft notes; too low and noise gets through.`,

    ratio: `Expander types only: how fast the level falls below the threshold. With a ratio of 4, every dB the input drops
below the threshold takes 3 dB more off. Low ratios (1.5 to 3) are a gentle noise reducer; high ratios behave almost
like a gate. Hidden on the Gate types.`,

    attack: `How fast the gate opens when you play. Very short times (under 1 ms) let pick attacks through intact; longer
times soften them into a fade-in.`,

    hold: `How long the gate stays fully open after the signal drops below the threshold, before it starts to close.
Raise it if the gate flutters on staccato playing.`,

    release: `How fast the gate closes once the hold time has passed. Short releases are tight and abrupt (good for
chugging rhythms); long ones let notes fade naturally. The release is smoothed in decibels, so it means the same at any
depth.`,

    type: `**Expander** and **Gate**, and their **Modern** versions. See the list below.`,

    attenuation: `Gate types only: how far the gate turns the signal down when closed, in dB. 100 dB is effectively
silence; 20 to 40 dB takes the noise down without the sudden, unnatural silence of a full gate. Hidden on the expander
types: their depth is set by Ratio (down to −100 dB at most).`,

    scLowCut: `Sidechain page: a high-pass on what the detector hears (20 Hz is off). Raise it so low-string thumps or hum
do not hold the gate open.`,

    scHighCut: `Sidechain page: a low-pass on what the detector hears (20 kHz is off). Lower it so hiss does not hold the
gate open.`,

    detector: `**Peak** reacts to every transient instantly (with a 10 ms release), the usual choice for guitar. **RMS**
averages over about 10 ms, so it follows the body of the note rather than the pick spike.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    knee: `Expander types only: rounds the corner at the threshold over 6 dB (Soft) or 12 dB (Softer), so the expander eases
in rather than switching at one level. **Hard** is a sharp corner.`,

    scSource: `Sidechain page: what the detector listens to. **Block** is the block's own input. **Grid Input** is the signal
entering the grid, your clean guitar. **Row 1** to **Row 6** take the output of the last active block to the left of
this one on that row (if there is none, the gate listens to its own input). **Input 1** to **USB 7/8** are your
interface's inputs directly; an unmapped one is silence.`
  },

  options: {
    type: {
      Expander: `Below the threshold, the level falls by Ratio: a gradual, natural fade. Uses Ratio and Knee.`,
      Gate: `Below the threshold, the level drops to the Attenuation depth: on or off. Uses Attenuation.`,
      'Modern Expander': `The Expander with 6 dB of hysteresis: once open it stays open until the level falls 6 dB below the
threshold, so a fading note does not chatter.`,
      'Modern Gate': `The Gate with the same 6 dB of hysteresis. The best all-round choice for high gain.`
    },
    knee: {
      Hard: `A sharp corner at the threshold.`,
      Soft: `Rounded over 6 dB around the threshold.`,
      Softer: `Rounded over 12 dB around the threshold.`
    },
    detector: {
      Peak: `Follows every peak at once; releases over 10 ms.`,
      RMS: `Averages the power over about 10 ms.`
    }
  },

  tips: `- The gate starts open, so the first note after loading a preset is never clipped while the detector settles.
- Detection is linked across both channels, so a stereo signal never shifts in the image as the gate works.
- If long notes die too soon with a high-gain amp, try a gate before the amp and a gentle expander (Ratio 2) after it,
  rather than one aggressive gate.
- Chugging metal rhythms: Modern Gate, Attack 0.1 ms, Hold 10 ms, Release 30 to 50 ms, Sidechain on Grid Input, for
  tight stops between palm mutes.`
};
