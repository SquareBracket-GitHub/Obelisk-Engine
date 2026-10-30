using System.Numerics;
using System.Text;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Effects;
using Obelisk.Plugins.Text;
using SkiaSharp;

namespace Obelisk.Plugins.Tests;

public sealed class PluginFeatureTests
{
    private static string FontPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
    private static RenderContext Frame(ulong number) => new(new GameTime(TimeSpan.Zero, TimeSpan.Zero, number));
    [Fact]
    public void DynamicGlyphsReuseUploadsAndCreateActualSizeAndDpiVariants()
    {
        if (!OperatingSystem.IsWindows()) return;
        var device = new Device(); using var face = FontFace.LoadFile(FontPath);
        using var font = new DynamicFont(device, face);
        var measured = font.Measure("안녕 A");
        Assert.True(measured.X > 0); Assert.Equal(0, device.Created);
        var first = Frame(0); font.Draw(first, "안녕 A", Vector2.Zero);
        var uploads = device.Updates; var count = font.CachedGlyphCount;
        font.Draw(first, "안녕 A", new(0, 40));
        Assert.Equal(uploads, device.Updates);
        font.Draw(first, "새 글자", new(0, 80)); Assert.True(device.Updates > uploads);
        font.Draw(first, "안녕 A", new(0, 120), scale: 2);
        Assert.True(font.CachedGlyphCount > count);
        Assert.Equal(font.Measure("안녕 A", scale: 2), font.Measure("안녕 A", dpiScale: 2));
        Assert.InRange(font.AtlasBytes, 1, 32L * 1024 * 1024);
        Assert.All(first.Canvas.Commands, c => Assert.Equal(TextureFilter.Linear, c.Filter));
        font.Dispose(); Assert.Equal(device.Created, device.Released); Assert.False(face.IsDisposed);
    }
    [Fact]
    public void DynamicCacheRejectsLiveFrameEvictionButReusesOldPagesOnNewFrames()
    {
        if (!OperatingSystem.IsWindows()) return;
        var device = new Device(); using var face = FontFace.LoadFile(FontPath);
        using var font = new DynamicFont(device, face, options: new TextCacheOptions { PageSize = 64, MaximumPages = 1, MaximumGlyphs = 1 });
        var frame = Frame(0); font.Draw(frame, "가", Vector2.Zero, 24);
        var original = Assert.IsType<SpriteCommand>(Assert.Single(frame.Canvas.Commands)).Texture;
        Assert.Throws<InvalidOperationException>(() => font.Draw(frame, "나", Vector2.Zero, 24));
        Assert.Single(frame.Canvas.Commands); Assert.Equal(0, device.Released);
        font.Draw(Frame(1), "나", Vector2.Zero, 24);
        Assert.Equal(1, device.Released); Assert.Equal(1, font.AtlasPageCount);
        Assert.Equal(1, font.CachedGlyphCount);
        Assert.NotEqual(original, device.Textures.Last().Handle);
        Assert.Throws<InvalidOperationException>(() => font.Draw(Frame(0), "가", Vector2.Zero));
    }
    [Fact]
    public void TextFailureAndHelpersPreserveCallerStateAndRespectFaces()
    {
        if (!OperatingSystem.IsWindows()) return;
        var device = new Device(); using var face = FontFace.LoadFile(FontPath);
        using var font = new DynamicFont(device, face);
        using var material = device.CreateMaterial("unused");
        var context = Frame(0); context.Canvas.Material = material; context.Canvas.Blend = BlendMode.Opaque;
        context.Canvas.Parameters = Vector4.One;
        font.Draw(context, "한글\nText", Vector2.Zero);
        Assert.Same(material, context.Canvas.Material); Assert.Equal(BlendMode.Opaque, context.Canvas.Blend);
        Assert.Equal(Vector4.One, context.Canvas.Parameters);
        var before = context.Canvas.Commands.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Draw(context, "a", Vector2.Zero, float.NaN));
        using var strict = new DynamicFont(device, face, options: new TextCacheOptions { StrictMissingGlyphs = true });
        Assert.Throws<InvalidDataException>(() => strict.Draw(context, "A" + new Rune(0x10ffff), Vector2.Zero));
        Assert.Equal(before, context.Canvas.Commands.Count);
        font.Draw(context, new Rune(0x10ffff).ToString(), Vector2.Zero);
        face.Dispose(); Assert.Throws<ObjectDisposedException>(() => font.Measure("a"));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2049)]
    public void TextBudgetsRejectUnboundedAllocations(int page)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var face = FontFace.LoadFile(FontPath);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicFont(new Device(), face, options: new TextCacheOptions { PageSize = page }));
    }
    [Fact]
    public void RasterFontFallbackFindsKoreanInTheConfiguredSecondFace()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var latin = FontFace.LoadFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"));
        using var korean = FontFace.LoadFile(FontPath);
        Assert.False(latin.HasGlyph(new Rune('한'))); Assert.True(korean.HasGlyph(new Rune('한')));
        using var font = new DynamicFont(new Device(), latin, [korean], new TextCacheOptions { StrictMissingGlyphs = true });
        var frame = Frame(0); font.Draw(frame, "Latin 한글", Vector2.Zero);
        Assert.True(frame.Canvas.Commands.Count >= 7);
    }
    [Fact]
    public void BlurBypassPassesReuseResizeAndStateAreExplicit()
    {
        var device = new Device(); using var effect = new BlurEffect(device);
        using var source = device.CreateTexture(new RgbaImage(8, 8, new byte[8 * 8 * 4]));
        var frame = Frame(0);
        Assert.Same(source, effect.Apply(frame, source, new BlurSettings { Radius = 0 }));
        Assert.Empty(frame.Canvas.Commands); Assert.Equal(0, device.Targets);
        Assert.Throws<ArgumentOutOfRangeException>(() => effect.Draw(frame, source, new(0, 0, 16, 16), tint: new(float.NaN, 1, 1)));
        Assert.Empty(frame.Canvas.Commands); Assert.Equal(0, device.Targets);
        effect.Draw(frame, source, new(0, 0, 16, 16));
        Assert.Equal(2, device.Targets);
        Assert.Equal(2, frame.Canvas.Commands.OfType<RenderPassCommand>().Count());
        var created = device.Created;
        effect.Draw(frame, source, new(20, 0, 16, 16)); Assert.Equal(created, device.Created);
        Assert.Throws<InvalidOperationException>(() => effect.Apply(frame, source, new BlurSettings { Downsample = 2 }));
        effect.Apply(Frame(1), source, new BlurSettings { Downsample = 2 });
        Assert.Equal(4, device.Targets); Assert.Equal(2, device.Released);
        Assert.Throws<ArgumentOutOfRangeException>(() => effect.Apply(Frame(2), source, new BlurSettings { Radius = -1 }));
        effect.Dispose(); Assert.Throws<ObjectDisposedException>(() => effect.Apply(Frame(2), source));
    }
    [Fact]
    public void PartialResourceCreationFailureRollsBackAndCanRetry()
    {
        var device = new Device { FailTarget = 2 }; using var effect = new BlurEffect(device);
        using var source = device.CreateTexture(new RgbaImage(1, 1, [255, 255, 255, 255]));
        var frame = Frame(0);
        Assert.Throws<InvalidOperationException>(() => effect.Apply(frame, source));
        Assert.Equal(1, device.Released); Assert.Empty(frame.Canvas.Commands);
        device.FailTarget = -1; effect.Apply(frame, source);
        Assert.Equal(2, frame.Canvas.Commands.Count);
    }
    [Fact]
    public void MsdfMetadataDimensionsGlyphsAndKerningAreValidated()
    {
        var atlas = new MsdfAtlas(new RgbaImage(8, 8, new byte[8 * 8 * 4]), Metadata());
        var device = new Device(); using var font = new MsdfFont(device, atlas);
        Assert.Equal(1, atlas.GlyphCount);
        Assert.Equal(new Vector2(30, 20), font.Measure("AA", 20));
        var frame = Frame(0); font.Draw(frame, "A", Vector2.Zero, 64);
        var draw = Assert.IsType<SpriteCommand>(Assert.Single(frame.Canvas.Commands));
        Assert.NotNull(draw.Material); Assert.Equal(TextureFilter.Linear, draw.Filter);
        Assert.Equal(64, draw.Destination.Width);
        Assert.Throws<InvalidDataException>(() => new MsdfAtlas(atlas.Image, Metadata().Replace("\"width\":8", "\"width\":9")));
        Assert.Throws<InvalidDataException>(() => new MsdfAtlas(atlas.Image, Metadata().Replace("\"type\":\"msdf\"", "\"type\":\"sdf\"")));
        Assert.Throws<InvalidDataException>(() => new MsdfAtlas(atlas.Image, Metadata().Replace("\"unicode\":65", "\"unicode\":55296")));
    }
    [Fact]
    public void MsdfPngLoaderPreservesLinearBytesAndRejectsMalformedMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "obelisk-msdf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "font.json");
            File.WriteAllText(path, Metadata());
            using var bitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            bitmap.Erase(new SKColor(47, 129, 201, 255));
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(Path.ChangeExtension(path, ".png"))) data.SaveTo(file);
            var atlas = MsdfAtlas.LoadFile(path);
            Assert.Equal(new byte[] { 47, 129, 201, 255 }, atlas.Image.Pixels.Span[..4].ToArray());
            var device = new Device();
            using var bottomFont = new MsdfFont(device, atlas);
            var topMetadata = Metadata().Replace("\"yOrigin\":\"bottom\"", "\"yOrigin\":\"top\"")
                .Replace("\"ascender\":1", "\"ascender\":-1")
                .Replace("\"bottom\":0,\"right\":1,\"top\":1", "\"bottom\":0,\"right\":1,\"top\":-1")
                .Replace("\"bottom\":0,\"right\":8,\"top\":8", "\"bottom\":8,\"right\":8,\"top\":0");
            using var topFont = new MsdfFont(device, new MsdfAtlas(atlas.Image, topMetadata));
            var bottom = Frame(0); var top = Frame(0);
            bottomFont.Draw(bottom, "A", Vector2.Zero); topFont.Draw(top, "A", Vector2.Zero);
            Assert.Equal(Assert.IsType<SpriteCommand>(bottom.Canvas.Commands[0]).Destination,
                Assert.IsType<SpriteCommand>(top.Canvas.Commands[0]).Destination);
            Assert.Throws<InvalidDataException>(() => new MsdfAtlas(atlas.Image, Metadata().Replace("\"width\":8", "\"width\":8,\"width\":8")));
            Assert.Throws<InvalidDataException>(() => new MsdfAtlas(atlas.Image, Metadata().Replace("\"right\":8", "\"right\":9")));
            File.WriteAllText(path, "{}");
            Assert.Throws<InvalidDataException>(() => MsdfAtlas.LoadFile(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static string Metadata() => """
        {"atlas":{"type":"msdf","width":8,"height":8,"distanceRange":4,"yOrigin":"bottom"},
         "metrics":{"emSize":1,"ascender":1,"lineHeight":1},
         "glyphs":[{"unicode":65,"advance":1,"planeBounds":{"left":0,"bottom":0,"right":1,"top":1},"atlasBounds":{"left":0,"bottom":0,"right":8,"top":8}}],
         "kerning":[{"unicode1":65,"unicode2":65,"advance":-0.5}]}
        """;

    private sealed class Device : IRenderDevice
    {
        private ulong _next;
        public int Created, Released, Updates, Targets;
        public int FailTarget = -1;
        public readonly List<Texture> Textures = [];
        public int SampleCount => 1; public float DisplayScale => 1;
        public Texture CreateTexture(RgbaImage image)
        { Created++; var texture = new Texture(new(++_next), image.Width, image.Height, () => Released++); Textures.Add(texture); return texture; }
        public void UpdateTexture(Texture texture, int x, int y, RgbaImage image) { Updates++; }
        public RenderTarget CreateRenderTarget(int width, int height)
        { if (++Targets == FailTarget) throw new InvalidOperationException("Allocation failed"); return new(CreateTexture(new(width, height, new byte[width * height * 4]))); }
        public Material CreateMaterial(string fragmentFunction) => new(++_next, () => { });
    }
}
