import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The IR Player plays impulse responses that are not a guitar cab: an acoustic guitar body, a room or hall, a
microphone or speaker correction, or an effect captured as an IR. It holds two IR files of up to about
1.4 s each and keeps each file at its own gain, with a dry/wet mix and low and high cuts on the wet sound. It adds no
latency.

For a guitar cab IR, the [Cab](/manual/blocks/cab) block is usually the better choice: it normalises IR levels,
mixes four slots, and adds mic, room and preamp controls.`,

  usage: `**Loading.** Pick a .wav for IR 1 and, if you want, IR 2. A stereo file feeds the left and right outputs
separately. With one IR loaded, every Mode plays that IR alone; with none, the block passes the signal through.

**Typical uses.**

- An acoustic-body IR after a piezo pickup or a clean electric, Mix around 70 to 100 %.
- A room or plate IR as a reverb, Mix 10 to 30 %.
- A second cab IR blended with the Cab block, or two IRs in *Series* (a cab IR followed by a correction IR).

Like the Cab, the IR Player passes audio through untouched when the global cab modelling is off.`,

  params: {
    mix: `The balance between the dry signal and the IRs: 100 % is only the IR sound (as for a cab or body IR), lower
values blend in the dry signal (as for a reverb IR).`,

    lowcut: `A high-pass on the wet (IR) signal, in Hz; at the bottom of its range it is off. The dry signal is not
touched.`,

    highcut: `A low-pass on the wet signal, in Hz; at the top of its range it is off.`,

    lowslope: `How steeply the low cut falls, from a gentle 6 dB to a sharp 24 dB per octave.`,

    highslope: `How steeply the high cut falls.`,

    level: `The block's output level in dB.`,

    balance: `The left/right balance of the output.`,

    input: `What the IRs hear: the stereo input as it is, its sum (L+R), or one side only.`,

    mode: `How the two IRs combine when both are loaded.`,

    'length#': `Cuts the IR to this length, with a short fade. Shorten a long IR to make it drier or to cut its noise
tail; at the top of the range the whole IR plays.`,

    'level#': `The IR's level, in dB. Unlike the Cab block's slots, levels here are absolute: the IR plays at the file's
own gain plus this.`,

    'pan#': `Where the IR sits in the stereo output in *Parallel* mode: the centre is full level on both sides, panning
turns the other side down. In *Series* mode the two IRs' pans multiply; in *Stereo* mode pan is not used.`
  },

  options: {
    input: {
      Stereo: 'Left feeds the left side of the IRs, right the right.',
      'L+R': 'Both sides hear the left and right inputs mixed.',
      Left: 'Both sides hear the left input.',
      Right: 'Both sides hear the right input.'
    },
    mode: {
      Parallel: 'The two IRs are mixed, each with its own level and pan.',
      Stereo: 'IR 1 plays on the left and IR 2 on the right.',
      Series:
        'One IR after the other (their convolution, cut to the maximum length): for example a cab IR followed by a mic or speaker correction.'
    }
  },

  tips: `**Watch the level.** IRs play at the file's own gain, so a reverb or body IR can be much louder or quieter than
the dry signal. Set *IR Level* or *Level* by ear after loading a new file.

**Noisy tails.** If an IR ends in hiss or a long fade you do not need, shorten its *Length*.`
};
