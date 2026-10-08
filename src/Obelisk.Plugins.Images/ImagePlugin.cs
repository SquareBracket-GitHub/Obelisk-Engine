using Obelisk.Graphics2D;
using StbImageSharp;

namespace Obelisk.Plugins.Images;

/// <summary>Bundled, optional image importer. Decoding/downloading never accesses a GPU.</summary>
public sealed class ImagePlugin : IDisposable
{
    public const int MaximumEncodedBytes = 16 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ImagePlugin(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public RgbaImage Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length == 0 || encoded.Length > MaximumEncodedBytes)
            throw new InvalidDataException("Image must contain between 1 byte and 16 MiB of encoded data.");
        try
        {
            using var stream = new MemoryStream(encoded.ToArray(), writable: false);
            var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Unsupported image header.");
            RgbaImage.ValidateSize(info.Width, info.Height);
            stream.Position = 0;
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return new RgbaImage(image.Width, image.Height, image.Data);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Image decoding failed: {error.Message}", error);
        }
    }

    public RgbaImage LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumEncodedBytes) throw new InvalidDataException("Image exceeds 16 MiB.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return Decode(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not load image '{path}': {error.Message}", error);
        }
    }

    public Texture LoadTexture(ITextureDevice device, string path) => device.CreateTexture(LoadFile(path));

    /// <summary>Returns CPU pixels. Upload them via ITextureDevice on the game thread after completion.</summary>
    public async Task<RgbaImage> LoadUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Image URL must use HTTP or HTTPS.", nameof(uri));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumEncodedBytes)
            throw new InvalidDataException("Image download exceeds 16 MiB.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > MaximumEncodedBytes) throw new InvalidDataException("Image download exceeds 16 MiB.");
            output.Write(buffer, 0, read);
        }
        timeout.Token.ThrowIfCancellationRequested();
        return Decode(output.ToArray());
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
