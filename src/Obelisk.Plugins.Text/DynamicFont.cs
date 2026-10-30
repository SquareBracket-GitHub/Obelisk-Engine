using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using SkiaSharp;

namespace Obelisk.Plugins.Text;

/// <summary>Owns bounded dynamic raster atlas pages and borrows font faces. Draw at the actual size instead of scaling a fixed bitmap.</summary>
public sealed class DynamicFont : IDisposable
{
    private readonly ITextureDevice _device;
    private readonly FontFace[] _faces;
    private readonly TextCacheOptions _options;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<Page> _pages = [];
    private readonly Dictionary<(int Face, ushort Glyph, int Size), Glyph> _glyphs = [];
    private ulong? _frame;
    private sealed class Page(Texture texture)
    {
        internal readonly Texture Texture = texture;
        internal int X, Y, Row;
        internal ulong LastFrame;
    }
    private sealed record Glyph(Page? Page, RectangleF Source, float Left, float Top, float Advance);
    private readonly record struct Placement(int X, int Y, int NextX, int NextY, int Row);

    /// <summary>Borrows a primary face and up to 15 fallback faces. All faces must outlive the cache.</summary>
    public DynamicFont(ITextureDevice device, FontFace primary, IEnumerable<FontFace>? fallbacks = null, TextCacheOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(primary);
        _device = device; _options = options ?? new TextCacheOptions(); _options.Validate();
        _faces = new[] { primary }.Concat(fallbacks ?? []).Take(17).ToArray();
        if (_faces.Length > 16 || _faces.Any(f => f is null)) throw new ArgumentException("Expected at most 16 nonnull font faces.", nameof(fallbacks));
        foreach (var face in _faces) face.EnsureAlive();
    }
    /// <summary>Gets the number of resident GPU atlas pages.</summary>
    public int AtlasPageCount => _pages.Count;
    /// <summary>Gets the number of cached glyph/size entries, including whitespace.</summary>
    public int CachedGlyphCount => _glyphs.Count;
    /// <summary>Gets resident atlas RGBA8 bytes, excluding backend MSAA or driver bookkeeping.</summary>
    public long AtlasBytes => (long)_pages.Count * _options.PageSize * _options.PageSize * 4;
    /// <summary>Gets whether GPU cache resources have been released.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Measures output pixels without uploading glyphs. Size is multiplied by scale and explicit DPI scale; supports line breaks and four-space tabs.</summary>
    public Vector2 Measure(string text, float size = 32, float scale = 1, float dpiScale = 1)
    {
        EnsureAlive(); var pixels = Validate(text, size, scale, dpiScale);
        using var fonts = new FontSet(_faces, (int)MathF.Ceiling(pixels));
        var factor = pixels / fonts.Size;
        var x = 0f; var maximum = 0f; var lines = 1;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n') { maximum = Math.Max(maximum, x); x = 0; lines++; continue; }
            var (_, font, scalar) = Select(fonts, rune.Value == '\t' ? new Rune(' ') : rune);
            x += font.MeasureText(scalar.ToString()) * (rune.Value == '\t' ? 4 : 1);
        }
        return new(Math.Max(maximum, x) * factor, lines * fonts.LineHeight * factor);
    }

    /// <summary>Queues monochrome glyphs using actual-size rasterization and linear sampling. Frame numbers must be monotonic; never reuse the cache across different frame timelines.</summary>
    public void Draw(RenderContext context, string text, Vector2 position, float size = 32,
        ColorRgba? color = null, float scale = 1, float dpiScale = 1)
    {
        ArgumentNullException.ThrowIfNull(context);
        Draw(context.Canvas, context.Time.FrameNumber, text, position, size, color, scale, dpiScale);
    }

    /// <summary>Draws into a Canvas, including a RenderTo child. Supply the same physical frame number for every draw using this cache.</summary>
    public void Draw(Canvas2D canvas, ulong frameNumber, string text, Vector2 position, float size = 32,
        ColorRgba? color = null, float scale = 1, float dpiScale = 1)
    {
        EnsureAlive(); ArgumentNullException.ThrowIfNull(canvas);
        var pixels = Validate(text, size, scale, dpiScale);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        var tint = color ?? ColorRgba.White;
        if (!float.IsFinite(tint.R) || !float.IsFinite(tint.G) || !float.IsFinite(tint.B) || !float.IsFinite(tint.A)) throw new ArgumentOutOfRangeException(nameof(color));
        var frame = frameNumber;
        if (_frame is { } previous && frame < previous) throw new InvalidOperationException("Font frame numbers must not move backwards.");
        _frame = frame;
        using var fonts = new FontSet(_faces, (int)MathF.Ceiling(pixels));
        var factor = pixels / fonts.Size;
        var pen = position;
        var draws = new List<(Texture Texture, RectangleF Source, RectangleF Destination)>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n') { pen.X = position.X; pen.Y += fonts.LineHeight * factor; continue; }
            var (face, font, scalar) = Select(fonts, rune.Value == '\t' ? new Rune(' ') : rune);
            var key = (face, font.GetGlyph(scalar.Value), fonts.Size);
            if (!_glyphs.TryGetValue(key, out var glyph))
            {
                MakeGlyphRoom(frame);
                glyph = Rasterize(font, scalar, frame);
                _glyphs.Add(key, glyph);
            }
            if (glyph.Page is { } page)
            {
                page.LastFrame = frame;
                var destination = new RectangleF(pen.X + glyph.Left * factor, pen.Y + (fonts.Baseline + glyph.Top) * factor,
                    glyph.Source.Width * factor, glyph.Source.Height * factor);
                if (!destination.IsVisible) throw new ArgumentOutOfRangeException(nameof(position));
                draws.Add((page.Texture, glyph.Source, destination));
            }
            pen.X += glyph.Advance * factor * (rune.Value == '\t' ? 4 : 1);
            if (!float.IsFinite(pen.X) || !float.IsFinite(pen.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        }
        var blend = canvas.Blend; var filter = canvas.Filter; var material = canvas.Material; var parameters = canvas.Parameters;
        try
        {
            canvas.Blend = BlendMode.Alpha; canvas.Filter = TextureFilter.Linear; canvas.Material = null; canvas.Parameters = default;
            foreach (var draw in draws) canvas.DrawSprite(draw.Texture, draw.Destination, draw.Source, tint);
        }
        finally { canvas.Blend = blend; canvas.Filter = filter; canvas.Material = material; canvas.Parameters = parameters; }
    }

    private float Validate(string text, float size, float scale, float dpiScale)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > _options.MaximumTextLength) throw new ArgumentOutOfRangeException(nameof(text));
        var pixels = size * scale * dpiScale;
        if (!float.IsFinite(size) || !float.IsFinite(scale) || !float.IsFinite(dpiScale) || size <= 0 || scale <= 0 || dpiScale <= 0 ||
            !float.IsFinite(pixels) || pixels < 1 || pixels > 512) throw new ArgumentOutOfRangeException(nameof(size), "Effective raster size must be 1–512 pixels.");
        foreach (var face in _faces) face.EnsureAlive();
        return pixels;
    }
    private (int Face, SKFont Font, Rune Scalar) Select(FontSet fonts, Rune rune)
    {
        for (var i = 0; i < fonts.Fonts.Length; i++) if (fonts.Fonts[i].GetGlyph(rune.Value) != 0) return (i, fonts.Fonts[i], rune);
        if (!_options.StrictMissingGlyphs)
            foreach (var replacement in new[] { new Rune(0xfffd), new Rune('?') })
                for (var i = 0; i < fonts.Fonts.Length; i++) if (fonts.Fonts[i].GetGlyph(replacement.Value) != 0) return (i, fonts.Fonts[i], replacement);
        throw new InvalidDataException($"No configured font contains U+{rune.Value:X4} or a replacement glyph.");
    }
    private Glyph Rasterize(SKFont font, Rune rune, ulong frame)
    {
        var advance = font.MeasureText(rune.ToString(), out var bounds);
        if (!float.IsFinite(advance)) throw new InvalidDataException("Font contains nonfinite metrics.");
        if (bounds.IsEmpty) return new(null, default, 0, 0, advance);
        var left = (int)MathF.Floor(bounds.Left) - 2; var top = (int)MathF.Floor(bounds.Top) - 2;
        var width = checked((int)MathF.Ceiling(bounds.Right) - left + 2);
        var height = checked((int)MathF.Ceiling(bounds.Bottom) - top + 2);
        if (width > _options.PageSize || height > _options.PageSize) throw new InvalidOperationException("Glyph does not fit the configured atlas page. Increase PageSize or reduce font size.");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        { canvas.Clear(SKColors.Transparent); canvas.DrawText(rune.ToString(), -left, -top, SKTextAlign.Left, font, paint); }
        var bytes = new byte[width * height * 4]; Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        // Monochrome white RGB in transparent padding avoids dark halos under linear sampling.
        for (var i = 0; i < bytes.Length; i += 4) bytes[i] = bytes[i + 1] = bytes[i + 2] = 255;
        var page = FindPage(width, height, frame, out var placement);
        _device.UpdateTexture(page.Texture, placement.X, placement.Y, new RgbaImage(width, height, bytes));
        page.X = placement.NextX; page.Y = placement.NextY; page.Row = placement.Row; page.LastFrame = frame;
        return new(page, new(placement.X, placement.Y, width, height), left, top, advance);
    }
    private Page FindPage(int width, int height, ulong frame, out Placement placement)
    {
        foreach (var page in _pages) if (TryPlace(page, width, height, out placement)) return page;
        if (_pages.Count == _options.MaximumPages) EvictPage(frame);
        var bytes = new byte[_options.PageSize * _options.PageSize * 4];
        for (var i = 0; i < bytes.Length; i += 4) bytes[i] = bytes[i + 1] = bytes[i + 2] = 255;
        var created = new Page(_device.CreateTexture(new RgbaImage(_options.PageSize, _options.PageSize, bytes))) { LastFrame = frame };
        _pages.Add(created);
        if (!TryPlace(created, width, height, out placement)) throw new InvalidOperationException("Glyph page allocation failed.");
        return created;
    }
    private bool TryPlace(Page page, int width, int height, out Placement placement)
    {
        var x = page.X; var y = page.Y; var row = page.Row;
        if (x + width > _options.PageSize) { x = 0; y += row; row = 0; }
        placement = new(x, y, x + width, y, Math.Max(row, height));
        return y + height <= _options.PageSize;
    }
    private void MakeGlyphRoom(ulong frame)
    {
        if (_glyphs.Count < _options.MaximumGlyphs) return;
        var whitespace = _glyphs.FirstOrDefault(p => p.Value.Page is null);
        if (whitespace.Value is not null) { _glyphs.Remove(whitespace.Key); return; }
        EvictPage(frame);
    }
    private void EvictPage(ulong frame)
    {
        var page = _pages.Where(p => p.LastFrame != frame).MinBy(p => p.LastFrame)
            ?? throw new InvalidOperationException("Text cache budget is exhausted by this frame. Increase MaximumPages/MaximumGlyphs; live glyphs were preserved.");
        page.Texture.Dispose(); _pages.Remove(page);
        foreach (var key in _glyphs.Where(p => ReferenceEquals(p.Value.Page, page)).Select(p => p.Key).ToArray()) _glyphs.Remove(key);
    }
    private sealed class FontSet : IDisposable
    {
        internal readonly SKFont[] Fonts;
        internal readonly int Size;
        internal readonly float LineHeight, Baseline;
        internal FontSet(FontFace[] faces, int size)
        {
            Size = size; var created = new List<SKFont>();
            try
            {
                foreach (var face in faces) created.Add(face.CreateFont(size));
                Fonts = created.ToArray();
                var metrics = Fonts.Select(f => f.Metrics).ToArray();
                Baseline = -metrics.Min(m => m.Ascent);
                LineHeight = metrics.Max(m => m.Descent + m.Leading) + Baseline;
                if (!float.IsFinite(LineHeight) || LineHeight <= 0 || !float.IsFinite(Baseline)) throw new InvalidDataException("Invalid font line metrics.");
            }
            catch { foreach (var font in created) font.Dispose(); throw; }
        }
        public void Dispose() { foreach (var font in Fonts) font.Dispose(); }
    }
    private void EnsureAlive()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Text caches must be used on their owning thread.");
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
    /// <summary>Releases owning-thread GPU pages; borrowed font faces remain alive.</summary>
    public void Dispose()
    { if (IsDisposed) return; EnsureAlive(); foreach (var page in _pages) page.Texture.Dispose(); _pages.Clear(); _glyphs.Clear(); IsDisposed = true; }
}
