using MovingRectangle;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;

namespace MovingRectangle.Tests;

public sealed class MovingRectangleGameTests
{
    [Fact]
    public void Update_MovesRightUsingDeltaTime()
    {
        var game = new MovingRectangleGame();
        var initial = game.Bounds;
        var input = CreateInput(Key.Right);
        var time = new GameTime(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5), 0);

        game.Update(new UpdateContext(time, input, new OutputSize(1280, 720)));

        Assert.Equal(initial.X + (MovingRectangleGame.MovementSpeed * 0.5f), game.Bounds.X);
        Assert.Equal(initial.Y, game.Bounds.Y);
    }

    [Fact]
    public void Update_NormalizesDiagonalMovement()
    {
        var game = new MovingRectangleGame();
        var initial = game.Bounds;
        var input = CreateInput(Key.Right, Key.Down);
        var time = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 0);

        game.Update(new UpdateContext(time, input, new OutputSize(2000, 1200)));

        var distance = MathF.Sqrt(
            MathF.Pow(game.Bounds.X - initial.X, 2) +
            MathF.Pow(game.Bounds.Y - initial.Y, 2));
        Assert.Equal(MovingRectangleGame.MovementSpeed, distance, precision: 3);
    }

    [Fact]
    public void Update_ClampsRectangleInsideResizedOutput()
    {
        var game = new MovingRectangleGame();

        game.Update(new UpdateContext(
            new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 0),
            CreateInput(Key.Right, Key.Down),
            new OutputSize(600, 400)));

        Assert.Equal(600 - MovingRectangleGame.RectangleWidth, game.Bounds.X);
        Assert.Equal(400 - MovingRectangleGame.RectangleHeight, game.Bounds.Y);
    }

    [Fact]
    public void Render_QueuesTheCurrentRectangle()
    {
        var game = new MovingRectangleGame();
        var context = new RenderContext(default, new OutputSize(1280, 720));

        game.Render(context);

        var command = Assert.IsType<RectangleCommand>(Assert.Single(context.Canvas.Commands));
        Assert.Equal(game.Bounds, command.Bounds);
        Assert.Equal(ColorRgba.Orange, command.Color);
    }

    private static InputSnapshot CreateInput(params Key[] keys)
    {
        return new InputSnapshot(
            new KeyboardSnapshot(keys, [], []),
            new MouseSnapshot(0, 0, [], [], []));
    }
}
