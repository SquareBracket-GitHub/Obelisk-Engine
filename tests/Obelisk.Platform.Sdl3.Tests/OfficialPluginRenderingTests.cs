using System.Numerics;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Effects;
using Obelisk.Plugins.Text;
using SDL3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed partial class Sdl3ApplicationPlatformTests
{
    [Fact]
    public void NativeDynamicTextAddsKoreanAndRerasterizesZoomedGlyphs()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var face = FontFace.LoadFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf"));
        using var font = new DynamicFont(renderer, face);
        var frame = new RenderContext(default); frame.Canvas.FillRectangle(new(0, 0, 256, 256), new(0, 0, 0));
        font.Draw(frame, "가A", new(4, 4), 32);
        var cached = font.CachedGlyphCount;
        font.Draw(frame, "가A", new(4, 80), 32, scale: 2);
        Assert.True(font.CachedGlyphCount > cached);
        var pixels = RenderPixels(renderer, frame.Canvas, 256);
        var small = 0; var large = 0;
        for (var y = 0; y < 220; y++) for (var x = 0; x < 220; x++)
            if (pixels[(y * 256 + x) * 4] > 100) { if (y < 70) small++; else large++; }
        Assert.True(small > 100); Assert.InRange((float)large / small, 3.0f, 5.2f);
        font.Draw(new RenderContext(new GameTime(TimeSpan.Zero, TimeSpan.Zero, 1)), "새로운 한글", Vector2.Zero);
        font.Dispose(); Assert.Equal(1, renderer.TextureCount);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(128)]
    public void NativeMsdfMedianShaderKeepsEdgesNarrowAcrossMagnification(int size)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        const int width = 32; var bytes = new byte[width * width * 4];
        for (var y = 0; y < width; y++) for (var x = 0; x < width; x++)
        {
            var dx = Math.Min(x + 0.5f - 8, 24 - (x + 0.5f));
            var dy = Math.Min(y + 0.5f - 8, 24 - (y + 0.5f)); var i = (y * width + x) * 4;
            bytes[i] = Encode(dx); bytes[i + 1] = Encode(dy); bytes[i + 2] = Encode(Math.Min(dx, dy)); bytes[i + 3] = 255;
        }
        var metadata = """
            {"atlas":{"type":"msdf","width":32,"height":32,"distanceRange":4,"yOrigin":"bottom"},
             "metrics":{"emSize":1,"ascender":1,"lineHeight":1},
             "glyphs":[{"unicode":65,"advance":1,"planeBounds":{"left":0,"bottom":0,"right":1,"top":1},"atlasBounds":{"left":0,"bottom":0,"right":32,"top":32}}]}
            """;
        using var font = new MsdfFont(renderer, new MsdfAtlas(new RgbaImage(width, width, bytes), metadata));
        var frame = new RenderContext(default); frame.Canvas.FillRectangle(new(0, 0, 256, 256), new(0, 0, 0));
        font.Draw(frame, "A", new(0.3f, 0.3f), size);
        var pixels = RenderPixels(renderer, frame.Canvas, 256);
        Pixel(pixels, 256, size / 2, size / 2, 255, 255, 255);
        Pixel(pixels, 256, 1, 1, 0, 0, 0);
        var fractional = Enumerable.Range(0, size).Count(x => pixels[(size / 2 * 256 + x) * 4] is > 5 and < 250);
        Assert.InRange(fractional, 1, 4);
        static byte Encode(float distance) => (byte)Math.Clamp((int)MathF.Round((0.5f + distance / 4) * 255), 0, 255);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void NativeGaussianBlurSpreadsImpulseWithoutDarkeningTransparentColor(int samples)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm, samples);
        var bytes = new byte[16 * 16 * 4]; var center = (8 * 16 + 8) * 4;
        for (var channel = 0; channel < 4; channel++) bytes[center + channel] = 255;
        using var source = renderer.CreateTexture(new RgbaImage(16, 16, bytes));
        using var blur = new BlurEffect(renderer);
        var frame = new RenderContext(default); frame.Canvas.FillRectangle(new(0, 0, 32, 32), new(0, 0, 1));
        blur.Draw(frame, source, new(0, 0, 16, 16), new BlurSettings { Radius = 3, Sigma = 1 });
        var pixels = RenderPixels(renderer, frame.Canvas, 32);
        var a = (8 * 32 + 8) * 4; var b = (8 * 32 + 9) * 4;
        Assert.InRange((int)pixels[a], 30, 55); Assert.InRange((int)pixels[b], 12, 40);
        Assert.InRange((int)pixels[a + 2], 253, 255); Assert.InRange((int)pixels[b + 2], 253, 255);
        Assert.Equal(4, renderer.TextureCount); blur.Dispose(); Assert.Equal(2, renderer.TextureCount);
    }

    [Fact]
    public void NativeBlurDownsamplingAndTransparentConstantEdgesRemainCorrect()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        var bytes = new byte[16 * 16 * 4];
        for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = 255; bytes[i + 3] = 128; }
        using var source = renderer.CreateTexture(new RgbaImage(16, 16, bytes)); using var blur = new BlurEffect(renderer);
        var frame = new RenderContext(default); frame.Canvas.FillRectangle(new(0, 0, 32, 32), new(0, 0, 1));
        blur.Draw(frame, source, new(0, 0, 16, 16), new BlurSettings { Radius = 6, Sigma = 2, Downsample = 2 });
        var pixels = RenderPixels(renderer, frame.Canvas, 32);
        Pixel(pixels, 32, 0, 0, 128, 0, 127); Pixel(pixels, 32, 8, 8, 128, 0, 127);
    }
}
