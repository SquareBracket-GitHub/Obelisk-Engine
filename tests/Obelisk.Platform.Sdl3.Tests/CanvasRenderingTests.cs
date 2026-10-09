using System.Numerics;
using System.Runtime.InteropServices;
using Obelisk.Abstractions;
using Obelisk.Assets;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Images;
using SDL3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed partial class Sdl3ApplicationPlatformTests
{
    [Fact]
    public void NativeText_LatinAndKoreanGlyphsProduceVisiblePixels()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
        if (!File.Exists(fontPath)) return;
        using var platform = new NativePlatformScope();
        using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var font = new FontLoader(renderer, 24, "안녕").LoadFile(fontPath);
        foreach (var text in new[] { "Hello", "안녕" })
        {
            var canvas = new Canvas2D();
            canvas.DrawText(font, text, new(4, 4), ColorRgba.White);
            var pixels = RenderPixels(renderer, canvas, 160);
            var bright = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (pixels[i] > 80 && pixels[i + 1] > 80 && pixels[i + 2] > 80) bright++;
            Assert.True(bright > 20, $"Expected visible '{text}' glyph pixels, got {bright}.");
        }
    }

    [Fact]
    public void NativeShowcase_DefaultImageAndMixedSceneRunThroughShutdown()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var game = new CanvasShowcase.ShowcaseGame(null, null);
        game.Initialize(new InitializeContext(platform.Platform.Textures));
        for (var frame = 0; frame < 12; frame++)
        {
            var time = new GameTime(TimeSpan.FromSeconds(frame / 60.0), TimeSpan.FromSeconds(1.0 / 60), (ulong)frame);
            game.Update(new UpdateContext(time));
            var context = new RenderContext(time, platform.Platform.OutputSize);
            game.Render(context);
            platform.Platform.Present(context);
        }
        game.Shutdown(new ShutdownContext());
    }

    [Fact]
    public void CanvasMesh_BatchesAdjacentStateAndPreservesOverlapOrder()
    {
        var canvas = new Canvas2D();
        canvas.FillRectangle(new(0, 0, 5, 5), ColorRgba.White);
        canvas.DrawLine(Vector2.Zero, new(10, 10), 2, ColorRgba.White);
        canvas.FillCircle(new(20, 20), 4, ColorRgba.White);
        canvas.DrawSprite(new TextureHandle(1), new(0, 0, 10, 10));
        canvas.DrawSprite(new TextureHandle(1), new(1, 1, 10, 10));
        canvas.DrawSprite(new TextureHandle(2), new(2, 2, 10, 10));
        canvas.DrawSprite(new TextureHandle(1), new(3, 3, 10, 10));
        canvas.Blend = BlendMode.Opaque;
        canvas.DrawSprite(new TextureHandle(1), new(4, 4, 10, 10));
        var mesh = CanvasMesh.Create(canvas.Commands, 64, 64, _ => (2, 2));
        Assert.Equal(5, mesh.Batches.Count);
        Assert.Equal(12U, mesh.Batches[1].Count);
        Assert.Equal(new ulong[] { 0, 1, 2, 1, 1 }, mesh.Batches.Select(b => b.Texture.Value));
        Assert.Equal(BlendMode.Opaque, mesh.Batches[^1].Blend);
        Assert.Equal((uint)mesh.Mesh.Indices.Length, mesh.Batches.Aggregate(0U, (sum, b) => sum + b.Count));
    }

    [Fact]
    public void NativeCanvas_PixelsVerifySpritesShapesTransformTintAndBlendingAfterResize()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var texture = LoadTestTexture(renderer);
        using var translucent = renderer.CreateTexture(new RgbaImage(1, 1, [0, 255, 0, 128]));
        var canvas = new Canvas2D();
        canvas.FillRectangle(new(0, 0, 128, 128), new(0, 0, 0));
        canvas.DrawSprite(texture, new(0, 0, 16, 16));
        canvas.DrawSprite(texture, new(20, 0, 16, 16), rotation: MathF.PI, origin: new(8, 8));
        canvas.DrawSprite(texture, new(40, 0, 16, 16), source: new(1, 1, 1, 1), tint: new(0.5f, 1, 0.25f));
        canvas.FillRectangle(new(0, 20, 16, 16), new(1, 0, 0, 0.5f));
        canvas.DrawLine(new(20, 28), new(36, 28), 6, new(0, 1, 0));
        canvas.FillCircle(new(48, 28), 7, new(0, 0, 1));
        canvas.DrawSprite(texture, new(20, 40, 16, 16), source: new(0, 0, 1, 1), tint: new(1, 1, 1, 0.5f));
        canvas.DrawSprite(translucent, new(40, 40, 16, 16));
        canvas.Blend = BlendMode.Opaque;
        canvas.FillRectangle(new(0, 40, 16, 16), new(1, 0, 0, 0.5f));
        foreach (var size in new[] { 64, 96, 64 })
        {
            Assert.True(SDL.MinimizeWindow(platform.Platform.WindowHandle));
            Assert.True(SDL.SyncWindow(platform.Platform.WindowHandle));
            platform.Platform.PumpEvents();
            Assert.True(SDL.RestoreWindow(platform.Platform.WindowHandle));
            Assert.True(SDL.SetWindowSize(platform.Platform.WindowHandle, size * 4, size * 3));
            Assert.True(SDL.SyncWindow(platform.Platform.WindowHandle));
            platform.Platform.PumpEvents();
            var pixels = RenderPixels(renderer, canvas, size);
            Pixel(pixels, size, 2, 2, 255, 0, 0);
            Pixel(pixels, size, 12, 2, 0, 255, 0);
            Pixel(pixels, size, 2, 12, 0, 0, 255);
            Pixel(pixels, size, 22, 2, 255, 255, 255); // Rotation about the center.
            Pixel(pixels, size, 42, 2, 128, 255, 64);
            Pixel(pixels, size, 2, 22, 128, 0, 0);
            Pixel(pixels, size, 28, 28, 0, 255, 0);
            Pixel(pixels, size, 48, 28, 0, 0, 255);
            Pixel(pixels, size, 40, 20, 0, 0, 0); // Outside the circle.
            Pixel(pixels, size, 2, 42, 255, 0, 0); // Opaque ignores alpha for RGB blending.
            Pixel(pixels, size, 22, 42, 128, 0, 0); // Sprite tint alpha.
            Pixel(pixels, size, 42, 42, 0, 128, 0); // Texture alpha.
        }
    }

    [Fact]
    public void NativeTextures_RepeatedReleaseStaleForeignAndShutdownLifetimes()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        var image = new RgbaImage(1, 1, [255, 255, 255, 255]);
        for (var i = 0; i < 24; i++)
        {
            var texture = renderer.CreateTexture(image);
            var canvas = new Canvas2D();
            canvas.DrawSprite(texture, new(0, 0, 16, 16));
            RenderPixels(renderer, canvas, 16);
            texture.Dispose();
            texture.Dispose();
            Assert.Equal(1, renderer.TextureCount); // Only the internal white texture remains.
            Assert.Throws<InvalidOperationException>(() => RenderPixels(renderer, canvas, 16));
            Assert.Throws<ObjectDisposedException>(() => canvas.DrawSprite(texture, new(0, 0, 1, 1)));
        }
        var survivor = renderer.CreateTexture(image);
        var foreign = new Canvas2D();
        foreign.DrawSprite(new TextureHandle(ulong.MaxValue), new(0, 0, 2, 2));
        Assert.Throws<InvalidOperationException>(() => RenderPixels(renderer, foreign, 16));
        Exception? wrongThread = null;
        var worker = new Thread(() => wrongThread = Record.Exception(survivor.Dispose));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.False(survivor.IsDisposed);
        renderer.Dispose();
        Assert.True(survivor.IsDisposed);
        survivor.Dispose();
    }

    [Fact]
    public void Present_SuspendsForMinimizeAndZeroSizeAndResumes()
    {
        var api = new FakeSdl3Api();
        var platform = CreateInitializedPlatform(api);
        try
        {
            var initial = api.Calls.Count(c => c.StartsWith("render-frame:", StringComparison.Ordinal));
            api.Events.Enqueue(new(Sdl3EventType.WindowMinimized));
            platform.PumpEvents();
            platform.Present(new RenderContext(default));
            Assert.Equal(initial, api.Calls.Count(c => c.StartsWith("render-frame:", StringComparison.Ordinal)));
            api.Events.Enqueue(new(Sdl3EventType.WindowRestored));
            api.Events.Enqueue(new(Sdl3EventType.WindowResized, Width: 0, Height: 0));
            platform.PumpEvents();
            platform.Present(new RenderContext(default));
            Assert.Equal(initial, api.Calls.Count(c => c.StartsWith("render-frame:", StringComparison.Ordinal)));
            api.Events.Enqueue(new(Sdl3EventType.WindowResized, Width: 480, Height: 270));
            platform.PumpEvents();
            platform.Present(new RenderContext(default));
            Assert.Equal(initial + 1, api.Calls.Count(c => c.StartsWith("render-frame:", StringComparison.Ordinal)));
        }
        finally { platform.Shutdown(); }
    }

    [Fact]
    public void NativeCanvas_WindowResizeMinimizeRestoreRetainsTexture()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var texture = platform.Platform.CreateTexture(new RgbaImage(1, 1, [255, 128, 64, 255]));
        var frame = new RenderContext(default);
        frame.Canvas.DrawSprite(texture, new(12, 14, 30, 40));
        frame.Canvas.DrawLine(new(0, 0), new(80, 80), 4, ColorRgba.White);
        frame.Canvas.FillCircle(new(100, 100), 20, ColorRgba.Orange);
        platform.Platform.Present(frame);
        Assert.True(SDL.MinimizeWindow(platform.Platform.WindowHandle));
        Assert.True(SDL.SyncWindow(platform.Platform.WindowHandle));
        platform.Platform.PumpEvents();
        platform.Platform.Present(frame);
        Assert.True(SDL.RestoreWindow(platform.Platform.WindowHandle));
        Assert.True(SDL.SetWindowSize(platform.Platform.WindowHandle, 480, 270));
        Assert.True(SDL.SyncWindow(platform.Platform.WindowHandle));
        platform.Platform.PumpEvents();
        for (var i = 0; i < 5; i++) platform.Platform.Present(frame);
        Assert.False(texture.IsDisposed);
    }

    private static GpuResource TestDevice() => new(Sdl3NativeApi.Instance.CreateGpuDevice(), h =>
    {
        SDL.WaitForGPUIdle(h);
        SDL.DestroyGPUDevice(h);
    });

    private static Texture LoadTestTexture(ITextureDevice device)
    {
        var path = Path.Combine(Path.GetTempPath(), $"obelisk-gpu-image-{Guid.NewGuid():N}.png");
        using var images = new ImagePlugin();
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAHUlEQVR4AQESAO3/AP8AAP8A/wD/AAAA////////ScgJ962rVhsAAAAASUVORK5CYII="));
            return images.LoadTexture(device, path);
        }
        finally { File.Delete(path); }
    }

    private static byte[] RenderPixels(CanvasRenderer renderer, Canvas2D canvas, int size)
    {
        var info = new SDL.GPUTextureCreateInfo
        {
            Type = SDL.GPUTextureType.TextureType2D,
            Format = SDL.GPUTextureFormat.R8G8B8A8Unorm,
            Usage = SDL.GPUTextureUsageFlags.ColorTarget,
            Width = (uint)size,
            Height = (uint)size,
            LayerCountOrDepth = 1,
            NumLevels = 1,
        };
        using var target = new GpuResource(SDL.CreateGPUTexture(renderer.Device, in info), h => SDL.ReleaseGPUTexture(renderer.Device, h));
        Assert.NotEqual(0, target.Handle);
        var commands = SDL.AcquireGPUCommandBuffer(renderer.Device);
        Assert.NotEqual(0, commands);
        try { Assert.True(renderer.Record(commands, target.Handle, (uint)size, (uint)size, canvas.Commands)); }
        catch { SDL.CancelGPUCommandBuffer(commands); throw; }
        var rowBytes = (size * 4 + 255) / 256 * 256;
        var transferInfo = new SDL.GPUTransferBufferCreateInfo { Usage = SDL.GPUTransferBufferUsage.Download, Size = (uint)(rowBytes * size) };
        using var transfer = new GpuResource(SDL.CreateGPUTransferBuffer(renderer.Device, in transferInfo), h => SDL.ReleaseGPUTransferBuffer(renderer.Device, h));
        Assert.NotEqual(0, transfer.Handle);
        var copy = SDL.BeginGPUCopyPass(commands);
        var source = new SDL.GPUTextureRegion { Texture = target.Handle, W = (uint)size, H = (uint)size, D = 1 };
        var destination = new SDL.GPUTextureTransferInfo { TransferBuffer = transfer.Handle, PixelsPerRow = (uint)rowBytes / 4, RowsPerLayer = (uint)size };
        SDL.DownloadFromGPUTexture(copy, in source, in destination);
        SDL.EndGPUCopyPass(copy);
        Assert.True(SDL.SubmitGPUCommandBuffer(commands));
        Assert.True(SDL.WaitForGPUIdle(renderer.Device));
        var mapped = SDL.MapGPUTransferBuffer(renderer.Device, transfer.Handle, false);
        Assert.NotEqual(0, mapped);
        var pixels = new byte[size * size * 4];
        for (var row = 0; row < size; row++) Marshal.Copy(mapped + row * rowBytes, pixels, row * size * 4, size * 4);
        SDL.UnmapGPUTransferBuffer(renderer.Device, transfer.Handle);
        return pixels;
    }

    private static void Pixel(byte[] pixels, int size, int x, int y, int r, int g, int b)
    {
        var offset = (y * size + x) * 4;
        Assert.InRange((int)pixels[offset], r - 1, r + 1);
        Assert.InRange((int)pixels[offset + 1], g - 1, g + 1);
        Assert.InRange((int)pixels[offset + 2], b - 1, b + 1);
    }
}
