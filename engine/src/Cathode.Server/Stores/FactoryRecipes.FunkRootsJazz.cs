namespace Cathode.Server.Stores;

public static partial class FactoryRecipes {
    static IEnumerable<FactoryRecipe> Funk() => [
        Preset(
                "Le Freak",
                "Nile Rodgers in 1978: a Strat on the middle and neck pickups, squeezed by a compressor into a "
                + "clean, bright Fender. Muted sixteenths from the wrist, chords high on the neck.",
                "Funk",
                "Disco",
                "1970s",
                "Clean"
            )
            .Add("compressor", "type=Pedal threshold=-34 ratio=6 attack=3 release=120 makeup=8")
            .Amp("Twin Reverb", "gain=3.5 bass=3 middle=4 treble=7 bright=1")
            .Cab("2x12 Open", "D120F", "SM57", "m1position=20 m1distance=1")
            .Add("reverb", "type=Room decay=0.6 size=35 mix=6")
            .Scene("Chank"),
        Preset(
                "Theme from Shaft",
                "The 1971 wah-wah rhythm: a hollow-body guitar through a wah and a compressor into a clean blackface "
                + "Fender. Rock the pedal in time with the sixteenths (it answers an expression pedal on External "
                + "1).",
                "Funk",
                "Soul",
                "1970s",
                "Clean"
            )
            .Add("wah", "type=V846 position=50")
            .Pedal("position", 0, 100)
            .Add("compressor", "type=Pedal threshold=-32 ratio=4 attack=5 release=150 makeup=6")
            .Amp("Bandmaster AB763", "gain=4 bass=4 treble=7 bright=1")
            .Cab("2x12 Closed", "D120F", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Spring springs=2 decay=1.2 mix=10")
            .Scene("Wah Rhythm")
            .Scene("No Wah", "-wah"),
        Preset(
                "Can't Stop",
                "John Frusciante in 2002: a Strat into a Marshall Silver Jubilee, clean-ish and funky for the muted "
                + "verse, with a distortion pedal for the chorus and a delay for the solo.",
                "Funk",
                "Alternative",
                "2000s",
                "Crunch"
            )
            .Drive("DS1 Distortion", "drive=5 tone=5 level=0")
            .Off()
            .Amp("Silver Jubilee", "gain=4 bass=5 middle=6 treble=6 presence=5 master=6 control1=5")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Analog time=380 feedback=25 mix=16")
            .Off()
            .Add("reverb", "type=Room decay=0.9 size=45 mix=8")
            .Scene("Verse")
            .Scene("Chorus", "+drive")
            .Scene("Solo", "+drive +delay", 2)
    ];

    static IEnumerable<FactoryRecipe> Roots() => [
        Preset(
                "Chicken Pickin'",
                "The Nashville Telecaster sound of James Burton and Brent Mason: a compressor into a clean Deluxe "
                + "Reverb with a slapback echo. Hybrid-pick, snap the strings; the Twang Lead scene pushes the amp.",
                "Country",
                "Clean"
            )
            .Add("compressor", "type=Pedal threshold=-32 ratio=6 attack=3 release=120 makeup=8")
            .Amp("Deluxe Reverb", "gain=3.5 bass=4 treble=7", "gain=6 bass=4 treble=6.5")
            .Cab("1x12 Open", "C12N", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Analog time=110 feedback=0 mix=20")
            .Add("reverb", "type=Spring springs=2 decay=1.4 mix=12")
            .Scene("Pickin'")
            .Scene("Twang Lead", "amp=B", 2),
        Preset(
                "Folsom Prison Blues",
                "Luther Perkins' boom-chicka behind Johnny Cash, live in 1968: a palm-muted Telecaster-style guitar "
                + "into a brownface Fender Deluxe. Mute everything with the heel of the hand.",
                "Country",
                "1960s",
                "Clean"
            )
            .Amp("Deluxe 6G3", "gain=1.5 treble=6")
            .Cab("1x12 Open", "C12N", "SM57", "m1position=30 m1distance=2")
            .Add("reverb", "type=Room decay=1 size=50 mix=12")
            .Scene("Boom-Chicka"),
        Preset(
                "Mystery Train",
                "Scotty Moore behind Elvis in 1955: a hollow-body Gibson into a small tube combo with its own tape "
                + "slapback, the sound of rockabilly. Thumb the bass notes, pick the double stops.",
                "Rockabilly",
                "1950s",
                "Clean"
            )
            .Amp("Tweed Deluxe 5E3", "gain=0.5 treble=6")
            .Cab("1x12 Open", "C12N", "SM57", "m1position=30 m1distance=2")
            .Add("delay", "type=Mono_Tape time=120 feedback=8 mix=32")
            .Scene("Slapback"),
        Preset(
                "Misirlou",
                "Dick Dale in 1962: a Strat with heavy strings into a Fender Showman and a tube spring reverb tank "
                + "set drippy. Tremolo-pick as fast as you can; the AB763 Bandmaster has the Showman's circuit.",
                "Surf",
                "1960s",
                "Clean"
            )
            .Amp("Bandmaster AB763", "gain=4.5 bass=3 treble=8 bright=1")
            .Cab("1x12 Closed", "D120F", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Spring springs=3 decay=2.6 mix=34 springDrive=6 boing=45")
            .Scene("Surf"),
        Preset(
                "Apache",
                "Hank Marvin with the Shadows in 1960: a Strat into a Vox AC15 with a multi-head tape echo, the "
                + "melody on the bridge pickup with the tremolo arm. Let every note repeat.",
                "Instrumental",
                "1960s",
                "Clean"
            )
            .Amp("AC15", "gain=1 cut=4")
            .Cab("1x12 Open", "Alnico Blue", "SM57", "m1position=30 m1distance=2")
            .Add("delay", "type=Mono_Tape time=300 feedback=30 head2Ratio=50 head2Level=-4 mix=28")
            .Scene("Echo")
    ];

    static IEnumerable<FactoryRecipe> Jazz() => [
        Preset(
                "Four on Six",
                "Wes Montgomery's sound: a big archtop played with the thumb into a Fender Super Reverb, treble down "
                + "and warm. Single notes, then octaves, then chords, all with the side of the thumb.",
                "Jazz",
                "1960s",
                "Clean"
            )
            .Amp("Super Reverb", "gain=2.5 bass=6 middle=5 treble=3")
            .Cab("4x10 Open", "P10R", "R-121", "m1position=40 m1distance=3")
            .Add("reverb", "type=Room decay=1 size=50 mix=10")
            .Scene("Thumb"),
        Preset(
                "Bright Size Life",
                "Pat Metheny in 1976: a hollow-body into a solid-state combo (here the JC-120), doubled by two "
                + "slightly modulated delays and a hall. Play the lines legato and let them bloom.",
                "Jazz",
                "1970s",
                "Clean"
            )
            .Amp("JC-120", "gain=1.75 bass=6 middle=5 treble=4 control1=3")
            .Cab("4x10 Open", "P10R", "SM57", "m1position=35 m1distance=2")
            .Add(
                "delay",
                "type=Dual time=390 timeRight=430 feedback=12 feedbackRight=12 mix=28 lfo1Rate=0.6 lfo1Depth=2"
            )
            .Add("reverb", "type=Hall decay=2.4 mix=18")
            .Scene("Doubled")
            .Scene("Dry", "-delay"),
        Preset(
                "Smooth",
                "Carlos Santana in 1999: a PRS into the lead channel of a Mesa Mark IV, sustaining and vocal. Long "
                + "notes, slow vibrato, the neck pickup for the melody.",
                "Latin Rock",
                "1990s",
                "Lead"
            )
            .Amp(
                "Mark IV",
                "gain=6 bass=4 middle=6 treble=6 presence=5 master=5 control1=6 control2=5"
            )
            .Cab("1x12 Closed", "EVM12L", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Analog time=380 feedback=22 mix=14")
            .Add("reverb", "type=Hall decay=2 mix=14")
            .Scene("Lead")
    ];
}
