using System.Numerics;
using System.Text;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;

namespace Obelisk.Plugins.Text;

/// <summary>Owns a GPU MSDF atlas/material and borrows validated CPU metadata. No native generator is required at runtime.</summary>
public sealed class MsdfFont : IDisposable
{
    private const string Shader = """
        float4 Shade(float4 color, float2 uv) {
            float3 value=SampleImage(uv).rgb;
            float median=max(min(value.r,value.g),min(max(value.r,value.g),value.b));
            float2 texels=1.0/max(fwidth(uv),float2(0.0000001,0.0000001));
            float range=max(0.5*dot(Parameters.xy,texels),1.0);
            float coverage=saturate(range*(median-0.5)+0.5);
            return float4(color.rgb,color.a*coverage);
        }
        """;
    private readonly MsdfAtlas _atlas;
    private readonly Texture _texture;
    private readonly Material _material;
    private readonly bool _strict;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    /// <summary>Uploads an atlas and compiles a derivative-based MSDF material on the device's owning thread.</summary>
    public MsdfFont(IRenderDevice device, MsdfAtlas atlas, bool strictMissingGlyphs = false)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(atlas);
        _atlas = atlas; _strict = strictMissingGlyphs; _texture = device.CreateTexture(atlas.Image);
        try { _material = device.CreateMaterial(Shader); } catch { _texture.Dispose(); throw; }
    }
    /// <summary>Gets whether this font's GPU resources have been released.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Measures output pixels at a requested em size, including declared kerning and line breaks.</summary>
    public Vector2 Measure(string text, float size = 32, float scale = 1, float dpiScale = 1)
    { EnsureAlive(); var pixels = Validate(text, size, scale, dpiScale); return Layout(text, pixels, Vector2.Zero).Extent; }
    /// <summary>Draws MSDF glyphs in output coordinates, preserving Canvas state. Unsupported characters use U+FFFD/'?' unless strict mode is selected.</summary>
    public void Draw(RenderContext context, string text, Vector2 position, float size = 32,
        ColorRgba? color = null, float scale = 1, float dpiScale = 1)
    {
        ArgumentNullException.ThrowIfNull(context);
        Draw(context.Canvas, text, position, size, color, scale, dpiScale);
    }

    /// <summary>Draws into an ordinary or RenderTo child Canvas with the same MSDF sampling contract.</summary>
    public void Draw(Canvas2D canvas, string text, Vector2 position, float size = 32,
        ColorRgba? color = null, float scale = 1, float dpiScale = 1)
    {
        EnsureAlive(); ArgumentNullException.ThrowIfNull(canvas);
        var pixels = Validate(text, size, scale, dpiScale);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        var tint = color ?? ColorRgba.White;
        if (!float.IsFinite(tint.R) || !float.IsFinite(tint.G) || !float.IsFinite(tint.B) || !float.IsFinite(tint.A)) throw new ArgumentOutOfRangeException(nameof(color));
        var layout = Layout(text, pixels, position);
        var blend = canvas.Blend; var filter = canvas.Filter; var material = canvas.Material; var parameters = canvas.Parameters;
        try
        {
            canvas.Blend = BlendMode.Alpha; canvas.Filter = TextureFilter.Linear; canvas.Material = _material;
            canvas.Parameters = new(_atlas.DistanceRange / _texture.Width, _atlas.DistanceRange / _texture.Height, 0, 0);
            foreach (var draw in layout.Draws) canvas.DrawSprite(_texture, draw.Destination, draw.Source, tint);
        }
        finally { canvas.Blend = blend; canvas.Filter = filter; canvas.Material = material; canvas.Parameters = parameters; }
    }
    private (Vector2 Extent, List<(RectangleF Source, RectangleF Destination)> Draws) Layout(string text, float pixels, Vector2 position)
    {
        var draws = new List<(RectangleF, RectangleF)>();
        var x = 0f; var y = 0f; var maximum = 0f; var lines = 1; int? previous = null;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n') { maximum = Math.Max(maximum, x); x = 0; y += _atlas.LineHeight; lines++; previous = null; continue; }
            var scalar = rune.Value == '\t' ? ' ' : rune.Value;
            if (!_atlas.Glyphs.ContainsKey(scalar))
            {
                if (_strict) throw new InvalidDataException($"MSDF atlas lacks U+{scalar:X4}.");
                scalar = _atlas.Glyphs.ContainsKey(0xfffd) ? 0xfffd : '?';
                if (!_atlas.Glyphs.ContainsKey(scalar)) throw new InvalidDataException("MSDF atlas lacks a replacement glyph.");
            }
            var glyph = _atlas.Glyphs[scalar];
            if (previous is { } first && _atlas.Kerning.TryGetValue((first, scalar), out var adjustment)) x += adjustment;
            if (glyph.Source is { } source && glyph.Plane is { } plane)
            {
                var destination = new RectangleF(position.X + (x + plane.X) * pixels,
                    position.Y + (y + _atlas.Ascender + plane.Y) * pixels, plane.Width * pixels, plane.Height * pixels);
                if (!destination.IsVisible) throw new ArgumentOutOfRangeException(nameof(position));
                draws.Add((source, destination));
            }
            x += glyph.Advance * (rune.Value == '\t' ? 4 : 1); previous = scalar;
        }
        var extent = new Vector2(Math.Max(maximum, x) * pixels, lines * _atlas.LineHeight * pixels);
        if (!float.IsFinite(extent.X) || !float.IsFinite(extent.Y)) throw new ArgumentOutOfRangeException(nameof(text));
        return (extent, draws);
    }
    private static float Validate(string text, float size, float scale, float dpiScale)
    {
        ArgumentNullException.ThrowIfNull(text);
        var pixels = size * scale * dpiScale;
        if (text.Length > 16384 || !float.IsFinite(size) || !float.IsFinite(scale) || !float.IsFinite(dpiScale) ||
            size <= 0 || scale <= 0 || dpiScale <= 0 || !float.IsFinite(pixels) || pixels < 1 || pixels > 4096)
            throw new ArgumentOutOfRangeException(nameof(size));
        return pixels;
    }
    private void EnsureAlive()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("MSDF fonts must be used on their owning thread.");
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
    /// <summary>Releases atlas and material on the owning thread after the final queued draw has been presented.</summary>
    public void Dispose() { if (IsDisposed) return; EnsureAlive(); _material.Dispose(); _texture.Dispose(); IsDisposed = true; }
}
