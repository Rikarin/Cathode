import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The RTA block lets the real-time analyser look at any point of your signal chain instead of only the output.
Drop it anywhere on the grid: it passes the sound through untouched and sends a copy to the analyser, and while a preset
has one, the RTA view shows what this block sees. A preset can have one RTA block.`,

  usage: `Use it to see what a block actually does: put it after the amp to see the amp's response before the cab, or after
an EQ to check your cuts. Move it along the chain to compare points.

It makes no sound of its own and costs almost nothing, so you can leave it in a preset. Decay, band resolution and window
belong to the analyser itself, not to this block.`,

  params: {
    inputSelect: `What the analyser sees: **L+R** the average of both sides (the usual choice), **Left** or **Right** one
side only, for checking a stereo effect or a split path.`
  },

  options: {
    inputSelect: {
      'L+R': `The average of the left and right sides.`,
      Left: `The left side only.`,
      Right: `The right side only.`
    }
  }
};
