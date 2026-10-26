import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A stereo widener for a mono or narrow signal. Your guitar is mono until a stereo effect splits it; the Enhancer
makes it wide by sending slightly different versions of it to the left and right. Three of its types do this in a
mono-compatible way: they add a derived "side" signal to one side and subtract it from the other, so when the two sides
are summed back to mono the effect cancels and your tone is untouched. The fourth, Haas, delays one side, which is
wider but not mono safe.

It also has phase invert and per-side panning, so it doubles as a channel utility for swapping, narrowing or merging
the sides.`,

  usage: `**Where it goes:** after the amp and cab (which are usually mono), before the stereo time effects or at the very
end of the chain.

**Starting point:** Modern, Width 50 %, Low Cut 150 Hz. Raise Width until the sound is as wide as you like on headphones,
then check it in mono: with Modern, Classic and Stereoizer, nothing should change.

Keep **Low Cut** up: a wide low end sounds vague on speakers and is the first thing to go wrong in a PA.`,

  params: {
    type: `How the side signal is made. See the options below.`,

    width: `How much side signal is added: 0 % leaves the input as it is, higher is wider. On Haas, how much of the right
side is replaced by its delayed copy (100 % is fully delayed).`,

    time: `Classic and Haas only: the delay. On Classic it is the delay of the side signal: short times (2 to 8 ms) give a
fine, comb-filtered width; longer ones a more obvious doubling. On Haas it is how much later the right side arrives:
under about 30 ms the ear still hears one sound, pulled towards the earlier (left) side.`,

    lowCut: `Modern, Classic and Stereoizer: a high-pass on the side signal, so the bass below it stays centred. 100 to 200 Hz
keeps the low end solid.`,

    highCut: `Modern, Classic and Stereoizer: a low-pass on the side signal, so the very top is not widened. Lower it if the
width sounds phasey or swirly in the highs.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    invert: `Flips the polarity of the left, right or both outputs. Inverting one side of a mono signal makes it sound very
wide and "outside the speakers", but it cancels in mono, so use it with care.`,

    panLeft: `Where the left side goes in the output. −100 % (the default) keeps it on the left; at 0 it is in both sides at
full level.`,

    panRight: `Where the right side goes in the output. 100 % (the default) keeps it on the right. Swap the two pans to swap
the sides; set both to 0 to merge to mono (a signal identical on both sides comes out about 6 dB louder).`,

    depth: `Modern and Stereoizer only: how sharply the all-pass filters turn the phase. Higher gives stronger, narrower
alternations between the sides (a more dramatic but more coloured width); lower is subtler and smoother. 50 % is the
original Modern voicing.`
  },

  options: {
    type: {
      Modern: `Decorrelation: the side signal is the input through a chain of four all-pass filters (around 220 Hz, 760 Hz,
2.3 kHz and 6.4 kHz), whose phase rotates across the spectrum, so different frequency regions lean alternately left and
right. Natural and mono compatible.`,
      Classic: `The side signal is the input delayed by Time, giving complementary comb filters on the two sides. The
classic studio widener; mono compatible.`,
      Haas: `The right side is delayed by Time (the precedence effect). Very wide and simple, but it combs badly when summed to
mono. Low Cut and High Cut do not apply.`,
      Stereoizer: `Like Modern, with a long chain of 24 all-pass filters at third-octave steps from 100 Hz, so narrow bands
alternate left and right across the whole spectrum: complementary filtering. The widest of the mono-compatible types.`
    },
    invert: {
      None: `Both sides as they are.`,
      Both: `Both sides inverted (the image does not change; the absolute polarity does).`
    }
  },

  tips: `- Widening a signal that is already stereo (after a stereo chorus or reverb) adds little and can smear it. Widen the
  dry, mono part of the chain instead.
- For a big double-tracked rhythm sound, try Classic with Time around 10 to 20 ms and Width 70 to 100 %.`
};
