namespace Obelisk.Graphics2D;

/// <summary>A rectangle measured in Canvas output pixels.</summary>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    public bool IsVisible =>
        float.IsFinite(X) &&
        float.IsFinite(Y) &&
        float.IsFinite(Width) &&
        float.IsFinite(Height) &&
        Width > 0 &&
        Height > 0;
}

/// <summary>A linear RGBA color whose components normally range from zero to one.</summary>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1)
{
    public static ColorRgba White { get; } = new(1, 1, 1);

    public static ColorRgba Orange { get; } = new(0.95f, 0.38f, 0.12f);
}

/// <summary>An engine-owned texture identity. It is not a native graphics handle.</summary>
public readonly record struct TextureHandle(ulong Value)
{
    public bool IsValid => Value != 0;
}
