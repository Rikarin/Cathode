import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A graphic EQ: a row of sliders, each boosting or cutting a fixed band by up to 12 dB. It is the quick, visual
way to shape a tone: you can see the curve at a glance. **Type** picks the layout, from the classic ten octave bands
(31 Hz to 16 kHz) to 2/3-octave, eight, seven and five-band versions, and a set of passive and vintage layouts that run
the real circuits: the Mesa Mark-style five-band inductor graphic, Marshall-style bass/middle/treble stacks and a
console EQ.

A flat band does nothing and costs nothing, so a flat EQ is perfectly transparent.`,

  usage: `**Where it goes:** most often after the cab, to shape the finished tone, just as many players run a graphic EQ in
an amp's effects loop. Before the amp, it changes what the preamp distorts: cutting lows tightens, boosting mids makes it
break up sooner.

**Starting point:** start flat and cut before you boost. A gentle "smile" (lows and highs up a little, 500 Hz to 1 kHz
down) is the classic scooped metal rhythm; a mid hump (500 Hz to 2 kHz up) helps a lead cut through.

The band sliders keep their ids on every layout but are renamed (and the unused ones hidden) when you change Type, so a
slider's label always tells you its frequency.

**Reading the chart:** at the right of the page is the EQ's frequency response, from 20 Hz to 20 kHz, with faint lines
at the layout's band centres. Cathode measures it on the block's own equaliser after each change, so it shows what you
hear, Level included: on the passive layouts the bands interact as the real circuits' do, so one slider moves its
neighbours' regions too, and the tone stacks are not flat even with every knob at noon.`,

  params: {
    g31: `The first band. On 10 Band it is 31 Hz (sub-bass: mostly rumble on guitar, usually best cut). On the other layouts
it is that layout's lowest band, as its label shows.`,
    g63: `The second band: 63 Hz on 10 Band, the low end's weight and thump.`,
    g125: `The third band: 125 Hz on 10 Band, body and warmth; too much is boomy.`,
    g250: `The fourth band: 250 Hz on 10 Band, fullness; too much is muddy.`,
    g500: `The fifth band: 500 Hz on 10 Band, low mids; cut here for a scooped sound, boost for thickness.`,
    g1k: `The sixth band: 1 kHz on 10 Band, the core midrange, where a guitar sits in a mix.`,
    g2k: `The seventh band: 2 kHz on 10 Band, presence and bite.`,
    g4k: `The eighth band: 4 kHz on 10 Band, attack and edge; too much is harsh.`,
    g8k: `The ninth band: 8 kHz on 10 Band, fizz and air on a distorted tone.`,
    g16k: `The tenth band: 16 kHz on 10 Band, mostly hiss after a guitar cab.`,

    level: `The block's output level in dB. Use it to make up for overall boosts or cuts. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    masterQ: `Scales the width of every band (not on the passive types). 1 is the layout's own width (one octave on 10 Band);
higher makes each band narrower, so sliders interact less; lower makes them broader and smoother.`,

    qMode: `**Constant Q** keeps each band the same width at any setting. **Variable Q** narrows a band as you push it
harder, from 40 % of its width near 0 dB to the full value at ±12 dB, as analogue graphics do: small moves are broad and
gentle, big ones focused. The passive types always behave this way.`,

    type: `The band layout. See the options below.`,

    shelves: `Turns the lowest and highest bands into shelves (corners placed half a band outward), so they lift or cut
everything beyond them rather than a peak. Not on the passive types.`
  },

  options: {
    type: {
      '10 Band': `Ten octave bands from 31 Hz to 16 kHz: the classic studio graphic.`,
      '10 Band 2/3 Oct': `Ten narrower bands from 63 Hz to 4 kHz, concentrated on the guitar's range.`,
      '8 Band': `Eight octave bands from 63 Hz to 8 kHz.`,
      '7 Band': `Seven octave bands from 100 Hz to 6.4 kHz, like the classic seven-band guitar pedal EQ.`,
      '5 Band': `Five broad bands at 100, 300, 1k, 3k and 8k Hz.`,
      '5 Band Mark': `A simulation of the Mesa/Boogie Mark-series five-band inductor graphic (80, 240, 750, 2.2k and 6.6k on
the panel). The sliders are slider positions in the real circuit, so the bands interact and overlap as on the amp: the
famous "V" lives here.`,
      '5 Band Passive': `The same kind of inductor graphic at 100, 400, 1k, 3k and 8k Hz.`,
      '4 Band Passive': `A Marshall-style bass/middle/treble stack followed by a presence control, after the JMP-1 preamp.
The sliders are pot positions: 0 dB is noon.`,
      '3 Band Passive': `A Marshall-style bass/middle/treble tone stack. The sliders are pot positions: 0 dB is noon, which is
the stack's own voicing (made up to unity at 1 kHz), not flat.`,
      '3 Band Console': `Three broad resonant bands at 100 Hz, 1 kHz and 10 kHz, like a mixing console's EQ.`
    },
    qMode: {
      'Constant Q': `Each band keeps its width at any gain.`,
      'Variable Q': `Bands are broad at small settings and narrow as you push them.`
    }
  },

  tips: `- On the passive types the sliders are positions in a real circuit, not exact decibels: neighbouring bands add up,
  moving one shifts what the others do, and the stack types are not flat at 0 dB. Trust your ears rather than the
  numbers.
- After a high-gain cab, a cut of a few dB at 8 kHz and 16 kHz takes off fizz and hiss; a cut at 31 Hz clears rumble
  you cannot hear on small speakers but that eats headroom.
- For precise surgery (one narrow resonance), the [Parametric EQ](/manual/blocks/peq) is the better tool.`
};
