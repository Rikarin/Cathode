import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Synth turns your guitar into a monophonic synthesizer. It listens to the note you play, works out its pitch, and plays up to three oscillator voices at that pitch (or shifted from it), each with its own waveform, filter, level and pan. A volume envelope follows your picking, so the synth swells and fades with your playing, and the filters can open as you dig in.

It is monophonic: play single notes. A chord has no single pitch, so the tracking voices stay silent while you strum one.`,

  usage: `**Where it goes:** early in the chain, before any drive, compression or modulation, so the pitch tracker hears a clean, clear guitar. Neck pickup and tone rolled back a little help. The synth's output can then go through the amp, or alongside it on a parallel row.

**A starting point:** the defaults give one saw-wave voice (Voice 1) at your pitch through a low-pass filter. Voices 2 and 3 are set up an octave below and a fifth above, but at −80 dB; raise their levels to hear them. Set Mix to 100 % for synth only, or lower it to blend in the guitar.

**Things to try:**
- Voice 2 at −12 st with a Square wave for a fat octave-down bass synth.
- A high **Env Amount** with a resonant low-pass for a funky, quacky filter that opens as you pick.
- A long **Attack** for slow, bowed-sounding swells.
- **Glide** of 50–100 ms for a portamento lead.`,

  params: {
    'voice#Shape': `The waveform of this voice. Saw is bright and buzzy, Square hollow and reedy (its width set by **Duty**), Triangle and Sine soft and pure. Random, White Noise and Pink Noise are for effects and textures. Off silences the voice.`,
    'voice#Track': `What plays this voice. **Pitch + Env** follows the note you play and your picking dynamics. **Envelope** plays the voice's own fixed **Frequency**, but still swells and fades with your picking. **Off** is a constant drone at its Frequency, at a fixed level (half of full scale, about −6 dB) whatever you play; only its filter opens with your picking.`,
    'voice#Freq': `The voice's own pitch, used when Track is Envelope or Off. Ignored when the voice follows your playing.`,
    'voice#Shift': `Transposes the voice in semitones: −12 is an octave down, +7 a fifth up, +12 an octave up. Applies to the tracked pitch and to Frequency alike.`,
    'voice#Tune': `Fine-tunes the voice in cents (hundredths of a semitone). Detune two voices by a few cents against each other for a thicker, chorused synth sound.`,
    'voice#Level': `The voice's volume. At its minimum the voice is silent and costs nothing.`,
    'voice#Duty': `The pulse width of the Square shape. 50 % is a true square (hollow, clarinet-like); narrower or wider is thinner and more nasal. Only the Square shape uses it.`,
    'voice#Pan': `Places the voice in the stereo field. Spread two or three voices left and right for a wide synth.`,
    'voice#FilterType': `The voice's filter: Low Pass keeps the lows (warm, the classic synth sound), Band Pass a band around the cutoff (nasal, vocal), High Pass the highs (thin, airy).`,
    'voice#Cutoff': `The filter's base frequency, where it sits when you are not playing hard. **Env Amount** opens it upwards from here as you play.`,
    'voice#Resonance': `Emphasis at the cutoff frequency. Low values are smooth; high values add a whistling peak that makes filter sweeps sound squelchy.`,
    envAmount: `How far your picking opens every voice's filter: up to five octaves above Cutoff at 100 % when you play hard. At 0 % the filters stay at their Cutoff. The harder you pick, the brighter it gets, the classic envelope-filter quack. A drone voice (Track Off) keeps its level whatever you play, but its filter still opens with your picking and rests at Cutoff when you stop.`,
    attack: `How fast the synth's volume (and filter) rises to follow your picking. Short is a sharp, plucky start; long (200 ms and more) gives slow, violin-like swells.`,
    release: `How fast the synth fades once your note stops or falls below the Threshold. Short is tight and staccato; long lets notes hang on.`,
    glide: `How long the pitch takes to slide to a new note (portamento). 0 jumps straight to each note. A note that starts from silence always starts on pitch; glide only acts between notes you play legato.`,
    threshold: `The input level below which the synth's gate closes. Set it just above your guitar's noise and string buzz so the synth doesn't sputter between notes, but below your softest playing.`,
    mix: `The balance between your guitar (dry) and the synth (a straight crossfade). 100 % is synth only.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    globalMix: `When on, this block's Mix follows the global **Effects Mix** offset.`
  },

  options: {
    'voice#Shape': {
      Random:
        "A new random level held for each cycle of the voice's pitch: a gritty, noisy tone that still has a pitch.",
      'White Noise':
        'Hiss with no pitch, equal energy at every frequency. Through a filter and the envelope it makes wind, breath and percussive effects.',
      'Pink Noise': 'Softer, darker noise (−3 dB per octave).',
      Off: 'The voice is silent.'
    },
    'voice#Track': {
      'Pitch + Env':
        'Plays the note you play (plus Shift and Tune), shaped by your picking. Silent while the tracker hears no clear note.',
      Envelope:
        'Plays its own Frequency (plus Shift and Tune), but its volume still follows your picking. Good for a fixed drone note that you "play" rhythmically.',
      Off: 'A constant drone at Frequency, at a fixed level (about −6 dB), independent of the input.'
    }
  },

  tips: `- **The synth speaks about 25 ms after you pick.** That is how long the pitch tracker needs to lock onto a new note. Pick cleanly and let it catch up; the dry guitar (Mix below 100 %) is not delayed.
- **Tracking glitches?** Use the neck pickup, roll back the tone, play single notes cleanly, and keep drive pedals after this block. Raise **Threshold** if it sputters as notes die.
- **A drone that never stops** is Track Off. Use Envelope instead if you want the fixed note to follow your dynamics.
- The volume follows the level of your playing, so a compressor before the synth evens it out.`
};
