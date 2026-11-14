using Cathode.Dsp.Amp;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream M2's guard that the driver variants and the loop stage leave every other model as it was: each model's
///     phase-inverter table and its power amp, rendered alone, hash to the same bits as on the commit before them
///     (49fe687). An intended change to an inverter or to <see cref="PowerAmp" /> regenerates the table from this
///     test's output (the power-amp renders of the Mesa, Soldano and Engl models were regenerated when their supplies
///     took their drawings' preamp draws and filter caps; their inverter tables did not move; the Powerball's render
///     again when its inverter's grid leak was corrected to the drawing's 100k; the 5E3's entries when its supply took
///     the drawing's 5k and 22k droppers and the preamp node that follows from them; the Bassman 5F6-A's renders
///     when it took its inverter's input network; the Blackface's, Twin Reverb's and Super Reverb's entries when
///     their inverters moved to the drawings' 450 V node, with the JC-120's and Valvestate 8080's, whose unused tube
///     power amp is the Blackface's; the three again when the Blackface took the AB763 inverter and its input
///     network; the fourteen entries of the nine models whose presence pot is the inverter tail's shunt, when the
///     tail took that pot in as LongTailPairDesign documents, #540). The hashes are macOS ARM64 renders, so the test runs
///     there only (<see cref="BaselinePlatform" />).
/// </summary>
public class InverterBitIdentityTests(ITestOutputHelper output) {
    /// <summary>(inverter table, power amp render) per model, from 49fe687.</summary>
    static readonly Dictionary<string, (ulong Table, ulong Render)> Expected = new() {
        { "Blackface", (0x6F3DB50BEBC24351, 0x4DE20F2B6F27B5DD) },
        { "Plexi", (0x198AF1CECAF0406C, 0x1B2E221578D35E40) },
        { "AC30", (0x4D72675F6BB65764, 0x525D82DC7ED95538) },
        { "AC15", (0xF9848A7503B3F8AB, 0x5057E88EAE50971A) },
        { "DC-30", (0xA1D1374C5E3FB19C, 0x0FAD1D83528941C6) },
        { "Prometheus", (0xC4A963BB5339C57B, 0x7F5D2A4DDF0B8085) },
        { "Prometheus Clean", (0xC4A963BB5339C57B, 0x7F5D2A4DDF0B8085) },
        { "JCM800 2203", (0xCB3D9FB8825C45A1, 0x1F4E04B2DACF6BEE) },
        { "JCM800 2204", (0x4689ECBDB6C21135, 0xA5CC4BCB6B368679) },
        { "JTM45", (0x9D35EA85F493B585, 0x180193783DD84BC6) },
        { "Plexi 100W", (0xD92B27A10111CA3B, 0xE1BE0AE54B59331E) },
        { "Laney GH100L", (0xA3C3C2B376049844, 0x2F2F00B80AA3CE84) },
        { "Deluxe Reverb", (0xE39CFAB4C3BBB031, 0x828221858C877461) },
        { "Twin Reverb", (0x6F3DB50BEBC24351, 0xAC70DD676CCD3DE7) },
        { "Super Reverb", (0x6F3DB50BEBC24351, 0xA1A01FB2FA26953A) },
        { "Princeton Reverb", (0xD45935F647FBF6E8, 0x93A5F529338C924E) },
        { "Bassman 5F6-A", (0x9D35EA85F493B585, 0x354ED7A50DBE7B8A) },
        { "Bassman 5F6-A Jumped", (0x9D35EA85F493B585, 0x354ED7A50DBE7B8A) },
        { "Tweed Deluxe 5E3", (0x97438764B590D2D0, 0x8011E0CB34687A9E) },
        { "Tweed Deluxe 5E3 Jumped", (0x97438764B590D2D0, 0x8011E0CB34687A9E) },
        { "SLO-100", (0x9E76F230EA59D180, 0x27B46BEE5B31135E) },
        { "Mark IIC+", (0x3F5926936759F572, 0xF3D511300BEF608D) },
        { "5150", (0x8C9B83B274B605CD, 0xB0878A7D08849D61) },
        { "Dual Rectifier", (0x626AC8FABA73BCDB, 0xF7212E815DDBD503) },
        { "5150III", (0x4B03EF3D2EA85CC3, 0x708013B6768AE884) },
        { "Mark IV", (0x93C47AD3F12CD6BF, 0x8C544EBFD94C96DD) },
        { "Legacy VL100", (0x62CA6B505BCAA6A8, 0xCA558CC4597F3C55) },
        { "Chieftain", (0x6DC958B20D52CEBF, 0x547BF15F51E8D6DE) },
        { "Hot Cat 30", (0x7D4B8004F4D3DC40, 0x669CD062BE109B34) },
        { "ODS 97", (0x9C009F3371372A78, 0xFA2A0B335CEC4510) },
        { "Express", (0xC31F897BF4678464, 0xB9D539BABE6DA486) },
        { "Lone Star", (0xEF23C177E7AB0CDD, 0x845D81BE45FEC979) },
        { "Mark V", (0x494E0C9ED0AB710C, 0xE51AA977E67BB128) },
        { "Rect-o-Verb", (0xB778F3CB9BA94445, 0x0B3B1694A03FFC0C) },
        { "Triple Rectifier", (0xA743C4E16D9DAC79, 0xA9F46236E15CFC0E) },
        { "Powerball", (0xD08441B5791E9BF4, 0xEC7B5CD03B4FE1D2) },
        { "Savage 120", (0xCD989D3C7C6AC55B, 0xA21EA041F6001415) },
        { "SVT", (0xD1764C0CC01C37B0, 0x1D8F66AC6688D267) },
        { "Portaflex B-15N", (0x76788BF6655480E6, 0x375A6E0DDFCC5DBB) },
        { "JC-120", (0x6F3DB50BEBC24351, 0x4DE20F2B6F27B5DD) },
        { "Hot Rod Deluxe", (0xBE549F7E7E20226C, 0xAD94948C98BC73D3) },
        { "Champ 5F1", (0x751704F24D4270C5, 0xA36EB5EA922A9E6A) },
        { "Vibroverb AB763", (0x6F3DB50BEBC24351, 0x37651BB26BC8642F) },
        { "Vibrolux Reverb AA964", (0x810B1B19D4ECF8D6, 0xB48A5575F71915A4) },
        { "Bandmaster AB763", (0x82D8093074D94FAA, 0xF7DBF8FB2C81AB8D) },
        { "Tweed Twin 5F8-A", (0x8768F171DA00FFF8, 0x277CB28006E9AE6F) },
        { "Tweed Twin 5F8-A Jumped", (0x8768F171DA00FFF8, 0x277CB28006E9AE6F) },
        { "Tremolux AA763", (0x7ADA586669E241F5, 0x4866AEF82C659AAC) },
        { "Princeton 5F2-A", (0x751704F24D4270C5, 0x86E1083C42F44E68) },
        { "Princeton AA964", (0xFF3967EFEBF1B134, 0x7B58C8DA86BCB73E) },
        { "Vibro-King", (0xDA7A23B207AE5239, 0xDCDFEB2108848402) },
        { "Blues Junior", (0x0874B0DB2CAC1F77, 0x9724A2AE834FEAEF) },
        { "BE-100", (0x51FC4DE2349278F4, 0xE0970CA70EF1C836) },
        { "BE-100 HBE", (0x51FC4DE2349278F4, 0xE0970CA70EF1C836) },
        { "Silver Jubilee", (0x198AF1CECAF0406C, 0xAF50C4364DA829BB) },
        { "18 W 1974X", (0x7A5B065D38C6FE49, 0x9D08619B28E0F94D) },
        { "Hiwatt DR103", (0xA513A2418095E460, 0xEF47D21928AC9EC3) },
        { "1959SLP", (0xD92B27A10111CA3B, 0xE1BE0AE54B59331E) },
        { "1987X", (0xD92B27A10111CA3B, 0x61AAD200EFBAF76C) },
        { "1959T Tremolo", (0xD92B27A10111CA3B, 0xE1BE0AE54B59331E) },
        { "Bluesbreaker 1962", (0x10E0082A8CA22E07, 0xFD3C367B9DDE8220) },
        { "Hiwatt DR504", (0xA513A2418095E460, 0x9D120AF0604F1412) },
        { "Cornford MK50H II", (0x3FD4A75D1C056D1D, 0x5CF8E174477B2B79) },
        { "Tiny Terror", (0xB4AED2D403EDCFC9, 0x0C40B7AEE6FCCD4F) },
        { "Liverpool", (0x20A2049A8B34EA73, 0xFC1885E601CEA09B) },
        { "ODS 70s", (0xC0BA94849F63DA26, 0xCF4101F54875F55A) },
        { "Dual Rectifier 3-Ch", (0xA743C4E16D9DAC79, 0x2C849CE4D927DABF) },
        { "Subway Blues", (0xB83BF82E1409763B, 0xDF97CEAC16A96332) },
        { "TriAxis Rhythm", (0x626AC8FABA73BCDB, 0xF7212E815DDBD503) },
        { "TriAxis Lead 1", (0x626AC8FABA73BCDB, 0xF7212E815DDBD503) },
        { "TriAxis Lead 2", (0x626AC8FABA73BCDB, 0xF7212E815DDBD503) },
        { "JCM900 4502", (0x55F038EDD3B16567, 0x71BA9E615F87AA03) },
        { "JCM900 4100", (0x55F038EDD3B16567, 0xBC5542DDCB8ECBCE) },
        { "Valvestate 8080", (0x6F3DB50BEBC24351, 0x4DE20F2B6F27B5DD) },
        { "JVM410H OD", (0x128950361FAF7559, 0xB090FDEFB362E508) },
        { "Concert 6G12", (0x8BB74C86362290BE, 0x710EE1F1FAB335D8) },
        { "Super 6G4", (0x8BB74C86362290BE, 0xCECC5E45BFA7CAC2) },
        { "Deluxe 6G3", (0xCF998D388667A246, 0xD83E23FAB3ED2FE9) },
        { "Vibrolux 6G11", (0x571C6DBBBE507E38, 0xDF5707421F87007E) },
        { "Bassman 135", (0xFC42035894385A78, 0x60B4A0E9B0B74D78) },
        { "Scout GA-17RVT", (0x53511F6E464C9C80, 0x65AA4CE5C6153C38) }
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
