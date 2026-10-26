import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A passive amp tone stack on its own: the same exact circuit as in the [Amp](/manual/blocks/circuitamp)
block, with real pots and caps, driven as it is in an amp (from a cathode follower into the next tube's grid). Bass,
Middle and Treble interact the way they do on the amp: this is not a three-band EQ with separate bands.

It is linear (no distortion) and stereo. Its natural loss with every knob at noon is made up, so *Level* at 0 dB is
about unity at 1 kHz.`,

  usage: `Use it to give a sound that has no tone stack one that behaves like an amp's:

- after an [Amp NAM](/manual/blocks/nam) capture, to get real amp-style Bass, Middle and Treble on a capture whose
  knobs are fixed;
- after a [Drive](/manual/blocks/drive) block used as a preamp, or before one, to shape what it distorts;
- on a clean signal, acoustic or keys, for a vintage amp's mid scoop.

Start with everything at 5, then adjust. Placed before a distortion, it changes what distorts; after, it is tone
shaping.`,

  params: {
    type: `Which amp's stack.`,

    bass: `The stack's bass pot. It also changes how much midrange gets through, as on the amp.`,

    middle: `The stack's middle pot: at 0 the mid scoop is deepest and the stack quietest, at 10 it is closest to flat.`,

    treble: `The stack's treble pot: how much high end passes the treble cap, and the balance of the treble against the
rest.`,

    level: `The output level in dB, on top of the automatic make-up gain.`,

    balance: `The left/right balance of the output.`
  },

  options: {
    type: {
      Fender: `The Fender blackface stack (AB763 Twin and Super Reverb values): a deep, wide mid scoop and a sparkly top.`,
      Marshall: `The Marshall 1959 Super Lead stack: less scoop, more mids, brighter.`,
      Vox: `An AC30 Top Boost style stack in the same layout. The original has a fixed resistor in place of the middle
pot; here it is a pot, set so that Middle at 5 is the original.`
    }
  },

  tips: `A passive tone stack is never flat: even with every knob on 10 it shapes the sound, and the knobs work
together rather than as separate bands. If you want no colour at all, take the block out rather than looking for a
flat setting. For surgical EQ, use the [Parametric EQ](/manual/blocks/peq) or [Graphic EQ](/manual/blocks/geq).`
};
