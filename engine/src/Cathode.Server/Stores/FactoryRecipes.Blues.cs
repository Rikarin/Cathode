namespace Cathode.Server.Stores;

public static partial class FactoryRecipes {
    static IEnumerable<FactoryRecipe> Blues() => [
        Preset(
                "Texas Flood",
                "Stevie Ray Vaughan's 1983 sound: a loud blackface Vibroverb with a big EV speaker, heavy strings "
                + "and a screamer kept at low drive and high level. Dig in with the pick; the Lead scene adds the "
                + "pedal.",
                "Blues",
                "1980s",
                "Crunch"
            )
            .Drive("TS808 OD", "drive=1.5 tone=6 level=6")
            .Off()
            .Amp("Vibroverb AB763", "gain=6 bass=3.5 treble=6.5 bright=1")
            .Cab("1x12 Open", "EVM12L", "SM57", "m1position=20 m1distance=1.5")
            .Add("reverb", "type=Spring springs=3 decay=1.8 mix=16")
            .Scene("Rhythm")
            .Scene("Lead", "+drive", 2),
        Preset(
                "Lenny",
                "Stevie Ray Vaughan's ballad tone: a clean Super Reverb on the neck pickup, mixed with a "
                + "rotating-baffle speaker. Play it soft and let the chords ring; switch the rotor fast for the "
                + "swells.",
                "Blues",
                "1980s",
                "Clean"
            )
            .Amp("Super Reverb", "gain=3.5 bass=4 middle=5 treble=6 bright=1")
            .Cab("4x10 Open", "P10R", "SM57", "m1position=30 m1distance=2")
            .Add("rotary", "speed=Slow mix=55 hornDepth=40 drumDepth=35", "speed=Fast mix=55 hornDepth=40 drumDepth=35")
            .Add("reverb", "type=Spring springs=3 decay=2 mix=20")
            .Scene("Rotor Slow")
            .Scene("Rotor Fast", "rotary=B")
            .Scene("Dry", "-rotary"),
        Preset(
                "The Thrill Is Gone",
                "B.B. King in 1969: a semi-hollow humbucker guitar into a clean Fender Twin, middle up, with a "
                + "little plate. Vibrato with the wrist and leave space; the Lead scene pushes the amp a touch "
                + "harder.",
                "Blues",
                "1960s",
                "Clean"
            )
            .Amp("Twin Reverb", "gain=4.5 bass=3.5 middle=7 treble=5.5", "gain=6 bass=3.5 middle=7.5 treble=5.5")
            .Cab("2x12 Open", "D120F", "SM57", "m1position=35 m1distance=2")
            .Add("reverb", "type=Plate decay=1.6 mix=14")
            .Scene("Clean")
            .Scene("Lead", "amp=B", 2),
        Preset(
                "Hideaway",
                "Eric Clapton with the Bluesbreakers in 1966: a Les Paul into a Marshall 1962 combo turned all the "
                + "way up. Bridge pickup for the bite; roll the guitar's volume back for the rhythm parts.",
                "Blues",
                "1960s",
                "Crunch"
            )
            .Amp(
                "Bluesbreaker 1962",
                "gain=6.5 bass=4 middle=7 treble=6.5 presence=5",
                "gain=9 bass=4 middle=7.5 treble=6.5 presence=5.5"
            )
            .Cab("2x12 Open", "Greenback", "SM57", "m1position=30 m1distance=2")
            .Add("reverb", "type=Room decay=0.8 size=40 mix=10")
            .Scene("Rhythm")
            .Scene("Lead", "amp=B", 2),
        Preset(
                "Still Got the Blues",
                "Gary Moore in 1990: a Les Paul into a JTM45-style Marshall pushed by a Guv'nor-type distortion, "
                + "with a warm delay and hall for the long, singing bends of the solo.",
                "Blues",
                "1990s",
                "Lead"
            )
            .Input("type=Classic threshold=-62")
            .Drive("Governor Dist", "drive=6 tone=5 level=2 stackBass=5 stackMid=6")
            .Off()
            .Amp("JTM45", "gain=6 bass=4 middle=7 treble=6 presence=5 switch1=1 control1=5")
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Analog time=420 feedback=25 mix=18")
            .Off()
            .Add("reverb", "type=Hall decay=2.2 mix=15")
            .Scene("Rhythm")
            .Scene("Lead", "+drive +delay", 2),
        Preset(
                "Talk to Your Daughter",
                "Robben Ford in the late eighties: a Dumble Overdrive Special, its clean channel for the comping and "
                + "the overdrive for the horn-like lines. Light touch: the amp compresses and sings on its own.",
                "Blues",
                "1980s",
                "Lead"
            )
            .Amp(
                "ODS 70s",
                "gain=4 bass=4 middle=5 treble=5.5 presence=4",
                "gain=4 bass=4 middle=6 treble=5 presence=4 switch1=1 control1=5.5 control2=5"
            )
            .Cab("2x12 Open", "EVM12L", "SM57", "m1position=30 m1distance=2")
            .Add("delay", "type=Digital time=340 feedback=20 mix=12")
            .Off()
            .Add("reverb", "type=Plate decay=1.8 mix=14")
            .Scene("Clean")
            .Scene("Lead", "amp=B +delay", 2),
        Preset(
                "Slow Dancing in a Burning Room",
                "John Mayer in 2006: a big, clean Dumble-style amp on a single-coil guitar, with a transparent "
                + "Klon-type drive for the solo. Keep the touch light in the verses; dig in for the lead.",
                "Blues",
                "2000s",
                "Clean"
            )
            .Drive("Chiron OD", "drive=3 tone=6 level=4")
            .Off()
            .Amp("ODS 97", "gain=5 bass=4.5 middle=5 treble=6 presence=5")
            .Cab("2x12 Open", "EVM12L", "SM57", "m1position=35 m1distance=2 m2mute=0 m2mic=R-121 m2level=-4")
            .Add("delay", "type=Analog time=400 feedback=18 mix=12")
            .Off()
            .Add("reverb", "type=Hall decay=2 mix=16")
            .Scene("Clean")
            .Scene("Lead", "+drive +delay", 2),
        Preset(
                "Ice Pickin'",
                "Albert Collins' icy Telecaster: a Fender Quad Reverb (a Twin with four twelves) with the treble and "
                + "the bright switch up and lots of volume. Play with the fingers, snap the strings, and use a capo.",
                "Blues",
                "1970s",
                "Crunch"
            )
            .Amp(
                "Twin Reverb",
                "gain=5 bass=3 middle=5 treble=8 bright=1",
                "gain=7 bass=3 middle=5.5 treble=7.5 bright=1"
            )
            .Cab("2x12 Open", "D120F", "SM57", "m1position=15 m1distance=1")
            .Add("reverb", "type=Spring springs=3 decay=2 mix=22")
            .Scene("Rhythm")
            .Scene("Lead", "amp=B", 2)
    ];
}
