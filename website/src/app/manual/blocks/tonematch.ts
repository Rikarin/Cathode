import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `Tone Match learns an EQ that makes your rig's overall frequency balance match a reference recording, then
applies it. Point it at a recording of a tone you like (a .wav), play for a few seconds, and it compares the long-term
spectrum of the reference with your rig's output and builds a correction filter.

It matches the frequency balance only: the brightness, the weight of the low end, the shape of the mids. It does not
match the amount of distortion, the compression, or the effects. Get those close with the amp and the other blocks
first, then let Tone Match close the gap in EQ.`,

  usage: `**Learning a match.**

1. Put Tone Match near the end of the chain, after the Cab and before the time effects.
2. Get your sound roughly right first: same kind of amp, similar gain.
3. In the block's panel, pick the reference recording and how long to listen for, press *Learn*, and play the same
   kind of part as the reference, at playing level, until it stops listening.

**Amount** and **Smoothing** beside *Listen for* shape the correction as it is learnt. Amount (25 to 100 %) is how
much of the difference goes into the match; the block's own Amount knob scales it again afterwards. Smoothing (1/6 of
an octave to a whole octave, a third by default) is how closely the correction follows the reference: finer smoothing
copies more detail, wider smoothing keeps only the broad shape.

It listens to the rig's output. Learning again replaces the old match rather than stacking a new one on top. Until you
learn a match, the block passes the signal through unchanged.

**What the filter does.** The correction is smoothed to the chosen width, a third of an octave by default (so it
follows the overall shape, not single notes), limited to ±18 dB, held flat below 40 Hz and above 16 kHz, and set so the level between 100 Hz and
5 kHz stays about the same. It is a minimum-phase filter with no latency.`,

  params: {
    mix: `How much of the correction you hear: 100 % is the full match, 0 % the signal as it was, and settings between
blend the two. Turn it down if the match is close but a little too much.`,

    level: `The output level in dB.`,

    balance: `The left/right balance of the output.`
  },

  tips: `**Choose a clean reference.** A guitar on its own (an isolated track, or a solo section with little else
going on) gives a much better match than a full mix, where bass, drums and vocals end up in the guitar's EQ.

**Play like the reference.** Chords against single notes, palm mutes against open strings: what you play changes the
spectrum. Match the part, not just the sound.

**Fix big gaps first.** If the match needs a huge boost or cut somewhere, the amp, cab or mic choice is probably wrong.
Change those, then learn again.`
};
