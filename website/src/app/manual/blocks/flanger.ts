import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Flanger is the jet-plane whoosh: a copy of your signal delayed by only a millisecond or so, swept back and forth and mixed with the dry sound. Because the delay is so short, the two copies cancel at some frequencies and reinforce at others, and as the delay moves those notches slide up and down the spectrum. Feedback sends the delayed signal back into itself, which sharpens the notches into a ringing, metallic sweep.

It also does thru-zero flanging, the tape-machine trick where the swept copy passes right through the dry one, for the deepest, most dramatic sweep.`,

  usage: `**Where it goes:** after the amp for a big, clear sweep, or before it (where many pedal flangers live) for a grittier, more aggressive sound. Put it before delay and reverb.

**A starting point:** Rate 0.2–0.4 Hz, Depth 60–80 %, Manual 1 ms, Feedback 40–60 %, Mix 50 %. Strum a chord and let it ring to hear the sweep.

**Things to try:**
- Negative **Feedback** for a hollower, more nasal tone.
- **Thru-Zero** Auto with **Phase Reverse** on Both for the classic tape-flange "zero point" where the sound almost disappears.
- A slow rate and high feedback with **Mix** at 50 % on a palm-muted riff: the 80s metal jet.
- Very small Depth and a fast rate for a metallic vibrato.`,

  params: {
    rate: `How fast the sweep moves. Slow rates (0.1–0.5 Hz) give long, dramatic swooshes; faster ones turn into a warble. Ignored when **Tempo** is set.`,
    depth: `How far the delay sweeps: up to 7 ms on top of **Manual** at 100 %. More depth means the notches travel further. Kept within **Min Time** and **Max Time**.`,
    delay: `The shortest delay of the sweep (the "manual" control on a flanger pedal). It sets where the notches sit: short delays put them high and far apart, longer ones bring them lower and closer together. With Depth at 0 this knob alone gives a static, fixed-comb tone.`,
    feedback: `How much of the delayed signal goes back into the delay. Higher values sharpen the effect into a ringing, resonant sweep. Positive and negative values emphasise different sets of frequencies: positive is the classic jet, negative sounds hollower.`,
    mix: `The balance between dry and flanged sound, at constant power. 50 % gives the deepest notches, because dry and wet are equal there.`,
    spread: `The offset between the left and right sweeps. At 0° both sides sweep together; at 90–180° they sweep against each other, which makes the effect move across the stereo field.`,
    level: `The block's output volume, applied to dry and wet alike.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    tempo: `Locks the sweep to the preset's tempo: one sweep cycle per note value you pick. When set, it replaces **Rate**.`,
    lfoType: `The shape of the sweep. Triangle (the default) sweeps evenly up and down; Sine slows down at the ends; the others give stepped or lopsided motion.`,
    lowCut: `A gentle (6 dB/octave) high-pass on the wet signal only. Raise it to keep the flanging out of the low end.`,
    highCut: `A gentle (6 dB/octave) low-pass on the wet signal only. Lower it for a darker, less fizzy sweep.`,
    phaseReverse: `Flips the polarity of the wet signal on the left, right or both sides. With the wet inverted, the notches move to where the peaks were and the sweep sounds hollower; with **Thru-Zero** on, an inverted wet cancels the dry completely at the zero point.`,
    bassFocus: `A high-pass filter on what enters the delay, so the low end stays dry and tight while the mids and highs are flanged. Raise it when the flanger makes your bass notes wobble or thin out.`,
    drive: `Saturates the signal going into the delay (and the feedback around it), for a dirtier, more aggressive flange.`,
    minTime: `The shortest delay the sweep is allowed to reach. If **Manual** is below it, the sweep is pushed up to start here.`,
    maxTime: `The longest delay the sweep is allowed to reach. If Manual plus the depth would go past it, the sweep stops here. Use Min and Max Time together to keep the sweep in a range you like.`,
    thruZero: `Delays the dry signal too, so the swept copy passes through zero delay relative to it: the sound of two tape machines flanged by hand. **Auto** delays the dry by the middle of the sweep; **Manual** delays it by **Dry Delay**. Off is a normal flanger.`,
    dryDelay: `How much the dry signal is delayed when **Thru-Zero** is Manual. The zero point of the sweep is where the wet delay passes this value.`,
    lfoHicut: `Smooths the LFO with a low-pass filter. At its maximum (50 Hz) it is off; lower values round off sharp corners in the waveform, and soften the steps of **LFO Quantize**.`,
    lfoQuantize: `Steps the LFO into a number of fixed levels instead of a smooth sweep, so the flanger jumps between that many notch positions. Off is a smooth sweep.`,
    autoDepth: `Narrows the sweep as the rate rises above 1 Hz, so fast settings don't wobble wildly. Low scales the depth by one over the square root of the rate, High by one over the rate.`,
    vcoResponse: `How the LFO maps onto the delay time. **Linear** sweeps evenly in milliseconds, so the notches rush through the short-delay (high) end of the sweep and crawl at the long-delay (low) end. **Exponential** sweeps evenly in pitch, so the notches move at an even musical pace across the whole range.`,
    exponent: `Bends the sweep towards one end. At 1 the sweep is as set; above 1 it lingers at the short-delay (high-notch) end; below 1 it lingers at the long-delay end.`,
    stereoSpread: `Widens or narrows the wet signal's stereo image. 100 % leaves it as it is, 0 % folds it to mono, negative swaps the sides, beyond ±100 % widens past the speakers.`,
    lfoReset: `When on, engaging the block restarts the sweep from the same point, so every time you stomp it on it starts the same way. Handy when the flange has to land on a particular beat.`,
    globalMix: `When on, this block's Mix follows the global **Effects Mix** offset.`
  },

  options: {
    lfoType: {
      Square: 'Softened square: jumps between two delay times without clicking.',
      Random: 'Sample and hold: a new random level each cycle.',
      Log: 'Rounded tops and sharp troughs.',
      Exponential: 'Sharp peaks and rounded troughs.',
      Trapezoid: 'A clipped triangle that lingers at both ends of the sweep.',
      Astable: 'The charge-and-discharge curve of an analogue oscillator circuit.'
    },
    thruZero: {
      Off: 'A normal flanger: the dry signal is not delayed.',
      Auto: 'The dry is delayed by the middle of the sweep, so the wet crosses it twice per cycle.',
      Manual: 'The dry is delayed by Dry Delay, so you choose where the zero point falls.'
    },
    vcoResponse: {
      Linear: 'Even in milliseconds: the notches move fast at the short end of the sweep and slowly at the long end.',
      Exponential: 'Even in pitch: the notches move at a steady musical pace.'
    }
  },

  tips: `- **The sweep is hard to hear on single notes.** Flanging is a comb filter, so it needs a harmonically rich sound: strum chords, use some gain, or raise Feedback.
- **Too much low-end wobble?** Raise **Bass Focus** to 150–300 Hz before you reach for Low Cut: it keeps the bass dry instead of just removing it.
- **Thru-zero tip:** with Thru-Zero on, the dry is delayed a little, so the whole block adds a millisecond or two of latency to the dry sound. You won't hear it, but it is there.
- High Feedback can get loud at some points of the sweep. Watch your levels and, if needed, trim **Level**.`
};
