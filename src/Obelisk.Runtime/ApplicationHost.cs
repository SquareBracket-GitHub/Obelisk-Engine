using Obelisk.Abstractions;

namespace Obelisk.Runtime;

/// <summary>
/// Runs one game instance against a platform backend.
/// </summary>
public sealed class ApplicationHost
{
    private readonly IApplicationPlatform _platform;
    private readonly IMonotonicClock _clock;
    private readonly GameLoopOptions _options;

    public ApplicationHost(
        IApplicationPlatform platform,
        IMonotonicClock? clock = null,
        GameLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(platform);

        _platform = platform;
        _clock = clock ?? new StopwatchClock();
        _options = options ?? new GameLoopOptions();

        if (_options.MaximumDeltaTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.MaximumDeltaTime,
                "Maximum delta time must be greater than zero.");
        }
    }

    public ApplicationState State { get; private set; } = ApplicationState.Created;

    public int Run(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (State is not ApplicationState.Created)
        {
            throw new InvalidOperationException("An application host can only run once.");
        }

        var platformInitialized = false;
        var gameInitialized = false;

        try
        {
            State = ApplicationState.Initializing;
            _platform.Initialize();
            platformInitialized = true;

            game.Initialize(new InitializeContext());
            gameInitialized = true;

            var previousTime = _clock.Elapsed;
            var simulationTime = TimeSpan.Zero;
            ulong frameNumber = 0;

            State = ApplicationState.Running;

            while (_platform.PumpEvents())
            {
                var currentTime = _clock.Elapsed;
                var measuredDelta = currentTime - previousTime;
                previousTime = currentTime;

                if (measuredDelta < TimeSpan.Zero)
                {
                    throw new InvalidOperationException("The monotonic clock moved backwards.");
                }

                var deltaTime = measuredDelta > _options.MaximumDeltaTime
                    ? _options.MaximumDeltaTime
                    : measuredDelta;

                simulationTime += deltaTime;

                var time = new GameTime(simulationTime, deltaTime, frameNumber);
                game.Update(new UpdateContext(time, _platform.Input, _platform.OutputSize));
                var renderContext = new RenderContext(time, _platform.OutputSize);
                game.Render(renderContext);
                _platform.Present(renderContext);

                frameNumber++;
            }

            return 0;
        }
        finally
        {
            State = ApplicationState.ShuttingDown;

            try
            {
                if (gameInitialized)
                {
                    game.Shutdown(new ShutdownContext());
                }
            }
            finally
            {
                if (platformInitialized)
                {
                    _platform.Shutdown();
                }

                State = ApplicationState.Stopped;
            }
        }
    }
}
