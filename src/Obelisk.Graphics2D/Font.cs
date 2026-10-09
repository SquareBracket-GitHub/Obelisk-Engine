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
        ArgumentNullException.ThrowIfNull(glyphs);
        if (!float.IsFinite(lineHeight) || lineHeight <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        Atlas = atlas;
        LineHeight = lineHeight;
        _glyphs = new Dictionary<Rune, FontGlyph>(glyphs);
    }

    public Texture Atlas { get; }
    public float LineHeight { get; }
    public bool IsDisposed { get; private set; }

    public FontGlyph GetGlyph(Rune rune)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!_glyphs.TryGetValue(rune, out var glyph))
            throw new ArgumentException($"Font atlas does not contain U+{rune.Value:X4}. Include it in the character set when loading the font.");
        return glyph;
    }

    public Vector2 Measure(string text)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(text);
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
        return new Vector2(MathF.Max(maximum, width), lines * LineHeight);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Atlas.Dispose();
        IsDisposed = true;
    }
}
