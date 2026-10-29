import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A three-band compressor. It splits the signal into lows, mids and highs at two crossover frequencies, gives
each band its own compressor, then adds the bands back together. That lets you control one part of the spectrum without
touching the rest: tame boomy low strings without squashing the mids, or level out spiky highs while the body of the
tone breathes.

With every band uncompressed the bands sum back flat (the crossovers are Linkwitz-Riley filters, phase-matched), so the
block is transparent until it has something to do.`,

  usage: `Usually after the amp and cab, near the end of the chain, as a mastering-style tool for the finished tone. It also
works on bass, or on an acoustic or clean tone before the effects.

**Starting point:** leave the crossovers at 200 Hz and 2.5 kHz. Set all three thresholds to 0 dB, then lower one band's
threshold at a time while you play, until that band is held in check. Ratios of 2 to 4 are plenty. Use each band's
Level to rebalance the bands afterwards.

To hear what one band is doing, mute the other two.

**Reading the meters:** the block's page has a gain-reduction meter for each band, **Low**, **Mid** and **High**. Each
bar hangs down from 0 dB as that band is turned down, on a scale to −24 dB; a mark holds the deepest reduction for a
second and the number under the bar shows it. Lower a band's threshold until its meter moves on the notes you want to
control and stays near 0 on the rest. The meters read 0 while the block is bypassed.`,

  params: {
    lowFreq: `The crossover between the low and mid bands. Everything below it is the low band.`,

    highFreq: `The crossover between the mid and high bands. Everything above it is the high band.`,

    lowThreshold: `Low page: the level above which the low band is compressed.`,
    lowRatio: `Low page: how much the low band is reduced above its threshold (4:1 means 4 dB in, 1 dB out).`,
    lowAttack: `Low page: how fast the low band's compressor reacts. Lows need longer times than highs to avoid distorting
their slow waveforms; the default is 20 ms.`,
    lowRelease: `Low page: how fast the low band's compressor lets go.`,
    lowLevel: `Low page: the low band's level after its compressor, in dB: a makeup gain, or a way to rebalance the bands.`,
    lowMute: `Low page: silences the low band. Mute two bands to hear the third alone.`,

    midThreshold: `Mid page: the level above which the mid band is compressed.`,
    midRatio: `Mid page: how much the mid band is reduced above its threshold.`,
    midAttack: `Mid page: how fast the mid band's compressor reacts.`,
    midRelease: `Mid page: how fast the mid band's compressor lets go.`,
    midLevel: `Mid page: the mid band's level after its compressor, in dB.`,
    midMute: `Mid page: silences the mid band.`,

    highThreshold: `High page: the level above which the high band is compressed.`,
    highRatio: `High page: how much the high band is reduced above its threshold.`,
    highAttack: `High page: how fast the high band's compressor reacts. Short times catch pick attacks and fizz.`,
    highRelease: `High page: how fast the high band's compressor lets go.`,
    highLevel: `High page: the high band's level after its compressor, in dB.`,
    highMute: `High page: silences the high band.`,

    knee: `How gradually compression starts around each band's threshold, in dB, for all three bands. 0 is a hard knee.`,

    mix: `Blends each band's uncompressed signal with its compressed one, for parallel compression. Both sides have the
same phase response, so they do not comb. The band levels and mutes come after the blend, so they act at any Mix: a
muted band stays silent, and at 0 % the block is a three-band EQ set by the levels.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    detector: `**Peak** reacts to every transient; **RMS** averages over about 5 ms and follows loudness more smoothly. It
applies to all three bands.`,

    slope: `How steep the crossovers are. **24 dB/oct** separates the bands cleanly, so each compressor sees only its band.
**12 dB/oct** overlaps them more, for a gentler, more natural blend.`,

    autoTime: `Scales each band's attack and release by that band's crest factor (how spiky it is compared with its
average), so transients get faster times and sustained parts slower ones.`
  },

  options: {
    detector: {
      Peak: `Instant peak level: tight control of transients.`,
      RMS: `Average power over about 5 ms: smoother.`
    },
    slope: {
      '24 dB/oct': `Fourth-order Linkwitz-Riley crossovers: clean separation.`,
      '12 dB/oct': `Second-order Linkwitz-Riley crossovers: more overlap between bands.`
    }
  },

  tips: `- Each band's detector is linked across both channels, so a stereo image stays put.
- **Tighten a boomy high-gain tone:** Low Freq 150 to 200 Hz, Low Threshold low enough for 3 to 6 dB of reduction on
  palm mutes, Low Ratio 3, Low Attack 20 to 30 ms. The low end stops swelling on chugs while chords keep their weight.
- **Smooth harsh highs:** High Freq 3 to 4 kHz, High Ratio 2 to 3, fast High Attack (1 to 3 ms).
- If you only want a static tone change, an [EQ](/manual/blocks/peq) is simpler. Reach for this block when a frequency
  range is only a problem some of the time, on some notes.`
};
