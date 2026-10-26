import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Mixer blends up to six parallel paths into one, with a gain and balance for each. Normally, when several cables meet at a block, their signals are simply added at full level; a Mixer gives each incoming row its own fader and pan, plus a master level, so you can set exactly how the paths sit against each other.

Each input is identified by the grid row its cable comes from. Gain changes glide over at most 20 ms, so switching scenes never clicks.`,

  usage: `**Example: two amps blended.** Split your signal into two rows, a clean amp on Row 1 and a crunchy amp on Row 2, and cable both into a Mixer. Set Row 1 Gain to 100 % and Row 2 Gain to 60 % for mostly clean with some grit. Balance Row 1 a little left and Row 2 a little right for a wide, two-amp stereo image.

**Example: a parallel effect.** Run your dry signal on Row 1 and a heavily effected copy (say, a pitch shifter or a 100 %-wet reverb) on Row 2, then use Row 2 Gain as the effect's level.

**Different blends per scene:** the Mixer has channels like any block, so channel A can be "mostly clean" and channel B "both amps", picked per [scene](/manual/presets-and-scenes).`,

  params: {
    'gain#': `The level of the signal cabled in from this grid row, from silent (0 %) to unchanged (100 %). It can only turn a row down; to make a row louder, raise the level of a block on that row.`,
    'balance#': `The left/right balance of this row: turning it one way turns the other side down. At the centre both sides pass at full level.`,
    mode: `**Stereo** keeps the mixed signal in stereo. **Mono** sums left and right to mono after the rows are mixed.`,
    level: `The volume of the mixed output.`,
    balance: `The left/right balance of the mixed output.`
  },

  options: {
    mode: {
      Stereo: "The mix stays stereo, with each row's balance.",
      Mono: 'The mix is folded to mono: left and right summed and halved.'
    }
  },

  tips: `- **Rows are counted by where the cable comes from:** a cable from a block on row 2 is Row 2's input, wherever the Mixer sits. If the grid has more than six rows, inputs from the rows below the sixth use Row 6's controls.
- **Watch the level.** Adding two full-level paths together is louder than either alone. Pull the row gains back (or the output Level down) to keep your [gain staging](/manual/gain-staging) sensible.
- **Phase:** two paths with different latency or polarity can cancel each other when mixed. Cathode lines up the timing of parallel paths for you, but an effect that shifts phase (a phaser at 100 % wet, say) on one path can still thin out the blend. Trust your ears and adjust the row gains.
- Up to four Mixers per preset. To pick one path rather than blend them, use the [Multiplexer](/manual/blocks/mux).`
};
