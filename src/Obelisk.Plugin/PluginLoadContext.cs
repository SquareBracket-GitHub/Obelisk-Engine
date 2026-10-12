using System.Reflection;
using System.Runtime.Loader;
using Obelisk.Abstractions;
using Obelisk.Assets;
using Obelisk.Graphics2D;

namespace Obelisk.Plugin;

internal sealed class PluginLoadContext(PluginPaths paths) : AssemblyLoadContext(isCollectible: false)
{
    // Managed package files stay replaceable between startups on Windows. This does not unload assemblies.
    internal Assembly LoadPackageAssembly(string path)
    {
        using var file = File.OpenRead(paths.ValidateDependency(path));
        return LoadFromStream(file);
    }
    private readonly AssemblyDependencyResolver _resolver = new(paths.Assembly);
    private static readonly Dictionary<string, Assembly> Shared = new[]
    {
        typeof(IObeliskPlugin).Assembly, typeof(IGame).Assembly, typeof(AssetStore).Assembly, typeof(Canvas2D).Assembly,
    }.Distinct().ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Framework = Directory.EnumerateFiles(
        Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
        .Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is not null && Shared.TryGetValue(name.Name, out var shared))
        {
            if (!string.Equals(name.FullName, shared.FullName, StringComparison.OrdinalIgnoreCase))
                throw new FileLoadException($"Contract '{name}' does not match host '{shared.GetName()}'.");
            return shared;
        }
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path is not null) return LoadPackageAssembly(path);
        if (name.Name is not null)
        {
            var local = paths.Resolve(name.Name + ".dll");
            if (File.Exists(local)) return LoadPackageAssembly(local);
        }
        if (name.Name is not null && Framework.Contains(name.Name)) return null;
        throw new FileNotFoundException($"Private dependency '{name}' is missing from plugin package '{paths.Root}'.");
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        if (path is not null) return LoadUnmanagedDllFromPath(paths.ValidateDependency(path));
        var local = paths.ResolveNative(OperatingSystem.IsWindows() && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name + ".dll" : name);
        return File.Exists(local) ? LoadUnmanagedDllFromPath(local) : 0;
    }
}
