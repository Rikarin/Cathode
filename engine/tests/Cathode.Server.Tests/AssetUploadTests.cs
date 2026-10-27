using Cathode.Contracts;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cathode.Server.Tests;

/// <summary>Asset uploads at their limits and with hostile content.</summary>
public sealed class AssetUploadTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => client = await server.ConnectedClientAsync();

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("&processing=autoTrim")]
    public async Task Hostile_or_unsupported_wavs_are_refused_with_a_reason(string processing) {
        byte[][] hostile = [
            Wav(1, 1, 16, 0x7FFF_FFF0, []), // 44 bytes declaring 2 GB of samples
            Wav(1, 1, 16, 0xFFFF_FFFF, []),
            Wav(1, 1, 0, 4, [0, 0, 0, 0]), // zero bits: a division by zero
            Wav(1, 0, 16, 4, [0, 0, 0, 0]),
            Wav(2, 1, 4, 4, [0, 0, 0, 0]), // ADPCM
            [.."RIFF\0\0\0\0WAVEfmt "u8] // cut short
        ];
        var stored = (await client.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!.Length;

        foreach (var wav in hostile) {
            using var response = await client.PostAsync(
                $"/api/v1/assets?name=bad.wav{processing}",
                new ByteArrayContent(wav),
                Token
            );

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
            Assert.StartsWith("bad.wav", problem!.Detail, StringComparison.Ordinal);
        }

        Assert.Equal(stored, (await client.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("&processing=autoTrim")]
    public async Task An_eight_bit_wav_is_taken(string processing) {
        using var response = await client.PostAsync(
            $"/api/v1/assets?name=old.wav{processing}",
            new ByteArrayContent(Wav(1, 1, 8, 4, [255, 200, 128, 128])),
            Token
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    static byte[] Wav(ushort format, ushort channels, ushort bits, uint dataSize, byte[] data) {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write(format);
        writer.Write(channels);
        writer.Write(48_000);
        writer.Write(48_000 * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public async Task A_damaged_asset_index_is_rebuilt_from_the_files_rather_than_stopping_the_server() {
        await using var damaged = new ServerFixture();
        var folder = Directory.CreateDirectory(Path.Combine(damaged.DataDirectory, "assets")).FullName;
        byte[] content = [1, 2, 3, 4];
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        await File.WriteAllBytesAsync(Path.Combine(folder, hash + ".nam"), content, Token);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.json"), """[{ "ref": "sha256:""", Token);

        using var http = await damaged.ConnectedClientAsync();
        var listed = (await http.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!;
        using var upload = await http.PostAsync("/api/v1/assets?name=more.nam", new ByteArrayContent([5, 6]), Token);

        var asset = Assert.Single(listed);
        Assert.Equal(("sha256:" + hash, "nam", 4L), (asset.Ref, asset.Kind, asset.Size));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(2, (await http.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!.Length);
        Assert.True(File.Exists(Path.Combine(folder, "index.json.damaged")));
        Assert.Equal(
            2,
            JsonSerializer.Deserialize<AssetInfo[]>(
                await File.ReadAllTextAsync(Path.Combine(folder, "index.json"), Token),
                JsonSerializerOptions.Web
            )!
                .Length
        );
    }

    [Fact]
    public async Task Uploads_up_to_64_mb_are_taken_and_larger_ones_are_refused_with_a_reason() {
        // Kestrel's own request limit is 30,000,000 bytes; the endpoint raises it to the store's 64 MB. A real
        // Kestrel is not used here: refusing a body while the client still writes it is a broken pipe on some
        // platforms and a 413 on others, so the test checks the limit the endpoint carries and the store's answer
        // past it.
        var upload = server.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.Single(static e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "UploadAsset"
            );
        Assert.Equal(AssetStore.MaxBytes, upload.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);

        using var large = await client.PostAsync(
            "/api/v1/assets?name=big.nam",
            new ByteArrayContent(new byte[40 * 1024 * 1024]),
            Token
        );
        using var tooLarge = await client.PostAsync(
            "/api/v1/assets?name=huge.nam",
            new ByteArrayContent(new byte[AssetStore.MaxBytes + 1]),
            Token
        );

        Assert.Equal(HttpStatusCode.OK, large.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        var problem = await tooLarge.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Contains("64 MB", problem!.Detail, StringComparison.Ordinal);
    }
}
