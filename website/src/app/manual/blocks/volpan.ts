import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A volume pedal and panner in one block. **Volume** is the pedal, with a choice of pot tapers so it feels like
the pedal you are used to; **Auto-Swell** fades every note in automatically, like rolling up your guitar's volume knob
after picking, for violin-like swells. It also has an output level and balance, an input selector and per-side panning.

At its defaults it changes nothing.`,

  usage: `**Volume pedal:** put an expression pedal on **Volume** (see [Controllers and modifiers](/manual/modifiers)).
Where you put the block matters:

- **Before the drives and amp**, it works like your guitar's volume knob: turning down also cleans up the distortion.
- **After the amp, before delay and reverb**, it changes only the loudness, and when you pull it back the delay and
  reverb tails carry on ringing. This is the usual place for swells and for a pedal used as a master volume.

**Swells without a pedal:** set **Type** to Auto-Swell, Threshold a little below your softest picking, and Attack 300 to
800 ms. Put a delay and reverb after it for ambient pads.`,

  params: {
    level: `The block's output level in dB, separate from the pedal. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down; at the centre both are at
full level. A simple way to place a mono signal in the stereo field.`,

    volume: `The volume pedal: 100 % is full level, 0 % silence, following the chosen Taper. This is the knob to put on an
expression pedal.`,

    taper: `How the pedal's travel maps to volume. See the options below. Audio (Log) tapers feel more even to the ear, as
most of the audible change happens at the top on a linear one.`,

    type: `**Volume** is a normal volume pedal. **Auto-Swell** also fades each note in from silence over Attack once your
playing crosses the Threshold.`,

    threshold: `Auto-Swell only: how loud a note must be to trigger a swell. After a note, the swell re-arms once the level
falls 6 dB below the threshold, ready for the next one. Too low and noise triggers swells, too high and soft notes are
never heard.`,

    attack: `Auto-Swell only: how long each note takes to fade in, from silence to full level, along an S-shaped curve like a
hand on a knob.`,

    inputSelect: `**Stereo** uses both sides. **Left Only** or **Right Only** copies that side to both, for example to pick
one side of a stereo input or to drop a side you do not want.`,

    panLeft: `Where the left side goes in the output. −100 % (the default) keeps it on the left; at 0 it is in both sides at
full level.`,

    panRight: `Where the right side goes in the output. 100 % (the default) keeps it on the right. Set both pans to 0 for
mono (a signal identical on both sides comes out about 6 dB louder), or swap them to swap the sides.`
  },

  options: {
    taper: {
      Linear: `Level proportional to the pedal's travel.`,
      'Log 30A': `An audio taper at 30 % of full level at half travel: a gentle curve.`,
      'Log 20A': `An audio taper at 20 % at half travel.`,
      'Log 15A': `An audio taper at 15 % at half travel.`,
      'Log 10A': `An audio taper at 10 % at half travel: the classic audio pot.`,
      'Log 5A': `An audio taper at 5 % at half travel: most of the change near the top.`,
      'S-Taper': `Slow at both ends, fast in the middle: fine control near silence and near full level.`
    },
    type: {
      Volume: `A normal volume pedal.`,
      'Auto-Swell': `Each note fades in automatically.`
    }
  },

  tips: `- The order inside the block is: input select, pans, swell, volume pedal, then level and balance. Gains glide per
  sample, so pedal moves never click.
- If you put Volume on an expression pedal in several presets, keep the block in the same place in the chain each time
  so the pedal always does the same job.`
};
