import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Multiplexer (Mux) is a selector switch for parallel paths. Cable several rows of the grid into it and it passes **one** of them on (or all of them), switching with a short 10 ms crossfade so changes never click. Normally, when several cables meet at a block, their signals are simply added together; a Mux instead lets you choose.

It is the tidy way to build presets that change their whole signal path per scene: a clean chain on one row, a high-gain chain on another, and a Mux picking between them. It can also select one of your audio interface's inputs directly, to switch in a second instrument or a return from your computer.`,

  usage: `**Example: two amps, one per scene.**
1. Split the signal after your input and pedals into two rows: Row 1 with a clean amp and cab, Row 2 with a high-gain amp and cab.
2. Cable both rows into a Multiplexer, then on to your delay, reverb and output.
3. Set the Mux's channel A to **Input** Row 1 and channel B to Row 2.
4. In your clean scenes pick channel A on the Mux, in your lead scenes channel B (see [Presets and scenes](/manual/presets-and-scenes)).

Only the selected row is heard, so the amp you aren't using doesn't colour the sound, and the switch fades over 10 ms.

**Why not just bypass blocks?** You could, but a Mux makes the choice in one place, and the paths can be as long and different as you like.`,

  params: {
    input: `Which input to pass on. **Row 1–6** picks the signal cabled in from that grid row (the row the cable comes from); the other rows are muted. **All** sums every cabled row, like a plain junction. **Input 1–4** and **USB 5/6, 7/8** play one of your audio interface's inputs instead of the rows, for a second guitar, a keyboard or a computer return. Changes crossfade over 10 ms.`,
    level: `The block's output volume. Handy for matching the level of the selected path to the others.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`
  },

  options: {
    input: {
      All: 'Every cabled row is summed, as at any junction in the grid.',
      'Input 1':
        "Your audio interface's Input 1 is heard instead of the rows (likewise Inputs 2–4 and the USB pairs). The block still needs a cable from the grid's input to be part of the signal path."
    }
  },

  tips: `- **Selecting a row with no cable** gives silence. Check that the row you picked actually feeds into the Mux.
- **Rows are counted by where the cable comes from:** a cable from a block on row 3 counts as Row 3, wherever the Mux itself sits.
- **Hardware inputs:** to pick an interface input, the Mux must still be connected to the grid's input somewhere upstream, or it isn't part of the signal path at all.
- Up to four Multiplexers per preset. To blend paths rather than pick one, use the [Mixer](/manual/blocks/mixer).`
};
