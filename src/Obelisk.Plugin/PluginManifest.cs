using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Obelisk.Plugin;

/// <summary>Identifies a Plugin name and failed operation while preserving the original error as InnerException.</summary>
/// <param name="pluginName">Failing Plugin identifier.</param>
/// <param name="operation">Failing operation or source path.</param>
/// <param name="cause">Underlying error preserved as InnerException.</param>
public sealed class PluginException(string pluginName, string operation, Exception cause)
    : Exception($"Plugin '{pluginName}' failed during {operation}: {cause.Message}", cause)
{
    /// <summary>Gets the failing Plugin identifier.</summary>
    public string PluginName { get; } = pluginName;
    /// <summary>Gets the failing operation, potentially including a source path.</summary>
    public string Operation { get; } = operation;
}

/// <summary>Schema 1: stable three-part versions, inclusive minimum and exclusive maximum engine version.</summary>
public sealed record PluginManifest
{
    /// <summary>Names the manifest file required at the root of a Plugin package.</summary>
    public const string FileName = "obelisk.plugin.json";
    /// <summary>Gets the engine version used for manifest compatibility checks, currently 0.1.0.</summary>
    public static System.Version CurrentEngineVersion { get; } = new(0, 1, 0);
    /// <summary>Gets manifest schema 1; unknown schema versions are rejected.</summary>
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the unique lowercase ASCII Plugin name, 1–64 characters, starting with a letter.</summary>
    [JsonRequired] public required string Name { get; init; }
    /// <summary>Gets the stable three-part Plugin version without prerelease or build suffixes.</summary>
    [JsonRequired] public required string Version { get; init; }
    /// <summary>Gets the inclusive minimum compatible engine version.</summary>
    [JsonRequired] public required string EngineMinimum { get; init; }
    /// <summary>Gets the exclusive maximum compatible engine version.</summary>
    [JsonRequired] public required string EngineMaximumExclusive { get; init; }
    /// <summary>Gets the relative entry DLL path within the package.</summary>
    [JsonRequired] public required string Assembly { get; init; }
    /// <summary>Gets the full name of a public concrete IObeliskPlugin type with a public parameterless constructor.</summary>
    [JsonRequired] public required string EntryPoint { get; init; }
    /// <summary>Gets the relative asset directory, defaulting to assets.</summary>
    public string Assets { get; init; } = "assets";
    /// <summary>Gets the relative shader directory, defaulting to shaders; raw GPU hooks remain experimental.</summary>
    public string Shaders { get; init; } = "shaders";
    /// <summary>Gets the relative private native library directory, defaulting to native.</summary>
    public string Native { get; init; } = "native";

    /// <summary>Reads and validates a strict bounded schema-1 manifest against the current engine; wraps failures in PluginException.</summary>
    public static PluginManifest Read(string path)
    {
        var name = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path))) ?? "unknown";
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 64 * 1024) throw new InvalidDataException("Manifest exceeds 64 KiB.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Manifest must be an object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate manifest property '{property.Name}'.");
            var manifest = document.RootElement.Deserialize<PluginManifest>(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidDataException("Manifest is empty.");
            name = manifest.Name ?? name;
            manifest.Validate(CurrentEngineVersion);
            return manifest;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new PluginException(name, $"manifest '{path}'", error); }
    }

    /// <summary>Validates names, stable versions and the inclusive/exclusive engine range; invalid values throw InvalidDataException.</summary>
    public void Validate(System.Version engine)
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"Unsupported manifest schema {SchemaVersion}; expected 1.");
        if (Name is null || !Regex.IsMatch(Name, "^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Plugin name must be 1–64 lowercase ASCII letters, digits or hyphens, starting with a letter.");
        _ = ParseVersion(Version);
        var minimum = ParseVersion(EngineMinimum);
        var maximum = ParseVersion(EngineMaximumExclusive);
        if (minimum >= maximum) throw new InvalidDataException("Engine minimum must be less than its exclusive maximum.");
        if (engine < minimum || engine >= maximum)
            throw new InvalidDataException($"Engine {engine} is incompatible; required [{minimum}, {maximum}).");
        if (string.IsNullOrWhiteSpace(Assembly) || !Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Assembly must identify a relative .dll file.");
        if (string.IsNullOrWhiteSpace(EntryPoint)) throw new InvalidDataException("Plugin entryPoint is missing.");
    }

    private static System.Version ParseVersion(string value)
    {
        if (value is null || !Regex.IsMatch(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant) ||
            !System.Version.TryParse(value, out var version))
            throw new InvalidDataException($"Invalid stable three-part version '{value}'.");
        return version;
    }
}
