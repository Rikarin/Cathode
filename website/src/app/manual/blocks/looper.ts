import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Looper records what you play and plays it back in a loop, so you can layer parts on top of each other: a rhythm part, then a bass line, then a lead over both. It records in stereo, up to five minutes, and the memory is only used as you record.

Cathode always has a looper at the very end of the rig. Placing this block in a preset moves the looper to wherever you put it in the grid, and gives you its settings below. A common reason: put it **before** your delays and reverbs, so the loop is recorded dry and the effects (and any changes you make to them) apply to the loop and your live playing alike. While a preset with a Looper block is running, the looper controls (the toolbar's Looper window, MIDI and footswitches) drive this block.`,

  usage: `**The workflow.** Open the Looper window from the toolbar, or assign the looper actions to footswitches (see [MIDI and footswitches](/manual/midi-and-footswitches)). The buttons do this:

- **Record** on an empty looper starts recording. Pressing it again closes the loop, and what happens next is set by **Record 2nd Press**: by default it starts playing straight away. With **Threshold Record** on, the first press only arms the looper; recording starts the moment you play above the threshold. Pressing Record while armed cancels.
- **Record** on a loop that already exists starts an overdub, like **Overdub**.
- **Overdub** layers what you play onto the loop as it plays. Press it again to stop overdubbing and keep playing. If you press it while recording the first loop, the loop closes and the overdub starts at once.
- **Play** closes a recording and plays it, stops an overdub (the loop keeps playing), or restarts a stopped loop from its beginning.
- **Stop** stops playback (with a 2 ms fade, so it never clicks). If you press it while recording, the loop is kept, stopped.
- **Undo** removes the most recent overdub pass, exactly, and stops overdubbing if you were. It only goes back one pass: the original recording stays.
- **Reverse** plays the loop backwards; press again for forwards.
- **Half speed** plays the loop at half speed (an octave lower); press again to return.
- **Clear** erases the loop.

The Looper window also shows the state, the position and length, and can export the loop as a WAV file.

**Changing presets:** if the next preset also has a Looper block, the loop moves across and keeps playing. If it has none, the loop stops and is dropped.`,

  params: {
    maxTime: `The longest loop you can record. When a recording reaches it, the loop closes and starts playing on its own. Only the memory you actually record into is used, so a long setting costs nothing until you use it.`,
    once: `When on, the loop plays through once and stops, instead of repeating: a one-shot sample you can trigger again with Play.`,
    trimStart: `Where playback starts, as a percentage of the recorded loop. Raise it to cut a late start off the loop without re-recording. Overdubs, reverse and Once all stay inside the trimmed window.`,
    trimEnd: `Where playback ends, as a percentage of the recorded loop. Lower it to cut off a loop that ran too long.`,
    threshold: `When on, pressing Record arms the looper instead of recording at once: recording starts the moment your signal rises above **Threshold Level**. This catches the very first note and avoids silence at the start of the loop.`,
    thresholdLevel: `The level your playing has to exceed to start an armed recording. Set it above your noise floor and below your softest first note.`,
    playbackLevel: `The volume of the loop's playback. Turn it down to sit the loop under your live playing.`,
    dryLevel: `The volume of your live signal through the block. Turn it all the way down to hear only the loop (for example to check what you recorded).`,
    overdubLevel: `How much of the existing loop is kept on each overdub pass. At 100 % every layer stays at full level; at 80 % the older layers fade a little on every pass, so the loop slowly evolves and never builds up into mush. At 0 % each overdub replaces what was there, and Undo can no longer bring the old material back.`,
    crossfade: `When a recording closes, the first moments of what you play next are faded into the start of the loop, so the seam continues your performance instead of clicking. This sets how long that fade is. It only applies on the first pass, played forwards at normal speed, with Trim Start at 0.`,
    quantize: `When on, the length of a new loop is rounded to a whole number of beats at the preset's tempo, padding with silence or cutting off the end as needed, so the loop stays in time with a click or a tempo-synced delay.`,
    secondPress: `What pressing Record a second time does while you record the first loop: close it and start **Overdub**bing at once, close it and **Play**, or close it and **Stop**.`,
    speed: `The playback speed: Normal, Half (an octave down, twice as long) or Double (an octave up, half as long). The Half speed button halves whichever speed is set here.`,
    lowCut: `A high-pass filter on the loop's playback only. Raise it to thin out a loop so it doesn't muddy the low end of your live playing.`,
    highCut: `A low-pass filter on the loop's playback only. Lower it to push the loop into the background.`,
    level: `The block's output volume, applied to the loop and your live signal together.`,
    balance: `Turns the opposite side down to shift the block's output left or right.`
  },

  options: {
    secondPress: {
      Overdub: 'The loop closes and you go straight into overdubbing the next layer.',
      Play: 'The loop closes and plays. The default, and what the rig looper always does.',
      Stop: 'The loop closes and waits, stopped, until you press Play.'
    },
    speed: {
      Half: 'Half speed: the loop plays an octave lower and takes twice as long.',
      Double: 'Double speed: the loop plays an octave higher and takes half as long.'
    }
  },

  tips: `- **Before or after the effects?** Before delay and reverb, the loop is recorded dry and gets the effects live, so you can change them later. After them (or at the end of the rig, without this block), the effects are recorded into the loop and won't change.
- **Clean loop points:** use Threshold Record to start exactly on your first note, and close the loop exactly on the downbeat. Loop Quantize helps if you play to a click.
- **Bypass:** with the block bypassed in Thru or Mute mode it stops running, so the loop pauses. In the other bypass modes the loop keeps playing.
- One Looper block per preset.`
};
