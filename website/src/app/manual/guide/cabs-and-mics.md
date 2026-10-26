An amp on its own sounds harsh and buzzy: the real thing is always heard through a speaker in a cabinet, and on a record through a microphone in front of it. The Cab block is that speaker, cabinet and mic. It changes the sound more than any EQ, so time spent here pays back on every preset.

The Cab block has four mic slots (**Mic 1** to **Mic 4**). Each slot plays one of four sources:

- **DynaCab**: a modelled speaker in a modelled cabinet, with a mic you place freely across the cone and away from the grille.
- **IR**: an impulse response file (a `.wav`), from the Library.
- **IR Pack**: a multi-position IR pack (a `.cabgrid`), which you move across like a DynaCab.
- **Flat**: no cabinet at all, a straight line through, still shaped by the slot's own cuts.

![The Cab block: four microphones placed across the speaker, and Mic 1's cabinet, speaker, microphone, position, distance and angle](screenshots/cab.webp)

## Speakers

A DynaCab slot has ten speakers, each built from its maker's published data and frequency chart:

| Speaker                           | Character                                                                              | Typical use                            |
| --------------------------------- | -------------------------------------------------------------------------------------- | -------------------------------------- |
| **Celestion Greenback (G12M)**    | Warm, a broad hump around 2 kHz, a soft top that falls away above 5 kHz; loose low end | Plexis, JTM45, classic rock and blues  |
| **Celestion Creamback (G12M-65)** | The Greenback's voice with a tighter low end and a smoother top                        | Classic rock at higher gain, Hiwatts   |
| **Celestion G12H30 Anniversary**  | Tighter bass and brighter upper-mid peaks than the Greenback                           | 1970s rock, Laney, Marshall Major      |
| **Celestion G12T-75**             | Scooped around 1 to 1.5 kHz, strong peaks at 3 and 5 kHz: the 1980s sizzle             | JCM800, 5150, 1980s hard rock          |
| **Celestion Vintage 30**          | A strong upper-mid peak around 2.5 kHz, tight bass, aggressive                         | Mesa, high gain, modern rock and metal |
| **Celestion Alnico Blue**         | Light and chiming, loose bass, more top than the ceramic Celestions                    | Vox AC30 and AC15, jangle              |
| **Jensen P10R**                   | A ten-inch alnico: less low end, early breakup, a bright 3 kHz peak                    | Tweed and blackface 4x10s and 1x10s    |
| **Jensen C12N**                   | Flatter mids than the Celestions, an extended top: the Fender clean voice              | Deluxe Reverb and other Fender combos  |
| **Electro-Voice EVM12L**          | Flat, clean, hi-fi, a deep tight low end                                               | Loud clean Fenders, Mesa combos, jazz  |
| **JBL D120F**                     | Light cone and heavy magnet: clear, sparkling, firm bass                               | Twin Reverb cleans, country, funk      |

> **Note:** The JBL D120F's low end is based on JBL's later E120, because no D120F data sheet was ever published.

## Cabinets

Seven cabinets: **1x10 Open Back**, **1x12 Open Back**, **1x12 Closed Back**, **2x12 Open Back**, **2x12 Closed Back**, **4x10 Open Back** and **4x12 Closed Back**.

- **Open back** cabinets radiate from the back too. The back wave partly cancels the front at low frequencies, so the bass is lighter and less focused, and the sound is more open and airy. The classic combo sound: Fender, Vox.
- **Closed back** cabinets trap the back wave. The low end is tighter and punchier, with box resonances in the low mids. A closed 4x12 is the sound of rock and metal.
- **Size** changes the low end and the box's resonances: a 4x12 has a big, deep thump; a 1x12 a smaller, focused one.

Pair the cab with the amp first by what the real amp shipped with or was usually played through (the [amp catalogue](/manual/choosing-an-amp) and [tone recipes](/manual/tone-recipes) give typical pairs), then break the rules if it sounds better.

> **Tip:** On the Amp block, set **Speaker load** to **Follow Cab**. The amp's power section then sees the electrical load of the speaker and cabinet chosen in the Cab, so the two agree. See [Choosing and dialling an amp](/manual/choosing-an-amp#speaker).

## Microphones

Eleven mics:

| Mic                            | Type                   | Character                                                                                            |
| ------------------------------ | ---------------------- | ---------------------------------------------------------------------------------------------------- |
| **Dynamic 57 (SM57)**          | Dynamic, cardioid      | The standard: falls off below about 200 Hz, a presence lift near 5 to 6 kHz. Forward and mid-focused |
| **Dynamic 421 (MD 421)**       | Dynamic, cardioid      | Fuller low end than the 57, a gentle 4 to 5 kHz lift                                                 |
| **Dynamic 906 (e 906)**        | Dynamic, supercardioid | Made for guitar cabs: a 4 kHz presence peak and some air                                             |
| **Dynamic 609 (e 609)**        | Dynamic, supercardioid | Mid-forward, peaks at 3 and 6 kHz                                                                    |
| **Dynamic Beta 57 (Beta 57A)** | Dynamic, supercardioid | A slightly fuller, brighter cousin of the 57                                                         |
| **Ribbon 121 (R-121)**         | Ribbon, figure-8       | Flat mids, a smooth, gently falling top, strong proximity bass. The classic partner for a 57         |
| **Ribbon 4038 (Coles 4038)**   | Ribbon, figure-8       | Darker than the R-121, its top rolls off earlier. Vintage and warm                                   |
| **Ribbon 160 (M 160)**         | Ribbon, hypercardioid  | Smooth, more top than the other ribbons                                                              |
| **Condenser 87 (U 87)**        | Condenser, cardioid    | Detailed and open, a broad lift around 10 kHz. Good at a distance                                    |
| **Condenser 414 (C414 XLS)**   | Condenser, cardioid    | Very flat and extended                                                                               |
| **Measurement Omni**           | Omni                   | A flat reference, with no proximity effect                                                           |

**Proximity effect**: directional mics (all but the omni) boost the low end when they are close to the source, more the closer they get. Ribbons, being figure-8, show it most. In a DynaCab slot the **Distance** sets it automatically.

## Placing the microphone

Drag a mic's dot across the speaker picture, or set the slot's knobs.

### Position: cap to edge

**Position** runs from 0 % (the centre of the dust cap) to 100 % (the cone's edge).

- **On the cap (0 to 15 %)**: brightest and most aggressive. Can be harsh or fizzy with distortion.
- **The cap's edge (20 to 35 %)**: the usual starting point. Bright and clear but with body.
- **On the cone (40 to 70 %)**: warmer, rounder, fewer upper mids.
- **Near the edge (70 to 100 %)**: dark and thick, with less definition.

Position works because a speaker's cone stops moving as one piece at high frequencies: the treble comes mostly from the middle. Moving outward is the most natural tone control you have.

### Distance

**Distance** is from the grille cloth, 0 to 12 inches.

- **0 to 1 inch**: close and direct, most proximity bass. The sound of most rock and metal records.
- **2 to 4 inches**: a little less bass, a slightly more blended picture of the speaker.
- **6 to 12 inches**: the speaker blends with the cabinet; with the Room on, more of the room. Good for a condenser as a second mic.

### Angle

**Angle** tilts the mic off the speaker's axis, up to 60 degrees. Off-axis, the treble falls: a cheap fix for harshness without moving the mic outward. 15 to 30 degrees is a useful range.

> **Tip:** For fizz or harshness, move outward before you angle, and angle before you reach for an EQ.

## Mixing microphones

Unmute more slots to blend mics, exactly as engineers use two or three mics on one cab. Common pairs:

- **SM57 near the cap edge + R-121 a little further out.** The 57 brings bite and definition, the ribbon body and a smooth top. Start with the ribbon 3 to 6 dB below the 57. The Factory presets "Enter Sandman Rhythm" and "Slow Dancing in a Burning Room" do this.
- **A close dynamic + a condenser at 6 to 12 inches.** Adds air and size.
- **Two speakers.** Different speakers in two slots (a Vintage 30 and a Greenback is a classic pair) blend their voices.

The **Level** of each slot is relative: the levels of the slots that play are scaled to sum to unity, so they set the blend, not the loudness. The block's own **Level** sets the loudness. **Pan** places each mic in the stereo field: two different mics panned left and right make a wide stereo cab.

**Mute** and **Solo** let you hear one mic at a time. Editing a muted slot turns it on.

### Phase and alignment

Two mics at different distances hear the speaker at slightly different times. Summed, some frequencies cancel and others add: comb filtering, which can sound hollow, thin or phasey. Sometimes that is the sound you want; usually not.

- **Auto-Align** (on by default) lines up the modelled slots, as though every mic were the same distance away. Turn it off to hear the real time-of-flight differences.
- **Delay** shifts a slot by up to about 10 ms, shown as both time and mic distance. Small amounts (fractions of a millisecond) change the blend's tone a lot.
- **Invert** flips a slot's polarity. If a blend sounds thin, try inverting one mic: if the low end comes back, they were fighting.
- The **Align** view, beside **Response** under the speaker picture, shows each slot's impulse on a shared time line. Drag a waveform sideways to set its Delay, nudge it, or line every slot up with the latest one.

<!-- screenshot wanted: the Cab block's Align view with two mics (an SM57 and an R-121) and their onsets marked -->

> **Tip:** Judge a blend with all mics playing, not by soloing. Phase problems only show when the mics are summed.

## Room

The **Room** page adds the room around the cabinet: **Room** (how much), **Room Size**, **Room Shape**, **Floor Reflection**, **Room HF Damping**, **Room LF Damping**, **Room Diffusion**, and **Room Mic Spacing**, which hears the room through a stereo pair that far apart (the close mics stay centred). A little room makes a close-miked cab sound less "in your face" and more like a recording; a lot sounds like an ambience mic. IR files and packs carry their own room, so the Room page shapes the DynaCab slots.

**Air** mixes a little of the signal above **Air Freq** back in, as the air between the speaker and a mic; it adds openness to the top.

## Low and high cut

Every cab has a **Low Cut** and a **High Cut** (with their slopes, 6 to 24 dB/oct), and so does each slot.

- **Low cut, 60 to 100 Hz**: removes rumble no real cab in a room produces, and leaves room for the bass guitar. Raise it to 100 to 150 Hz for a band mix.
- **High cut, 6 to 10 kHz**: removes fizz above what a guitar speaker really reproduces. Lower for dark, vintage sounds; higher for clean, bright ones.

Each slot also has **Proximity** and **Proximity Freq** (on IR, IR Pack and Flat slots; a DynaCab sets its own from its Distance), **Smoothing** (up to an octave, to tame a spiky IR), and **IR Length**.

## The rest of the Cab block

- **Input**: Left, Right, their Sum, or **Stereo**, where slots 1 and 3 hear the left input and slots 2 and 4 the right.
- **Motor Drive**: the voice coil heating under power and compressing the sound, with **Motor Time Constant**. The **Motor** meter shows how much it is reducing the level.
- **Preamp** page: a mic preamp (**Tube**, **Transformer**, **Tape** and others), with **Preamp Drive**, **Preamp Saturation** and a three-band tone. The **Preamp in** meter shows how close it is to clipping. **Preamp Mode High Quality** runs it oversampled.
- **Export IR** and **Save as IR** turn the whole mixed block into one IR file, as a download or straight into the Library.

## Impulse responses

An impulse response (IR) is a recording of how a real speaker, cabinet, mic and room respond, captured by playing a test signal through them. Cathode plays IRs in the Cab block (set a slot's **Source** to **IR** and pick the file) and longer, non-cabinet IRs (acoustic guitar bodies, rooms) in the **IR Player** block. You can import, trim, mix and capture your own: see [Neural captures and impulse responses](/manual/nam-and-irs).

> **Note:** On the Global page, **Cab modeling off** makes every Cab and IR Player block pass the signal straight through, without changing any preset. Use it when you play through a real guitar cabinet.
