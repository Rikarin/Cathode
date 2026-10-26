import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Chorus thickens your sound by adding copies of it that are delayed by a few milliseconds and gently pitch-wobbled. Mixed with the dry signal, those copies make one guitar sound like several playing together: shimmering, wide and a little seasick if you push it.

Under the hood each side (left and right) runs a short delay line whose delay time is swept by an LFO. The block can read that line with up to eight voices at once, each sweeping at a different point of the cycle, and it has a Dimension mode modelled on the classic studio "dimension" units, which gives width with almost no audible wobble.`,

  usage: `**Where it goes:** after the amp and cab is the usual spot, before delay and reverb, so the echoes repeat an already chorused sound. Before the amp it works too, but distortion exaggerates the pitch movement and can make it sound out of tune.

**A starting point:** Rate around 0.5–1 Hz, Depth 30–50 %, Delay 6–10 ms, Mix 50 %. That is a classic stereo chorus. For a subtle thickener on a lead, drop Mix to 25–30 % and Depth to about 20 %.

**Things to try:**
- More **Voices** (3–4) for a lusher, more ensemble-like sound with less obvious wobble.
- **Dimension** Low or Medium on clean rhythm parts: wide and still, no seasickness.
- **Tempo** set to 1/2 or 1/1 so slow sweeps line up with the song.`,

  params: {
    rate: `How fast the sweep moves, in cycles per second. Slow (0.2–1 Hz) is a gentle shimmer; fast (4 Hz and up) turns into a warbly vibrato-like sound. Ignored when **Tempo** is set.`,
    depth: `How far the delay time swings, from nothing up to 5 ms of sweep at 100 %. More depth means more pitch wobble. Keep it modest at fast rates, or let **Auto Depth** do that for you.`,
    delay: `The shortest delay of the sweep: the sweep runs from this time up to this time plus the depth. Short delays (2–5 ms) sound tight and flanger-like; longer ones (10–20 ms) sound more like a separate doubled player.`,
    mix: `The balance between dry and chorused sound, at constant power (both at −3 dB at 50 %), so the loudness stays even across the knob. 50 % gives the deepest chorus; lower values just add a sheen.`,
    spread: `The offset between the left and right sweeps. At 0° both sides move together (a mono-sounding chorus in stereo); at 90° or 180° they move against each other and the sound spreads wide.`,
    level: `The block's output volume, applied to dry and wet alike.`,
    balance: `Turns the opposite side down to shift the block's output left or right. At the centre both sides are at full level.`,
    tempo: `Locks the sweep to the preset's tempo: one LFO cycle per note value you pick (1/4 = one cycle per beat). When set, it replaces **Rate**. Off uses Rate.`,
    lfoType: `The shape of the sweep. Sine and Triangle are the classic smooth sweeps; the others give more unusual, stepped or lopsided motion.`,
    voices: `How many chorus voices each side has. Each voice reads the same delay line at a different point of the LFO cycle (spread evenly around it), and they are summed at equal power. One voice is a classic pedal chorus; more voices sound richer and smoother, like a string section. Ignored in Dimension mode, which always uses one.`,
    lowCut: `A high-pass filter on the chorused (wet) signal only. Raise it to keep the low end dry and solid, which helps a lot on bass-heavy amps.`,
    highCut: `A low-pass filter on the wet signal only. Lower it for a darker, more vintage chorus that sits behind the dry sound.`,
    phaseReverse: `Flips the polarity of the wet signal on the left, right or both sides. Inverting one side gives a hollow, very wide stereo image; inverting the wet against the dry changes which frequencies cancel.`,
    rightRate: `The right side's LFO rate as a multiple of the left's. At 1× both sides run at the same speed; other values let the two sides drift in and out of step for a more animated stereo field.`,
    rightDepth: `The right side's depth as a percentage of the left's. Use it to make one side wobble more than the other.`,
    rightTime: `The right side's base delay as a multiple of the left's. Different delays on each side widen the image even with no modulation.`,
    width: `Moves the right side's base delay towards 1 ms. At 0 % both sides use the same delay; turning it up separates them, which widens the stereo picture.`,
    autoDepth: `Reduces the depth as the rate rises above 1 Hz, so a fast chorus doesn't turn into a wild pitch wobble. Low scales the depth by one over the square root of the rate; High by one over the rate. Off leaves the depth as set.`,
    drive: `Gently saturates the signal going into the delay lines, like an overdriven bucket-brigade chorus. A little adds grit and warmth to the wet sound; the dry stays clean.`,
    lfo2Rate: `The speed of a second, sine-shaped LFO that wobbles the main LFO's rate. Only heard when **LFO2 Depth** is above zero.`,
    lfo2Depth: `How much LFO 2 speeds up and slows down the main sweep. A little makes the chorus less regular and more organic, like an old tape machine.`,
    stereoSpread: `Widens or narrows the wet signal's stereo image. 100 % leaves it as it is, 0 % folds it to mono, negative values swap the sides, and values beyond ±100 % push it wider than the speakers.`,
    dimension: `Switches to a dimension-style chorus: one voice per side, the two sweeps in opposite directions, and each side's wet taking an inverted portion of the other side's (30, 50 or 70 % for Low, Medium, High). The parts of the two wets that don't move cancel, so you get width and depth with very little audible pitch movement. Off is the normal chorus.`,
    globalMix: `When on, this block's Mix follows the global **Effects Mix** offset, so one setting makes every such effect wetter or drier for the room you are playing in.`
  },

  options: {
    lfoType: {
      Square: 'A square with softened edges, so it never clicks. Jumps between two delay times.',
      Random: 'Sample and hold: a new random level each cycle. The LFO Phase offset has no effect on it.',
      Log: 'Rounded tops and sharp troughs.',
      Exponential: 'Sharp peaks and rounded troughs.',
      Trapezoid: 'A triangle clipped at half its height, so it lingers at both ends of the sweep.',
      Astable: 'The charge-and-discharge curve of an analogue oscillator circuit.'
    },
    dimension: {
      Off: 'The normal chorus, with Voices, LFO Phase and the rest.',
      Low: "Each side takes 30 % of the other side's wet, inverted. The subtlest setting.",
      Medium: '50 % cross-mix. Wider and more obvious.',
      High: '70 % cross-mix. The widest and most hollow.'
    },
    autoDepth: {
      Low: 'Depth falls gently above 1 Hz.',
      High: 'Depth falls strongly above 1 Hz, keeping the pitch wobble about constant as you speed up.'
    }
  },

  tips: `- **Too seasick?** Lower Depth before you lower Mix. Pitch wobble comes from depth times rate, so a fast chorus wants a small depth.
- **Mono compatibility:** with LFO Phase at 180° or Phase Reverse on one side, summing to mono can cancel some of the effect. If your sound will be heard in mono (a single PA speaker), check it.
- **Doubling a part:** Delay 15–20 ms, a very slow rate (0.1–0.2 Hz), small depth and Mix around 40 % sounds more like a second take than an effect.
- Want a pitch-shifted double instead? The [Pitch block](/manual/blocks/pitch)'s Detune type gives a steadier, wider doubling.`
};
