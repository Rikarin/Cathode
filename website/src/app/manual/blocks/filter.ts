import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A general-purpose filter: one band of EQ or filtering with a choice of fifteen responses, from plain low- and
high-pass filters to shelves, a tilt EQ, an all-pass with feedback and two comb filters. A built-in LFO can sweep it
between two frequencies, which turns it into an auto-wah, a slow filter sweep or a rhythmic, stepped effect.

It also has a low and high cut, per-side panning and phase invert, so the Null type makes it a handy utility for
level, pan and polarity alone.`,

  usage: `**Uses:**

- **A simple cut or tone shaper:** Low Pass or High Pass at the frequency you want, or a shelf.
- **Telephone or radio voice:** Band Pass around 1 to 2 kHz with a Q of 1 to 2.
- **Auto filter:** Low Pass or Band Pass with Q 3 to 6, LFO Enable on, Frequency at the low end of the sweep and Mod
  Frequency at the top. Sync the rate with LFO Tempo.
- **Synth-like stepped filter:** the same, with LFO Quantize at 4 to 8 steps and a Saw or Square LFO.
- **Flange-like combs:** FF Comb or FB Comb with Comb Time 1 to 10 ms.

Because its parameters glide smoothly, it is also a good target for an expression pedal or a
[modifier](/manual/modifiers).`,

  params: {
    type: `The filter's response. See the options below.`,

    freq: `The filter's frequency: the cut-off of a low- or high-pass, the centre of a band-pass, notch or peak, the corner
of a shelf, the hinge of the tilt EQ. With the LFO on, it is one end of the sweep. The comb types ignore it.`,

    q: `The resonance or width. On a low- or high-pass, Q above 0.707 adds a resonant peak at the cut-off (the classic
synth-filter "wow"); on a band-pass, notch or peak, higher Q is narrower. On the standard shelves it is limited to 1;
Low and High Shelf 2 allow up to 4, so they overshoot at the corner.`,

    gain: `The boost or cut, in dB, of the Peaking, shelf and Tilt EQ types. The pass, notch, all-pass and comb types
ignore it.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    order: `**2nd** is one filter section (12 dB/oct for low- and high-pass). **4th** cascades two identical sections for
a steeper 24 dB/oct cut and a sharper resonance. It applies to Low Pass, High Pass, Band Pass, Notch and All Pass; the
other types ignore it.`,

    lowCut: `A gentle 6 dB/oct high-pass after the filter (20 Hz is off).`,

    highCut: `A gentle 6 dB/oct low-pass after the filter (20 kHz is off).`,

    panLeft: `Stereo page: where the left input goes in the output. −100 % (the default) keeps it on the left. Turning it
towards the right turns its left share down and its right share up; at 0 it is in both sides at full level.`,

    panRight: `Stereo page: where the right input goes, as Pan Left. Setting both to 0 sums the block to mono (a signal
that was already identical on both sides comes out about 6 dB louder, so trim Level).`,

    phaseInvert: `Stereo page: flips the polarity of the left, right or both outputs. Use it to fix a phase problem between
two paths, or for wide, phasey stereo tricks.`,

    combTime: `FF Comb and FB Comb only: the comb's delay time. The notches or peaks fall at multiples of 1 / time: 1 ms
gives 1 kHz, 2 kHz, 3 kHz and so on. Short times sound metallic and flanger-like; longer times are more like a very
short slapback.`,

    depth: `Modulation page, but it is the comb types' amount: how much of the delayed signal is added. Positive and negative
values put the peaks and notches in opposite places. On FB Comb it is the feedback, limited to ±98 %, so high values
ring. The LFO does not use it: the sweep always runs the full way between Frequency and Mod Frequency.`,

    lfoOn: `Modulation page: switches the built-in LFO on. It sweeps the filter's frequency between Frequency and Mod
Frequency (spaced logarithmically, so the sweep sounds even). It does not move the comb types.`,

    lfoType: `Modulation page: the LFO's waveform. See the options below.`,

    lfoRate: `Modulation page: the LFO's speed, in cycles per second, when LFO Tempo is Off.`,

    lfoTempo: `Modulation page: locks the LFO to the preset's tempo as a note value (1/4 is one cycle per beat). **Off** uses
LFO Rate.`,

    modFreq: `Modulation page: the other end of the LFO's sweep. It can be above or below Frequency.`,

    feedback: `All Pass only: feeds the filter's output back to its input (one sample later). The all-pass alone only shifts
phase; with feedback its phase sweep turns into peaks (positive) or notches (negative), like a single phaser stage.`,

    lfoDuty: `Modulation page: the LFO's symmetry. On Triangle it moves the peak (0 % is a falling saw, 100 % a rising one);
on Square and Trapezoid it sets the width; on Astable it sets the curve's shape. 50 % is symmetric.`,

    lfoQuantize: `Modulation page: steps the LFO into this many levels, so the sweep jumps between fixed frequencies, for a
sample-and-hold or sequenced filter. **Off** is smooth.`
  },

  options: {
    type: {
      'Low Pass': `Passes the lows, cuts above the frequency. With Q, a resonant peak at the cut-off.`,
      'High Pass': `Passes the highs, cuts below the frequency.`,
      'Band Pass': `Passes a band around the frequency, cuts both sides: telephone and wah sounds.`,
      Notch: `Cuts a narrow band around the frequency and passes the rest.`,
      Peaking: `Boosts or cuts a band around the frequency by Gain.`,
      'Low Shelf': `Boosts or cuts everything below the frequency by Gain.`,
      'High Shelf': `Boosts or cuts everything above the frequency by Gain.`,
      Null: `No filter: only the cuts, pans, phase invert, level and balance act.`,
      'All Pass': `Changes phase, not level, around the frequency. On its own you hear nothing; mixed with the dry signal
in parallel, or with Feedback, it becomes a phaser-like notch or peak.`,
      'Tilt EQ': `Tilts the whole spectrum around the frequency: a low shelf of −Gain/2 and a high shelf of +Gain/2. Positive
Gain is brighter, negative darker. One knob for "warmer or brighter".`,
      'FF Comb': `Adds the signal to a copy of itself delayed by Comb Time: a row of evenly spaced notches.`,
      'FB Comb': `Feeds the delayed output back in: a row of resonant peaks that ring at the comb's pitch.`,
      'Low Shelf 2': `A low shelf whose Q can go up to 4, so it overshoots at the corner like a passive shelving network: a
bump just above the shelf when cutting, a dip when boosting.`,
      'High Shelf 2': `A high shelf whose Q can go up to 4, overshooting at the corner like a passive network.`,
      'Peaking 2': `A peak that narrows as its gain grows (its Q is multiplied by the square root of the linear gain), so big
boosts and cuts stay focused.`
    },
    lfoType: {
      Sine: `Smooth and even.`,
      Triangle: `Straight ramps up and down; Duty skews it towards a saw.`,
      Square: `Jumps between the two ends, with softened edges so it never clicks; Duty sets its width.`,
      'Saw Up': `Rises slowly, drops at once.`,
      'Saw Down': `Drops slowly, jumps back up.`,
      Random: `A new random value each cycle.`,
      Log: `Rounded at the Mod Frequency end, quick at the Frequency end, so it lingers near Mod Frequency.`,
      Exponential: `The mirror image: lingers near Frequency and only touches Mod Frequency briefly.`,
      Trapezoid: `A triangle clipped flat at both ends, so it dwells at each end; Duty skews it.`,
      Astable: `The charge and discharge curve of an analogue multivibrator; Duty sets its curvature.`
    },
    order: {
      '2nd': `One section: 12 dB/oct.`,
      '4th': `Two sections: 24 dB/oct and sharper resonance.`
    },
    phaseInvert: {
      None: `Both outputs as they are.`,
      Both: `Both outputs inverted.`
    }
  },

  tips: `- While the filter moves, its coefficients are updated every 32 samples on a fixed grid, so the sound does not
  depend on your buffer size, and fast sweeps stay smooth.
- For a classic envelope-controlled auto-wah, leave the LFO off and drive Frequency from an envelope follower
  [modifier](/manual/modifiers) instead.
- A Low Pass at 5 to 6 kHz after a direct-recorded distortion is the quickest way to take off the worst fizz.`
};
