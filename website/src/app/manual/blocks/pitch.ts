import type { BlockDoc } from '../types';

/** The Custom Shifter table: one knob per voice for each of the twelve notes of the key, named as if the key were C. */
const customNotes: Record<string, string> = {
  C: "the key's root",
  Cs: 'one semitone above the root',
  D: 'two semitones above the root (the second)',
  Ds: 'three semitones above the root',
  E: 'four semitones above the root (the major third)',
  F: 'five semitones above the root (the fourth)',
  Fs: 'six semitones above the root',
  G: 'seven semitones above the root (the fifth)',
  Gs: 'eight semitones above the root',
  A: 'nine semitones above the root (the sixth)',
  As: 'ten semitones above the root',
  B: 'eleven semitones above the root (the major seventh)'
};

const customTable = Object.fromEntries(
  Object.entries(customNotes).map(([note, where]) => [
    `custom#${note}`,
    `Custom Shifter only: how far this voice shifts when you play ${where}. The knobs are named as if the key were C, so the same table works in any key: in the key of A, the "C" knob applies to the note A. Any value is allowed, diatonic or not.`
  ])
);

export const doc: BlockDoc = {
  summary: `The Pitch block shifts the pitch of your playing. One block covers nine quite different effects, chosen with **Type**: subtle detuning to double and thicken your sound, fixed-interval and intelligent (in-key) harmonies of up to four voices, a whammy pedal, an analogue-style octave divider, shimmering reversed "crystal" echoes, a tempo-synced arpeggiator, a virtual capo, and a fully custom harmoniser.

Most types listen to the note you play with a pitch tracker. It is monophonic, so the harmony and tracking features work best on single notes; chords are covered by the Detune type, the polyphonic Whammy and the Octave Divider's rough charm.

Many knobs only apply to some types; the editor hides the ones the current type doesn't use. The descriptions below say which types each knob is for.`,

  usage: `**Where it goes:** generally early, before drive and modulation, so the tracker hears a clean note. Detune and harmony are also great after the amp, where they shift the full distorted tone. Whammy is usually first in the chain, like the pedal.

**Quick recipes:**
- **Thickening a sound (doubling):** Type Detune, two voices at +10 and −10 cents, panned left and right, Mix 30–50 %. Add a few milliseconds of voice delay for an even wider double.
- **Harmony leads:** Type Diatonic, set **Key** and **Scale** to your song's key, Voice 1 Harmony +3rd, Mix about 50 %. Play a melody and the harmony follows in key.
- **Octave up or down:** Type Chromatic, Voice 1 Shift +12 or −12 (turn Voice 2 down if you only want one).
- **Whammy:** Type Whammy, Start 0, End +12, then put **Pedal** on an expression pedal with a [modifier](/manual/modifiers).
- **Dropped tuning without retuning:** Type Virtual Capo, Capo −2 for D standard.
- **Ambient shimmer:** Type Crystals with the defaults, Mix 30–40 %, into a big reverb.

**Intelligent harmony needs a key.** Diatonic voices pick the right interval for each note from the key and scale. If they sound wrong, the key is wrong: set it, use **Learn**, or let the MIDI or Auto key source find it.`,

  params: {
    type: `Which pitch effect the block is. Each type uses its own set of knobs; see the options below.`,
    mix: `The balance between dry and shifted sound (a straight crossfade: both at −6 dB at 50 %). For harmonies 40–60 % is typical; for subtle doubling 25–40 %. Whammy and Virtual Capo ignore it and are always fully wet, because a shifted copy just a few milliseconds behind the dry would comb-filter at small shifts.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,

    'voice#Shift': `Chromatic only: this voice's fixed shift in semitones. +12 is an octave up, −12 an octave down, +7 a fifth up. The interval stays the same whatever note you play, so it can go out of key; for in-key harmonies use Diatonic.`,
    'voice#Harmony': `Diatonic only: this voice's interval in steps of the scale. "+3rd" means two scale notes up, so it is a major or a minor third depending on the note you play, staying in the key. A note you play that isn't in the scale is harmonised as if it were the scale note just below it.`,
    'voice#Level': `This voice's volume (Detune, Chromatic, Diatonic and Custom Shifter). At its minimum the voice is off and costs nothing, so turn unused voices all the way down. By default voices 1 and 2 are on and 3 and 4 off.`,
    'voice#Pan': `This voice's position in the stereo field (Detune, Chromatic, Diatonic and Custom Shifter). Spread voices left and right for a wide harmony or double.`,
    'voice#Delay': `Delays this voice (Detune, Chromatic, Diatonic and Custom Shifter). A few milliseconds widens a double; 50–150 ms makes the voice an echo; with **Feedback** it repeats. Replaced by **Delay Tempo** when that is set.`,
    'detune#': `Detune only: how far this voice is detuned, in cents (hundredths of a semitone). ±5–15 cents is the classic thickening; more sounds out of tune on purpose.`,
    'delayTempo#': `Sets this voice's delay from the preset's tempo instead of the Delay knob (capped at one second). Off uses the knob.`,
    'feedback#': `Repeats this voice (Detune, Chromatic, Diatonic and Custom Shifter). With **Feedback Mode** Delay, the voice's delay line repeats at the same pitch; with Shifter, each repeat goes back through the shifter, so it climbs (or falls) by the interval again every time. Negative values invert each repeat's polarity. In Delay mode it needs some voice delay to be heard as echoes; in Shifter mode the shifter's own short delay is enough for the pitch to spiral.`,

    key: `The key of your song, for the in-key types (Diatonic and Custom Shifter). Used directly when **Key Source** is Fixed, and as the fallback for MIDI and Auto until they have found a key.`,
    scale: `The scale the Diatonic harmonies use, on **Key**. Major and Minor cover most songs; the modes, pentatonics and blues give other colours; Custom uses the notes on the Scale page.`,
    keySource: `Where the in-key types get their key. **Fixed** uses Key and Scale. **MIDI** follows the chord you hold on a keyboard connected to Cathode's MIDI input. **Auto** works out the key from the notes you play.`,
    temperament: `Diatonic and Custom Shifter: **Equal** uses ordinary equal-tempered intervals. **Just** tunes them to pure (just) ratios on the key, so a major third is exactly 5:4: smoother, beatless harmonies, especially on sustained notes.`,
    glide: `Diatonic and Custom Shifter: when the harmony interval changes from one note to the next, the voice slides to it over this time instead of jumping. A little (20–50 ms) smooths the harmony; a lot gives a deliberate portamento.`,
    learn: `Diatonic: while on, the key follows the note you are playing, so play the root of your song's key and the harmonies lock to it. When you turn it off, that key stays until you move the Key knob.`,
    customNotes: `For Scale Custom: how many of the Note knobs make up the scale (4 to 8).`,
    'customNote#': `For Scale Custom: one note of your scale, as a degree above the key's root (1 is the root, b3 a minor third, 5 a fifth and so on). The root is always part of the scale, and repeated notes are dropped.`,

    whammyStart: `Whammy: the shift with **Pedal** at 0 %, in semitones. Usually 0 (no shift) so the heel-down position is your normal sound.`,
    whammyEnd: `Whammy: the shift with **Pedal** at 100 %. +12 is the classic octave-up dive; −12 dives down; +24 two octaves up.`,
    pedal: `Whammy: the pedal position, sweeping the shift from Whammy Start to Whammy End. Put it on an expression pedal with a [modifier](/manual/modifiers) to play it with your foot.`,
    whammyTracking: `Whammy: how the shifter works. **Mono** follows your note for the cleanest single-note shifting. **Poly** handles chords cleanly too, at the cost of about 43 ms of delay. **Off** doesn't follow the pitch at all: a rougher, classic dual-tap shifter.`,
    octave1Level: `Octave Divider: the level of the voice one octave below your note.`,
    octave2Level: `Octave Divider: the level of the voice two octaves below your note.`,
    capo: `Virtual Capo: transposes everything by this many semitones, from −12 to +12. −1 gives E♭ tuning, −2 D standard, +2 or more acts like a capo.`,
    capoDetune: `Virtual Capo: a fine adjustment in cents, for example to match a recording that isn't quite at A = 440 Hz.`,

    crystalsShift: `Crystals: the pitch of the grains, in semitones. +12 is the classic octave-up shimmer.`,
    crystalsTime: `Crystals: the delay time between repeats. Changing it while playing glides, with a pitchy smear.`,
    crystalsFeedback: `Crystals: how much of each repeat feeds the next. Higher values give more, longer-lasting repeats.`,
    crystalsFeedbackType: `Crystals: **Shifted** feeds the shifted grains back, so each repeat climbs again (an octave up, then two, ...) into a rising shimmer. **Unshifted** feeds back the unshifted repeat, so every repeat is shifted just once and they all sit at the same interval.`,
    crystalsSplice: `Crystals: the length of each grain. Short grains are glittery and granular; long ones smoother. Replaced by **Crystals Splice Tempo** when that is set.`,
    crystalsSpliceTempo: `Crystals: sets the grain length from the preset's tempo instead (at most 200 ms). Off uses Crystals Splice.`,
    crystalsDirection: `Crystals: **Reverse** plays every grain backwards, the classic sparkly, swelling crystal sound. **Forward** plays them forwards, a smoother granular pitch echo.`,
    crystalsDiffusion: `Crystals: smears the repeats with a diffuser in the feedback loop, so they blur into a wash instead of distinct echoes.`,

    arpTempo: `Arpeggiator: the length of each step, as a note value at the preset's tempo. The arpeggiator always follows the tempo (120 BPM if there is none).`,
    arpSteps: `Arpeggiator: how many steps the sequence has before it repeats.`,
    'arpStep#': `Arpeggiator: the shift of this step, in semitones. The default sequence plays root, fifth, octave and so on; set your own to spell a chord or a riff.`,
    arpRepeats: `Arpeggiator: **Infinite** runs the sequence continuously. A number plays the sequence that many times from each new note you pick, then fades out until the next note.`,
    arpAmpShape: `Arpeggiator: the volume shape of each step. Flat holds level; Decay starts loud and fades, like a pluck; Swell fades in; Triangle rises then falls.`,
    arpAmpAlpha: `Arpeggiator: the curve of the Amp Shape. At 50 % it is a straight line. Higher values bend it one way (a shorter, punchier decay; a swell that comes in late); lower values the other way (a decay that holds before it drops; a swell that rises early).`,
    arpPanShape: `Arpeggiator: how the steps are placed in stereo. Center keeps them in the middle; Alternate bounces left and right; Ramp moves from left to right across the sequence; Random gives each step its own fixed place.`,
    arpPanAlpha: `Arpeggiator: how wide the pan pattern goes. 0 % is centred; 100 % uses the full width.`,

    detuneLfoRate: `Detune: the speed of an LFO that sways the voices' detune, so the thickening moves like a chorus. Ignored when Detune LFO Tempo is set.`,
    detuneLfoTempo: `Detune: locks the detune LFO to the preset's tempo, one cycle per note value. Off uses Detune LFO Rate.`,
    detuneLfoDepth: `Detune: how much the LFO sways the detune, in cents. The four voices sway a quarter or half cycle apart, so they drift against each other. At 0 the detune stays fixed.`,

    masterLevel: `Scales every voice's level at once (and is the output level of the single-voice types).`,
    masterPan: `Scales every voice's pan at once (Detune, Chromatic, Diatonic, Custom Shifter): 100 % uses the pans as set, 0 % centres them all, negative mirrors them.`,
    masterFeedback: `Scales every voice's Feedback at once (Detune, Chromatic, Diatonic, Custom Shifter).`,
    masterPitch: `Scales every voice's shift at once, for Detune (the cents) and Chromatic (the semitones). At 50 % an octave becomes a tritone and 10 cents becomes 5. Put it on a modifier for whammy-like sweeps of a multi-voice shift.`,
    masterDelay: `Scales every voice's delay at once (Detune, Chromatic, Diatonic, Custom Shifter).`,
    feedbackMode: `What the voice Feedback repeats. **Delay** repeats each voice through its own delay at the same pitch, like an echo. **Shifter** sends each repeat back into the shifter, so the pitch keeps climbing or falling with every repeat (the classic "pitch spiral").`,

    inputMode: `**Mono** shifts the selected input (see **Input Select**) in every voice. **Stereo** shifts the left input in voices 1 and 3 and the right in voices 2 and 4, and keeps the sides apart for the single-voice types, so a stereo signal stays stereo.`,
    tracking: `How the pitch tracker follows your playing. **Smooth** changes note only once you are clearly on a new one, so bends and vibrato don't flip the harmony. **Fast** reacts sooner, for quick lines. **Off** stops tracking: the harmony stays on its last interval and the shifters run free.`,
    splice: `The shortest splice of the time-domain shifter (Detune, Whammy Mono and Off, Arpeggiator, Virtual Capo). Longer splices warble less on sustained notes but smear fast passages more.`,
    inputGain: `The level going into the shifters. Lower it if the shifted voices sound strained or distorted.`,
    lowCut: `A high-pass filter on the shifted (wet) signal only. Raise it to keep octave-down voices from muddying the low end.`,
    highCut: `A low-pass filter on the wet signal only. Lower it to soften the artefacts of large upward shifts.`,
    detectorSource: `Where the pitch tracker listens. **Block Input** is the normal choice. **Row 1–6** takes the signal cabled in from that grid row for the tracker only (the shifters still use the rest of the block's input), so you can track a clean signal while shifting a distorted one. **Input 1–4** and **USB 5/6, 7/8** listen to one of your interface's inputs, for example a second instrument.`,
    inputSelect: `Which side of a stereo input the tracker hears, and the shifters in Mono input mode: the sum of both, or only left or right.`,
    killDry: `Mutes the block's dry signal, so Mix sets only the level of the shifted sound. Use it when the block is on a parallel row and the dry already reaches the output another way.`,
    formant: `Keeps the natural tone colour (the formants) of your guitar while the pitch moves, instead of the "chipmunk" or "giant" character a large shift gives. Available on Chromatic, Diatonic, Whammy, Arpeggiator, Virtual Capo and Custom Shifter. Off by default.`,
    'formantShift#': `With Formant Correction on: moves the tone colour of this voice up or down, independent of its pitch. Positive sounds smaller and brighter, negative bigger and darker. The single-voice types use the first one. Has no effect while Formant Correction is off.`,

    globalMix: `When on, this block's Mix follows the global **Effects Mix** offset.`,
    ...customTable
  },

  options: {
    type: {
      Detune:
        'Up to four voices, each detuned by a few cents, with level, pan, delay and an optional swaying LFO. For doubling and thickening; works on chords.',
      Chromatic:
        'Up to four voices at fixed semitone shifts (Voice Shift). Simple octaves and fifths; the interval never changes, so it can step out of key.',
      Diatonic:
        'Intelligent harmony: up to four voices at intervals in the key and scale (Voice Harmony), choosing a major or minor interval per note so the harmony stays in key.',
      Whammy: 'One voice swept between Whammy Start and Whammy End by the Pedal knob. Always 100 % wet.',
      'Octave Divider':
        'An analogue-style octave pedal: one and two octaves below, made by dividing your note with flip-flops. No delay at all, glitchy on chords, which is part of its charm.',
      Crystals:
        'Pitch-shifted grains on a feedback delay, reversed by default: sparkling, shimmering, ambient repeats.',
      Arpeggiator:
        'Steps through a sequence of up to 16 shifts in time with the tempo, each step with its own volume shape and pan.',
      'Virtual Capo':
        'Transposes everything by a fixed amount, for alternate tunings without retuning. Always 100 % wet.',
      'Custom Shifter':
        'Up to four voices whose interval for each of the twelve notes of the key you set yourself, in the V1–V4 tables.'
    },
    keySource: {
      Fixed: 'The Key and Scale knobs.',
      MIDI: 'The chord held on a MIDI keyboard (any channel): its lowest note is the key, and the scale is your Scale if it contains every note of the chord, otherwise the first scale that does (A minor picks Minor, G7 picks Mixolydian). The harmony voices then snap to the nearest chord tone. Letting go of the keys keeps the last chord.',
      Auto: 'The key is estimated from the notes you play over the last few phrases; it needs under a second of playing to make a first guess and changes only when another key clearly fits better. The scale is your Scale if it is major or minor like the estimate, otherwise plain Major or Minor.'
    },
    whammyTracking: {
      Mono: 'Follows your note for clean single-note shifts with low delay.',
      Poly: 'A phase vocoder that shifts chords cleanly, with about 43 ms of delay.',
      Off: 'No pitch following: the classic, warblier dual-tap sound.'
    },
    tracking: {
      Smooth: 'Ignores small pitch changes (bends, vibrato) before switching the harmony to a new note.',
      Fast: 'Switches sooner, for fast lines.',
      Off: 'No tracking: the harmony stays where it is and the shifters splice freely.'
    },
    feedbackMode: {
      Delay: 'Repeats at the same pitch, like an echo of the voice.',
      Shifter: 'Each repeat is shifted again, so the pitch spirals up or down.'
    },
    crystalsFeedbackType: {
      Shifted: 'Repeats climb further with each pass.',
      Unshifted: 'Every repeat is one shift away from the original.'
    },
    arpAmpShape: {
      Flat: 'Each step at a steady level.',
      Decay: 'Each step starts loud and fades.',
      Swell: 'Each step fades in.',
      Triangle: 'Each step rises then falls.'
    },
    arpPanShape: {
      Center: 'All steps in the middle.',
      Alternate: 'Steps alternate left and right.',
      Ramp: 'Steps move from left to right across the sequence.',
      Random: 'Each step gets its own place, the same on every pass.'
    },
    temperament: {
      Equal: 'Standard equal-tempered intervals, as on a guitar.',
      Just: 'Pure five-limit ratios on the key: smoother, beatless harmonies.'
    },
    inputMode: {
      Mono: 'One input (per Input Select) feeds every voice.',
      Stereo: 'Voices 1 and 3 shift the left input, 2 and 4 the right.'
    }
  },

  tips: `- **Harmonies sound wrong?** Nine times out of ten the key or scale is wrong. A minor-key song with Scale Major will give you sour thirds on some notes.
- **Play cleanly for the tracking types.** Single notes, the neck pickup, and the Pitch block before your drives all help the tracker. Chords give it no single pitch: use Detune, Whammy Poly or the Octave Divider for those.
- **The shifted sound is slightly late.** Detune, Whammy, Arpeggiator and Virtual Capo add roughly 5–15 ms to the wet path, Chromatic, Diatonic and Custom Shifter about 29 ms, polyphonic Whammy about 43 ms, and the Octave Divider nothing. Mixed with the dry, a little lateness actually helps a harmony sound like a second player. The dry signal is never delayed.
- **Shifting down a lot sounds thin or boomy?** Turn on Formant Correction and use Low Cut on the wet.
- **Formant Correction on an octave up** is a few decibels quieter than without; nudge Master Level up to compensate.
- **A parallel harmony:** put the Pitch block on its own row with Kill Dry on and Mix at 100 %, then blend it with a [Mixer](/manual/blocks/mixer).`
};
