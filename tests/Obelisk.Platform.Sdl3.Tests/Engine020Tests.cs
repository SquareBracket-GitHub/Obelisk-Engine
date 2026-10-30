using System.Numerics;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed partial class Sdl3ApplicationPlatformTests
{
    [Fact]
    public void LargeCirclesKeepSubpixelChordError()
    {
        var canvas = new Canvas2D(); canvas.FillCircle(new(1200, 1200), 1024, ColorRgba.White);
        var mesh = CanvasMesh.Create(canvas.Commands, 2400, 2400, _ => (1, 1));
        var segments = mesh.Mesh.Vertices.Length - 1;
        Assert.True(1024 * (1 - Math.Cos(Math.PI / segments)) <= 0.25);
        Assert.True(segments > 128);
    }
    [Fact]
    public void WheelAccumulationIsFractionalAndSnapshotsResetIndependently()
    {
        var tracker = new InputStateTracker();
        tracker.BeginFrame();
        tracker.Apply(new(Sdl3EventType.MouseMoved, MouseX: 17, MouseY: 29));
        tracker.Apply(new(Sdl3EventType.MouseWheel, WheelX: 0.25f, WheelY: -0.5f));
        tracker.Apply(new(Sdl3EventType.MouseWheel, WheelX: 0.5f, WheelY: 2));
        var first = tracker.Capture();
        Assert.Equal(0.75f, first.Mouse.WheelX); Assert.Equal(1.5f, first.Mouse.WheelY);
        Assert.Equal(17, first.Mouse.X); Assert.Equal(29, first.Mouse.Y);
        tracker.BeginFrame();
        Assert.Equal(0, tracker.Capture().Mouse.WheelY);
        Assert.Equal(1.5f, first.Mouse.WheelY);
        Assert.Throws<InvalidDataException>(() => tracker.Apply(new(Sdl3EventType.MouseWheel, WheelY: float.NaN)));
    }

    [Fact]
    public void NativeWheelDirectionIsNormalizedAndDelivered()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope();
        scope.Platform.PumpEvents();
        var e = new SDL.Event
        {
            Wheel = new SDL.MouseWheelEvent
            {
                Type = SDL.EventType.MouseWheel,
                X = 0.25f,
                Y = 1.5f,
                Direction = SDL.MouseWheelDirection.Flipped
            }
        };
        Assert.True(SDL.PushEvent(ref e));
        scope.Platform.PumpEvents();
        Assert.Equal(-0.25f, scope.Platform.Input.Mouse.WheelX);
        Assert.Equal(-1.5f, scope.Platform.Input.Mouse.WheelY);
        scope.Platform.PumpEvents();
        Assert.Equal(0, scope.Platform.Input.Mouse.WheelY);
    }

    [Fact]
    public void NativeTextureUpdatesPreserveUntouchedPixelsAndFiltersDiffer()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var image = renderer.CreateTexture(new RgbaImage(2, 1, [255, 0, 0, 255, 255, 0, 0, 255]));
        renderer.UpdateTexture(image, 1, 0, new RgbaImage(1, 1, [0, 0, 255, 255]));
        var canvas = new Canvas2D(); canvas.Blend = BlendMode.Opaque;
        canvas.DrawSprite(image, new(0, 0, 32, 16));
        canvas.Filter = TextureFilter.Linear;
        canvas.DrawSprite(image, new(0, 16, 32, 16));
        var pixels = RenderPixels(renderer, canvas, 64);
        Pixel(pixels, 64, 2, 2, 255, 0, 0); Pixel(pixels, 64, 29, 2, 0, 0, 255);
        var nearest = (2 * 64 + 15) * 4; var linear = (18 * 64 + 15) * 4;
        Assert.Equal(255, pixels[nearest]);
        Assert.InRange((int)pixels[linear], 90, 170); Assert.InRange((int)pixels[linear + 2], 90, 170);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.UpdateTexture(image, 2, 0, new RgbaImage(1, 1, [0, 0, 0, 0])));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.UpdateTexture(image, -1, 0, new RgbaImage(1, 1, [0, 0, 0, 0])));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void NativeOrderedPassesAndCustomMaterialsPreserveTransparentCompositing(int samples)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm, samples);
        using var first = renderer.CreateRenderTarget(16, 16);
        using var second = renderer.CreateRenderTarget(16, 16);
        using var material = renderer.CreateMaterial("float4 Shade(float4 color, float2 uv) { float4 v=SampleImage(uv); return float4(v.b,v.r,v.g,v.a)*color*Parameters; }");
        var canvas = new Canvas2D();
        canvas.FillRectangle(new(0, 0, 64, 64), new(0, 0, 1));
        canvas.RenderTo(first, c => c.FillRectangle(new(0, 0, 16, 16), new(1, 0, 0, 0.5f)));
        canvas.DrawSprite(first.Texture, new(0, 0, 16, 16));
        canvas.RenderTo(second, c => { c.Material = material; c.Parameters = Vector4.One; c.DrawSprite(first.Texture, new(0, 0, 16, 16)); });
        canvas.DrawSprite(second.Texture, new(32, 0, 16, 16));
        var pixels = RenderPixels(renderer, canvas, 64);
        Pixel(pixels, 64, 8, 8, 128, 0, 127);
        Pixel(pixels, 64, 40, 8, 0, 128, 127);
        Pixel(pixels, 64, 24, 8, 0, 0, 255);
        Assert.InRange(renderer.SampleCount, 1, samples);
    }

    [Fact]
    public void NativeResourcesRejectFeedbackForeignDisposedAndBadShaderWithoutLeaks()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var other = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var target = renderer.CreateRenderTarget(8, 8);
        var canvas = new Canvas2D();
        canvas.RenderTo(target, c => c.DrawSprite(target.Texture, new(0, 0, 8, 8)));
        Assert.Throws<InvalidOperationException>(() => RenderPixels(renderer, canvas, 32));
        Assert.Throws<InvalidOperationException>(() => renderer.UpdateTexture(target.Texture, 0, 0, new RgbaImage(1, 1, [0, 0, 0, 0])));
        Assert.Throws<Sdl3Exception>(() => renderer.CreateMaterial("this is not HLSL"));
        using var foreignTexture = other.CreateTexture(new RgbaImage(1, 1, [255, 255, 255, 255]));
        Assert.Throws<InvalidOperationException>(() => renderer.UpdateTexture(foreignTexture, 0, 0, new RgbaImage(1, 1, [0, 0, 0, 0])));
        var released = renderer.CreateTexture(new RgbaImage(1, 1, [255, 255, 255, 255]));
        released.Dispose();
        Assert.Throws<ObjectDisposedException>(() => renderer.UpdateTexture(released, 0, 0, new RgbaImage(1, 1, [0, 0, 0, 0])));
        using var foreign = other.CreateMaterial("float4 Shade(float4 c,float2 uv){return c;}");
        var foreignCanvas = new Canvas2D { Material = foreign };
        foreignCanvas.FillRectangle(new(0, 0, 8, 8), ColorRgba.White);
        Assert.Throws<InvalidOperationException>(() => RenderPixels(renderer, foreignCanvas, 32));
        var survivor = renderer.CreateMaterial("float4 Shade(float4 c,float2 uv){return c;}");
        renderer.Dispose();
        Assert.True(survivor.IsDisposed); Assert.True(target.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => renderer.CreateRenderTarget(2, 2));
    }

    [Fact]
    public void NativeMsaaProducesFractionalShapeEdgeCoverage()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm, 4);
        var canvas = new Canvas2D();
        canvas.FillRectangle(new(0, 0, 64, 64), new(0, 0, 0));
        canvas.FillCircle(new(30.3f, 30.2f), 17.4f, ColorRgba.White);
        var pixels = RenderPixels(renderer, canvas, 64);
        var fractional = Enumerable.Range(0, 64 * 64).Count(i => pixels[i * 4] is > 0 and < 255);
        if (renderer.SampleCount > 1) Assert.True(fractional > 8, $"Expected MSAA edge coverage; got {fractional}.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void NativeRepeatedTargetWritesPreserveEarlierDrawsAndAlpha(int samples)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = new NativePlatformScope(); using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm, samples);
        using var target = renderer.CreateRenderTarget(8, 8);
        var canvas = new Canvas2D(); canvas.FillRectangle(new(0, 0, 64, 64), new(0, 0, 1));
        canvas.RenderTo(target, c => { c.Blend = BlendMode.Opaque; c.FillRectangle(new(0, 0, 8, 8), new(1, 0, 0, 0.5f)); });
        canvas.DrawSprite(target.Texture, new(0, 0, 8, 8));
        canvas.RenderTo(target, _ => { }, new(1, 0, 0, 0.5f));
        canvas.DrawSprite(target.Texture, new(16, 0, 8, 8));
        canvas.RenderTo(target, c =>
        {
            c.FillRectangle(new(0, 0, 8, 8), new(1, 0, 0, 0.5f));
            c.FillRectangle(new(0, 0, 8, 8), new(0, 1, 0, 0.5f));
        });
        canvas.DrawSprite(target.Texture, new(32, 0, 8, 8));
        var pixels = RenderPixels(renderer, canvas, 64);
        Pixel(pixels, 64, 4, 4, 128, 0, 127);
        Pixel(pixels, 64, 20, 4, 128, 0, 127);
        Pixel(pixels, 64, 36, 4, 64, 128, 63);
    }
}
