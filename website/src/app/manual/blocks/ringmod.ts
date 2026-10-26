import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Ring Modulator multiplies your guitar by a sine wave (the carrier). Every frequency in your playing is replaced by two new ones: the sum and the difference of it and the carrier. Because those new frequencies are not musically related to your notes, the result is metallic, bell-like, robotic or outright clangorous, the sound of sci-fi robots and broken radios.

Besides the classic ring modulator it offers single-sideband types (a frequency shifter, which moves every partial up or down by the same number of hertz), a carrier locked to the song tempo, and a carrier that follows the note you play, which keeps the effect musical.`,

  usage: `**Where it goes:** usually after the amp for a clear, defined effect, or before it for a more chaotic, fizzy one. Before delay and reverb.

**A starting point:** Type Classic, Frequency 200–800 Hz, Mix 100 %. Play single notes and sweep Frequency slowly: you will hear it go from bell tones to dissonant clangs.

**Things to try:**
- **Pitch Tracking** on with **Freq Multiplier** at 2× or 0.5×: the carrier follows your notes, so the result stays related to what you play, a synth-like or octave-ish tone.
- **Tempo** at 1/16 for a choppy tremolo that also flips polarity on each cycle.
- **Upper Sideband** with a small Frequency (a few hertz) for a slow, phasing-like detune.`,

  params: {
    frequency: `The carrier's frequency. Low values (10–50 Hz) give a rough, tremolo-like flutter; mid values (100–1000 Hz) the classic bell and robot tones; high values a thin, metallic shimmer. Replaced by **Tempo** or **Pitch Tracking** when either is on.`,
    tempo: `Sets the carrier from the preset's tempo instead of Frequency: 1/4 is one carrier cycle per beat, 1/8 two, down to 1/64 at sixteen per beat. At these low rates the effect becomes a tremolo whose polarity flips every half cycle. Off uses Frequency.`,
    highCut: `A low-pass filter on the modulated (wet) signal, to tame the harsh upper sideband. Lower it for a darker, less fizzy ring mod.`,
    mix: `The balance between dry and modulated sound (a straight crossfade). A ring modulator is normally used fully wet; lower values blend the clean note back in.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    multiplier: `Multiplies the carrier frequency, whether it comes from Frequency, Tempo or Pitch Tracking. Most useful with Pitch Tracking: 1× puts the carrier on the note you play, 2× an octave up, 0.5× an octave down, and in-between values give inharmonic, bell-like results.`,
    type: `**Classic** produces both sum and difference frequencies. The sideband types keep only one, which turns the block into a frequency shifter.`,
    tracking: `Sets the carrier to the note you are playing (times **Freq Multiplier**), gliding to each new note in about 10 ms and holding the last note between notes. It overrides Frequency and Tempo. It follows single notes from about 70 Hz to 1.4 kHz; chords don't give it a clear pitch, so it holds its last note.`
  },

  options: {
    type: {
      Classic:
        "Your signal times the carrier: each partial becomes two, at its frequency plus and minus the carrier's. Neither the input nor the carrier is left in the wet signal.",
      'Upper Sideband':
        'A frequency shifter: every partial moves up by the carrier frequency. Because it adds the same number of hertz to every partial, harmonics stop being harmonic and the sound turns metallic.',
      'Lower Sideband':
        'A frequency shifter downwards: every partial moves down by the carrier frequency. Partials pushed below 0 Hz fold back up.'
    }
  },

  tips: `- **Ring mod and chords don't mix well.** Every note of the chord makes its own pair of sidebands, which quickly turns into noise. Single notes and simple riffs sound best.
- **Make it musical:** turn on Pitch Tracking with the multiplier at a whole number (1×, 2×, 3×) or a simple ratio. The result stays tied to your notes.
- **Frequency shifting for detune:** Upper or Lower Sideband with a carrier of 1–5 Hz gives a slow, swirling beating that is quite different from a chorus.
- Put Frequency on an expression pedal with a [modifier](/manual/modifiers) for wild, sweeping sci-fi effects.`
};
