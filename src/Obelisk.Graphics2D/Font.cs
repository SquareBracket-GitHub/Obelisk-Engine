using System.Numerics;
using System.Text;

namespace Obelisk.Graphics2D;

/// <summary>One glyph's location in a font atlas and its horizontal advance in pixels.</summary>
public readonly record struct FontGlyph(RectangleF Source, float Advance);

/// <summary>An immutable GPU glyph atlas. Dispose it on the texture device's owning thread.</summary>
public sealed class Font : IDisposable
{
    private readonly IReadOnlyDictionary<Rune, FontGlyph> _glyphs;

    public Font(Texture atlas, float lineHeight, IReadOnlyDictionary<Rune, FontGlyph> glyphs)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ObjectDisposedException.ThrowIf(atlas.IsDisposed, atlas);
        ArgumentNullException.ThrowIfNull(glyphs);
        if (!float.IsFinite(lineHeight) || lineHeight <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        Atlas = atlas;
        LineHeight = lineHeight;
        _glyphs = new Dictionary<Rune, FontGlyph>(glyphs);
    }

    public Texture Atlas { get; }
    public float LineHeight { get; }
    private bool _disposed;
    public bool IsDisposed => _disposed || Atlas.IsDisposed;

    public FontGlyph GetGlyph(Rune rune)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!_glyphs.TryGetValue(rune, out var glyph))
            throw new ArgumentException($"Font atlas does not contain U+{rune.Value:X4}. Include it in the character set when loading the font.");
        return glyph;
    }

    /// <summary>Measures text at the same uniform scale used by Canvas.DrawText.</summary>
    public Vector2 Measure(string text, float scale = 1)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale), "Text scale must be finite and greater than zero.");
        var width = 0f;
        var maximum = 0f;
        var lines = 1;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                maximum = MathF.Max(maximum, width);
                width = 0;
                lines++;
            }
            else if (rune.Value != '\r') width += GetGlyph(rune).Advance;
        }
        var result = new Vector2(MathF.Max(maximum, width), lines * LineHeight) * scale;
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y))
            throw new ArgumentOutOfRangeException(nameof(scale), "Scaled text dimensions exceed the supported range.");
        return result;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Atlas.Dispose();
        _disposed = true;
    }
}
