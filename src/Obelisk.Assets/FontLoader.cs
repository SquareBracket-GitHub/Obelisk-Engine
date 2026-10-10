using System.Runtime.InteropServices;
using System.Text;
using Obelisk.Graphics2D;
using SkiaSharp;

namespace Obelisk.Assets;

/// <summary>Rasterizes a bounded character set into one immutable RGBA glyph atlas.</summary>
public sealed class FontLoader
{
    public const string BasicLatin = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
    private readonly ITextureDevice _device;
    private readonly float _size;
    private readonly string _characters;

    public FontLoader(ITextureDevice device, float size = 32, string additionalCharacters = "")
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(additionalCharacters);
        if (!float.IsFinite(size) || size < 8 || size > 96) throw new ArgumentOutOfRangeException(nameof(size));
        _device = device;
        _size = size;
        _characters = BasicLatin + additionalCharacters;
    }

    public Font LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException($"Font file '{path}' was not found. Supply an installed .ttf or .otf font file.", path);
        using var face = SKTypeface.FromFile(path) ?? throw new InvalidDataException($"Font file '{path}' could not be decoded.");
        using var font = new SKFont(face, _size);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        var characters = _characters.EnumerateRunes().Distinct().ToArray();
        if (characters.Length == 0 || characters.Length > 1024)
            throw new ArgumentOutOfRangeException(nameof(path), "Font atlas must contain between 1 and 1024 characters.");
        var cellWidth = (int)MathF.Ceiling(_size * 2);
        var cellHeight = (int)MathF.Ceiling(_size * 2);
        var columns = (int)Math.Ceiling(Math.Sqrt(characters.Length));
        var rows = (characters.Length + columns - 1) / columns;
        var width = checked(columns * cellWidth);
        var height = checked(rows * cellHeight);
        RgbaImage.ValidateSize(width, height);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var glyphs = new Dictionary<Rune, FontGlyph>();
        for (var i = 0; i < characters.Length; i++)
        {
            var rune = characters[i];
            var text = rune.ToString();
            if (font.GetGlyphs(text) is not [var glyph] || glyph == 0)
                throw new InvalidDataException($"Font file '{path}' is missing glyph U+{rune.Value:X4}.");
            var advance = font.MeasureText(text, out var bounds);
            var x = i % columns * cellWidth;
            var y = i / columns * cellHeight;
            canvas.DrawText(text, x + 2 - MathF.Min(bounds.Left, 0), y + _size * 1.25f,
                SKTextAlign.Left, font, paint);
            glyphs.Add(rune, new FontGlyph(new RectangleF(x, y, cellWidth, cellHeight), advance));
        }
        var pixels = new byte[checked(width * height * 4)];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        var atlas = _device.CreateTexture(new RgbaImage(width, height, pixels));
        return new Font(atlas, cellHeight, glyphs);
    }
}
