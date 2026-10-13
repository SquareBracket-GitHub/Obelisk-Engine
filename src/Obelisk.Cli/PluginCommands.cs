using System.Text.RegularExpressions;
using Obelisk.Plugin;

namespace Obelisk.Cli;

public sealed class PluginCommands
{
    internal static bool IsValidName(string? name) => name is not null &&
        Regex.IsMatch(name, "\\A[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant) &&
        !Regex.IsMatch(name, "\\A(con|prn|aux|nul|com[1-9]|lpt[1-9])\\z", RegexOptions.CultureInvariant);

    public int Run(CliContext context, string command, string? argument = null)
    {
        var project = ProjectManifest.Read(context.WorkingDirectory);
        using var operation = command == "list" ? null : ProjectOperation.Acquire(context.WorkingDirectory);
        var root = ProjectFiles.ContentRoot(context.WorkingDirectory, project.Plugins);
        var packages = Discover(root);
        if (command == "list")
        {
            if (packages.Count == 0) context.Output.WriteLine("No local Plugins installed.");
            foreach (var (directory, manifest) in packages.OrderBy(p => p.Manifest.Name, StringComparer.Ordinal))
                context.Output.WriteLine($"{manifest.Name} {manifest.Version} [{manifest.EngineMinimum}, {manifest.EngineMaximumExclusive}) {directory}");
            return 0;
        }
        if (command == "add")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(argument);
            var source = Path.GetFullPath(argument, context.WorkingDirectory);
            var manifest = ReadPackage(source);
            if (packages.Any(p => p.Manifest.Name == manifest.Name))
                throw new IOException($"Plugin '{manifest.Name}' is already installed. Stop the game and remove it before installing another version.");
            var destination = ProjectManifest.Resolve(root, manifest.Name);
            if (Path.Exists(destination)) throw new IOException($"Plugin destination '{destination}' already exists; existing files were preserved.");
            var staging = ProjectManifest.Resolve(context.WorkingDirectory, $"artifacts/.obelisk-add-{Guid.NewGuid():N}");
            try
            {
                ProjectFiles.CopyTree(source, staging);
                var copied = ReadPackage(staging);
                if (copied != manifest) throw new IOException("Plugin manifest changed during installation. Retry with the game stopped.");
                Directory.CreateDirectory(root);
                Directory.Move(staging, destination);
                context.Output.WriteLine($"Installed Plugin '{manifest.Name}' {manifest.Version} at '{destination}'. Restart the game to load it.");
            }
            finally { Cleanup(staging); }
            return 0;
        }
        if (command == "remove")
        {
            if (!IsValidName(argument))
                throw new ArgumentException("Use a Plugin manifest name, without paths: obelisk remove <plugin>.");
            var match = packages.SingleOrDefault(p => p.Manifest.Name == argument);
            if (match.Manifest is null) throw new IOException($"Plugin '{argument}' is not installed. Run 'obelisk list'.");
            var staging = ProjectManifest.Resolve(context.WorkingDirectory, $"artifacts/.obelisk-remove-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
            Directory.Move(match.Directory, staging);
            Cleanup(staging);
            context.Output.WriteLine($"Removed Plugin '{argument}'. Restart the game to apply removal.");
            return 0;
        }
        throw new ArgumentException("Unknown Plugin command.");
    }

    internal static List<(string Directory, PluginManifest Manifest)> Discover(string root)
    {
        var packages = new List<(string Directory, PluginManifest Manifest)>();
        if (!Directory.Exists(root)) return packages;
        ProjectFiles.ValidateTree(root);
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var manifest = ReadPackage(directory);
            if (packages.Any(p => p.Manifest.Name == manifest.Name)) throw new InvalidDataException($"Duplicate Plugin '{manifest.Name}' in '{root}'.");
            packages.Add((directory, manifest));
        }
        return packages;
    }

    private static PluginManifest ReadPackage(string directory)
    {
        ProjectFiles.ValidateTree(directory);
        try
        {
            var manifest = PluginManifest.Read(Path.Combine(directory, PluginManifest.FileName));
            if (!IsValidName(manifest.Name))
                throw new InvalidDataException($"Plugin '{manifest.Name}' cannot be used as a portable package directory name.");
            _ = ProjectManifest.Resolve(directory, manifest.Name);
            var assembly = ProjectManifest.Resolve(directory, manifest.Assembly);
            foreach (var path in new[] { manifest.Assets, manifest.Shaders, manifest.Native })
            {
                var resolved = ProjectManifest.Resolve(directory, path);
                if (File.Exists(resolved)) throw new InvalidDataException($"Plugin content path '{path}' must be a directory.");
            }
            if (!File.Exists(assembly)) throw new FileNotFoundException($"Plugin '{manifest.Name}' assembly is missing: '{assembly}'.");
            return manifest;
        }
        catch (PluginException error) { throw new InvalidDataException(error.Message, error); }
    }

    private static void Cleanup(string directory)
    {
        try { ProjectFiles.DeleteTree(directory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { throw new IOException($"Could not remove owned staging directory '{directory}': {error.Message}. Stop the game and remove this leftover directory.", error); }
    }
}
