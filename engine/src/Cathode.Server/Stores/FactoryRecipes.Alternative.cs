namespace Cathode.Server.Stores;

public static partial class FactoryRecipes {
    static IEnumerable<FactoryRecipe> Alternative() => [
        Preset(
                "Smells Like Teen Spirit",
                "Kurt Cobain in 1991: a humbucker guitar into a DS-1 distortion and a Mesa preamp's clean channel "
                + "(the Lone Star's here), with a deep chorus for the verse. Loud chorus, quiet verse.",
                "Grunge",
                "1990s",
                "Crunch"
            )
            .Input("type=Classic threshold=-58")
            .Drive("DS1 Distortion", "drive=7 tone=4 level=0")
            .Add("chorus", "rate=1 depth=70 mix=50")
            .Off()
            .Amp("Lone Star", "gain=5 bass=5 middle=5 treble=6 master=6")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=0.9 size=45 mix=8")
            .Scene("Chorus")
            .Scene("Verse", "-drive +chorus")
            .Scene("Solo", "", 1),
        Preset(
                "Cherub Rock",
                "Billy Corgan in 1993: a Strat into an op-amp Big Muff and a Marshall, layered into a fuzz wall. The "
                + "Clean scene drops the fuzz for the intro.",
                "Alternative",
                "1990s",
                "Fuzz"
            )
            .Drive("Pi Fuzz", "drive=7 tone=5 level=0")
            .Amp("JCM800 2203", "gain=1 bass=5 middle=6 treble=6 presence=5 master=7")
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=10")
            .Scene("Fuzz Wall")
            .Scene("Clean", "-drive"),
        Preset(
                "Where the Streets Have No Name",
                "The Edge in 1987: a Strat into a Vox AC30 through an analog delay set to dotted eighths at the "
                + "song's tempo, so every picked eighth note comes back between the beats. Mute lightly and keep "
                + "strict time.",
                "Alternative",
                "1980s",
                "Clean"
            )
            .Tempo(126)
            .Amp("AC30", "gain=1.25 bass=4 treble=7 cut=3")
            .Cab("2x12 Open", "Alnico Blue", "SM57", "m1position=30 m1distance=1")
            .Add("delay", "type=Analog tempo=1/8. feedback=35 mix=40")
            .Add("reverb", "type=Hall decay=2.4 mix=15")
            .Scene("Delay")
            .Scene("Dry", "-delay"),
        Preset(
                "How Soon Is Now",
                "Johnny Marr in 1985: a guitar into Fender Twin Reverbs whose vibrato throbs in time with the song, "
                + "layered and drenched. Strum the open-string drone; the tremolo locks to the tempo.",
                "Alternative",
                "1980s",
                "Clean"
            )
            .Tempo(96)
            .Amp("Twin Reverb", "gain=5 bass=4 middle=5 treble=6 bright=1")
            .Cab("2x12 Open", "D120F", "SM57", "m1position=30 m1distance=2")
            .Add("tremolo", "type=Optical_Trem tempo=1/16 depth=90")
            .Add("reverb", "type=Spring springs=3 decay=2.2 mix=24")
            .Scene("Tremolo")
            .Scene("Dry", "-tremolo"),
        Preset(
                "Creep",
                "Radiohead in 1992: a Telecaster into a clean, solid-state Fender (a Twin's clean here), and a "
                + "ShredMaster-type distortion that slams in before the chorus. Kick it on hard.",
                "Alternative",
                "1990s",
                "Crunch"
            )
            .Drive("Shred Distortion", "drive=7 tone=6 level=0")
            .Off()
            .Amp("Twin Reverb", "gain=4 bass=4 middle=5 treble=6")
            .Cab("2x12 Open", "D120F", "SM57", "m1position=30 m1distance=1.5")
            .Add("reverb", "type=Plate decay=1.6 mix=14")
            .Scene("Verse")
            .Scene("Chorus", "+drive"),
        Preset(
                "Seven Nation Army",
                "Jack White in 2003: a hollow-body through an octave-down pitch pedal into a big Silvertone combo (a "
                + "four-ten Super Reverb here), with a Big Muff for the chorus. Single notes on the low strings.",
                "Alternative",
                "2000s",
                "Fuzz"
            )
            .Add("pitch", "type=Whammy whammyStart=-12 whammyEnd=-12 mix=100")
            .Drive("Pi Fuzz", "drive=6 tone=5 level=0")
            .Off()
            .Amp("Super Reverb", "gain=6 bass=5 middle=6 treble=6")
            .Cab("4x10 Open", "P10R", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=0.9 size=45 mix=8")
            .Scene("Riff")
            .Scene("Chorus", "+drive"),
        Preset(
                "Everlong",
                "Dave Grohl in 1997: a semi-hollow Gibson in drop D into a Mesa Dual Rectifier, chords ringing over "
                + "the drone strings. The Verse scene turns the gain down.",
                "Alternative",
                "1990s",
                "Crunch"
            )
            .Input("type=Classic threshold=-58")
            .Amp(
                "Dual Rectifier",
                "gain=4.5 bass=5 middle=6 treble=6 master=5 control2=5",
                "gain=2.5 bass=5 middle=6 treble=6 master=5 control2=5"
            )
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=8")
            .Scene("Chorus")
            .Scene("Verse", "amp=B"),
        Preset(
                "Last Nite",
                "The Strokes in 2001: a guitar into the clean channel of a Fender Hot Rod DeVille turned up until it "
                + "frays, thin and close. The Solo scene switches to the drive channel.",
                "Alternative",
                "2000s",
                "Crunch"
            )
            .Amp(
                "Hot Rod Deluxe",
                "bass=4 middle=6 treble=7 presence=5 master=6 bright=1 switch1=1 control1=7",
                "gain=4 bass=4 middle=6.5 treble=6.5 presence=5 master=6"
            )
            .Cab("4x10 Open", "P10R", "SM57", "m1position=20 m1distance=1")
            .Add("reverb", "type=Room decay=0.7 size=35 mix=6")
            .Scene("Rhythm")
            .Scene("Solo", "amp=B", 2),
        Preset(
                "Killing in the Name",
                "Tom Morello in 1992: a guitar in drop D into a 50-watt Marshall JCM800, with a pitch pedal sweeping "
                + "an octave up for the solo (an expression pedal on External 1 rocks it).",
                "Alternative",
                "Metal",
                "1990s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-55")
            .Add("pitch", "type=Whammy whammyStart=0 whammyEnd=12 mix=100")
            .Pedal("pedal", 0, 100)
            .Off()
            .Amp("JCM800 2204", "gain=7 bass=6 middle=5 treble=6 presence=5 master=5")
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Analog time=300 feedback=20 mix=12")
            .Off()
            .Add("reverb", "type=Room decay=0.9 size=45 mix=8")
            .Scene("Riff")
            .Scene("Solo", "+pitch +delay", 2),
        Preset(
                "Basket Case",
                "Billie Joe Armstrong in 1994: a humbucker Strat-style guitar into a hot-rodded 100-watt Marshall, "
                + "pushed by a boost. Fast downstrokes, palm-muted verses.",
                "Punk",
                "1990s",
                "High Gain"
            )
            .Input("type=Classic threshold=-58")
            .Drive("FET Boost", "drive=5 tone=6 level=4")
            .Amp("Plexi 100W", "gain=8 bass=5 middle=7 treble=6 presence=5 switch1=1 control1=5")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=0.8 size=40 mix=6")
            .Scene("Rhythm"),
        Preset(
                "Message in a Bottle",
                "Andy Summers in 1979: a Telecaster into a Marshall turned down to just-clean, with a chorus and a "
                + "tape echo filling out the arpeggios. The Chorus scene turns the amp up for the power chords.",
                "New Wave",
                "1970s",
                "Clean"
            )
            .Amp(
                "JCM800 2203",
                "gain=1 bass=5 middle=6 treble=6 presence=5 master=7",
                "gain=6 bass=5 middle=6 treble=6 presence=5 master=7"
            )
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=30 m1distance=1")
            .Add("chorus", "rate=0.7 depth=45 mix=40")
            .Add("delay", "type=Tape time=340 feedback=25 mix=20")
            .Add("reverb", "type=Plate decay=1.6 mix=12")
            .Scene("Arpeggio")
            .Scene("Chorus", "amp=B -chorus")
    ];

    static IEnumerable<FactoryRecipe> Atmospheric() => [
        Preset(
                "Only Shallow",
                "Kevin Shields in 1991: an offset guitar with the tremolo arm held while strumming, into fuzz, a "
                + "reverse effect and a huge hall, through a Vox. Bend the whole chord with the arm as you play.",
                "Shoegaze",
                "1990s",
                "Fuzz"
            )
            .Drive("Pi Fuzz", "drive=8 tone=5 level=0")
            .Amp("AC30", "gain=1 bass=5 treble=6 cut=4")
            .Cab("2x12 Open", "Alnico Blue", "SM57", "m1position=30 m1distance=2")
            .Add("delay", "type=Reverse time=420 feedback=20 mix=35")
            .Add("reverb", "type=Hall decay=5 size=90 mix=35")
            .Scene("Glide")
            .Scene("Clean Glide", "-drive"),
        Preset(
                "When the Sun Hits",
                "Slowdive in 1993: a clean Fender combo washed in chorus, long delay and a cathedral-sized reverb, "
                + "with a mild overdrive that swells the chorus. Strum slowly and let everything blur.",
                "Shoegaze",
                "1990s",
                "Ambient"
            )
            .Drive("Blues OD", "drive=6 tone=5 level=0")
            .Off()
            .Amp("Deluxe Reverb", "gain=5 bass=5 treble=6")
            .Cab("1x12 Open", "C12N", "SM57", "m1position=30 m1distance=2")
            .Add("chorus", "rate=0.3 depth=50 mix=40")
            .Add("delay", "type=Digital time=500 feedback=45 mix=28")
            .Add("reverb", "type=Cathedral decay=6 mix=35")
            .Scene("Verse")
            .Scene("Chorus", "+drive"),
        Preset(
                "Heaven or Las Vegas",
                "Robin Guthrie in 1990: a clean guitar through chorus and stereo delays into a glassy solid-state "
                + "amp (the JC-120 here) and a long hall. Chime the chords high on the neck.",
                "Dream Pop",
                "1990s",
                "Ambient"
            )
            .Amp("JC-120", "gain=4 bass=5 middle=5 treble=6 control1=6")
            .Cab("2x12 Open", "D120F", "SM57", "m1position=35 m1distance=2")
            .Add("chorus", "rate=0.8 depth=60 mix=50")
            .Add("delay", "type=Dual time=375 timeRight=500 feedback=40 feedbackRight=40 mix=32")
            .Add("reverb", "type=Hall decay=4.5 mix=30")
            .Scene("Shimmer"),
        Preset(
                "Svefn-g-englar",
                "Sigur Rós in 1999: a Les Paul played with a cello bow into a Vox AC30, a long delay and a "
                + "cathedral. The Bowed scene swells every note in (no bow needed); the Picked scene plays them as "
                + "you strike.",
                "Ambient",
                "Post-Rock",
                "1990s"
            )
            .Add("volpan", "type=Auto-Swell threshold=-45 attack=700")
            .Amp("AC30", "gain=6.5 bass=5 treble=5 cut=5")
            .Cab("2x12 Open", "Alnico Blue", "SM57", "m1position=35 m1distance=2")
            .Add("delay", "type=Digital time=700 feedback=50 mix=30")
            .Add("reverb", "type=Cathedral decay=8 mix=40")
            .Scene("Bowed")
            .Scene("Picked", "-volpan")
    ];
}
