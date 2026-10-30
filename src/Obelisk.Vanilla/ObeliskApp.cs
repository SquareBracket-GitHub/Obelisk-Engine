using Obelisk.Abstractions;
using Obelisk.Platform.Sdl3;
using Obelisk.Runtime;
using Obelisk.Plugin;
using Obelisk.Plugins.Images;
using Obelisk.Plugins.Text;
using Obelisk.Plugins.Effects;

namespace Obelisk.Vanilla;

/// <summary>
/// Provides the default Obelisk application setup.
/// </summary>
public static class ObeliskApp
{
    /// <summary>Runs one game synchronously on the main STA thread with bundled images and discovered local Plugins; returns zero on normal exit and propagates exceptions after cleanup.</summary>
    public static int Run(IGame game, AppOptions? options = null, PluginOptions? plugins = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        options ??= new AppOptions();
        plugins ??= new PluginOptions();
        using var pluginHost = new PluginHost(plugins.ProjectAssetRoot);
        ArgumentNullException.ThrowIfNull(plugins.SharedPluginContracts);
        foreach (var contract in plugins.SharedPluginContracts) pluginHost.ShareContract(contract);
        if (plugins.IncludeImages)
            pluginHost.Add(new PluginManifest
            {
                Name = "images",
                Version = typeof(ImagePlugin).Assembly.GetName().Version!.ToString(3),
                EngineMinimum = "0.2.0",
                EngineMaximumExclusive = "0.3.0",
                Assembly = "Obelisk.Plugins.Images.dll",
                EntryPoint = typeof(ImagePlugin).FullName!,
            }, new ImagePlugin(), AppContext.BaseDirectory);
        if (plugins.IncludeText) AddBundled("text", new TextPlugin());
        if (plugins.IncludeEffects) AddBundled("effects", new EffectsPlugin());
        pluginHost.LoadDirectory(plugins.Directory);

        var platform = new Sdl3ApplicationPlatform(options);
        var loopOptions = new GameLoopOptions
        {
            MaximumDeltaTime = options.MaximumDeltaTime,
        };
        var host = new ApplicationHost(platform, options: loopOptions);

        return host.Run(pluginHost.Wrap(game));

        void AddBundled(string name, IObeliskPlugin plugin)
        {
            var type = plugin.GetType();
            pluginHost.Add(new PluginManifest
            {
                Name = name,
                Version = type.Assembly.GetName().Version!.ToString(3),
                EngineMinimum = "0.2.0",
                EngineMaximumExclusive = "0.3.0",
                Assembly = type.Assembly.GetName().Name + ".dll",
                EntryPoint = type.FullName!
            }, plugin, AppContext.BaseDirectory);
        }
    }
}
