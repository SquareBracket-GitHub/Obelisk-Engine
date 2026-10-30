using System.Numerics;

namespace Obelisk.Graphics2D.Tests;

public sealed class RenderingResourceTests
{
    [Fact]
    public void CommandsCaptureMaterialFilterAndConstantsWithoutLaterStateChanges()
    {
        using var material = new Material(7, () => { });
        var canvas = new Canvas2D { Material = material, Filter = TextureFilter.Linear, Parameters = Vector4.One };
        canvas.FillRectangle(new(0, 0, 10, 10), ColorRgba.White);
        canvas.Material = null; canvas.Filter = TextureFilter.Nearest; canvas.Parameters = Vector4.Zero;
        canvas.FillRectangle(new(0, 0, 5, 5), ColorRgba.White);
        Assert.Same(material, canvas.Commands[0].Material);
        Assert.Equal(TextureFilter.Linear, canvas.Commands[0].Filter);
        Assert.Equal(Vector4.One, canvas.Commands[0].Parameters);
        Assert.Null(canvas.Commands[1].Material);
        canvas.Filter = (TextureFilter)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillRectangle(new(0, 0, 1, 1), ColorRgba.White));
        canvas.Filter = TextureFilter.Nearest; canvas.Parameters = new(float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillRectangle(new(0, 0, 1, 1), ColorRgba.White));
        canvas.Parameters = default; canvas.Material = material; material.Dispose();
        Assert.Throws<ObjectDisposedException>(() => canvas.FillRectangle(new(0, 0, 1, 1), ColorRgba.White));
    }

    [Fact]
    public void OffscreenPassesSnapshotChildrenAndFailedCallbacksDoNotAppend()
    {
        using var target = new RenderTarget(new Texture(new(1), 8, 8, () => { }));
        var canvas = new Canvas2D(); Canvas2D? saved = null;
        canvas.RenderTo(target, child => { saved = child; child.FillCircle(new(4, 4), 2, ColorRgba.White); });
        saved!.FillCircle(new(4, 4), 1, ColorRgba.Orange);
        var pass = Assert.IsType<RenderPassCommand>(Assert.Single(canvas.Commands));
        Assert.Single(pass.Commands);
        Assert.Equal(0, pass.ClearColor.A);
        Assert.Throws<InvalidOperationException>(() => canvas.RenderTo(target, _ => throw new InvalidOperationException()));
        Assert.Throws<NotSupportedException>(() => canvas.RenderTo(target, child => child.RenderTo(target, _ => { })));
        Assert.Single(canvas.Commands);
        target.Dispose();
        Assert.Throws<ObjectDisposedException>(() => canvas.RenderTo(target, _ => { }));
    }

    [Fact]
    public void ResourceDisposalCanRetryAfterWrongThreadFailureAndOnlyReleasesOnce()
    {
        var attempts = 0;
        var material = new Material(1, () => { if (++attempts == 1) throw new InvalidOperationException("Wrong thread"); });
        Assert.Throws<InvalidOperationException>(() => material.Dispose());
        Assert.False(material.IsDisposed);
        material.Dispose(); material.Dispose();
        Assert.True(material.IsDisposed); Assert.Equal(2, attempts);
    }
}
