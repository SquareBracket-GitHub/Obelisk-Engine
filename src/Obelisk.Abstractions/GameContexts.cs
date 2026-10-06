namespace Obelisk.Abstractions;

public sealed class InitializeContext;

public sealed class UpdateContext
{
    public UpdateContext(
        GameTime time,
        InputSnapshot? input = null,
        OutputSize outputSize = default)
    {
        Time = time;
        Input = input ?? InputSnapshot.Empty;
        OutputSize = outputSize;
    }

    public GameTime Time { get; }

    public InputSnapshot Input { get; }

    public OutputSize OutputSize { get; }
}

public sealed class RenderContext
{
    private readonly List<SolidRectangle> _solidRectangles = [];

    public RenderContext(GameTime time, OutputSize outputSize = default)
    {
        Time = time;
        OutputSize = outputSize;
    }

    public GameTime Time { get; }

    public OutputSize OutputSize { get; }

    public IReadOnlyList<SolidRectangle> SolidRectangles => _solidRectangles;

    public void FillRectangle(RectangleF bounds, ColorRgba color)
    {
        if (bounds.IsVisible)
        {
            _solidRectangles.Add(new SolidRectangle(bounds, color));
        }
    }
}

public sealed class ShutdownContext;
