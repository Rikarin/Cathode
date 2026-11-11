import type { BlockDoc } from '../types';

export const doc: BlockDoc = {
  summary: `The Amp block is a complete tube amp, simulated from its schematic. Every model is built from its real parts:
the preamp's triode stages and cathode followers, the passive tone stack with its exact pots and caps, the phase
inverter, the power tubes pushing into an output transformer and a speaker load, the global negative feedback, and a
power supply that sags as you dig in. Nothing is a recording of the amp: when you turn a knob, the matching component
in the circuit changes, so the amp reacts to your picking, your guitar's volume knob and the other knobs the way the
real one does.

The block has no cabinet. Its output is what the amp sends to its speaker, so follow it with a
[Cab](/manual/blocks/cab) block (or an [IR Player](/manual/blocks/irplayer) or a real cab through a power amp).

The front-panel knobs (Gain, Bass, Middle, Treble, Presence, Master and so on) are the real amp's. Each model shows
only the controls the original has, under the original's names: a Tweed Deluxe shows *Instr Vol* and *Tone*, a
Mark IIC+ shows *Lead Drive*, *Lead Master* and its five EQ sliders. The other pages hold the expert controls: the
tubes, bias, sag, transformer and speaker load, plus extras around the circuit (a boost pedal, EQs, a compressor).
Every expert control starts at the model's stock value, so you can ignore them all and still get the amp as built.`,

  usage: `**Where it goes.** After your drives, wah and compressor, before the Cab block. Modulation, delay and reverb
usually go after the Cab, as they would in an effects loop or a studio. The amp is mono: it takes one input (see
*Input Select*) and sends the same signal to both outputs.

**Getting a sound fast.**

1. Pick a model from *Type*. Every knob goes back to that model's defaults.
2. Set the tone knobs to noon and Master where the model starts it. On amps with a master volume, Master sets how
   hard the power tubes are pushed; on amps without one (a Plexi, a Tweed, a Twin) the power section is always fully
   driven and the volume knobs do everything.
3. Set Gain for the amount of dirt you want, then shape the tone with Bass, Middle and Treble.
4. Set the loudness with *Level*. Level is a clean digital volume after the amp and changes nothing about the tone.

A cranked power amp with no Cab block after it (into a real power amp and cabinet, say) puts out very short spikes
above its usual peak level: the speaker load kicks back when the output tubes cut off, as it does on the real amp.
They are too short to hear, but they can light the output clip warning on a preset that does not sound that loud.
Leave a few dB of headroom in *Level*; a Cab block after the amp removes them.

**Picking a model.** A short guide by family:

- **Fender clean and edge-of-breakup:** Blackface, Deluxe Reverb, Twin Reverb, Super Reverb, Princeton Reverb,
  Champ AA764, Vibroverb, Vibrolux, Bandmaster, Tremolux, the brownface Concert, Super, Deluxe 6G3 and Vibrolux
  6G11. A Twin stays clean longest; a Deluxe or Princeton breaks up early and sweetly.
- **Tweed (raw, compressed, early breakup):** Champ 5F1, Princeton 5F2-A, Tweed Deluxe 5E3, Bassman 5F6-A, Tweed
  Twin. The *Jumped* versions run both channels at once, the classic way to play them.
- **British crunch and classic rock:** JTM45, Plexi, Plexi 100W, 1959SLP, 1987X, 1959T Tremolo, Bluesbreaker 1962,
  18 W 1974X, Major 1967, Super Bass 100, JCM800 2203 and 2204, JCM800 2210 and 2205, Hiwatt DR103 and DR504, OR120
  and OR80.
- **Chime and class A:** AC30, AC15, DC-30, Chieftain, Hot Cat 30, Tiny Terror, Liverpool, Express.
- **Smooth, vocal overdrive:** ODS 97, ODS 70s, Cornford MK50H II, Lone Star, Subway Blues.
- **Hot-rodded British and modern rock:** Silver Jubilee, JCM900 4100 and 4502, DSL100, TSL100, JVM410H, BE-100
  and BE-100 HBE, AFD100, Quick Rod, Laney GH100L, Hot Rod Deluxe, Classic 30.
- **High gain:** SLO-100, 5150, 6505+ (and its rhythm channel, 6505+ Rhythm), 5150III (and its clean and crunch
  channels, 5150III Clean/Crunch), Dual Rectifier and
  Dual Rectifier 3-Ch (each with its other channels: Dual Rectifier Orange, Dual Rectifier 3-Ch Orange and Clean),
  Triple Rectifier (with Triple Rectifier Orange and Clean), Rect-o-Verb, Mark IIC+ (and its clean rhythm channel,
  Mark IIC+ Rhythm), Mark IV (and its clean and crunch channels, Mark IV Rhythm 1 and Rhythm 2), Mark V (and its
  channels 1 and 2, Mark V Clean and Mark V Crunch), TriAxis, Legacy VL100, Powerball, Savage 120 (and Savage 120
  Clean, its clean and first crunch channel), Fireball 60, Prometheus.
- **Loud, clean and heavy (doom, drone, a fuzz in front):** Sunn Model T.
- **Bass:** SVT (and its second channel, SVT Channel 2), SVT-CL, Portaflex B-15N (and both its channels jumpered, Portaflex B-15N Jumped), Bassman 135, Bass 400, and the solid-state GK 800RB.
- **Solid state:** JC-120, GK 800RB, Valvestate 8080 (a tube preamp into a transistor power amp) and RG100ES (JFET
  stages, a tight metal sound). Their tube-only knobs are hidden.

**Two amps in one preset.** You can use the block twice, for example a clean amp and a lead amp on separate rows
switched by scenes, or two amps blended in parallel. See [the grid](/manual/the-grid) and
[presets and scenes](/manual/presets-and-scenes).

**Using a real power amp.** Turn *Power Amp* off and the block stops at the phase inverter's input, so a real tube
power amp gets the preamp alone. Turn *Output Mode* to *SS Power Amp + Cab* when you play through a solid-state power
amp into a real guitar cab.`,

  params: {
    type: `The amp model. Changing it loads that model's circuit and sets every knob (except Level, Balance and Input
Select) back to the model's stock settings, so each model starts as it ships. See the list below and the family guide
above.`,

    gain: `The preamp's gain or volume pot, at its real place in the circuit and with its real taper. Turning it up
sends more signal into the next tube stages, so they clip harder: clean at the bottom, then crunch, then full
distortion on the high-gain models. On many vintage amps this knob is the channel's *Volume*: there is no separate
gain, and the amp gets louder and dirtier together. Where a model's control has another name (*Treble Vol*,
*Instr Vol*, *Bright Vol*, *Loudness 1*, *Pre Gain*, *Drive*), it is still the first volume or gain pot of the channel
you play.

On amps with a bright cap across this pot, the treble is boosted most at low settings and fades as you turn up, just
as on the real amp.`,

    bass: `The tone stack's bass control. It is the real passive network, so it interacts with Middle and Treble: on a
Fender or Marshall stack, turning Bass up also changes how much midrange gets through. On high-gain amps the stack sits
between gain stages, so more bass also means more low end driving the later stages: a looser, fuzzier low end. If the
low end gets flubby, turn Bass down before reaching for an EQ.`,

    middle: `The tone stack's middle control. On Fender and Marshall stacks it sets the depth of the stack's natural mid
scoop: at 0 the mids are scooped hardest, at 10 the stack is closest to flat and loudest. Many Fender and Vox amps have
no middle knob; there the stack's mid resistor is fixed at the stock value.

On the Portaflex B-15N Jumped, which has no middle control, this knob is *Bass 2*, channel two's bass.

On the Powerball this is *Mid Open*, the lead channel's middle pot that works with the Focused switch off (see
Control 1 and Switch 1); in its channel 1 it is the one middle knob.`,

    treble: `The tone stack's treble control. It sets how much high end passes the stack's treble cap and, on most stacks,
also moves the balance between treble and the rest. On amps with a single tone knob (the Tweed Deluxe, the 18 W, the
brownface Deluxe) it appears as *Tone*.`,

    presence: `Lifts the upper treble in the power amp. On most models it works through the global negative feedback:
the presence pot and cap reduce the feedback at high frequencies, so the power amp's gain rises there. Turn it up for
bite and cut, down to soften harsh tops. Because it acts in the feedback loop, its effect also depends on *Feedback*:
with no feedback it has nothing to work on. On the Mark IIC+, Mark IV and Mark V the presence is a rheostat in series with the
feedback, as on the amps: turning it up lowers the feedback at every frequency and the treble's most, so the amp also
gets a little louder and looser; at 0 it is at its darkest and tightest. The Fireball 60 has the same rheostat with no
cap at all, so there the knob is simply the amount of feedback: up is louder, looser and brighter. A few models wire
their presence elsewhere
(the Rectifiers' is shown as Control 2: a preamp treble control on the two-channel Dual Rectifier and on the
Rect-o-Verb's lead channel, and in the feedback on its rhythm channel;
on the three-channel Dual Rectifier and the Triple Rectifier it is in the feedback in Raw and Vintage and a preamp
control in Modern, and on the three-channel's orange channel a preamp control in Raw and in the feedback otherwise, so
in that channel's Modern it does next to nothing, as on the drawing; on the three-channel's clean channel it is a
preamp treble control at the master, and on the two-channel's orange channel it is in the feedback, so it does nothing
in that channel's Modern); the ODS 70s calls it *Accent*. On the Chieftain and Hot Cat 30 this knob is the amp's
*Brilliance*, on the Tiny Terror its *Tone*: a pot and cap across the power tubes' grids, like a Vox cut but wired the
other way, so turning it **up** puts more resistance in front of the cap and brings the treble back (0 is darkest).
On the OR120 and OR80 it is the *HF Drive*: it takes the feedback away from the low mids up, so it adds gain and bite
as well as treble.`,

    depth: `Lifts the low end in the power amp, around the speaker's resonance. It reduces the global negative feedback
below its corner frequency (at 10, down to a tenth of the stock amount), so the power amp's gain and its
interaction with the speaker load rise in the bass: more thump and chest. It is on the models whose originals have a
resonance or depth control (*Resonance* on the 5150s, Cornford and JVM, *Depth Punch* on the Powerball, *Depth Boost*
on the Savage). At 0 the feedback is stock. On the 6505+ it is the amp's own network, a 1M pot with a cap across
it in series with the feedback resistor: at 10 the feedback below about 500 Hz falls to a twenty-fourth. The DSL100's
and TSL100's *Deep* is a switch, as on the amps: see *Bright*.`,

    cut: `The Vox-style cut control (AC30, AC15, DC-30): a pot and cap across the phase inverter's outputs that shunt
treble away before the power tubes. Turning it **up** removes more treble (it works backwards to a treble knob). Use it
to tame the top end of a bright class-A amp. The Chieftain, Hot Cat 30 and Tiny Terror have the same circuit wired the
other way, as their *Brilliance* or *Tone* (see *Presence*).

Two models without a cut control put a knob of their own here. On the Powerball it is *Gate Threshold*, the lead
channel's noise gate (the knob on the amp's rear panel): at 0, and up to about 1.3, the gate is off; above that it
mutes the lead channel whenever the signal falls under the threshold, and turning it up raises the threshold. A played
note passes untouched. Set it just high enough that the amp is quiet between notes; set too high it cuts the tails of
held notes. It does nothing in channel 1. On the Savage 120 it is *Hi Balance*, which works, and shows, only with
*Smooth* on: it sets how much high end reaches the extra tube stage that Smooth adds. It starts at 5.`,

    master: `The master volume, on models that have one, at its real place in the circuit (on most amps between the
preamp and the phase inverter). It sets how hard the phase inverter and the power tubes are driven. At high settings
the power amp clips and compresses and the sag comes in; at low settings the power amp stays clean and the distortion
comes from the preamp alone. A new amp starts at 5, half driven; turn it up for power-amp breakup.

On amps without a master (Plexis, Tweeds, blackface Fenders) the control is hidden: the power amp is always fully
driven, as on the original, and the volume knobs set both loudness and drive. Use *Level* for the block's loudness.
Some models name it differently (*Post Gain*, *Output*, *Master Vol*, *Volume*).`,

    sag: `How much the power supply's voltage drops when you play hard. It scales the supply's source resistance (the
transformer and rectifier) from a quarter of stock at 0 to four times stock at 10; 5 is stock. More sag means the
plate voltage falls on loud notes and chords, so the power tubes run out of headroom sooner, the attack swells and the
amp feels softer and more compressed. Less sag is tighter and punchier. It only matters when the power amp is working
hard: with a low master and a clean power amp you will hardly hear it. On the solid-state models it scales the rails'
resistance in the same way.`,

    bias: `The power tubes' idle current, from cold (0 %) to hot (100 %); 50 % is stock. On fixed-bias amps it sets the
grid bias voltage from 1.2 times stock (cold) to 0.8 times stock (hot); on cathode-biased amps (AC30, AC15, Tweeds,
Champ and the like) it sets the cathode resistor from twice to half of stock. A colder bias gives more crossover
distortion, a grittier, thinner break-up and more headroom before it; a hotter bias is fuller and smoother and
saturates sooner. The effect is strongest with the power amp working hard.`,

    feedback: `The amount of global negative feedback from the speaker output back to the phase inverter: 5 is stock,
0 none, 10 twice stock. Less feedback makes the power amp louder, looser and more raw: the speaker load shapes the tone
more (bigger bass and treble peaks), and it distorts earlier and more openly. More feedback is tighter, flatter and
cleaner, with a harder edge when it does clip. Presence and Depth work through the feedback, so they lose their effect
as you turn it down.

On models built without global feedback (the AC30), 0 to 5 is none and only above 5 does it add some. On the
Rectifiers, the *Modern* switch takes the feedback out as on the real amp.`,

    bright: `On the models that have it, the bright switch: a small cap across the volume or gain pot that lets treble
bypass the pot. The boost is biggest with the pot turned down and fades as you turn it up. Some models give it their
own name and meaning: *Brilliance* (AC15), *Brilliant* (Prometheus Clean), *Thick* (Lone Star), *Raw* (Rectifiers),
*Bright Input* (SVT, Portaflex), *Sat* (BE-100), *OD2* (Quick Rod), *Hi Boost* (GK 800RB). On the Mark IIC+ it pulls both brights, Volume 1's
and the Lead Master's. On the DSL100 and TSL100 it is the amp's *Deep* switch, which is not a bright at all: it puts the amp's
resonant network (a simulated inductor, tuned to about 53 Hz) across the feedback, for a broad lift in the bass. On the
three-channel Dual Rectifier and the Triple Rectifier, *Raw* and *Modern* (Switch 1) are the amp's one Raw / Vintage /
Modern switch: both off is Vintage, and with both on the model is in Modern. On Dual Rectifier Orange it is *Clean*,
one end of that channel's Clean / Normal / Modern style (Switch 1 is Modern, both off is Normal). On the Powerball it
is channel 1's Bright and does nothing in the lead channel; the same goes for the 5150, where it is the rhythm
channel's Bright. On the Savage 120 it is *Crunch 2*, the amp's second crunch sound: the lead
channel with its extra gain stage and the *Lead* knob out of the path (*Lead Boost* then does nothing); *Smooth* and
the tone controls work as in the lead. On the Savage 120 Clean it is the clean channel's Bright. The value of the cap
is on the Advanced page as *Bright Cap*.`,

    level: `The block's output level in dB, after everything else. A clean digital volume: it does not change the amp's
drive or tone. Use it to balance this amp against other presets or the other amp. See
[gain staging](/manual/gain-staging).`,

    switch1: `A model's own switch, shown only on the models that have one and named after it: a channel select
(*Normal Ch*, *Vibrato Ch*, *Clean Ch*, *Channel 2*), an input select (*Low Input*, *Normal Input*, *Mic Input*), the
Plexi-style *Jumper* that links both channels, or a voicing switch (*Fat*, *Modern*, *Clarity*, *Contour*, *Treble Shift*).
Each switches the real components on the model's schematic. Some worth knowing:

- **Dual and Triple Rectifiers, *Modern*:** the Modern voicing, which also takes the global feedback out, as on the
  real amp.
- **Rect-o-Verb, *Rhythm* and *Pushed*:** *Rhythm* plays the clean rhythm channel in place of the lead channel, with
  the same knobs; it is much quieter at the same settings, so turn *Master* up. *Pushed* drives the rhythm channel
  harder and does nothing on the lead channel. *Presence* is the playing channel's. *Output* (Control 1) is the
  amp's Output Level, after both channels' masters and the effects loop's tube stage: the masters set how hard the
  loop stage is driven, Output how hard the power amp is.
- **5150, *Rhythm Ch*:** plays the rhythm channel in place of the lead channel, with the same knobs (*Pre Gain* and
  *Post Gain* are the playing channel's). It is clean and much quieter at the same settings, so turn *Post Gain* up.
  *Crunch* (Switch 2) gives it about 20 dB more gain for a rock rhythm sound, and *Bright* is its bright switch;
  neither does anything on the lead channel.
- **5150III Clean/Crunch, *Crunch*:** plays the amp's blue channel (channel two) in place of the green one, with its
  own gain network and tone stack behind the same knobs.
- **Powerball, *Focused*:** in the lead channel it swaps the open middle pot for the focused one (*Mid Focused*,
  Control 1) and doubles the treble cap; in channel 1 it doubles the treble cap and makes the middle's cap smaller.
- **Savage 120 Clean, *Contour*:** adds to the treble cap, as on the Savage 120.
- **Mark V, *IIC+ Mode*:** with *Mark IV Mode* (Switch 2), selects the channel-3 mode; both off is Extreme. The amp
  has one three-position switch, so with both on the model is in Mark IV mode. On Mark V Clean the pair is *Fat* and
  *Tweed* (both off is Clean; both on is Tweed) and Switch 3 is the amp's *Bold* switch; on Mark V Crunch it is
  *Edge* and *Mark I* (both off is Crunch; both on is Mark I). On all three, *Master* (Control 1) is the channel's
  master and *Output* is the amp's Output control after the effects loop's tube stage.
- **TriAxis, *Yellow*:** with *Red* (Switch 2), the preamp's mode switches.
- **AFD100, *AFD*:** off is the #34 mode, on is AFD.
- **6505+ Rhythm, *Crunch*:** takes the pad ahead of the rhythm channel's first stage out, about 20 dB more gain:
  the clean channel becomes a crunch channel. *Bright* is the channel's own switch.
- **JCM800 2210 and 2205, *Normal*:** off is the Boost channel, on the Normal (clean) channel. *Gain* is the Boost
  channel's Gain and the Normal channel's Volume; *Bass* and *Treble* are the playing channel's; *Middle* and
  *Boost Volume* (Control 1) belong to the Boost channel.
- **TSL100, *Overdrive*:** off is the Clean channel, on the Crunch channel, or the Lead channel with *Lead*
  (Switch 2), as the amp's two mode switches work. Gain, Volume and the tone knobs are the playing channel's own.
  *Mid Boost / Tone Shift* (Switch 3) is that channel's voicing switch: Mid Boost on Clean, Tone Shift on Crunch
  and Lead.
- **RG100ES, *Channel 1*:** off is channel 2 (red LED), the high-gain channel; on is channel 1 (green), with less
  gain and no clamps. *Sustain Boost* (Switch 2, the pull switch on the amp's Treble knob) puts two clipping diodes
  ahead of channel 2's *Master*: more sustain and much less level, so turn *Master* up. It does nothing on channel 1.
- **JC-120, *Distortion On*:** the effect channel's distortion (on the real amp, the switch at the bottom of the
  Distortion pot's travel, and the footswitch): the *Distortion* knob (Control 2) and the diode clipper ahead of the
  tone stack come in. Off, as the amp is usually played, the knob does nothing and is not shown.
- **GK 800RB, *Contour*:** the amp's Mid Contour filter, a notch of about 10 dB at 500 Hz for a rounder sound.
  *Lo Cut* (Switch 2) rolls the bass off below about 200 Hz, and *-10 dB* (Switch 3) is the input attenuator for a
  hot bass.`,

    switch2: `A model's second switch, where it has one: for example *Tube Rectifier* on the Rectifiers (5U4 tube
rectifiers instead of silicon: more sag), *Crunch* on the SLO-100's normal channel and the 5150's rhythm channel, *Deep* on the Mark IIC+
(a bass boost), *Bite Up* on the Prometheus, *Preshape* on the Savage 120 Clean (it takes the signal past the
clean channel's shaping network: much louder and fuller, and it overdrives sooner), *Gain Hi* on the Powerball (the amp's Gain Lo/Hi button: the hotter Hi Lead in the lead channel, Crunch in place of
Clean in channel 1), *Mark IV Mode* on
the Mark V, *C45* on the BE-100, *Rhythm Clip* on the Silver Jubilee, *Ultra Lo* on the SVT, SVT-CL and B-15. It
switches the same parts the real switch does.

Where the amp has one three-position switch, the model shows its two outer positions as two switches and both off is
the middle position: *Bite Up* and *Bite Down* on the Prometheus, *Ultra Lo* and *Bass Cut* on the SVT, *IIC+ Mode*
and *Mark IV Mode* on the Mark V (*Fat* and *Tweed*, *Edge* and *Mark I* on its other channels), *Raw* and *Modern*
on the three-channel Rectifiers. The amp cannot have both at
once, so with both on the model takes the second of each pair as named here and ignores the first.`,

    switch3: `A model's third switch, where it has one: *Spongy* on the Rectifiers (a lower supply voltage, modelled as
90 % mains, for a softer, saggier feel), *Bass Shift* on the Mark IIC+, *Bite Down* on the Prometheus, *Mid Gain*
on the Mark IV (the amp's Harmonics / Mid Gain switch; off is Harmonics), *Bright* on the Mark V, *Bass Cut* on the
SVT, *-15 dB Input* on the SVT-CL (the padded jack, for active or hot basses), *Voice* on the BE-100, *Low Power* on
the Silver Jubilee, *Tone Shift* on the DSL100, *3rd Gear* on the Quick Rod,
*-10 dB* on the GK 800RB (its input attenuator), *Smooth* on the Savage 120 (its second lead voice: less gain into the tone stack and one more tube stage after it, so
the sound is rounder and the tone knobs do less; off is Rough), *Channel 1* on the Powerball (its Clean and Crunch
channel, chosen by *Gain Hi*, with its own gain, tone stack and *Bright*; off is the lead channel), *Crunch 1* on the
Savage 120 Clean (one more tube stage behind its own *Crunch 1 Gain*; off is Clean), *Clean* on the
Fireball 60 (its clean channel, which shares the gain and the tone stack with Ultra; off is Ultra). *Bite Down* and
*Bass Cut* are the other side of the switch that *Bite Up* and *Ultra Lo* (Switch 2) belong to, and win when both are
on.`,

    control1: `A model's own extra knob, where it has one: a second volume (*Normal Vol* on Plexis and Bassmans,
*Mic Vol* on the Tweed Deluxe, *Loudness 2*, *Volume II*), a lead drive (*Lead Drive* on the Mark IIC+ and IV,
*Lead 1 Drive*, *Overdrive*, *Drive*), a channel master (*R1 Master* and *R2 Master* on the Mark IV's rhythm
channels), an output level after the masters (*Output* on the Rect-o-Verb), or a tone control (*Edge* on the Hot Cat, *Mid Freq* on the SVT, *Frequency* on the SVT-CL (the
five-position switch that sets where *Middle* works: 220 Hz, 450 Hz, 800 Hz, 1.6 kHz or 3 kHz, at 0, 2.5, 5, 7.5 and
10), *Hi Treble*
on the JC-120, *Mid Focused* on the Powerball, *Cut* on the Prometheus, a low cut before its gain stages, *Lo Mid* on
the GK 800RB, the 250 Hz band of its four-band EQ; *Middle* is its *Hi Mid*, at 1 kHz, *FAC* on the
OR120 and OR80: a six-position switch that cuts more bass at each step, full range at 0, where it starts), or a
second gain (*Crunch 1 Gain* on the Savage 120 Clean, shown with *Crunch 1* on). On jumped
Plexi-style amps the two volumes blend the bright and normal channels. On the JCM800 2210 and 2205 it is the
*Boost Volume*, the Boost channel's level after its clipper, which starts at 10: lower it and the amp gets much
quieter, as it does not drive the power amp then. On the Quick Rod it is the *Solo Boost*, which
starts at 0: the boost is off there, as on the amp until you press its footswitch; turn it up for the solo lift.`,

    control2: `A model's second extra knob, where it has one: *Lead Master* on the Mark IIC+ and IV, *Presence* on the
Rectifiers (a preamp presence control there), *Lead Volume* on the Savage (Crunch 2's volume too), *Volume* on the
Savage 120 Clean (the Clean or the Crunch volume), *Channel Volume* on the Powerball (the volume of the mode that plays: Lo or Hi Lead,
Clean or Crunch), *OD Level* on the ODS,
*Dynamic Voice* on the TriAxis, the 4 kHz band of the Bass 400's EQ, *Distortion* on the JC-120 (the gain ahead of its
diode clipper, from about 2× to 48×; it shows, and acts, only with the *Distortion On* switch on), *Boost* on the
GK 800RB (up to 18 dB more into its FET stage after the EQ: turn it up and *Master* down for the amp's growl).`,

    eq1: `The first band of a graphic EQ that is part of the model's own circuit (the Mesa Mark IIC+, IV and V's 80 Hz
slider, the Bass 400's 34 Hz). ±12 dB maps onto the slider's travel; 0 is the slider centred. Hidden on models without
one. For a general EQ use the EQ page.`,
    eq2: `The second band of the model's own graphic EQ (240 Hz on the Mark amps, 107 Hz on the Bass 400).`,
    eq3: `The third band of the model's own graphic EQ (750 Hz on the Mark amps, 269 Hz on the Bass 400). Pulling it
down gives the classic Mark "V" curve.`,
    eq4: `The fourth band of the model's own graphic EQ (2200 Hz on the Mark amps, 723 Hz on the Bass 400).`,
    eq5: `The fifth band of the model's own graphic EQ (6600 Hz on the Mark amps, 1.6 kHz on the Bass 400).`,

    hitreble: `A high-treble shelf at 3 kHz between the preamp and the power amp, ±12 dB across the knob; 5 is flat
(and the shelf is then switched out). Use it to add sparkle or take fizz off the preamp's distortion before it hits
the power amp, which a cab-block EQ after the amp cannot do. Hidden on the JC-120, which has its own *Hi Treble*.`,

    overdrive: `An extra gain stage's worth of drive for models that have no drive knob of their own: up to 20 dB more
swing into the last gain stage at 10; 0 is off. It turns a crunch amp into a lead amp without changing the tone stack,
and the extra distortion comes from the amp's own tubes. Hidden on models that already have an overdrive or lead
drive control, and where the preamp has no gain stage to drive (the JC-120).`,

    presshift: `Moves the presence control's frequency down an octave, for a broader, lower-reaching presence lift. Shown
on models whose power amp has a presence circuit in its feedback loop, whether or not the panel has a Presence knob;
hidden on solid-state amps and on tube amps without one (the SVT, the blackface Fenders). On the Mark IV and Mark IV
Rhythm 2 it is the amp's own *Pull Shift* on the Presence knob instead: a small cap in series with the feedback, so the
power amp keeps its feedback only in the top of the treble and runs open below it. The amp gets louder, looser and more
forward in the mids, and the Presence knob then works on that top range only.`,

    fatsw: `Moves the tone stack's frequencies down (to 0.6 times stock): fuller lows and low mids, as the "Fat" switch
does on amps that have one. Hidden where the model has its own Fat switch, and where its preamp has no tone stack.

On the Powerball it is *Bottom*, the amp's own low boost: its three Bottom buttons (channel 1, Lo Lead and Hi Lead) on
one switch, which acts on the mode that plays. A gentle lift of the lows, strongest in channel 1.`,

    cutsw: `A 150 Hz high-pass at the input (if *Low Cut* is set higher, the higher one wins). Tightens a boomy amp or a
dark guitar and keeps the low end from smearing high-gain sounds. Hidden on the SVT, which has its own *Bass Cut*.`,

    preamp: `Turns the preamp simulation off, so the block is the model's power amp alone: phase inverter, output tubes,
transformer, negative feedback, supply and speaker load. Use it behind another preamp: an [Amp NAM](/manual/blocks/nam)
block holding a capture of a preamp, or a second Amp block with its *Power Amp* off. Presence, Depth, Sag, Bias and the
speaker load then work on that preamp's sound. The input is taken as a preamp's output, not a guitar: full scale drives
the inverter as hard as the model's own preamp can, which is the level an Amp block with *Power Amp* off puts out.
*Master* becomes a plain volume control in front of the inverter (10 passes the input unchanged) and *Input Trim* trims
the level as well; Gain, the tone controls and the rest of this page do nothing. The switch stays as it is when you
change the type, so you can try one preamp through different power amps.`,

    intrim: `Gain at the input jack, in dB, before the first tube, with no change to the tone. It is like using a hotter
or weaker pickup: +6 dB drives every stage harder, -6 dB cleans the amp up. Use it to match single coils and
humbuckers on the same preset, or when an amp feels too clean or too hot at the gain you like. It acts before the
built-in boost, input filter and Input Dynamics, so it also sets how hard the boost is hit, as a hotter pickup would.
The amp also reads your interface's input calibration, so a correctly set [audio setup](/manual/audio-setup) means
0 dB here behaves like a real guitar into the real amp.`,

    inputsel: `Which input the mono amp listens to: Left, Right, or the average of both. Use Right or Left + Right when
the block before it is stereo or your guitar comes in on the second input.`,

    lowcut: `A first-order high-pass at the amp's input, before the first tube, in Hz; 0 is off. Cutting lows before the
gain stages tightens a high-gain amp much more than cutting them afterwards, because the bass never drives the
distortion. Around 80 to 120 Hz is a good start for chugging rhythm; push it higher for a thinner, more cutting sound.`,

    pretube: `Replaces every 12AX7 in the preamp with another triode: 12AX7 (the usual high-gain tube), 12AY7 (much
lower gain, the original tweed tube), 5751 (about 30 % less gain than a 12AX7, smoother), 12AT7 (lower gain, more
current) or 12AU7 (lowest gain). Lower-gain tubes give a cleaner, more open amp with more headroom. Stages that are not
12AX7s in the original (a 12AT7 phase inverter or reverb driver) keep their own tube; a stage that runs both halves of a
12AX7 in parallel gets both halves of the chosen tube. Hidden on models whose preamp has no 12AX7. Stock keeps the schematic's
tubes.`,

    brightcap: `The value of the model's bright caps, as a percentage of stock. A larger cap lets lower frequencies
bypass the pot too, so the bright boost reaches down into the upper mids; a smaller one leaves only the highest treble.
It affects every bright cap of the model (fixed ones across a volume pot, and switchable ones while the Bright switch
is on), but not a cap across the master. Hidden on models without a bright cap.`,

    prebias: `The bias of the last preamp gain stage: its cathode resistor from twice stock (-100 %, colder) to half
stock (+100 %, hotter). Moving the operating point off-centre makes that stage clip one side earlier than the other,
which changes its balance of even and odd harmonics and how its distortion feels. Small moves are enough.`,

    presag: `How much of the power supply's sag reaches the preamp: 100 % is the circuit as built, 0 % gives the preamp a
stiff supply of its own, 200 % doubles the effect. With more preamp sag the preamp's gain and headroom drop with the
supply on hard notes, adding to the bloom and compression. Hidden on rack preamps with a supply of their own (the
JMP-1, the TriAxis).`,

    stackfreq: `Shifts all the tone stack's frequencies, by dividing its caps by this percentage: above 100 % the stack's
response moves up (brighter, mids higher), below it moves down (fuller, darker). Hidden on models whose preamp has no
tone stack.`,

    mvtrim: `Scales the Master knob's travel. At 200 % the master reaches full at 5 and stays there; at 50 % it never gets
past halfway. Use it to fine-tune where the power amp starts to break up without changing your Master setting. Hidden
on models without a master.`,

    poweramp: `Turns the power amp simulation off. The block then outputs the preamp's signal as it arrives at the phase
inverter, for driving a real tube power amp. The supply, bias and speaker load no longer apply; everything up to and
including the Master still does.`,

    powertube: `Replaces the power tubes with another type, using that tube's own curves, rebiased to the model's stock
idle current. The usual reputations: 6L6GC big and clean (American), EL34 crunchy with strong mids (British), EL84
small, chimey and early to break up, 6V6GT small and sweet, KT66 smooth, KT88 and 6550 big and tight. For a 5881 pick
6L6GC: the 5881 is a 6L6 electrically, built for higher ratings that the model doesn't limit. The rest of the circuit (transformer, supply voltage, tube count)
stays the model's, so this tells you what that amp would do with those tubes, not what a different amp would do, and
the difference is clearest with the power amp driven.`,

    xfmrmatch: `The output transformer's primary impedance, as a percentage of stock: the load the power tubes see. A
mismatch changes how much voltage and current the tubes swing, so it changes where and how they clip, and the
transformer's turns ratio changes the output level too (higher is quieter). Small moves are the useful ones; listen
with the power amp driven.`,

    xfmrlf: `The output transformer's low-frequency corner, as a percentage of stock (the magnetising inductance
divided by it). Above 100 % the transformer passes less deep bass; below, more.`,

    xfmrhf: `The output transformer's high-frequency corner, as a percentage of stock (its leakage inductance and
capacitance divided by it). Above 100 % the amp has more top end; below, it rolls off earlier.`,

    xfmrdrive: `Core saturation beyond stock: at 0 the transformer is as the model has it; each step of 2 halves the flux
at which the core saturates. With it up, loud low notes at full power saturate the core, which compresses and
thickens the bass. Only heard with the power amp working hard.`,

    speaker: `The speaker as the electrical load on the power amp, not its sound. A real speaker's impedance peaks at the
cone's resonance and rises with frequency, and a tube power amp's output follows it, especially with little
feedback: more thump and more treble, and different clipping and sag. *Stock* is the model's own load; the presets
are typical open-back and closed cabinets; *Follow Cab*, where a new amp starts, takes the load of the speaker and cabinet chosen in the
next Cab block (with no Cab after the amp it is the model's own load). The Cab block still provides the speaker's sound (the cone, box and mic); this only changes how the amp
behaves driving it.`,

    spkres: `The load's bass resonance frequency, as a percentage of the chosen curve's. It moves the low-end impedance
peak, where a low-feedback amp gets its extra thump.`,

    spkq: `How sharp the load's resonance peak is: above 100 % narrower and taller, below it broader.`,

    spkpeak: `The height of the load's resonance peak. Higher means more bass from the power amp around the resonance,
most audible on amps with little feedback.`,

    spkhf: `The voice coil's inductance, the rise in impedance toward the treble. Higher gives the power amp more upper
treble and presence; lower is smoother.`,

    spkimp: `The whole load against the transformer tap, as a percentage: 50 % is a 4 Ω cab on the 8 Ω tap, 200 % a
16 Ω cab on it. The mismatch changes the load the power tubes see through the transformer, so it changes how they
clip and how strongly the amp's response follows the speaker's curve. Real amps are often run mismatched by a factor
of two without harm; here you can hear what it does.`,

    presfreq: `The presence control's frequency, as a percentage of stock (its cap divided by it). Higher moves the lift
up into the upper treble, lower brings it down into the upper mids. Shown on models whose power amp has a presence
circuit, also where it is a fixed one with no knob (the Dual Rectifier, whose panel Presence is in the preamp).`,

    depthfreq: `The depth control's corner frequency, as a percentage of stock. Higher makes Depth reach higher into the
low mids; lower keeps it down in the deep bass. Shown on models that have a Depth (or Resonance) control; on the
DSL100 and TSL100 it moves the *Deep* switch's resonance instead.`,

    variac: `The mains voltage, as a percentage of normal: it scales every supply voltage (and a fixed bias with them).
Below 100 % the amp has less headroom and breaks up earlier and softer, the "brown sound" trick of running an amp on a
variac; above it gets stiffer and cleaner. The output is not level-compensated, so a low variac is also a little
quieter at the same settings.`,

    btc: `The B+ time constant: the main filter cap, as a percentage of stock. Larger means the supply sags and recovers
more slowly (a longer swell and release); smaller makes the sag quick and twitchy. Also changes the hum ripple on an AC
supply.`,

    supply: `How the supply is fed. *Stock* is as the model has it. *DC* is an ideal source with no ripple. *AC 60 Hz* and
*AC 50 Hz* rectify the mains with its ripple (120 Hz or 100 Hz) riding on the supply, which modulates the power amp
slightly when it is working hard.`,

    rectifier: `The rectifier, replacing the model's. *Silicon* has almost no voltage drop: stiff and punchy. Tube
rectifiers drop more voltage the more current you draw, so they add sag: *GZ34* is the stiffest, *5U4* softer, *5Y3*
the softest (the tweed sound), *EZ81* the small one of EL84 amps. Panel switches such as the Rectifiers' *Tube
Rectifier* apply as on the real amp; a choice here applies on top.`,

    katc: `On cathode-biased power amps, the cathode bypass cap as a percentage of stock. It sets how fast the bias
shifts when the amp is driven hard: larger is slower, so the amp keeps pumping and recovering over a longer time. Hidden
on fixed-bias amps.`,

    boost: `A drive pedal built into the amp's input, run inside the amp's own oversampled circuit. *TS808 OD* is an
Ibanez Tube Screamer, *Super OD* a Boss SD-1, *Chiron OD* a Klon Centaur, *Treble Booster* a Dallas Rangemaster,
*Esoteric Boost* an Xotic RC Booster and *FET Boost* a JFET clean boost. Classic use: Drive low, Level up, to tighten
and push a high-gain amp. It gets your guitar's signal after *Input Trim* and the input calibration, so it is hit
exactly as the same pedal in a [Drive](/manual/blocks/drive) block in front would be; use that block for the full
pedal with all its knobs.`,

    boostdrive: `The boost pedal's drive (its gain). Low settings keep the pedal clean and let the amp do the distortion.`,

    boosttone: `The boost pedal's tone control.`,

    boostlevel: `The boost pedal's output level into the amp, in dB. This is what pushes the amp harder.`,

    saturation: `A diode-style soft clipper between the preamp and the power amp, like the common "saturation" mod.
Adds sustain and a smoother, more compressed distortion on top of the preamp's own.`,

    satdrive: `How hard the Saturation clipper is driven (up to 30 dB into it at 10). It sets how early it clips, not the
level: the small-signal gain stays the same.`,

    definition: `A tilt EQ into the preamp, pivoting around 650 Hz: positive sends more treble and less bass into the
gain stages (up to ±4 dB at the ends), so the amp clips more on the treble and the attack gets tighter and more
defined; negative is darker and woollier. There is no matching correction afterwards, so you hear the tilt too.`,

    infilter: `A filter at the amp's input, before the first stage: shelves, a peak, a high-pass or a low-pass. Shaping
what goes into the preamp changes what distorts, which an EQ after the amp cannot do. Shelves and the peak are
switched out while their gain is 0 dB.`,

    inffreq: `The input filter's frequency.`,
    infq: `The input filter's Q (bandwidth for the peak, resonance for the high- and low-pass).`,
    infgain: `The input filter's boost or cut, for the shelves and the peak.`,

    outeq: `A graphic EQ built into the block, with a choice of layouts: octave and 2/3-octave layouts, and the passive
Mark, 5-, 4- and 3-band and console voicings, approximated here by broad overlapping bands. Off by default. Bands left
at 0 dB cost nothing. The chart at the EQ page's right shows its curve, measured on this EQ itself: its passive layouts
are the approximations, so they differ from the Graphic EQ block's circuits of the same name.`,

    outeqloc: `Where the graphic EQ sits: at the block's *Output* (a plain tone EQ), *Pre Power Amp* (between preamp and
power amp, so it shapes what the power tubes distort, like the Mark series' EQ), or at the *Input* (before the preamp,
so it shapes what the preamp distorts).`,

    'outeq#': `One band of the graphic EQ, ±12 dB. The band's frequency depends on the layout chosen in *Output EQ*
(for example, band 1 is 31 Hz on the 10 Band layout and 100 Hz on the 5 Band one). Bands past the layout's count do
nothing.`,

    indyn: `An expander or compressor on the amp's input, following your picking. Positive values expand: soft playing
gets softer and cleaner, hard playing hotter, for a more touch-sensitive amp. Negative values compress: a more even
drive whatever your attack. It pivots around -20 dBV at the input jack (a firmly picked note, after *Input Trim*
and the input calibration), and the change is held within ±12 dB; 0 is off.`,

    comptype: `A compressor on the amp's output. *Output* detects the amp's output and compresses it (feed-forward,
firmer); *Feedback* detects its own compressed output (smoother, with a lower effective ratio).`,

    compamount: `The output compressor's ratio, from 1:1 at 0 to 10:1 at 10.`,

    compthresh: `The level above which the output compressor works, in dB below the block's full scale (before Level).`,

    compclarity: `High-passes the compressor's detector from 20 Hz (0) to 500 Hz (10), so low notes trigger less
compression and the low end stays punchier.`,

    spkdrive: `Speaker cone excursion: the band below 150 Hz, where the cone moves most, runs into a soft limit that
comes in earlier as you turn it up. A loud low note flattens a little while the rest passes. Turned off with *Output
Mode* on *SS Power Amp + Cab*.`,

    thump: `A low resonance bump (up to +12 dB) at *Cab Resonance*, like the thump of a closed cab. Turned off with
*Output Mode* on *SS Power Amp + Cab*.`,

    cabres: `The frequency of the Speaker Thump bump, in Hz. Around 80 to 120 Hz for a 4x12, higher for small cabs.`,

    spkcomp: `Voice-coil heating: the signal's power, averaged over *Speaker Comp Time*, turns the output down the way a
hot voice coil does. A full-scale note held long enough loses about 0.6 dB per step (6 dB at 10). Turned off with
*Output Mode* on *SS Power Amp + Cab*.`,

    spkcomptc: `How fast the speaker compression responds: short times react to single notes, long ones to how hard you
have been playing over the last second or two.`,

    tremrate: `The tremolo's speed in Hz. On the models with a tremolo or vibrato circuit of their own (the blackface
Fenders' vibrato: Deluxe, Twin, Super and Princeton Reverb, Vibroverb, Vibrolux Reverb, Bandmaster, Tremolux and
Princeton AA964; the Vibro-King, the brownface Deluxe and Vibrolux, the brownface Concert and Super's harmonic
vibrato, the 18 W, the Bluesbreaker 1962 and the Scout) it is that circuit's *Speed*, held to the original knob's
range (the Bluesbreaker's is slow, about 2 to 4.7 Hz). On every other model it is a sine that swings the power tubes'
bias.`,

    tremdepth: `The tremolo's depth; 0 is off. On models with their own tremolo it is that circuit's *Intensity* (or
*Depth*) knob. Elsewhere it swings the power tubes' bias around the *Bias* setting, 100 % taking it all the way from
cold to hot: a bias tremolo, which also changes the tone and break-up as it pulses. On the 18 W the knob is the amp's
Intensity control, which does not go down to nothing: 0 is the footswitch (tremolo off), and the lowest setting above
it already pulses a little, because in that circuit the running oscillator alone moves the channel. The Bluesbreaker
1962's Intensity does not reach nothing either (at its lowest the switch still works on the resistor under the pot), and its tremolo works on Channel II
only: play *Channel II* alone for the full effect; with the channels jumpered Channel I plays on through it.`,

    balance: `The left/right balance of the block's stereo output.`,

    pthard: `How sharply the power tubes go from clean to clipped: 5 is stock, lower softens the knee (down to a quarter),
higher sharpens it (up to four times). Softer is rounder and more compressed, harder is more aggressive.`,

    prehard: `The same for every preamp triode's cut-off knee: lower for a rounder, softer preamp distortion, higher for a
harder, edgier one.`,

    stacktype: `Swaps the model's tone stack for another amp's, keeping it in the same place in the circuit. *Stock* is
the model's own. Shown on models whose stack is a separate stage that can be swapped.`,

    stackloc: `Moves the tone stack to another place in the preamp, each stage keeping its operating point. *Pre* puts it
before the first tube (the stack shapes what the whole preamp distorts: tighter or looser, depending on Bass), *Mid*
after the first gain stage, *Post* after the last preamp stage (the stack shapes the finished distortion, more like
an EQ). *Stock* is where the schematic has it.`,

    t2plate: `The corner of the plate-bypass cap on the last preamp stage that has one (the cold clipper of the SLO-100
and the Rectifiers, the Mark IIC+'s lead stage, the 5150III, the Quick Rod), as a percentage of stock. This cap rolls off that stage's treble: above 100 % the roll-off
moves up (more fizz and edge), below it the distortion gets smoother and darker.`,

    cfcomp: `How much grid current the cathode follower driving the tone stack draws, as a percentage of stock. When the
previous stage drives its grid above its cathode, it compresses. More gives a squishier, more compressed high-gain
feel; 0 removes it. On the models with a cathode follower (Plexi, JCM800s, Prometheus, GH100L, Bassman 5F6-A,
SLO-100).`,

    cfharm: `The cathode follower's cathode resistor, as a percentage of stock. It moves its idle current, so how early
and how asymmetrically it clips: a change in its harmonics.`,

    prebiasx: `How much the preamp triodes' grid current shifts their bias when you hit them hard (it charges the coupling
caps), as a percentage of stock. This is the "blocking" that makes an amp choke and then recover after a loud chord.
0 removes it, higher exaggerates it.`,

    pibiasx: `The same for the phase inverter's driven grid: how far the inverter's bias shifts under heavy drive.
Shown on models whose phase inverter input is modelled with its coupling cap.`,

    ptbiasx: `The same for the power tubes' grids: how far grid current charges the coupling caps and shifts the power
tubes' bias when the power amp is driven hard, giving the sag-like squash and "farting out" of a cranked amp.`,

    ptbiastime: `The coupling caps into the power tubes' grids, as a percentage of stock: both how fast the bias shifts
under drive and how fast it recovers.`,

    ptrecovery: `The power tubes' grid leak resistors, as a percentage of stock: how long the bias takes to recover after
a hard hit, without changing how fast it shifts.`,

    masterbiasx: `Scales the preamp, phase inverter and power tube bias excursion together; 100 % is stock, 0 % none.`,

    suppressor: `Clamp diodes that keep each plate from swinging below its cathode, as some amps fit to protect the output
transformer. They limit the inductive spikes when the power amp is driven into hard clipping, which slightly changes
its hardest distortion.`,

    mvloc: `Moves the master volume. *Stock* is where the circuit has it; *Post PI* makes it a divider after the phase
inverter, so the inverter is driven fully whatever the Master, and turning the master down keeps the inverter's own
distortion. *Pre-Triode* makes it a divider ahead of the last preamp gain stage, with the circuit's own master full up:
turning it down cleans up that stage and everything after it, and what the earlier stages do stays. Shown on push-pull
amps that have a master; where the master belongs to one channel, it acts while that channel plays.`,

    outmode: `What the amp's output feeds. *FRFR* is for headphones, studio monitors and full-range speakers through a
Cab block. *SS Power Amp + Cab* is for a solid-state power amp into a real guitar cabinet: it turns off Speaker Drive,
Speaker Thump and Speaker Compression, since the real speaker does those itself.`,

    screenfreq: `The screen supply node's filter corner, as a percentage of stock (its cap divided by it). It sets how
fast the screen voltage follows the power tubes' current: a lower corner (bigger cap) holds the screens steadier.`,

    screenq: `0 feeds the screen node through a resistor, as on most amps. Above 0 the feed becomes a choke, with this Q
at its resonance, as on choke-filtered supplies: the screen voltage can bounce a little after a hard hit.`,

    oversampling: `Advanced page: how many times the sample rate the whole circuit runs at. **Standard** is 2x (1x at 64 kHz
and up). **High** doubles it: 7 to 14 dB less aliasing on the cranked high-gain models (the inharmonic grit on high
single notes with the gain up), for about twice the CPU of the amp. The cleaner models gain little from it. Changing it
rebuilds the signal path and changes the block's latency, so set it while you are not playing. A type change keeps it.`
  },

  options: {
    type: {
      Blackface: 'A generic Fender blackface (Deluxe and Twin style): clean, sparkling, scooped mids, bright switch.',
      Plexi: 'A generic Marshall 1959 Super Lead, 50 W style: open, bright crunch with no master volume.',
      AC30: 'Vox AC30 with the Top Boost channel (Switch 1 picks the Normal channel). Chimey class A, no feedback, Cut control.',
      AC15: 'Vox AC15 with its EF86 pentode channel (Switch 1 picks the 12AX7 channel). Warm, rich early break-up.',
      'DC-30': 'Matchless DC-30: two channels of boutique class-A chime and crunch (Switch 1 for channel 2).',
      Prometheus:
        'Lichtlaerm Prometheus lead channel. No schematic is published, so this is a plausible circuit fitted to captures, not a copy. Modern high gain with Bite switches.',
      'Prometheus Clean': 'The Prometheus clean channel, also inferred: a Fender-style clean with a Brilliant voice.',
      'JCM800 2203': 'Marshall JCM800 2203, 100 W master-volume: the classic 80s rock crunch and lead.',
      'JCM800 2204': 'Marshall JCM800 2204, the 50 W version: a little looser and earlier to break up.',
      JTM45: 'Marshall JTM45: the first Marshall, KT66 tubes and tube rectifier. Warm, round blues crunch.',
      'Plexi 100W': 'Marshall 100 W Plexi: more headroom and a bigger low end than the 50 W.',
      'Laney GH100L': 'Laney GH100L: British high gain with a drive control.',
      'Deluxe Reverb':
        'Fender Deluxe Reverb AB763 with its optical vibrato. The studio classic: sweet clean, early break-up. Switch 1 picks the vibrato channel.',
      'Twin Reverb': 'Fender Twin Reverb AB763: huge clean headroom, bright and glassy.',
      'Super Reverb': 'Fender Super Reverb AB763: four 10s, punchy and bright with a bit more grit than a Twin.',
      'Princeton Reverb': 'Fender Princeton Reverb: a small amp that breaks up early, sweet and compressed.',
      'Bassman 5F6-A': 'Fender tweed Bassman 5F6-A, one channel: fat, punchy crunch, the ancestor of the Marshall.',
      'Bassman 5F6-A Jumped': 'The 5F6-A with both channels jumpered, the way most people play it.',
      'Tweed Deluxe 5E3': 'Fender tweed Deluxe 5E3, one input: raw, compressed, breaks up very early.',
      'Tweed Deluxe 5E3 Jumped': 'The 5E3 with both inputs linked: thicker and dirtier.',
      'SLO-100':
        'Soldano SLO-100 overdrive channel (Switch 1 for the normal channel with its Crunch switch). Smooth, singing high gain.',
      'Mark IIC+': 'Mesa/Boogie Mark IIC+ lead channel with its five-band EQ: tight, focused, singing lead tone.',
      'Mark IIC+ Rhythm':
        'Mesa/Boogie Mark IIC+ rhythm channel: the same amp without the lead circuit, clean to a light break-up, with the five-band EQ.',
      '5150':
        'Peavey 5150 lead channel: aggressive high gain, tight and bright. Switch 1 for the rhythm channel with its Crunch and Bright switches.',
      '6505+':
        "Peavey 5150 II (6505+) lead channel, from the factory drawing: tighter in the low end than the 5150, with the amp's own presence and resonance networks.",
      '6505+ Rhythm':
        'Peavey 5150 II (6505+) rhythm channel: a bright, scooped clean, and a crunch channel with Crunch (Switch 1) on.',
      'Dual Rectifier':
        'Mesa/Boogie Dual Rectifier (two-channel, 1993), red channel, with Modern, Tube Rectifier and Spongy switches. Thick, saturated modern high gain.',
      '5150III':
        'EVH 5150III red channel, modelled from the 100S (Stealth) version of the amp: tight, saturated high gain.',
      '5150III Clean/Crunch':
        'EVH 5150III 100 W head, the green channel: a full clean with headroom that breaks up with Gain past halfway. Switch 1 (Crunch) for the blue channel: tighter lows and a hot-rodded crunch. Gain and Volume belong to the playing channel.',
      'Mark IV': 'Mesa/Boogie Mark IV lead channel with its five-band EQ.',
      'Mark IV Rhythm 1':
        'Mesa/Boogie Mark IV Rhythm 1, the clean channel, with its Bright pull, its own master and the five-band EQ.',
      'Mark IV Rhythm 2':
        'Mesa/Boogie Mark IV Rhythm 2, the crunch channel, with its Fat pull, its own master and the five-band EQ.',
      'Legacy VL100': 'Carvin Legacy VL100 (the Steve Vai amp) lead channel; Switch 1 for the clean channel.',
      Chieftain: 'Matchless Chieftain: boutique EL34 class A, rich and chimey.',
      'Hot Cat 30': 'Bad Cat Hot Cat 30, both inputs: hot-rodded class A with an Edge control.',
      'ODS 97': 'Dumble Overdrive Special (a 1997 ODS 100), clean and overdrive: smooth, vocal, touch-sensitive.',
      Express: 'Trainwreck Express, from a redraw of the circuit: raw, responsive EL34 crunch. Medium confidence.',
      'Lone Star': 'Mesa/Boogie Lone Star, both channels: warm cleans and a smooth drive mode.',
      'Mark V': 'Mesa/Boogie Mark V channel 3 with its IIC+, Mark IV and Extreme modes.',
      'Mark V Clean': 'Mesa/Boogie Mark V channel 1 with its Clean, Fat and Tweed modes and the Bold switch.',
      'Mark V Crunch': 'Mesa/Boogie Mark V channel 2 with its Edge, Crunch and Mark I modes.',
      'Rect-o-Verb':
        'Mesa/Boogie Rect-o-Verb (first series), both channels with its Output Level: a smaller Rectifier. For its reverb, put a Reverb block (Spring) after the Cab.',
      'Triple Rectifier':
        'Mesa/Boogie Triple Rectifier: built as the three-channel Dual Rectifier with six 6L6s, as no schematic was found.',
      Powerball:
        'Engl Powerball: channel 2 (lead), Lo and Hi modes, with its Open and Focused middle, its noise gate (Gate Threshold) and Bottom, and on Switch 3 channel 1, Clean and Crunch. Tight modern metal. The factory drawing of its preamp is wrong, so the stages are coupled as a hand-corrected copy of it shows.',
      'Savage 120':
        'Engl Savage 120, channel 2: the lead, Rough and (Switch 3) Smooth, and Crunch 2 on the Bright switch. Aggressive, mid-forward high gain.',
      'Savage 120 Clean':
        'Engl Savage 120, channel 1: Clean and (Switch 3) Crunch 1, with Bright, Preshape and Contour. A bright, tight clean that Preshape fattens and pushes.',
      'Fireball 60':
        'Engl Fireball 60: the Ultra channel and (Switch 3) the clean channel, into two 6L6: high gain with few controls. A resistor the drawing leaves out and two supply filter nodes are assumed, both from the Powerball.',
      SVT: 'Ampeg SVT, channel one: the classic tube bass amp, with its Mid-Range and Mid Freq, Ultra Hi, Ultra Lo and Bass Cut.',
      'SVT Channel 2':
        'Ampeg SVT, channel two: the plainer channel, Volume, Bass and Treble with Ultra Hi and Ultra Lo and no midrange controls. With Ultra Lo off its volume network cuts the lows, as on the amp.',
      'SVT-CL':
        'Ampeg SVT-CL (SVT Classic): the 1990s SVT, with Gain and Master, Ultra Hi and Ultra Lo, a five-position midrange Frequency switch and the -15 dB input.',
      'Portaflex B-15N': 'Ampeg B-15N Portaflex: warm, round vintage bass. One channel: its two are the same circuit.',
      'Portaflex B-15N Jumped':
        'Ampeg B-15N Portaflex with a cable from channel one to channel two, so both play. Each channel has its own controls: Volume 1, Bass 1, Treble 1, Ultra Hi 1 and Ultra Lo 1; Volume 2 (Control 1), Bass 2 (the Middle knob), Treble 2 (Control 2), Ultra Hi 2 (the Bright switch) and Ultra Lo 2 (Switch 3). Set one channel deep and the other bright and blend them with the volumes.',
      'GK 800RB':
        'Gallien-Krueger 800RB (1982): the solid-state rock bass head. Four-band EQ, Contour, Lo Cut and Hi Boost, and a FET Boost for its growl. Full-range mode into the 300 W amp.',
      'JC-120':
        'Roland JC-120 Jazz Chorus (the 1984 one-channel circuit, reverb off): a solid-state clean, very flat and bright, with its diode Distortion on Switch 1. For its chorus, put a [Chorus block](/manual/blocks/chorus) of type JC-120 after the amp and cab.',
      'Hot Rod Deluxe': 'Fender Hot Rod Deluxe, Normal and Drive channels with More Drive.',
      'Champ 5F1': 'Fender tweed Champ 5F1: single-ended, one knob, breaks up almost at once.',
      'Vibroverb AB763': 'Fender Vibroverb AB763 with its optical vibrato.',
      'Vibrolux Reverb AA964': 'Fender Vibrolux Reverb AA964 with its optical vibrato.',
      'Bandmaster AB763': 'Fender Bandmaster AB763 with its optical vibrato.',
      'Tweed Twin 5F8-A': 'Fender high-power tweed Twin 5F8-A: big, loud tweed with a presence control.',
      'Tweed Twin 5F8-A Jumped': 'The 5F8-A with both channels jumpered.',
      'Tremolux AA763': 'Fender Tremolux AA763 with its optical vibrato.',
      'Princeton 5F2-A': 'Fender tweed Princeton 5F2-A: tiny, single-ended, raw.',
      'Princeton AA964': 'Fender blackface-era Princeton AA964 (no reverb).',
      'Champ AA764':
        'Fender blackface and silverface Champ AA764: the single-ended 6V6 Champ with a Treble and Bass stack and more feedback.',
      'Vibro-King': 'Fender Vibro-King with its optical vibrato and Fat switch.',
      'Blues Junior': 'Fender Blues Junior III: small EL84 combo with a Fat switch.',
      'Pro Junior': 'Fender Pro Junior: 15 W EL84 combo with only Volume and Tone, and no master.',
      'BE-100': 'Friedman BE-100, BE channel: hot-rodded Marshall crunch and lead, with Sat, C45 and Voice switches.',
      'BE-100 HBE': "The BE-100's HBE channel: more gain.",
      'Silver Jubilee': 'Marshall Silver Jubilee 2555, with its rhythm clip and lead modes.',
      '18 W 1974X': 'Marshall 18 W 1974X: EL84 small-box crunch with its own tremolo (Speed and Depth).',
      'Hiwatt DR103': 'Hiwatt DR103: loud, clean, stiff British power with Normal and Brilliant channels.',
      '1959SLP': 'Marshall 1959SLP reissue Super Lead.',
      '1987X': 'Marshall 1987X reissue, the 50 W Super Lead.',
      '1959T Tremolo':
        'Marshall 1959T Super Lead Tremolo (the tremolo itself is not built; its load on the circuit is).',
      'Bluesbreaker 1962':
        'Marshall 1962 Bluesbreaker combo, both channels: warm KT66 crunch, and its tremolo on Channel II.',
      'Hiwatt DR504': "Hiwatt Custom 50 / DR504: the DR103's preamp with a 50 W output.",
      'Cornford MK50H II': 'Cornford MK50H II, both channels: British boutique crunch and lead.',
      'Tiny Terror': 'Orange Tiny Terror, 15 W: thick, mid-heavy crunch.',
      OR120: 'Orange OR120 Graphic Mk II ("Pics Only"), 120 W: thick 1970s crunch with the FAC and HF Drive.',
      OR80: 'Orange OR80 Graphic Mk II: the OR120 on two EL34, 80 W.',
      Liverpool: 'Trainwreck Liverpool: EL84 boutique crunch with a Clarity switch.',
      'ODS 70s': 'The 1970s Dumble Overdrive Special, with Rock/Jazz, Deep, Bright and Accent.',
      'Dual Rectifier 3-Ch':
        'Mesa/Boogie Dual Rectifier three-channel Solo Head, channel 3, with Raw, Vintage and Modern.',
      'Subway Blues': 'Mesa/Boogie Subway Blues: small, warm blues amp (the potted toning module is left out).',
      'TriAxis Rhythm': 'Mesa/Boogie TriAxis preamp, rhythm modes, into a generic 4 x 6L6 power amp.',
      'TriAxis Lead 1': 'The TriAxis Lead 1 modes.',
      'TriAxis Lead 2': 'The TriAxis Lead 2 modes: the hottest.',
      'JCM900 4502': 'Marshall JCM900 4502 (50 W), both channels.',
      'JCM900 4100': 'Marshall JCM900 4100 (100 W), both channels: hot 90s high gain.',
      'Valvestate 8080': 'Marshall Valvestate 8080: a tube preamp stage into a solid-state power amp.',
      'JVM410H OD': 'Marshall JVM410H OD channels in Green, Orange and Red modes.',
      'Concert 6G12': 'Fender brownface Concert 6G12 with its harmonic vibrato.',
      'Super 6G4': 'Fender brownface Super 6G4 with its harmonic vibrato.',
      'Deluxe 6G3': 'Fender brownface Deluxe 6G3, Normal and Bright channels, with its bias tremolo.',
      'Vibrolux 6G11': 'Fender brownface Vibrolux 6G11 with its bias tremolo.',
      'Bassman 135': 'Fender silverface Bassman 135 bass amp, Bass and Normal channels.',
      'Scout GA-17RVT': 'Gibson Scout: a small 60s combo with its bias tremolo.',
      'Classic 30':
        'Peavey Classic 30: a four-EL84 combo, Lead channel (Pre and Post) and Normal channel, with its Boost switch.',
      'Major 1967': 'Marshall Major 1967, 200 W: huge, loud and clean until it is very loud.',
      'Bass 400': 'Mesa/Boogie Bass 400 with its graphic EQ.',
      'JVM410H Clean/Crunch': 'Marshall JVM410H Clean and Crunch channels.',
      DSL100: 'Marshall DSL100, both channels with Clean/Crunch and Lead 1/Lead 2.',
      TSL100: 'Marshall TSL100, the three-channel JCM2000: Clean, Crunch and Lead.',
      'JMP-1': "Marshall JMP-1 preamp (Clean and OD) into the JCM900 4100's power amp.",
      'Quick Rod': 'Splawn Quick Rod, both channels with its gear switches (the power amp is assumed).',
      AFD100: 'Marshall AFD100 (Slash), #34 and AFD modes.',
      'JCM800 2210':
        'Marshall JCM800 2210, the 100 W two-channel JCM800: a diode-clipped Boost channel and a clean Normal channel (Switch 1).',
      'JCM800 2205': 'Marshall JCM800 2205, the 50 W version of the 2210.',
      RG100ES: 'Randall RG100ES: a solid-state head with a JFET preamp, both channels and the Sustain Boost.',
      'Dual Rectifier 3-Ch Orange':
        'Mesa/Boogie Dual Rectifier three-channel Solo Head, channel 2 (orange), with Raw, Vintage and Modern.',
      'Dual Rectifier Orange':
        'Mesa/Boogie Dual Rectifier (two-channel, 1993), orange channel, with Clean, Normal and Modern styles.',
      'Dual Rectifier 3-Ch Clean':
        'Mesa/Boogie Dual Rectifier three-channel Solo Head, channel 1: Clean, and Pushed for a crunch.',
      'Triple Rectifier Orange': 'Mesa/Boogie Triple Rectifier, channel 2 (orange), with Raw, Vintage and Modern.',
      'Triple Rectifier Clean': 'Mesa/Boogie Triple Rectifier, channel 1: Clean, and Pushed for a crunch.',
      'Sunn Model T': 'Sunn Model T (1973, 150 W, four 6550), Brite and Normal channels: the doom and drone amp.',
      'Super Bass 100':
        'Marshall 1992 Super Bass, 100 W: the Super Lead without its bright caps, darker and fuller, for guitar or bass.'
    },
    pretube: {
      Stock: "The schematic's own preamp tubes.",
      '12AX7': 'The usual high-gain preamp triode (gain about 100).',
      '12AY7': 'Much lower gain (about 45): the original tweed tube, cleaner and more open.',
      '5751': 'About 30 % less gain than a 12AX7: a little more headroom, smoother break-up.',
      '12AT7': 'Lower gain (about 60) and more current.',
      '12AU7': 'The lowest gain (about 20): a much cleaner preamp.'
    },
    powertube: {
      Stock: "The schematic's own power tubes.",
      '6L6GC': 'The big American beam tetrode of Fender and Mesa amps.',
      EL34: 'The British power pentode of Marshall and Hiwatt amps.',
      EL84: 'The small pentode of the AC30, AC15 and 18 W.',
      '6V6GT': 'The small American beam tetrode of the Deluxe and Princeton.',
      KT66: "The JTM45 and Bluesbreaker's tube.",
      KT88: 'A large beam tetrode, as in high-power amps such as the Major.',
      '6550': 'A large beam tetrode, as in the SVT and US-market Marshalls.'
    },
    speaker: {
      Stock: "The model's own speaker load.",
      '1x12 Open': 'A typical open-back 1x12 combo speaker.',
      '2x12 Open': 'A typical open-back 2x12 combo.',
      '4x10 Open': 'A typical open-back 4x10 (Bassman, Super Reverb).',
      '4x12 Closed': 'A typical closed-back 4x12.',
      '8x10 Closed': 'A closed 8x10 bass cabinet.',
      'Follow Cab':
        "The load of the speaker and cabinet in the next Cab block's first unmuted DynaCab slot, from that speaker's own electrical data. With no Cab after the amp, or one playing only IR files, it falls back to Stock."
    },
    supply: {
      Stock: 'As the model has it.',
      DC: "An ideal DC source behind the supply's resistance: sag but no ripple.",
      'AC 60 Hz': 'Rectified 60 Hz mains: 120 Hz ripple on the supply.',
      'AC 50 Hz': 'Rectified 50 Hz mains: 100 Hz ripple on the supply.'
    },
    rectifier: {
      Stock: "The model's own rectifier.",
      Silicon: 'Solid-state diodes: almost no drop, the stiffest supply.',
      GZ34: 'The stiffest tube rectifier (about 17 V drop at 250 mA).',
      '5U4': 'A softer tube rectifier (about 44 V at 275 mA).',
      '5Y3': 'The softest (about 60 V at 125 mA): the tweed sag.',
      EZ81: 'The small rectifier of EL84 amps (about 25 V at 150 mA, approximate).'
    },
    boost: {
      Off: 'No boost.',
      'TS808 OD':
        'Ibanez TS808: mid-hump overdrive that cuts the bass going into the amp. The classic high-gain tightener.',
      'Super OD': 'Boss SD-1: asymmetric clipping, a little more edge than the TS.',
      'Chiron OD': 'Klon Centaur: transparent boost and overdrive with a clean blend.',
      'Treble Booster': 'Dallas Rangemaster: a germanium treble booster, the 60s and 70s British lead sound.',
      'Esoteric Boost': 'Xotic RC Booster: a clean, wide boost with an active treble control.',
      'FET Boost': 'A JFET clean boost: wide band, soft compression when pushed.'
    },
    infilter: {
      Off: 'No input filter.',
      'Low Shelf': 'Boosts or cuts everything below the frequency.',
      'High Shelf': 'Boosts or cuts everything above the frequency.',
      Peak: 'Boosts or cuts a band around the frequency; Q sets its width.',
      'High Pass': 'Removes lows below the frequency.',
      'Low Pass': 'Removes highs above the frequency.'
    },
    outeq: {
      Off: 'No graphic EQ.',
      '10 Band': 'Octave bands from 31 Hz to 16 kHz.',
      '10 Band 2/3 Oct': "Narrower bands from 63 Hz to 4 kHz, for the guitar's core range.",
      '8 Band': 'Octave bands from 63 Hz to 8 kHz.',
      '7 Band': 'Octave bands from 100 Hz to 6.4 kHz.',
      '5 Band': '100, 300, 1k, 3k and 8k.',
      '5 Band Mark': "The Mesa Mark series' bands (80, 240, 750, 2200, 6600 Hz), broad and overlapping.",
      '5 Band Passive': 'A passive-style five-band at 100, 400, 1k, 3k and 8k.',
      '4 Band Passive': 'Bass, Middle, Treble and Presence.',
      '3 Band Passive': 'Bass, Middle and Treble.',
      '3 Band Console': "A mixing desk's Low, Mid and High."
    },
    outeqloc: {
      Output: 'After the amp: a plain tone EQ.',
      'Pre Power Amp': 'Between the preamp and the power amp: it shapes what the power tubes distort.',
      Input: 'Before the preamp: it shapes what the preamp distorts.'
    },
    comptype: {
      Off: 'No output compression.',
      Output: "Detects the amp's output: firmer, feed-forward compression.",
      Feedback: 'Detects its own compressed output: smoother, gentler.'
    },
    stacktype: {
      Stock: "The model's own stack.",
      Fender: 'The Fender blackface stack (AB763 Twin and Super values): scooped mids.',
      Marshall: 'The Marshall 1959 stack: more mids, brighter.',
      Vox: 'A Vox Top Boost style stack: bright, with a fixed-feel middle.',
      Prometheus: "The Prometheus model's stack.",
      JCM800: "The JCM800's stack.",
      GH100L: "The Laney GH100L's stack.",
      'Fender AB763 fixed mid': 'The AB763 stack with its middle fixed, as on amps without a middle knob.',
      'Fender AB763': 'The AB763 stack with a middle pot.',
      'Bassman 5F6-A': "The tweed Bassman's stack.",
      'SLO-100': "The SLO-100's stack."
    },
    stackloc: {
      Stock: 'Where the schematic has it.',
      Pre: 'Before the first tube.',
      Mid: 'After the first gain stage.',
      Post: 'After the last preamp stage, into the phase inverter.'
    },
    mvloc: {
      Stock: 'Where the circuit has it.',
      'Post PI': 'As a divider after the phase inverter.',
      'Pre-Triode': 'As a divider ahead of the last preamp gain stage.'
    },
    outmode: {
      FRFR: 'For a Cab block into headphones, monitors or full-range speakers.',
      'SS Power Amp + Cab': 'For a solid-state power amp into a real guitar cab: the speaker stages are switched off.'
    },
    oversampling: {
      Standard: '2x the sample rate (1x at 64 kHz and up), at the usual cost.',
      High: '4x the sample rate (2x at 64 kHz and up): less aliasing, about twice the CPU.'
    }
  },

  tips: `**Gain staging inside the amp.** Gain sets the preamp distortion, Master the power amp distortion. For a
tight modern sound keep Master moderate and let the preamp do the work. For a vintage, "cranked amp" feel, keep the
preamp lower and push Master (or pick an amp without a master), then use Sag, Bias and Feedback to shape how the power
amp breaks up. Set the final loudness with Level, not Master. More in [gain staging](/manual/gain-staging).

**Tightening high gain.** In order of effect: *Low Cut* or *Input Cut* at the input, a TS808 boost with Drive low and
Level up, Bass down on the amp, then *Definition* up a little. Cutting bass after the amp does not tighten it; cutting
it before the distortion does.

**Fizz and harshness.** Try Presence and Treble down first, then *High Treble* below 5, then the Cab block's High
Cut around 6 to 8 kHz, and different mic positions. On the SLO-100 and 5150III, *Triode 2 Plate Freq* below 100 %
smooths the preamp's top.

**Too much low-end mud with a low-feedback amp?** The speaker load's resonance is doing its job. Lower *Load peak* or
raise *Feedback* a little, or set *Speaker load* to *Follow Cab* so the load matches the cab you hear.

**Feel.** *Sag*, *Preamp Sag*, the *Rectifier*, *Variac* and the bias excursion controls change how the amp responds
to your picking more than how it sounds on a recording. Adjust them while playing, not while looking at a spectrum.

**Stock is right.** Every expert control at its default is the schematic as drawn. If you get lost, change *Type*
away and back: every knob returns to the model's stock setting.

**Differences from the Axe-Fx.** Cathode's expert controls scale real components (a cap, a resistor, a tube's curve)
rather than behaviour, and a control the model has no part for is hidden and does nothing. Some models rest on
assumed values where the schematic was incomplete or wrong (the Powerball's preamp, taken from a hand-corrected copy
of the factory drawing, the Fireball's two extra supply filter nodes, the Prometheus as a whole, several output
transformers); the model descriptions say where.

**CPU.** The amp runs a full circuit at twice the sample rate (at rates below 64 kHz), so it is the heaviest block in
most presets, and the high-gain models are the heaviest of all. Two amps in one preset is fine on most computers; see
[troubleshooting](/manual/troubleshooting) if you hear dropouts.`
};
