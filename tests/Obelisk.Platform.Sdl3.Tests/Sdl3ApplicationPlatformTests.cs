using Obelisk.Abstractions;
using SDL3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed class Sdl3ApplicationPlatformTests
{
    [Fact]
    public void NativeBackend_InitializesWindowAndConsumesQuitEvent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new NativePlatformScope();
        var nativeEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.Quit,
        };

        Assert.True(SDL.PushEvent(ref nativeEvent));
        Assert.False(platform.Platform.PumpEvents());
    }

    [Fact]
    public void NativeBackend_ConsumesEscapeKeyEvent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new NativePlatformScope();
        var nativeEvent = new SDL.Event
        {
            Key = new SDL.KeyboardEvent
            {
                Type = SDL.EventType.KeyDown,
                Key = SDL.Keycode.Escape,
                Down = true,
            },
        };

        Assert.True(SDL.PushEvent(ref nativeEvent));
        Assert.False(platform.Platform.PumpEvents());
    }

    [Fact]
    public void Initialize_CreatesConfiguredWindowAndClearsItToBlack()
    {
        var api = new FakeSdl3Api();
        var options = new AppOptions
        {
            Title = "Test Game",
            Width = 960,
            Height = 540,
            Resizable = false,
        };
        var platform = new Sdl3ApplicationPlatform(options, api);

        platform.Initialize();

        Assert.Equal(
            [
                "initialize-video",
                "create-window:Test Game:960:540:False",
                "clear-window:42",
            ],
            api.Calls);
    }

    [Fact]
    public void PumpEvents_StopsForQuitEvent()
    {
        AssertStopsForExitEvent(Sdl3PlatformEvent.QuitRequested);
    }

    [Fact]
    public void PumpEvents_StopsForEscapeKey()
    {
        AssertStopsForExitEvent(Sdl3PlatformEvent.EscapePressed);
    }

    private static void AssertStopsForExitEvent(Sdl3PlatformEvent exitEvent)
    {
        var api = new FakeSdl3Api();
        api.Events.Enqueue(Sdl3PlatformEvent.Other);
        api.Events.Enqueue(exitEvent);
        var platform = CreateInitializedPlatform(api);

        var shouldRunFrame = platform.PumpEvents();

        Assert.False(shouldRunFrame);
        Assert.Empty(api.Events);
    }

    [Fact]
    public void PumpEvents_ContinuesWhenNoExitWasRequested()
    {
        var api = new FakeSdl3Api();
        api.Events.Enqueue(Sdl3PlatformEvent.Other);
        var platform = CreateInitializedPlatform(api);

        Assert.True(platform.PumpEvents());
    }

    [Fact]
    public void Shutdown_DestroysWindowBeforeQuittingSdl()
    {
        var api = new FakeSdl3Api();
        var platform = CreateInitializedPlatform(api);

        platform.Shutdown();
        platform.Shutdown();

        Assert.Equal(
            [
                "initialize-video",
                "create-window:Test:640:480:True",
                "clear-window:42",
                "destroy-window:42",
                "quit",
            ],
            api.Calls);
    }

    [Fact]
    public void Initialize_ReportsNativeInitializationError()
    {
        var api = new FakeSdl3Api
        {
            InitializeVideoResult = false,
            Error = "video driver unavailable",
        };
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), api);

        var exception = Assert.Throws<Sdl3Exception>(platform.Initialize);

        Assert.Equal("SDL3 video initialization", exception.Operation);
        Assert.Equal("video driver unavailable", exception.NativeError);
        Assert.Contains("video driver unavailable", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("quit", api.Calls);
    }

    [Fact]
    public void Initialize_CleansUpSdlWhenWindowCreationFails()
    {
        var api = new FakeSdl3Api
        {
            Window = 0,
            Error = "window creation failed",
        };
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), api);

        var exception = Assert.Throws<Sdl3Exception>(platform.Initialize);

        Assert.Equal("SDL3 window creation", exception.Operation);
        Assert.Equal("window creation failed", exception.NativeError);
        Assert.Equal("quit", api.Calls[^1]);
        Assert.DoesNotContain(api.Calls, call => call.StartsWith("destroy-window", StringComparison.Ordinal));
    }

    [Fact]
    public void Initialize_DestroysWindowWhenInitialClearFails()
    {
        var api = new FakeSdl3Api
        {
            ClearWindowResult = false,
            Error = "surface unavailable",
        };
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), api);

        var exception = Assert.Throws<Sdl3Exception>(platform.Initialize);

        Assert.Equal("SDL3 window surface clear", exception.Operation);
        Assert.Equal("destroy-window:42", api.Calls[^2]);
        Assert.Equal("quit", api.Calls[^1]);
    }

    private static Sdl3ApplicationPlatform CreateInitializedPlatform(FakeSdl3Api api)
    {
        var platform = new Sdl3ApplicationPlatform(
            new AppOptions
            {
                Title = "Test",
                Width = 640,
                Height = 480,
            },
            api);
        platform.Initialize();
        return platform;
    }

    private sealed class NativePlatformScope : IDisposable
    {
        public NativePlatformScope()
        {
            Platform = new Sdl3ApplicationPlatform(
                new AppOptions
                {
                    Title = "Obelisk SDL3 Integration Test",
                    Width = 320,
                    Height = 180,
                    Resizable = false,
                });
            Platform.Initialize();
        }

        public Sdl3ApplicationPlatform Platform { get; }

        public void Dispose()
        {
            Platform.Shutdown();
        }
    }

    private sealed class FakeSdl3Api : ISdl3Api
    {
        public bool InitializeVideoResult { get; init; } = true;

        public nint Window { get; init; } = 42;

        public bool ClearWindowResult { get; init; } = true;

        public string Error { get; init; } = string.Empty;

        public Queue<Sdl3PlatformEvent> Events { get; } = [];

        public List<string> Calls { get; } = [];

        public bool InitializeVideo()
        {
            Calls.Add("initialize-video");
            return InitializeVideoResult;
        }

        public nint CreateWindow(string title, int width, int height, bool resizable)
        {
            Calls.Add($"create-window:{title}:{width}:{height}:{resizable}");
            return Window;
        }

        public bool ClearWindowToBlack(nint window)
        {
            Calls.Add($"clear-window:{window}");
            return ClearWindowResult;
        }

        public bool TryPollEvent(out Sdl3PlatformEvent platformEvent)
        {
            if (Events.TryDequeue(out platformEvent))
            {
                return true;
            }

            platformEvent = Sdl3PlatformEvent.Other;
            return false;
        }

        public void DestroyWindow(nint window)
        {
            Calls.Add($"destroy-window:{window}");
        }

        public void Quit()
        {
            Calls.Add("quit");
        }

        public string GetError()
        {
            return Error;
        }
    }
}
