import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Plex Delay is up to eight delay lines that feed into each other, so every echo spawns echoes on the other
lines and the repeats thicken into a dense, evolving wash somewhere between a delay and a reverb. Five types share the
network: plain lines, lines that detune each pass, lines that pitch-shift each pass (so the repeats climb or fall), a
reverb built from short lines, and a shimmer reverb that adds an octave on every pass.

It's an ambient tool. Reach for it for pads, swells, shimmer, cascading pitched echoes and big atmospheric tails behind
a clean or lightly driven guitar.`,

  usage: `**Where it goes.** At the end of the chain, after the amp and cab, either instead of or before your reverb. It
works best on clean and edge-of-breakup tones; with high gain the wash can get dense very quickly.

**How it decays.** Instead of a feedback percentage, Plex has a **Decay Time**: every line's loop gain is set so its
repeats fall by 60 dB in that many seconds, whatever the line's length. Short decays (0.5 to 1.5 s) give a rhythmic
cluster; long ones (5 to 15 s) a pad.

**A starting point.** Plex Delay, 8 delays, Decay 3 s, Diffusion 40 %, Low Cut 150 Hz, High Cut 5 kHz, Mix 30 %. Then try
Plex Detune for a wider, chorused wash, or Shimmer Verb with a 6 to 10 s decay for an ambient pad.

**About Mix.** Plex uses a linear mix: at 30 % the wet is about 10.5 dB down and the dry about 3 dB down; at 50 % both are
6 dB down. You'll often want 25 to 40 %, or 100 % with **Kill Dry** in a parallel path.

**Tempo.** The Tempo knobs follow the preset's tempo (or the global tempo or MIDI clock) and override the matching time
or rate.`,

  params: {
    type: `Which version of the network you hear. See the options below.`,

    delays: `How many lines are in the network, from 1 to 8. More lines make a denser, smoother wash; fewer give more
distinct echoes. The input reaches every line at 1/√N, so the level stays about the same. Changing it restarts the
network empty, so the tail is cut.`,

    masterTime: `Scales every line's time at once, from 10 % to 200 %. Use it to make the whole cluster tighter or more
spacious without editing eight times. Not used by the two verb types, which use Size.`,

    decay: `How long the wash takes to fall by 60 dB, in seconds, for every line. This is the Plex's feedback control.
Longer decays build a sustained pad; on the shifting types they also let the pitch climb further.`,

    diffusion: `Smears each pass with an allpass in every loop. At 0 each line's echoes stay distinct and arrive exactly on
its time; higher values blur them, and the network sounds more like a reverb. The verb types diffuse a little more for
the same setting.`,

    inputDiffusion: `Smears your signal with four allpasses before it enters the lines. It softens the attack of the
first echoes, so even the start of the wash is smooth. 0 leaves the input as it is.`,

    preDelay: `A gap before your signal reaches the network, up to 1 second. Lets your dry note speak before the wash
starts.`,

    lowCut: `A high-pass (12 dB/oct) inside every loop. It acts on every pass, so the wash gets thinner as it decays.
Raising it to 150 to 300 Hz keeps a long wash from turning into low-end mud. At its lowest setting it's off.`,

    highCut: `A low-pass (12 dB/oct) inside every loop, so the wash darkens as it decays. The default 8 kHz already rounds
it off; 3 to 5 kHz gives a warmer, more distant pad. At its highest setting it's off.`,

    hold: `**Hold** freezes the network: the decay becomes effectively infinite and the input closes, so the current wash
sustains while you play over it. **Stack** does the same with the input open, so new notes keep piling in (a limiter
keeps it bounded). The loop filters and pitch shifting still act on every pass, so a held wash keeps darkening, thinning
or climbing if they're set.`,

    masterPan: `Added to every line's pan, to shift the whole wash towards one side while keeping the lines' spacing.`,

    'time#': `This line's delay time. With no diffusion, its first echo arrives exactly at this time. The defaults are
unrelated values (143, 197, 263 ms and so on) so the echoes don't line up; set them to related values for a more
rhythmic cluster. Scaled by Master Time. Not used by the verb types.`,

    'tempo#': `Locks this line's time to a note value at the current tempo, overriding its Time. Still scaled by Master
Time. Not used by the verb types.`,

    'level#': `How loud this line is in the output. It doesn't change how much the line feeds the others, so a line at 0
still shapes the wash; you just don't hear it directly.`,

    'pan#': `Where this line sits in the stereo field, before Master Pan. The defaults alternate from hard left and right
inwards.`,

    filterType: `A filter inside every loop: Off, Low Pass, High Pass, Band Pass or Peak. Because it acts on every pass,
it shapes the whole wash, and with **Mod Frequency** it can sweep.`,

    filterFrequency: `The cutoff or centre frequency of the loop filter.`,

    filterQ: `The resonance of the loop filter (for Band Pass, how narrow it is). Since it acts on every pass, a high Q
makes the wash ring at that frequency.`,

    filterGain: `Peak only: how much the loop filter boosts or cuts its band, on every pass.`,

    filterLfoRate: `The speed of the sine LFO that sweeps the loop filter. Ignored when **Filter LFO Tempo** is set.`,

    filterLfoTempo: `Locks the filter LFO to one cycle per note value at the current tempo.`,

    modFrequency: `How far the filter LFO sweeps the loop filter, in octaves either way. 0 keeps the filter still. A slow
sweep of 1 to 2 octaves on a Band Pass makes the wash breathe.`,

    envelopeThreshold: `The level your playing must cross to start a swell, when **Envelope Attack** is above 0.`,

    envelopeAttack: `Swells what enters the network: each note above the threshold fades in from silence over this time,
on a smooth S-curve, like a volume pedal. The dry isn't affected. 300 ms to 1 s turns picked notes into pads. 0 turns
it off.`,

    envelopeRelease: `How quickly the swell closes once your note falls 6 dB below the threshold. The next note starts a
new swell from silence.`,

    'detune#': `Plex Detune only: how far this line is detuned on each pass, in cents. Because it's in the loop, the
detuning compounds: a line at +9 cents is 18 cents sharp on its second pass. The defaults mix small sharp and flat
amounts so the wash beats and spreads rather than drifting one way.`,

    masterDetune: `Plex Detune only: scales every line's detune at once. 0 removes the detune; 200 % doubles it.`,

    granule: `The grain length of the pitch shifters, in milliseconds. Short grains (20 to 40 ms) track faster but sound
buzzier; long grains (80 to 200 ms) are smoother but smear the attack and add a chorused warble.`,

    'shift#': `Plex Shifter only: how far this line is pitch-shifted on each pass, in semitones. The shift compounds, so a
line at +12 climbs an octave on every repeat until the decay takes it away. The defaults mix octaves, fifths and fourths
up and down for a cascading, harp-like cloud.`,

    direction: `**Forward** shifts normally. **Reverse** plays every grain backwards, which turns the pitched repeats into
a shimmering, reversed texture.`,

    crossfade: `How much of each grain fades into the next. 100 % is the smoothest; shorter fades hold each grain at full
level longer and switch quickly, which sounds more defined but choppier.`,

    verbSize: `Plex Verb and Shimmer Verb only: the size of the space. It scales the eight short line lengths (about 31 to
72 ms at 50 %) from 0.3 to 1.7 times. Bigger sounds roomier and less dense.`,

    shimmerIntensity: `Shimmer Verb only: how much of the octave-up shift is blended into each pass. 0 % is a plain Plex
Verb; higher values make the shimmer climb faster and louder. Around 30 to 50 % gives a classic shimmer pad.`,

    lfoRate: `The speed of the sine LFO that moves every line's time, for chorus and movement in the wash. The lines are
spread evenly across its cycle so they don't move together. Ignored when **LFO Tempo** is set.`,

    lfoTempo: `Locks the time LFO to one cycle per note value at the current tempo.`,

    lfoDepth: `How far the LFO moves each line's time, in milliseconds. A few tenths to 2 ms gives a lush chorus on the
wash; more gives a seasick pitch wobble. 0 turns it off.`,

    duckAttenuation: `How far the wash drops while you play. 0 dB turns ducking off. 6 to 15 dB keeps a big wash from
burying your notes and lets it bloom between phrases.`,

    duckThreshold: `How loud your playing must be for the ducker to turn the wash down.`,

    duckRelease: `How quickly the wash swells back once you stop. The ducker always pulls down within about 5 ms.`,

    spread: `The stereo width of the wash. 100 % leaves it as the line pans make it, 0 % folds it to mono, negative values
swap the sides.`,

    mix: `The balance of dry and wet, on a linear law (both at −6 dB at 50 %).`,

    level: `The block's output level, wet and dry together.`,

    balance: `Turns down one side of the block's output, wet and dry alike.`,

    inputGain: `How much of your signal enters the network. The dry is not affected.`,

    inputSelect: `Which input the network hears: the sum of both sides, the left only or the right only.`,

    killDry: `Mutes the block's dry signal, so Mix sets only the wet level. For parallel paths that already carry the
dry.`
  },

  options: {
    type: {
      'Plex Delay': `The eight lines alone, cross-fed. A dense, diffuse multi-echo that builds into a wash.`,
      'Plex Detune': `Each line is detuned by a few cents on every pass, so the wash beats, chorusses and spreads wider as
it decays. Lush on clean chords.`,
      'Plex Shifter': `Each line is pitch-shifted by its interval on every pass, so the repeats climb or fall in pitch: a
cascading, harp-like or "infinite staircase" effect.`,
      'Plex Verb': `The same network with short lines set by **Size** and denser diffusion: a smooth reverb. The line Time
and Tempo knobs are not used.`,
      'Shimmer Verb': `Plex Verb with an octave-up shifter in every loop, blended in by **Shimmer Intensity**. Each pass
climbs an octave, for the ethereal, organ-like shimmer pad.`
    },
    hold: {
      Hold: `Freezes the wash with the input closed: play over a sustained pad.`,
      Stack: `Freezes the wash with the input open: every new note piles on top.`
    }
  },

  tips: `- **Shimmer pad.** Shimmer Verb, Size 70 %, Decay 8 to 12 s, Shimmer Intensity 40 %, Low Cut 200 Hz, High Cut
6 kHz, Mix 30 to 40 %. Add **Envelope Attack** around 500 ms for a swelled, volume-pedal feel.
- **Rising cascade.** Plex Shifter with every shift at +12 or +7, Decay 3 to 5 s, Diffusion 20 % and a High Cut around
6 kHz to stop the top end from getting shrill as it climbs.
- **Freeze and play.** Put **Stack/Hold** on a footswitch. Hold a chord, freeze it, then solo over the pad.
- **Keep the low end clean.** The wash sums eight lines; a Low Cut of 150 to 300 Hz stops it from building up bass,
especially at long decays.
- **Expert note.** The lines are mixed by a Householder feedback matrix (I − 2/N · 11ᵀ), which is lossless, so the
network decays only by the per-line gains derived from Decay. The low and high cut, loop filter and pitch shifter all
sit inside each loop, after the line read; what you hear from each line is taken after them and before the decay gain.`
};
