import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Input block is where your guitar enters the grid. It picks which input of your audio interface to use,
can simulate how your pickup sounds into different input impedances and cable lengths, and has a noise gate and an input
level. It is always there and cannot be bypassed; at its defaults (Input 1, gate Off, pickup Off, 0 dB) it is a plain
wire.

A preset can have more than one Input block, each taking its own source, for example a second guitar, a microphone or a
signal coming back over USB.`,

  usage: `**The gate here** is the first thing your signal meets, so it hears your guitar before any gain is added. For most
high-gain presets, this gate is all you need: choose **Intelligent** (especially with single coils), then raise
**Threshold** until the noise between notes disappears without cutting your sustain. For more control (hold, hysteresis,
a sidechain), use a separate [Gate/Expander](/manual/blocks/gate) block.

**Input Level** is a trim for the whole preset. Leave it at 0 dB unless a preset needs more or less into its first block;
your interface's gain and input calibration are covered in [Audio setup](/manual/audio-setup) and
[Gain staging](/manual/gain-staging).

**The pickup simulation** (Input page) is for re-amping and for matching vintage gear: real fuzzes, wahs and old amps
load a pickup down, which darkens it. Leave **Pickup** Off unless you want that.`,

  params: {
    source: `Input page: which interface input feeds this Input block. **Input 1** is the default grid input. Choosing
another switches over with a 10 ms crossfade. **USB 5/6** and **USB 7/8** are channels coming back from your computer,
for re-amping a recorded DI.`,

    type: `The noise gate's type. See the options below. **Off** (the default) means no gate.`,

    threshold: `Gate page: the level below which the gate starts turning the signal down. Raise it until the noise between
notes goes away; if it starts eating the ends of notes, back it off. A global gate offset, if your setup uses one, is
added to this value.`,

    ratio: `Gate page: how hard the gate turns down below the threshold. Each dB the input falls below the threshold takes
ratio − 1 dB more off: 2 is a gentle downward expander, 10 or more acts like a hard gate (to at most −100 dB).`,

    attack: `Gate page: how fast the gate opens when you play. Keep it short (1 ms or less) so pick attacks are not
softened.`,

    release: `Gate page: how fast the gate closes once you stop, after a short 15 ms hold. Short is tight and abrupt; long
lets notes die naturally.`,

    mains: `Gate page: your country's mains frequency, used by the Intelligent gate to ignore hum. 50 Hz in Europe and most
of the world, 60 Hz in North America and parts of South America and Asia.`,

    level: `The input level, in dB, after the gate. A trim for everything that follows. −80 dB is silent.`,

    impedance: `Input page: the input impedance your pickup plays into. **Auto** and **1M** are a normal high-impedance
input, the reference your DI was recorded through, so they change nothing. Lower values load the pickup like a vintage
fuzz, wah or old amp input would: the pickup's treble peak is damped and drops, so the sound gets darker and softer. It
only acts when Pickup is not Off.`,

    pickup: `Input page: the kind of pickup you are playing, so the loading can be simulated. **Off** (the default) turns
the pickup simulation off. **Custom** uses the inductance, resistance and capacitance below.`,

    pickupL: `Input page, Custom pickup: the coil's inductance in henries. Single coils are around 2 to 3 H, humbuckers
around 4 to 5 H, P-90s around 7 H. More inductance puts the resonant peak lower.`,

    pickupR: `Input page, Custom pickup: the coil's DC resistance in kΩ, the number pickup makers usually quote.`,

    pickupC: `Input page, Custom pickup: the coil's own winding capacitance in pF.`,

    cable: `Input page: the capacitance of your guitar cable, in pF (roughly 100 pF per metre for a typical guitar cable).
More capacitance moves the pickup's resonant peak down, for a darker sound; it is why long cables dull your tone. Set it
to your real cable so the simulation starts from what you recorded.`,

    buffer: `Input page: simulates a buffer at the guitar (a buffered pedal first or active electronics). The pickup then
sees a high impedance and none of the cable, which keeps its full brightness whatever the cable and the chosen input
impedance.`
  },

  options: {
    source: {
      'Input 1': `The default grid input.`,
      'USB 5/6': `Audio sent back from your computer on USB channels 5 and 6, for re-amping.`,
      'USB 7/8': `Audio sent back from your computer on USB channels 7 and 8.`
    },
    type: {
      Off: `No gate.`,
      Classic: `A downward expander on a peak detector: below the threshold the level falls by Ratio.`,
      Intelligent: `The Classic gate with a detector that ignores mains hum (notches at 50 or 60 Hz and its second and third
harmonics), so a buzzing single coil does not hold the gate open. The best default for most guitars.`,
      'Noise Reducer': `Acts only above about 2 kHz: the hiss fades with the note while the body of the sound is never cut.
Gentle and transparent, for clean and low-gain tones.`
    },
    impedance: {
      Auto: `1 MΩ: the reference, no change.`,
      '1M': `1 MΩ: a normal high-impedance input, no change.`,
      '22k': `A very low load, like a Fuzz Face's input: much darker.`
    },
    pickup: {
      Off: `No pickup simulation: the signal is untouched.`,
      'Single Coil': `A vintage-style single coil (2.5 H, 6 kΩ, 100 pF).`,
      Humbucker: `A PAF-style humbucker (4.5 H, 8.5 kΩ, 150 pF).`,
      P90: `A P-90 (7 H, 8 kΩ, 110 pF).`,
      Custom: `Your own values from the three knobs below.`
    }
  },

  tips: `- The pickup simulation applies the difference between the chosen load and a 1 MΩ input with the same cable,
  because your DI has already been through your real pickup and cable. With Pickup Off, or at Auto or 1M without the
  buffer, the signal passes untouched.
- The gate detector is linked across both channels and opens fast (it never cuts the first note after loading a preset).
- Hum that the gate cannot remove usually comes from the setup itself; see [Troubleshooting](/manual/troubleshooting).`
};
