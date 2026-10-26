import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Vibe recreates the Uni-Vibe, the 60s photocell effect behind some of the most famous psychedelic and blues-rock guitar tones. It is a kind of phaser, but a very particular one: its four filter stages use very different capacitors, so their notches sit far apart and move together in an uneven, throaty way. And it is driven by a lamp shining on light-sensitive resistors, which brighten quickly and darken slowly, so the sweep has a lurching, pulsing feel rather than a smooth one.

It has two modes: **Chorus**, which mixes the effect with your dry sound for the classic swirl, and **Vibrato**, which is the effect alone, heard as a watery pitch wobble.`,

  usage: `**Where it goes:** before the amp, usually after any fuzz or overdrive. That is where the original lived on most famous recordings, and pushing a cranked amp with it is a big part of the sound.

**A starting point:** Mode Chorus, Rate 2–4 Hz, Intensity 70–80 %, Mix 50 %. For a slow, deep throb, drop the rate to around 1 Hz.

**Things to try:**
- **Vibrato** mode with a moderate rate for a seasick, watery pitch wobble.
- A slow rate with high intensity into a fuzz for heavy, swirling leads.
- Some **Spread** for a stereo vibe, where each side sweeps at a different point.`,

  params: {
    mode: `**Chorus** blends the phase-shifted signal with the dry, cutting the Vibe's lopsided notches: the classic sound. **Vibrato** is the shifted signal alone, which you hear as pitch wobble; in Vibrato the Mix knob is ignored.`,
    rate: `How fast the lamp pulses. Slow (0.5–2 Hz) is a deep, throbbing swirl; faster (4–8 Hz) a rapid shimmer. There is no tempo sync on this block.`,
    intensity: `How far the sweep moves: up to four octaves at 100 %. Low values are a subtle shimmer; high values the full, dramatic throb.`,
    mix: `In Chorus mode: the balance between dry and effect. At 50 % dry and wet are equal and the notches are deepest. Ignored in Vibrato mode, which is always fully wet.`,
    spread: `Offsets the right channel's sweep from the left's. At 0° both sides move together (a mono vibe); at 90–180° they sweep at different points for a wide stereo effect.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`
  },

  options: {
    mode: {
      Chorus: 'The effect mixed with the dry signal, using Mix: the classic Uni-Vibe swirl.',
      Vibrato: 'The effect alone: pure pitch wobble.'
    }
  },

  tips: `- **Fuzz first, then Vibe, then a loud amp** is the classic order for that late-60s sound.
- The pulse is lopsided on purpose: the lamp lights up fast and fades slowly, so the sweep lurches. If you want an even, smooth sweep, use the [Phaser](/manual/blocks/phaser) instead.
- Want the rate to follow a foot pedal, as the original's speed pedal did? Assign **Rate** to an expression pedal with a [modifier](/manual/modifiers).`
};
