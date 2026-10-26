import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `Amp NAM plays a neural capture of a real amp (or pedal, or whole rig): a Neural Amp Modeler file (.nam) or an
AIDA-X model (.aidax or .json). A capture is a snapshot of one amp at one setting: it reproduces that sound very
closely, but its knobs are fixed where they were when it was made. This block adds an input trim, loudness
normalisation and a four-band EQ after the capture, so you can fit it into your rig.

If you want an amp whose Gain, tone stack, tubes and power section you can change, use the circuit
[Amp](/manual/blocks/circuitamp) block instead.`,

  usage: `**Loading a capture.** Pick a file in the block's model slot. NAM files and AIDA-X models from the usual capture
sites work. Without a model the block only runs its EQ.

**Where it goes.** Like an amp: after your drives, before a [Cab](/manual/blocks/cab) block. Check whether the capture
includes a cab: a "direct", "DI" or "amp only" capture needs a Cab after it; a "full rig" or "with cab" capture already
has one, so leave the Cab out.

**Levels.** Leave *Normalize* on so captures come in at a similar loudness, then match your guitar's level into the
capture with *Input* (see the tips). Set the final loudness with *Output*.`,

  params: {
    input: `Gain into the capture, in dB. A capture was made at one input level, and it only reacts like the real amp
when your guitar arrives at a similar level: too low and it sounds cleaner than it should, too high and it distorts
more. Use this to match. It is not a gain knob in the amp's sense: the capture's tone stays the capture's.`,

    output: `The block's output level in dB, after the EQ.`,

    normalize: `Brings the capture to a common loudness (-18 dB, as the NAM plugin does) using the loudness stored in
the file, so switching captures does not jump in level. Files without a stored loudness are left as they are. Turn it
off to hear the capture's raw output level.`,

    bass: `A bass shelf at 120 Hz after the capture, ±12 dB.`,

    middle: `A broad mid peak at 650 Hz after the capture, ±12 dB.`,

    treble: `A treble shelf at 2.5 kHz after the capture, ±12 dB.`,

    presence: `A peak at 5 kHz after the capture, ±12 dB: upper bite without touching the treble below it.`
  },

  tips: `**The EQ is after the capture.** The four tone knobs are a plain EQ on the capture's output: they do not change
how the capture distorts, as an amp's tone stack would. For that, put an EQ block before the capture.

**Matching the input.** Many NAM files state the input level they were made at. If yours does not, start at 0 dB and
adjust *Input* until a hard strum gives the amount of break-up you expect from that amp at that setting.

**CPU.** Large NAM models are heavy. If you hear dropouts with two captures in a preset, try smaller ("lite" or
"feather") versions of the files. See [troubleshooting](/manual/troubleshooting).`
};
