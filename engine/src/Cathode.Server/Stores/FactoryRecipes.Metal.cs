namespace Cathode.Server.Stores;

public static partial class FactoryRecipes {
    static IEnumerable<FactoryRecipe> Metal() => [
        Preset(
                "Paranoid",
                "Tony Iommi in 1970: an SG into a Rangemaster-type treble booster and a 100-watt Laney Supergroup, a "
                + "Plexi-style circuit (the Plexi 100W here). The booster makes the riff; the Solo scene adds gain.",
                "Metal",
                "1970s",
                "Crunch"
            )
            .Drive("Treble Booster", "drive=8 tone=5 level=4")
            .Amp(
                "Plexi 100W",
                "gain=8 bass=5 middle=6 treble=6 presence=5 switch1=1 control1=5",
                "gain=10 bass=5 middle=6.5 treble=6 presence=5 switch1=1 control1=6"
            )
            .Cab("4x12 Closed", "G12H30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=10")
            .Scene("Riff")
            .Scene("Solo", "amp=B", 2),
        Preset(
                "Breaking the Law",
                "Judas Priest in 1980: a Strat and an SG into 100-watt Marshalls of the JMP 2203 kind, the master up "
                + "for a dry, biting crunch. The Solo scene adds delay.",
                "Metal",
                "1980s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-58")
            .Amp("JCM800 2203", "gain=7 bass=5 middle=6 treble=7 presence=6 master=6")
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=25 m1distance=1")
            .Add("delay", "type=Digital time=360 feedback=22 mix=15")
            .Off()
            .Add("reverb", "type=Room decay=1 size=50 mix=10")
            .Scene("Riff")
            .Scene("Solo", "+delay", 2),
        Preset(
                "Master of Puppets",
                "James Hetfield in 1986: a screamer into a Mesa Mark IIC+ with its graphic EQ in a deep V, for the "
                + "tight downpicked thrash rhythm. Gate on; the Lead scene adds delay.",
                "Metal",
                "1980s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-52")
            .Drive("TS808 OD", "drive=1 tone=6 level=5")
            .Amp(
                "Mark IIC+",
                "gain=7.5 bass=2 middle=3 treble=7 presence=6 master=5 control1=7 control2=6 eq1=3 eq2=0 eq3=-5 "
                + "eq4=1 eq5=3"
            )
            .Cab("4x12 Closed", "G12T-75", "SM57", "m1position=20 m1distance=1")
            .Add("delay", "type=Digital time=380 feedback=25 mix=16")
            .Off()
            .Add("reverb", "type=Room decay=1 size=50 mix=8")
            .Scene("Rhythm")
            .Scene("Lead", "+delay", 2),
        Preset(
                "Enter Sandman Rhythm",
                "Metallica in 1991: the big, polished rhythm of a Mesa Rectifier in Modern mode, blended with the "
                + "Mark's punch. Close-miked and ribbon-blended; the Solo scene adds wah and delay.",
                "Metal",
                "1990s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-52")
            .Add("wah", "type=Cry_Baby position=50")
            .Pedal("position", 0, 100)
            .Off()
            .Amp("Dual Rectifier", "gain=6 bass=6 middle=4.5 treble=6 master=5 control2=5 switch1=1")
            .Cab(
                "4x12 Closed",
                "Vintage 30",
                "SM57",
                "m1position=20 m1distance=1 m2mute=0 m2mic=R-121 m2position=30 m2distance=2 m2level=-4"
            )
            .Add("delay", "type=Digital time=420 feedback=25 mix=15")
            .Off()
            .Add("reverb", "type=Room decay=1 size=50 mix=8")
            .Scene("Rhythm")
            .Scene("Solo", "+wah +delay", 2),
        Preset(
                "Fade to Black Intro",
                "Metallica in 1984: the clean intro's solid-state Roland JC-120 with its chorus, a touch of delay "
                + "and a hall. Arpeggiate on the neck pickup.",
                "Metal",
                "1980s",
                "Clean"
            )
            .Amp("JC-120", "gain=4 bass=5 middle=5 treble=6 control1=5")
            .Cab("2x12 Open", "D120F", "SM57", "m1position=35 m1distance=2")
            .Add("chorus", "rate=0.5 depth=45 mix=50 spread=180")
            .Add("delay", "type=Digital time=450 feedback=25 mix=14")
            .Add("reverb", "type=Hall decay=2.4 mix=18")
            .Scene("Clean")
            .Scene("Dry", "-chorus -delay"),
        Preset(
                "Du Hast",
                "Rammstein in 1997: drop-tuned guitars into Mesa Triple Rectifiers, a wall of tight, mechanical "
                + "palm-mutes. Hit hard and stop dead; the gate keeps the rests silent.",
                "Metal",
                "1990s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-50")
            .Amp("Triple Rectifier", "gain=6 bass=6 middle=5 treble=6 master=5 control2=5 switch1=1")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=20 m1distance=1")
            .Add("reverb", "type=Room decay=0.8 size=40 mix=6")
            .Scene("Riff"),
        Preset(
                "Djent Rhythm",
                "The modern progressive-metal rhythm sound: a screamer with the drive at zero tightening an EVH "
                + "5150III, a hard gate and a close V30. For extended-range palm-mutes; the Lead scene adds space.",
                "Modern Metal",
                "2010s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-48")
            .Drive("TS9 OD", "drive=0 tone=6 level=6")
            .Amp("5150III", "gain=5 bass=4 middle=6 treble=6 presence=6 depth=5 master=5")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=15 m1distance=0.5")
            .Add("peq", "freq1=90 gain1=-3 freq3=500 gain3=-2 q3=1.2")
            .Add("delay", "type=Digital time=480 feedback=30 mix=18")
            .Off()
            .Add("reverb", "type=Plate decay=2 mix=15")
            .Off()
            .Scene("Rhythm")
            .Scene("Lead", "+delay +reverb", 2),
        Preset(
                "Ghost of Perdition",
                "Opeth in 2005: PRS guitars into Laney GH100L heads, crushing in the heavy parts and turned down "
                + "with a chorus for the acoustic-like clean passages. The Lead scene adds delay.",
                "Metal",
                "Prog",
                "2000s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-52")
            .Amp(
                "Laney GH100L",
                "gain=8 bass=6 middle=5 treble=6 presence=5 master=5 control1=6",
                "gain=2 bass=5 middle=5 treble=6 presence=5 master=6 control1=1"
            )
            .Cab("4x12 Closed", "G12H30", "SM57", "m1position=25 m1distance=1")
            .Add("chorus", "rate=0.5 depth=40 mix=40")
            .Off()
            .Add("delay", "type=Digital time=420 feedback=25 mix=16")
            .Off()
            .Add("reverb", "type=Hall decay=2.2 mix=14")
            .Scene("Heavy")
            .Scene("Clean", "amp=B +chorus")
            .Scene("Lead", "+delay", 2),
        Preset(
                "The Enemy Inside",
                "John Petrucci in 2013: a screamer into a Mesa Mark V in IIC+ mode, its graphic EQ in a gentle V, "
                + "for precise, articulate rhythm and lead. The Lead scene adds delay and hall.",
                "Metal",
                "Prog",
                "2010s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-52")
            .Drive("TS808 OD", "drive=0.5 tone=6 level=6")
            .Amp(
                "Mark V",
                "gain=6 bass=3 middle=4 treble=6 presence=6 master=5 control1=5 switch1=1 eq1=2 eq2=-1 eq3=-3 eq4=1 "
                + "eq5=2"
            )
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=20 m1distance=1")
            .Add("delay", "type=Digital time=430 feedback=28 mix=18")
            .Off()
            .Add("reverb", "type=Hall decay=2.2 mix=12")
            .Off()
            .Scene("Rhythm")
            .Scene("Lead", "+delay +reverb", 2),
        Preset(
                "Schism",
                "Tool in 2001: drop-D riffs through stacked amps, a Mesa Dual Rectifier with a German high-gain "
                + "head; here the three-channel Rectifier in Vintage mode, mids kept up for the grind.",
                "Metal",
                "Prog",
                "2000s",
                "High Gain"
            )
            .Input("type=Intelligent threshold=-54")
            .Amp("Dual Rectifier 3-Ch", "gain=5 bass=6 middle=6 treble=5 master=5 control2=5")
            .Cab("4x12 Closed", "Vintage 30", "SM57", "m1position=25 m1distance=1")
            .Add("reverb", "type=Room decay=1 size=50 mix=8")
            .Scene("Riff")
    ];
}
