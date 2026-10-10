namespace Obelisk.Abstractions;

/// <summary>Immutable, interleaved signed 16-bit little-endian PCM sound data.</summary>
public sealed class Sound
{
    private readonly byte[] _pcm;

    public Sound(int sampleRate, int channels, ReadOnlySpan<byte> pcm)
    {
        if (sampleRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels));
        if (pcm.Length == 0 || pcm.Length > 16 * 1024 * 1024 || pcm.Length % (channels * 2) != 0)
            throw new ArgumentException("Sound must contain complete PCM frames, up to 16 MiB.", nameof(pcm));
        SampleRate = sampleRate;
        Channels = channels;
        _pcm = pcm.ToArray();
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public TimeSpan Duration => TimeSpan.FromSeconds((double)_pcm.Length / (SampleRate * Channels * 2));
    public ReadOnlySpan<byte> Pcm => _pcm;
}

/// <summary>Playback belongs to the device's owning thread. Dispose or Stop to release it.</summary>
public interface ISoundPlayback : IDisposable
{
    bool IsPlaying { get; }
    float Volume { get; set; }
    void Stop();
}

public interface IAudioDevice
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    ISoundPlayback Play(Sound sound, bool loop = false, float volume = 1);
}

/// <summary>A safe fallback for headless platforms and machines without playback devices.</summary>
public sealed class SilentAudioDevice : IAudioDevice
{
    public static SilentAudioDevice Instance { get; } = new();
    public bool IsAvailable => false;
    public string? UnavailableReason => "Audio is not available on this platform.";

    public ISoundPlayback Play(Sound sound, bool loop = false, float volume = 1)
    {
        ArgumentNullException.ThrowIfNull(sound);
        return new SilentPlayback { Volume = volume };
    }

    internal static void ValidateVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1) throw new ArgumentOutOfRangeException(nameof(volume));
    }

    private sealed class SilentPlayback : ISoundPlayback
    {
        private float _volume;
        public bool IsPlaying => false;
        public float Volume { get => _volume; set { ValidateVolume(value); _volume = value; } }
        public void Stop() { }
        public void Dispose() { }
    }
}
