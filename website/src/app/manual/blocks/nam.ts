import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `Amp NAM plays a neural capture of a real amp (or pedal, or whole rig): a Neural Amp Modeler file (.nam) or an
AIDA-X model (.aidax or .json). A capture is a snapshot of one amp at one setting: it reproduces that sound very
closely, but its knobs are fixed where they were when it was made. This block adds an input trim, loudness
normalisation and a four-band EQ after or before the capture, so you can fit it into your rig.

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

    output: `The block's output level in dB, after the capture and the EQ.`,

    normalize: `Brings the capture to a common loudness (-18 dB, as the NAM plugin does) using the loudness stored in
the file, so switching captures does not jump in level. Files without a stored loudness are left as they are. Turn it
off to hear the capture's raw output level.`,

    bass: `A bass shelf at 120 Hz, ±12 dB, on the side of the capture *EQ Location* names.`,

    middle: `A broad mid peak at 650 Hz, ±12 dB, on the side of the capture *EQ Location* names.`,

    treble: `A treble shelf at 2.5 kHz, ±12 dB, on the side of the capture *EQ Location* names.`,

    presence: `A peak at 5 kHz, ±12 dB, on the side of the capture *EQ Location* names: upper bite without touching
the treble below it.`,

    calibrate: `Drives the capture as hard as the real amp was driven by the same guitar. Many NAM files record the
level, in dBu, that reached full scale on the interface they were made with; with this on, the block adds the
difference between that and Input 1's calibration (Settings ▸ Audio setup) before the *Input* knob, as the NAM plugin's
Calibrate Input does. A file made at +12 dBu played through an input calibrated to 1 V (-0.8 dBu) gets 12.8 dB less.
Files without the level, and AIDA-X models, are not changed. Turn it off to drive the capture at the level it gets.`,

    size: `Which size of the capture runs. A NAM A2 capture holds a full model and smaller ones in the same file:
*Full* is the most accurate, *Lite* is the smallest and costs about a seventh of Full's CPU with a slightly less exact
sound. Captures with one size (NAM A1 files, LSTM files, AIDA-X models) sound the same on either setting. Changing it
rebuilds the rig, as changing a block does.`,

    location: `Where the four tone knobs sit. *Post* equalises the capture's output: the tone changes, the distortion
does not. *Pre* equalises your guitar before the capture, as a tone stack ahead of an amp's gain stages does: a boost
drives the capture harder in that band and a cut cleans it up, so Bass tightens or loosens the low end and Treble
changes how the top breaks up.`
  },

  tips: `**The EQ is after the capture unless you move it.** With *EQ Location* on Post the four tone knobs are a plain
EQ on the capture's output: they do not change how the capture distorts, as an amp's tone stack would. For that, set
*EQ Location* to Pre, or put an EQ or Tone Stack block before the capture and keep these knobs for the output.

**Matching the input.** Many NAM files record the input level they were made at, and with *Calibrated Input* on the
block matches it for you once Input 1 is calibrated. If yours does not record it, start at 0 dB and adjust *Input* until
a hard strum gives the amount of break-up you expect from that amp at that setting.

**A capture of a preamp.** A capture of a preamp alone, or of an amp taken before its power amp, has no power amp in
it. Follow it with an [Amp](/manual/blocks/circuitamp) block with *Preamp* switched off (on its Preamp page): that
block is then a power amp alone, so its Master, Presence, Depth and Sag work on the capture. Set the level into it with
this block's *Output* or the Amp's *Master*.

**CPU.** Large NAM models are heavy. If you hear dropouts with two captures in a preset, set *Size* to Lite on an A2
capture, or try smaller ("lite" or "feather") versions of the files. See [troubleshooting](/manual/troubleshooting).`
};
