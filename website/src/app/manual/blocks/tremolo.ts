import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Tremolo/Panner rhythmically changes your volume (tremolo) or moves your sound from side to side (auto-panner). It covers the classic amp tremolos too: the bias tremolo of tweed and blackface amps, the harmonic tremolo of the brown-panel era, and the lamp-and-photocell optical tremolo.

There is no dry/wet mix here: the effect works on the whole signal, and **Depth** sets how strong it is.`,

  usage: `**Where it goes:** amp tremolos sit inside the amp, so the most authentic spot is after the amp (and cab), before delay and reverb. Placing it before the amp also works, but a driven amp will squash the volume dips. A panner wants to be late in the chain, after the reverb if you want the whole sound to move, before it if you want the reverb to stay still.

**A starting point:** Type Tremolo, Rate 4–6 Hz, Depth 50 %, LFO Type Sine. For surf and vintage country, try Optical Trem with Depth 70–90 %.

**Things to try:**
- **Tempo** set to 1/8 or 1/8T so the pulse locks to the song.
- **LFO Type** Square for a choppy, stuttering "helicopter" effect.
- **Harmonic Trem** for a subtle, swirling, almost phaser-like throb.
- **Trigger Level** so each strum restarts the pulse in time with your picking.`,

  params: {
    type: `The kind of modulation. Tremolo, Bias, Harmonic and Optical change the volume; Panner moves the sound between left and right.`,
    rate: `How fast the pulse or pan moves, in cycles per second. 2–4 Hz is a slow, lilting throb; 6–8 Hz the fast shimmer of a vintage amp. Ignored when **Tempo** is set.`,
    depth: `How strong the effect is. For the tremolo types, the volume dips to 100 % minus this at the bottom of each cycle (values past 100 % act as 100 %). For Panner, where it is labelled **Width**, it sets how far the sound swings; past 100 % the swing overshoots the sides and lingers there, a harder, more obvious pan.`,
    shape: `The shape of the LFO. Sine is smooth and vintage; Triangle is even; Square is choppy on/off; the saws give a pulsing ramp up or down.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    tempo: `Locks the rate to the preset's tempo: one cycle per note value you pick (1/8 = two pulses per beat). When set, it replaces **Rate**.`,
    duty: `Skews the LFO. For Triangle, Square and Trapezoid it sets how much of each cycle is the rising part (or, for Square, how long it stays up); for Astable it sets the curvature. 50 % is symmetric. The other shapes ignore it.`,
    phase: `Offsets the right channel's LFO from the left's (tremolo types only). At 0° both sides pulse together; at 180° one side is loud while the other is quiet, a stereo tremolo that seems to bounce between speakers. Panner ignores it.`,
    panCenter: `For Panner: moves the middle of the swing to the left (negative) or right (positive), so the sound pans around an off-centre point.`,
    crossover: `For Harmonic Trem: the frequency that splits the signal into a low and a high band, which then pulse in opposite directions. Around 500–800 Hz is typical of the vintage amps.`,
    trigger: `When set above its minimum, the LFO restarts from the beginning of its cycle every time your playing rises above this level (it re-arms once the signal falls 6 dB below it). Set it just below your picking level and each new strum starts the pulse in time with you. At the minimum it is off.`,
    crossoverSlope: `For Harmonic Trem: how steep the split between the two bands is. 6 dB/oct overlaps the bands a lot, for a softer effect; 24 dB/oct separates them sharply, for a clearer high-against-low throb.`,
    startPhase: `Where in its cycle the LFO starts when you engage the block. Set it so the first pulse lands where you want it, for example 90° to start on a peak with a Sine.`
  },

  options: {
    type: {
      Tremolo: 'Plain volume modulation: the gain dips from full to 100 % minus Depth and back.',
      Panner:
        'Auto-pan: the mono sum of the input is moved between left and right at constant power, so the level stays even as it moves.',
      'Bias Trem':
        "The tremolo feeds a gentle saturation stage, as in an amp that wobbles its power tubes' bias: loud notes squash and thicken at the top of each pulse.",
      'Harmonic Trem':
        'The signal is split at Crossover Freq and the low and high bands pulse in opposite directions, as in brown-panel amps. Subtle, swirling, almost phase-like.',
      'Optical Trem':
        'The LFO lights a lamp read by a photocell that brightens in about 5 ms and darkens in about 35 ms, giving the soft, lopsided pulse of blackface amps.'
    },
    shape: {
      Square: 'A square with softened edges: choppy on/off without clicks.',
      Random: 'Sample and hold: a new random level each cycle, for a stuttering, unpredictable pulse.',
      Log: 'Rounded tops and sharp troughs: long loud parts with quick dips.',
      Exponential: 'Sharp peaks and rounded troughs: short loud blips.',
      Trapezoid: 'A clipped triangle that holds at full and at its lowest for a moment.',
      Astable:
        'The charge-and-discharge curve of an analogue oscillator, as in many amp tremolo circuits; Duty sets its curvature.'
    }
  },

  tips: `- **Depth over 100 %** only does something on the Panner. For the tremolo types, 100 % already dips to silence.
- **Square at 100 % Depth with Tempo** is an instant rhythmic gate (stutter) effect.
- **Stereo tremolo:** LFO Phase at 180° with Tremolo or Optical Trem gives a lush throb that bounces between the speakers; the overall level stays steadier than with a mono tremolo.
- The Trigger restarts the cycle at its beginning, not at **Start Phase**. If your pulse starts on a dip, try a different LFO Type or Duty.
- Want the tremolo or pan to follow an expression pedal or a scene? Drive **Rate** or **Depth** with a [modifier](/manual/modifiers).`
};
