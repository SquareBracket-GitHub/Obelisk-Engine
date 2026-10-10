using Obelisk.Abstractions;
using Obelisk.Vanilla;

namespace Pong;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using var game = new PongGame();
            return ObeliskApp.Run(game, new AppOptions { Title = "Obelisk Pong", Width = 960, Height = 540 });
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
