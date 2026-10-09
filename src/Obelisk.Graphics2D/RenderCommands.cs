using System.Numerics;

namespace Obelisk.Graphics2D;

/// <summary>An immutable 2D drawing instruction collected for one frame.</summary>
public abstract record RenderCommand2D
{
    public BlendMode Blend { get; init; } = BlendMode.Alpha;
}

/// <summary>Fills a rectangle with a solid color.</summary>
public sealed record RectangleCommand(RectangleF Bounds, ColorRgba Color) : RenderCommand2D;

/// <summary>Draws a line segment with a thickness measured in output pixels.</summary>
public sealed record LineCommand(
    Vector2 Start,
    Vector2 End,
    float Thickness,
    ColorRgba Color) : RenderCommand2D;

/// <summary>Fills a circle whose radius is measured in output pixels.</summary>
public sealed record CircleCommand(
    Vector2 Center,
    float Radius,
    ColorRgba Color) : RenderCommand2D;

/// <summary>Draws a texture region into a destination rectangle.</summary>
public sealed record SpriteCommand(
    TextureHandle Texture,
    RectangleF Destination,
    RectangleF? Source,
    ColorRgba Tint,
    float Rotation,
    Vector2 Origin) : RenderCommand2D;

/// <summary>Draws pre-rasterized glyphs from an owned font atlas.</summary>
public sealed record TextCommand(Font Font, string Text, Vector2 Position, ColorRgba Color) : RenderCommand2D;
