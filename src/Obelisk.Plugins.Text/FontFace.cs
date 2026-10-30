using System.Text;
using SkiaSharp;

namespace Obelisk.Plugins.Text;

/// <summary>Owns a CPU font face. Asset-loaded faces are borrowed from the host; keep them alive until dependent DynamicFonts are disposed.</summary>
public sealed class FontFace : IDisposable
{
    private readonly SKTypeface _face;
    private FontFace(SKTypeface face) => _face = face;
    /// <summary>Loads a local TTF/OTF font without allocating GPU resources; the caller owns it unless loaded through Assets.</summary>
    public static FontFace LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Font file was not found.", path);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Font file exceeds 64 MiB.");
        return new FontFace(SKTypeface.FromFile(path) ?? throw new InvalidDataException($"Cannot decode font '{path}'."));
    }
    /// <summary>Gets the font family name without exposing backend-native types.</summary>
    public string FamilyName { get { EnsureAlive(); return _face.FamilyName; } }
    /// <summary>Gets whether the owned face has been released.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Tests whether the face contains a Unicode scalar without rasterizing it.</summary>
    public bool HasGlyph(Rune rune) { EnsureAlive(); using var font = CreateFont(16); return font.GetGlyph(rune.Value) != 0; }
    internal SKFont CreateFont(float size) { EnsureAlive(); return new SKFont(_face, size) { Edging = SKFontEdging.Antialias }; }
    internal void EnsureAlive() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    /// <summary>Releases the native CPU face. Dispose dependent GPU font caches first.</summary>
    public void Dispose() { if (IsDisposed) return; _face.Dispose(); IsDisposed = true; }
}
