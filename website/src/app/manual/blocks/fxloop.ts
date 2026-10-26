import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The FX Loop block sends your signal out of your audio interface to external gear and brings it back in, right at that point of the chain. Use it to put a favourite pedal in the middle of a Cathode preset, or to use a real amplifier's preamp as part of the chain (the "four-cable method").

The send and return use your interface's spare channels, which you choose in [Audio setup](/manual/audio-setup) (FX loop send and FX loop return). The trip out and back takes time, so the block delays its own dry signal by **Loop Latency** to keep it in step with what comes back, and Cathode lines up any parallel paths around it by the same amount.`,

  usage: `**Putting a pedal in the chain:**
1. In [Audio setup](/manual/audio-setup), pick an FX loop send (an interface output) and an FX loop return (an interface input).
2. Cable that output to your pedal's input, and the pedal's output to that input.
3. Place the FX Loop block where the pedal should sit, for example after the wah and before the amp.
4. Measure the round-trip latency with **Measure latency…** in Audio setup (with a cable from the send straight to the return) and enter it as **Loop Latency**.

**The four-cable method (a real amp's preamp in the chain):**
1. Guitar into your interface's Input 1.
2. FX loop send → the amp's front input. The amp's own effects-loop send → your FX loop return.
3. Your main output → the amp's effects-loop return (its power amp).
4. In the preset: Input → pedals and drives you want before the amp → **FX Loop** → modulation, delay, reverb → Output.

Now your Cathode drives sit in front of the real preamp, and Cathode's time-based effects sit between the preamp and the power amp, where they sound cleanest. With this setup, leave Cathode's amp and cab blocks out of that path: the real amp does that job.`,

  params: {
    send: `The level going out of the send channels. Match it to what your pedal or amp expects: too low and the return is noisy, too high and you may clip the pedal's input or overdrive the amp more than you intended.`,
    return: `The level of the signal coming back in. Use it to make the loop come back at the same volume it went out, so engaging the block doesn't change your overall level.`,
    mix: `The balance between the block's own dry signal (delayed by Loop Latency) and what comes back from the loop, as a straight crossfade. 100 % is only the return, which is what you want for an amp or a series pedal. Lower values blend dry and return, for running a pedal "in parallel" (for example a fuzz blended with your clean tone).`,
    latency: `The round-trip delay of your interface's send and return, in milliseconds. The block delays its dry signal by this much, and Cathode delays parallel paths to match, so everything stays in time and doesn't comb-filter. Measure it with the loopback test in [Audio setup](/manual/audio-setup). A change takes effect when the preset next loads.`,
    level: `The block's output volume.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`
  },

  tips: `- **No sound with the block on?** If no return channels are set up in Audio setup, the return is silent, and at Mix 100 % so is the block. Check the channels and the cables.
- **Thin or phasey sound at Mix below 100 %?** The Loop Latency doesn't match the real round trip. Re-measure it and reload the preset.
- **Two FX Loop blocks share the same channels.** A preset can hold two, but both send into the same send channels and hear the same return, so you can't use them for two separate pedals at once.
- **Mind the levels:** interface outputs are line level, much hotter than a guitar. Turn **Send Level** down when feeding a pedal or an amp's front input, or use a reamp box.`
};
