using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Vanilla;

namespace MovingRectangle;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
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
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}

internal sealed class MovingRectangleGame : IGame
{
    internal const float RectangleWidth = 180;
    internal const float RectangleHeight = 110;
    internal const float MovementSpeed = 320;

    private float _x = 550;
    private float _y = 305;

    internal RectangleF Bounds => new(_x, _y, RectangleWidth, RectangleHeight);

    public void Initialize(InitializeContext context)
    {
    }

    public void Update(UpdateContext context)
    {
        var horizontal = 0f;
        var vertical = 0f;

        if (context.Input.Keyboard.IsDown(Key.Left)) horizontal--;
        if (context.Input.Keyboard.IsDown(Key.Right)) horizontal++;
        if (context.Input.Keyboard.IsDown(Key.Up)) vertical--;
        if (context.Input.Keyboard.IsDown(Key.Down)) vertical++;

        if (horizontal != 0 && vertical != 0)
        {
            const float inverseSquareRootOfTwo = 0.70710677f;
            horizontal *= inverseSquareRootOfTwo;
            vertical *= inverseSquareRootOfTwo;
        }

        _x += horizontal * MovementSpeed * context.Time.Delta;
        _y += vertical * MovementSpeed * context.Time.Delta;

        if (context.OutputSize.Width > 0 && context.OutputSize.Height > 0)
        {
            _x = Math.Clamp(_x, 0, Math.Max(0, context.OutputSize.Width - RectangleWidth));
            _y = Math.Clamp(_y, 0, Math.Max(0, context.OutputSize.Height - RectangleHeight));
        }
    }

    public void Render(RenderContext context)
    {
        context.Canvas.FillRectangle(Bounds, ColorRgba.Orange);
    }

    public void Shutdown(ShutdownContext context)
    {
    }
}
