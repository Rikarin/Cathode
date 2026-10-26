import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Rotary models a rotating speaker cabinet in the style of the Leslie 122 and 147: the swirling sound of organ records and of guitar on songs from the late 60s on. Inside such a cabinet a treble horn and a bass drum spin in opposite directions. As each one turns towards and away from the microphones, you hear the pitch rise and fall (the Doppler effect) and the volume and tone pulse.

Cathode builds that physically: the signal is split by a crossover into horn and drum, each rotor is heard by two microphones some angle apart, and the speed switch makes each rotor speed up and slow down with its own inertia, like the real motors and belts. That spin-up and spin-down between Slow and Fast is a big part of the sound.`,

  usage: `**Where it goes:** a real Leslie is a speaker, so the most authentic spot is after the amp, in place of (or after) the cab. In front of the amp it also sounds great, and many players like it there for a grittier swirl. Put it before reverb.

**A starting point:** the defaults are a good Leslie: Speed Slow, Horn Slow 0.83 Hz, Horn Fast 6.7 Hz, both Dopplers 50 %, AM Depth 50 %, Mix 100 %.

**Playing it:** switch **Speed** between Slow and Fast during the song, and listen to the rotors ramp: the horn gets there in under a second, the heavier drum takes several. Assign Speed to a footswitch or a scene, or drive it from a [modifier](/manual/modifiers), so you can do this hands-free. **Brake** spins both rotors down to a stop.`,

  params: {
    speed: `The rotor speed switch. **Slow** (the "chorale" setting) is a gentle swirl, **Fast** ("tremolo") a fast warble, and **Brake** stops both rotors. Changes are not instant: each rotor ramps to the new speed with its own acceleration and deceleration times.`,
    hornSlow: `The horn's speed on Slow, in rotations per second.`,
    hornFast: `The horn's speed on Fast. Around 6–7 Hz is typical of a real cabinet.`,
    drumSlow: `The bass drum's speed on Slow. Slightly different from the horn's, so the two never move in lockstep.`,
    drumFast: `The bass drum's speed on Fast. Usually a little slower than the horn's.`,
    hornAccel: `How long the horn takes to speed up to a new, faster speed (to get within 5 % of it). The real horn is light, so it is quick: under a second.`,
    hornDecel: `How long the horn takes to slow down to a new, slower speed.`,
    drumAccel: `How long the drum takes to speed up. The drum is heavy, so it takes several seconds; that slow catch-up is the signature Leslie "ramp".`,
    drumDecel: `How long the drum takes to slow down.`,
    crossover: `Where the signal splits between drum (below) and horn (above), with a steep 24 dB/octave crossover. The real cabinets split at around 800 Hz. Lower it to give more of the sound to the horn's bright swirl; raise it for a darker, drum-heavy sound.`,
    hornDepth: `How much pitch wobble (Doppler) the horn adds as it turns. It scales the change in path length to the mic, up to half a millisecond at 100 %. Turn it down for a smoother, more chorus-like horn.`,
    drumDepth: `How much Doppler pitch wobble the bass drum adds, up to 0.6 ms of path change at 100 %.`,
    amDepth: `How much the volume pulses as each rotor faces towards and away from the mic. At 100 % the horn dips by up to 90 % facing away; the drum, whose baffle is far less directional, by up to 50 %. This is the throb you hear most on Fast.`,
    spread: `The angle between the two horn microphones. At 0° both mics hear the same thing and the horn is mono; at 90–180° each mic hears the horn at a different point of its turn, for a wide, swirling stereo image.`,
    hornLevel: `The horn's volume in the mix. Raise it for a brighter, more animated sound.`,
    drumLevel: `The bass drum's volume in the mix. Raise it for more low-end throb.`,
    drive: `Saturates the signal before the crossover, like the tube amplifier inside a real Leslie being pushed. A little adds warmth and growl; a lot gives the overdriven organ sound.`,
    mix: `The balance between dry and rotary sound, at constant power. A real rotary speaker is 100 % wet; lower values blend in the unmoving dry signal for a subtler effect.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`,
    tempo: `Locks the horn's speed to the preset's tempo: one rotation per note value you pick. The drum keeps the speed ratio it has to the horn at the selected speed. When set, it replaces both the Slow and the Fast horn speed, so switching between Slow and Fast no longer changes the horn (only the drum's ratio); Brake still stops both rotors.`,
    rotorLength: `Scales the horn's radius. A longer horn travels further on each turn, so it gives more Doppler pitch wobble; a shorter one gives less.`,
    drumSpread: `The angle between the two bass-drum microphones. 0° makes the drum mono; wider angles spread it.`,
    micDistance: `How far the mics are from the rotors. At 100 % they are far away and the movement is smooth and even. Closer, the geometry of a near mic takes over: the pass right in front of the mic is louder and the pitch sweep turns lopsided, a more aggressive, close-miked sound.`,
    stereoSpread: `Widens or narrows the rotary's stereo image. 100 % leaves it as the mics hear it, 0 % folds it to mono, negative values swap the sides.`,
    inputSelect: `Which input the cabinet hears: the sum of left and right, or only one side. A real rotary speaker is mono, so the block always starts from a mono signal.`,
    lowRateMult: `Multiplies every drum speed (Slow, Fast and the tempo-locked one) against the horn. At 1× the drum speeds are as you set them; other values make the drum turn proportionally faster or slower without retuning each knob.`
  },

  options: {
    speed: {
      Slow: 'Chorale: a gentle, slow swirl.',
      Fast: 'Tremolo: a fast, intense warble.',
      Brake: 'Both rotors slow down to a stop, with their deceleration times.'
    },
    inputSelect: {
      'L+R': 'The mono sum of both sides.',
      Left: 'Only the left input.',
      Right: 'Only the right input.'
    }
  },

  tips: `- **The ramp is the magic.** Most of the Leslie sound people love is the transition between Slow and Fast. Switch speed on a footswitch at the right moment, for example going into a solo.
- **Too seasick on Fast?** Lower Horn Doppler before AM Depth; the throb from AM Depth is what sells the effect.
- **Brake** is a lovely effect on its own: the swirl winds down and the rotors stop wherever they are, so the sound freezes slightly off-axis.
- Before the amp, a rotary with some **Drive** into a crunchy amp gives the classic 60s blues-rock tone. After the cab, it is closer to a miked Leslie in the studio.`
};
