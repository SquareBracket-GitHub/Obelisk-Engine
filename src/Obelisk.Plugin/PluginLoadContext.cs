using System.Reflection;
using System.Runtime.Loader;
using Obelisk.Abstractions;
using Obelisk.Assets;
using Obelisk.Graphics2D;

namespace Obelisk.Plugin;

internal sealed class PluginLoadContext(PluginPaths paths, IReadOnlyDictionary<string, Assembly> additionalShared) : AssemblyLoadContext(isCollectible: false)
{
    // Managed package files stay replaceable between startups on Windows. This does not unload assemblies.
    internal Assembly LoadPackageAssembly(string path)
    {
        path = paths.ValidateDependency(path);
        var name = AssemblyName.GetAssemblyName(path);
        if (name.Name is not null && additionalShared.TryGetValue(name.Name, out var sdk)) return Match(sdk, name);
        using var file = File.OpenRead(path);
        return LoadFromStream(file);
    }
    private readonly AssemblyDependencyResolver _resolver = new(paths.Assembly);
    private static readonly Dictionary<string, Assembly> Shared = new[]
    {
        typeof(IObeliskPlugin).Assembly, typeof(IGame).Assembly, typeof(AssetStore).Assembly, typeof(Canvas2D).Assembly,
    }.Distinct().ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Framework = Directory.EnumerateFiles(
        Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
        .Select(path => Path.GetFileNameWithoutExtension(path))
        // In self-contained games this directory also contains application/plugin DLLs.
        .Where(name => name is "mscorlib" or "netstandard" or "WindowsBase" or "System" or "Microsoft.CSharp" or "Microsoft.VisualBasic" or "Microsoft.VisualBasic.Core" ||
            name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static void ValidateContract(Assembly assembly)
    {
        if (Framework.Contains(assembly.GetName().Name!)) throw new ArgumentException("Framework assemblies are already shared by the runtime.", nameof(assembly));
        if (Shared.TryGetValue(assembly.GetName().Name!, out var engine) && !ReferenceEquals(engine, assembly))
            throw new ArgumentException("Engine contract assemblies cannot be replaced.", nameof(assembly));
    }
    private static Assembly Match(Assembly shared, AssemblyName name)
    {
        if (!string.Equals(name.FullName, shared.FullName, StringComparison.OrdinalIgnoreCase))
            throw new FileLoadException($"Contract '{name}' does not match host SDK '{shared.GetName()}'. Update SDK and plugin package together.");
        return shared;
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is not null && Shared.TryGetValue(name.Name, out var shared))
        {
            return Match(shared, name);
        }
        if (name.Name is not null && additionalShared.TryGetValue(name.Name, out var sdk)) return Match(sdk, name);
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
