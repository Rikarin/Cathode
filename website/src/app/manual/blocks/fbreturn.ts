import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Feedback Return is the receiving half of a feedback loop; the other half is the [Feedback Send](/manual/blocks/fbsend). It adds whatever the Send received (one audio buffer ago) to its own input, so a signal from a later column can be fed back into an earlier one. That lets you build loops the grid's left-to-right flow can't otherwise make: delay into reverb and back into the delay, a pitch shifter feeding itself, and so on.

The Return can also have nothing cabled into it: then it outputs only what comes back from the Send. To stop runaway feedback, the returned signal passes a soft limiter at about +18 dBFS.`,

  usage: `**Building a loop:**
1. Place the Feedback Return where the loop should start, usually after the point where your dry playing enters (cable your playing into it, so the Return passes it on and adds the feedback to it).
2. Put the effects you want in the loop after it (a delay and a reverb are the classic pair).
3. Branch off after the last effect into a Feedback Send.
4. Start with a low Send or Return Level and bring it up.

See the [Feedback Send](/manual/blocks/fbsend) page for example loops.`,

  params: {
    level: `How loud the fed-back signal is when it's added to the Return's input. Like the Send's Level, it sets the amount of feedback: turn it down to shorten the build-up, up for more.`,
    outputLevel: `The volume of everything leaving the Return: your input plus the fed-back signal.`,
    balance: `Turns the opposite side down to shift the Return's output left or right.`
  },

  tips: `- **Silence from the Return?** Check that the preset has a Feedback Send and that it isn't bypassed. The Return only hears the Send with its own number (Send 1 feeds Return 1, Send 2 feeds Return 2).
- **Getting too loud?** The soft limiter keeps the loop from exploding, but at +18 dBFS it is already far louder than your playing. Keep the loop gain below unity (Send Level plus Return Level plus the effects' gain below 0 dB overall) for decaying repeats.
- A feedback loop adds one audio buffer of delay per pass, which you won't hear as such; it just becomes part of the loop's timing.`
};
