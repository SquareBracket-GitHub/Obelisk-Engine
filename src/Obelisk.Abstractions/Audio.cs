namespace Obelisk.Abstractions;

/// <summary>Immutable, interleaved signed 16-bit little-endian PCM sound data.</summary>
public sealed class Sound
{
    private readonly byte[] _pcm;

    /// <summary>Copies complete PCM16 frames at 8–192 kHz, mono or stereo, up to 16 MiB; invalid data throws an argument exception.</summary>
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

    /// <summary>Gets the number of PCM frames per second.</summary>
    public int SampleRate { get; }
    /// <summary>Gets the channel count, one or two.</summary>
    public int Channels { get; }
    /// <summary>Gets the duration of the complete PCM buffer.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)_pcm.Length / (SampleRate * Channels * 2));
    /// <summary>Gets borrowed interleaved PCM16 little-endian bytes.</summary>
    public ReadOnlySpan<byte> Pcm => _pcm;
}

/// <summary>Playback belongs to the device's owning thread. Dispose or Stop to release it.</summary>
public interface ISoundPlayback : IDisposable
{
    /// <summary>Gets whether playback is still active; silent playback always returns false.</summary>
    bool IsPlaying { get; }
    /// <summary>Gets or sets a finite gain in the inclusive range zero to one.</summary>
    float Volume { get; set; }
    /// <summary>Stops playback and releases its native stream; repeated calls are safe.</summary>
    void Stop();
}

/// <summary>Starts owning-thread playback, with silent fallback when a device is unavailable.</summary>
public interface IAudioDevice
{
    /// <summary>Gets whether a real playback device was initialized.</summary>
    bool IsAvailable { get; }
    /// <summary>Gets a diagnostic for an unavailable device, or null when available.</summary>
    string? UnavailableReason { get; }
    /// <summary>Starts a copied sound stream with optional looping and gain; the caller owns the returned playback.</summary>
    ISoundPlayback Play(Sound sound, bool loop = false, float volume = 1);
}

/// <summary>A safe fallback for headless platforms and machines without playback devices.</summary>
public sealed class SilentAudioDevice : IAudioDevice
{
    /// <summary>Gets the shared silent fallback; it does not acquire native resources.</summary>
    public static SilentAudioDevice Instance { get; } = new();
    /// <summary>Gets false because this device produces no audio.</summary>
    public bool IsAvailable => false;
    /// <summary>Gets the fallback diagnostic.</summary>
    public string? UnavailableReason => "Audio is not available on this platform.";

    /// <summary>Validates the sound and gain and returns caller-owned, already-stopped silent playback.</summary>
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
