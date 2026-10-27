using Cathode.Dsp.Amp;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream M2's guard that the driver variants and the loop stage leave every other model as it was: each model's
///     phase-inverter table and its power amp, rendered alone, hash to the same bits as on the commit before them
///     (49fe687). An intended change to an inverter or to <see cref="PowerAmp" /> regenerates the table from this
///     test's output (the power-amp renders of the Mesa, Soldano and Engl models were regenerated when their supplies
///     took their drawings' preamp draws and filter caps; their inverter tables did not move; the Powerball's render
///     again when its inverter's grid leak was corrected to the drawing's 100k). The hashes are macOS ARM64 renders, so
///     the test runs there only
///     (<see cref="BaselinePlatform" />).
/// </summary>
public class InverterBitIdentityTests(ITestOutputHelper output) {
    /// <summary>(inverter table, power amp render) per model, from 49fe687.</summary>
    static readonly Dictionary<string, (ulong Table, ulong Render)> Expected = new() {
        { "Blackface", (0xE5591A0D89D0E308, 0xA7D1B0B5F3CF18FC) },
        { "Plexi", (0x198AF1CECAF0406C, 0x1B2E221578D35E40) },
        { "AC30", (0x4D72675F6BB65764, 0xD5BAE76FDC8CC32D) },
        { "AC15", (0xF9848A7503B3F8AB, 0x98B9B8CADFA484D0) },
        { "DC-30", (0xA1D1374C5E3FB19C, 0x5F76B2420E703F16) },
        { "Prometheus", (0xC4A963BB5339C57B, 0x9EA81DECE4CFAE9B) },
        { "Prometheus Clean", (0xC4A963BB5339C57B, 0x9EA81DECE4CFAE9B) },
        { "JCM800 2203", (0xA3C3C2B376049844, 0xBFDB26B4BB819CDA) },
        { "JCM800 2204", (0xA3C3C2B376049844, 0x7D1F033CE6E2B575) },
        { "JTM45", (0x198AF1CECAF0406C, 0xA3E1395CFCBD2AC3) },
        { "Plexi 100W", (0xA0965AF03FAF2545, 0xE51F1842AB18035A) },
        { "Laney GH100L", (0xA3C3C2B376049844, 0x82F977071CC0A25C) },
        { "Deluxe Reverb", (0xE39CFAB4C3BBB031, 0x9D144EB089B635B1) },
        { "Twin Reverb", (0x2BD2DBD0FCB651D7, 0xAF1A2D8E9FD1E59F) },
        { "Super Reverb", (0x2BD2DBD0FCB651D7, 0x4D6D0818CA15640F) },
        { "Princeton Reverb", (0xD45935F647FBF6E8, 0xE1E7DA20CF247239) },
        { "Bassman 5F6-A", (0x198AF1CECAF0406C, 0xB398A79890A66813) },
        { "Bassman 5F6-A Jumped", (0x198AF1CECAF0406C, 0xB398A79890A66813) },
        { "Tweed Deluxe 5E3", (0x751704F24D4270C5, 0x838AB22CAAE5B68B) },
        { "Tweed Deluxe 5E3 Jumped", (0x751704F24D4270C5, 0x838AB22CAAE5B68B) },
        { "SLO-100", (0x5295DF82E6C0BE75, 0x3EF7F59DF1E7CD0D) },
        { "Mark IIC+", (0x69AC6CE47CB13EA8, 0x130114B4A1DAE26A) },
        { "5150", (0x4D48EE5EA28519A1, 0xCA33CC27152AEE83) },
        { "Dual Rectifier", (0x626AC8FABA73BCDB, 0xFC58CA1C580ED32A) },
        { "5150III", (0x4F811AEFF475696E, 0x9CF3048C46C2C9A7) },
        { "Mark IV", (0x65C665B82CA5FABB, 0x4FF6661EC3386049) },
        { "Legacy VL100", (0x62CA6B505BCAA6A8, 0xCA558CC4597F3C55) },
        { "Chieftain", (0x6DC958B20D52CEBF, 0xD7205E0F637221E2) },
        { "Hot Cat 30", (0x7D4B8004F4D3DC40, 0xB32027284E21F09F) },
        { "ODS 97", (0x9C009F3371372A78, 0xF8A921750B7850F6) },
        { "Express", (0x3C6D880CF487CCF8, 0x052A5010005DAEEB) },
        { "Lone Star", (0xEF23C177E7AB0CDD, 0xC231447E9AD36884) },
        { "Mark V", (0x3DA801BA1355FEFD, 0x78933BBAB444EB1F) },
        { "Rect-o-Verb", (0x2A1BCEFCC98E2CDB, 0xBB5AFC5116095590) },
        { "Triple Rectifier", (0xA743C4E16D9DAC79, 0x5B11C2A97AF5269A) },
        { "Powerball", (0x581B90237DB38239, 0xC18642751A42C71C) },
        { "Savage 120", (0x96BE26A30C20DFDD, 0xFE6DD5C86DB23352) },
        { "SVT", (0xD1764C0CC01C37B0, 0x1D8F66AC6688D267) },
        { "Portaflex B-15N", (0x76788BF6655480E6, 0x375A6E0DDFCC5DBB) },
        { "JC-120", (0xE5591A0D89D0E308, 0xA7D1B0B5F3CF18FC) },
        { "Hot Rod Deluxe", (0xBE549F7E7E20226C, 0xC934A025D888CB23) },
        { "Champ 5F1", (0x751704F24D4270C5, 0xA36EB5EA922A9E6A) },
        { "Vibroverb AB763", (0x6F3DB50BEBC24351, 0xEECFD6ABF6A76C80) },
        { "Vibrolux Reverb AA964", (0x810B1B19D4ECF8D6, 0x0C96A8F162FDD247) },
        { "Bandmaster AB763", (0x82D8093074D94FAA, 0xCAA9A691AD167ED6) },
        { "Tweed Twin 5F8-A", (0xF6A44F422C4235FB, 0x662F5D33C862698B) },
        { "Tweed Twin 5F8-A Jumped", (0xF6A44F422C4235FB, 0x662F5D33C862698B) },
        { "Tremolux AA763", (0x7ADA586669E241F5, 0x7C125AF1766A8130) },
        { "Princeton 5F2-A", (0x751704F24D4270C5, 0x86E1083C42F44E68) },
        { "Princeton AA964", (0xFF3967EFEBF1B134, 0x0DE0213189DB888A) },
        { "Vibro-King", (0xDA7A23B207AE5239, 0xFD7EFD5D17F5BF06) },
        { "Blues Junior", (0x0874B0DB2CAC1F77, 0x680C8F5553985E66) },
        { "BE-100", (0x06CBD971B1F3A258, 0x828C6DC6401C58DB) },
        { "BE-100 HBE", (0x06CBD971B1F3A258, 0x828C6DC6401C58DB) },
        { "Silver Jubilee", (0x198AF1CECAF0406C, 0x93F357923D47EEB2) },
        { "18 W 1974X", (0x7A5B065D38C6FE49, 0x15A3F336A676560C) },
        { "Hiwatt DR103", (0xA513A2418095E460, 0xEF47D21928AC9EC3) },
        { "1959SLP", (0xA0965AF03FAF2545, 0x1E3C60F0D9C6DE39) },
        { "1987X", (0xA0965AF03FAF2545, 0x3DA0D1D24BFA3FCE) },
        { "1959T Tremolo", (0xA0965AF03FAF2545, 0x1E3C60F0D9C6DE39) },
        { "Bluesbreaker 1962", (0x198AF1CECAF0406C, 0xB53BE4D463DA8E29) },
        { "Hiwatt DR504", (0xA513A2418095E460, 0x9D120AF0604F1412) },
        { "Cornford MK50H II", (0x3FD4A75D1C056D1D, 0xD40A4389D50BCE8E) },
        { "Tiny Terror", (0xB4AED2D403EDCFC9, 0xCA968B0E0F45D0AF) },
        { "Liverpool", (0x20A2049A8B34EA73, 0x1E80AAC97F3C88F8) },
        { "ODS 70s", (0xC0BA94849F63DA26, 0x88D1D84D616BD065) },
        { "Dual Rectifier 3-Ch", (0xA743C4E16D9DAC79, 0x7DF854C88033DFE1) },
        { "Subway Blues", (0xB83BF82E1409763B, 0xDF1FEEAD30F31B64) },
        { "TriAxis Rhythm", (0x626AC8FABA73BCDB, 0xFC58CA1C580ED32A) },
        { "TriAxis Lead 1", (0x626AC8FABA73BCDB, 0xFC58CA1C580ED32A) },
        { "TriAxis Lead 2", (0x626AC8FABA73BCDB, 0xFC58CA1C580ED32A) },
        { "JCM900 4502", (0x55F038EDD3B16567, 0xC81F78F553E1B12E) },
        { "JCM900 4100", (0x55F038EDD3B16567, 0x6FF6AEF8FDB12BD9) },
        { "Valvestate 8080", (0xE5591A0D89D0E308, 0xA7D1B0B5F3CF18FC) },
        { "JVM410H OD", (0xD2F7D6AB3160DAFF, 0xCD00DD5E9BC7EAC7) },
        { "Concert 6G12", (0x8BB74C86362290BE, 0x062DB96DD486477B) },
        { "Super 6G4", (0x8BB74C86362290BE, 0xF43ACD6B126E92C7) },
        { "Deluxe 6G3", (0xCF998D388667A246, 0xB56B3E631E221656) },
        { "Vibrolux 6G11", (0x571C6DBBBE507E38, 0x59E3FA28438EA575) },
        { "Bassman 135", (0xFC42035894385A78, 0xD78999EF2D341A23) },
        { "Scout GA-17RVT", (0x53511F6E464C9C80, 0x1E5CA20F2BA4C479) }
    };

    static readonly Lock UpdateLock = new();

    public static TheoryData<string> Names => [..Expected.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    public void Existing_inverters_and_power_amps_render_bit_identical(string name) {
        BaselinePlatform.Require();
        var design = AmpDesign.All.Single(a => a.Name == name);
        var (table, render) = (Table(design.Power.PhaseInverter), Render(design.Power));
        var line = $"{{ \"{name}\", (0x{table:X16}, 0x{render:X16}) }},";
        output.WriteLine(line);
        // CATHODE_UPDATE_INVERTERS=<file> collects every model's entry there instead of checking it, for the table
        // above.
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_INVERTERS") is { Length: > 0 } updates) {
            lock (UpdateLock) {
                File.AppendAllLines(updates, [line]);
            }

            return;
        }

        Assert.Equal(Expected[name].Table, table);
        Assert.Equal(Expected[name].Render, render);
    }

    static ulong Mix(ulong hash, double value) =>
        (hash ^ (ulong)BitConverter.DoubleToInt64Bits(value)) * 1099511628211UL;

    static ulong Table(PhaseInverterDesign design) {
        var inverter = PhaseInverter.For(design);
        var hash = 14695981039346656037UL;
        hash = Mix(hash, inverter.RestGrid);
        hash = Mix(hash, inverter.RestPlateA);
        hash = Mix(hash, inverter.RestPlateB);
        hash = Mix(hash, inverter.OutputImpedance.Zaa);
        hash = Mix(hash, inverter.OutputImpedance.Zab);
        hash = Mix(hash, inverter.OutputImpedance.Zba);
        hash = Mix(hash, inverter.OutputImpedance.Zbb);
        for (var n = 0; n <= 2_000; n++) {
            var vd = inverter.Range * (n / 1_000.0 - 1) * 1.01;
            var (a, b) = inverter.Evaluate(vd, out var sa, out var sb);
            hash = Mix(Mix(Mix(Mix(hash, a), b), sa), sb);
            hash = Mix(hash, inverter.GridToCathode(vd, out _));
        }

        return hash;
    }

    static ulong Render(PowerAmpDesign design) {
        var power = new PowerAmp(design, 96_000);
        power.SetSourceResistance(30e3);
        power.Configure(new(0.6, 0.3, 0.4, 0.5, 0.5, 0.5, 0.9));
        power.Reset();
        var hash = 14695981039346656037UL;
        for (var n = 0; n < 4_800; n++) {
            var x = 4 * Math.Sin(2 * Math.PI * 110 * n / 96_000.0) + 1.5 * Math.Sin(2 * Math.PI * 1_330 * n / 96_000.0);
            hash = Mix(hash, power.Tick(x));
        }

        return hash;
    }
}
