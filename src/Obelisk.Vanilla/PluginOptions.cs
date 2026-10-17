namespace Obelisk.Vanilla;

/// <summary>Configures local Plugin discovery and shared project assets before platform initialization.</summary>
public sealed class PluginOptions
{
    /// <summary>Gets the package discovery directory; defaults to plugins beside the application. A missing directory means no external Plugins.</summary>
    public string Directory { get; init; } = Path.Combine(AppContext.BaseDirectory, "plugins");
    /// <summary>Gets the project asset directory; defaults to assets beside the application.</summary>
    public string ProjectAssetRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "assets");
    /// <summary>Gets whether to register the bundled images Plugin; defaults to true. Disable it before adding another Plugin named images.</summary>
    public bool IncludeImages { get; init; } = true;
}
