using System.Numerics;
using Obelisk.Graphics2D;

namespace Obelisk.Graphics2D.Tests;

public sealed class Canvas2DTests
{
    [Fact]
    public void DrawingCommands_AreCollectedInSubmissionOrder()
    {
        var canvas = new Canvas2D();
        var rectangle = new RectangleF(10, 20, 30, 40);
        var lineStart = new Vector2(5, 6);
        var lineEnd = new Vector2(70, 80);
        var center = new Vector2(100, 120);
        var destination = new RectangleF(200, 220, 64, 32);

        canvas.FillRectangle(rectangle, ColorRgba.Orange);
        canvas.DrawLine(lineStart, lineEnd, 3, ColorRgba.White);
        canvas.FillCircle(center, 25, ColorRgba.Orange);
        canvas.DrawSprite(new TextureHandle(7), destination);

        Assert.Collection(
            canvas.Commands,
            command => Assert.Equal(
                new RectangleCommand(rectangle, ColorRgba.Orange),
                Assert.IsType<RectangleCommand>(command)),
            command => Assert.Equal(
                new LineCommand(lineStart, lineEnd, 3, ColorRgba.White),
                Assert.IsType<LineCommand>(command)),
            command => Assert.Equal(
                new CircleCommand(center, 25, ColorRgba.Orange),
                Assert.IsType<CircleCommand>(command)),
            command => Assert.Equal(
                new SpriteCommand(
                    new TextureHandle(7),
                    destination,
                    null,
                    ColorRgba.White,
                    0,
                    Vector2.Zero),
                Assert.IsType<SpriteCommand>(command)));
    }

    [Fact]
    public void InvalidGeometry_IsRejectedBeforeItReachesABackend()
    {
        var canvas = new Canvas2D();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            canvas.FillRectangle(new RectangleF(0, 0, 0, 10), ColorRgba.White));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            canvas.DrawLine(Vector2.Zero, Vector2.One, 0, ColorRgba.White));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            canvas.FillCircle(Vector2.Zero, float.NaN, ColorRgba.White));
        Assert.Throws<ArgumentException>(() =>
            canvas.DrawSprite(default, new RectangleF(0, 0, 10, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            canvas.FillRectangle(
                new RectangleF(0, 0, 10, 10),
                new ColorRgba(float.PositiveInfinity, 0, 0)));

        Assert.Empty(canvas.Commands);
    }

    [Fact]
    public void GraphicsContract_DoesNotReferenceAPlatformBackend()
    {
        var references = typeof(Canvas2D).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is not null &&
            (reference.Name.Contains("SDL", StringComparison.OrdinalIgnoreCase) ||
             reference.Name.Contains("Platform", StringComparison.OrdinalIgnoreCase)));
    }
}
