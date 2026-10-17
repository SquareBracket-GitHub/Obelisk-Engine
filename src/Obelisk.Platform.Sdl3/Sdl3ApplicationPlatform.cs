using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Runtime;

namespace Obelisk.Platform.Sdl3;

/// <summary>
/// Provides the SDL3 window and event-pump implementation used by the runtime.
/// </summary>
public sealed class Sdl3ApplicationPlatform : IApplicationPlatform, ITextureDevice
{
    private readonly AppOptions _options;
    private readonly ISdl3Api _api;
    private readonly InputStateTracker _inputState = new();
    private nint _window;
    private nint _gpuDevice;
    private nint _canvasPipeline;
    private bool _windowClaimedByGpu;
    private bool _shaderCompilerInitialized;
    private bool _initialized;
    private bool _minimized;
    private int? _owningThreadId;
    private SdlAudioDevice? _audio;
    /// <summary>Gets the owning-thread audio device or silent fallback.</summary>
    public IAudioDevice Audio => (IAudioDevice?)_audio ?? SilentAudioDevice.Instance;

    /// <summary>Gets the latest immutable keyboard and mouse state.</summary>
    public InputSnapshot Input { get; private set; } = InputSnapshot.Empty;

    /// <summary>Gets the latest drawable dimensions in pixels.</summary>
    public OutputSize OutputSize { get; private set; }

    /// <summary>Gets this backend's owning-thread texture factory.</summary>
    public ITextureDevice Textures => this;

    /// <summary>Uploads bounded RGBA8 pixels to the initialized owning-thread device; the caller owns the texture.</summary>
    public Texture CreateTexture(RgbaImage image)
    {
        EnsureInitializedOnOwningThread();
        return _api.CreateTexture(_gpuDevice, image);
    }

    internal nint WindowHandle => _window;

    /// <summary>Validates window settings without acquiring native resources until Initialize is called.</summary>
    public Sdl3ApplicationPlatform(AppOptions options)
        : this(options, Sdl3NativeApi.Instance)
    {
    }

    internal Sdl3ApplicationPlatform(AppOptions options, ISdl3Api api)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(api);

        if (string.IsNullOrWhiteSpace(options.Title))
        {
            throw new ArgumentException("Window title must not be empty.", nameof(options));
        }

        if (options.Width <= 0 || options.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Window width and height must be greater than zero.");
        }

        _options = options;
        _api = api;
        OutputSize = new OutputSize(options.Width, options.Height);
    }

    /// <summary>Acquires window, audio, shader and GPU resources on the calling thread; rolls back on failure.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            throw new InvalidOperationException("The SDL3 platform is already initialized.");
        }

        _owningThreadId = Environment.CurrentManagedThreadId;
        _minimized = false;

        if (!_api.InitializeVideo())
        {
            _owningThreadId = null;
            throw CreateException("SDL3 video initialization");
        }

        _initialized = true;

        try
        {
            if (_api.AudioApi is { } audioApi)
            {
                _audio = new SdlAudioDevice(audioApi);
                _audio.Initialize();
            }
            if (!_api.InitializeShaderCompiler())
            {
                throw CreateException("SDL_shadercross initialization");
            }

            _shaderCompilerInitialized = true;
            _window = _api.CreateWindow(
                _options.Title,
                _options.Width,
                _options.Height,
                _options.Resizable);

            if (_window == 0)
            {
                throw CreateException("SDL3 window creation");
            }

            _gpuDevice = _api.CreateGpuDevice();

            if (_gpuDevice == 0)
            {
                throw CreateException("SDL_GPU device creation");
            }

            if (!_api.ClaimWindowForGpuDevice(_gpuDevice, _window))
            {
                throw CreateException("SDL_GPU swapchain creation");
            }

            _windowClaimedByGpu = true;
            _canvasPipeline = _api.CreateCanvasPipeline(_gpuDevice, _window);

            if (_canvasPipeline == 0)
            {
                throw CreateException("SDL_GPU Canvas pipeline creation");
            }

            Present(new RenderContext(default, OutputSize));
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    /// <summary>Captures input and resize state; returns false for quit or Escape, including focus-loss key releases.</summary>
    public bool PumpEvents()
    {
        EnsureInitializedOnOwningThread();
        _inputState.BeginFrame();
        var shouldRunFrame = true;

        while (_api.TryPollEvent(out var platformEvent))
        {
            _inputState.Apply(platformEvent);

            if (platformEvent.Type == Sdl3EventType.WindowMinimized) _minimized = true;
            if (platformEvent.Type == Sdl3EventType.WindowRestored) _minimized = false;

            if (platformEvent.Type is Sdl3EventType.WindowResized &&
                platformEvent.Width >= 0 && platformEvent.Height >= 0)
            {
                OutputSize = new OutputSize(platformEvent.Width, platformEvent.Height);
            }

            if (platformEvent.Type is Sdl3EventType.QuitRequested ||
                (platformEvent.Type is Sdl3EventType.KeyDown && platformEvent.Key is Key.Escape))
            {
                shouldRunFrame = false;
            }
        }

        Input = _inputState.Capture();
        return shouldRunFrame;
    }

    /// <summary>Submits frame commands on the owning thread; minimized/zero-size output suspends presentation without stopping updates.</summary>
    public void Present(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureInitializedOnOwningThread();
        _audio?.Update();
        if (_minimized || OutputSize.Width == 0 || OutputSize.Height == 0)
        {
            Thread.Sleep(16); // Keep pumping input without spinning while presentation is suspended.
            return;
        }

        if (_gpuDevice == 0 || _canvasPipeline == 0 ||
            !_api.RenderGpuFrame(
                _gpuDevice,
                _window,
                _canvasPipeline,
                context.Canvas.Commands))
        {
            throw CreateException("SDL_GPU frame rendering");
        }
    }

    /// <summary>Releases surviving platform resources on the owning thread; repeat calls after cleanup are safe.</summary>
    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        EnsureOwningThread();

        try
        {
            _audio?.Dispose();
            _audio = null;
            if (_canvasPipeline != 0)
            {
                _api.ReleaseGpuPipeline(_gpuDevice, _canvasPipeline);
                _canvasPipeline = 0;
            }

            if (_windowClaimedByGpu)
            {
                _api.ReleaseWindowFromGpuDevice(_gpuDevice, _window);
                _windowClaimedByGpu = false;
            }

            if (_gpuDevice != 0)
            {
                _api.DestroyGpuDevice(_gpuDevice);
                _gpuDevice = 0;
            }

            if (_window != 0)
            {
                _api.DestroyWindow(_window);
                _window = 0;
            }
        }
        finally
        {
            if (_shaderCompilerInitialized)
            {
                _api.QuitShaderCompiler();
                _shaderCompilerInitialized = false;
            }

            _api.Quit();
            _initialized = false;
            _owningThreadId = null;
        }
    }

    private Sdl3Exception CreateException(string operation)
    {
        return new Sdl3Exception(operation, _api.GetError());
    }

    private void EnsureInitializedOnOwningThread()
    {
        if (!_initialized || _window == 0)
        {
            throw new InvalidOperationException("The SDL3 platform is not initialized.");
        }

        EnsureOwningThread();
    }

    private void EnsureOwningThread()
    {
        if (_owningThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "SDL3 window operations must run on the thread that initialized the platform.");
        }
    }
}
