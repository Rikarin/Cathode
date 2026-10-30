import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A five-band parametric EQ: for each band you choose the frequency, how much to boost or cut, and how wide.
Bands 2 to 4 are peaking bands; bands 1 and 5 can each be a shelf, a peak, a high- or low-cut filter with a slope up to
36 dB/oct, a gentle first-order shelf, or a console-style peak. It is the precise tool for fixing problems: a boomy
note, a harsh peak, fizz, mud.

A band at 0 dB does nothing and costs nothing, so a flat EQ is perfectly transparent.`,

  usage: `**Where it goes:** most often after the amp and cab, where you hear the finished tone. Before the amp it changes
what the amp's preamp distorts, which is a different (and also useful) job: cutting lows there tightens the distortion.

**Search and destroy:** to find a problem frequency, set a band to a narrow Q (5 to 8) and a big boost (+10 dB), sweep
the Frequency until the problem jumps out, then turn the boost into a modest cut (−3 to −6 dB) and widen the Q a little.
**Solo** lets you hear just that band's region while you search.

**The chart:** at the right of the Basic page is the EQ's curve, with a handle for each band on it. Drag a handle to
set its band's frequency and gain, and scroll over it to change its Q; the knobs follow.

**Common moves after the cab:**
- Band 1 as **Blocking** at 70 to 100 Hz to clear sub-bass rumble.
- Band 5 as **Blocking** at 6 to 8 kHz, or a High Shelf cut, to tame fizz.
- A small, wide cut around 200 to 400 Hz to clear mud; a small boost around 1 to 2 kHz to cut through a band mix.`,

  params: {
    'freq#': `The band's frequency: the centre of a peak, the corner of a shelf or of a high- or low-cut.`,

    'gain#': `How much the band boosts or cuts, in dB. Blocking types ignore it. Cuts usually sound more natural than big
boosts; if you find yourself boosting a lot, try cutting elsewhere instead.`,

    'q#': `The band's width. Low Q (0.5 to 1) is broad and musical; high Q (4 to 10) is narrow, for surgical cuts. On a
shelf it shapes the corner (limited to 1, so no overshoot); on a Blocking band at 12 dB/oct it sets the resonance at the
corner (0.707 is flat); Shelving 2 ignores it.`,

    lowtype: `What band 1 is. See the options below.`,

    hightype: `What band 5 is. See the options below.`,

    level: `The block's output level in dB. Use it to make up for overall boosts or cuts. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    lowslope: `Band 1 as Blocking only: how steep the high-pass is, 6 to 36 dB/oct. 12 dB/oct uses the band's Q; the other
slopes are Butterworth (flat, no peak). Steeper slopes cut harder below the corner while leaving more just above it.`,

    highslope: `Band 5 as Blocking only: how steep the low-pass is, 6 to 36 dB/oct, as for band 1.`,

    solo: `Lets you hear one band's region on its own while you set it: a peak's band-pass at its frequency and Q, band 1's
low-pass or band 5's high-pass at its frequency. Everything else is muted, so remember to turn it **Off** again.`
  },

  options: {
    lowtype: {
      'Low Shelf': `Boosts or cuts everything below the frequency (second-order).`,
      Peak: `A peaking band like bands 2 to 4.`,
      Blocking: `A high-pass (low cut) at the frequency, with the Band 1 Slope. Gain does nothing.`,
      'Shelving 2': `A gentle first-order shelf centred on the frequency: a broad, natural bass tilt. Q does nothing.`,
      'Peaking 2': `A proportional-Q peak, as on an analogue console: broad at small gains, narrowing to the band's Q at
±12 dB.`
    },
    hightype: {
      'High Shelf': `Boosts or cuts everything above the frequency (second-order).`,
      Peak: `A peaking band like bands 2 to 4.`,
      Blocking: `A low-pass (high cut) at the frequency, with the Band 5 Slope. Gain does nothing.`,
      'Shelving 2': `A gentle first-order shelf centred on the frequency: a broad, natural treble tilt. Q does nothing.`,
      'Peaking 2': `A proportional-Q peak: broad at small gains, narrowing to the band's Q at ±12 dB.`
    },
    solo: {
      Off: `Normal operation: every band works.`
    }
  },

  tips: `- Small moves go a long way after a cab: 2 to 4 dB is often enough.
- Fizz on high-gain tones usually lives between 5 and 10 kHz; a High Shelf cut or a Blocking low-pass there smooths it
  without dulling the pick attack, which sits lower.
- Before the amp, a Blocking high-pass at 100 to 150 Hz tightens a high-gain amp much like a Tube Screamer boost does,
  without the mid push.
- Coefficients follow the knobs smoothly, so you can sweep a band with a [modifier](/manual/modifiers) or an expression
  pedal without clicks.`
};
