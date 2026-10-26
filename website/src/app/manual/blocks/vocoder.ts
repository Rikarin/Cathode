import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Vocoder is the "talking synth" effect: it imposes the tone colour of one signal (the **modulator**, usually your voice) onto another (the **carrier**, usually a synth tone or your guitar). The modulator is split into a bank of frequency bands, and how loud each band is moment to moment sets the volume of the matching band of the carrier. Speak into a mic and the carrier "speaks" your words.

The carrier can be a built-in oscillator (saw, pulse or noise, at a note or a chord) or one side of the block's input. The modulator can be the block's input, a signal from another row of the grid, or one of your audio interface's inputs directly, so a microphone on Input 2 can talk through your guitar.`,

  usage: `**The classic rig: talk through your guitar.**
1. Plug a microphone into a second input of your audio interface, say Input 2 (see [Audio setup](/manual/audio-setup)).
2. Put the Vocoder after your amp and cab.
3. Set **Analysis Source** to Input 2 and **Carrier** to Left Input (the guitar arrives in stereo, so either side works).
4. Play chords and speak or sing into the mic. With Mix at 100 % you hear only the vocoded guitar.

**Robot voice without a guitar:** set Carrier to Saw or Pulse, pick a **Note** and a **Chord**, and use the mic as the modulator. The carrier then plays that fixed chord, shaped by your voice.

**Starting values:** 16–20 bands, 100 Hz to 8 kHz, Resonance around 3, Attack 2–5 ms, Decay 30–60 ms. More bands are clearer; fewer are more robotic.`,

  params: {
    carrier: `What gets shaped. **Saw** is bright and the most intelligible; **Pulse** hollower; **Noise** turns the effect into a whisper. **Left Input** or **Right Input** use one side of the block's own input, such as your guitar, as the carrier.`,
    note: `The pitch of the internal carrier (Saw or Pulse), as a MIDI note number: 48 is C3, 60 is middle C. The carrier stays on this note; it does not follow your playing.`,
    chord: `Turns the internal carrier into a chord built on **Note**: Single, an octave, a fifth, or major, minor, sus4, major 7 or minor 7 triads and sevenths. The voices are mixed at equal power.`,
    attack: `How fast each band responds when the modulator gets louder in it. Short attacks keep consonants crisp; longer ones soften and smear the articulation.`,
    release: `How fast each band fades once the modulator gets quieter in it. Short decays are choppy and precise; longer ones are smooth and legato, but words blur together.`,
    emphasis: `Tilts the carrier's bands towards the treble, by up to 6 dB per octave around 1 kHz at 100 %. Consonants live up there, so this keeps words intelligible. Lower it for a darker, smoother sound.`,
    mix: `The balance between the block's input (dry) and the vocoded sound. Both stay at full level up to 50 %, then the other fades out: 100 % is vocoder only.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    bands: `How many frequency bands the vocoder uses. More bands give clearer, more natural words; fewer give a cruder, more robotic sound.`,
    minFreq: `The lowest band's frequency. The bands are spread evenly in pitch between Min and Max Frequency.`,
    maxFreq: `The highest band's frequency. Around 8 kHz keeps sibilants; lower values give a darker, telephone-like result.`,
    resonance: `The sharpness (Q) of the band filters. Low values overlap the bands for a smooth, blended sound; high values make each band narrow and ringing, more metallic and synthetic.`,
    freqShift: `Moves the carrier's bands up or down against the analysis bands, which shifts the vowel sounds (formants). Above 1× sounds smaller and chipmunk-like; below 1× bigger and deeper.`,
    freeze: `Holds every band's level where it is, so the carrier keeps the last spectrum: freeze a vowel and play chords through it.`,
    hpMix: `Adds the modulator's own content above the highest band back in. That brings back the "s" and "t" sounds a vocoder otherwise loses, at the cost of hearing a little of the raw modulator.`,
    masterPan: `Scales every band's pan. 100 % uses the band pans as set, 0 % centres them all, negative mirrors them.`,
    inputGain: `The level of the modulator going into the analysis. Lower it if a loud mic makes the result harsh.`,
    analysisChannel: `Which side of the modulator signal the analysis hears: the sum of both, or only left or right. With Carrier set to Left or Right Input and the modulator coming from the block's input, L+R automatically means "the other side", so one side can carry the guitar and the other the voice.`,
    analysisSource: `Where the modulator comes from. **Block Input** uses the block's own input. **Row 1–6** takes it from whatever is cabled into the Vocoder from that grid row; the other rows still feed the block input, which is where an input carrier comes from. **Input 1–4** and **USB 5/6, 7/8** read your audio interface's inputs directly, the easy way to use a microphone.`,
    'bandLevel#': `The level of this band of the carrier. Use the band levels to shape the vocoder's tone, like a graphic EQ on the result. Bands beyond the **Bands** count are not used.`,
    'bandPan#': `The stereo position of this band. Alternating bands left and right gives a wide, spacious vocoder.`
  },

  options: {
    carrier: {
      Saw: 'A bright, buzzy oscillator at Note (and Chord): the classic robot voice.',
      Pulse: 'A hollower, narrower pulse wave at Note (and Chord).',
      Noise: 'White noise: the words come out whispered.',
      'Left Input': "The left side of the block's input is the carrier: put your guitar here.",
      'Right Input': "The right side of the block's input is the carrier."
    },
    analysisSource: {
      'Block Input': "The block's own input is the modulator.",
      'Row 1':
        'The signal cabled into the Vocoder from grid row 1 is the modulator (likewise Rows 2–6). If that row has no cable into the block, the modulator is silent.'
    }
  },

  tips: `- **Silence in, silence out.** Every band is gated by the modulator, so if the modulator is silent (wrong input, no cable from the chosen row), you hear nothing from the vocoder at all.
- **A guitar carrier wants sustain.** Chords, compression and some drive before the vocoder give it more to shape. A dull, quiet carrier gives a mumbled result.
- **Intelligibility:** raise HF Emphasis and Highpass Mix, use 16 or more bands, and keep Attack short.
- The vocoder carrier is fixed at Note and Chord. To change chords with the song, put **Note** on a [modifier](/manual/modifiers) or use a guitar carrier.`
};
