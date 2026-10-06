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
    public void NativeBackend_RendersGameRectangleAfterResize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new NativePlatformScope();
        Assert.True(SDL.SetWindowSize(platform.Platform.WindowHandle, 480, 270));
        Assert.True(SDL.SyncWindow(platform.Platform.WindowHandle));
        Assert.True(SDL.GetWindowSizeInPixels(
            platform.Platform.WindowHandle,
            out var pixelWidth,
            out var pixelHeight));

        Assert.True(platform.Platform.PumpEvents());
        Assert.Equal(new OutputSize(pixelWidth, pixelHeight), platform.Platform.OutputSize);

        var context = new RenderContext(default, platform.Platform.OutputSize);
        context.FillRectangle(new RectangleF(40, 30, 180, 110), ColorRgba.Orange);
        platform.Platform.Present(context);
    }

    [Fact]
    public void Initialize_CreatesWindowGpuDeviceSwapchainPipelineAndFirstFrame()
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
                "initialize-shader-compiler",
                "create-window:Test Game:960:540:False",
                "create-gpu-device",
                "claim-window:77:42",
                "create-pipeline:77:42",
                "render-frame:77:42:88",
            ],
            api.Calls);
    }

    [Fact]
    public void PumpEvents_StopsForQuitEvent()
    {
        AssertStopsForExitEvent(new Sdl3PlatformEvent(Sdl3EventType.QuitRequested));
    }

    [Fact]
    public void PumpEvents_StopsForEscapeKey()
    {
        AssertStopsForExitEvent(new Sdl3PlatformEvent(Sdl3EventType.KeyDown, Key.Escape));
    }

    private static void AssertStopsForExitEvent(Sdl3PlatformEvent exitEvent)
    {
        var api = new FakeSdl3Api();
        api.Events.Enqueue(new Sdl3PlatformEvent(Sdl3EventType.Other));
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
        api.Events.Enqueue(new Sdl3PlatformEvent(Sdl3EventType.Other));
        var platform = CreateInitializedPlatform(api);

        Assert.True(platform.PumpEvents());
    }

    [Fact]
    public void PumpEvents_UpdatesOutputSizeAfterWindowResize()
    {
        var api = new FakeSdl3Api();
        var platform = CreateInitializedPlatform(api);
        api.Events.Enqueue(new Sdl3PlatformEvent(
            Sdl3EventType.WindowResized,
            Width: 1024,
            Height: 576));

        Assert.True(platform.PumpEvents());
        Assert.Equal(new OutputSize(1024, 576), platform.OutputSize);
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
                "initialize-shader-compiler",
                "create-window:Test:640:480:True",
                "create-gpu-device",
                "claim-window:77:42",
                "create-pipeline:77:42",
                "render-frame:77:42:88",
                "release-pipeline:77:88",
                "release-window:77:42",
                "destroy-gpu-device:77",
                "destroy-window:42",
                "quit-shader-compiler",
                "quit",
            ],
            api.Calls);
    }

    [Fact]
    public void KeyboardSnapshot_TransitionsAcrossFramesAndIgnoresRepeatedDown()
    {
        var api = new FakeSdl3Api();
        var platform = CreateInitializedPlatform(api);
        api.Events.Enqueue(new Sdl3PlatformEvent(Sdl3EventType.KeyDown, Key.A));
        api.Events.Enqueue(new Sdl3PlatformEvent(Sdl3EventType.KeyDown, Key.A));

        Assert.True(platform.PumpEvents());
        Assert.True(platform.Input.Keyboard.IsDown(Key.A));
        Assert.True(platform.Input.Keyboard.WasPressed(Key.A));
        Assert.False(platform.Input.Keyboard.WasReleased(Key.A));

        Assert.True(platform.PumpEvents());
        Assert.True(platform.Input.Keyboard.IsDown(Key.A));
        Assert.False(platform.Input.Keyboard.WasPressed(Key.A));

        api.Events.Enqueue(new Sdl3PlatformEvent(Sdl3EventType.KeyUp, Key.A));
        Assert.True(platform.PumpEvents());
        Assert.False(platform.Input.Keyboard.IsDown(Key.A));
        Assert.True(platform.Input.Keyboard.WasReleased(Key.A));

        Assert.True(platform.PumpEvents());
        Assert.False(platform.Input.Keyboard.WasReleased(Key.A));
    }

    [Fact]
    public void MouseSnapshot_TracksPositionAndButtonTransitions()
    {
        var api = new FakeSdl3Api();
        var platform = CreateInitializedPlatform(api);
        api.Events.Enqueue(new Sdl3PlatformEvent(Sdl3EventType.MouseMoved, MouseX: 12.5f, MouseY: 24.25f));
        api.Events.Enqueue(new Sdl3PlatformEvent(
            Sdl3EventType.MouseButtonDown,
            MouseButton: MouseButton.Left,
            MouseX: 13,
            MouseY: 25));

        Assert.True(platform.PumpEvents());
        Assert.Equal(13, platform.Input.Mouse.X);
        Assert.Equal(25, platform.Input.Mouse.Y);
        Assert.True(platform.Input.Mouse.IsDown(MouseButton.Left));
        Assert.True(platform.Input.Mouse.WasPressed(MouseButton.Left));

        api.Events.Enqueue(new Sdl3PlatformEvent(
            Sdl3EventType.MouseButtonUp,
            MouseButton: MouseButton.Left,
            MouseX: 18,
            MouseY: 30));

        Assert.True(platform.PumpEvents());
        Assert.Equal(18, platform.Input.Mouse.X);
        Assert.Equal(30, platform.Input.Mouse.Y);
        Assert.False(platform.Input.Mouse.IsDown(MouseButton.Left));
        Assert.True(platform.Input.Mouse.WasReleased(MouseButton.Left));
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
    public void Initialize_ReportsShaderCompilerInitializationError()
    {
        var api = new FakeSdl3Api
        {
            ShaderCompilerResult = false,
            Error = "shader compiler unavailable",
        };
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), api);

        var exception = Assert.Throws<Sdl3Exception>(platform.Initialize);

        Assert.Equal("SDL_shadercross initialization", exception.Operation);
        Assert.Contains("shader compiler unavailable", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("quit-shader-compiler", api.Calls);
        Assert.Equal("quit", api.Calls[^1]);
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
    public void Initialize_CleansUpGpuResourcesWhenInitialFrameFails()
    {
        var api = new FakeSdl3Api
        {
            RenderGpuFrameResult = false,
            Error = "swapchain submission failed",
        };
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), api);

        var exception = Assert.Throws<Sdl3Exception>(platform.Initialize);

        Assert.Equal("SDL_GPU frame rendering", exception.Operation);
        Assert.Equal("quit-shader-compiler", api.Calls[^2]);
        Assert.Equal("quit", api.Calls[^1]);
    }

    [Theory]
    [InlineData("device", "SDL_GPU device creation")]
    [InlineData("swapchain", "SDL_GPU swapchain creation")]
    [InlineData("pipeline", "SDL_GPU solid rectangle pipeline creation")]
    public void Initialize_ReportsTheFailingGpuStage(string failingStage, string expectedOperation)
    {
        var api = new FakeSdl3Api
        {
            GpuDevice = failingStage == "device" ? 0 : 77,
            ClaimWindowResult = failingStage != "swapchain",
            Pipeline = failingStage == "pipeline" ? 0 : 88,
            Error = $"{failingStage} unavailable",
        };
        var platform = new Sdl3ApplicationPlatform(new AppOptions(), api);

        var exception = Assert.Throws<Sdl3Exception>(platform.Initialize);

        Assert.Equal(expectedOperation, exception.Operation);
        Assert.Contains(failingStage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("quit", api.Calls[^1]);
    }

    [Fact]
    public void Present_SubmitsAnotherGpuFrame()
    {
        var api = new FakeSdl3Api();
        var platform = CreateInitializedPlatform(api);

        var context = new RenderContext(default, new OutputSize(640, 480));
        context.FillRectangle(new RectangleF(10, 20, 30, 40), ColorRgba.Orange);

        platform.Present(context);

        Assert.Equal(2, api.Calls.Count(call => call == "render-frame:77:42:88"));
        Assert.Single(api.LastSolidRectangles);
        Assert.Equal(new RectangleF(10, 20, 30, 40), api.LastSolidRectangles[0].Bounds);
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

        public bool ShaderCompilerResult { get; init; } = true;

        public nint GpuDevice { get; init; } = 77;

        public bool ClaimWindowResult { get; init; } = true;

        public nint Pipeline { get; init; } = 88;

        public bool RenderGpuFrameResult { get; init; } = true;

        public string Error { get; init; } = string.Empty;

        public Queue<Sdl3PlatformEvent> Events { get; } = [];

        public List<string> Calls { get; } = [];

        public IReadOnlyList<SolidRectangle> LastSolidRectangles { get; private set; } = [];

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

        public bool InitializeShaderCompiler()
        {
            Calls.Add("initialize-shader-compiler");
            return ShaderCompilerResult;
        }

        public nint CreateGpuDevice()
        {
            Calls.Add("create-gpu-device");
            return GpuDevice;
        }

        public bool ClaimWindowForGpuDevice(nint device, nint window)
        {
            Calls.Add($"claim-window:{device}:{window}");
            return ClaimWindowResult;
        }

        public nint CreateSolidRectanglePipeline(nint device, nint window)
        {
            Calls.Add($"create-pipeline:{device}:{window}");
            return Pipeline;
        }

        public bool RenderGpuFrame(
            nint device,
            nint window,
            nint pipeline,
            IReadOnlyList<SolidRectangle> solidRectangles)
        {
            Calls.Add($"render-frame:{device}:{window}:{pipeline}");
            LastSolidRectangles = solidRectangles.ToArray();
            return RenderGpuFrameResult;
        }

        public bool TryPollEvent(out Sdl3PlatformEvent platformEvent)
        {
            if (Events.TryDequeue(out platformEvent))
            {
                return true;
            }

            platformEvent = new Sdl3PlatformEvent(Sdl3EventType.Other);
            return false;
        }

        public void DestroyWindow(nint window)
        {
            Calls.Add($"destroy-window:{window}");
        }

        public void ReleaseGpuPipeline(nint device, nint pipeline)
        {
            Calls.Add($"release-pipeline:{device}:{pipeline}");
        }

        public void ReleaseWindowFromGpuDevice(nint device, nint window)
        {
            Calls.Add($"release-window:{device}:{window}");
        }

        public void DestroyGpuDevice(nint device)
        {
            Calls.Add($"destroy-gpu-device:{device}");
        }

        public void QuitShaderCompiler()
        {
            Calls.Add("quit-shader-compiler");
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
