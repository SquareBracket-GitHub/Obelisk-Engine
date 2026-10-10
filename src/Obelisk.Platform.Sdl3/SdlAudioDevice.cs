using Obelisk.Abstractions;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal interface ISdlAudioApi
{
    bool Initialize();
    nint Open(int rate, int channels);
    bool Resume(nint stream);
    bool Put(nint stream, ReadOnlySpan<byte> data);
    bool Gain(nint stream, float volume);
    int Queued(nint stream);
    int Available(nint stream);
    bool Flush(nint stream);
    void Destroy(nint stream);
    void Quit();
    string Error { get; }
}

internal sealed class SdlAudioApi : ISdlAudioApi
{
    public bool Initialize() => SDL.InitSubSystem(SDL.InitFlags.Audio);
    public nint Open(int rate, int channels)
    {
        var spec = new SDL.AudioSpec { Format = SDL.AudioFormat.AudioS16LE, Freq = rate, Channels = channels };
        return SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in spec, null!, 0);
    }
    public bool Resume(nint stream) => SDL.ResumeAudioStreamDevice(stream);
    public bool Put(nint stream, ReadOnlySpan<byte> data) => SDL.PutAudioStreamData(stream, data, data.Length);
    public bool Gain(nint stream, float volume) => SDL.SetAudioStreamGain(stream, volume);
    public int Queued(nint stream) => SDL.GetAudioStreamQueued(stream);
    public int Available(nint stream) => SDL.GetAudioStreamAvailable(stream);
    public bool Flush(nint stream) => SDL.FlushAudioStream(stream);
    public void Destroy(nint stream) => SDL.DestroyAudioStream(stream);
    public void Quit() => SDL.QuitSubSystem(SDL.InitFlags.Audio);
    public string Error => SDL.GetError();
}

/// <summary>Queues bounded PCM chunks on the owning thread; SDL mixes and converts streams.</summary>
internal sealed class SdlAudioDevice(ISdlAudioApi api) : IAudioDevice, IDisposable
{
    private readonly ISdlAudioApi _api = api;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<Playback> _voices = [];
    private nint _anchor;
    private bool _initialized;
    private bool _disposed;
    public bool IsAvailable => !_disposed && _anchor != 0;
    public string? UnavailableReason { get; private set; }

    public void Initialize()
    {
        EnsureThread();
        if (_initialized || _anchor != 0) throw new InvalidOperationException("Audio is already initialized.");
        if (!_api.Initialize()) { UnavailableReason = $"SDL3 audio initialization: {_api.Error}"; return; }
        _initialized = true;
        _anchor = _api.Open(48000, 2);
        if (_anchor == 0)
        {
            UnavailableReason = $"SDL3 audio playback device: {_api.Error}";
            _api.Quit();
            _initialized = false;
        }
    }

    public ISoundPlayback Play(Sound sound, bool loop = false, float volume = 1)
    {
        EnsureThread();
        ArgumentNullException.ThrowIfNull(sound);
        ValidateVolume(volume);
        if (!IsAvailable) return SilentAudioDevice.Instance.Play(sound, loop, volume);
        Update();
        if (_voices.Count >= 128) throw new InvalidOperationException("SDL3 audio supports at most 128 simultaneous voices.");
        var stream = _api.Open(sound.SampleRate, sound.Channels);
        if (stream == 0) throw Failure("stream creation");
        var voice = new Playback(this, sound, stream, loop);
        try
        {
            voice.Volume = volume;
            voice.Fill();
            if (!_api.Resume(stream)) throw Failure("stream start");
            _voices.Add(voice);
            return voice;
        }
        catch { voice.Stop(); throw; }
    }

    public void Update()
    {
        EnsureThread();
        foreach (var voice in _voices)
        {
            voice.Fill();
            if (!voice.IsPlaying) voice.Stop();
        }
        _voices.RemoveAll(v => v.Stream == 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureThread();
        foreach (var voice in _voices) voice.Stop();
        _voices.Clear();
        if (_anchor != 0) { _api.Destroy(_anchor); _anchor = 0; }
        if (_initialized) { _api.Quit(); _initialized = false; }
        _disposed = true;
    }

    private void EnsureThread()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Audio operations must run on the platform owning thread.");
    }
    private Sdl3Exception Failure(string operation) => new($"SDL3 audio {operation}", _api.Error);
    private static void ValidateVolume(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
    }

    private sealed class Playback(SdlAudioDevice owner, Sound sound, nint stream, bool loop) : ISoundPlayback
    {
        public nint Stream { get; private set; } = stream;
        private int _offset;
        private float _volume = 1;
        public bool IsPlaying
        {
            get
            {
                if (Stream == 0) return false;
                owner.EnsureThread();
                return loop || _offset < sound.Pcm.Length || Queued() > 0 || Available() > 0;
            }
        }
        public float Volume
        {
            get => _volume;
            set
            {
                ValidateVolume(value);
                owner.EnsureThread();
                if (Stream == 0) throw new ObjectDisposedException(nameof(Playback));
                if (!owner._api.Gain(Stream, value)) throw owner.Failure("volume");
                _volume = value;
            }
        }
        private int Queued()
        {
            var count = owner._api.Queued(Stream);
            return count >= 0 ? count : throw owner.Failure("queue inspection");
        }
        private int Available()
        {
            var count = owner._api.Available(Stream);
            return count >= 0 ? count : throw owner.Failure("playback inspection");
        }
        public void Fill()
        {
            if (Stream == 0) return;
            var target = sound.SampleRate * sound.Channels * 2 / 10;
            target -= target % (sound.Channels * 2);
            var queued = Queued();
            while (queued < target)
            {
                if (_offset == sound.Pcm.Length)
                {
                    if (!loop) break;
                    _offset = 0;
                }
                var count = Math.Min(target - queued, sound.Pcm.Length - _offset);
                if (!owner._api.Put(Stream, sound.Pcm.Slice(_offset, count))) throw owner.Failure("PCM queueing");
                _offset += count;
                queued += count;
                if (!loop && _offset == sound.Pcm.Length && !owner._api.Flush(Stream)) throw owner.Failure("stream flush");
            }
        }
        public void Stop()
        {
            if (Stream == 0) return;
            owner.EnsureThread();
            owner._api.Destroy(Stream);
            Stream = 0;
        }
        public void Dispose() => Stop();
    }
}
