import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Megatap Delay places up to 128 taps on one delay line and spreads them across a time window. With a few
taps you get a rhythmic burst of echoes; with dozens they blur into a swell, a bouncing-ball effect, a stutter or a
shimmering cloud. Shape controls set how the taps are spaced in time, how loud each one is and where it sits in the
stereo field.

It's the block for special effects more than everyday echo: reverse-like swells (taps that fade in), "ping-pong
machine-gun" bursts, a ball bouncing to a stop, or a granular smear behind a lead.`,

  usage: `**Where it goes.** After the amp and cab, alongside or instead of your regular delay.

**How the taps are placed.** The taps sit between the **Pre-Delay** and Pre-Delay + **Time**. With **Time Alpha** at 0
they're evenly spaced: 16 taps over 1000 ms land every 62.5 ms, the last one exactly on Time. The tap levels are
normalised to the same total power whatever the tap count, so adding taps doesn't make the block louder on sustained
playing.

**Starting points.**
- *Swell:* 32 taps, Time 1 s, **Level Shape** Fade In, Pan Shape Alternate, Mix 40 %. Each note blooms in behind you.
- *Bouncing ball:* 12 to 20 taps, Time 1 to 1.5 s, **Time Alpha** around −60 %, Level Shape Fade Out. The echoes start
far apart and get quicker and quieter, like a ball settling.
- *Stutter:* 8 taps, Tempo 1/4, Level Shape Flat, Pan Shape Alternate.

**About Mix.** Megatap uses a linear mix: at 50 % the dry and wet are each 6 dB down.

**Tempo.** **Tempo** follows the preset's tempo (or the global tempo or MIDI clock) and overrides **Time** when it isn't
Off.`,

  params: {
    time: `The length of the window the taps are spread over, from the end of the pre-delay to the last tap. Changes
glide rather than jump.`,

    tempo: `Locks **Time** to a note value at the current tempo. A dot means dotted, T means triplet. Capped at 4
seconds.`,

    taps: `How many taps there are, from 1 to 128. A handful gives distinct echoes; 30 and up blurs them into a
continuous swell or texture. Changing it re-places every tap at once, so expect a jump in the sound if you change it
while the block is ringing.`,

    timeAlpha: `Bends the spacing of the taps. At 0 they're evenly spaced. Positive values bunch the early taps together
and spread the later ones out, so the echoes start fast and slow down. Negative values do the opposite: the echoes start
sparse and speed up, like a ball bouncing to rest. The last tap always lands on Time.`,

    levelShape: `The loudness of the taps across the pattern. See the options below.`,

    panShape: `Where the taps sit in the stereo field across the pattern. See the options below.`,

    spread: `Scales the pan positions from **Pan Shape**. 100 % uses the full width; 50 % keeps the taps between half left
and half right; 0 puts them all in the centre; negative values mirror the pattern (a left-to-right sweep becomes right to
left).`,

    lowCut: `A high-pass on what enters the line (12 dB/oct). It thins every tap, and with feedback each pass is thinner
still. At its lowest setting it's off.`,

    highCut: `A low-pass on what enters the line (12 dB/oct). It darkens every tap, and with feedback each pass gets
darker. At its highest setting it's off.`,

    mix: `The balance of dry and wet, on a linear law (both at −6 dB at 50 %). With many taps the wet can sound quieter
than you'd expect at a low Mix, because the energy is spread out over the window.`,

    level: `The block's output level, wet and dry together.`,

    balance: `Turns down one side of the block's output, wet and dry alike.`,

    inputGain: `How much of your signal enters the delay line. The dry is not affected.`,

    inputSelect: `Which input the line hears: the sum of both sides, the left only or the right only.`,

    preDelay: `A gap before the first tap, up to 1 second. The taps are spread over Time after it. Use it to let your
dry note speak before a swell comes in.`,

    feedback: `Feeds one tap back into the line, so the whole pattern repeats at that tap's time. 0 % plays the pattern
once. Higher values repeat it more times, each pass passing the Low and High Cut again. Which tap is used is set by
**Feedback Tap**.`,

    feedbackTap: `Which tap is fed back. The default (the highest number) uses the last tap, so the pattern repeats once
it ends. A lower tap makes the pattern restart before it finishes, which overlaps the repeats into a denser texture.
Limited to the current tap count.`,

    timeRandom: `Moves each tap forwards or backwards by up to half a tap spacing, by a fixed random amount per tap. It
turns a mechanical, evenly spaced burst into something looser and more organic. The pattern is the same every time.`,

    levelAlpha: `Steepens or softens the **Level Shape**. Positive values make a fade steeper (the quiet taps get quieter
sooner); negative values make it gentler. On Exponential it deepens or raises the floor of the last taps. No effect on
Flat.`,

    levelRandom: `Turns each tap down by a fixed random amount, up to this percentage. Breaks up the regularity of the
pattern, a bit like a granular texture.`,

    panAlpha: `Curves the pan positions from **Pan Shape**. Positive values pull the taps towards the centre; negative
values push them out towards the sides. No effect on Center or Alternate, whose taps are already at the centre or the
edges.`,

    diffusionMix: `Blends in a diffuser after the taps that smears them into a wash, without changing the level. Many taps
plus diffusion makes a smooth, reverb-like swell.`,

    diffusionTime: `The size of the diffuser. Longer times smear more.`,

    diffusionRate: `The speed of the slow LFO that moves the diffuser.`,

    diffusionDepth: `How far the diffusion LFO moves the diffuser (up to ±8 % of its lengths). 0 leaves it still.`,

    envelopeThreshold: `The level your playing must cross to start a swell, when **Envelope Attack** is above 0.`,

    envelopeAttack: `Swells what enters the delay line: each note above the threshold fades into the taps from silence
over this time, on a smooth S-curve. The dry isn't affected. 0 turns the swell off.`,

    envelopeRelease: `How quickly the swell closes once your note falls 6 dB below the threshold. The next note starts a
new swell from silence.`,

    globalMix: `When on, the global Effects Mix offset is added to this block's Mix.`
  },

  options: {
    levelShape: {
      Flat: `Every tap at the same level: a steady burst or stutter.`,
      'Fade Out': `The taps get quieter in a straight line from first to last, like a natural decay. The default.`,
      'Fade In': `The taps get louder from first to last, so each note swells in after you play it, a reverse-like effect.`,
      Swell: `The taps rise to a peak in the middle of the pattern and fall away again.`,
      Exponential: `The taps fall on a decibel scale, the last one 40 dB below the first: a fast, natural-sounding decay.`
    },
    panShape: {
      Center: `Every tap in the centre.`,
      Alternate: `The taps alternate hard left and hard right (scaled by Spread), starting on the left. The default.`,
      'Left to Right': `The taps sweep from left to right across the pattern.`,
      'Right to Left': `The taps sweep from right to left across the pattern.`
    }
  },

  tips: `- **Swells that follow the tempo.** Set **Tempo** to 1/2 or 1/1, Level Shape Fade In, 24 to 48 taps and a little
diffusion. Every chord swells up into the next beat.
- **Pre-delay plus Fade In.** A 100 to 300 ms pre-delay lets the dry note speak on its own before the swell starts,
which keeps the attack clear.
- **Darken the wet.** Many taps of a bright, distorted guitar can fizz. A High Cut at 3 to 5 kHz keeps it smooth.
- **Expert note.** Tap k of N sits at Pre-Delay + Time × (e^(a·k/N) − 1) / (e^a − 1), with a = 4 × Time Alpha. Amplitude
Alpha raises each tap's level to the power 2^(2 × alpha), Pan Alpha does the same to the pan position, and the amplitudes
are then normalised to unit total power.`
};
