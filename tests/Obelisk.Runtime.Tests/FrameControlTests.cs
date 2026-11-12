using Obelisk.Abstractions;

namespace Obelisk.Runtime.Tests;

public sealed class FrameControlTests
{
    [Fact]
    public void PausedPumpsDoNotUpdateRenderOrAdvanceSimulationAndCompletionFollowsPresent()
    {
        var platform = new Platform(); var game = new Game(platform);
        Assert.Equal(0, new ApplicationHost(platform).Run(game));
        Assert.Equal(2, game.Times.Count);
        Assert.Equal(new ulong[] { 0, 1 }, game.Times.Select(t => t.FrameNumber));
        Assert.All(game.Times, t => Assert.Equal(TimeSpan.FromMilliseconds(10), t.DeltaTime));
        Assert.Equal(TimeSpan.FromMilliseconds(20), game.Times[^1].Elapsed);
        Assert.Equal(2, platform.Completed);
        Assert.True(platform.Stopped);
    }
    private sealed class Platform : IApplicationPlatform, IFrameControl
    {
        private int _pumps; private bool _presented;
        public int Completed { get; private set; }
        public bool Stopped { get; private set; }
        public bool ShouldRunFrame => _pumps is 2 or 5;
        public TimeSpan? FrameDelta => TimeSpan.FromMilliseconds(10);
        public InputSnapshot Input => InputSnapshot.Empty;
        public OutputSize OutputSize => new(320, 240);
        public void Initialize() { }
        public bool PumpEvents() => ++_pumps < 6;
        public void Present(RenderContext context) => _presented = true;
        public void FrameCompleted(GameTime time) { Assert.True(_presented); _presented = false; Completed++; }
        public void Shutdown() => Stopped = true;
    }
    private sealed class Game(Platform platform) : IGame
    {
        public List<GameTime> Times { get; } = [];
        public void Initialize(InitializeContext context) { }
        public void Update(UpdateContext context) { Assert.True(platform.ShouldRunFrame); Times.Add(context.Time); }
        public void Render(RenderContext context) => Assert.Equal(Times[^1], context.Time);
        public void Shutdown(ShutdownContext context) { }
    }
}
