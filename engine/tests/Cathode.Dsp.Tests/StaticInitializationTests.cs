using System.Runtime.CompilerServices;
using Cathode.Dsp.Amp;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Static initializers run in declaration order, so a code cleanup that reorders members (fields before properties,
///     say) can leave one reading another that is still null. These catch that directly rather than through every test
///     that builds the block registry.
/// </summary>
public sealed class StaticInitializationTests {
    public static TheoryData<Type> DspTypes() {
        var types = new TheoryData<Type>();
        foreach (var type in typeof(BlockRegistry).Assembly.GetTypes()) {
            if (!type.ContainsGenericParameters && type.TypeInitializer is not null) {
                types.Add(type);
            }
        }

        return types;
    }

    [Theory]
    [MemberData(nameof(DspTypes))]
    public void Every_static_initializer_runs(Type type) =>
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);

    [Fact]
    public void Every_amp_design_is_complete() {
        foreach (var design in AmpDesign.All) {
            Assert.True(design.Power is not null, $"{design.Name} has no power amp");
            Assert.True(design.Power!.PhaseInverter is not null, $"{design.Name} has no phase inverter");
            Assert.NotEmpty(design.Preamp);
            Assert.All(design.Preamp, stage => Assert.NotNull(stage));
            Assert.True(design.Panel is not null, $"{design.Name} has no panel");
        }
    }
}
