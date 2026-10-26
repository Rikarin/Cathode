import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Phaser gives that swirling, liquid "whoosh" heard on countless funk, rock and 70s records. It passes your signal through a chain of all-pass filters, which shift the phase of different frequencies by different amounts. Mixed back with the dry sound, this cuts a set of notches in the spectrum, and an LFO sweeps those notches up and down.

Compared with a flanger, a phaser's notches are fewer and spread unevenly, so it sounds smoother and more vocal. Cathode's phaser goes from a simple four-stage pedal to a twelve-stage studio phaser, models the different control elements real phasers used (including the photocell of optical units), and has a Barberpole mode whose sweep seems to rise or fall forever.`,

  usage: `**Where it goes:** before the amp for the classic pedal sound (a 4-stage phaser into a cranked amp is a 70s rock staple), or after the amp for a cleaner, more studio-like swirl. Put it before delay and reverb.

**A starting point:** Order 4, Rate 0.5 Hz, Depth 80 %, Manual 800 Hz, Feedback 0, Mix 50 %. That is close to a classic orange four-stage pedal.

**Things to try:**
- **Feedback** 40–60 % for a more pronounced, vowel-like sweep.
- **Order** 8–12 for a deeper, more dramatic effect with more notches.
- **VCR Type** Photocell for the lopsided, breathing sweep of an optical phaser.
- **Type** Barberpole for an endless rising (or falling) sweep.`,

  params: {
    rate: `How fast the sweep moves. Slow (0.2–0.6 Hz) is the classic swirl; fast (3–6 Hz) gets close to a vibrato-like warble. In Barberpole mode it is how fast the endless rise travels. Ignored when **Tempo** is set.`,
    depth: `How wide the sweep is: up to two octaves either side of **Manual** at 100 %. At 0 % the notches sit still. In Barberpole mode it sets the span the endless sweep covers.`,
    center: `The centre frequency of the sweep, where the notches sit with the LFO in the middle. Low values give a dark, throaty sweep; high values a bright, airy one.`,
    stages: `The number of all-pass stages. Every two stages add one notch: 2 stages make one notch, 4 make two, up to 12 for six. More stages give a deeper, more complex sweep. In Barberpole mode each pair of stages becomes one second-order filter.`,
    feedback: `Feeds the phaser's output back into its input, which sharpens the notches and adds resonant peaks between them for a more pronounced, vocal sweep. Positive and negative values emphasise different frequencies, so each has its own vowel.`,
    mix: `The balance between dry and phased sound, at constant power. The notches are deepest at 50 %, where dry and wet are equal. At 100 % you hear only the phase-shifted signal, which on its own is a subtle vibrato.`,
    spread: `The offset between the left and right sweeps. 0° sweeps both sides together; 90–180° makes them sweep against each other for a wide, moving stereo image.`,
    level: `The block's output volume, applied to dry and wet alike.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    tempo: `Locks the sweep to the preset's tempo: one sweep cycle per note value you pick. When set, it replaces **Rate**.`,
    lfoType: `The shape of the sweep (Classic type only). Sine is the smooth default; Triangle sweeps at an even speed; the others are stepped or lopsided.`,
    lfoDuty: `Skews the LFO (Classic type only). For Triangle, Square and Trapezoid it sets how much of each cycle rises; for Astable it sets the curvature. At 50 % every shape is symmetric; the other shapes ignore it.`,
    lfoHicut: `Smooths the LFO with a low-pass filter (Classic type only). At its maximum it is off; lower values round off corners and soften the steps of **LFO Quantize**.`,
    lfoQuantize: `Steps the LFO into a fixed number of levels (Classic type only), so the notches jump between positions instead of sweeping: a step-phaser effect, great with **Tempo**.`,
    tone: `Tilts the wet signal around 1 kHz, by up to 6 dB either way. Negative makes the phased sound darker and warmer; positive brighter.`,
    feedbackPoint: `Where the feedback is taken from (Classic type only). **Output** feeds back the whole chain; **Middle** takes it from halfway through, which gives a different set of resonant peaks.`,
    minFreq: `The lowest frequency the sweep may reach. The sweep's bottom end is clamped here if Manual and Depth would take it lower.`,
    maxFreq: `The highest frequency the sweep may reach. Use Min and Max Frequency together to keep the sweep in a range you like.`,
    vcrType: `The element that turns the LFO into the filters' frequency, as in real phaser circuits (Classic type only). It changes the feel of the sweep rather than its range.`,
    vcrBias: `Shifts where the LFO sits within the sweep range (Classic type only). Positive pushes the sweep towards the top, negative towards the bottom; the LFO then spends time pinned at one end.`,
    exponent: `Bends the sweep towards one end (Classic type only). Above 1 it lingers at the low end; below 1 at the high end.`,
    photoAttack: `With VCR Type Photocell: how fast the photocell follows when the light gets brighter (sweeping up). Short is snappy; long is lazy.`,
    photoRelease: `With VCR Type Photocell: how fast the photocell follows when the light dims (sweeping down). Real photocells are slower to darken than to brighten, which is what makes optical phasers sound lopsided.`,
    type: `**Classic** is a normal phaser that sweeps up and down. **Barberpole** sweeps in one direction forever, an audio illusion like a barber's pole.`,
    direction: `In Barberpole mode: whether the endless sweep rises or falls.`,
    filterQ: `In Barberpole mode: the sharpness of the second-order all-pass filters. Higher values give narrower, more pronounced notches.`,
    lfoReset: `When on, engaging the block restarts the sweep from the same point, so it always starts the same way.`,
    globalMix: `When on, this block's Mix follows the global **Effects Mix** offset.`,
    stereoSpread: `Widens or narrows the wet signal's stereo image. 100 % leaves it as it is, 0 % folds it to mono, negative swaps the sides, beyond ±100 % widens past the speakers.`
  },

  options: {
    stages: {
      '2': 'One notch. A gentle, subtle phase.',
      '4': 'Two notches. The classic pedal phaser.',
      '6': 'Three notches.',
      '8': 'Four notches. Deeper and more complex.',
      '10': 'Five notches.',
      '12': 'Six notches. Big, studio-style phasing.'
    },
    lfoType: {
      Square: 'Softened square: the notches jump between two positions without clicking.',
      Random: 'Sample and hold: a new random position each cycle.',
      Log: 'Rounded tops and sharp troughs.',
      Exponential: 'Sharp peaks and rounded troughs.',
      Trapezoid: 'A clipped triangle that lingers at both ends of the sweep.',
      Astable: 'The charge-and-discharge curve of an analogue oscillator circuit; LFO Duty sets its curvature.'
    },
    vcrType: {
      Linear: 'Sweeps evenly in hertz: the notches seem to rush through the low end and crawl at the top.',
      Exponential: 'Sweeps evenly in octaves, so the movement sounds even. The default.',
      JFET: 'A square law, as in transistor phasers: slow at the bottom of the sweep, fast at the top.',
      Photocell:
        'An optical phaser: a lamp lights a photocell that follows with its own attack and release, so the sweep lags and leans to one side.'
    },
    type: {
      Classic: 'A normal phaser, swept up and down by the LFO.',
      Barberpole:
        'Two filter chains, half a cycle apart, crossfade as they sweep, so the notches seem to rise or fall without end. LFO Type and the other LFO controls do not apply.'
    }
  },

  tips: `- **Before the amp vs after:** in front of a driven amp the phaser's peaks get compressed and grainy, which is the classic rock sound. After the amp it stays clean and hi-fi.
- **Four stages, slow rate, no feedback** is the go-to for funk rhythm and Van Halen-style crunch.
- **Barberpole with Tempo** makes a hypnotic, never-ending riser for ambient builds.
- The Photocell VCR type pairs well with a slow rate: you hear the lamp's lag as a lopsided, breathing sweep. For the Uni-Vibe sound, though, use the [Vibe block](/manual/blocks/vibe), which models that circuit's unequal stages.`
};
