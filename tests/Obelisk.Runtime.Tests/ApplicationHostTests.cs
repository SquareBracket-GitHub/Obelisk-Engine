using Obelisk.Abstractions;

namespace Obelisk.Runtime.Tests;

public sealed class ApplicationHostTests
{
    [Fact]
    public void Run_ExecutesLifecycleInOrderAndSharesFrameTime()
    {
        var events = new List<string>();
        var clock = new FakeClock();
        var platform = new FakePlatform(
            clock,
            events,
            TimeSpan.FromMilliseconds(16),
            TimeSpan.FromMilliseconds(20));
        var game = new RecordingGame(events);
        var host = new ApplicationHost(platform, clock);
        game.GetState = () => host.State;

        var exitCode = host.Run(game);

        Assert.Equal(0, exitCode);
        Assert.Equal(ApplicationState.Stopped, host.State);
        Assert.Equal(
            [
                "platform.initialize",
                "game.initialize",
                "platform.pump",
                "game.update",
                "game.render",
                "platform.present",
                "platform.pump",
                "game.update",
                "game.render",
                "platform.present",
                "platform.pump",
                "game.shutdown",
                "platform.shutdown",
            ],
            events);
        Assert.Equal(
            [
                ApplicationState.Initializing,
                ApplicationState.Running,
                ApplicationState.Running,
                ApplicationState.Running,
                ApplicationState.Running,
                ApplicationState.ShuttingDown,
            ],
            game.ObservedStates);

        Assert.Collection(
            game.UpdateTimes,
            first =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(16), first.DeltaTime);
                Assert.Equal(TimeSpan.FromMilliseconds(16), first.Elapsed);
                Assert.Equal(0UL, first.FrameNumber);
            },
            second =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(20), second.DeltaTime);
                Assert.Equal(TimeSpan.FromMilliseconds(36), second.Elapsed);
                Assert.Equal(1UL, second.FrameNumber);
            });
        Assert.Equal(game.UpdateTimes, game.RenderTimes);
    }

    [Fact]
    public void Run_ClampsLargeDeltaAndAccumulatesSimulationTime()
    {
        var clock = new FakeClock();
        var platform = new FakePlatform(
            clock,
            [],
            TimeSpan.FromMilliseconds(16),
            TimeSpan.FromSeconds(2));
        var game = new RecordingGame([]);
        var host = new ApplicationHost(
            platform,
            clock,
            new GameLoopOptions { MaximumDeltaTime = TimeSpan.FromMilliseconds(100) });

        host.Run(game);

        Assert.Equal(TimeSpan.FromMilliseconds(16), game.UpdateTimes[0].DeltaTime);
        Assert.Equal(TimeSpan.FromMilliseconds(100), game.UpdateTimes[1].DeltaTime);
        Assert.Equal(TimeSpan.FromMilliseconds(116), game.UpdateTimes[1].Elapsed);
        Assert.Equal(0.1f, game.UpdateTimes[1].Delta);
    }

    [Fact]
    public void Run_ShutsDownGameAndPlatformWhenUpdateFails()
    {
        var events = new List<string>();
        var clock = new FakeClock();
        var platform = new FakePlatform(clock, events, TimeSpan.FromMilliseconds(16));
        var game = new RecordingGame(events) { UpdateException = new TestException() };
        var host = new ApplicationHost(platform, clock);

        Assert.Throws<TestException>(() => host.Run(game));

        Assert.Equal(ApplicationState.Stopped, host.State);
        Assert.Equal(
            [
                "platform.initialize",
                "game.initialize",
                "platform.pump",
                "game.update",
                "game.shutdown",
                "platform.shutdown",
            ],
            events);
    }

    [Fact]
    public void Run_RejectsAClockThatMovesBackwardsAndStillShutsDown()
    {
        var events = new List<string>();
        var clock = new FakeClock();
        var platform = new FakePlatform(clock, events, TimeSpan.FromMilliseconds(-1));
        var game = new RecordingGame(events);
        var host = new ApplicationHost(platform, clock);

        var exception = Assert.Throws<InvalidOperationException>(() => host.Run(game));

        Assert.Contains("moved backwards", exception.Message, StringComparison.Ordinal);
        Assert.Contains("game.shutdown", events);
        Assert.Contains("platform.shutdown", events);
        Assert.Equal(ApplicationState.Stopped, host.State);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveMaximumDeltaTime()
    {
        var options = new GameLoopOptions { MaximumDeltaTime = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApplicationHost(new FakePlatform(new FakeClock(), []), options: options));
    }

    private sealed class FakeClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Advance(TimeSpan duration)
        {
            Elapsed += duration;
        }
    }

    private sealed class FakePlatform : IApplicationPlatform
    {
        private readonly FakeClock _clock;
        private readonly List<string> _events;
        private readonly IReadOnlyList<TimeSpan> _frameDurations;
        private int _nextFrame;

        public FakePlatform(
            FakeClock clock,
            List<string> events,
            params TimeSpan[] frameDurations)
        {
            _clock = clock;
            _events = events;
            _frameDurations = frameDurations;
        }

        public InputSnapshot Input { get; } = InputSnapshot.Empty;

        public OutputSize OutputSize { get; } = new(640, 480);

        public void Initialize()
        {
            _events.Add("platform.initialize");
        }

        public bool PumpEvents()
        {
            _events.Add("platform.pump");

            if (_nextFrame >= _frameDurations.Count)
            {
                return false;
            }

            _clock.Advance(_frameDurations[_nextFrame]);
            _nextFrame++;
            return true;
        }

        public void Shutdown()
        {
            _events.Add("platform.shutdown");
        }

        public void Present(RenderContext context)
        {
            _events.Add("platform.present");
        }
    }

    private sealed class RecordingGame : IGame
    {
        private readonly List<string> _events;

        public RecordingGame(List<string> events)
        {
            _events = events;
        }

        public Func<ApplicationState>? GetState { get; set; }

        public Exception? UpdateException { get; init; }

        public List<ApplicationState> ObservedStates { get; } = [];

        public List<GameTime> UpdateTimes { get; } = [];

        public List<GameTime> RenderTimes { get; } = [];

        public void Initialize(InitializeContext context)
        {
            _events.Add("game.initialize");
            ObserveState();
        }

        public void Update(UpdateContext context)
        {
            _events.Add("game.update");
            ObserveState();
            UpdateTimes.Add(context.Time);

            if (UpdateException is not null)
            {
                throw UpdateException;
            }
        }

        public void Render(RenderContext context)
        {
            _events.Add("game.render");
            ObserveState();
            RenderTimes.Add(context.Time);
        }

        public void Shutdown(ShutdownContext context)
        {
            _events.Add("game.shutdown");
            ObserveState();
        }

        private void ObserveState()
        {
            if (GetState is not null)
            {
                ObservedStates.Add(GetState());
            }
        }
    }

    private sealed class TestException : Exception;
}
