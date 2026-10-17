namespace Obelisk.Graphics2D;

/// <summary>Top-to-bottom, tightly packed RGBA8 pixels with straight (not premultiplied) alpha.</summary>
public sealed class RgbaImage
{
    /// <summary>Limits each decoded dimension to 8192 pixels.</summary>
    public const int MaximumDimension = 8192;
    /// <summary>Limits total decoded pixels to 16 million (16 times 1024 squared).</summary>
    public const int MaximumPixels = 16 * 1024 * 1024;

    /// <summary>Copies exactly width times height times four straight-alpha RGBA8 bytes after validating size limits.</summary>
    public RgbaImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        ValidateSize(width, height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA data must contain exactly four bytes per pixel.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels.ToArray();
    }

    /// <summary>Gets the positive width in pixels.</summary>
    public int Width { get; }
    /// <summary>Gets the positive height in pixels.</summary>
    public int Height { get; }
    /// <summary>Gets borrowed top-to-bottom, tightly packed CPU pixel data.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Throws ArgumentOutOfRangeException for nonpositive or oversized image dimensions.</summary>
    public static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension ||
            (long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions exceed the supported limits.");
    }
}

/// <summary>Creates textures on the game's owning thread. No file or network I/O is performed.</summary>
public interface ITextureDevice
{
    /// <summary>Uploads CPU pixels on the initialized device's owning thread; the caller owns the result.</summary>
    Texture CreateTexture(RgbaImage image);
}

/// <summary>An owned GPU texture. Dispose on the game's owning thread; shutdown releases survivors.</summary>
public sealed class Texture : IDisposable
{
    private readonly Action _release;

    /// <summary>Wraps a valid engine identity and release callback; primarily intended for device implementations.</summary>
    public Texture(TextureHandle handle, int width, int height, Action release)
    {
        if (!handle.IsValid) throw new ArgumentException("Invalid texture handle.", nameof(handle));
        RgbaImage.ValidateSize(width, height);
        ArgumentNullException.ThrowIfNull(release);
        Handle = handle;
        Width = width;
        Height = height;
        _release = release;
    }

    /// <summary>Gets the engine identity; it is not an SDL or GPU native handle.</summary>
    public TextureHandle Handle { get; }
    /// <summary>Gets the positive width in pixels.</summary>
    public int Width { get; }
    /// <summary>Gets the positive height in pixels.</summary>
    public int Height { get; }
    /// <summary>Gets whether disposal has completed successfully.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Releases on the owning thread; repeats after successful disposal are no-ops. Keep the texture alive while queued draws use it.</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        _release();
        IsDisposed = true;
    }
}

/// <summary>Selects the blending state captured with each drawing command.</summary>
public enum BlendMode
{
    /// <summary>Uses straight-alpha source-over blending.</summary>
    Alpha,
    /// <summary>Writes source color without alpha blending.</summary>
    Opaque,
}
