namespace Obelisk.Graphics2D;

/// <summary>A rectangle measured in Canvas output pixels.</summary>
/// <param name="X">Left output coordinate.</param>
/// <param name="Y">Top output coordinate.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    /// <summary>Gets whether all components are finite and both dimensions are positive.</summary>
    public bool IsVisible =>
        float.IsFinite(X) &&
        float.IsFinite(Y) &&
        float.IsFinite(Width) &&
        float.IsFinite(Height) &&
        Width > 0 &&
        Height > 0;
}

/// <summary>A linear RGBA color whose components normally range from zero to one.</summary>
/// <param name="R">Red component.</param>
/// <param name="G">Green component.</param>
/// <param name="B">Blue component.</param>
/// <param name="A">Straight alpha; defaults to opaque.</param>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1)
{
    /// <summary>Gets opaque white.</summary>
    public static ColorRgba White { get; } = new(1, 1, 1);

    /// <summary>Gets the opaque orange used by the rectangle sample.</summary>
    public static ColorRgba Orange { get; } = new(0.95f, 0.38f, 0.12f);
}

/// <summary>An engine-owned texture identity. It is not a native graphics handle.</summary>
/// <param name="Value">Engine identity; zero is invalid.</param>
public readonly record struct TextureHandle(ulong Value)
{
    /// <summary>Gets whether the engine identity is nonzero; it does not prove the resource is still alive.</summary>
    public bool IsValid => Value != 0;
}
