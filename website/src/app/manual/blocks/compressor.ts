import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A compressor evens out your dynamics: it turns loud notes down so the quiet ones sit closer to them. On guitar
that means more sustain, a smoother, more even clean tone, and chords that do not jump out over single notes. The
**Type** picks the character, from a snappy pedal and a smooth studio unit to optical, FET and tube styles, a sustainer
that lifts the quiet parts, and a compander that also pushes the noise down between notes.

Behind the basic knobs it is a full studio compressor: a sidechain with filters and an EQ band, a choice of detector,
look-ahead, auto attack and release, and a parallel mix.`,

  usage: `**Where it goes:** for clean and lightly driven tones, first in the chain or right after the wah, before the
drives and amp. That is the classic "pedal compressor" place: it evens your picking before anything else. After the amp
and cab it works like a studio compressor on the finished sound, which suits a gentle Studio or Opto setting. High-gain
amps compress a lot by themselves, so you rarely need much compression in front of them.

**Starting point (clean funk or country):** Type Pedal, Threshold −30 dB, Ratio 4, Attack 5 ms, Release 150 ms, Auto
Makeup on. Lower the Threshold for more squash, then raise Attack if the pick attack sounds dull.

**The one-knob way:** set **Controls** to Compression and use the single Compression knob, as on an optical or pedal
compressor.

**Parallel compression:** set Mix around 50 %: you keep the natural attack of your playing and add a sustained,
compressed layer under it.`,

  params: {
    type: `The compressor's design: how it measures the level and how it reacts. See the list below.`,

    threshold: `The level above which the compressor starts working. Lower means more of your playing is compressed. As a guide,
a threshold that takes 3 to 6 dB off your normal picking is gentle; 10 dB or more is obvious squash. Bypass the block now
and then to hear how much it is doing.`,

    ratio: `How much the level over the threshold is reduced. At 4:1, a note 8 dB over the threshold comes out only 2 dB
over. 2:1 is gentle, 4:1 to 8:1 is a typical guitar compressor, and at the top of the range (100) it is ∞:1, a limiter.
The Dynamics type sets its own ratio from its Dynamics knob and ignores this one.`,

    attack: `How fast the compressor reacts when the level rises. Short attacks (under 2 ms) clamp down on the pick attack,
which softens and flattens it; longer attacks (10 to 30 ms) let the pick "click" through before the compression grabs,
for a punchier sound.`,

    release: `How fast the compressor lets go after the level falls. Short releases recover quickly and can "pump" or
"breathe" audibly; long releases are smoother but hold the level down longer. On Opto the release also slows down as
the reduction deepens.`,

    knee: `How gradually compression starts around the threshold, in dB. 0 is a hard knee (compression switches on exactly
at the threshold); wider knees ease in, which sounds more natural. The Tube type always uses at least 18 dB.`,

    makeup: `Gain added after compression, in dB, to bring the level back up. Use it (or Auto Makeup) so the block is about
as loud on as off.`,

    mix: `Blends the uncompressed signal with the compressed one, for parallel compression. 100 % is fully compressed; at
50 % the two are mixed at equal level.`,

    level: `The block's output level in dB, after everything else. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    autoMakeup: `Adds makeup gain automatically: half the reduction a full-scale (0 dBFS) signal would get with the current
threshold, ratio and knee. It is added on top of the Makeup knob.`,

    scLowCut: `Sidechain page: a high-pass on what the detector hears (20 Hz is off). Raise it to 100 to 200 Hz so low notes
and palm mutes do not trigger heavy compression and pumping.`,

    scHighCut: `Sidechain page: a low-pass on what the detector hears (20 kHz is off).`,

    detector: `Sidechain page: how the level is measured. **Type Default** uses the type's own: Peak for Pedal, Opto and
JFET, RMS for the others.`,

    autoTime: `Advanced page: scales the attack and release by the signal's crest factor (how spiky it is compared with its
average), so sharp transients get faster times and sustained parts slower ones. Good for mixed playing where one setting
does not fit everything.`,

    dynamics: `Advanced page, Dynamics type only: sets the slope above the threshold. Positive values compress (100 % is a
limiter, 50 % is 2:1); negative values expand upwards, so playing harder gets you even louder (−100 % doubles every dB
over the threshold), with at most 24 dB of boost. Use it to add punch and dynamics rather than remove them.`,

    light: `Opto type only: the optical cell. **Classic** is the reference. **Fast LED** reacts twice as fast with a
shallower release law (the release slows less as reduction deepens). **Slow Vactrol** attacks half as fast and slows its
release much more with reduction, for the smoothest, most "leveling" action.`,

    emphasis: `Advanced page: a high shelf (up to +12 dB) on the detector, so it reacts more to the treble and less to the
bass. Low notes then pump less, and bright pick attacks are controlled more.`,

    emphasisFreq: `Advanced page: the corner of the emphasis shelf.`,

    scFilter: `Sidechain page: an EQ band on what the detector hears: a low shelf, high shelf or peak. Boost a band to make
the compressor react more to it (a de-esser-like effect on pick clank with a high peak), cut to make it ignore it.
**Off** leaves the detector flat.`,

    scFreq: `Sidechain page: the frequency of the sidechain EQ band.`,

    scQ: `Sidechain page: the width of the sidechain EQ band (higher is narrower; for shelves, their slope).`,

    scGain: `Sidechain page: how much the sidechain EQ band boosts or cuts, in dB. At 0 the band does nothing.`,

    inputLevel: `**Instrument** for a guitar-level signal (the usual case, before the amp). **Line** takes 12 dB off what
the detector hears, for hotter line-level signals, so the same threshold settings stay useful after the amp or on a
synth or keyboard.`,

    scSource: `Sidechain page: what the detector listens to. **Block** is the block's own input. **Grid Input** is the signal
entering the grid. **Row 1** to **Row 6** take the output of the last active block to the left of this one on that row
(nothing there means the block's own input). **Input 1** to **USB 7/8** are your interface's inputs directly; an
unmapped one is silence. Use another source for ducking, for example a guitar sidechained from a vocal or drum input.`,

    lookAhead: `Advanced page: delays the audio while the detector hears it straight away, so the gain is already down when
a transient arrives: no overshoot, even with slower attacks. It adds the chosen delay as latency (parallel rows are
aligned automatically), and changing it rebuilds the signal path. **Off** adds no delay.`,

    controls: `**Threshold/Ratio** uses the two knobs. **Compression** replaces both with the single Compression knob.`,

    compression: `Used when Controls is Compression: one knob that lowers the threshold from −6 to −50 dB and raises the ratio
from 1:1 to 20:1 together. The ratio rises with the square of the setting, so the first half stays gentle. 30 to 50 % is
a typical pedal setting.`,

    expThreshold: `Advanced page, Compander type only: the level below which the downward expander starts to turn the signal
down, for quieter noise between notes.`,

    expRatio: `Advanced page, Compander type only: how fast the expander turns down below its threshold (each dB under takes
ratio − 1 dB more off, to at most 80 dB). 2 is gentle; higher acts more like a gate.`
  },

  options: {
    type: {
      Pedal: `Peak detection, the gain following every transient: the snappy, punchy squash of a VCA pedal compressor
(Dyna Comp and Ross style). Great for clean funk, country and chicken picking.`,
      Studio: `RMS detection over about 5 ms: smoother and more program-like. A transparent choice for leveling.`,
      Opto: `Peak detection with a release that slows down the more it compresses, like a photocell. Smooth and musical; use
Light Type to pick the cell.`,
      Sustainer: `Upward compression: instead of turning loud notes down, it turns quiet ones up (by up to 24 dB) as they
fade, for long sustain. It also raises the noise floor when you stop playing, so pair it with a gate.`,
      Dynamics: `A dynamics processor: the Dynamics knob sets compression or upward expansion above the threshold.`,
      Feedback: `The classic feedback design: the detector hears the compressed output, so the effective ratio is softer and
the release follows the playing. Smooth and vintage.`,
      JFET: `After the 1176-style FET compressors: a feedback peak detector driving a gain cell that adds odd harmonics as it
compresses harder. Fast and aggressive, with colour.`,
      Tube: `After the variable-mu tube compressors: a wide knee (at least 18 dB) where the ratio grows with the level, an RMS
detector on the output, and a little second-harmonic warmth.`,
      Compander: `A compressor and a downward expander in one: notes are levelled above the threshold, and the noise between
them is pushed down below the Expander Threshold.`
    },
    detector: {
      'Type Default': `The type's own detector: Peak for Pedal, Opto and JFET, RMS for the others.`,
      Peak: `Reacts to every peak instantly: firm control of pick attacks.`,
      RMS: `Averages the power over about 5 ms: follows loudness, smoother.`,
      'RMS+Peak': `The mean of the two (in dB): a compromise.`
    },
    light: {
      Classic: `The reference optical cell.`,
      'Fast LED': `Half the attack time, and the release slows less with deep reduction.`,
      'Slow Vactrol': `Twice the attack time, and the release slows much more with deep reduction.`
    },
    scFilter: {
      Off: `No EQ on the sidechain.`,
      'Low Shelf': `Boosts or cuts the detector's lows below Sidechain Freq.`,
      'High Shelf': `Boosts or cuts the detector's highs above Sidechain Freq.`,
      Peak: `Boosts or cuts a band around Sidechain Freq.`
    },
    inputLevel: {
      Instrument: `Guitar level: the detector hears the signal as it is.`,
      Line: `Line level: the detector hears the signal 12 dB lower.`
    },
    controls: {
      'Threshold/Ratio': `Separate Threshold and Ratio knobs.`,
      Compression: `One Compression knob sets both.`
    }
  },

  tips: `- Detection is linked across both channels, so a stereo signal stays centred while it is compressed.
- **Funk and country clean:** Pedal, Ratio 6 to 8, Attack 2 to 5 ms, Release 100 ms, around 8 to 12 dB of reduction.
- **Sparkly, natural clean:** Opto or Studio, Ratio 2 to 3, Attack 15 to 30 ms, Release 200 to 400 ms, 3 to 6 dB of
  reduction. The slow attack keeps the pick attack alive.
- **Pumping bass?** Raise Sidechain Low Cut to 100 to 150 Hz, or add some Emphasis.
- Compression raises the noise floor along with the quiet notes; a [Gate](/manual/blocks/gate) or the Compander type
  helps.
- Compressing before a drive makes the drive react more evenly (less dynamic); compressing after it mostly changes
  level, as the drive is already compressed.`
};
