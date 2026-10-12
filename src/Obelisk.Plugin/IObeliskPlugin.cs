using Obelisk.Abstractions;
using Obelisk.Assets;

namespace Obelisk.Plugin;

/// <summary>A trusted package's entry point. Registration occurs once before platform initialization.</summary>
public interface IObeliskPlugin : IDisposable
{
    void Register(PluginRegistrationContext context);
    void IDisposable.Dispose() { }
}

public sealed class PluginRegistrationContext
{
    private readonly AssetStore _assets;
    private bool _closed;
    private readonly List<IObeliskModule> _modules = [];
    internal PluginRegistrationContext(PluginManifest manifest, PluginPaths paths, AssetStore assets)
    { Manifest = manifest; Paths = paths; _assets = assets; }
    public PluginManifest Manifest { get; }
    public PluginPaths Paths { get; }
    public IAssetSource Assets => _assets;
    internal IReadOnlyList<IObeliskModule> Modules => _modules;
    internal void Close() => _closed = true;

    public void RegisterModule(IObeliskModule module)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(module);
        if (_modules.Any(m => ReferenceEquals(m, module))) throw new InvalidOperationException("Module instance is already registered.");
        _modules.Add(module);
    }
    public void RegisterAssetLoader<T>(Func<string, T> loader) where T : class
    { EnsureOpen(); _assets.RegisterPluginLoader(Manifest.Name, loader); }
    /// <summary>Registers a shared default loader, e.g. the bundled image decoder for project URIs.</summary>
    public void RegisterDefaultAssetLoader<T>(Func<string, T> loader) where T : class
    { EnsureOpen(); _assets.RegisterLoader(loader); }
    private void EnsureOpen()
    { if (_closed) throw new InvalidOperationException($"Plugin '{Manifest.Name}' registration is closed."); }
}
