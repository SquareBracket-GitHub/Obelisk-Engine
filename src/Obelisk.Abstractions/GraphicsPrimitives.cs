namespace Obelisk.Abstractions;

/// <summary>A rectangle in output pixels, measured from the top-left corner.</summary>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    public bool IsVisible => Width > 0 && Height > 0;
}

/// <summary>A linear RGBA color whose components normally range from zero to one.</summary>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1)
{
    public static ColorRgba Orange { get; } = new(0.95f, 0.38f, 0.12f);
}

/// <summary>The drawable output size in pixels.</summary>
public readonly record struct OutputSize(int Width, int Height);

/// <summary>A filled rectangle queued for the current frame.</summary>
public readonly record struct SolidRectangle(RectangleF Bounds, ColorRgba Color);
