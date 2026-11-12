using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Obelisk.Platform.Sdl3;

internal static class PngWriter
{
    internal static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4)) throw new ArgumentException("Invalid screenshot dimensions.");
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var row = 0; row < height; row++) { zlib.WriteByte(0); zlib.Write(rgba, row * width * 4, width * 4); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }
    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        var tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (var b in tag.Concat(data))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
