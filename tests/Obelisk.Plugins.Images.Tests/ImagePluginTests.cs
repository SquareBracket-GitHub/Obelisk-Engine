using System.Net;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Images;

namespace Obelisk.Plugins.Images.Tests;

public sealed class ImagePluginTests
{
    private static byte[] ImageBytes => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAHUlEQVR4AQESAO3/AP8AAP8A/wD/AAAA////////ScgJ962rVhsAAAAASUVORK5CYII=");

    [Fact]
    public void Decode_PngAndFileImportProduceRgbaPixels()
    {
        using var plugin = new ImagePlugin();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
        var decoded = plugin.Decode(png);
        Assert.Equal(1, decoded.Width);
        Assert.Equal(4, decoded.Pixels.Length);
        var path = Path.Combine(Path.GetTempPath(), $"obelisk-image-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, ImageBytes);
            var device = new FakeTextures();
            using var texture = plugin.LoadTexture(device, path);
            Assert.Equal(2, texture.Width);
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, device.Image!.Pixels.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Decode_RejectsCorruptAndOversizedImagesAndMissingPaths()
    {
        using var plugin = new ImagePlugin();
        Assert.Throws<InvalidDataException>(() => plugin.Decode([1, 2, 3]));
        var oversized = ImageBytes;
        oversized[18] = 0x20; // PNG width = 8193, beyond the 8192 dimension limit.
        oversized[19] = 0x01;
        var failure = Assert.Throws<InvalidDataException>(() => plugin.Decode(oversized));
        Assert.IsType<ArgumentOutOfRangeException>(failure.InnerException);
        Assert.Throws<ArgumentException>(() => new RgbaImage(2, 2, [0]));
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");
        Assert.Contains(path, Assert.Throws<IOException>(() => plugin.LoadFile(path)).Message);
    }

    [Fact]
    public async Task UrlImport_DecodesWithoutCreatingGpuResources()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ImageBytes),
        })));
        using var plugin = new ImagePlugin(http);
        var image = await plugin.LoadUrlAsync(new Uri("https://images.example.test/sprite.png"));
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(255, image.Pixels.Span[0]);
    }

    [Fact]
    public async Task UrlImport_RejectsSchemeStatusOversizeAndHonorsCancellation()
    {
        using var errorHttp = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        using var errorPlugin = new ImagePlugin(errorHttp);
        await Assert.ThrowsAsync<ArgumentException>(() => errorPlugin.LoadUrlAsync(new Uri("file:///sprite.png")));
        await Assert.ThrowsAsync<HttpRequestException>(() => errorPlugin.LoadUrlAsync(new Uri("https://images.example.test/missing")));
        using var largeHttp = new HttpClient(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ImageBytes) };
            response.Content.Headers.ContentLength = ImagePlugin.MaximumEncodedBytes + 1;
            return Task.FromResult(response);
        }));
        using var largePlugin = new ImagePlugin(largeHttp);
        await Assert.ThrowsAsync<InvalidDataException>(() => largePlugin.LoadUrlAsync(new Uri("https://images.example.test/large")));
        using var slowHttp = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var slowPlugin = new ImagePlugin(slowHttp);
        using var cancellation = new CancellationTokenSource();
        var pending = slowPlugin.LoadUrlAsync(new Uri("https://images.example.test/slow"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Fact]
    public async Task UrlImport_EnforcesLimitWithoutContentLength()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnboundedStream()),
        })));
        using var plugin = new ImagePlugin(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => plugin.LoadUrlAsync(new Uri("https://images.example.test/chunked")));
    }

    private sealed class UnboundedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeTextures : ITextureDevice
    {
        public RgbaImage? Image { get; private set; }
        public Texture CreateTexture(RgbaImage image)
        {
            Image = image;
            return new Texture(new(1), image.Width, image.Height, () => { });
        }
    }
}
