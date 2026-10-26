import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Cab block turns an amp's speaker output into what a microphone in front of a guitar cabinet hears.
Without it, an amp block sounds thin and fizzy, because a real guitar speaker and mic cut almost everything above
5 or 6 kHz and shape the mids heavily.

It has four slots (mics), mixed together. Each slot plays one of:

- **DynaCab:** a modelled speaker in a modelled cabinet, with a modelled mic you place anywhere from the dust cap to
  the cone's edge, at any distance up to 12 inches, and at an angle. The physics (cone breakup, the box's resonance,
  an open back's rear wave, the other speakers in the cab, the mic's pattern and proximity effect) is computed from
  data, not recorded, so every position is available and moves smoothly.
- **IR:** an impulse response file you load (a .wav), as from any cab IR library.
- **IR Pack:** a multi-position IR pack (a .cabgrid file or folder), interpolated at the slot's Position and Distance.
- **Flat:** passes the sound through unchanged, for blending a little direct signal in.

Around the mics are a simple room, a mic preamp, a voice-coil compression stage and the usual low and high cuts.`,

  usage: `**Where it goes.** Right after the [Amp](/manual/blocks/circuitamp) (or [Amp NAM](/manual/blocks/nam), if your
capture has no cab in it). Time effects usually go after it.

**Getting started.** The default is one SM57 on a closed 4x12 with Vintage 30s, a quarter of the way from cap to edge,
1 inch away. That is a solid rock starting point. Then:

1. Pick the cabinet and speaker that suit the amp: an open-back 1x12 or 2x12 for Fender and Vox cleans, a closed 4x12
   for Marshall and high-gain amps.
2. Move *Position* to set the brightness: towards the cap is brighter and more aggressive, towards the edge darker and
   rounder.
3. Move *Distance* to set the low end: close is bassier (the mic's proximity effect), further back is leaner and more
   natural.
4. Unmute slot 2 for a second mic. A ribbon (the R-121 is the default) a few inches back adds body under the SM57.

Slot 1 plays by default; slots 2 to 4 start muted, set up with an R-121, an MD 421 and a U 87 at different places, so
you can unmute one to try it.

**Slot levels are relative.** The levels of the slots that play are scaled to add up to unity, so with one slot its
Level does nothing, and with several they set the blend, not the loudness. Use the block's *Level* for loudness. IR
files are normalised when loaded, so different IRs come in at similar levels.

**Linking the amp.** Set the Amp block's *Speaker load* to *Follow Cab* and the amp's power section drives the
electrical load of the speaker and cabinet in this block's first unmuted DynaCab slot.`,

  params: {
    lowcut: `A high-pass on the block's whole output, in Hz; at the bottom of its range (10 Hz) it is off. Use it to take
out rumble and the low thump that muddies a mix: 60 to 100 Hz is common for a guitar that sits with bass and drums.`,

    lowslope: `How steeply the Low Cut falls: 6 dB per octave is gentle, 24 dB per octave a sharp wall.`,

    highcut: `A low-pass on the block's whole output, in Hz; at the top of its range (20 kHz) it is off. Use it to take
off fizz and hiss that the speaker and mic let through: 6 to 8 kHz is a common setting for distorted sounds, 10 kHz or
higher for cleans.`,

    highslope: `How steeply the High Cut falls.`,

    input: `What the slots hear. *Left*, *Right* and *Sum L+R* feed every slot the same mono signal. *Stereo* feeds
slots 1 and 3 from the left input and slots 2 and 4 from the right, each still panned into the stereo output; use it
with two amps, or a stereo signal, into one Cab. With *Stereo*, slot levels are relative within each side.`,

    air: `Mixes back some of the signal from before the cab, above *Air Freq*, like the air between speaker and mic.
Up to -12 dB at 100 %. A little adds openness and sparkle;
too much brings back the fizz the cab removes.`,

    airfreq: `Where *Air* starts: only the dry signal above this frequency is mixed in.`,

    preamp: `A mic preamp after the mics, adding the colour of a recording chain. Each type has its own curve and treble
roll-off; *None* bypasses it. Its small-signal gain is unity, so *Preamp Drive* changes the colour, not the level.`,

    drive: `How hard the mic preamp is driven, in dB. Low settings add a touch of warmth; high settings saturate the
preamp audibly.`,

    saturation: `Biases the mic preamp's curve off-centre, adding even harmonics (a thicker, warmer colour). Needs some
Drive to be heard.`,

    prebass: `A bass shelf at 120 Hz after the mic preamp.`,

    premid: `A broad mid peak at 800 Hz after the mic preamp.`,

    pretreble: `A treble shelf at 4 kHz after the mic preamp.`,

    level: `The block's output level in dB. Since slot levels only set the blend, this is the Cab's loudness.`,

    motor: `Voice-coil heating, before the speaker's response: the louder and longer you play, the hotter the coils get
and the more the output drops, like a speaker pushed hard. At 100 % a full-scale note held long enough settles about
8 dB down. Adds a natural, slow compression and "sag" in the speaker, separate from the amp's.`,

    motortime: `How fast the voice coil heats and cools: short times react within a note, long ones over phrases.`,

    room: `How much of the room the DynaCab mics hear: the floor reflection and the walls and ceiling, each arriving at
its own delay. At 0 the cab is anechoic (no floor either). Applies to DynaCab slots only: IR files and packs carry
their own room.`,

    roomsize: `Scales every wall's and the ceiling's distance: 100 % is a small studio room. Larger moves the reflections
later and further apart.`,

    roomshape: `The room's proportions: positive moves the side wall out and the back wall in, negative the reverse;
0 is the stock room.`,

    floor: `The floor reflection's level relative to stock. It makes a comb filter with the direct sound (the classic
"cab on the floor" colouring); turn it down to simulate a cab on a stand, up for a hard floor.`,

    hfdamp: `How much the room's surfaces absorb treble: 50 % is stock, higher is a duller, softer room, lower a brighter,
harder one.`,

    lfdamp: `A high-pass on the room's reflections, in Hz (0 is none), like bass traps: the room still adds air but no
low-end boom.`,

    diffusion: `How rough the walls are. Smooth walls give clean reflections that comb-filter with the direct sound;
rougher walls scatter them, which evens the comb out from the treble down.`,

    preampMode: `*Economy* runs the mic preamp at the engine's sample rate; *High Quality* runs it four times
oversampled, so heavy drive aliases far less, at more CPU. Changing it rebuilds the preset's processing, so set it once
rather than switching it while playing.`,

    micspacing: `A stereo pair of room mics this far apart. Each DynaCab slot's room is computed twice, as heard from either
side, while the close mic stays centred: the room widens the image as you increase the spacing. Needs some *Room*;
at 0 the room is mono.`,

    'm#source': `What the slot plays: a modelled *DynaCab*, an *IR* file, an *IR Pack*, or *Flat* (the signal unchanged).
Load files for IR and IR Pack in the slot's file picker.`,

    'm#cab': `The cabinet the DynaCab speaker sits in (DynaCab slots only). Closed boxes give a tighter, punchier low end
with a resonance; open backs a looser, airier low end with the rear wave's cancellation and comb.`,

    'm#speaker': `The speaker in the DynaCab slot. Each one is fitted to its maker's published response and data, so it
has its own low end, breakup peaks and treble roll-off.`,

    'm#mic': `The microphone in the DynaCab slot. Each has its own frequency response and polar pattern; the pattern sets
how much proximity effect it has close up (none for the omni) and how it hears the room.`,

    'm#position': `Where the mic points, from the centre of the dust cap (0 %) to the cone's edge (100 %), on DynaCab and IR
Pack slots. At the cap the sound is brightest and most aggressive; moving out it gets darker and smoother, and some
breakup peaks fade as you cross the cone's nodes. The effect is strongest close up: a distant mic hears the whole cone.`,

    'm#distance': `How far the mic is from the grille cloth, in inches, on DynaCab and IR Pack slots. Close up, directional
mics get a strong bass boost (proximity effect) and hear mostly the one cone. Further back the bass evens out, the
other speakers and the room come in, and the sound gets more natural and less focused.`,

    'm#angle': `How far the DynaCab mic is tilted off-axis, in degrees. Off-axis the mic loses some upper treble: a softer,
smoother top without moving the mic.`,

    'm#lowcut': `A high-pass on this slot only, in Hz (10 Hz is off). Useful to keep a close mic's proximity boom out of a
blend while the other mic carries the low end.`,

    'm#lowslope': `How steeply the slot's low cut falls.`,

    'm#highcut': `A low-pass on this slot only, in Hz (20 kHz is off).`,

    'm#highslope': `How steeply the slot's high cut falls.`,

    'm#length': `Cuts the slot's impulse response to this length, with a short fade. Long IRs carry room sound; shortening
them to 20 to 50 ms gives a drier, tighter cab. At the top of the range the whole IR plays.`,

    'm#level': `The slot's level in the blend. Levels are relative: they set the mix between slots, not the block's
loudness, and with one slot playing this does nothing.`,

    'm#pan': `Where the slot sits in the stereo output. The centre is full level on both sides; panning turns the other
side down.`,

    'm#delay': `Delays the slot by up to about 10 ms, as moving the mic back up to 3.4 m. Small delays between two mics
shift where their phase cancellations fall, which is the main tone control of a two-mic blend.`,

    'm#invert': `Flips the slot's polarity. If two mics sound thin together, try inverting one: if it gets fuller, they
were cancelling.`,

    'm#mute': `Silences the slot. A muted slot does not count in the level blend.`,

    'm#solo': `Plays only the soloed slots, for checking each mic on its own.`,

    'm#proximity': `Adds a mic's proximity bass boost to an IR, IR Pack or Flat slot: 100 % is about +3 dB at *Proximity
Freq*, rising toward the low end; 200 % twice that. It does nothing on a DynaCab slot, where the proximity effect is
already modelled from the mic and its Distance.`,

    'm#proxfreq': `Where the proximity boost reaches: higher frequencies make it sound like a closer mic.`,

    'm#smoothing': `Smooths the slot's frequency response by up to an octave, ironing out the narrow peaks and notches of a
real cab. A little takes the harsh edge off an IR; a lot makes it bland.`,

    'm#align': `On DynaCab and IR Pack slots, takes out the time the sound takes to reach the mic, so mics at different
distances line up and blend without phase cancellation. Turn it off to keep the real arrival times (a mic further back
then arrives later, as in a real recording), and use *Delay* to fine-tune. IR files keep the timing that is in the
file.`
  },

  options: {
    input: {
      Left: 'Every slot hears the left input.',
      Right: 'Every slot hears the right input.',
      'Sum L+R': 'Every slot hears the left and right inputs mixed.',
      Stereo: 'Slots 1 and 3 hear the left input, slots 2 and 4 the right.'
    },
    preamp: {
      None: 'No mic preamp.',
      Tube: 'A triode preamp: softer on one side than the other, warm.',
      Bipolar: 'A transistor preamp with a long, soft knee.',
      'FET 1': 'A symmetric soft clip.',
      'FET 2': 'A JFET stage: mostly second harmonic.',
      Transformer: 'A transformer that saturates hard, as a core does.',
      'Tape 70 µs': 'A tape stage with 70 µs equalisation: loud treble compresses first.',
      'Tape 50 µs': 'Tape with 50 µs equalisation.',
      'Tape 35 µs': 'Tape with 35 µs equalisation: the treble compresses least.',
      Vintage: 'A transformer-coupled tube preamp: the lows saturate first, darker top.',
      Modern: 'An op-amp preamp: clean and flat up to a sharp clip.',
      Exciter: 'Only the treble above 3 kHz goes through the curve: adds harmonics to the top, lows stay clean.'
    },
    preampMode: {
      Economy: "At the engine's sample rate.",
      'High Quality': 'Four times oversampled: cleaner when driven hard, more CPU.'
    },
    'm#source': {
      IR: 'An impulse response file (.wav). Stereo files feed left and right separately.',
      DynaCab: 'The modelled speaker, cabinet and mic, placed with Position, Distance and Angle.',
      'IR Pack': 'A multi-position IR pack, interpolated at Position and Distance.',
      Flat: 'No cab at all: the signal as it is, for blending in a little direct sound.'
    },
    'm#cab': {
      '1x12 Open Back': 'A 1x12 combo: open, airy low end.',
      '1x12 Closed Back': 'A closed 1x12: tighter and punchier than the open one.',
      '2x12 Open Back': 'A 2x12 combo (Twin, AC30 style).',
      '2x12 Closed Back': 'A closed 2x12.',
      '4x10 Open Back': 'The 4x10 of a Bassman or Super Reverb.',
      '4x12 Closed Back': 'The classic closed 4x12 for rock and high gain.',
      '1x10 Open Back': 'A small 1x10 combo (Princeton style).'
    },
    'm#speaker': {
      'Celestion Greenback (G12M)': 'Warm, woody mids with a broad 2 kHz hump. Classic British crunch.',
      'Celestion G12H30 Anniversary': 'Heavier magnet: tighter bass and brighter than the Greenback.',
      'Celestion G12T-75': 'Scooped low mids with sizzling 3 and 5 kHz peaks: the 80s 4x12.',
      'Celestion Vintage 30':
        'A strong upper-mid peak near 2.5 kHz: aggressive and focused, the modern rock and metal choice.',
      'Celestion Creamback (G12M-65)': 'A Greenback voicing with a tighter low end and smoother top.',
      'Celestion Alnico Blue': 'Light and chimey with loose bass and a lot of top: the Vox speaker.',
      'Jensen P10R': 'An alnico 10": less low end, bright, early breakup. Tweed and Fender 10s.',
      'Jensen C12N': 'A ceramic 12" with flatter mids and an extended top: the Fender clean voice.',
      'Electro-Voice EVM12L': 'Very flat and full-range: the clean, hi-fi option, heavy low end.',
      'JBL D120F': 'Light cone and heavy magnet: punchy, bright and very clean.'
    },
    'm#mic': {
      'Dynamic 57 (SM57)': 'The standard guitar mic: lean low end, presence peak around 5 to 6 kHz.',
      'Dynamic 421 (MD 421)': 'Fuller and wider than the 57, with a gentle 4 to 5 kHz lift.',
      'Dynamic 906 (e 906)': 'A flat-faced dynamic made for cabs, with a presence lift.',
      'Dynamic 609 (e 609)': 'Similar to the 906, with more upper-mid bite.',
      'Dynamic Beta 57 (Beta 57A)': 'Supercardioid: tighter pattern, more top than the SM57.',
      'Ribbon 121 (R-121)': 'Figure-8 ribbon: warm, smooth top and a big proximity effect. Pairs well with a 57.',
      'Ribbon 4038 (Coles 4038)': 'A darker, vintage ribbon with a soft top.',
      'Ribbon 160 (M 160)': 'A hypercardioid ribbon: focused, with a gentle presence.',
      'Condenser 87 (U 87)': 'A large-diaphragm condenser: detailed and open, best a little further back.',
      'Condenser 414 (C414 XLS)': 'A bright, extended condenser.',
      'Measurement Omni': 'A flat omni: no colour and no proximity effect, the speaker as it is.'
    }
  },

  tips: `**Fizz.** Before reaching for the High Cut, try moving the mic a little off the cap (Position 30 to 50 %), a few
degrees of Angle, or a darker speaker. Then High Cut around 6 to 8 kHz.

**Mud.** Pull the mic back an inch or two (less proximity), raise the Low Cut to 80 to 100 Hz, or on an IR slot lower
*Proximity*. A closed 4x12 has more low end than an open combo.

**Two-mic blends.** A classic pair is a dynamic close on the cap edge plus a ribbon a few inches back. Keep Auto-Align
on for a phase-coherent blend, then use *Delay* (fractions of a millisecond) or *Invert* to tune the comb between
them. Solo each mic to hear what it adds.

**Room.** A little Room (10 to 25 %) with a small Mic Spacing makes a cab sound less like a close-miked recording and
more like an amp in a room, which helps on headphones. Keep it off for a dry, upfront rock tone.

**IRs and Length.** If a third-party IR sounds roomy or smeared, shorten its IR Length. If it is harsh, add a little
Smoothing.

**Cab modelling off.** With the global cab switch off, the Cab block (and the IR Player) pass audio through untouched,
for playing through a real cab.`
};
