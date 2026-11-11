using Cathode.Contracts;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http;
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
        var stored = (await client.UserAssetsAsync(Token)).Length;

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

        Assert.Equal(stored, (await client.UserAssetsAsync(Token)).Length);
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
        var listed = (await http.UserAssetsAsync(Token));
        using var upload = await http.PostAsync("/api/v1/assets?name=more.nam", new ByteArrayContent([5, 6]), Token);

        var asset = Assert.Single(listed);
        Assert.Equal(("sha256:" + hash, "nam", 4L), (asset.Ref, asset.Kind, asset.Size));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(2, (await http.UserAssetsAsync(Token)).Length);
        Assert.True(File.Exists(Path.Combine(folder, "index.json.damaged")));
        Assert.Equal(
            2,
            JsonSerializer.Deserialize<AssetInfo[]>(
                await File.ReadAllTextAsync(Path.Combine(folder, "index.json"), Token),
                JsonSerializerOptions.Web
            )!
                .Count(static a => a.Factory != true) // the factory assets are in every index
        );
    }

    [Fact]
    public async Task A_nam_capture_says_whether_it_holds_more_than_one_size() {
        var a2 = """{"architecture":"SlimmableContainer","config":{"submodels":[]}}"""u8.ToArray();
        var a1 = """{"architecture":"WaveNet","config":{"layers":[{"channels":16}]}}"""u8.ToArray();

        async Task<AssetInfo> Upload(string name, byte[] content) {
            using var response = await client.PostAsync(
                $"/api/v1/assets?name={name}",
                new ByteArrayContent(content),
                Token
            );
            return (await response.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
        }

        Assert.True((await Upload("a2.nam", a2)).Slimmable);
        Assert.False((await Upload("a1.nam", a1)).Slimmable);
        Assert.Null(
            (await Upload("model.aidax", [..a2, (byte)' '])).Slimmable
        ); // other bytes: the store keeps one file per content
        Assert.Null((await Upload("ir.wav", Wav(1, 1, 8, 4, [255, 200, 128, 128]))).Slimmable);
    }

    [Fact]
    public async Task An_index_written_before_captures_recorded_their_sizes_gets_them_from_the_files() {
        await using var old = new ServerFixture();
        var folder = Directory.CreateDirectory(Path.Combine(old.DataDirectory, "assets")).FullName;
        var content = """{"architecture":"SlimmableContainer","config":{"submodels":[]}}"""u8.ToArray();
        var reference = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
        await File.WriteAllBytesAsync(Path.Combine(folder, reference[7..] + ".nam"), content, Token);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "index.json"),
            $$"""
              [{"ref":"{{reference}}","name":"Mine.nam","kind":"nam","size":{{content.Length}},
                "added":"2026-01-02T03:04:05+00:00","favorite":true}]
              """,
            Token
        );

        using var http = await old.ConnectedClientAsync();
        var asset = Assert.Single(await http.UserAssetsAsync(Token)); // the factory impulse responses are listed too

        Assert.Equal(("Mine.nam", true, true), (asset.Name, asset.Favorite, asset.Slimmable));
        Assert.Contains(
            "\"slimmable\":true",
            (await File.ReadAllTextAsync(Path.Combine(folder, "index.json"), Token)).Replace(
                " ",
                "",
                StringComparison.Ordinal
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task An_upload_that_fails_while_it_is_read_leaves_no_temporary_file() {
        var store = server.Services.GetRequiredService<AssetStore>();
        var folder = Path.Combine(server.DataDirectory, "assets");

        // The client goes away mid-body, Kestrel refuses a chunked body at the request limit, and the store's own
        // answers for an empty upload and for a file that is not a WAV.
        await Assert.ThrowsAsync<IOException>(() => store.AddAsync(
                new FailingStream(100_000, new IOException("The client went away.")),
                "gone.nam",
                Token
            )
        );
        await Assert.ThrowsAsync<BadHttpRequestException>(() => store.AddAsync(
                new FailingStream(100_000, new BadHttpRequestException("Request body too large.", 413)),
                "chunked.nam",
                Token
            )
        );
        await Assert.ThrowsAsync<RigException>(() => store.AddAsync(new MemoryStream(), "empty.nam", Token));
        await Assert.ThrowsAsync<RigException>(() => store.AddAsync(new MemoryStream([1, 2, 3]), "bad.wav", Token));

        Assert.Empty(Directory.EnumerateFiles(folder, "upload-*"));
        Assert.DoesNotContain(store.List(), static a => a.Factory != true);
    }

    [Fact]
    public async Task Unfinished_uploads_of_an_earlier_run_are_removed_at_start_up() {
        await using var restarted = new ServerFixture();
        var folder = Directory.CreateDirectory(Path.Combine(restarted.DataDirectory, "assets")).FullName;
        var stale = Path.Combine(folder, $"upload-{Guid.NewGuid():n}.tmp");
        await File.WriteAllBytesAsync(stale, new byte[1_000], Token);
        // Only the store's own temporary files are its to delete.
        var other = Path.Combine(folder, "upload-notes.txt");
        await File.WriteAllTextAsync(other, "mine", Token);

        using var http = await restarted.ConnectedClientAsync();
        using var upload = await http.PostAsync("/api/v1/assets?name=a.nam", new ByteArrayContent([5, 6]), Token);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(other));
    }

    /// <summary>A request body that gives <c>good</c> bytes and then fails, as a dropped connection does.</summary>
    sealed class FailingStream(int good, Exception failure) : Stream {
        int left = good;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            if (left == 0) {
                throw failure;
            }

            var read = Math.Min(left, count);
            Array.Clear(buffer, offset, read);
            left -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
