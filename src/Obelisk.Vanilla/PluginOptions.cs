namespace Obelisk.Vanilla;

public sealed class PluginOptions
{
    public string Directory { get; init; } = Path.Combine(AppContext.BaseDirectory, "plugins");
    public string ProjectAssetRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "assets");
    public bool IncludeImages { get; init; } = true;
}
