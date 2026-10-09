namespace Obelisk.Assets;

/// <summary>Loads typed assets from confined roots and owns cached disposable results.</summary>
public sealed class AssetStore : IDisposable
{
    private readonly string _projectRoot;
    private readonly Dictionary<string, string> _pluginRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, Func<string, object>> _loaders = [];
    private readonly Dictionary<(AssetUri Uri, Type Type), object> _cache = [];
    private bool _disposed;

    public AssetStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public void RegisterPlugin(string name, string root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            throw new ArgumentException("Invalid plugin name.", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!_pluginRoots.TryAdd(name, Path.GetFullPath(root)))
            throw new InvalidOperationException($"Plugin asset root '{name}' is already registered.");
    }

    public void RegisterLoader<T>(Func<string, T> loader) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(loader);
        if (!_loaders.TryAdd(typeof(T), path => loader(path)))
            throw new InvalidOperationException($"Asset loader for '{typeof(T).Name}' is already registered.");
    }

    public T Load<T>(string uri) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = AssetUri.Parse(uri);
        if (_cache.TryGetValue((key, typeof(T)), out var cached)) return (T)cached;
        if (!_loaders.TryGetValue(typeof(T), out var loader))
            throw new InvalidOperationException($"No asset loader for '{typeof(T).Name}' while loading '{key}'.");
        var path = Resolve(key);
        if (!File.Exists(path)) throw new FileNotFoundException($"Asset '{key}' was not found at '{path}'.", path);
        try
        {
            var value = loader(path) ?? throw new InvalidDataException("Loader returned null.");
            _cache.Add((key, typeof(T)), value);
            return (T)value;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Could not load asset '{key}' at '{path}': {error.Message}", error);
        }
    }

    public string Resolve(AssetUri uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var root = uri.Scheme switch
        {
            "project" => _projectRoot,
            "plugin" when uri.Plugin is not null && _pluginRoots.TryGetValue(uri.Plugin, out var pluginRoot) => pluginRoot,
            _ => throw new InvalidOperationException($"Asset root for '{uri}' is not registered."),
        };
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, uri.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Asset '{uri}' escapes its root '{root}'.");
        var cursor = root;
        foreach (var segment in uri.Path.Split('/'))
        {
            cursor = Path.Combine(cursor, segment);
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Asset '{uri}' traverses a symbolic link at '{cursor}'.");
        }
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var value in _cache.Values.Distinct(ReferenceEqualityComparer.Instance))
            if (value is IDisposable disposable) disposable.Dispose();
        _cache.Clear();
    }
}
