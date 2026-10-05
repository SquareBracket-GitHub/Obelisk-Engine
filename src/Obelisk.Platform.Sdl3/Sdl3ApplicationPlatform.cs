using Obelisk.Abstractions;
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
    private bool _initialized;
    private int? _owningThreadId;

    public InputSnapshot Input { get; private set; } = InputSnapshot.Empty;

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
            _window = _api.CreateWindow(
                _options.Title,
                _options.Width,
                _options.Height,
                _options.Resizable);

            if (_window == 0)
            {
                throw CreateException("SDL3 window creation");
            }

            if (!_api.ClearWindowToBlack(_window))
            {
                throw CreateException("SDL3 window surface clear");
            }
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

            if (platformEvent.Type is Sdl3EventType.QuitRequested ||
                (platformEvent.Type is Sdl3EventType.KeyDown && platformEvent.Key is Key.Escape))
            {
                shouldRunFrame = false;
            }
        }

        Input = _inputState.Capture();
        return shouldRunFrame;
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
            if (_window != 0)
            {
                _api.DestroyWindow(_window);
                _window = 0;
            }
        }
        finally
        {
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
