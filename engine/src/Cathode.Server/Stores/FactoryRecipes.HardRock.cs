namespace Cathode.Server.Stores;

public static partial class FactoryRecipes {
    static IEnumerable<FactoryRecipe> HardRock() => [
        Preset(
                "Brown Sound",
                "Eddie Van Halen's 1978 tone: a home-built humbucker guitar into a 100-watt Marshall Super Lead with "
                + "every knob up and its mains voltage turned down, a phase pedal in front and a tape echo after.",
                "Hard Rock",
                "1970s",
                "Crunch"
            )
            .Amp("Plexi 100W", "gain=10 bass=5 middle=7 treble=7.5 presence=5 control1=0 variac=85")
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("phaser", "stages=4 rate=0.5 depth=80 center=700 mix=50")
            .Off()
            .Add("flanger", "rate=0.12 depth=80 delay=1.5 feedback=55 mix=50")
            .Off()
            .Add("delay", "type=Tape time=330 feedback=15 mix=14")
            .Off()
            .Add("reverb", "type=Plate decay=1.8 mix=15")
            .Scene("Rhythm")
            .Scene("Lead", "+delay", 2)
            .Scene("Phaser", "+phaser")
            .Scene("Flanger", "+flanger"),
        Preset(
                "Sweet Child O' Mine",
                "Slash in 1987: a Les Paul copy into a hot-rodded Marshall, the sound the AFD100 was built from. The "
                + "Intro scene is the amp's milder mode for the neck-pickup riff; the Solo scene adds wah and delay.",
                "Hard Rock",
                "1980s",
                "Lead"
            )
            .Input("type=Classic threshold=-60")
            .Add("wah", "type=Cry_Baby position=50")
            .Pedal("position", 0, 100)
            .Off()
            .Amp(
                "AFD100",
                "gain=7 bass=5 middle=7 treble=6 presence=5 master=6 switch1=1",
                "gain=6 bass=5 middle=6.5 treble=6.5 presence=5 master=6"
            )
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Analog time=360 feedback=25 mix=16")
            .Off()
            .Add("reverb", "type=Plate decay=1.6 mix=12")
            .Scene("Rhythm")
            .Scene("Intro", "amp=B")
            .Scene("Solo", "+wah +delay", 2),
        Preset(
                "Crazy Train",
                "Randy Rhoads in 1980: a Les Paul Custom into an MXR-style distortion pedal and a 100-watt Marshall, "
                + "shaped by a ten-band EQ. The Solo scene adds a short echo.",
                "Hard Rock",
                "Metal",
                "1980s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-58")
            .Drive("Plus Distortion", "drive=6 level=2")
            .Amp("1959SLP", "gain=7 bass=5 middle=7 treble=6 presence=5 switch1=1 control1=5")
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=20 m1distance=1")
            .Add("geq", "type=10_Band g125=-2 g500=2 g1k=3 g2k=2 g8k=-2")
            .Add("delay", "type=Digital time=380 feedback=25 mix=16")
            .Off()
            .Add("reverb", "type=Plate decay=1.5 mix=12")
            .Scene("Riff")
            .Scene("Solo", "+delay", 2),
        Preset(
                "Still of the Night",
                "John Sykes in 1987: a Les Paul into a hot Marshall JCM800, pushed by a screamer for a tight, "
                + "saturated rhythm. Palm-mute the riff; the Solo scene adds delay.",
                "Hard Rock",
                "1980s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-56")
            .Drive("TS9 OD", "drive=1 tone=6 level=6")
            .Amp("JCM800 2203", "gain=8 bass=5 middle=6 treble=7 presence=6 master=5")
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=20 m1distance=1")
            .Add("delay", "type=Digital time=400 feedback=28 mix=18")
            .Off()
            .Add("reverb", "type=Hall decay=1.8 mix=12")
            .Scene("Riff")
            .Scene("Solo", "+delay", 2),
        Preset(
                "Surfing with the Alien",
                "Joe Satriani in 1987: a superstrat into a DS-1 type distortion and a Marshall at a modest gain, "
                + "with a wah for the alien squeals. Legato everything; the Wah scene puts the pedal in.",
                "Hard Rock",
                "1980s",
                "Lead"
            )
            .Input("type=Intelligent threshold=-58")
            .Add("wah", "type=Cry_Baby position=50")
            .Pedal("position", 0, 100)
            .Off()
            .Drive("DS1 Distortion", "drive=6 tone=5 level=2")
            .Amp("JCM800 2203", "gain=5 bass=5 middle=6 treble=6 presence=5 master=6")
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Digital time=360 feedback=25 mix=15")
            .Add("reverb", "type=Hall decay=2 mix=14")
            .Scene("Lead")
            .Scene("Wah", "+wah"),
        Preset(
                "Cliffs of Dover",
                "Eric Johnson in 1990: a Strat into a screamer and a 50-watt Marshall, with a tape echo, for that "
                + "violin-like lead. The Clean scene turns the amp down for the intro's chords.",
                "Hard Rock",
                "1990s",
                "Lead"
            )
            .Drive("TS808 OD", "drive=4 tone=5 level=4")
            .Amp(
                "1987X",
                "gain=6 bass=4 middle=7 treble=6 presence=4 switch1=1 control1=4",
                "gain=0.5 bass=4.5 middle=6 treble=6 presence=4 switch1=1 control1=0.5"
            )
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=35 m1distance=2")
            .Add("delay", "type=Tape time=375 feedback=15 mix=15")
            .Add("reverb", "type=Hall decay=2 mix=14")
            .Scene("Lead")
            .Scene("Clean", "-drive amp=B"),
        Preset(
                "Building the Church",
                "Steve Vai in 2005: his signature Carvin Legacy on the lead channel, with a wah, a long delay and a "
                + "big hall. Whammy-bar dives and wah vowels; the Wah scene engages the pedal.",
                "Hard Rock",
                "2000s",
                "Lead"
            )
            .Input("type=Intelligent threshold=-58")
            .Add("wah", "type=Morley position=50")
            .Pedal("position", 0, 100)
            .Off()
            .Amp("Legacy VL100", "gain=7 bass=5 middle=6 treble=6 master=6 control1=5")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Digital time=420 feedback=30 mix=20")
            .Add("reverb", "type=Hall decay=3 mix=18")
            .Scene("Lead")
            .Scene("Wah", "+wah"),
        Preset(
                "Poundcake",
                "Eddie Van Halen in 1991: a Music Man guitar into a Soldano SLO-100 on its overdrive channel, thick "
                + "and smooth. The Lead scene adds a short delay.",
                "Hard Rock",
                "1990s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-56")
            .Amp("SLO-100", "gain=7 bass=5 middle=6 treble=6 presence=5 depth=5 master=5")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Digital time=350 feedback=20 mix=14")
            .Off()
            .Add("reverb", "type=Plate decay=1.6 mix=12")
            .Scene("Rhythm")
            .Scene("Lead", "+delay", 2),
        Preset(
                "Don't Tell Me (What Love Can Do)",
                "Eddie Van Halen in 1995: his own Peavey 5150 on the lead channel, darker and heavier than the brown "
                + "sound of the seventies. The Lead scene adds delay.",
                "Hard Rock",
                "1990s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-55")
            .Amp("5150", "gain=6 bass=5 middle=5 treble=6 presence=5 depth=5 master=4")
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Digital time=380 feedback=20 mix=14")
            .Off()
            .Add("reverb", "type=Plate decay=1.6 mix=12")
            .Scene("Rhythm")
            .Scene("Lead", "+delay", 2),
        Preset(
                "Tom Sawyer",
                "Alex Lifeson in 1981: a semi-hollow Gibson into Hiwatt heads, with a chorus spreading the chords. "
                + "The Solo scene adds an echo for the wild lead.",
                "Prog",
                "Hard Rock",
                "1980s",
                "Crunch"
            )
            .Amp("Hiwatt DR504", "gain=6 bass=5 middle=6 treble=6 presence=5 master=7 switch1=1 control1=4")
            .Cab("4x12 Closed", "Creamback", "SM57", "m1position=30 m1distance=1")
            .Add("chorus", "rate=0.6 depth=50 mix=40")
            .Add("delay", "type=Tape time=380 feedback=22 mix=16")
            .Off()
            .Add("reverb", "type=Plate decay=1.6 mix=12")
            .Scene("Riff")
            .Scene("Solo", "+delay", 2)
    ];
}
