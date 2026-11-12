using System.Buffers.Binary;
using System.IO.Compression;
using Obelisk.Platform.Sdl3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed class DebugEncodingTests
{
    [Fact]
    public void PngRetainsRgbaRowsAndDimensions()
    {
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 128];
        var png = PngWriter.Encode(1, 2, pixels);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        using var compressed = new MemoryStream(png, 41, length);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream(); zlib.CopyTo(output);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255, 0, 0, 255, 0, 128 }, output.ToArray());
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, pixels));
    }
}
