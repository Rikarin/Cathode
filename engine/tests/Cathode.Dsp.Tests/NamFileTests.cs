using Cathode.Dsp.Capture;

namespace Cathode.Dsp.Tests;

public sealed class NamFileTests : IDisposable {
    readonly string folder = Directory.CreateTempSubdirectory("cathode-namfile").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    string Write(string json) {
        var path = Path.Combine(folder, $"{Guid.NewGuid():n}.nam");
        File.WriteAllText(path, json);
        return path;
    }

    [Theory]
    [InlineData("""{"version":"0.7.0","architecture":"SlimmableContainer","config":{"submodels":[]}}""")]
    [InlineData(
        """
        {"architecture":"WaveNet","config":{"layers":[
          {"channels":3},
          {"channels":8,"slimmable":{"method":"slice_channels_uniform","kwargs":{"allowed_channels":[2,8]}}}]}}
        """
    )]
    public void A_container_or_a_wavenet_with_a_slimmable_layer_array_has_sizes(string json) =>
        Assert.True(NamFile.IsSlimmable(Write(json)));

    [Theory]
    [InlineData("""{"architecture":"WaveNet","config":{"layers":[{"channels":16},{"channels":8}]}}""")]
    [InlineData("""{"architecture":"WaveNet","config":{"layers":[{"channels":8,"slimmable":{}}]}}""")]
    [InlineData("""{"architecture":"WaveNet","config":{"layers":[{"channels":8,"slimmable":true}]}}""")]
    [InlineData("""{"architecture":"LSTM","config":{"slimmable":{"method":"slice_channels_uniform"}}}""")]
    [InlineData("""{"architecture":"WaveNet"}""")]
    [InlineData("""{"architecture":7}""")]
    [InlineData("[]")]
    [InlineData("RIFF, not JSON")]
    [InlineData("")]
    public void Any_other_file_has_one(string content) => Assert.False(NamFile.IsSlimmable(Write(content)));

    [Fact]
    public void A_file_that_is_not_there_has_one() =>
        Assert.False(NamFile.IsSlimmable(Path.Combine(folder, "missing.nam")));
}
