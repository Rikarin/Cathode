import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A two-way crossover: it splits each side of the signal into a low band and a high band at one frequency, gives
each of the four bands (left low, left high, right low, right high) its own level and pan, and recombines them. At the
defaults the bands sum back flat (Linkwitz-Riley filters at 24 dB/oct), so the block changes nothing until you move
something.

Its other mode sends the low band to the left output and the high band to the right, so you can process or route the
two bands separately.`,

  usage: `**Uses:**

- **Rebalance lows and highs:** turn the high bands up or down against the lows, like a very smooth tilt EQ.
- **Keep the bass centred, spread the highs:** pan the low bands to the centre (0 %) and leave the highs hard left and
  right, after a stereo effect.
- **Split for separate processing:** choose **Low Left / High Right**, then use the left and right sides in later blocks
  (or a mixer) to treat the bands differently, for example drive only the highs of a bass. Cascade two crossovers for
  three bands.`,

  params: {
    frequency: `The crossover point: below it is the low band, above it the high band.`,

    lowLevelL: `The level of the left input's low band, in dB. In Low Left / High Right mode, the level of the low band
(sent left).`,

    lowLevelR: `The level of the right input's low band, in dB. Stereo mode only.`,

    highLevelL: `The level of the left input's high band, in dB. Stereo mode only.`,

    highLevelR: `The level of the right input's high band, in dB. In Low Left / High Right mode, the level of the high band
(sent right).`,

    lowPanL: `Stereo mode: where the left input's low band goes. −100 % is hard left (the default, which leaves the image as
it is); towards the centre it is added to the right side too, at full level at 0.`,

    lowPanR: `Stereo mode: where the right input's low band goes. 100 % is hard right (the default).`,

    highPanL: `Stereo mode: where the left input's high band goes. −100 % is hard left (the default).`,

    highPanR: `Stereo mode: where the right input's high band goes. 100 % is hard right (the default).`,

    mode: `**Stereo** splits each side and recombines the four bands with their levels and pans. **Low Left / High Right**
splits the mono sum of the input and sends the low band to the left output and the high band to the right; only Low
Level L, High Level R, Level and Balance apply.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`
  },

  options: {
    mode: {
      Stereo: `Each side split into low and high, each band with its own level and pan.`,
      'Low Left / High Right': `The mono sum split: low band out of the left, high band out of the right.`
    }
  },

  tips: `- The pans use a balance law: the near side stays at full level and the far side is turned down. Two bands panned to
  the centre therefore add up at full level on both sides, so a centred pair of identical signals is about 6 dB louder.
- Changes to the levels, pans and frequency glide over a few milliseconds, so you can switch them with scenes without
  clicks.`
};
