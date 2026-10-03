using Obelisk.Abstractions;
using Obelisk.Platform.Sdl3;
using Obelisk.Runtime;

namespace Obelisk.Vanilla;

/// <summary>
/// Provides the default Obelisk application setup.
/// </summary>
public static class ObeliskApp
{
    public static int Run(IGame game, AppOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        options ??= new AppOptions();

        var platform = new Sdl3ApplicationPlatform(options);
        var loopOptions = new GameLoopOptions
        {
            MaximumDeltaTime = options.MaximumDeltaTime,
        };
        var host = new ApplicationHost(platform, options: loopOptions);

        return host.Run(game);
    }
}
