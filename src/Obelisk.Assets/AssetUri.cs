namespace Obelisk.Assets;

/// <summary>A path confined to the project or a named plugin asset root.</summary>
public readonly record struct AssetUri
{
    private AssetUri(string scheme, string? plugin, string path)
    {
        Scheme = scheme;
        Plugin = plugin;
        Path = path;
    }

    /// <summary>Gets project or plugin.</summary>
    public string Scheme { get; }
    /// <summary>Gets the Plugin authority, or null for project assets.</summary>
    public string? Plugin { get; }
    /// <summary>Gets the decoded slash-separated path confined to its asset root.</summary>
    public string Path { get; }

    /// <summary>Parses project:///path or plugin://name/path; malformed or escaping input throws FormatException.</summary>
    public static AssetUri Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException($"Invalid asset URI '{value}'. Expected project:///path or plugin://name/path.");
        if (uri.Scheme is not ("project" or "plugin") || uri.UserInfo.Length != 0 || uri.Port != -1)
            throw new FormatException($"Unsupported asset URI '{value}'.");
        if (uri.Scheme == "project" && uri.Host.Length != 0 || uri.Scheme == "plugin" && uri.Host.Length == 0)
            throw new FormatException($"Invalid asset root in '{value}'.");
        var plugin = uri.Scheme == "plugin" ? uri.Host : null;
        if (plugin is not null && !IsSafeSegment(plugin)) throw new FormatException($"Invalid plugin name in '{value}'.");
        var raw = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var slash = raw.IndexOf('/');
        if (slash < 0) throw new FormatException($"Asset path is missing in '{value}'.");
        var encodedPath = raw[(slash + 1)..];
        if (encodedPath.Length == 0 || encodedPath.Contains('\\')) throw new FormatException($"Invalid asset path in '{value}'.");
        var parts = encodedPath.Split('/');
        var decoded = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var segment = Uri.UnescapeDataString(part);
            if (!IsSafeSegment(segment)) throw new FormatException($"Asset path escapes its root in '{value}'.");
            decoded.Add(segment);
        }
        return new AssetUri(uri.Scheme, plugin, string.Join('/', decoded));
    }

    private static bool IsSafeSegment(string value) => value.Length > 0 && value is not ("." or "..") &&
        value.IndexOfAny(['/', '\\', ':', '\0']) < 0;

    /// <summary>Returns the normalized scheme, authority and stored path.</summary>
    public override string ToString() => Scheme == "project"
        ? $"project:///{Path}"
        : $"plugin://{Plugin}/{Path}";
}
