namespace Obelisk.Graphics2D;

/// <summary>Top-to-bottom, tightly packed RGBA8 pixels with straight (not premultiplied) alpha.</summary>
public sealed class RgbaImage
{
    public const int MaximumDimension = 8192;
    public const int MaximumPixels = 16 * 1024 * 1024;

    public RgbaImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        ValidateSize(width, height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA data must contain exactly four bytes per pixel.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Pixels { get; }

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
    Texture CreateTexture(RgbaImage image);
}

/// <summary>An owned GPU texture. Dispose on the game's owning thread; shutdown releases survivors.</summary>
public sealed class Texture : IDisposable
{
    private readonly Action _release;

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

    public TextureHandle Handle { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        _release();
        IsDisposed = true;
    }
}

public enum BlendMode
{
    Alpha,
    Opaque,
}
