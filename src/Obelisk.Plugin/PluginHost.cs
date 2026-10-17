using Obelisk.Abstractions;
using Obelisk.Assets;

namespace Obelisk.Plugin;

/// <summary>Owns local plugin registrations and their process-lifetime assembly contexts.</summary>
/// <param name="projectAssetRoot">Project asset directory used by the shared cache.</param>
public sealed class PluginHost(string projectAssetRoot) : IDisposable
{
    private readonly AssetStore _assets = new(projectAssetRoot);
    private readonly List<Registration> _registrations = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _wrapped;
    private bool _disposed;

    /// <summary>Gets borrowed cached assets shared by the game and registered Plugins.</summary>
    public IAssetSource Assets => _assets;
    /// <summary>Gets a snapshot of currently registered validated manifests.</summary>
    public IReadOnlyList<PluginManifest> Plugins => _registrations.Select(r => r.Manifest).ToArray();

    /// <summary>Takes ownership of a Plugin instance and registers it under a unique compatible manifest and root.</summary>
    public void Add(PluginManifest manifest, IObeliskPlugin plugin, string root)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(plugin);
        try
        {
            Preflight(manifest);
            Register(manifest, plugin, new PluginPaths(root, manifest));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Fail(manifest.Name, "registration", error);
        }
    }

    /// <summary>Loads a trusted local package, preserving engine contract assembly identity; errors identify the Plugin and operation.</summary>
    public void Load(string packageDirectory)
    {
        EnsureMutable();
        LoadDirectoryCore([packageDirectory]);
    }

    /// <summary>An absent discovery directory is an empty installation; each child needs a manifest.</summary>
    public void LoadDirectory(string directory)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            if (File.Exists(directory)) throw new InvalidDataException($"Plugin discovery path '{directory}' must be a directory.");
            if (!Directory.Exists(directory)) return;
            PluginPaths.RejectLinks(Path.GetFullPath(directory));
            LoadDirectoryCore(Directory.GetDirectories(directory).Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Fail("discovery", "directory discovery", error); }
    }

    /// <summary>Wraps one game with ordered module callbacks, borrowed Assets and reverse-order cleanup.</summary>
    public IGame Wrap(IGame game)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(game);
        _wrapped = true;
        return new HostedGame(this, game);
    }

    private void LoadDirectoryCore(string[] directories)
    {
        var name = "discovery";
        try
        {
            // Validate the entire discovered set before constructing any external plugin.
            var packages = new List<(PluginManifest Manifest, PluginPaths Paths)>();
            var names = _registrations.Select(r => r.Manifest.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
                var manifest = PluginManifest.Read(Path.Combine(directory, PluginManifest.FileName));
                name = manifest.Name;
                if (!names.Add(name)) throw new InvalidDataException($"Duplicate plugin '{name}'.");
                var paths = new PluginPaths(directory, manifest);
                if (!File.Exists(paths.Assembly)) throw new FileNotFoundException("Plugin assembly is missing.", paths.Assembly);
                packages.Add((manifest, paths));
            }
            foreach (var (manifest, paths) in packages)
            {
                name = manifest.Name;
                var context = new PluginLoadContext(paths);
                var assembly = context.LoadPackageAssembly(paths.Assembly);
                var type = assembly.GetType(manifest.EntryPoint, throwOnError: true)!;
                if (!type.IsPublic || type.IsAbstract || !typeof(IObeliskPlugin).IsAssignableFrom(type) || type.ContainsGenericParameters)
                    throw new InvalidDataException($"Entry '{manifest.EntryPoint}' must be a public concrete IObeliskPlugin.");
                var plugin = (IObeliskPlugin)(Activator.CreateInstance(type)
                    ?? throw new InvalidDataException("Plugin construction returned null."));
                Register(manifest, plugin, paths);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Fail(name, "local loading", error); }
    }

    private void Preflight(PluginManifest manifest)
    {
        manifest.Validate(PluginManifest.CurrentEngineVersion);
        if (_registrations.Any(r => string.Equals(r.Manifest.Name, manifest.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Duplicate plugin '{manifest.Name}'.");
    }

    private void Register(PluginManifest manifest, IObeliskPlugin plugin, PluginPaths paths)
    {
        var context = new PluginRegistrationContext(manifest, paths, _assets);
        _registrations.Add(new Registration(manifest, plugin, context)); // own it even if Register throws
        try
        {
            _assets.RegisterPlugin(manifest.Name, paths.Assets);
            plugin.Register(context);
            var allModules = _registrations.SelectMany(r => r.Context.Modules).ToArray();
            if (allModules.Distinct(ReferenceEqualityComparer.Instance).Count() != allModules.Length)
                throw new InvalidOperationException("Module instance is already registered by another plugin.");
        }
        finally { context.Close(); }
    }

    private void Fail(string name, string operation, Exception error)
    {
        while (error is System.Reflection.TargetInvocationException { InnerException: not null } invocation)
            error = invocation.InnerException!;
        try { Dispose(); }
        catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
        throw error is PluginException pluginError ? pluginError : new PluginException(name, operation, error);
    }

    private void EnsureThread()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Plugin lifecycle must run on its owning thread.");
    }

    private void EnsureMutable()
    {
        EnsureThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_wrapped) throw new InvalidOperationException("Plugin registrations are frozen after wrapping the game.");
    }

    /// <summary>Disposes cached assets then Plugin instances in reverse order, continuing cleanup and aggregating failures.</summary>
    public void Dispose()
    {
        EnsureThread();
        if (_disposed) return;
        _disposed = true;
        var errors = new List<Exception>();
        try { _assets.Dispose(); } catch (Exception error) { errors.Add(error); }
        foreach (var registration in _registrations.AsEnumerable().Reverse())
            try { registration.Plugin.Dispose(); }
            catch (Exception error) { errors.Add(new PluginException(registration.Manifest.Name, "dispose", error)); }
        if (errors.Count != 0) throw new AggregateException("Plugin cleanup failed.", errors);
    }

    private sealed record Registration(PluginManifest Manifest, IObeliskPlugin Plugin, PluginRegistrationContext Context);

    private sealed class HostedGame(PluginHost host, IGame game) : IGame
    {
        private readonly List<(string Name, IObeliskModule Module)> _initialized = [];
        private bool _gameInitialized;
        private bool _started;
        private bool _stopped;

        public void Initialize(InitializeContext context)
        {
            host.EnsureThread();
            ObjectDisposedException.ThrowIf(host._disposed, host);
            if (_started) throw new InvalidOperationException("A plugin game can only initialize once.");
            _started = true;
            try
            {
                var enriched = context.WithAssets(host.Assets);
                foreach (var registration in host._registrations)
                    foreach (var module in registration.Context.Modules)
                    {
                        // Include partially initialized modules in cleanup.
                        _initialized.Add((registration.Manifest.Name, module));
                        Invoke(registration.Manifest.Name, "module initialize", () => module.Initialize(enriched));
                    }
                game.Initialize(enriched);
                _gameInitialized = true;
            }
            catch (Exception error)
            {
                try { Shutdown(new ShutdownContext()); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }

        public void Update(UpdateContext context)
        {
            EnsureRunning();
            foreach (var (name, module) in _initialized) Invoke(name, "module update", () => module.Update(context));
            game.Update(context);
        }

        public void Render(RenderContext context)
        {
            EnsureRunning();
            game.Render(context);
            foreach (var (name, module) in _initialized)
            {
                var blend = context.Canvas.Blend;
                try { Invoke(name, "module render", () => module.Render(context)); }
                finally { context.Canvas.Blend = blend; }
            }
        }

        public void Shutdown(ShutdownContext context)
        {
            host.EnsureThread();
            if (_stopped) return;
            _stopped = true;
            var errors = new List<Exception>();
            if (_gameInitialized)
                try { game.Shutdown(context); } catch (Exception error) { errors.Add(error); }
            foreach (var (name, module) in _initialized.AsEnumerable().Reverse())
                try { Invoke(name, "module shutdown", () => module.Shutdown(context)); } catch (Exception error) { errors.Add(error); }
            try { host.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Plugin game shutdown failed.", errors);
        }

        private void EnsureRunning()
        {
            host.EnsureThread();
            ObjectDisposedException.ThrowIf(host._disposed, host);
            if (!_gameInitialized || _stopped) throw new InvalidOperationException("Plugin game is not running.");
        }

        private static void Invoke(string name, string operation, Action action)
        {
            try { action(); }
            catch (Exception error) when (error is not OutOfMemoryException) { throw new PluginException(name, operation, error); }
        }
    }
}
