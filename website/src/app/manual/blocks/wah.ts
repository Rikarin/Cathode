import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `A wah pedal: a resonant peak that sweeps from the bass (heel down) to the treble (toe down) as you rock the
pedal, for the vocal "wah" of funk rhythm and screaming leads. The **Position** knob is the treadle; map it to an
expression pedal to play it.

The first three types are light filter voicings. All the others are circuit models of real wahs: the inductor wahs
(Cry Baby, the Vox family, Colorsound) simulated component by component, transistors and inductor included, and the
inductor-less Morley optical wah and Parapedal as their op-amp circuits. Around the circuit you can reshape the sweep's
range and peak, drive it harder, bias the inductor, and EQ the result.`,

  usage: `**Where it goes:** before the drives and the amp, as on a pedalboard. Wah into a dirty amp is the classic lead
sound. After a drive it sounds more synth-like and aggressive.

**Setting it up:** connect your expression pedal ([MIDI and footswitches](/manual/midi-and-footswitches)) and put it on
**Position** ([Controllers and modifiers](/manual/modifiers)); if you like, map a toe switch to the block's bypass. Rock the pedal and check that heel down and toe down reach the
ends of the sweep; if your pedal does not reach them, fix its calibration rather than the wah.

**Starting point:** Cry Baby, Resonance Q 1x, everything else at default. If the sweep feels bunched at one end of the
travel, try another **Control Taper**. If the heel is too boomy, raise **Low Cut**.`,

  params: {
    type: `The wah circuit or voicing. See the list below.`,

    position: `The pedal's position: 0 % is heel down (dark, bassy), 100 % toe down (bright, piercing). This is the knob to
put on an expression pedal or a [modifier](/manual/modifiers). Parking it somewhere fixed gives the "cocked wah"
sound: a fixed, honky midrange peak for leads.`,

    mix: `Blends the dry signal with the wah. Below 100 % the effect is subtler, as the dry signal fills in the parts of the
spectrum the peak leaves out.`,

    level: `The block's output level in dB. −80 dB is silent.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down.`,

    resonance: `Scales the sharpness of the peak. 1x is the voicing as built; higher is more vocal, quacky and peaky,
lower is smoother and broader. On the inductor circuits it scales the resistor that damps the inductor; on the
Morley and Parapedal their damping.`,

    qTracking: `Makes the peak less sharp towards the toe, by up to half at full toe and 100 %. Some wahs get thinner and
shriller at the toe; this keeps the top of the sweep smoother. 0 % keeps the same peak everywhere.`,

    taper: `How the pedal's travel maps to the sweep. **Linear** spreads it evenly. **Log** spends more of the travel near
the heel and sweeps quickly near the toe; **Reverse Log** the opposite. Pick the one that makes your pedal feel even.`,

    lowCut: `A high-pass on everything the wah hears, wet and dry alike, like a smaller input coupling capacitor (20 Hz is
off). Raise it if heel-down is too boomy or muddy.`,

    minFreq: `Moves the heel-down end of the sweep, as a percentage of the voicing's own (100 %). 50 % takes it an octave
lower, 200 % an octave higher. On the circuits it works through the inductor (or the capacitors on the inductor-less
ones), so the sweep moves like a real modded pedal.`,

    maxFreq: `Moves the toe-down end of the sweep, as Min Frequency does for the heel. Lower it to tame a shrill toe;
raise it for a brighter, more cutting top.`,

    drive: `Pushes the wah harder. On the filter types it adds soft saturation in front of the filter. On the circuits it
raises the input by up to 18 dB (and lowers the output to match), so the circuit's transistors start to clip, for a
gritty, hairy wah.`,

    inductorBias: `A DC current through the inductor lowers its inductance, down to 40 % at 100 %, which raises the whole
sweep by up to about 1.6 times and changes its feel, as on modded wahs with a bias trim. On the filter types and the
inductor-less circuits it gives the same frequency shift. The inductance is modelled as linear, without the core's
saturation.`,

    'eq#': `Graphic EQ page: one band of an eight-band octave EQ after the wah (63 Hz to 8 kHz, ±12 dB), centred on the
frequency in its name. Use it to shape the wah's overall voice, for example a cut at 2 to 4 kHz to tame a shrill toe.
A band at 0 dB does nothing.`
  },

  options: {
    type: {
      Classic: `A light filter voicing in the style of a Cry Baby: about 350 Hz to 2.2 kHz, fairly peaky.`,
      Vox: `A light filter voicing in the style of a Vox: about 450 Hz to 2 kHz, slightly smoother.`,
      Wide: `A light filter voicing with a wide sweep, about 300 Hz to 3 kHz, and the sharpest peak of the three.`,
      Clyde: `Based on the Vox Clyde McCoy: the V846 circuit with the "nasal" Halo inductor and a 100k across it. A higher
sweep and the most vocal peak of the family. Its inductance is an estimate.`,
      Colorsound: `Based on the Sola Sound Colorsound wah: the same two-transistor circuit with values chosen for its wide,
smooth character (estimated, as no traced values were to hand).`,
      Morley: `Based on the Morley optical wah: a lamp and light-dependent resistor tune a one-op-amp band-pass, about 330 Hz
to 3.4 kHz, with a peak that sharpens towards the toe. No inductor, so a cleaner, more even sweep. Values are estimates
fitted to its known range.`,
      V845: `Based on the Vox V845, the budget sibling: the same circuit with cheaper parts, a narrower sweep and a milder
peak. Values are estimates.`,
      Parapedal: `Based on the Tycobrahe Parapedal: no inductor, two op-amps tuned together by a dual-gang pot, about 260 Hz
to 2.2 kHz with the same peak all the way.`,
      'Cry Baby': `Based on the Dunlop GCB-95 Cry Baby, with the component values of the published academic analysis and a
red Fasel inductor. The reference wah sound.`,
      V846: `Based on the Vox V846: a lower-gain transistor pair and a Halo-type inductor, so a narrower sweep and a
smoother, lower peak than the Cry Baby.`
    },
    taper: {
      Linear: `Even sweep along the travel.`,
      Log: `More travel at the heel end, faster at the toe.`,
      'Reverse Log': `More travel at the toe end, faster at the heel.`
    }
  },

  tips: `- The filter follows the pedal smoothly in small steps (every 16 samples), so fast rocking never zips or clicks.
- **Cocked wah:** leave Position fixed around 40 to 60 % for a lead tone with a honky mid peak, a sound many players use
  without ever rocking the pedal.
- **Too shrill at the toe?** Lower Max Frequency to 80 to 90 %, add some Q Tracking, or cut 4 kHz on the Graphic EQ
  page.
- **Auto-wah:** drive Position from an envelope follower or LFO [modifier](/manual/modifiers) instead of a pedal.`
};
