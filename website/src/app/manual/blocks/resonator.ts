import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Resonator adds four tuned, ringing "strings" to your sound. Each one is a short delay with feedback (a comb filter, the same principle as a plucked-string synthesizer) that rings at its own pitch and its harmonics whenever your playing contains energy near them. Tune the four to a chord and anything you play, even a muted strum or a pick scrape, makes that chord ring out, like a sitar's sympathetic strings or a piano with the sustain pedal down.

You can tune the resonators to a chord on a root note, or set four free frequencies.`,

  usage: `**Where it goes:** before the amp it adds a drone-like, sitar-ish shimmer that the amp then distorts; after the amp (or on a clean sound) it is clearer and more like sympathetic strings or a metallic ambience. Put it before delay and reverb.

**A starting point:** Tuning Chord, Root on the key of your song, Chord Major or Minor, Decay 1–2 s, Damping 25–40 %, Mix 30–50 %. Play in that key and listen to the chord ring along.

**Things to try:**
- **Octaves** or **Fifths** on the song's root for a drone that fits almost anything in that key.
- A short Decay (0.1–0.3 s) for a boxy, metallic body resonance.
- Long Decay with high Damping for soft, warm, string-like swells.
- **Filter Location** Pre on each voice so only notes near its pitch set it ringing.`,

  params: {
    tuning: `How the four resonators are tuned. **Chord** builds them from **Root** and **Chord**; **Frequency** uses the four Freq knobs.`,
    root: `In Chord tuning: the chord's root note, as a MIDI note number (45 is A2, 110 Hz; 48 is C3). Set it to the key of your song.`,
    chord: `In Chord tuning: the four notes the resonators ring at, built on Root. Octaves stacks four octaves; Fifths stacks fifths; Power is root, fifth, octave and the fifth above; the triads (Major, Minor, Sus2, Sus4) add the octave as the fourth note; the sevenths use all four notes.`,
    'freq#': `In Frequency tuning: the pitch of this resonator, in hertz. Ignored in Chord tuning.`,
    'level#': `The volume of this resonator.`,
    'pan#': `The stereo position of this resonator. Spreading the four voices out (the default is −75, −25, +25 and +75 %) makes the ringing wide and spacious. In Stereo input mode it becomes a balance.`,
    decay: `How long the ringing lasts: the time for each resonator to fall by 60 dB. Every voice decays at the same rate whatever its pitch. Short is a percussive, boxy ring; long a sustained drone.`,
    damping: `Darkens the ringing over time, by putting a low-pass filter inside each resonator's loop (from 20 kHz at 0 % down to 1 kHz at 100 %). The upper harmonics die away sooner, like a plucked string. Low damping is bright and metallic; high damping soft and warm.`,
    mix: `The balance between dry and resonated sound (a straight crossfade). 30–50 % adds sympathetic ringing to your playing; 100 % is the resonators alone.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    inputGain: `The level going into the resonators. Lower it if strong notes make the ringing too loud.`,
    masterLevel: `Scales all four resonators' levels at once.`,
    masterPan: `Scales all four pans at once: 100 % uses them as set, 0 % centres every voice, negative mirrors them.`,
    masterFeedback: `Scales every voice's **Feedback** at once. Turning it down shortens all the decays together.`,
    masterQ: `Scales every voice's **Filter Q** at once.`,
    inputMode: `**Mono** feeds every resonator the sum of left and right, and places each with its pan. **Stereo** runs a second set of resonators, so the left input rings in the left set and the right in the right; the pans then act as balances.`,
    'feedback#': `Scales this resonator's feedback. 100 % gives the decay the **Decay** knob asks for; lower values make this voice die away sooner than the others.`,
    'filterLocation#': `Adds a band-pass filter at this resonator's own pitch. **Pre** filters what goes in, so only the parts of your playing near its pitch excite it: it rings more selectively. **Post** filters what comes out, so it rings with mainly its fundamental, a purer tone. **Off** leaves it unfiltered.`,
    'filterQ#': `The width of this resonator's band-pass filter (when Filter Location is Pre or Post). Higher Q is narrower and more selective.`,
    globalMix: `When on, this block's Mix follows the global **Effects Mix** offset.`
  },

  options: {
    chord: {
      Octaves: 'Root and three octaves above.',
      Fifths: 'Root and three stacked fifths (root, fifth, ninth, thirteenth).',
      Power: 'Root, fifth, octave and the fifth above that.',
      Major: 'Root, major third, fifth and octave.',
      Minor: 'Root, minor third, fifth and octave.',
      Sus2: 'Root, second, fifth and octave.',
      Sus4: 'Root, fourth, fifth and octave.',
      'Major 7': 'Root, major third, fifth and major seventh.',
      'Minor 7': 'Root, minor third, fifth and minor seventh.',
      'Dominant 7': 'Root, major third, fifth and minor seventh.'
    },
    'filterLocation#': {
      Off: 'No filter: the resonator rings at its pitch and all its harmonics.',
      Pre: "Only input near the resonator's pitch excites it.",
      Post: "The resonator's output is filtered to its fundamental."
    }
  },

  tips: `- **Match the key.** The resonators ring at fixed pitches, so they sound great in their key and clash outside it. Use [scenes](/manual/presets-and-scenes) or a [modifier](/manual/modifiers) on Root to follow chord changes.
- **Gets loud on sustained notes:** a note held exactly on a resonator's pitch builds up more than broadband playing does. Lower Input Gain or Master Level if certain notes jump out.
- **Long decays and high gain** can turn into a wall of drone. Keep Mix moderate before a high-gain amp.`
};
