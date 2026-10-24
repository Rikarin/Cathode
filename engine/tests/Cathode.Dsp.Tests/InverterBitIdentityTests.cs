using Cathode.Dsp.Amp;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream M2's guard that the driver variants and the loop stage leave every other model as it was: each model's
///     phase-inverter table and its power amp, rendered alone, hash to the same bits as on the commit before them
///     (49fe687). An intended change to an inverter or to <see cref="PowerAmp" /> regenerates the table from this
///     test's output. The hashes are macOS ARM64 renders, so the test runs there only
///     (<see cref="BaselinePlatform" />).
/// </summary>
public class InverterBitIdentityTests(ITestOutputHelper output) {
    /// <summary>(inverter table, power amp render) per model, from 49fe687.</summary>
    static readonly Dictionary<string, (ulong Table, ulong Render)> Expected = new() {
{ "Blackface", (0x73F1EB592475C752, 0x0B945E6E906D3EBB) },
            { "Plexi", (0xD535E011B1985FB8, 0xD852F5683D0FD68F) },
            { "AC30", (0x731769887A21DADD, 0xF9BB59C484210F61) },
            { "AC15", (0xF7E22DE6D50EE7D2, 0xA954CBD8DEAA99A8) },
            { "DC-30", (0x2A1F1DA5B3E5479B, 0x55F712C2570E890C) },
            { "Prometheus", (0x10FFBF45733298F6, 0x447157A5E0ED478B) },
            { "Prometheus Clean", (0x10FFBF45733298F6, 0x447157A5E0ED478B) },
            { "JCM800 2203", (0x6338D0295A510D97, 0x893920CC5F23441D) },
            { "JCM800 2204", (0x6338D0295A510D97, 0xDFE356C750A28820) },
            { "JTM45", (0x1B1B8E057D99CEB1, 0xE4A656361B3466A3) },
            { "Plexi 100W", (0x3518E29DEFB120FB, 0x6DF025493291427D) },
            { "Laney GH100L", (0x6338D0295A510D97, 0x00F279C87527974C) },
            { "Deluxe Reverb", (0x84F415ABB4B27941, 0xE377D27C8D806263) },
            { "Twin Reverb", (0x4E5D97BF86993270, 0x6CC448E4144A4FE0) },
            { "Super Reverb", (0x4E5D97BF86993270, 0x3F4637A579DC7A48) },
            { "Princeton Reverb", (0x484895953B375CD4, 0x7E6D889FF24FCE67) },
            { "Bassman 5F6-A", (0x1B1B8E057D99CEB1, 0x0C5B1B42CCC82609) },
            { "Bassman 5F6-A Jumped", (0x1B1B8E057D99CEB1, 0x0C5B1B42CCC82609) },
            { "Tweed Deluxe 5E3", (0xF5C3E39B9328521D, 0xFEB8AE264447133F) },
            { "Tweed Deluxe 5E3 Jumped", (0xF5C3E39B9328521D, 0xFEB8AE264447133F) },
            { "SLO-100", (0x2122E94E9BE46B97, 0x59E849BD5938B28F) },
            { "Mark IIC+", (0xC319317C6737374F, 0x90DC463583EBF3AE) },
            { "5150", (0xB8C651357C57AF18, 0x22C1E4B1BF3B5301) },
            { "Dual Rectifier", (0xE3C04F0D7552081C, 0x1ADE0BE9D5598793) },
            { "5150III", (0x97357C54684DA6D6, 0xAAA4C09AE06966CA) },
            { "Mark IV", (0xC319317C6737374F, 0x45C57F283D25825B) },
            { "Legacy VL100", (0xE3B2347B457F82A4, 0xC3005B8335AAA05B) },
            { "Chieftain", (0xE9DFB2A7E5B7B7C3, 0x8E2FBE2542C8DEEE) },
            { "Hot Cat 30", (0x32110142F0D25A23, 0xA7B12FC5C8BD56E4) },
            { "ODS 97", (0xFD54388E435BF330, 0x00A9D8D7F9E8F5C8) },
            { "Express", (0x33E114ABA558BE40, 0xCCF8DBE491C0847C) },
            { "Lone Star", (0x32A9FEF035830863, 0x13BFA3AC44B7A1E5) },
            { "Mark V", (0xDF1CAB4F1DEFDA37, 0x6A8AAE370B74788F) },
            { "Rect-o-Verb", (0xB22F9C680150E717, 0xDAE1EC851FCC884A) },
            { "Triple Rectifier", (0xB62E07BDCA98538F, 0xFC5AD77F8539028D) },
            { "Powerball", (0x11BDB1BD4DDE23FD, 0x7263D23D98AF28CC) },
            { "Savage 120", (0x2206D57787839850, 0x61C5B1EA51A04996) },
            { "SVT", (0xF51AE03F1F289260, 0x792236633D0D1D4A) },
            { "Portaflex B-15N", (0x76788BF6655480E6, 0x375A6E0DDFCC5DBB) },
            { "JC-120", (0x73F1EB592475C752, 0x0B945E6E906D3EBB) },
            { "Hot Rod Deluxe", (0xAA0ACB78A6830B92, 0xA320F7A3DC06C963) },
            { "Champ 5F1", (0xF5C3E39B9328521D, 0xA36EB5EA922A9E6A) },
            { "Vibroverb AB763", (0x7BD07D68471AEE67, 0xBCE4951CAC8D4022) },
            { "Vibrolux Reverb AA964", (0x27CF8F0FD91F2213, 0x7F09C877A99E6D33) },
            { "Bandmaster AB763", (0xF2E1C8B655523A3F, 0x3149A82EDD3BACE2) },
            { "Tweed Twin 5F8-A", (0x11D34A8C5C2ACB7D, 0xBCCE83F24E32B042) },
            { "Tweed Twin 5F8-A Jumped", (0x11D34A8C5C2ACB7D, 0xBCCE83F24E32B042) },
            { "Tremolux AA763", (0x02864CE584E18FFB, 0x48E54F5B4A747A0F) },
            { "Princeton 5F2-A", (0xF5C3E39B9328521D, 0x86E1083C42F44E68) },
            { "Princeton AA964", (0xF7D7AF33D86D4EDA, 0x697034CB2A3ED875) },
            { "Vibro-King", (0xDAC54AC38AFD5DE9, 0x6F55B20110917B3F) },
            { "Blues Junior", (0xCE566EDF52D73EA3, 0x4126B9EBD5A28703) },
            { "BE-100", (0x86E865B1DC48AF67, 0x5FA9F0D3C5322B83) },
            { "BE-100 HBE", (0x86E865B1DC48AF67, 0x5FA9F0D3C5322B83) },
            { "Silver Jubilee", (0x1B1B8E057D99CEB1, 0x83CCA41097B801A8) },
            { "18 W 1974X", (0xC43E50132CFF5914, 0xF919C4F48BCA4DA9) },
            { "Hiwatt DR103", (0x23C228C9CA47A5FA, 0xC76B8307581CE67A) },
            { "1959SLP", (0x3518E29DEFB120FB, 0x0E33A16D495D0C70) },
            { "1987X", (0x3518E29DEFB120FB, 0x2B521F600EFEA0DD) },
            { "1959T Tremolo", (0x3518E29DEFB120FB, 0x0E33A16D495D0C70) },
            { "Bluesbreaker 1962", (0x1B1B8E057D99CEB1, 0xF4FBF59D3C683AD7) },
            { "Hiwatt DR504", (0x23C228C9CA47A5FA, 0xD898FC5837C5E13E) },
            { "Cornford MK50H II", (0x69D221F8C588F804, 0x76FA87992709D441) },
            { "Tiny Terror", (0xF58B6FB9872C2273, 0xF06F1AE45DA9BFB8) },
            { "Liverpool", (0xB972F6CE371901EB, 0xD0EF3A068E4DB682) },
            { "ODS 70s", (0x32BBEA98F056FDC9, 0x96C32A454B7E5F89) },
            { "Dual Rectifier 3-Ch", (0xB62E07BDCA98538F, 0xFF0338DF358B0054) },
            { "Subway Blues", (0x223A8A7C87B73259, 0xAA01EA0A1472716A) },
            { "TriAxis Rhythm", (0xE3C04F0D7552081C, 0x1ADE0BE9D5598793) },
            { "TriAxis Lead 1", (0xE3C04F0D7552081C, 0x1ADE0BE9D5598793) },
            { "TriAxis Lead 2", (0xE3C04F0D7552081C, 0x1ADE0BE9D5598793) },
            { "JCM900 4502", (0x97055A1174EFEE0A, 0x899DB8DF21B6DFF8) },
            { "JCM900 4100", (0x97055A1174EFEE0A, 0xD41BC65E9B4531FA) },
            { "Valvestate 8080", (0x73F1EB592475C752, 0x0B945E6E906D3EBB) },
            { "JVM410H OD", (0xAD8CC35F9B74CA7B, 0x4376E5A0E03B2EFB) },
            { "Concert 6G12", (0xDAFA801F17282A17, 0xA7586F3D6A686EDA) },
            { "Super 6G4", (0xDAFA801F17282A17, 0x41C47E58497ECF18) },
            { "Deluxe 6G3", (0xD303D1C0C9489D7F, 0x34C87EABA2B07D0A) },
            { "Vibrolux 6G11", (0x88580DB597B768FC, 0x6C4E8ABD7368BDC3) },
            { "Bassman 135", (0xA6E9836F76297840, 0x8DDFCD7CEE3B3A31) },
            { "Scout GA-17RVT", (0x53511F6E464C9C80, 0x1E5CA20F2BA4C479) }
    };

    public static TheoryData<string> Names => [..Expected.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    public void Existing_inverters_and_power_amps_render_bit_identical(string name) {
        BaselinePlatform.Require();
        var design = AmpDesign.All.Single(a => a.Name == name);
        var (table, render) = (Table(design.Power.PhaseInverter), Render(design.Power));
        output.WriteLine($"{{ \"{name}\", (0x{table:X16}, 0x{render:X16}) }},");
        Assert.Equal(Expected[name].Table, table);
        Assert.Equal(Expected[name].Render, render);
    }

    static ulong Mix(ulong hash, double value) => (hash ^ (ulong)BitConverter.DoubleToInt64Bits(value)) * 1099511628211UL;

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
