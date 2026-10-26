import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Feedback Send is one half of a feedback loop; the other half is the [Feedback Return](/manual/blocks/fbreturn). Signal in the grid normally only flows left to right. A Feedback Send lets you take a signal from a later column and feed it back to an earlier one: whatever enters the Send comes out of the matching Return.

The Send has no output of its own: its signal goes only to the Return. It arrives there one audio buffer later (for example 64 samples, about 1.3 ms at 48 kHz), which is what makes a loop possible without the signal chasing its own tail within one buffer.`,

  usage: `**Example: delay into reverb into delay.** Put a Feedback Return early in a row, followed by a delay and a reverb. After the reverb, branch off into a Feedback Send. Now some of the reverb's output goes back into the delay, so the echoes get more and more washed out with each repeat, an ambient, ever-blooming wash. The Send's **Level** (or the Return's) sets how much is fed back.

**Example: a feedback loop through a pitch shifter.** Return → Pitch (+12) → Send, with the Return also fed by your playing: each pass is shifted up again, a rising shimmer.

A preset can hold two Send/Return pairs. They are numbered as you add them (each takes the lowest free number), and Send 1 feeds Return 1, Send 2 feeds Return 2.`,

  params: {
    level: `How much of the signal is sent back to the Feedback Return. This is your feedback amount: higher values make each pass around the loop louder. Above roughly unity gain around the whole loop the sound keeps building up; the Return softly limits it at about +18 dBFS so it saturates instead of running away, but it will still get loud.`
  },

  tips: `- **Start low.** Begin with the Send Level well down (−12 dB or lower) and bring it up slowly. Loops with gain build up fast.
- **Bypassing the Send** breaks the loop: the Return falls silent instead of repeating old audio, so you can switch the feedback on and off per scene.
- The Send needs a matching [Feedback Return](/manual/blocks/fbreturn) in the preset; without one, its signal goes nowhere.`
};
