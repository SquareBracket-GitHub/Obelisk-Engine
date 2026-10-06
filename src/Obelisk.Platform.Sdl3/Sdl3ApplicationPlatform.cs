using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Runtime;

namespace Obelisk.Platform.Sdl3;

/// <summary>
/// Provides the SDL3 window and event-pump implementation used by the runtime.
/// </summary>
public sealed class Sdl3ApplicationPlatform : IApplicationPlatform
{
    private readonly AppOptions _options;
    private readonly ISdl3Api _api;
    private readonly InputStateTracker _inputState = new();
    private nint _window;
    private nint _gpuDevice;
    private nint _solidRectanglePipeline;
    private bool _windowClaimedByGpu;
    private bool _shaderCompilerInitialized;
    private bool _initialized;
    private int? _owningThreadId;

    public InputSnapshot Input { get; private set; } = InputSnapshot.Empty;

    public OutputSize OutputSize { get; private set; }

    internal nint WindowHandle => _window;

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

    public void Initialize()
    {
        if (_initialized)
        {
            throw new InvalidOperationException("The SDL3 platform is already initialized.");
        }

        _owningThreadId = Environment.CurrentManagedThreadId;

        if (!_api.InitializeVideo())
        {
            _owningThreadId = null;
            throw CreateException("SDL3 video initialization");
        }

        _initialized = true;

        try
        {
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
            _solidRectanglePipeline = _api.CreateSolidRectanglePipeline(_gpuDevice, _window);

            if (_solidRectanglePipeline == 0)
            {
                throw CreateException("SDL_GPU solid rectangle pipeline creation");
            }

            Present(new RenderContext(default, OutputSize));
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    public bool PumpEvents()
    {
        EnsureInitializedOnOwningThread();
        _inputState.BeginFrame();
        var shouldRunFrame = true;

        while (_api.TryPollEvent(out var platformEvent))
        {
            _inputState.Apply(platformEvent);

            if (platformEvent.Type is Sdl3EventType.WindowResized &&
                platformEvent.Width > 0 && platformEvent.Height > 0)
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

    public void Present(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureInitializedOnOwningThread();
        var rectangles = context.Canvas.Commands.OfType<RectangleCommand>().ToArray();

        if (_gpuDevice == 0 || _solidRectanglePipeline == 0 ||
            !_api.RenderGpuFrame(
                _gpuDevice,
                _window,
                _solidRectanglePipeline,
                rectangles))
        {
            throw CreateException("SDL_GPU frame rendering");
        }
    }

    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        EnsureOwningThread();

        try
        {
            if (_solidRectanglePipeline != 0)
            {
                _api.ReleaseGpuPipeline(_gpuDevice, _solidRectanglePipeline);
                _solidRectanglePipeline = 0;
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
