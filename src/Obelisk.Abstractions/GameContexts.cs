using Obelisk.Graphics2D;

namespace Obelisk.Abstractions;

public sealed class InitializeContext(ITextureDevice? textures = null)
{
    public ITextureDevice Textures => textures ?? throw new NotSupportedException("This platform does not provide textures.");
}

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
    public RenderContext(GameTime time, OutputSize outputSize = default)
    {
        Time = time;
        OutputSize = outputSize;
        Canvas = new Canvas2D();
    }

    public GameTime Time { get; }

    public OutputSize OutputSize { get; }

    public Canvas2D Canvas { get; }
}

public sealed class ShutdownContext;
