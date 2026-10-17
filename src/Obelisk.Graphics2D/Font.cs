using System.Numerics;
using System.Text;

namespace Obelisk.Graphics2D;

/// <summary>One glyph's location in a font atlas and its horizontal advance in pixels.</summary>
/// <param name="Source">Glyph bounds in atlas pixels.</param>
/// <param name="Advance">Unscaled horizontal advance in pixels.</param>
public readonly record struct FontGlyph(RectangleF Source, float Advance);

/// <summary>An immutable GPU glyph atlas. Dispose it on the texture device's owning thread.</summary>
public sealed class Font : IDisposable
{
    private readonly IReadOnlyDictionary<Rune, FontGlyph> _glyphs;

    /// <summary>Takes ownership of the atlas, copies glyph metadata and requires a finite positive line height.</summary>
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

    /// <summary>Gets the owned glyph atlas; dispose the Font rather than independently disposing this texture.</summary>
    public Texture Atlas { get; }
    /// <summary>Gets the unscaled line advance in pixels.</summary>
    public float LineHeight { get; }
    private bool _disposed;
    /// <summary>Gets whether this Font or its atlas has been disposed.</summary>
    public bool IsDisposed => _disposed || Atlas.IsDisposed;

    /// <summary>Gets atlas metadata for a loaded Unicode rune; missing runes throw ArgumentException.</summary>
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

    /// <summary>Disposes the owned atlas on the texture device's owning thread; repeated calls are safe.</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        Atlas.Dispose();
        _disposed = true;
    }
}
