import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Reverb block puts your guitar in a space: a small room, a hall, a plate, a spring tank, a cathedral or one
of the classic digital reverbs of the late 1970s and 80s. Eleven types cover subtle ambience through to huge ambient
washes, and a pitch section turns any of them into a shimmer reverb.

Every type shares the basics (decay, size, pre-delay, high and low cut, mix). The deeper pages add early reflections,
separate decay times for the lows and highs, modulation, diffusion, spring controls, a wet EQ, ducking, and Hold and
Stack for freezing the tail.`,

  usage: `**Where it goes.** Usually last, after the amp, cab and delay. A reverb in front of a driven amp gets distorted
along with your guitar, which can work for a gritty spring on a low-gain tone but rarely suits high gain.

**A starting point.** Hall or Plate, **Decay** 1.5 to 2.5 s, **Pre-Delay** 20 to 40 ms, **Low Cut** 100 to 200 Hz, **High
Cut** 4 to 6 kHz, **Mix** 15 to 25 %. For a rhythm tone that should just sound "in a room", try Room or Classic Room with
a decay under 1 s.

**About Mix.** The Reverb uses a constant-power law: at 20 % the wet is about 10 dB down and the dry under half a dB down;
at 50 % both are at −3 dB. A reverb you can clearly hear on its own often sounds too much in a band, so set it with the
band, or a backing track, playing.

**Trails.** By default a bypassed block goes silent at once. Set the block's bypass mode to **Mute FX In** to let the
tail ring out when you switch the reverb off.`,

  params: {
    type: `The reverb algorithm. Each type has its own structure and character; see the options below. Changing type
starts the new tank empty.`,

    decay: `How long the tail takes to fall by 60 dB after you stop, in seconds. Under 1 s sounds like a room; 1.5 to 3 s
like a hall or plate; 5 s and longer is an ambient wash. The tail follows the setting closely on every type (Classic
Room bottoms out near 0.3 s).`,

    size: `The size of the space. It scales the reverb's internal delay lengths (from half to one and a half times) and
the early reflection pattern, so small settings sound tighter and more resonant, large ones roomier and slower to build.
It changes the character more than the length; use **Decay** for that. Size isn't smoothed, so set it rather than
sweeping it while a tail is ringing.`,

    predelay: `A gap between your note and the start of the reverb, up to 1 second. 10 to 40 ms keeps the attack of each
note clear before the space blooms behind it, which helps a lot in a dense mix. Longer pre-delays sound like a distinct
slap followed by the reverb.`,

    damping: `How fast the high frequencies die away in the tail. It's a filter inside the reverb's loops, so the tail
gets darker the longer it rings, the way sound behaves in a real room. 4 to 6 kHz suits most guitar tones; lower gives a
warm, distant sound; higher a bright, airy one. On Spring it damps each pass around the springs, so the drip keeps
its bite while the highs of the tail die away faster than the lows, as a real tank's do; **Spring Tone** sets how bright
the springs are to begin with.`,

    lowcut: `A high-pass (12 dB/oct) on the reverb's output. It removes the low end from the wet only, so the reverb
doesn't muddy your low notes or the bass player. 100 to 200 Hz is common; up to 400 Hz for a thin, airy reverb behind
heavy rhythm parts. At its lowest setting it's off.`,

    mix: `The balance of dry and wet, on a constant-power law (about −10 dB of wet at 20 %, both at −3 dB at 50 %).
15 to 25 % suits most playing; ambient patches go to 40 % or higher.`,

    level: `The block's output level, wet and dry together.`,

    balance: `Turns down one side of the block's output, wet and dry alike.`,

    inputGain: `How much of your signal enters the reverb. The dry is not affected. Modulate it to send only some notes
into the reverb.`,

    inputSelect: `Which input the reverb hears: the sum of both sides, the left only or the right only. The output is
always stereo.`,

    spread: `The stereo width of the reverb. 100 % leaves it as the algorithm makes it, 0 % folds it to mono, negative
values swap the sides, and up to ±200 % widens it past the speakers by raising its side signal. Very wide settings can
sound hollow in mono.`,

    duckAttenuation: `How far the reverb drops while you play. 0 dB turns ducking off. 6 to 12 dB keeps a big reverb from
smearing fast lead lines, then lets it bloom when you hold a note or stop.`,

    duckThreshold: `How loud your playing must be for the ducker to turn the reverb down. Set it just below your picking
level.`,

    duckRelease: `How quickly the reverb swells back once you stop or your note falls below the threshold. The ducker
always pulls down within about 5 ms.`,

    hold: `**Hold** freezes the tail: the decay stretches to 600 seconds and the input closes, so the current sound
sustains while you play over it. **Stack** does the same with the input open, so every note adds to the frozen pad (a
soft limiter keeps it bounded). The High Cut damping still acts, so a held tail slowly darkens.`,

    quality: `Room, Hall, Plate, Chamber and Cathedral only: how many delay lines the reverb uses, 4 (Economy), 8 (Normal),
12 (High) or 16 (Ultra High). More lines give a denser, smoother tail with less metallic ringing, at more CPU. The level
is matched, so switching doesn't change the volume. Changing it starts the tank empty.`,

    lowCrossover: `The frequency below which **Low Freq Time** applies.`,

    lowTime: `The decay time below the low crossover, as a percentage of **Decay**. Below 100 % the bass dies sooner than
the rest, which keeps a long reverb from booming; above 100 % it lingers, for a dark, big-hall sound. 50 to 80 % is a good
range for guitar. At 100 % (with High Freq Time also 100 %) the split is off.`,

    highCrossover: `The frequency above which **High Freq Time** applies.`,

    highTime: `The decay time above the high crossover, as a percentage of **Decay**. Below 100 % the top end fades sooner,
on top of what the High Cut damping does; above 100 % it rings longer, for a bright, shimmering tail.`,

    earlyLevel: `The level of the early reflections: the first few distinct echoes off nearby walls, before the dense tail.
They give a sense of the room's size and shape. Off by default; 20 to 50 % adds realism and helps a short reverb sound
like a real space. Not on Spring.`,

    lateLevel: `The level of the reverb tail itself. Turn it down with Early Level up to get just the reflections, a
"room" sound with no long tail.`,

    earlyDiffusion: `Smears the early reflections. Low values keep them as distinct, slap-like echoes; high values blend
them into a soft cloud.`,

    earlyTime: `Stretches or squeezes the early reflection pattern, on top of **Size**. Lower values bunch the reflections
up like a small room; higher ones spread them out like a big one.`,

    earlyDecay: `How quickly the reflections fade across the pattern. At 0 % they're all equally loud; at 100 % the last
one is 60 dB below the first.`,

    lateInputMix: `How much the tail is fed by the early reflections instead of by your signal directly. At 0 % the tail
hears your (pre-delayed) signal; at 100 % it hears only the reflections, so the tail grows out of them, which sounds
smoother and more natural. Works even with Early Level at 0.`,

    lowMidFrequency: `The centre frequency of the low-mid EQ band on the reverb's output.`,

    lowMidQ: `The width of the low-mid band. Low Q is broad; high Q is narrow.`,

    lowMidGain: `Boosts or cuts the low-mid band of the wet. A cut of 2 to 4 dB around 300 to 500 Hz clears out boxiness
and mud. 0 dB turns the band off.`,

    highMidFrequency: `The centre frequency of the high-mid EQ band on the reverb's output.`,

    highMidQ: `The width of the high-mid band.`,

    highMidGain: `Boosts or cuts the high-mid band of the wet. A small cut around 2 to 4 kHz tames a harsh, splashy tail; a
boost adds presence. 0 dB turns the band off.`,

    preDelayTempo: `Locks the pre-delay to a note value at the current tempo, overriding **Pre-Delay** (up to 1 second).
A 1/16 or 1/32 pre-delay lets the reverb bloom in time with the music.`,

    preDelayFeedback: `Repeats the pre-delay into the reverb, up to 90 %. You hear a series of echoes at the pre-delay time,
each one smeared into reverb: an "echo-verb" in one block. Pairs well with a tempo-synced pre-delay.`,

    preDelayMix: `How much of the reverb's input comes through the pre-delay. At 100 % the reverb hears only the delayed
signal; lower values blend in your signal without the delay, so the reverb starts immediately and again after the
pre-delay.`,

    echoDensity: `How quickly the echoes in the tail multiply. On Room, Hall, Plate, Chamber and Cathedral it sets the
length of the wall allpasses, so it only does something when **Wall Diffusion** is above 0. On Classic Hall and Classic
Plate it scales the diffusion inside the loop: 50 % is the stock voicing, lower is sparser and grainier, higher is
denser.`,

    wallDiffusion: `Room, Hall, Plate, Chamber and Cathedral only: adds an allpass in every loop, so each pass is smeared
again. It thickens the tail and removes any fluttery or metallic quality, without changing the decay. Off by default.`,

    inputDiffusion: `How much your signal is smeared before it enters the tail. High values give a smooth, instant wash;
low values let distinct early echoes through, a grainier, more "digital" start. Values above 100 % diffuse more than the
type's stock setting.`,

    inputDiffusionTime: `Scales the length of the input diffusers. Longer times smear the attack more, softening it; shorter
times keep it tighter.`,

    micSpacing: `Delays the right side of the reverb by up to 20 ms, like moving two room microphones apart. A few
milliseconds widens the reverb; much more can sound like a separate echo, and may sound hollow in mono.`,

    springs: `Spring only: how many springs are in the tank, 1 to 6. One spring is the thin, drippy sound of a small amp;
two or three a classic amp tank; more springs sound denser and smoother. The springs alternate between the left and right
outputs (one spring feeds both).`,

    springTone: `Spring only: moves the springs' natural roll-off (about 4.1 kHz) up to an octave darker or brighter. Turn
it down for a warmer, duller tank; up for more drip and splash.`,

    springDrive: `Spring only: saturates what goes into the springs, like a hot spring driver. Adds grit and compression to
the reverb on loud notes. 0 is clean.`,

    boing: `Spring only: makes the springs react to your pick attack, the way a real tank "boings" when it's hit hard. It
detects transients and drives the springs up to five times harder while they last, so picked notes splash and sustained
ones don't. Great for surf.`,

    modDepth: `How much the reverb's internal delays are modulated, which adds a slight chorus to the tail and stops it
from ringing at fixed pitches. 100 % is the stock amount; up to 300 % for a lush, swirling tail; 0 for a perfectly still
one. On Room, Hall, Plate, Chamber, Cathedral, Classic Hall and Classic Plate.`,

    modRate: `The speed of the modulation. Slow rates (under 1 Hz) give a subtle, natural movement; faster ones a more
obvious chorus.`,

    modPhase: `The phase between the modulated lines. 180° (the default) moves them in opposite directions, which keeps
the pitch movement subtle and spreads it across the stereo field.`,

    pitchMix: `How much of the pitch-shifted sound you hear, blended in at the **Pitch Position**. 0 % with Pitch Feedback
up and the position on Tank Feedback still gives a shimmer, because the shifted sound feeds the tail without being heard
directly.`,

    shift1: `The first pitch voice's shift, in semitones. +12 (an octave up) is the classic shimmer.`,

    shift2: `The second pitch voice's shift, in semitones. +7 (a fifth) or +19 (an octave and a fifth) with an octave on
voice 1 gives a rich, organ-like shimmer; −12 adds an octave below.`,

    voiceBalance: `The blend between the two pitch voices. 0 % is voice 1 only, 100 % voice 2 only, 50 % an equal mix.`,

    splice: `The grain length of the pitch shifters. Longer grains (80 to 200 ms) sound smoother and suit shimmer; shorter
ones respond faster but sound buzzier.`,

    pitchFeedback: `Feeds the pitched sound back so it climbs again. On **Tank Feedback** it sends the pitched tail back
into the reverb's input, so each pass is shifted again and the shimmer keeps rising. On Input or Output it feeds the
shifters back into themselves (kept below unity), which stacks octaves on the shifted sound.`,

    pitchDirection: `**Forward** shifts normally. **Reverse** plays each grain backwards, for a reversed, swelling shimmer.`,

    pitchPosition: `Where the pitch section sits. See the options below.`,

    globalMix: `When on, the global Effects Mix offset is added to this block's Mix, so one global control makes every such
block wetter or drier for the room you're playing in.`,

    killDry: `Mutes the block's dry signal, so Mix sets only the reverb level. For parallel paths that already carry the
dry.`
  },

  options: {
    type: {
      Room: `A small, tight space with short reflections. Adds air and depth to a rhythm tone without an obvious tail.
Keep the decay under about 1 s.`,
      Hall: `A large, smooth concert hall with a gradual build. The all-rounder for leads, clean parts and ambient
playing. The default.`,
      Plate: `A studio plate: dense and bright, with almost no sense of a room and a fast build. Sits well on leads and
clean arpeggios and stays clear in a mix.`,
      'Dattorro Plate': `Jon Dattorro's published plate algorithm, with its own built-in modulation. Smooth, lush and
slightly chorused; a different flavour of plate. The modulation, decay-band and diffusion controls aren't used.`,
      Spring: `A tank of one to six springs, with the characteristic drip, chirp and boing of a guitar amp's reverb. Surf,
rockabilly, blues and country. Has its own Spring page and no early reflections; High Cut damps the springs' tail.`,
      Chamber: `An echo chamber: between a room and a hall, warm and dense. A natural, slightly vintage studio sound.`,
      Cathedral: `A huge, stone space with long reflections and a slow build. Use with long decays for ambient and
worship-style swells.`,
      'Classic Hall': `The lush, modulated hall of the classic late-70s and 80s studio units: a ring of four sections with
slow sine and random modulation. Rich, wide and smooth, with gentle movement in the tail.`,
      'Classic Plate': `The same classic ring with shorter sections and denser diffusion: a bright, fast-building 80s
studio plate.`,
      'Vintage Digital': `A first-generation digital reverb: parallel combs and allpasses behind a 10 kHz bandwidth.
Sparser and grainier, with a metallic edge at small sizes. Good for lo-fi and retro sounds.`,
      'Classic Room': `A small room or ambience built from nested allpasses. Short, dense and uncoloured; adds space to a
dry tone without a noticeable tail.`
    },
    quality: {
      Economy: `4 lines: lightest on CPU, but sparser and more prone to metallic ringing on long decays.`,
      Normal: `8 lines: the default.`,
      High: `12 lines: denser and smoother.`,
      'Ultra High': `16 lines: the densest tail, for long ambient decays.`
    },
    hold: {
      Hold: `Freezes the tail with the input closed.`,
      Stack: `Freezes the tail with the input open, so new notes pile on top.`
    },
    pitchPosition: {
      Input: `Shifts what goes into the reverb. The shifted notes are then reverberated, so they bloom in like the rest
of the tail.`,
      Output: `Shifts the reverb's tail (the default). Pitch Mix blends the shifted tail with the plain one.`,
      'Tank Feedback': `Feeds the shifted tail back into the reverb's input, so every pass is shifted again and the sound
climbs: the classic shimmer. Pitch Feedback sets how much goes back.`
    }
  },

  tips: `- **Behind the guitar, not on top of it.** A Low Cut of 120 to 200 Hz and a High Cut of 4 to 6 kHz keep the reverb
out of the bass and away from the pick attack. Add 20 to 40 ms of pre-delay and you can run more Mix before it gets
muddy.
- **Leads.** Use the ducker: Atten 6 to 10 dB, threshold just under your picking level, release 300 to 600 ms. Fast
lines stay clear and held notes bloom.
- **High gain.** Keep the decay short (under 1.5 s), the Low Cut high (200 Hz or more) and the Mix low. Long tails on a
heavy rhythm tone turn to mush quickly.
- **Shimmer.** Hall, Classic Hall or Cathedral, Decay 6 to 10 s, Pitch Position Tank Feedback, Shift 1 +12, Pitch
Feedback 30 to 50 %, Pitch Mix 0 to 20 %, Splice 100 ms or more, Low Cut 200 Hz, High Cut 5 to 7 kHz, Mix 30 to 40 %.
Raise Pitch Feedback slowly: past about 60 % the shimmer can dominate. For a related block built for this, see
[Plex Delay](/manual/blocks/plex) (Shimmer Verb).
- **Freeze pad.** Put **Stack/Hold** on a footswitch: strum a chord, freeze it, and play over the pad. Pair it with a
[Volume/Pan](/manual/blocks/volpan) block or a swell to fade it out.
- **Surf spring.** Spring, 3 springs, Decay 2 to 3 s, Boing 50 to 80 %, Spring Tone above 50 %, Mix 30 to 50 %. After the
cab it splashes cleanly; in front of a lightly driven amp it breaks up with the amp, for a rawer sound.
- **Expert note.** Room, Hall, Plate, Chamber and Cathedral are a feedback delay network with a Householder matrix; each
line's gain is derived from its length and Decay, so the decay time is accurate rather than approximate. Signal flow:
input select and gain, the pitch section (at Input), pre-delay with its feedback and mix, early reflections and Late
Input Mix, the tank, Late Level, the pitch section (at Output or Tank Feedback), Early Level, then the Low Cut, the two
EQ bands, Mic Spacing, the ducker and Spread on the wet.`
};
