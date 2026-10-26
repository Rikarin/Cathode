import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A vowel filter that makes your guitar "talk", like a talk box without the tube. Three resonant peaks sit on the
first three formants of a spoken vowel (A, E, I, O or U), the frequencies that let your ear tell vowels apart. The
**Control** knob morphs from a start vowel through a mid vowel to an end vowel, so sweeping it with an expression pedal or
a modifier says things like "yah-oo" or "wow".`,

  usage: `Put it before or after the drive: in front of an overdriven amp it sounds like a vocal wah; after the amp it is more
of a talk-box effect.

**Starting point:** Start A, Mid O, End U, Resonance 8. Put **Control** on an expression pedal (see
[Controllers and modifiers](/manual/modifiers)) and rock it while you play. For an automatic effect, drive Control with
an envelope follower so each pick says the vowel sequence, or with an LFO for a rhythmic "yoy-yoy".`,

  params: {
    start: `The vowel at Control 0 %.`,

    mid: `The vowel at Control 50 %.`,

    end: `The vowel at Control 100 %.`,

    control: `Morphs between the vowels: start at 0 %, mid at 50 %, end at 100 %, gliding smoothly in between. This is the
knob to move with a pedal or modifier.`,

    resonance: `The sharpness of the three formant peaks. Higher is more vocal and pronounced but thinner and more
"whistly"; lower is subtler and fuller.`,

    shift: `Moves every formant up or down by up to an octave (in semitones), like a smaller or larger throat. Up sounds
more like a child or a cartoon; down sounds bigger and darker.`,

    mix: `Blends the dry signal with the filtered one. The formant filter alone is thin by nature, so 50 to 80 % often keeps
more body while the vowels still read.`,

    level: `The block's output level in dB. The formant peaks take away a lot of the spectrum, so you may need to raise it.
−80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`
  },

  options: {
    start: {
      A: `As in "father".`,
      E: `As in "bed".`,
      I: `As in "heed".`,
      O: `As in "ought".`,
      U: `As in "food".`
    },
    mid: {
      A: `As in "father".`,
      E: `As in "bed".`,
      I: `As in "heed".`,
      O: `As in "ought".`,
      U: `As in "food".`
    },
    end: {
      A: `As in "father".`,
      E: `As in "bed".`,
      I: `As in "heed".`,
      O: `As in "ought".`,
      U: `As in "food".`
    }
  },

  tips: `- The formant frequencies are averages for an adult male voice, from the classic Peterson and Barney vowel study.
  The three peaks are mixed with a gentle downward tilt (the second and third a little quieter), so the vowel reads
  without losing all the guitar's top.
- Vowels with very different first formants (I to A, U to A) give the most obvious movement.
- Set Mid to the same vowel as Start or End for a simple two-vowel sweep.`
};
