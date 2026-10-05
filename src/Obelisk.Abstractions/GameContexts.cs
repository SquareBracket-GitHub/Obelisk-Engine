namespace Obelisk.Abstractions;

public sealed class InitializeContext;

public sealed class UpdateContext
{
    public UpdateContext(GameTime time, InputSnapshot? input = null)
    {
        Time = time;
        Input = input ?? InputSnapshot.Empty;
    }

    public GameTime Time { get; }

    public InputSnapshot Input { get; }
}

public sealed class RenderContext
{
    public RenderContext(GameTime time)
    {
        Time = time;
    }

    public GameTime Time { get; }
}

public sealed class ShutdownContext;
