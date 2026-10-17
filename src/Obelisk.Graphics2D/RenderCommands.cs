using System.Numerics;

namespace Obelisk.Graphics2D;

/// <summary>An immutable 2D drawing instruction collected for one frame.</summary>
public abstract record RenderCommand2D
{
    /// <summary>Gets the blend mode captured when the command was submitted.</summary>
    public BlendMode Blend { get; init; } = BlendMode.Alpha;
}

/// <summary>Fills a rectangle with a solid color.</summary>
/// <param name="Bounds">Bounds in output pixels.</param>
/// <param name="Color">Fill color.</param>
public sealed record RectangleCommand(RectangleF Bounds, ColorRgba Color) : RenderCommand2D;

/// <summary>Draws a line segment with a thickness measured in output pixels.</summary>
/// <param name="Start">First output-space endpoint.</param>
/// <param name="End">Last output-space endpoint.</param>
/// <param name="Thickness">Positive thickness in pixels.</param>
/// <param name="Color">Line color.</param>
public sealed record LineCommand(
    Vector2 Start,
    Vector2 End,
    float Thickness,
    ColorRgba Color) : RenderCommand2D;

/// <summary>Fills a circle whose radius is measured in output pixels.</summary>
/// <param name="Center">Center in output pixels.</param>
/// <param name="Radius">Positive radius in pixels.</param>
/// <param name="Color">Fill color.</param>
public sealed record CircleCommand(
    Vector2 Center,
    float Radius,
    ColorRgba Color) : RenderCommand2D;

/// <summary>Draws a texture region into a destination rectangle.</summary>
/// <param name="Texture">Engine texture identity.</param>
/// <param name="Destination">Destination in output pixels.</param>
/// <param name="Source">Optional source region in texture pixels.</param>
/// <param name="Tint">Multiplicative RGBA tint.</param>
/// <param name="Rotation">Rotation in radians.</param>
/// <param name="Origin">Local destination-pixel rotation origin.</param>
public sealed record SpriteCommand(
    TextureHandle Texture,
    RectangleF Destination,
    RectangleF? Source,
    ColorRgba Tint,
    float Rotation,
    Vector2 Origin) : RenderCommand2D;

/// <summary>Draws pre-rasterized glyphs from an owned font atlas.</summary>
/// <param name="Font">Borrowed Font, alive until presentation completes.</param>
/// <param name="Text">Text using loaded glyphs or line breaks.</param>
/// <param name="Position">Top-left output position.</param>
/// <param name="Color">Text color.</param>
/// <param name="Scale">Positive finite uniform scale.</param>
public sealed record TextCommand(Font Font, string Text, Vector2 Position, ColorRgba Color, float Scale = 1) : RenderCommand2D;
