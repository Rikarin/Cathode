import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A distortion whose gain follows how hard you play. An envelope follower measures your picking, and every dB
you play above the **Threshold** adds (or, with negative **Dynamics**, removes) drive. Play softly and it stays clean or
lightly driven; dig in and it saturates. Or turn it around, so hard picking cleans up.

It is not a pedal model but a flexible clipper: pick a clipping curve with **Type**, and optionally emphasise one band
going into the clipper so that band distorts first and hardest, while quiet playing still comes through flat.`,

  usage: `Put it where you would put a drive pedal, in front of the amp, or use it on its own into a cab for a lo-fi or
synthetic grit.

**Starting point:** Drive 3, Dynamics 50 %, Threshold −40 dB. Play softly, then hard, and move Threshold until the
change happens at the picking strength you want. Then raise Dynamics for a bigger difference between soft and hard.

For an amp-like "cleans up when you play lightly" feel, keep Drive low and Dynamics positive. For a drive that stays
articulate on hard strums, use negative Dynamics.`,

  params: {
    type: `The clipping curve. See the list below; Soft is a safe, smooth start, Hard is buzzier, the Tube and FET curves
add even harmonics.`,

    drive: `The fixed part of the gain into the clipper, 0 to 40 dB (4 dB per step). Dynamics then adds to or subtracts from
it as you play.`,

    dynamics: `How much your playing changes the drive. Each dB the input is above the Threshold adds this percentage of a
dB of drive, over at most 40 dB of envelope. At 50 %, playing 20 dB over the threshold adds 10 dB of drive. Negative
values take drive away instead (never below none), so harder playing gets cleaner. 0 % is a plain distortion.`,

    threshold: `The input level where Dynamics starts to act. Below it you get only the Drive setting. Set it a little below
your softest picking to make the whole range dynamic, or near your hardest to react only to accents.`,

    attack: `How fast the envelope follower rises when you pick harder. Short times react to each pick attack; longer ones
let the transient through before the drive changes, which can sound more natural.`,

    release: `How fast the envelope falls when you play softer. Short times follow the playing closely (and can pump on
decays); long times keep the drive more even through a phrase.`,

    mix: `Blends the clean input with the distortion, for parallel drive. The clean side goes through the same
oversampling filters, so it does not comb with the wet.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    filterType: `An emphasis band ahead of the clipper, with its exact inverse after it. A quiet signal comes out flat, but
the emphasised band reaches the clipper hotter, so it distorts first and hardest. **Low Shelf** makes the lows break up
first (fat, woolly), **High Shelf** the highs (fizzy, cutting), **Peaking** one band (try a mid peak for a vocal,
Screamer-like clip). **None** clips everything alike.`,

    filterFreq: `Where the emphasis band sits: the shelf's corner or the peak's centre.`,

    filterQ: `The emphasis band's width (Peaking) or the shelf's slope. Higher is narrower.`,

    filterGain: `How much the band is emphasised before the clipper (and taken back out after). Positive values make the
band clip earlier; negative values protect it, so that band stays cleaner than the rest.`,

    bias: `An offset ahead of the clipper, for even harmonics and an asymmetric, more tube- or fuzz-like edge. A DC blocker
after the clipper removes the offset again.`,

    mode: `**Stereo** distorts each side separately (the envelope is still linked, so both sides get the same drive).
**Mono** sums the input and sends one distorted signal to both outputs, which saves a little CPU and keeps a mono source
mono.`
  },

  options: {
    type: {
      Soft: `A smooth, symmetric tanh curve: classic overdrive.`,
      Hard: `A flat clip at full scale: bright, buzzy distortion.`,
      Asymmetric: `A biased soft curve that adds even harmonics: fuzz-like.`,
      Germanium: `The longest, softest knee: warm and compressed.`,
      LED: `Twice the headroom of Soft before it clips: louder and cleaner.`,
      Tube: `Soft on one half, harder on the other, like a triode's grid side.`,
      'Op-Amp': `Soft rails, between Soft and Hard.`,
      FET: `Gentler on one half: even harmonics with a soft onset.`,
      Rectifier: `A leaky half-wave rectifier: octave-rich and gnarly.`,
      Variable: `A sine-shaped knee: harder than Soft, softer than Hard.`,
      'HV Tube': `A high-voltage triode: more headroom on one half.`,
      'LV Tube': `A starved-plate triode: clips hard and early on one half.`,
      Silicon: `A silicon diode pair's exponential knee.`,
      '4558 Diode': `The Tube Screamer's op-amp-and-diodes curve: soft clipping with a little clean signal through.`
    },
    filterType: {
      None: `No emphasis: every frequency clips alike.`,
      'Low Shelf': `The lows reach the clipper hotter (or cooler, with negative Gain).`,
      'High Shelf': `The highs reach the clipper hotter (or cooler, with negative Gain).`,
      Peaking: `One band around Frequency reaches the clipper hotter (or cooler).`
    }
  },

  tips: `- The envelope is linked across both channels and measures the peak of the two, so a stereo source never shifts in
  the image as the drive moves.
- The clipper runs at 4x oversampling with antiderivative anti-aliasing, and the drive changes smoothly between
  samples, so fast Attack settings do not click.
- There is no automatic level compensation: more drive means a louder, more compressed output. Trim with Level.
- For a touch-sensitive lead tone, try Type Tube, Drive 2, Dynamics 70 %, Threshold about 10 dB below your normal
  picking, Attack 5 ms, Release 150 ms.`
};
