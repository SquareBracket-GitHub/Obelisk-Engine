using System.Buffers.Binary;
using Obelisk.Abstractions;

namespace Obelisk.Assets.Tests;

public sealed class WavLoaderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Pcm16_LoadsAndOwnsCompleteFrames(int channels)
    {
        var wav = Wave(channels);
        var sound = WavLoader.Decode(wav);
        Assert.Equal(8000, sound.SampleRate);
        Assert.Equal(channels, sound.Channels);
        Assert.Equal(TimeSpan.FromSeconds(0.001), sound.Duration);
        wav[44] = 99;
        Assert.Equal(0, sound.Pcm[0]);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(43)]
    public void TruncatedFilesAreRejected(int length) => Assert.Throws<InvalidDataException>(() => WavLoader.Decode(Wave(1).AsSpan(0, length)));

    [Theory]
    [InlineData(20, 3)] // IEEE float instead of PCM
    [InlineData(22, 3)] // Too many channels
    [InlineData(32, 9)] // Wrong block alignment
    [InlineData(34, 8)] // Wrong bit depth
    [InlineData(40, 255)] // Truncated data
    public void InvalidFormatsAreRejected(int offset, byte value)
    {
        var wav = Wave(1);
        wav[offset] = value;
        Assert.Throws<InvalidDataException>(() => WavLoader.Decode(wav));
    }
    [Fact]
    public void OversizedChunkLengthsReportInvalidData()
    {
        var wav = Wave(1);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => WavLoader.Decode(wav));
        wav = Wave(1);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => WavLoader.Decode(wav));
    }
    [Fact]
    public void LoaderErrorsIncludePathAndCause()
    {
        var path = Path.Combine(Path.GetTempPath(), $"obelisk-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var error = Assert.Throws<InvalidDataException>(() => WavLoader.LoadFile(path));
            Assert.Contains(path, error.Message);
            Assert.NotNull(error.InnerException);
        }
        finally { File.Delete(path); }
        Assert.Throws<FileNotFoundException>(() => WavLoader.LoadFile(path));
    }
    [Fact]
    public void HeadlessAudioValidatesArgumentsAndStopsSafely()
    {
        var sound = new Sound(8000, 1, new byte[16]);
        using var voice = new InitializeContext().Audio.Play(sound, true, 0.5f);
        Assert.False(voice.IsPlaying);
        voice.Stop(); voice.Stop();
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Volume = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => SilentAudioDevice.Instance.Play(sound, volume: 2));
    }
    private static byte[] Wave(int channels)
    {
        var pcm = new byte[16 * channels];
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8); writer.Write(36 + pcm.Length); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels);
        writer.Write(8000); writer.Write(16000 * channels); writer.Write((short)(channels * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
        return file.ToArray();
    }
}
