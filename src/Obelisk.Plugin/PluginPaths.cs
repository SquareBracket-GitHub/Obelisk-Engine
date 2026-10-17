namespace Obelisk.Plugin;

/// <summary>Paths rooted in one package. Reparse points and root escape are rejected.</summary>
public sealed class PluginPaths
{
    /// <summary>Resolves manifest directories under the package root, rejecting traversal and reparse points.</summary>
    public PluginPaths(string root, PluginManifest manifest)
    {
        Root = Path.GetFullPath(root);
        RejectLinks(Root);
        Assembly = Resolve(manifest.Assembly);
        Assets = Resolve(manifest.Assets);
        Shaders = Resolve(manifest.Shaders);
        Native = Resolve(manifest.Native);
    }
    /// <summary>Gets the absolute package root.</summary>
    public string Root { get; }
    /// <summary>Gets the confined absolute entry DLL path.</summary>
    public string Assembly { get; }
    /// <summary>Gets the confined absolute asset directory.</summary>
    public string Assets { get; }
    /// <summary>Gets the confined absolute shader directory.</summary>
    public string Shaders { get; }
    /// <summary>Gets the confined absolute native library directory.</summary>
    public string Native { get; }
    /// <summary>Resolves a relative file below the package's asset directory.</summary>
    public string ResolveAsset(string relative) => ResolveUnder(Assets, relative);
    /// <summary>Resolves a relative file below the package's shader directory.</summary>
    public string ResolveShader(string relative) => ResolveUnder(Shaders, relative);
    /// <summary>Resolves a relative file below the package's native directory.</summary>
    public string ResolveNative(string relative) => ResolveUnder(Native, relative);
    internal string Resolve(string relative) => ResolveUnder(Root, relative);

    internal string ValidateDependency(string path)
    {
        var relative = Path.GetRelativePath(Root, path);
        return Resolve(relative);
    }

    private static string ResolveUnder(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        var segments = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || segments.Any(s => s.Length == 0 || s is "." or ".." || s.IndexOfAny([':', '\0']) >= 0))
            throw new InvalidDataException($"Plugin path '{relative}' must remain inside '{root}'.");
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin path '{relative}' escapes '{root}'.");
        RejectLinks(path);
        return path;
    }

    internal static void RejectLinks(string path)
    {
        for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Plugin path traverses a symbolic link at '{cursor}'.");
    }
}
