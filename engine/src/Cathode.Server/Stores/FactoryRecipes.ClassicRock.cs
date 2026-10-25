namespace Cathode.Server.Stores;

public static partial class FactoryRecipes {
    static IEnumerable<FactoryRecipe> ClassicRock() => [
        Preset(
                "Purple Haze",
                "Jimi Hendrix in 1967: a Strat into a germanium fuzz and a 100-watt Marshall Super Lead on full. The "
                + "Solo scene swaps in an octave fuzz; roll the guitar's volume back and the fuzz cleans up.",
                "Classic Rock",
                "1960s",
                "Fuzz"
            )
            .Drive("Face Fuzz", "drive=8 level=0")
            .Drive("Octave Fuzz", "drive=7 tone=6 level=0")
            .Off()
            .Amp(
                "Plexi 100W",
                "gain=7 bass=5 middle=6 treble=7 presence=5 switch1=1 control1=4",
                "gain=4 bass=5 middle=6 treble=7 presence=5 switch1=1 control1=2"
            )
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=0.9 size=45 mix=10")
            .Scene("Rhythm")
            .Scene("Solo", "-drive +drive2", 2)
            .Scene("Clean", "-drive amp=B"),
        Preset(
                "Little Wing",
                "Jimi Hendrix's ballad: a Strat on the neck and middle pickups into a Marshall turned down to the "
                + "edge of breakup, the guitar fed through a rotating speaker. Play the chord embellishments with "
                + "the thumb.",
                "Classic Rock",
                "1960s",
                "Clean"
            )
            .Amp(
                "Plexi 100W",
                "gain=4 bass=5 middle=6 treble=6 presence=5 switch1=1 control1=3",
                "gain=6.5 bass=5 middle=6.5 treble=6.5 presence=5 switch1=1 control1=4"
            )
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=30 m1distance=2")
            .Add("rotary", "speed=Slow mix=60 hornDepth=45 drumDepth=40", "speed=Fast mix=60 hornDepth=45 drumDepth=40")
            .Add("reverb", "type=Plate decay=1.8 mix=15")
            .Scene("Rotor Slow")
            .Scene("Rotor Fast", "rotary=B")
            .Scene("Solo", "amp=B", 2),
        Preset(
                "Machine Gun",
                "Jimi Hendrix with the Band of Gypsys, 1970: a fuzz into a Uni-Vibe-style phase shifter into a "
                + "cranked Marshall. Let the vibe's slow throb and the feedback do the work; the Clean scene drops "
                + "the fuzz.",
                "Classic Rock",
                "1970s",
                "Fuzz"
            )
            .Drive("Face Fuzz", "drive=7 level=0")
            .Add("vibe", "mode=Chorus rate=1.6 intensity=80 mix=50")
            .Amp(
                "Plexi 100W",
                "gain=7 bass=5 middle=6 treble=6.5 presence=5 switch1=1 control1=4",
                "gain=7 bass=5 middle=6 treble=6.5 presence=5 switch1=1 control1=4"
            )
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=12")
            .Scene("Fuzz Vibe")
            .Scene("Lead", "-vibe", 2)
            .Scene("Clean Vibe", "-drive amp=B"),
        Preset(
                "Sunshine of Your Love",
                "Eric Clapton with Cream, 1967: a humbucker guitar into a 100-watt Marshall on full. The Woman Tone "
                + "scene rolls off the treble before the amp, as Clapton did with the guitar's tone knob, for the "
                + "solo.",
                "Classic Rock",
                "1960s",
                "Crunch"
            )
            .Add("filter", "type=Low_Pass freq=900 q=0.9")
            .Off()
            .Amp("1959SLP", "gain=8 bass=5 middle=7 treble=6 presence=5 switch1=1 control1=5")
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=12")
            .Scene("Riff")
            .Scene("Woman Tone", "+filter", 2),
        Preset(
                "Whole Lotta Love",
                "Jimmy Page in 1969: a Les Paul into a 100-watt Marshall Super Lead, the input channels jumped. Riff "
                + "on the bridge pickup; the Solo scene adds a tape echo for the bends.",
                "Classic Rock",
                "1960s",
                "Crunch"
            )
            .Amp("1959SLP", "gain=7 bass=5 middle=6 treble=7 presence=6 switch1=1 control1=5")
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=20 m1distance=1")
            .Add("delay", "type=Tape time=280 feedback=25 mix=18")
            .Off()
            .Add("reverb", "type=Room decay=1.2 size=60 mix=12")
            .Scene("Riff")
            .Scene("Solo", "+delay", 2),
        Preset(
                "Smoke on the Water",
                "Ritchie Blackmore in 1972: a Strat into a modified 200-watt Marshall Major, pushed by a tape-deck "
                + "preamp used as a booster (a treble booster here). Pick the riff's fourths hard with the fingers.",
                "Classic Rock",
                "1970s",
                "Crunch"
            )
            .Drive("Treble Booster", "drive=5 tone=6 level=3")
            .Amp(
                "Major 1967",
                "gain=7 bass=4 middle=6 treble=7 presence=6 switch1=1 control1=5",
                "gain=9 bass=4 middle=6.5 treble=7 presence=6 switch1=1 control1=6"
            )
            .Cab("4x12 Closed", "G12H30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=10")
            .Scene("Riff")
            .Scene("Solo", "amp=B", 2),
        Preset(
                "Baba O'Riley",
                "Pete Townshend's stage sound of 1971: a Hiwatt Custom 100, loud and clean at heart, its power "
                + "chords breaking up only when hit hard. Strum big; the Lead scene turns the amp up.",
                "Classic Rock",
                "1970s",
                "Crunch"
            )
            .Amp(
                "Hiwatt DR103",
                "gain=7 bass=5 middle=6 treble=6 presence=5 master=8 switch1=1 control1=6",
                "gain=9 bass=5 middle=6.5 treble=6 presence=5 master=9 switch1=1 control1=7"
            )
            .Cab("4x12 Closed", "Creamback", "SM57", "m1position=30 m1distance=1")
            .Add("reverb", "type=Room decay=1.1 size=55 mix=10")
            .Scene("Power Chords")
            .Scene("Lead", "amp=B", 2),
        Preset(
                "Comfortably Numb Solo",
                "David Gilmour in 1979: a Strat into a Big Muff-type fuzz and a Hiwatt with Fane-loaded cabs, then a "
                + "long digital delay and a hall. The Verse scene is the Hiwatt clean; take the solo slow and wide.",
                "Classic Rock",
                "Prog",
                "1970s",
                "Lead"
            )
            .Drive("Pi Fuzz", "drive=6 tone=6 level=0")
            .Off()
            .Amp("Hiwatt DR103", "gain=5 bass=5 middle=6 treble=6 presence=5 master=8 switch1=1 control1=4")
            .Cab("4x12 Closed", "Creamback", "SM57", "m1position=30 m1distance=1.5")
            .Add("delay", "type=Digital time=440 feedback=30 mix=24")
            .Off()
            .Add("reverb", "type=Hall decay=2.6 mix=18")
            .Scene("Verse")
            .Scene("Solo", "+drive +delay", 2),
        Preset(
                "Cinnamon Girl",
                "Neil Young in 1969: a Les Paul into a tweed Fender Deluxe with everything on full, raw and woolly. "
                + "Play the drop-D riff loose; the Solo scene leans on the amp a little harder.",
                "Classic Rock",
                "1960s",
                "Crunch"
            )
            .Amp(
                "Tweed Deluxe 5E3 Jumped",
                "gain=9 treble=7 control1=6",
                "gain=10 treble=7 control1=8"
            )
            .Cab("1x12 Open", "C12N", "SM57", "m1position=35 m1distance=2")
            .Add("reverb", "type=Room decay=0.8 size=40 mix=8")
            .Scene("Riff")
            .Scene("Solo", "amp=B", 2),
        Preset(
                "Layla",
                "Eric Clapton with Derek and the Dominos, 1970: a Strat into a tiny tweed Fender Champ turned all "
                + "the way up. Small, hot and compressed: the riff on the bridge pickup, the fills on the neck.",
                "Classic Rock",
                "1970s",
                "Crunch"
            )
            .Amp("Champ 5F1", "gain=9.5")
            .Cab("1x10 Open", "P10R", "SM57", "m1position=30 m1distance=1")
            .Add("reverb", "type=Room decay=0.9 size=45 mix=10")
            .Scene("Riff"),
        Preset(
                "Bohemian Rhapsody Solo",
                "Brian May in 1975: a home-made guitar with a sixpence for a pick, into a treble booster and a Vox "
                + "AC30 on full. The booster is what makes the solo sing; the Rhythm scene leaves it off.",
                "Classic Rock",
                "1970s",
                "Lead"
            )
            .Drive("Treble Booster", "drive=7 tone=5 level=2")
            .Off()
            .Amp("AC30", "gain=8 bass=4 treble=7 cut=4")
            .Cab("2x12 Open", "Alnico Blue", "SM57", "m1position=30 m1distance=1")
            .Add("reverb", "type=Room decay=1.2 size=60 mix=12")
            .Scene("Rhythm")
            .Scene("Solo", "+drive", 2),
        Preset(
                "Day Tripper",
                "The Beatles in 1965: a guitar straight into a Vox AC30 Top Boost, bright and lightly broken up. "
                + "Play the riff with a firm, even attack.",
                "Classic Rock",
                "1960s",
                "Crunch"
            )
            .Amp("AC30", "gain=6 bass=5 treble=6 cut=5")
            .Cab("2x12 Open", "Alnico Blue", "SM57", "m1position=35 m1distance=2")
            .Add("reverb", "type=Room decay=0.9 size=45 mix=10")
            .Scene("Riff"),
        Preset(
                "Sultans of Swing",
                "Mark Knopfler in 1978: a Strat on the in-between pickup positions into a Fender Vibrolux, played "
                + "with the fingers. Snap the strings for the lead lines; the Solo scene pushes the amp.",
                "Classic Rock",
                "1970s",
                "Clean"
            )
            .Amp("Vibrolux Reverb AA964", "gain=5 bass=4 treble=7 bright=1", "gain=6.5 bass=4 treble=6.5 bright=1")
            .Cab("4x10 Open", "P10R", "SM57", "m1position=30 m1distance=2")
            .Add("reverb", "type=Spring springs=2 decay=1.6 mix=14")
            .Scene("Verse")
            .Scene("Solo", "amp=B", 2),
        Preset(
                "Money for Nothing",
                "Mark Knopfler in 1985: a Les Paul into a Laney amp through a wah pedal parked half way, the source "
                + "of that honking, nasal riff. Leave the wah where it is.",
                "Classic Rock",
                "1980s",
                "Crunch"
            )
            .Add("wah", "type=Cry_Baby position=55")
            .Amp("Laney GH100L", "gain=6 bass=4 middle=6 treble=6 presence=5 master=6 control1=5")
            .Cab("4x12 Closed", "G12H30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=10")
            .Scene("Riff"),
        Preset(
                "More Than a Feeling",
                "Tom Scholz in 1976: a Les Paul into a 50-watt Marshall cranked into an attenuator, the guitars "
                + "layered and polished. The Clean scene is the chorused intro; the Solo scene adds a short echo.",
                "Classic Rock",
                "1970s",
                "Crunch"
            )
            .Amp(
                "1987X",
                "gain=8 bass=4.5 middle=7 treble=6 presence=5 switch1=1 control1=5",
                "gain=2.5 bass=5 middle=5 treble=6 presence=5 switch1=1 control1=1"
            )
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("chorus", "rate=0.6 depth=40 mix=35")
            .Off()
            .Add("delay", "type=Analog time=320 feedback=20 mix=15")
            .Off()
            .Add("reverb", "type=Plate decay=1.6 mix=12")
            .Scene("Riff")
            .Scene("Clean Intro", "amp=B +chorus")
            .Scene("Solo", "+delay", 2),
        Preset(
                "Back in Black",
                "Angus and Malcolm Young in 1980: an SG into a 100-watt Marshall Super Lead, louder than it is "
                + "distorted. Hit the open chords and let them ring; the Lead scene turns the amp up for the solo.",
                "Classic Rock",
                "Hard Rock",
                "1980s",
                "Crunch"
            )
            .Amp(
                "Plexi 100W",
                "gain=6 bass=5 middle=6 treble=6.5 presence=5 switch1=1 control1=4",
                "gain=8 bass=5 middle=7 treble=6.5 presence=5 switch1=1 control1=5"
            )
            .Cab("4x12 Closed", "Greenback", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=55 mix=10")
            .Scene("Rhythm")
            .Scene("Lead", "amp=B", 2)
    ];
}
