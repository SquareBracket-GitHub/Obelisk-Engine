using Obelisk.Abstractions;
using Obelisk.Assets;

namespace Obelisk.Plugin;

/// <summary>A trusted package's entry point. Registration occurs once before platform initialization.</summary>
public interface IObeliskPlugin : IDisposable
{
    /// <summary>Registers modules and typed asset loaders once, before platform initialization; the registration context closes when this returns.</summary>
    void Register(PluginRegistrationContext context);
    void IDisposable.Dispose() { }
}

/// <summary>Provides registration-time access scoped to the current Plugin; registration methods throw after Register returns.</summary>
public sealed class PluginRegistrationContext
{
    private readonly AssetStore _assets;
    private bool _closed;
    private readonly List<IObeliskModule> _modules = [];
    internal PluginRegistrationContext(PluginManifest manifest, PluginPaths paths, AssetStore assets)
    { Manifest = manifest; Paths = paths; _assets = assets; }
    /// <summary>Gets the validated manifest for this Plugin.</summary>
    public PluginManifest Manifest { get; }
    /// <summary>Gets confined paths for this package's files.</summary>
    public PluginPaths Paths { get; }
    /// <summary>Gets borrowed shared cached assets; acquire GPU-dependent assets later during module initialization.</summary>
    public IAssetSource Assets => _assets;
    internal IReadOnlyList<IObeliskModule> Modules => _modules;
    internal void Close() => _closed = true;

    /// <summary>Registers a unique module instance in execution order before registration closes.</summary>
    public void RegisterModule(IObeliskModule module)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(module);
        if (_modules.Any(m => ReferenceEquals(m, module))) throw new InvalidOperationException("Module instance is already registered.");
        _modules.Add(module);
    }
    /// <summary>Registers one loader for this Plugin's URI authority and type; the host owns cached results.</summary>
    public void RegisterAssetLoader<T>(Func<string, T> loader) where T : class
    { EnsureOpen(); _assets.RegisterPluginLoader(Manifest.Name, loader); }
    /// <summary>Registers a shared default loader, e.g. the bundled image decoder for project URIs.</summary>
    public void RegisterDefaultAssetLoader<T>(Func<string, T> loader) where T : class
    { EnsureOpen(); _assets.RegisterLoader(loader); }
    private void EnsureOpen()
    { if (_closed) throw new InvalidOperationException($"Plugin '{Manifest.Name}' registration is closed."); }
}
