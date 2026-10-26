import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Ten-Tap Delay plays up to ten echoes of each note inside a time window, and can repeat that whole
pattern with feedback. **Ten Tap** spaces the taps evenly, with a shuffle for swing; **Rhythm Tap** lets you place each
tap anywhere, snap it to a tempo grid, or teach the block a rhythm by playing it.

Use it for rhythmic echo patterns that a single delay can't make: a swung eighth-note trail, a syncopated figure that
answers your playing, or a pattern that ping-pongs across the stereo field.`,

  usage: `**Where it goes.** After the amp and cab, before the reverb.

**How it works.** All the taps read one line fed by your (mono-summed) signal. **Time** is the length of the pattern;
in Ten Tap mode the taps divide it evenly (10 taps over 1 second land every 100 ms, the last one on Time). **Feedback**
re-enters the line at Time, so the whole pattern plays again, and again, fading by the feedback amount each time.

**A starting point.** Ten Tap, **Tempo** 1/2, **Number of Taps** 4, **Decay** 50 %, **Feedback** 30 %, Mix 25 %. You
hear four eighth-note echoes after each note, fading and alternating left and right, and the pattern repeats each half
note. Add **Shuffle** for swing.

**About Mix.** Ten-Tap uses a constant-power law: about −8 dB of wet at 25 % Mix, both at −3 dB at 50 %.

**Tempo.** Tempo and Quantize follow the preset's tempo (or the global tempo or MIDI clock); tap tempo moves them.`,

  params: {
    type: `**Ten Tap** spaces the taps evenly across Time, with Shuffle for swing. **Rhythm Tap** gives each tap its own
time, optionally quantised to a note grid or learned from your playing.`,

    time: `The length of the pattern: the last evenly spaced tap lands here, and the feedback repeats the pattern at this
interval. Changes glide rather than jump.`,

    tempo: `Locks **Time** to a note value at the current tempo, overriding it. Capped at 4 seconds.`,

    feedback: `Repeats the whole pattern. The line is read at Time and fed back in, so each pass of the pattern comes
back quieter by this amount. 0 % plays the pattern once. High values make long, layered trails; a limiter stops it from
clipping.`,

    taps: `How many taps play, from 1 to 10. In Ten Tap mode the taps are always spread evenly across Time, so fewer taps
means wider spacing (4 taps over a half note are eighth notes). In Rhythm Tap mode it limits how many of the ten tap
positions are used, in order.`,

    mode: `**Stereo** places the taps across the stereo field by Pan Shape and lets **Ratio** offset the right side.
**Mono** puts every tap in the centre at the same time on both sides.`,

    decay: `Tapers the tap levels across the pattern. Positive values fade the taps out, so the last one is quieter (at
100 % it's silent); negative values fade them in, so the pattern swells towards the end. 0 keeps them equal. The tap
levels below multiply on top of this.`,

    shuffle: `Ten Tap only: swing. It delays every other tap (the 1st, 3rd, 5th and so on) by up to half a tap spacing.
About 67 % gives a triplet swing; 100 % a dotted, hard shuffle.`,

    spread: `Scales the pan positions from **Pan Shape**. 100 % uses the full width, 0 % puts every tap in the centre,
negative values mirror the pattern. Stereo mode only.`,

    ratio: `Stereo mode only: sets the right side's tap times as a percentage of the left's. At 100 % both sides play the
same pattern; lower values make the right taps come earlier, so each echo is heard twice, offset between the speakers,
for a wider, more complex pattern.`,

    panShape: `Where the taps sit in the stereo field: alternating left and right, sweeping left to right or right to
left across the pattern, or all in the centre. Scaled by Spread.`,

    panAlpha: `Curves the pan positions. Positive values pull the taps towards the centre; negative values push them out
to the sides. No effect on Alternate or Center, whose taps are already at the edges or in the middle.`,

    lowCut: `A high-pass (12 dB/oct) on everything written into the line, so every tap is thinned, and each repeat of the
pattern a little more. At its lowest setting it's off.`,

    highCut: `A low-pass (12 dB/oct) on everything written into the line, so every tap is darkened, and each repeat of
the pattern a little more. At its highest setting it's off.`,

    'tap#Level': `The level of this tap, on top of the Decay taper. Turn individual taps down to 0 to make rhythms out of
the evenly spaced grid: on 8 taps, muting taps 2, 5 and 7 gives a syncopated figure.`,

    'tap#Time': `Rhythm Tap only: where this tap lands, as a percentage of Time (0 to 100 %). The taps don't have to be in
order. Ignored while a learned pattern is in use.`,

    quantize: `Rhythm Tap only: snaps every tap time to the nearest multiple of this note value at the current tempo. Off
leaves the times as set. Quantize to 1/16 to tidy up hand-set times into a rhythm that locks with the beat. It doesn't
apply to learned patterns.`,

    learn: `Rhythm Tap only: teaches the block a rhythm by playing it. On **Listen**, the first note you play starts a
pattern, and each further note within Time of it places the next tap at that point (up to ten). The first note after
Time has passed starts a new pattern. Notes are detected when your input crosses about −40 dBFS. **Hold** stops listening and keeps the last pattern; **Off** goes back to
the Tap Time knobs. The learned pattern lives in the block and doesn't rewrite the Tap Time knobs.`,

    mix: `The balance of dry and wet, on a constant-power law.`,

    level: `The block's output level, wet and dry together.`,

    balance: `Turns down one side of the block's output, wet and dry alike.`,

    inputGain: `How much of your signal enters the delay line. The dry is not affected.`,

    inputSelect: `Which input the line hears: the sum of both sides, the left only or the right only.`
  },

  options: {
    learn: {
      Listen: `The notes you play within Time of a pattern's first note become its taps; a note after that starts a new
pattern.`,
      Hold: `Keeps the last learned pattern and stops listening.`
    },
    panShape: {
      Alternate: `Taps alternate hard left and hard right, starting on the left.`,
      'Left to Right': `The taps move from left to right across the pattern.`,
      'Right to Left': `The taps move from right to left across the pattern.`,
      Center: `Every tap in the centre (the stereo Ratio still applies).`
    }
  },

  tips: `- **Swung eighths.** Ten Tap, Tempo 1/2, 4 taps, Shuffle about 67 %, Decay 60 %. The echoes swing with a triplet
feel.
- **Learn a riff.** Rhythm Tap, Time 1/1 (a bar), Learn on Listen. Leave a bar's silence, then play the rhythm you want
as single muted notes, all within one bar. Switch Learn to Hold and the block echoes every note you play in that
rhythm.
- **Taming a busy pattern.** Ten taps of a distorted guitar can crowd the mix. Use fewer taps, a High Cut around 4 kHz
and a positive Decay so the later taps fall away.
- **Expert note.** In Stereo mode with Ratio below 100 %, the right side reads the same line at shorter times, so you
hear each tap on the left at its time and on the right a little earlier. The feedback is always taken at Time.`
};
