namespace Obelisk.Plugins.Text;

/// <summary>Bounds a DynamicFont's GPU atlas memory and text work. Pages never move live glyphs.</summary>
public sealed record TextCacheOptions
{
    /// <summary>Gets the square atlas page size (64–2048 pixels); defaults to 1024.</summary>
    public int PageSize { get; init; } = 1024;
    /// <summary>Gets the maximum resident page count (1–64); defaults to 8.</summary>
    public int MaximumPages { get; init; } = 8;
    /// <summary>Gets the maximum cached glyph entries (1–65536), including whitespace; defaults to 8192.</summary>
    public int MaximumGlyphs { get; init; } = 8192;
    /// <summary>Gets the per-call UTF-16 character limit; defaults to 16384, maximum 1 million.</summary>
    public int MaximumTextLength { get; init; } = 16384;
    /// <summary>Gets whether unsupported characters throw instead of using U+FFFD or '?'.</summary>
    public bool StrictMissingGlyphs { get; init; }
    internal void Validate()
    {
        if (PageSize is < 64 or > 2048 || MaximumPages is < 1 or > 64 || MaximumGlyphs is < 1 or > 65536 ||
            MaximumTextLength is < 1 or > 1024 * 1024 || (long)PageSize * PageSize * MaximumPages * 4 > 256L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(TextCacheOptions), "Atlas limits must be bounded to at most 256 MiB.");
    }
}
