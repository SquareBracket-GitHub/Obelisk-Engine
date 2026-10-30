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
    /// <summary>Gets whether to register bundled text asset loaders; defaults to true. Disable before loading another plugin named text.</summary>
    public bool IncludeText { get; init; } = true;
    /// <summary>Gets whether to register optional effects setting loaders; defaults to false. GPU effects always require explicit creation/use.</summary>
    public bool IncludeEffects { get; init; }
    /// <summary>Gets SDK assemblies explicitly shared with dynamic packages; defaults to Text and Effects public APIs. Set before discovery; versions must match.</summary>
    public IReadOnlyCollection<System.Reflection.Assembly> SharedPluginContracts { get; init; } =
        [typeof(Obelisk.Plugins.Text.TextPlugin).Assembly, typeof(Obelisk.Plugins.Effects.EffectsPlugin).Assembly];
}
