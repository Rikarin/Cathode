import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Delay block repeats what you play. It covers everything from a single slapback to a dotted-eighth
rhythm, a dark analog-style wash, wobbly tape echo, a two-line stereo delay, reversed repeats and filter sweeps. Nine
types share one engine, so the same knobs (time, feedback, mix, filters, modulation, ducking) work on all of them, and
each type adds its own page where it needs one.

Time can be as long as 16 seconds, or follow the tempo. Feedback works in either polarity, a Master Feedback can push
every loop past unity (a limiter keeps it from running away), and the repeats can be filtered, driven, bit-reduced,
diffused, modulated and ducked under your playing.`,

  usage: `**Where it goes.** Put the delay after the amp and cab, usually before the reverb. That keeps the repeats clean
and lets the reverb soften them. A delay in front of a driven amp gets distorted along with your guitar, which is a
valid sound but a messier one.

**A starting point.** Pick **Digital** or **Analog**, set **Tempo** to 1/4 (or 1/8. for a dotted eighth), **Feedback**
around 25 to 35 % and **Mix** around 20 to 30 %. Set **High Cut** to 3 to 5 kHz and **Low Cut** to 150 to 250 Hz so the
repeats sit behind your playing instead of on top of it.

**About Mix.** The Delay uses a "sweetened" mix law: the dry stays at full level all the way to 50 %, and the wet rises
on a squared curve. At 10 % the repeats are about 28 dB down, at 20 % about 16 dB, at 25 % 12 dB, at 30 % 9 dB, and at
50 % they're as loud as the dry. Past 50 % the dry fades out until 100 % is wet only. So most of the knob is spent on
tasteful, quiet repeats, and 50 % is already loud.

**Tempo.** Every Tempo knob here follows the preset's tempo (or the global tempo or MIDI clock, depending on the tempo
mode you chose; see [MIDI and footswitches](/manual/midi-and-footswitches)). Tap tempo moves it. When a Tempo knob is
on anything other than **Off**, it overrides the matching Time or Rate.

**Trails.** By default a bypassed block goes silent at once. Set the block's bypass mode to **Mute FX In** if you want
the repeats to ring out when you switch the delay off.`,

  params: {
    type: `Chooses the delay's character. All types share the main knobs; some add their own page (Dual on Stereo, Sweep
on Sweep, Mono Tape and Reverse on Advanced). See the options below.`,

    time: `The time between your note and the first repeat, and between repeats. Short times (60 to 150 ms) give
slapback, 300 to 500 ms a classic echo, longer times ambient washes. Changes glide rather than click, so sweeping it with a
[modifier](/manual/modifiers) gives the pitch-bending "tape speed" effect.

On **Dual** this is the left line's time; on **Mono Tape** it's Head 1's. On **Tape** and **Mono Tape** the actual time
is also divided by Motor Speed.`,

    tempo: `Locks the time to a note value at the current tempo, overriding **Time**. A dot means dotted (1/8. is three
sixteenths, the "Edge" rhythm), T means triplet. At 120 BPM, 1/4 is 500 ms and 1/8. is 375 ms. The longest time is still
16 s. On Dual and Mono Tape it sets the left line or Head 1.`,

    feedback: `How much of each repeat is fed back to make the next one, so how many repeats you hear. 0 % gives a single
echo, 30 % a few, 60 % a long trail, near 100 % repeats that barely fade.

Negative values flip the polarity of each pass. It's the same number of repeats but with a slightly hollower,
less "stacked" tone, because alternate repeats cancel rather than reinforce low end. On Dual this is the left line's
feedback; on Mono Tape, Head 1's.`,

    mix: `The balance of dry and repeats. The Delay's mix law keeps the dry at full level up to 50 % while the repeats
rise on a squared curve (about −16 dB at 20 %, −12 dB at 25 %, 0 dB at 50 %). 20 to 30 % suits most rhythm and lead
playing. Use 100 % with **Kill Dry**, or in a parallel path, when the dry already reaches the output another way.`,

    lowcut: `A high-pass filter in the feedback loop. It thins the low end of the repeats so they don't muddy your bass
notes and chords. Because it sits in the loop, the first repeat is full range and each later repeat is filtered once
more, so the trail gets thinner as it fades. 150 to 300 Hz is a good range for most guitar delays. At its lowest
setting it's off. The slope and resonance are on the EQ page.`,

    highcut: `A low-pass filter in the feedback loop. It darkens the repeats so they sit behind the dry note. Like Low Cut,
it acts from the second repeat on, and each pass is darker than the last, which is how real analog and tape echoes behave.
2 to 5 kHz is typical. At its highest setting it's off.

Analog, Tape, Mono Tape and Vintage Digital add their own fixed darkening (about 2.8 kHz, 6.5 kHz × Motor Speed and
7 kHz). The lower of that and this knob wins.`,

    lfo1Rate: `The speed of LFO 1, which wobbles the delay time to give the repeats a chorus or vibrato shimmer. Slow
rates (0.3 to 1 Hz) give a gentle, tape-like drift; faster ones a more obvious warble. Ignored when **LFO 1 Tempo** is
set.`,

    lfo1Depth: `How far LFO 1 moves the delay time, in milliseconds (times the **LFO Depth Range**). A few tenths of a
millisecond gives a subtle chorus on the repeats; 2 to 5 ms a clear pitch wobble. 0 turns LFO 1 off. Not used by the
Reverse type.`,

    level: `The block's output level, wet and dry together. Use it to match the volume with the block on and off.`,

    balance: `Turns down one side of the block's output, wet and dry alike. At 0 both sides are at full level.`,

    inputGain: `How much of your signal goes into the delay lines. The dry is not affected. Turn it down to make the
repeats quieter without changing the Mix law's dry level, or modulate it to send only some notes into the delay (a
"dub" send).`,

    ratio: `Sets the right line's time as a percentage of the left's. At 100 % both sides repeat together. At 75 % the
right repeats come earlier, so the echoes bounce between the speakers in an uneven pattern; small changes (90 to 98 %)
just widen the sound. On Ping-Pong it shortens the hop to the right side. Hidden on Dual and Mono Tape, and it has no
effect on Reverse.`,

    drive: `Saturates what goes into the delay line with a soft tanh curve. Light settings add warmth and keep a high
feedback from getting harsh; higher settings make the repeats grittier the more they build up. 0 is clean. Bypassed
while Hold or Stack is on.`,

    bitReduction: `Lowers the word length of the repeats from 24 bits down to as few as 4, for a grainy, lo-fi edge.
Because it acts on what's read from the line, the first repeat is affected too. Vintage Digital always runs at 12 bits
and ignores this knob.`,

    spread: `The stereo width of the repeats. 100 % leaves them as they are, 0 % folds them to mono, negative values swap
the sides, and up to ±200 % widens them past the speakers by raising their side signal. It only has an effect when the
two sides differ, which a mono guitar into a plain Digital delay doesn't give you. Use **L/R Time Ratio**, an LFO phase,
**Right Post Delay**, Ping-Pong or Dual to make them differ.`,

    duckAttenuation: `How far the repeats are turned down while you play. 0 dB turns ducking off. 6 to 12 dB pulls the
repeats back under a lead and lets them bloom in the gaps; 20 dB or more nearly mutes them while you play.`,

    duckThreshold: `How loud your playing must be for the ducker to turn the repeats down. Lower values duck on softer
playing. Set it just below the level of your picked notes so the repeats come back as a note dies away.`,

    duckRelease: `How quickly the repeats swell back once you stop playing or your note falls below the threshold. Short
releases (100 to 200 ms) let the repeats pop back in between phrases; longer ones (500 ms to 1 s) give a slow bloom. The
ducker always pulls down within about 5 ms.`,

    hold: `Freezes the repeats. **Hold** sets the feedback to exactly 100 %, closes the input and turns off drive and
saturation, so whatever is in the line loops forever while you play over it. **Stack** does the same with the input
open, so you can keep layering; a limiter stops the stack from clipping. Any Low Cut, High Cut, EQ or type darkening
still acts every pass, so a held loop slowly loses lows or highs unless those filters are off.`,

    phaseReverse: `Inverts the polarity of the repeats on the left, right or both sides. Inverting one side makes the
echoes sound wide and diffuse in stereo but makes them thin or disappear when summed to mono, so use it with care.
Inverting both changes how the repeats combine with the dry.`,

    masterFeedback: `Scales every feedback in the block (both lines, cross feedback and Head 2) at once. 100 % leaves them
as set. Above 100 % the loop can pass unity and the repeats grow; a soft limiter holds them at a saturated ceiling
instead of letting them explode. Put it on a [modifier](/manual/modifiers) or footswitch for runaway "oscillation"
swells.`,

    echoPan: `Pans the repeats, after Spread. At 0 they stay where they are; turning it moves them towards one side (the
far side fades out). Useful to put a mono delay to one side of the mix, away from the dry guitar.`,

    rightPostDelay: `Delays only the right side of the repeats by up to 1 second, outside the feedback loop. A few
milliseconds to about 20 ms widens a mono delay without changing its rhythm; longer settings make the right repeats a
separate, offset echo.`,

    timeRight: `Dual only: the right line's delay time. Set it differently from the left time for interlocking rhythms,
for example 1/4 on the left and 1/8. on the right.`,

    tempoRight: `Dual only: locks the right line's time to a note value at the current tempo, overriding **Right Time**.`,

    feedbackRight: `Dual only: the right line's own feedback, in either polarity.`,

    levelLeft: `Dual only: the level of the left line's repeats.`,

    levelRight: `Dual only: the level of the right line's repeats.`,

    panLeft: `Dual only: where the left line's repeats sit in the stereo field. The default puts them hard left.`,

    panRight: `Dual only: where the right line's repeats sit. The default puts them hard right.`,

    crossLeftToRight: `Dual only: feeds the left line's repeats into the right line. With some cross feed in both
directions the two lines trade echoes and the pattern gets more complex as it decays. Negative values flip polarity.`,

    crossRightToLeft: `Dual only: feeds the right line's repeats into the left line.`,

    sweepStart: `Sweep only: one end of the band-pass filter's sweep. The filter moves between this and **Sweep Stop** on
a logarithmic scale, so the movement sounds even.`,

    sweepStop: `Sweep only: the other end of the band-pass filter's sweep. Swap Start and Stop to reverse the direction of
a sawtooth sweep.`,

    sweepResonance: `Sweep only: the filter's Q. Low values give a broad, gentle sweep; high ones a narrow, vocal,
wah-like peak.`,

    sweepShape: `Sweep only: the waveform that drives the sweep. Triangle (the default) moves evenly up and down; Sine
lingers at the ends; Saw shapes give a one-way sweep that jumps back. See the LFO shapes below.`,

    sweepRate: `Sweep only: how fast the filter sweeps. Ignored when **Sweep Tempo** is set.`,

    sweepTempo: `Sweep only: locks one sweep cycle to a note value at the current tempo, overriding **Sweep Rate**.`,

    sweepPhase: `Sweep only: the offset between the left and right filters. At 0° they move together; at 90° (the
default) to 180° they move apart and the sweep swirls across the speakers.`,

    motorSpeed: `Tape and Mono Tape only: the speed of the tape. Slower than 100 % lengthens the time (50 % doubles it),
darkens the repeats and slows the wow and flutter; faster shortens the time, brightens and speeds them up. Sweep it for
the classic tape-machine pitch bend.`,

    head2Ratio: `Mono Tape only: where the second playback head sits, as a percentage of Head 1's time. 50 % puts it
halfway, so you hear two repeats per Head 1 cycle. 0 turns Head 2 off.`,

    head1Level: `Mono Tape only: the level of the first (main) playback head.`,

    head2Level: `Mono Tape only: the level of the second playback head.`,

    head1Pan: `Mono Tape only: where Head 1's repeats sit in the stereo field.`,

    head2Pan: `Mono Tape only: where Head 2's repeats sit in the stereo field.`,

    head2Feedback: `Mono Tape only: how much of Head 2's output goes back onto the tape. With both heads feeding back
the repeat pattern gets denser and more irregular, like a multi-head echo unit.`,

    reverseRun: `Reverse only: starts and stops the reverse playback. Off fades the reversed output out over a few
milliseconds; turning it back on starts a fresh chunk. Put it on a footswitch to drop reversed phrases in and out.`,

    reverseRestart: `Reverse only: when on, a new reversed chunk starts as soon as you play a new note (the input
crossing about −40 dBFS), instead of on a fixed cycle of **Time**. This lines the reversed swell up with your playing.`,

    reverseCrossfade: `Reverse only: how long the old chunk and the new one overlap, as a percentage of **Time**. Short
crossfades keep the reversed notes distinct but can make the joins audible; longer ones smooth them into a wash.`,

    lowCutSlope: `How steeply **Low Cut** rolls off: 6 dB/oct is gentle, 24 dB/oct cuts hard below the frequency.
Remember the filter is in the loop, so its effect compounds on every repeat.`,

    highCutSlope: `How steeply **High Cut** rolls off. Steeper slopes give a clearly "band-limited" repeat, like an old
delay unit; 6 dB/oct darkens gradually.`,

    lowCutQ: `The resonance at the Low Cut corner. 0.71 is flat (Butterworth); higher values add a bump just above the
cutoff. Because it's in the loop, even a small bump grows on every repeat. The limiter engages automatically if the loop
could pass unity.`,

    highCutQ: `The resonance at the High Cut corner. Raising it adds a peak just below the cutoff, which accumulates over
the repeats into a ringing, resonant tone.`,

    eq1Frequency: `The centre frequency of the first peaking EQ band in the feedback loop.`,

    eq1Q: `The width of the first EQ band. Low Q is broad; high Q is narrow.`,

    eq1Gain: `Boosts or cuts the first EQ band. It acts on every pass, so the effect builds up: a 3 dB cut at 400 Hz is a
mild change on the second repeat and a big one by the fifth. 0 dB turns the band off.`,

    eq2Frequency: `The centre frequency of the second peaking EQ band in the feedback loop.`,

    eq2Q: `The width of the second EQ band.`,

    eq2Gain: `Boosts or cuts the second EQ band, again on every pass. 0 dB turns the band off.`,

    depthRange: `Multiplies the depth of LFO 1 and LFO 2: Low (×0.1) for very fine chorusing, Normal, or High (×10) for
depths of up to 100 ms, which give dramatic pitch dives and seasick warble.`,

    lfo1Type: `The waveform of LFO 1. Sine and Triangle give smooth chorus; Random gives a stepped, unpredictable
wobble. See the shapes below.`,

    lfo1Tempo: `Locks LFO 1 to one cycle per note value at the current tempo, overriding **LFO 1 Rate**.`,

    lfo1Phase: `The offset between LFO 1's effect on the left and right lines. At 0° both sides wobble together; at 90°
or 180° they wobble apart, which makes the repeats wider. Only matters when the target is Both.`,

    lfo1Target: `Which line LFO 1 moves: Both, Left only or Right only. Modulating one side only is an easy way to make a
mono delay stereo.`,

    lfo2Type: `The waveform of LFO 2, a second time modulator that adds to LFO 1. Pairing a slow LFO 1 with a faster LFO
2 gives a more natural, tape-like wobble.`,

    lfo2Rate: `The speed of LFO 2. Ignored when **LFO 2 Tempo** is set.`,

    lfo2Tempo: `Locks LFO 2 to one cycle per note value at the current tempo, overriding **LFO 2 Rate**.`,

    lfo2Depth: `How far LFO 2 moves the delay time, in milliseconds (times the **LFO Depth Range**). 0 turns LFO 2 off.`,

    lfo2Phase: `The offset between LFO 2's effect on the left and right lines.`,

    lfo2Target: `Which line LFO 2 moves: Both, Left only or Right only.`,

    lfo3Target: `What LFO 3 moves. **Level** pulses the repeats like a tremolo; **Pan** moves the echoes across the
stereo field (added to Echo Pan); **Spread** breathes the width in and out.`,

    lfo3Type: `The waveform of LFO 3. Square on Level gives a choppy, gated echo; Sine on Pan a smooth auto-pan.`,

    lfo3Rate: `The speed of LFO 3. Ignored when **LFO 3 Tempo** is set.`,

    lfo3Tempo: `Locks LFO 3 to one cycle per note value at the current tempo, overriding **LFO 3 Rate**. Syncing a
level or pan LFO to the tempo keeps the movement in time with the repeats.`,

    lfo3Depth: `How strongly LFO 3 acts. On Level, 100 % swings the repeats from full volume to silent; on Pan, 100 %
sweeps them from one side to the other; on Spread, 100 % swings the width between its setting and mono. 0 turns LFO 3
off.`,

    diffusionMix: `Blends in a diffuser that smears each repeat into a short wash, without changing its level. At 0 the
repeats are crisp; at 100 % each echo is a soft, reverb-like cloud (a "diffused delay"). 30 to 50 % softens the attack
of the repeats so they compete less with your playing.`,

    diffusionTime: `The size of the diffuser: the longest of its four allpass stages. Short times (5 to 20 ms) blur each
repeat a little; long times (50 to 100 ms) smear it into a wash. The right side's stages are 7 % longer, which also
widens the sound.`,

    diffusionRate: `The speed of a slow LFO that moves the diffuser's stage lengths, to keep the smear from sounding
static.`,

    diffusionDepth: `How far the diffusion LFO moves the stage lengths (up to ±8 % at 100 %). 0 leaves the diffuser
still. A little depth adds a lush, chorused quality to the wash.`,

    globalMix: `When on, the global Effects Mix offset is added to this block's Mix, so one global control makes every
such block wetter or drier for the room you're playing in.`,

    killDry: `Mutes the block's dry signal, so Mix sets only the repeat level. Use it when the delay sits in a parallel
path whose dry already reaches the output.`
  },

  options: {
    type: {
      Digital: `Clean, full-range repeats that stay exactly like your playing unless you filter them. The most
transparent choice and the best start for rhythmic and dotted-eighth parts.`,
      Analog: `Bucket-brigade style: the feedback loop is darkened at about 2.8 kHz and softly saturated, so the repeats
get warmer, darker and more compressed as they fade. Great for leads and for high feedback, since the trail never gets
harsh.`,
      Tape: `A tape echo: gentle saturation, a loop darkened at about 6.5 kHz, and constant wow (about 0.55 Hz) and
flutter (about 6.3 Hz) that make the repeats drift slightly in pitch. **Motor Speed** changes the tape speed.`,
      'Ping-Pong': `Your (mono-summed) signal goes into the left line, and every repeat crosses to the other side, so the
echoes bounce left, right, left. Needs a stereo rig to be heard; **L/R Time Ratio** makes the bounce uneven.`,
      'Vintage Digital': `An early digital rack delay: the repeats are quantised to 12 bits and the loop is darkened at
about 7 kHz. Clear but slightly gritty.`,
      Dual: `Two independent lines, left and right, each with its own time, tempo, feedback, level and pan, plus cross
feedback between them. Use it for two different rhythms at once, or a short and a long delay together. The extra knobs
are on the Stereo page.`,
      'Mono Tape': `A single tape loop with two playback heads, like a multi-head echo unit. Head 1 uses Time, Tempo and
Feedback; Head 2 sits at **Head 2 Ratio** of that time with its own level, pan and feedback. Includes the tape
saturation, darkening, wow and flutter, and Motor Speed. The input is the mono sum.`,
      Reverse: `Records chunks of **Time** and plays each one backwards, so every note swells in and cuts off. Repeats are
fed back at Time, so each repeat is reversed again. Run, Trigger Restart and Crossfade are on the Advanced page. Longest
chunk is about 5.3 s.`,
      Sweep: `A stereo delay whose repeats pass through a resonant band-pass filter swept by an LFO, for a moving, wah-
like or phaser-like echo. The filter is on the repeats you hear, not in the loop. Its knobs are on the Sweep page.`
    },
    'lfo#Type': {
      Random: `Jumps to a new random level each cycle and holds it (sample and hold).`,
      Log: `A rounded top and a sharp dip at the bottom.`,
      Exponential: `A sharp peak at the top and a rounded bottom.`,
      Trapezoid: `A triangle clipped at half its height, so it dwells at both ends and moves quickly between them.`,
      Astable: `The charge and discharge curve of an analog oscillator circuit: a fast rise that slows as it nears the
top, then the same on the way down.`
    },
    sweepShape: {
      Random: `Jumps to a new random filter position each cycle and holds it.`,
      Log: `A rounded top and a sharp dip at the bottom.`,
      Exponential: `A sharp peak at the top and a rounded bottom.`,
      Trapezoid: `Dwells at both ends of the sweep and moves quickly between them.`,
      Astable: `An analog oscillator's charge and discharge curve.`
    },
    hold: {
      Hold: `Feedback at exactly unity, input closed: the current repeats loop forever while you play over them.`,
      Stack: `Feedback at unity with the input open: what you play keeps layering on top, held under a limiter.`
    },
    depthRange: {
      'Low (×0.1)': `LFO 1 and 2 depths are a tenth of what the knobs show: up to 1 ms, for very subtle movement.`,
      'High (×10)': `LFO 1 and 2 depths are ten times what the knobs show: up to 100 ms, for extreme pitch effects.`
    }
  },

  tips: `- **The dotted eighth.** Set **Tempo** to 1/8., **Feedback** 25 to 35 %, **Mix** 25 to 35 % and play straight
eighth notes. The repeats fall between your notes and make a continuous, rolling pattern. Add a second Delay on 1/4
at a lower mix for an even denser rhythm.
- **Slapback.** 80 to 130 ms, one repeat (Feedback 0 to 10 %), Mix about 30 %, High Cut around 3 kHz. Analog or Tape
suit it best.
- **Leads that stay clear.** Set **Ducker Atten** to 8 to 15 dB and the threshold just under your picking level. The
repeats get out of the way while you play and swell up in the gaps, so you can run a higher Mix than you would
otherwise.
- **Stereo width from a mono guitar.** A plain delay on a mono signal gives identical repeats on both sides. Set **L/R
Time Ratio** to about 95 %, give the right side a **Right Post Delay** of 10 to 20 ms, use LFO 1 at 90 to 180° phase, or
pick Ping-Pong or Dual.
- **Self-oscillation.** Raise **Master Feedback** past 100 % with a footswitch or an expression pedal. The repeats build
into a roaring, saturated swell; the limiter keeps it from clipping. With Analog or Tape it also darkens as it grows.
- **Ambient pads.** Long Time (700 ms to 1.5 s), Feedback 60 to 75 %, **Diffusion Mix** 70 to 100 % with a long
Diffusion Time, a little LFO depth, and a Reverb after it.
- **Expert note on the filters.** Low Cut, High Cut, both EQ bands and the type darkening all sit in the feedback path.
The first repeat is as bright and full as your input (bit reduction and drive still apply); every later repeat passes
the filters once more. If you want the first repeat shaped too, run the delay in a parallel row with **Kill Dry** on
and put a [Filter](/manual/blocks/filter) or [Parametric EQ](/manual/blocks/peq) after it in that row.
- **Expert note on modulation.** To keep the modulated read behind the write, LFO and tape modulation add a small fixed
offset (the sum of the depths) to the delay time. With large depths the delay is slightly longer than Time.`
};
