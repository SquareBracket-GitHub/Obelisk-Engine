using System.Numerics;
using System.Text;
using Obelisk.Graphics2D;

namespace Obelisk.Graphics2D.Tests;

public sealed class FontLifetimeTests
{
    [Fact]
    public void FontOwnsAtlasAndOwnerShutdownInvalidatesFont()
    {
        var releases = 0;
        var atlas = new Texture(new(9), 8, 8, () => releases++);
        var font = new Font(atlas, 10, new Dictionary<Rune, FontGlyph> { [new('A')] = new(new(0, 0, 8, 8), 5) });
        atlas.Dispose(); // Device shutdown may release the atlas before the game disposes its Font.
        Assert.True(font.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => font.Measure("A"));
        Assert.Throws<ObjectDisposedException>(() => new Canvas2D().DrawText(font, "A", Vector2.Zero));
        font.Dispose(); font.Dispose();
        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => new Font(atlas, 10, new Dictionary<Rune, FontGlyph>()));
    }

    [Fact]
    public void ScaledTextMeasuresMultilineAndRecordsOneCommand()
    {
        var releases = 0;
        using var font = Font(() => releases++);
        Assert.Equal(new Vector2(10, 40), font.Measure("A\nA", 2));
        var canvas = new Canvas2D();
        canvas.DrawText(font, "A\nA", new(4, 6), scale: 2);
        Assert.Equal(2, Assert.IsType<TextCommand>(Assert.Single(canvas.Commands)).Scale);
        font.Dispose(); font.Dispose();
        Assert.Equal(1, releases);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    public void InvalidScaleIsRejectedBeforeRecordingCommands(float scale)
    {
        using var font = Font(() => { });
        var canvas = new Canvas2D();
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawText(font, "A", Vector2.Zero, scale: scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Measure("A", scale));
        Assert.Empty(canvas.Commands);
    }

    private static Font Font(Action release) => new(new Texture(new(1), 8, 8, release), 10,
        new Dictionary<Rune, FontGlyph> { [new('A')] = new(new(0, 0, 8, 8), 5) });
}
