using Obelisk.Abstractions;
using Obelisk.Assets;
using Pong;
using SDL3;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed partial class Sdl3ApplicationPlatformTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(960)]
    public void NativePong_TitleTextRemainsVisibleAfterScaling(int width)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        using var game = new PongGame();
        game.Initialize(new InitializeContext(renderer));
        var height = width * 9 / 16;
        var render = new RenderContext(default, new OutputSize(width, height));
        game.Render(render);
        var pixels = RenderPixels(renderer, render.Canvas, width);
        var bright = 0;
        for (var y = (int)(165 * width / 960f); y < (int)(213 * width / 960f); y++)
            for (var x = width / 4; x < width * 3 / 4; x++)
            {
                var offset = (y * width + x) * 4;
                if (pixels[offset] > 180 && pixels[offset + 1] > 180 && pixels[offset + 2] > 180) bright++;
            }
        Assert.True(bright > 20, $"Title glyphs should remain visible at width {width}: {bright} pixels.");
        // Keep an ignored image beside test binaries for visual review of the actual GPU output.
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), width * height * 4);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"pong-title-{width}.png"));
        encoded.SaveTo(output);
    }
    [Fact]
    public void Audio_PlatformInitializationFailureAndShutdownReleaseAudio()
    {
        var audioApi = new FakeAudio();
        var failing = new Sdl3ApplicationPlatform(new AppOptions(), new FakeSdl3Api { AudioApi = audioApi, Window = 0 });
        Assert.Throws<Sdl3Exception>(() => failing.Initialize());
        Assert.Empty(audioApi.Streams);
        Assert.Equal(1, audioApi.Quits);
        failing.Shutdown();
        var api = new FakeAudio();
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), new FakeSdl3Api { AudioApi = api });
        platform.Initialize();
        var voice = platform.Audio.Play(new Sound(8000, 1, new byte[16]), true);
        platform.Shutdown(); platform.Shutdown(); voice.Dispose();
        Assert.False(voice.IsPlaying);
        Assert.Empty(api.Streams);
        Assert.Equal(1, api.Quits);
    }

    [Fact]
    public void Audio_DeviceVoiceLimitBoundsNativeResources()
    {
        var api = new FakeAudio();
        using var audio = new SdlAudioDevice(api);
        audio.Initialize();
        var sound = new Sound(8000, 1, new byte[16]);
        for (var i = 0; i < 128; i++) audio.Play(sound, true);
        Assert.Throws<InvalidOperationException>(() => audio.Play(sound));
        audio.Dispose();
        Assert.Empty(api.Streams);
        Assert.Equal(129, api.Destroyed.Count);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Audio_NoSubsystemOrOutputDeviceContinuesSilently(bool initFails)
    {
        var api = new FakeAudio { InitFails = initFails, OpenFails = !initFails };
        using var audio = new SdlAudioDevice(api);
        audio.Initialize();
        Assert.False(audio.IsAvailable);
        Assert.Contains("no device", audio.UnavailableReason!);
        using var voice = audio.Play(new Sound(8000, 1, new byte[16]), loop: true);
        Assert.False(voice.IsPlaying);
        audio.Dispose(); audio.Dispose();
        Assert.Equal(initFails ? 0 : 1, api.Quits);
        Assert.Empty(api.Streams);
    }
    [Fact]
    public void Audio_LoopRefillsBoundedQueueAndStopReleasesStreams()
    {
        var api = new FakeAudio();
        using var audio = new SdlAudioDevice(api);
        audio.Initialize();
        using var voice = audio.Play(new Sound(8000, 1, new byte[16]), true, 0.4f);
        Assert.Equal(1600, api.Streams[2]);
        api.Streams[2] = 0;
        audio.Update();
        Assert.Equal(1600, api.Streams[2]);
        Assert.True(voice.IsPlaying);
        voice.Volume = 0.2f;
        Assert.Equal(0.2f, api.LastGain);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Volume = -1);
        voice.Stop(); voice.Dispose();
        Assert.False(voice.IsPlaying);
        audio.Dispose();
        Assert.Empty(api.Streams);
        Assert.Equal(2, api.Destroyed.Count);
        Assert.Equal(1, api.Quits);
    }
    [Fact]
    public void Audio_OneShotCompletesAndFailuresCleanUpPartialPlayback()
    {
        var api = new FakeAudio();
        using var audio = new SdlAudioDevice(api);
        audio.Initialize();
        using var voice = audio.Play(new Sound(8000, 1, new byte[16]));
        Assert.Equal(1, api.Flushes);
        api.Streams[2] = 0;
        audio.Update();
        Assert.False(voice.IsPlaying);
        api.ResumeFails = true;
        var error = Assert.Throws<Sdl3Exception>(() => audio.Play(new Sound(8000, 1, new byte[16])));
        Assert.Contains("audio stream start", error.Message);
        Assert.Single(api.Streams); // Only anchor remains.
        api.ResumeFails = false; api.PutFails = true;
        Assert.Throws<Sdl3Exception>(() => audio.Play(new Sound(8000, 1, new byte[16])));
        Assert.Single(api.Streams);
        Assert.Throws<InvalidOperationException>(() => Task.Run(() => audio.Play(new Sound(8000, 1, new byte[16]))).GetAwaiter().GetResult());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pong_TitleCollisionScoreWinAndRestart(bool leftWins)
    {
        var match = new PongMatch();
        var sounds = new List<MatchSound>();
        match.Sound += sounds.Add;
        Assert.Equal(MatchState.Title, match.State);
        match.Update(0.1f, 1, 1);
        Assert.Equal(222, match.LeftY);
        match.Start();
        // Winner follows the ball; the losing player stays at the top.
        for (var i = 0; i < 60000 && match.State != MatchState.GameOver; i++)
            match.Update(1f / 120, leftWins ? MathF.Sign(match.Ball.Y - match.LeftY - 48) : -1,
                leftWins ? -1 : MathF.Sign(match.Ball.Y - match.RightY - 48));
        Assert.Equal(MatchState.GameOver, match.State);
        Assert.Equal(PongMatch.WinningScore, leftWins ? match.LeftScore : match.RightScore);
        Assert.Contains(MatchSound.Hit, sounds);
        Assert.Contains(MatchSound.Score, sounds);
        Assert.Contains(MatchSound.Win, sounds);
        match.Start();
        Assert.Equal(MatchState.Playing, match.State);
        Assert.Equal(0, match.LeftScore); Assert.Equal(0, match.RightScore);
        Assert.Equal(222, match.LeftY); Assert.Equal(222, match.RightY);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativePong_PlaysFullMatchRestartsAndShutsDown(bool withAudio)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var game = new PongGame();
        game.Initialize(new InitializeContext(platform.Platform.Textures, withAudio ? platform.Platform.Audio : SilentAudioDevice.Instance));
        Assert.True(platform.Platform.Audio.IsAvailable, platform.Platform.Audio.UnavailableReason);
        var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / 60), 0);
        var render = new RenderContext(time, platform.Platform.OutputSize);
        game.Render(render); platform.Platform.Present(render); // Title.
        game.Match.Start();
        for (var frame = 0; frame < 40000 && game.Match.State != MatchState.GameOver; frame++)
        {
            game.Match.Update(1f / 120, MathF.Sign(game.Match.Ball.Y - game.Match.LeftY - 48), -1);
            if (frame % 120 != 0 && game.Match.State != MatchState.GameOver) continue;
            render = new RenderContext(time, platform.Platform.OutputSize);
            game.Render(render); platform.Platform.Present(render);
        }
        Assert.Equal(MatchState.GameOver, game.Match.State);
        var input = new InputSnapshot(new KeyboardSnapshot([Key.Enter], [Key.Enter], []), InputSnapshot.Empty.Mouse);
        game.Update(new UpdateContext(time, input));
        Assert.Equal(MatchState.Playing, game.Match.State);
        render = new RenderContext(time, platform.Platform.OutputSize);
        game.Render(render); platform.Platform.Present(render);
        game.Shutdown(new ShutdownContext());
    }
    [Fact]
    public void NativeAudio_WavCompletesLoopContinuesAndShutdownIsIdempotent()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var audio = new SdlAudioDevice(new SdlAudioApi());
        audio.Initialize();
        Assert.True(audio.IsAvailable, audio.UnavailableReason);
        var hit = WavLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "assets", "hit.wav"));
        using var once = audio.Play(hit, volume: 0);
        using var loop = audio.Play(hit, true, 0);
        for (var i = 0; i < 25; i++) { Thread.Sleep(10); audio.Update(); }
        Assert.False(once.IsPlaying);
        Assert.True(loop.IsPlaying);
        loop.Volume = 0.1f;
        loop.Stop();
        audio.Dispose(); audio.Dispose();
        Assert.False(loop.IsPlaying);
        Assert.False(audio.IsAvailable);
    }
    private sealed class FakeAudio : ISdlAudioApi
    {
        public bool InitFails, OpenFails, ResumeFails, PutFails;
        public int Quits, Flushes;
        public float LastGain;
        private int _next;
        public Dictionary<nint, int> Streams { get; } = [];
        public HashSet<nint> Destroyed { get; } = [];
        public string Error => "no device";
        public bool Initialize() => !InitFails;
        public nint Open(int rate, int channels)
        {
            if (OpenFails) return 0;
            nint stream = ++_next; Streams.Add(stream, 0); return stream;
        }
        public bool Resume(nint stream) => !ResumeFails;
        public bool Put(nint stream, ReadOnlySpan<byte> data)
        { if (PutFails) return false; Streams[stream] += data.Length; return true; }
        public bool Gain(nint stream, float volume) { LastGain = volume; return true; }
        public int Queued(nint stream) => Streams[stream];
        public int Available(nint stream) => 0;
        public bool Flush(nint stream) { Flushes++; return true; }
        public void Destroy(nint stream) { Assert.True(Destroyed.Add(stream)); Streams.Remove(stream); }
        public void Quit() => Quits++;
    }
}
