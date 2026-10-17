using System.Buffers.Binary;
using Obelisk.Abstractions;

namespace Obelisk.Assets;

/// <summary>Loads RIFF/WAVE PCM16 mono/stereo sounds without a native audio device.</summary>
public static class WavLoader
{
    /// <summary>Reads a WAV file up to 17 MiB; errors include the source path. Returned immutable PCM data has no native lifetime.</summary>
    public static Sound LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException($"WAV file '{path}' was not found.", path);
        using var file = File.OpenRead(path);
        if (file.Length > 17 * 1024 * 1024) throw new InvalidDataException($"WAV file '{path}' exceeds 17 MiB.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        try { return Decode(bytes); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        { throw new InvalidDataException($"WAV file '{path}' is invalid: {error.Message}", error); }
    }

    /// <summary>Decodes bounded RIFF/WAVE PCM16 mono/stereo bytes at 8–192 kHz; malformed data throws InvalidDataException.</summary>
    public static Sound Decode(ReadOnlySpan<byte> wav)
    {
        try { return DecodeCore(wav); }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { throw new InvalidDataException($"Invalid WAV data: {error.Message}", error); }
    }

    private static Sound DecodeCore(ReadOnlySpan<byte> wav)
    {
        if (wav.Length > 17 * 1024 * 1024) throw new InvalidDataException("WAV data exceeds 17 MiB.");
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Expected a RIFF/WAVE header.");
        var end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wav[4..8]) + 8);
        if (end != wav.Length) throw new InvalidDataException("RIFF size does not match the file length.");
        int rate = 0, channels = 0;
        ReadOnlySpan<byte> pcm = default;
        bool formatFound = false, dataFound = false;
        for (var offset = 12; offset < end;)
        {
            if (end - offset < 8) throw new InvalidDataException("Truncated WAV chunk header.");
            var name = wav.Slice(offset, 4);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(offset + 4, 4)));
            offset += 8;
            if (size > end - offset) throw new InvalidDataException("Truncated WAV chunk data.");
            var chunk = wav.Slice(offset, size);
            if (name.SequenceEqual("fmt "u8))
            {
                if (formatFound || size < 16) throw new InvalidDataException("Missing or duplicate WAV format.");
                formatFound = true;
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                rate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]));
                if (BinaryPrimitives.ReadUInt16LittleEndian(chunk) != 1 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]) != 16 || channels is < 1 or > 2 ||
                    rate is < 8000 or > 192000 || BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]) != channels * 2 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk[8..]) != rate * channels * 2)
                    throw new InvalidDataException("Only PCM16 WAV, mono/stereo, 8–192 kHz is supported.");
            }
            else if (name.SequenceEqual("data"u8))
            {
                if (dataFound) throw new InvalidDataException("Duplicate WAV data chunk.");
                dataFound = true;
                pcm = chunk;
            }
            offset = checked(offset + size + (size & 1));
            if (offset > end) throw new InvalidDataException("Missing WAV chunk padding.");
        }
        if (!formatFound || !dataFound) throw new InvalidDataException("WAV requires format and data chunks.");
        return new Sound(rate, channels, pcm);
    }
}
