using Obelisk.Abstractions;
using Obelisk.Vanilla;

namespace MovingRectangle;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        return ObeliskApp.Run(
            new MovingRectangleGame(),
            new AppOptions
            {
                Title = "Obelisk — Moving Rectangle",
                Width = 1280,
                Height = 720,
                Resizable = true,
            });
    }
}

internal sealed class MovingRectangleGame : IGame
{
    public void Initialize(InitializeContext context)
    {
    }

    public void Update(UpdateContext context)
    {
    }

    public void Render(RenderContext context)
    {
    }

    public void Shutdown(ShutdownContext context)
    {
    }
}
