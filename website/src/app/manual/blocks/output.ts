import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `An Output block is where the grid's signal leaves for your interface. **Output 1** is the main output; Outputs 2
to 4 are extra outputs, each played on whichever interface channels your audio settings map it to, for example a
separate feed to the front-of-house desk, a stage amp, or a dry signal for recording.

Each Output is also a small mixer: every grid row cabled into it has its own level and balance, followed by a main level
and balance for the whole output. At the defaults (0 dB, centre) the rows add up at unity, so a plain Output passes
everything through unchanged.`,

  usage: `**Balancing parallel rows:** if your preset runs parallel paths (two amps, or a dry and a wet row) that meet at the
Output, use the **Row Level** knobs to balance them and the **Row Balance** knobs to place each row left or right, for
example amp A left and amp B right. See [The grid](/manual/the-grid).

**Preset volume:** use **Main Level** to match the loudness of your presets, so switching between them does not jump in
volume. The scene level is applied after this. More on that in [Gain staging](/manual/gain-staging).

**Several outputs:** add Output 2 to 4 to send a different mix somewhere else; map their channels in
[Audio setup](/manual/audio-setup).`,

  params: {
    'level#': `The level of the signal arriving from that grid row, in dB, before it is mixed with the other rows. −80 dB
mutes the row. Rows below the sixth use Row 6's settings.`,

    'balance#': `The left/right balance of the signal arriving from that grid row. Turning it one way turns the other side
down; at the centre both sides are at full level. Rows below the sixth use Row 6's settings.`,

    level: `The output's main level, in dB, after the rows are mixed. −80 dB is silent. Use it to match preset volumes.`,

    balance: `The output's main left/right balance, after the rows are mixed.`
  },

  tips: `- Row gains glide (full scale in 20 ms), so changing them with scenes or controllers never clicks.
- If a mono preset seems louder or quieter than a stereo one, check the Row Balance settings first: a row balanced to one
  side is turned down on the other.
- Each Output type can appear once per preset.`
};
