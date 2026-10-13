namespace Obelisk.Cli;

internal static class ProjectFiles
{
    public static void ValidateTree(string root)
    {
        _ = ProjectManifest.Resolve(root, ".obelisk-path-check");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Directory '{root}' is missing.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            _ = ProjectManifest.Resolve(root, Path.GetFileName(entry));
            if (Directory.Exists(entry)) ValidateTree(entry);
        }
    }

    public static void CopyTree(string source, string target)
    {
        ValidateTree(source);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(target).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Copy destination must not be inside its source directory.");
        Copy(source, target);
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            _ = ProjectManifest.Resolve(source, name);
            var destination = ProjectManifest.Resolve(target, name);
            if (Directory.Exists(entry)) Copy(entry, destination);
            else File.Copy(entry, destination, overwrite: true);
        }
    }

    public static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        ValidateTree(root);
        Directory.Delete(root, recursive: true);
    }

    public static string ContentRoot(string project, string relative)
    {
        var root = ProjectManifest.Resolve(project, relative);
        var first = relative.Replace('\\', '/').Split('/')[0];
        if (new[] { "artifacts", "bin", "obj", ".git", ".vs", ".idea", ".vscode" }.Contains(first, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Content path '{relative}' must not use build output or editor metadata. Fix obelisk.json.");
        return root;
    }
}
