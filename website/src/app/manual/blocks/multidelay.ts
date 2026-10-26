import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Multitap Delay gives you four delay voices in one block, arranged in one of five ways: four taps on one
line, four independent lines, four lines in a chain, a diffusing network, or four taps on a tape loop. Each voice has its
own time, feedback, level, pan, filter, chorus, comb filter and ring modulator, and a row of master knobs scales all four
at once.

Reach for it when one delay isn't enough: rhythmic patterns, multi-head echo sounds, filtered "band" delays where each
repeat has its own tone, or dense diffuse clouds. For a straightforward echo the [Delay](/manual/blocks/delay) block is
simpler; for many evenly placed taps see [Ten-Tap](/manual/blocks/tentap) and [Megatap](/manual/blocks/megatap).`,

  usage: `**Where it goes.** After the amp and cab, before the reverb, like any delay.

**How the times work.** **Master Time** (or **Master Tempo**) sets the length of the pattern, and each voice's **Time**
is a percentage of it. The defaults put the four voices at 25, 50, 75 and 100 % of 400 ms, each 3 dB quieter than the
one before, panned hard left, hard right, half left and half right. A voice's own **Tempo** overrides its percentage
with a note value. Every time is capped at 2 seconds.

**A starting point.** Pick **Quad Tap**, set **Master Tempo** to 1/2, leave the taps at 25/50/75/100 % and give tap 4 a
little feedback (it has 20 % by default). You get four evenly spaced eighth-note repeats bouncing across the stereo
field, and the whole pattern repeating. Then try moving taps 2 and 3 to 37.5 % and 75 % for a dotted feel.

**About Mix.** This block uses a constant-power law: at 25 % the repeats are about 8 dB down and the dry about 0.7 dB
down; at 50 % both sit at −3 dB.

**Tempo.** Tempo knobs follow the preset's tempo (or the global tempo or MIDI clock), and tap tempo moves them. A Tempo
on anything other than Off overrides the matching Time or Rate.`,

  params: {
    type: `How the four voices are wired together. See the options below.`,

    time: `The pattern length that every voice's **Time** percentage is taken from. Change it and the whole pattern
stretches or shrinks together. Changes glide rather than jump.`,

    tempo: `Locks **Master Time** to a note value at the current tempo. 1/2 at 120 BPM is 1 second, so the default taps
land on eighth notes. A dot means dotted, T means triplet.`,

    masterFeedback: `Scales every feedback in the block at once: each voice's Feedback, the cross feedbacks and the Series
Feedback. Above 100 % the loops can pass unity; a limiter in every line write keeps them saturating instead of running
away.`,

    'tap#Time': `Where this voice repeats, as a percentage of **Master Time**. On Quad Tap and Quad Tape it's a tap
position on the shared line; on Quad Parallel, Series and Diffusor it's the length of this voice's own line. On Quad
Series the voices add up: voice 2 is heard after voice 1's time plus its own.`,

    'tap#Tempo': `Locks this voice's time to a note value at the current tempo, overriding its **Time** percentage.`,

    'tap#Feedback': `How much of this voice is fed back. On Quad Tap and Quad Tape it goes back into the shared line, so
the whole pattern repeats every time this voice comes round (with the default 20 % on tap 4 the pattern repeats once per
Master Time). On Quad Parallel and Series it feeds this voice's own line. On Quad Diffusor it's the allpass gain of the
voice, which sets how much it smears (limited to ±90 %). Because the voice's filter, comb and ring mod are applied before
the feedback, their effect builds up on every repeat. Negative values flip polarity.`,

    'tap#Level': `How loud this voice is. Negative values invert its polarity, which can make a voice cancel against
another one panned to the same place, or sound wider in stereo.`,

    'tap#Pan': `Where this voice sits in the stereo field, before **Master Pan** is added.`,

    'cross#': `Quad Parallel and Quad Diffusor only: feeds this voice into the next one (1 into 2, 2 into 3, 3 into 4, 4
into 1). With cross feedback the voices trade echoes and the pattern grows more complex as it decays. Scaled by Master
Feedback; negative values flip polarity.`,

    'filter#Type': `The filter on this voice: Off, Low Pass, High Pass, Band Pass or Peak. Band Pass on each voice at
different frequencies gives the "band delay" effect where each repeat has its own tone. The filtered voice is also what
feeds back, so a voice with feedback gets more filtered on every pass.`,

    'filter#Frequency': `The cutoff or centre frequency of this voice's filter, before **Master Filter Freq** and the filter
LFO move it. The defaults rise an octave per voice (500 Hz, 1, 2 and 4 kHz).`,

    'filter#Q': `The resonance of this voice's filter (for Band Pass, how narrow it is), before **Master Filter Q** scales
it. High values give a ringing, vocal tone.`,

    'filter#Gain': `Peak filters only: how much the band is boosted or cut.`,

    'chorus#Rate': `The speed of this voice's own chorus, a sine wave that moves its delay time. Giving each voice a
slightly different rate (the defaults step up by 0.13 Hz) keeps the four from wobbling in unison. Scaled by **Master LFO
Rate**.`,

    'chorus#Depth': `How far this voice's chorus moves its delay time, in milliseconds (scaled by **Master Chorus**). The
time swings between its setting and twice the depth longer. 0 turns this voice's chorus off.`,

    'comb#Time': `The delay of this voice's comb filter. The comb rings at a pitch of one over this time: 2 ms gives a
500 Hz tone and its harmonics, 10 ms gives 100 Hz. Short times sound metallic; long ones give a pitched buzz.`,

    'comb#Feedback': `How strongly this voice's comb filter resonates (scaled by **Master Comb**, limited to ±95 %). 0 turns
it off. Positive values ring at the comb's pitch and its harmonics; negative values ring an octave lower with only the
odd harmonics, a hollower sound. The comb is normalised so its peaks never rise above unity gain.`,

    'ring#Frequency': `The frequency of this voice's ring modulator. Low settings (1 to 20 Hz) pulse the repeat like a
tremolo; higher ones add clangorous, bell-like sidebands.`,

    'ring#Mix': `How much ring modulation this voice gets (scaled by **Master Ring Mod**). 0 turns it off; 100 % is full ring
modulation.`,

    masterPan: `Added to every voice's pan, so you can shift the whole pattern to one side while keeping the voices'
spacing (a voice already at the edge stays there).`,

    masterLfoRate: `Scales the rate of LFO 1, LFO 2 and every voice's chorus at once. 200 % doubles them all.`,

    masterLfoDepth: `Scales the depth of LFO 1 and LFO 2. It doesn't change the voice choruses; **Master Chorus** does
that.`,

    masterFilterFrequency: `Scales every voice's filter frequency at once, from a quarter (two octaves down) to four times
(two octaves up). Put it on an expression pedal to sweep all four filters together.`,

    masterFilterQ: `Scales every voice's filter Q at once.`,

    masterChorus: `Scales every voice's chorus depth at once. 0 turns all the voice choruses off.`,

    masterComb: `Scales every voice's comb feedback at once. 0 turns all the combs off.`,

    masterRing: `Scales every voice's ring mod mix at once. 0 turns all the ring modulators off.`,

    drive: `Saturates what goes into the lines with a soft tanh curve. A little adds warmth and tames high feedback;
more makes the repeats grittier as they build. 0 is clean.`,

    lowCut: `A high-pass on everything written into the lines, so the first repeat and every later one are thinned, more
so with each pass. 150 to 300 Hz keeps the pattern out of your low end. At its lowest setting it's off.`,

    highCut: `A low-pass on everything written into the lines, so the repeats get darker on every pass. On Quad Tape the
lower of this and the tape's own darkening (6.5 kHz × Motor Speed) wins. At its highest setting it's off.`,

    lowCutSlope: `How steeply **Low Cut** rolls off, from 6 to 24 dB/oct.`,

    highCutSlope: `How steeply **High Cut** rolls off, from 6 to 24 dB/oct.`,

    lfo1Type: `The waveform of LFO 1, which moves every voice's time. See the [Delay block](/manual/blocks/delay) for what
the shapes do.`,

    lfo1Rate: `The speed of LFO 1 (scaled by **Master LFO Rate**). Ignored when **LFO 1 Tempo** is set.`,

    lfo1Tempo: `Locks LFO 1 to one cycle per note value at the current tempo, overriding **LFO 1 Rate**.`,

    lfo1Depth: `How far LFO 1 moves every voice's time, in milliseconds (scaled by **Master LFO Depth**). 0 turns it off.`,

    lfo1Phase: `How far apart the voices sit on LFO 1's cycle: voice 2 is this many degrees after voice 1, voice 3 twice
as far, voice 4 three times. At 90° the four voices are spread evenly over a cycle, so they never wobble together.`,

    lfo2Type: `The waveform of LFO 2, a second time modulator added to LFO 1.`,

    lfo2Rate: `The speed of LFO 2 (scaled by **Master LFO Rate**). Ignored when **LFO 2 Tempo** is set.`,

    lfo2Tempo: `Locks LFO 2 to one cycle per note value at the current tempo, overriding **LFO 2 Rate**.`,

    lfo2Depth: `How far LFO 2 moves every voice's time, in milliseconds (scaled by **Master LFO Depth**). 0 turns it off.`,

    lfo2Phase: `How far apart the voices sit on LFO 2's cycle, as for LFO 1.`,

    filterLfoType: `The waveform of the filter LFO, which sweeps every voice's filter together.`,

    filterLfoRate: `The speed of the filter LFO. Unlike the other LFOs it isn't scaled by Master LFO Rate and has no tempo
sync.`,

    filterLfoDepth: `How far the filter LFO sweeps every voice's filter, in octaves either way. 1 octave sweeps a 1 kHz
filter between 500 Hz and 2 kHz. 0 turns the sweep off.`,

    diffusionMix: `Blends in a diffuser after the voices that smears each repeat into a short wash, without changing its
level. 0 is crisp; 100 % is fully diffused.`,

    diffusionTime: `The size of the diffuser. Short times blur the repeats a little; long ones (50 ms and up) turn them
into a cloud.`,

    diffusionRate: `The speed of the slow LFO that moves the diffuser, so the wash doesn't sound static.`,

    diffusionDepth: `How far the diffusion LFO moves the diffuser (up to ±8 % of its lengths). 0 leaves it still.`,

    duckThreshold: `How loud your playing must be for the ducker to turn the repeats down. Set it just under your picking
level.`,

    duckAttenuation: `How far the repeats drop while you play. 0 dB turns ducking off; 6 to 12 dB keeps a busy pattern out
of the way of a lead.`,

    duckRelease: `How quickly the repeats swell back once you stop. The ducker always pulls down within about 5 ms.`,

    envelopeThreshold: `The level your playing must cross to start a swell, when **Envelope Attack** is above 0.`,

    envelopeAttack: `Swells what enters the delay lines, like a hand on a volume knob: each time you play a note above the
threshold, the input to the delays rises from silence over this time on a smooth S-curve. The dry isn't affected, so you
hear your pick attack and then the repeats fade in. 0 turns the swell off; 300 ms to 1 s gives a violin-like bloom.`,

    envelopeRelease: `How quickly the swell closes again once your note falls 6 dB below the threshold. The next note
above the threshold starts a new swell from silence.`,

    seriesFeedback: `Quad Series only: feeds voice 4, the end of the chain, back into the start of line 1. Scaled by
Master Feedback.`,

    seriesSend: `Quad Series only: how much of each voice is sent into the next line in the chain. Lower values make each
step of the chain quieter.`,

    seriesReturn: `Quad Series only: how loud voices 1, 2 and 3 are in the output. Voice 4 is always heard at its own level.
Turn it down to hear mostly the end of the chain.`,

    motorSpeed: `Quad Tape only: the tape speed. Slower than 100 % stretches every tap's time (50 % doubles it), darkens the
loop and slows the wow and flutter; faster does the opposite.`,

    mix: `The balance of dry and repeats, on a constant-power law (about −8 dB of wet at 25 %, both at −3 dB at 50 %).`,

    level: `The block's output level, wet and dry together.`,

    balance: `Turns down one side of the block's output, wet and dry alike.`,

    inputGain: `How much of your signal enters the delay lines. The dry is not affected.`,

    inputSelect: `Which input the delays hear: the sum of both sides, the left only or the right only. The output is
always stereo.`,

    globalMix: `When on, the global Effects Mix offset is added to this block's Mix.`,

    killDry: `Mutes the block's dry signal, so Mix sets only the repeat level. For parallel paths that already carry the
dry.`
  },

  options: {
    type: {
      'Quad Tap': `One delay line fed by your (mono) signal, read by four taps. Each tap's processed output can feed back
into the line, so a tap with feedback makes the whole pattern repeat at its time. The classic multi-tap pattern
generator.`,
      'Quad Parallel': `Four independent delay lines side by side, each with its own time and feedback, and **Cross
Feedback** from each one into the next (1 → 2 → 3 → 4 → 1). Like four delays in parallel that can trade echoes.`,
      'Quad Series': `The four lines in a chain: line 1 feeds line 2, which feeds line 3, which feeds line 4, at the
**Series Send** level. The times add up along the chain, and **Series Feedback** sends the end back to the start. Voices
1 to 3 are heard at the **Series Return** level.`,
      'Quad Diffusor': `Each line becomes a long allpass diffuser, with its **Feedback** as the allpass gain. Instead of
distinct echoes you get a dense, smeared wash with no colouration, good for ambient pads and reverb-like clouds. Cross
Feedback links the four.`,
      'Quad Tape': `Quad Tap on a tape loop: saturation, a loop darkened at about 6.5 kHz, gentle wow and flutter, and a
**Motor Speed**. Sounds like a multi-head tape echo.`
    }
  },

  tips: `- **Band delay.** On Quad Tap, set each voice's filter to Band Pass at a different frequency (say 400 Hz, 900 Hz,
2 kHz, 4 kHz) with a Q of 2 to 4. Each repeat comes back with its own tone. Sweep **Master Filter Freq** with an
expression pedal to move all four at once.
- **Multi-head echo.** Quad Tape with taps at 25, 50, 75 and 100 %, levels roughly equal and a little feedback on tap 4
gives the irregular, rolling repeats of a vintage multi-head unit. Turn individual taps' levels to 0 for different head
combinations.
- **Dense ambient wash.** Quad Diffusor with long, unrelated tap times (e.g. 37, 53, 71, 97 %), Feedback around 60 to
70 % on each voice, a little cross feedback, a Low Cut around 200 Hz and some LFO depth. Put a reverb after it.
- **Keep it out of the way.** Four voices fill up the mix quickly. Use a lower Mix than you'd use on a single delay,
turn on the ducker, and pan the voices away from where your dry guitar sits.
- **Expert note.** Unlike the [Delay](/manual/blocks/delay) block, the Low and High Cut here filter everything written
into the lines, input included, so the first repeat is filtered too. The voice filters, combs and ring mods sit on each
voice's read, before its feedback.`
};
