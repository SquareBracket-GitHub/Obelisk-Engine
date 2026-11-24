namespace Obelisk.Plugins.Text;

/// <summary>Horizontal alignment of each logical line, using its advance width including whitespace.</summary>
public enum TextAlignment
{
    /// <summary>Starts each line at the supplied X coordinate.</summary>
    Left,
    /// <summary>Centers each line within the layout width, or on X when the width is zero.</summary>
    Center,
    /// <summary>Ends each line at X plus the layout width, or at X when the width is zero.</summary>
    Right,
}

internal static class TextAlignmentLayout
{
    internal static void Validate(TextAlignment alignment, float layoutWidth)
    {
        if (alignment is not (TextAlignment.Left or TextAlignment.Center or TextAlignment.Right))
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!float.IsFinite(layoutWidth) || layoutWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(layoutWidth));
    }

    internal static float Offset(TextAlignment alignment, float layoutWidth, float lineWidth) => alignment switch
    {
        TextAlignment.Center => (layoutWidth - lineWidth) / 2,
        TextAlignment.Right => layoutWidth - lineWidth,
        _ => 0,
    };
}
