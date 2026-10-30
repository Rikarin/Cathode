import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Drive block is your pedalboard of overdrives, distortions, fuzzes and boosts. Pick a **Type** and you get
that pedal's circuit: its gain stage, its clipping diodes or transistors, its tone control and its quirks, simulated
component by component rather than imitated with a generic curve. The first three types (Overdrive, Distortion, Fuzz) are
simpler, lighter voicings; every type after them is a circuit model of a specific pedal or design.

Behind the pedal's own knobs sit three extra pages: a four-band **Tone** EQ, a ten-band **Graphic EQ** that can go before
or after the clipping, and an **Advanced** page that lets you swap diodes, add bias or a clean blend, slow the op-amp
down, or crush the bits. Everything on those pages does nothing at its default, so a fresh Drive sounds like the pedal.`,

  usage: `**Where it goes:** in front of the amp, like a real pedal. A drive after the amp or cab works, but it will not
sound like a pedal into an amp.

**Two classic jobs:**

- **A boost for a high-gain amp.** Choose a Tube Screamer-style type (TS808 OD, TS9 OD, Super OD) or a clean boost, set
  **Drive** low (0 to 2), **Level** high (+6 to +10 dB) and **Tone** to taste. The amp does the distorting; the pedal cuts
  the low end going in (the Screamers' 720 Hz bass corner) and pushes the mids, so palm mutes tighten up and the amp's
  gain sounds focused instead of woolly.
- **The main source of grit into a clean or edge-of-breakup amp.** Turn **Drive** up to where you like the dirt, then
  set **Level** so the block is about as loud on as off (bypass it a few times to compare).

**Starting point:** Drive 5, Tone 5, Level 0 dB, then move Drive first, Tone second and Level last. The Level knob is the
one you trim to match volumes; see [Gain staging](/manual/gain-staging).

**Your guitar matters.** The circuit types work in volts, using the input calibration from
[Audio setup](/manual/audio-setup), so a hot humbucker hits them harder than a vintage single coil, as it would the real
pedal. **Input Vol** plays the part of your guitar's volume knob.`,

  params: {
    type: `Which circuit you are playing through. The first three are light voicings with a choice of clipping curve on
the Advanced page; the rest are component-level models of real pedals or of designs in their style. Changing it does
not reset the other knobs, so you can compare pedals at the same settings. See the list below for what each one is.`,

    drive: `How hard the circuit is pushed. On most pedal types it turns the pedal's own gain pot (an audio-taper pot in
the gain stage's feedback or ground leg), so it behaves like the real knob, including where most of the change happens.
On a few it is a gain in front of the circuit instead: Treble Booster (0 to 20 dB), FET Boost (0 to 20 dB), Hard Fuzz
(0 to 30 dB), Tape Distortion (−6 to +24 dB), FET Preamp (0 to 24 dB) and Master Fuzz (−18 to 0 dB, its Attack pot).
On the Fuzz Faces it moves the Fuzz pot's wiper, and on Bit Crusher it sweeps the word length and sample rate down.

On the three original voicings Drive is a plain gain into the clipper: Overdrive 0 to 40 dB, Distortion 6 to 60 dB,
Fuzz 10 to 50 dB.`,

    tone: `The pedal's tone control, working on the pedal's own tone network. Turning it up is always brighter.

- On the original voicings it is a low-pass that sweeps from 600 Hz (0) to 8 kHz (10).
- On the Screamers, Klon, Zendrive and other active tone stages it is the real tone circuit, so it cuts treble below noon
  and adds it above.
- On a passive treble cut (RAT, Timmy, OCD and many others) 10 is the pot at zero, the brightest the pedal gets.
- On the Big Muff, DS-1 and Ruckus it blends a low-pass and a high-pass: dark at 0, bright at 10, with a mid scoop
  around noon.
- On pedals with a three-band stack (Governor Dist, FET Preamp, Integral Pre) it is the treble pot; on the active EQs
  (M-Zone, Blackglass 7K, the Xotic types, Tube Drive 4-Knob) it is the treble band, cutting below 5 and boosting above.
- On the Treble Booster it sweeps the input capacitor from 100 nF at 0 (full range) to the stock 5 nF at 10 (thin and
  cutting).
- Pedals that have no tone knob of their own (the Fuzz Faces, Tone Bender, Octave Fuzz, OD-One, OD 250, Micro Boost
  and similar) get a simple passive treble cut, the common mod: at 10 it is out of the way and you hear the pedal as
  built.

On Transparent OD this knob is labelled Treble, as on the pedal. Super Fuzz has a two-way switch instead (below).`,

    level: `The block's output level in dB, after everything else. Use it to match the drive's volume to bypass, or to
push the next block (the amp) harder. At −80 dB the block is silent.`,

    mix: `Blends the clean input back in with the driven signal (a constant-power crossfade), for parallel drive. 100 % is
the pedal alone. Around 50 to 70 % keeps the attack and low end of your clean signal under the grit, which works well
for bass and for fuzz that would otherwise get mushy. The clean side passes the same oversampling filters as the driven
side, so the two never comb.`,

    inputVol: `Your guitar's volume knob, modelled: the pickup behind a 250k audio-taper pot. At 10 nothing changes. Rolling
it back lowers the level, and on the pedals whose first stage is a transistor (the Fuzz Faces, Bender Fuzz, Treble
Booster, Master Fuzz, Super Fuzz) it also raises the source impedance the circuit sees, so they clean up much more than the level alone would
suggest, and get a little darker, just like the real pedals. Map it to an expression pedal for that classic
clean-up-with-the-volume-knob trick.`,

    balance: `Left/right balance of the output. Turning it one way turns the other side down; at the centre both sides are
at full level.`,

    lowCut: `A 12 dB/oct high-pass on the driven signal after the pedal (20 Hz is off). Use it to take out low-end boom the
pedal adds, for instance before a high-gain amp. For tightening what the clipping hears, use Bass Response instead.`,

    highCut: `A 12 dB/oct low-pass on the driven signal after the pedal (20 kHz is off). Rolls off fizz the clipping adds.`,

    bass: `Tone page: a low shelf at 100 Hz, ±12 dB, on the driven signal after the pedal's own tone control.`,

    mid: `Tone page: a peaking band at **Mid Freq**, ±12 dB, after the pedal's tone control. Boost for a pushier,
more vocal drive; cut for a scooped one.`,

    midFreq: `Tone page: where the **Mid** band sits, 200 Hz to 5 kHz.`,

    highMid: `Tone page: a peaking band at 2.5 kHz, ±12 dB, after the pedal's tone control. Cut it a little if the
drive is harsh or "barky"; boost it to cut through a band mix.`,

    treble: `Tone page: a high shelf at 5 kHz, ±12 dB, after the pedal's tone control.`,

    bassResponse: `Tone page: moves the input coupling corner, the high-pass that decides how much bass reaches the clipping.
100 % is the pedal as built. Lower (down to 25 %) lets more bass in, for a fatter, looser drive; higher (up to 400 %)
tightens it, for a firmer palm mute in front of a high-gain amp. On the original voicings it scales their pre-filter
(Overdrive 720 Hz, Distortion 60 Hz, Fuzz 30 Hz); on a pedal whose input coupling is part of its first stage (the
Fuzz Faces) it adds a gentle high-pass at 10 Hz times the setting instead.`,

    eq2: `Graphic EQ page: where the ten-band EQ sits. **Pre** shapes what the clipping hears, so cutting bass there
tightens the distortion and boosting mids makes it break up sooner. **Post** shapes the result, after the Tone page.
**Off** removes it. The clean side of a partial Mix passes neither EQ. The chart at the page's right shows this EQ's
curve (flat while it is off).`,

    'geq#': `Graphic EQ page: one band of the ten-band EQ (2/3 octave wide, ±12 dB), centred on the frequency in its name.
Active only when **EQ 2** is Pre or Post. A band at 0 dB does nothing.`,

    clipType: `Advanced page, original voicings only: replaces the voicing's clipping curve with another, while keeping its
filtering and (for Overdrive) its clean blend. **Default** is the voicing's own: Soft for Overdrive, Hard for
Distortion, Asymmetric for Fuzz.`,

    clipShape: `Advanced page, original voicings only: bends the clipping curve. Negative values blend towards a long,
soft knee (the germanium curve); positive values blend towards a hard clip. 0 is the curve itself.`,

    diodeType: `Advanced page, pedals with clipping diodes: replaces every clipping diode in the circuit with another kind.
Lower-voltage diodes (germanium, Schottky) clip earlier and quieter with a softer edge; higher ones (LED, MOSFET) clip
later and louder, with more headroom and a more open sound. **Stock** is the pedal's own.`,

    diodeCount: `Advanced page, pedals with clipping diodes: puts this many diodes in series in each clipping stack (each
direction). More diodes raise the clipping threshold: louder, more dynamic, less compressed. **Stock** keeps the pedal's
own count (which can differ by direction on asymmetric pedals; a direction with none stays without).`,

    dryLevel: `Advanced page: adds the clean input inside the circuit, after the clipping and before the tone control, at
this level. −80 dB is off. Unlike Mix, the clean signal then goes through the pedal's tone network and the EQ pages
along with the dirt, as on a pedal with a built-in clean blend.`,

    bias: `Advanced page: a DC offset ahead of the clipping (up to 0.5 V on the circuit types), which makes the clipping
asymmetric and adds even harmonics: a thicker, more "tube-like" or sputtery edge depending on the circuit. The offset is
removed again after the clipping.`,

    slewRate: `Advanced page: limits how fast the signal may change, in volts per microsecond (10 is off). On pedals with an
op-amp the op-amps themselves slew at this rate, which rounds and softens the top of the distortion (the RAT's LM308 is
slow by nature; this replaces its own 0.3 V/µs). On other types the limit is applied after the clipping. Low settings
dull the treble and add a smeared, ragged edge.`,

    bitReduce: `Advanced page: lo-fi word-length reduction on the driven signal, in bits removed from 24 (0 is off, 20
leaves 4 bits). Adds grainy, gated-sounding distortion on decays.`,

    sampleRate: `Advanced page: lo-fi sample-and-hold at this rate, with no anti-alias filter (48 kHz is off). Low rates add
metallic, inharmonic aliasing.`,

    stackBass: `The pedal's bass control, on the types that have one: the bass pot of a three-band stack (Governor Dist, FET
Preamp, Integral Pre), the low band of an active EQ (M-Zone, Blackglass 7K, Esoteric ACB, Esoteric Bass RCB, Tube Drive
4-Knob), or the Bosom Boost's Bass, where it is a ±15 dB shelf at 100 Hz. 5 is the pedal as built.`,

    stackMid: `The pedal's middle control, on the types that have one: the middle pot of a three-band stack, or the first
mid band of an active EQ (the M-Zone's sweepable mid, the Blackglass 7K's Lo Mids at 500 Hz). 5 is the pedal as built.`,

    oversampling: `Advanced page: how many times the sample rate the clipping runs at. **Global** uses each type's own
choice (4x for most, 2x for the heavier circuits that need an iterative solve). Higher oversampling means less aliasing
(cleaner highs at high gain) and more CPU. Changing it rebuilds the signal path and changes the block's latency, so set
it while you are not playing.`,

    pedalBass: `Transparent OD only: the Timmy's Bass knob. At 0 the stage cuts its lows as the pedal does by default;
turning it up lets the low end through the gain stage, so bass is amplified and clipped as much as the mids, for a
fuller drive.`,

    voice: `Zen OD only: the Zendrive's Voice knob. At 10 you get the pedal as listed; turning it down puts resistance in
the gain stage's ground leg, so there is less gain and the low end reaches lower, for a smoother, darker voice.`,

    peak: `Obsessive OD only: the OCD's HP/LP switch. **LP** is the default voicing; **HP** puts a resistor in parallel with
the clipper's series resistor, so the diodes are hit harder: more gain, more output and a brighter, more open sound.`,

    pedalMidFreq: `M-Zone Distortion only: the Metal Zone's Middle Frequency, where its mid band sits (200 Hz to 5 kHz).
Use it with the Middle Pot: a cut around 500 to 800 Hz scoops the sound; a boost around 1 to 2 kHz makes it cut through.`,

    grunt: `Blackglass 7K only: the Grunt switch, how much bass goes into the distortion. **Fat** lowers the input corner to
a quarter (more bass into the clipping), **Raw** raises it four times (tighter, less low end distorted). **Stock** is
the middle position.`,

    attack: `Blackglass 7K only: the Attack switch, a treble shelf above 1.5 kHz ahead of the distortion. **Boost** adds
about 6 dB (more pick clank and bite), **Cut** takes 6 dB off (smoother), **Flat** leaves it.`,

    hiMid: `Blackglass 7K only: the Hi Mids band of its active EQ, at 1.5 kHz. 5 is flat; up for growl and presence in a
band mix, down for a smoother tone.`,

    blend: `Blackglass 7K only: crossfades the clean path and the distorted one. At 5 both are at full level (the pedal as
built), at 0 you hear only the clean path, at 10 only the distortion. Bass players usually keep plenty of clean in for
the low end.`,

    kingMode: `Tone of Kings only: the King of Tone's internal mode switches. **OD** has the diodes across the first stage's
feedback (smooth, dynamic overdrive). **Distortion** moves them to ground after the stage, for harder clipping and more
gain. **Boost** removes them, so the op-amps clip only on their rails: much louder and cleaner. The Distortion and Boost
component values are estimates.`,

    fullMode: `Full OD only: the Full-Drive 2's three-way switch. **Flat-Mids** is the pedal's default, with fuller bass than
a Tube Screamer. **Comp-Cut** doubles the diodes in series: less compression, more output and dynamics. **Vintage** uses
the Tube Screamer's 720 Hz corner, for the classic mid hump. The switch positions' values are estimated from Fulltone's
descriptions.`,

    boostOn: `Full OD only: the Full-Drive 2's Boost footswitch. On adds a clean boost ahead of the overdrive, set by the
Boost knob, so the same pedal gives you a second, hotter gain setting for solos.`,

    boostLevel: `Full OD only: how much the Boost footswitch adds, 0 to 20 dB (2 dB per step on the knob). Only works while
the Boost Switch is on.`,

    glow: `Eternal Love only: the Eternity's Glow switch. **Off** is the Glass voicing as built. **On** adds a pair of diodes
across the gain stage's feedback, for the smoother, more compressed voice the switch is known for (the Glow network is
an estimate, as no schematic of it was available).`,

    pedalAttack: `Horizon Precision Drive only: its Attack knob, the input coupling corner. 5 is about 160 Hz; turning it up
moves the corner up to four times higher (tighter, more percussive palm mutes), turning it down up to four times lower
(more low end into the gain stage).`,

    gate: `Horizon Precision Drive only: the pedal's built-in noise gate. 0 is off; turning it up raises the threshold from
about −80 dBV to −30 dBV. It listens to the pedal's input and turns the output down below the threshold (a 1:3
downward expander that opens in about a millisecond and closes over about 60 ms). Its curve and times are Cathode's
own, as Horizon Devices do not publish theirs.`,

    tight: `Bosom Boost only: the Tight switch. On raises the input coupling corner to 150 Hz, taking low end out of the boost
for a tighter high-gain amp. The value is an estimate.`,

    dip: `Esoteric Bass RCB only: the internal DIP switches, which move the bass and treble bands. **Bass Up** moves the bass
band from 60 to 120 Hz, **Treble Up** moves the treble band from 2.5 to 5 kHz, or both. These frequencies are estimates,
as Xotic do not publish them.`,

    toneSwitch: `Super Fuzz only: the two-way tone switch, in place of a Tone knob. **Scoop** keeps the pedal's
low-pass/high-pass filter, for its famous mid-scooped fuzz; **Flat** switches the filter out, for a fuller, more
midrange sound. The filter values are estimates.`
  },

  options: {
    type: {
      Overdrive: `A light Tube Screamer-style voicing: the clipped path is high-passed around 720 Hz and summed with a clean
path, for the mid hump and tight lows. Soft clipping, 0 to 40 dB of gain.`,
      Distortion: `A light RAT-style voicing: up to 60 dB of gain into a hard clipper, with a 60 Hz pre-filter.`,
      Fuzz: `A light fuzz voicing: an asymmetric soft clipper (even harmonics, sputtery edge) with up to 50 dB of gain.`,
      'TS808 OD': `Based on the Ibanez TS808 Tube Screamer: a JRC4558 op-amp with silicon diodes in its feedback, the 720 Hz
bass cut, and the active tone stage. Smooth, mid-forward, compressed. The classic boost for a high-gain amp.`,
      'TS9 OD': `Based on the Ibanez TS9: the TS808 circuit with the TS9's output section, which loads the tone stage less,
so a touch brighter and edgier.`,
      'TS Mod OD': `A modded Tube Screamer: asymmetric diodes (one one way, two the other), more bass into the clipper and
more gain. Fuller and more open than a stock TS.`,
      'Super OD': `Based on the Boss SD-1: asymmetric 1 + 2 silicon diodes across the op-amp, TS-type tone stage. Like a
Screamer with a bit more edge and even harmonics.`,
      'Zen OD': `Based on the Hermida Zendrive: a TL072 with doubled silicon diodes in its feedback and a Baxandall treble
control, plus its Voice knob. Smooth, thick, vocal; a favourite for blues lead.`,
      'Chiron OD': `Based on the Klon Centaur: a TL072 on 18 V, germanium diodes to ground and a clean path blended in that
fades as Drive goes up, then an active treble control. Open, full, uncompressed at low gain; famous as a "transparent"
boost.`,
      'Transparent OD': `Based on the Paul Cochrane Timmy: a TL072 with diodes in its feedback, a bass cut ahead and a
passive treble cut (its Tone is labelled Treble), plus its Bass knob. Low-gain, uncoloured, keeps the amp's character.`,
      'Blues OD': `Based on the Marshall Bluesbreaker pedal: a full-range op-amp stage with diodes in its feedback and a
passive treble cut. Low gain, warm, amp-like, with plenty of low end.`,
      'Governor Dist': `Based on the Marshall Guv'nor: op-amp gain into silicon diodes to ground, then a Marshall-style
treble/bass/middle stack (Tone, Bass Pot, Middle Pot). Amp-in-a-box crunch.`,
      'Obsessive OD': `Based on the Fulltone OCD: a hot op-amp stage running into its rails, then two MOSFETs to ground,
one with a germanium diode in series, for a soft, slightly asymmetric clip, plus the HP/LP switch. Amp-like, wide gain
range, from crunch to near distortion.`,
      'Rodent Dist': `Based on the ProCo RAT: a slew-limited LM308 gain stage, a silicon pair to ground and the RAT's
filter (Tone works like its Filter knob, reversed so 10 is brightest). Gritty, aggressive, very versatile.`,
      'Rodent LED': `Based on the Turbo RAT: the RAT gain stage into red LEDs. Louder and more open than the RAT, with less
compression.`,
      'DS1 Distortion': `Based on the Boss DS-1: a transistor boost (a fixed 20 dB here), op-amp gain, silicon diodes to
ground and a low-pass/high-pass tone blend. Bright, buzzy, classic 80s distortion.`,
      'Plus Distortion': `Based on the MXR Distortion+: an LM741 with the gain pot in its ground leg and germanium diodes to
ground. Raspy, fuzzy, early-70s distortion.`,
      'Pi Fuzz': `Based on the Electro-Harmonix Big Muff Pi: a sustain stage, two diode-feedback clipping stages and the
mid-scoop tone network. Huge sustain, wall-of-fuzz.`,
      'Face Fuzz': `Based on the Dallas Arbiter Fuzz Face with AC128 germanium transistors: a direct-coupled transistor pair
with DC feedback and the Fuzz pot in the emitter. Warm and woolly; cleans up beautifully with Input Vol.`,
      'Si Face Fuzz': `A Fuzz Face with BC108 silicon transistors: more gain, harder and brighter edges.`,
      'Bender Fuzz': `Based on the Sola Sound Tone Bender MkII: an AC128 gain stage into a Fuzz Face-style AC128 pair. More
gain and sustain than a Fuzz Face, with a nasal bite.`,
      'Treble Booster': `Based on the Dallas Rangemaster: one germanium transistor with a tiny 5 nF input capacitor, so only
the treble is boosted. Tone sweeps that capacitor (10 is the original). The 60s British lead sound into a cranked amp.`,
      'FET Boost': `A JFET-style clean boost: wide band, with soft, asymmetric compression when pushed, and a passive treble
cut.`,
      'Octave Fuzz': `Based on the Tycobrahe Octavia: a diode-feedback fuzz stage into a full-wave rectifier, which adds an
octave up. Strongest on the neck pickup and above the 12th fret.`,
      'Shred Distortion': `A modern high-gain pedal design: a tight pre-cut at 150 Hz, two diode-feedback gain stages and an
active treble control. Saturated, tight, for leads and riffs.`,
      'Esoteric Boost': `Based on the Xotic RC Booster: a clean TL072 boost that only softens when it hits its rails, with an
active treble control.`,
      'NKT Face Fuzz': `A Fuzz Face with NKT275 germanium transistors: the early, darker and leakier germanium Face.`,
      'Full OD': `Based on the Fulltone Full-Drive 2: a JRC4558 with silicon diodes in its feedback, a lower bass corner
than a Tube Screamer, a passive treble cut, its three-way mode switch and its Boost footswitch.`,
      'OD-One Overdrive': `Based on the Boss OD-1: an op-amp with asymmetric 1 + 2 silicon diodes in its feedback and a fixed
output low-pass. The pedal has no tone knob, so Tone at 10 is the pedal.`,
      'OD 250': `Based on the yellow DOD 250: an LM741 with the gain pot in its ground leg and silicon diodes to ground. Raw,
punchy, a little fuzzy. No tone knob on the pedal; Tone at 10 is the pedal.`,
      'Sonic Drive': `Based on the Ibanez SD9M / Maxon SD-9: a Tube Screamer gain stage driven into its rails, then silicon
diodes to ground and the TS tone stage. More gain and more open than a TS.`,
      'Eternal Love': `Based on the Lovepedal Eternity: a TS-style gain stage with a fuller bass corner into silicon diodes to
ground and a passive treble cut, plus its Glow switch.`,
      'Jam Ray': `Based on the Vemuram Jan Ray: a Timmy-style op-amp stage with diodes in its feedback, full bass and an active
treble control. Sparkly, open, "Fender-in-a-box" overdrive.`,
      'Horizon Precision Drive': `Based on the Horizon Devices Precision Drive: a tight pre-cut (its Attack knob), a high-gain
TL072 into its rails, silicon diodes to ground and a built-in noise gate. Made to tighten a high-gain amp.`,
      'Tone of Kings': `Based on the Analog Man King of Tone: a Bluesbreaker-style stage into a second, low-gain stage, with
its OD, Distortion and Boost modes.`,
      'Hoodoo Drive': `Based on the Voodoo Lab Overdrive: a TS808-derived clipping amp with more bass into the diodes. A
Screamer with a fuller low end.`,
      'Valve Screamer VS9': `A TS9 with a TL072 in place of the JRC4558: a little cleaner and smoother on top.`,
      'M-Zone Distortion': `Based on the Boss MT-2 Metal Zone: a fixed pre-gain, two cascaded gain stages on one Dist pot,
silicon to ground, and a three-band active EQ with a sweepable mid (Bass Pot, Middle Pot, Middle Freq, Tone as the
treble). Very high gain.`,
      'Fat Rat': `Based on the Pro Co Fat Rat: the RAT gain stage with its Fat bass boost on and MOSFET clipping to ground.
Fuller and louder than the RAT.`,
      'Suhr Riot Ge': `Based on the Suhr Riot with its voicing switch on germanium: a TL072 gain stage into germanium diodes
to ground, passive treble cut. The most compressed, smoothest Riot voicing.`,
      'Suhr Riot LED': `The Suhr Riot with red LEDs to ground: the loudest, most open Riot voicing.`,
      'Suhr Riot LED/Si': `The Suhr Riot with an LED one way and a silicon diode the other: asymmetric, between the other
two.`,
      'MCMLXXXI Drive': `Based on the 1981 Inventions DRV: a RAT-family op-amp gain stage on an 18 V supply, silicon to
ground and an active treble control. Big, punchy, more headroom than a RAT.`,
      "Box O' Crunch": `Based on the MI Audio Crunch Box: one hot op-amp stage voiced for the mids, silicon to ground and a
passive treble cut. A Marshall-style crunch in a box.`,
      'Blackglass 7K': `Based on the Darkglass Microtubes B7K bass preamp: two CMOS gain stages that soft-clip on their rails,
a clean/dirty Blend, a four-band active EQ and the Grunt and Attack switches. Made for bass.`,
      'Esoteric ACB': `Based on the Xotic AC Booster: an op-amp stage with silicon diodes in its feedback (soft compression)
and active bass and treble. Low-gain, amp-like crunch.`,
      'Esoteric Bass RCB': `Based on the Xotic Bass RC Booster: a full-range clean boost that only softens on its rails,
with active bass and treble and its internal DIP switches.`,
      'FAS LED-Drive': `Fractal's own LED-clipped overdrive design, as Cathode interprets it (no schematic is published): a
TL072 with red LEDs in its feedback and a passive treble cut. Open and dynamic.`,
      Ruckus: `The Suhr Riot as Fractal first modelled it: the Riot gain stage, silicon to ground, and a low-pass/high-pass
tone blend.`,
      'Shimmer Drive': `Fractal's own boost for amps without a master volume, as Cathode interprets it: a bright TL072 boost
that softens on its rails, with an active treble control.`,
      'Tube Drive 3-Knob': `Based on the Chandler Tube Driver (3 knobs): an op-amp drive stage into a 12AX7 on a low plate
voltage, whose grid and cut-off do the clipping, then a passive treble cut. Warm, tube-like breakup.`,
      'Tube Drive 4-Knob': `Based on the B.K. Butler Tube Driver (4 knobs): the same op-amp and starved 12AX7, with active
bass (Bass Pot) and treble (Tone) controls.`,
      'Master Fuzz': `Based on the Maestro FZ-1A Fuzz-Tone: starved germanium transistors on 3 V, a gain stage into a
direct-coupled pair. Drive is its Attack pot. The buzzy, brassy 60s fuzz.`,
      'Super Fuzz': `Based on the Univox Super-Fuzz: a silicon gain stage, a phase splitter and two diodes rectifying (an
octave up), a clipped output stage, and its Scoop/Flat switch in place of a Tone knob. Thick, octave-laden fuzz.`,
      'Hard Fuzz': `A hard-clipping 60s-style fuzz design: transistor gain slamming its rails, then asymmetric silicon
diodes to ground. Square-wave buzz.`,
      'Tape Distortion': `A reel-to-reel deck driven hot: treble pre-emphasis on record, tape saturation, then the matching
de-emphasis on playback. Soft, warm compression and saturation rather than pedal grit.`,
      'Bit Crusher': `A digital lo-fi design: Drive takes the signal from 16 bits at the full rate down to 3 bits held at
1.5 kHz.`,
      'SDD Preamp': `Based on the input preamp of the Korg SDD-3000 delay: a 4558 on ±15 V with treble emphasis, clipping on
its rails. A bright, cutting front end, famously used as an always-on boost.`,
      'FET Preamp': `A two-JFET preamp design: a square-law first stage into a second that runs out of headroom, then a
passive treble/bass/middle stack. Amp-like, even-harmonic crunch.`,
      'Integral Pre': `A clean preamp and EQ design: a TL072 on 18 V with modest gain and a passive treble/bass/middle stack.
Use it as a clean boost with tone shaping.`,
      'Micro Boost': `Based on the MXR Micro Amp: a CA3130 non-inverting stage with the gain pot in its ground leg, up to about
26 dB of clean boost.`,
      'Mid Boost': `A mid-boost overdrive design: a bass cut at 720 Hz and a treble cut in the feedback around silicon diodes,
with a TS-type tone stage.`,
      'Bosom Boost': `Based on the Friedman Buxom Boost: a clean op-amp boost to about 27 dB on 18 V, with active treble, a
Bass control and the Tight switch.`,
      'FAS Boost': `Fractal's own clean boost, as Cathode interprets it: a flat TL072 boost to about 21 dB on 18 V with a
gentle presence lift.`
    },
    eq2: {
      Off: `The graphic EQ is out of the circuit.`,
      Pre: `Before the drive: it shapes what the clipping hears.`,
      Post: `After the drive and the Tone page: it shapes the result.`
    },
    clipType: {
      Default: `The voicing's own curve (Overdrive: Soft, Distortion: Hard, Fuzz: Asymmetric).`,
      Soft: `A smooth, symmetric tanh curve, like a diode pair: classic overdrive.`,
      Hard: `A flat clip at full scale, like op-amp rails: bright and buzzy.`,
      Asymmetric: `A biased soft curve: adds even harmonics, fuzz-like.`,
      Germanium: `The longest, softest knee: warm and compressed.`,
      LED: `A diode pair with twice the forward voltage: louder and cleaner before it clips.`,
      Tube: `Soft on one half, harder on the other, like a triode's grid side.`,
      'Op-Amp': `Soft rails, between a soft and a hard clip.`,
      FET: `A JFET-like curve, gentler on one half: even harmonics with a soft onset.`,
      Rectifier: `A leaky half-wave rectifier: octave-rich and gnarly.`,
      Variable: `A sine-shaped knee, harder than Soft; the curve Clip Shape changes the most.`,
      'HV Tube': `A high-voltage triode: more headroom on one half.`,
      'LV Tube': `A starved-plate triode: clips hard and early on one half.`,
      Silicon: `A silicon diode pair's exponential knee.`,
      '4558 Diode': `A 4558 op-amp with diodes in its feedback, the Tube Screamer's: soft clipping with a little clean signal
leaking through.`,
      None: `No clipping: the voicing becomes a clean boost with its filtering.`
    },
    diodeType: {
      Stock: `The pedal's own diodes.`,
      Silicon: `Small-signal silicon (1N914 style), about 0.6 V: the common pedal diode.`,
      Germanium: `1N34A style, about 0.3 V with a soft knee: clips early, warm and compressed.`,
      Schottky: `About 0.3 V like germanium, but with a sharper knee.`,
      LED: `A red LED, about 1.7 V: lots of headroom, loud and open.`,
      MOSFET: `A MOSFET wired as a clipper, a very soft junction around 1.5 V: open and smooth.`
    },
    oversampling: {
      Global: `Each type's own factor: 4x for most, 2x for the heaviest circuits.`
    }
  },

  tips: `- **Boosting a high-gain amp:** Drive near 0, Level up, Tone around noon. If the low end is still loose, raise
  Bass Response to 150 to 200 % before reaching for the Low Cut.
- **Stacking drives:** two Drive blocks in series work like stacked pedals. The usual order is the lower-gain pedal
  first, used as a boost into the second.
- **Fuzz clean-up:** the guitar a fuzz sees is modelled inside the block (Input Vol), so the Fuzz Face trick of rolling
  back your volume to clean up works wherever the block sits. Blocks in front of it change the level it gets, not the
  impedance it sees.
- **Too fizzy?** Lower Drive a little and add level instead, use the High Cut around 6 to 8 kHz, or cut the High Mid on
  the Tone page. A [Parametric EQ](/manual/blocks/peq) after the cab is also a good place to tame fizz.
- **Bass guitar:** Blackglass 7K with Blend around 4 to 6, or any type with Mix at 50 to 70 %, keeps the clean low end
  under the grit.
- **For the expert:** every type models its circuit with wave digital filters at 2x or 4x oversampling; the gain pots
  use an audio taper; the clean blends of the Klon and the B7K fall with Drive as on the pedals. Values not available
  from a schematic were estimated, and the descriptions above note the main ones. Where a type is "based on" a pedal, the pedal's
  name belongs to its maker and is used only to say which circuit is modelled.`
};
