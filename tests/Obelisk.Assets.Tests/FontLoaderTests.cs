using System.Numerics;
using Obelisk.Assets;
using Obelisk.Graphics2D;

namespace Obelisk.Assets.Tests;

public sealed class FontLoaderTests
{
    [Fact]
    public void LoadFile_RasterizesLatinAndKorean_AndMeasuresText()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
        if (!File.Exists(path)) return;
        var device = new FakeTextureDevice();
        using var font = new FontLoader(device, 24, "안녕").LoadFile(path);
        Assert.NotNull(device.LastImage);
        Assert.Contains(device.LastImage!.Pixels.ToArray(), pixel => pixel != 0);
        Assert.True(font.Measure("Hello 안녕").X > 0);
        Assert.Equal(font.LineHeight * 2, font.Measure("A\n안").Y);
        var canvas = new Canvas2D();
        canvas.DrawText(font, "Hello 안녕", new Vector2(10, 20));
        Assert.IsType<TextCommand>(Assert.Single(canvas.Commands));
        Assert.Throws<ArgumentException>(() => canvas.DrawText(font, "없는글자", Vector2.Zero));
        font.Dispose();
        Assert.True(device.Disposed);
        Assert.Throws<ObjectDisposedException>(() => canvas.DrawText(font, "A", Vector2.Zero));
    }

    private sealed class FakeTextureDevice : ITextureDevice
    {
        public RgbaImage? LastImage { get; private set; }
        public bool Disposed { get; private set; }

        public Texture CreateTexture(RgbaImage image)
        {
            LastImage = image;
            return new Texture(new TextureHandle(1), image.Width, image.Height, () => Disposed = true);
        }
    }
}
