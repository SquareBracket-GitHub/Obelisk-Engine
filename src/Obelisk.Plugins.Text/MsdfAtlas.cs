using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Obelisk.Graphics2D;
using SkiaSharp;

namespace Obelisk.Plugins.Text;

/// <summary>Validated CPU atlas data in the single-font Unicode msdf-atlas-gen JSON/PNG format. RGB distance values remain linear data.</summary>
public sealed class MsdfAtlas
{
    internal sealed record Glyph(RectangleF? Source, RectangleF? Plane, float Advance);
    internal readonly Dictionary<int, Glyph> Glyphs = [];
    internal readonly Dictionary<(int First, int Second), float> Kerning = [];
    internal readonly float Ascender, LineHeight, DistanceRange;
    /// <summary>Gets immutable RGBA8 atlas bytes, whose RGB channels encode distances rather than colors.</summary>
    public RgbaImage Image { get; }
    /// <summary>Gets the number of declared Unicode glyphs, including whitespace.</summary>
    public int GlyphCount => Glyphs.Count;

    /// <summary>Validates one msdf/mtsdf atlas, symmetric distance range, emSize=1 and top/bottom Y origins before any GPU allocation.</summary>
    public MsdfAtlas(RgbaImage image, string metadata)
    {
        ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException("MSDF metadata exceeds 16 MiB characters.");
        Image = image;
        try
        {
            using var document = JsonDocument.Parse(metadata, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement; Unique(root);
            var atlas = root.GetProperty("atlas");
            var type = atlas.GetProperty("type").GetString();
            if (type is not ("msdf" or "mtsdf")) throw new InvalidDataException("Expected msdf or mtsdf RGB distance data.");
            if (atlas.GetProperty("width").GetInt32() != image.Width || atlas.GetProperty("height").GetInt32() != image.Height)
                throw new InvalidDataException("MSDF image dimensions disagree with metadata.");
            DistanceRange = Number(atlas, "distanceRange");
            if (DistanceRange <= 0 || DistanceRange > 256 || (atlas.TryGetProperty("distanceRangeMiddle", out var middle) && middle.GetSingle() != 0))
                throw new InvalidDataException("Expected a bounded symmetric distance range.");
            var origin = atlas.GetProperty("yOrigin").GetString();
            if (origin is not ("top" or "bottom")) throw new InvalidDataException("Expected top or bottom yOrigin.");
            var metrics = root.GetProperty("metrics");
            if (Number(metrics, "emSize") != 1) throw new InvalidDataException("Expected emSize=1.");
            // msdf-atlas-gen negates vertical font metrics for top-origin exports.
            Ascender = Number(metrics, "ascender") * (origin == "top" ? -1 : 1); LineHeight = Number(metrics, "lineHeight");
            if (Ascender <= 0 || LineHeight <= 0) throw new InvalidDataException("Invalid font metrics.");
            foreach (var entry in root.GetProperty("glyphs").EnumerateArray())
            {
                if (Glyphs.Count >= 65536) throw new InvalidDataException("Too many MSDF glyphs.");
                var scalar = Scalar(entry, "unicode"); var advance = Number(entry, "advance");
                var hasPlane = entry.TryGetProperty("planeBounds", out var plane);
                var hasAtlas = entry.TryGetProperty("atlasBounds", out var bounds);
                if (hasPlane != hasAtlas) throw new InvalidDataException("Glyph needs both planeBounds and atlasBounds, or neither for whitespace.");
                RectangleF? source = null, normalizedPlane = null;
                if (hasAtlas)
                {
                    var left = Number(bounds, "left"); var right = Number(bounds, "right");
                    var top = Number(bounds, "top"); var bottom = Number(bounds, "bottom");
                    source = new(left, origin == "bottom" ? image.Height - top : top, right - left, origin == "bottom" ? top - bottom : bottom - top);
                    if (!source.Value.IsVisible || source.Value.X < 0 || source.Value.Y < 0 ||
                        source.Value.X + source.Value.Width > image.Width || source.Value.Y + source.Value.Height > image.Height)
                        throw new InvalidDataException("MSDF glyph lies outside its atlas.");
                    left = Number(plane, "left"); right = Number(plane, "right"); top = Number(plane, "top"); bottom = Number(plane, "bottom");
                    normalizedPlane = new(left, origin == "bottom" ? -top : top, right - left, origin == "bottom" ? top - bottom : bottom - top);
                    if (!normalizedPlane.Value.IsVisible) throw new InvalidDataException("Invalid glyph plane bounds.");
                }
                if (!Glyphs.TryAdd(scalar, new(source, normalizedPlane, advance))) throw new InvalidDataException("Duplicate MSDF Unicode glyph.");
            }
            if (Glyphs.Count == 0) throw new InvalidDataException("Expected at least one Unicode glyph.");
            if (root.TryGetProperty("kerning", out var pairs))
                foreach (var pair in pairs.EnumerateArray())
                {
                    var key = (Scalar(pair, "unicode1"), Scalar(pair, "unicode2"));
                    if (Kerning.Count >= 262144 || !Glyphs.ContainsKey(key.Item1) || !Glyphs.ContainsKey(key.Item2) ||
                        !Kerning.TryAdd(key, Number(pair, "advance"))) throw new InvalidDataException("Invalid or duplicate kerning pair.");
                }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid single-font Unicode MSDF atlas metadata.", error); }
    }
    private static float Number(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetSingle();
        if (!float.IsFinite(value) || Math.Abs(value) > 1000000) throw new InvalidDataException($"Invalid MSDF number '{name}'.");
        return value;
    }
    private static int Scalar(JsonElement element, string name)
    { var value = element.GetProperty(name).GetInt32(); if (!Rune.IsValid(value)) throw new InvalidDataException("Invalid Unicode scalar."); return value; }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate MSDF metadata field."); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
    }
    /// <summary>Loads metadata and a same-basename PNG sibling, rejecting symbolic file links and oversized images. The asset contains no GPU resources.</summary>
    public static MsdfAtlas LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var png = Path.ChangeExtension(path, ".png");
        foreach (var file in new[] { path, png })
        {
            if (!File.Exists(file)) throw new FileNotFoundException("MSDF asset file was not found.", file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("MSDF files must be regular files no larger than 16 MiB.");
        }
        using var codec = SKCodec.Create(png) ?? throw new InvalidDataException("Cannot decode MSDF PNG.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png) throw new InvalidDataException("MSDF atlas must be lossless PNG.");
        RgbaImage.ValidateSize(codec.Info.Width, codec.Info.Height);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Incomplete MSDF PNG.");
        var bytes = new byte[checked(info.Width * info.Height * 4)]; Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return new MsdfAtlas(new RgbaImage(info.Width, info.Height, bytes), File.ReadAllText(path));
    }
}
