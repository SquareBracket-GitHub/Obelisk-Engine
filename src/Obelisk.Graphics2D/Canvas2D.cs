using System.Numerics;

namespace Obelisk.Graphics2D;

/// <summary>
/// Collects backend-independent 2D drawing commands in submission order for one frame.
/// </summary>
public sealed class Canvas2D
{
    private readonly List<RenderCommand2D> _commands = [];

    /// <summary>Creates an empty per-frame Canvas command collector.</summary>
    public Canvas2D()
    {
        Commands = _commands.AsReadOnly();
    }

    /// <summary>Gets a read-only view in submission order; resources used by these commands must remain alive through presentation.</summary>
    public IReadOnlyList<RenderCommand2D> Commands { get; }

    /// <summary>Captured by each draw call; Alpha uses straight-alpha source-over blending.</summary>
    public BlendMode Blend { get; set; } = BlendMode.Alpha;
    /// <summary>Gets or sets the sampling mode captured by subsequent draws.</summary>
    public TextureFilter Filter { get; set; } = TextureFilter.Nearest;
    /// <summary>Gets or sets a borrowed experimental material for subsequent draws.</summary>
    public Material? Material { get; set; }
    /// <summary>Gets or sets four finite constants for subsequent custom material draws.</summary>
    public Vector4 Parameters { get; set; }

    /// <summary>Captures an independent Canvas pass in submission order. Targets must not sample themselves or contain nested passes.</summary>
    public void RenderTo(RenderTarget target, Action<Canvas2D> draw, ColorRgba? clearColor = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(draw);
        ObjectDisposedException.ThrowIf(target.IsDisposed, target);
        var clear = clearColor ?? new ColorRgba(0, 0, 0, 0);
        EnsureFinite(clear, nameof(clearColor));
        var child = new Canvas2D();
        draw(child);
        if (child.Commands.Any(c => c is RenderPassCommand)) throw new NotSupportedException("Nested render passes are not supported.");
        _commands.Add(new RenderPassCommand(target, Array.AsReadOnly(child.Commands.ToArray()), clear));
    }

    private void Add(RenderCommand2D command)
    {
        if (!Enum.IsDefined(Blend)) throw new ArgumentOutOfRangeException(nameof(Blend));
        if (!Enum.IsDefined(Filter)) throw new ArgumentOutOfRangeException(nameof(Filter));
        if (!float.IsFinite(Parameters.X) || !float.IsFinite(Parameters.Y) || !float.IsFinite(Parameters.Z) || !float.IsFinite(Parameters.W))
            throw new ArgumentOutOfRangeException(nameof(Parameters));
        if (Material is { } material) ObjectDisposedException.ThrowIf(material.IsDisposed, material);
        _commands.Add(command with { Blend = Blend, Filter = Filter, Material = Material, Parameters = Parameters });
    }

    /// <summary>Queues a finite positive rectangle in output pixels; invalid geometry throws an argument exception.</summary>
    public void FillRectangle(RectangleF bounds, ColorRgba color)
    {
        EnsureVisible(bounds, nameof(bounds));
        EnsureFinite(color, nameof(color));
        Add(new RectangleCommand(bounds, color));
    }

    /// <summary>Queues a finite line segment with positive pixel thickness.</summary>
    public void DrawLine(Vector2 start, Vector2 end, float thickness, ColorRgba color)
    {
        EnsureFinite(start, nameof(start));
        EnsureFinite(end, nameof(end));
        EnsureFinite(color, nameof(color));

        if (!float.IsFinite(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thickness),
                "Line thickness must be finite and greater than zero.");
        }

        Add(new LineCommand(start, end, thickness, color));
    }

    /// <summary>Queues a finite circle with positive pixel radius.</summary>
    public void FillCircle(Vector2 center, float radius, ColorRgba color)
    {
        EnsureFinite(center, nameof(center));
        EnsureFinite(color, nameof(color));

        if (!float.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Circle radius must be finite and greater than zero.");
        }

        Add(new CircleCommand(center, radius, color));
    }

    /// <summary>Queues a sprite in output pixels with optional pixel source rectangle, white tint, rotation in radians and local pixel origin.</summary>
    public void DrawSprite(
        TextureHandle texture,
        RectangleF destination,
        RectangleF? source = null,
        ColorRgba? tint = null,
        float rotation = 0,
        Vector2 origin = default)
    {
        if (!texture.IsValid)
        {
            throw new ArgumentException("Texture handle zero is reserved as invalid.", nameof(texture));
        }

        EnsureVisible(destination, nameof(destination));

        if (source is { } sourceRectangle)
        {
            EnsureVisible(sourceRectangle, nameof(source));
        }

        if (!float.IsFinite(rotation))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), "Rotation must be finite.");
        }

        EnsureFinite(origin, nameof(origin));
        var resolvedTint = tint ?? ColorRgba.White;
        EnsureFinite(resolvedTint, nameof(tint));
        Add(new SpriteCommand(
            texture,
            destination,
            source,
            resolvedTint,
            rotation,
            origin));
    }

    /// <summary>Queues a sprite in output pixels with optional pixel source rectangle, white tint, rotation in radians and local pixel origin.</summary>
    public void DrawSprite(Texture texture, RectangleF destination, RectangleF? source = null,
        ColorRgba? tint = null, float rotation = 0, Vector2 origin = default)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        DrawSprite(texture.Handle, destination, source, tint, rotation, origin);
    }

    /// <summary>Draws text at a positive uniform scale, without creating another font atlas.</summary>
    public void DrawText(Font font, string text, Vector2 position, ColorRgba? color = null, float scale = 1)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(font.IsDisposed, font);
        EnsureFinite(position, nameof(position));
        var resolvedColor = color ?? ColorRgba.White;
        EnsureFinite(resolvedColor, nameof(color));
        var extent = font.Measure(text, scale); // Validate scale and glyphs before recording commands.
        EnsureFinite(position + extent, nameof(position));
        if (text.Length != 0) Add(new TextCommand(font, text, position, resolvedColor, scale));
    }

    private static void EnsureVisible(RectangleF rectangle, string parameterName)
    {
        if (!rectangle.IsVisible)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Rectangle coordinates and size must be finite, and its size must be greater than zero.");
        }
    }

    private static void EnsureFinite(Vector2 point, string parameterName)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Coordinates must be finite.");
        }
    }

    private static void EnsureFinite(ColorRgba color, string parameterName)
    {
        if (!float.IsFinite(color.R) ||
            !float.IsFinite(color.G) ||
            !float.IsFinite(color.B) ||
            !float.IsFinite(color.A))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Color components must be finite.");
        }
    }
}
