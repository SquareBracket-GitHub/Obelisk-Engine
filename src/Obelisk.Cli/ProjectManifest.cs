using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obelisk.Cli;

public sealed record ProjectManifest
{
    [JsonRequired] public int SchemaVersion { get; init; }
    [JsonRequired] public required string Name { get; init; }
    [JsonRequired] public required string EngineVersion { get; init; }
    [JsonRequired] public required string Project { get; init; }
    [JsonRequired] public required string Assets { get; init; }
    [JsonRequired] public required string Plugins { get; init; }

    public static ProjectManifest Read(string directory)
    {
        var path = Path.Combine(Path.GetFullPath(directory), "obelisk.json");
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 65536) throw new InvalidDataException("Manifest exceeds 64 KiB.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Manifest must be an object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!fields.Add(property.Name)) throw new InvalidDataException($"Duplicate field '{property.Name}'.");
            var result = document.RootElement.Deserialize<ProjectManifest>(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidDataException("Manifest is empty.");
            if (result.SchemaVersion != 1) throw new InvalidDataException($"Unsupported schemaVersion {result.SchemaVersion}; expected 1.");
            if (!ProjectGenerator.IsValidName(result.Name)) throw new InvalidDataException("Invalid project name.");
            if (result.EngineVersion != CliApplication.EngineVersion)
                throw new InvalidDataException($"Engine version '{result.EngineVersion}' is incompatible with CLI template {CliApplication.EngineVersion}.");
            var project = Resolve(directory, result.Project);
            if (!project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("project must identify a .csproj file.");
            if (!File.Exists(project)) throw new FileNotFoundException("The manifest's project file is missing.", project);
            foreach (var relative in new[] { result.Assets, result.Plugins })
                if (File.Exists(Resolve(directory, relative))) throw new InvalidDataException($"Content path '{relative}' must be a directory.");
            return result;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidDataException($"Could not read '{path}': {error.Message} Run inside a generated project directory or fix obelisk.json.", error);
        }
    }

    internal static string Resolve(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        var segments = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || segments.Any(s => s.Length == 0 || s.EndsWith('.') || s.EndsWith(' ') || s.IndexOfAny([':', '\0']) >= 0))
            throw new InvalidDataException($"Path '{relative}' must be relative and confined to the project directory.");
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Path '{relative}' must be confined to the project directory.");
        for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if (Path.Exists(cursor) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Project path traverses a symbolic link at '{cursor}'.");
        return path;
    }
}
