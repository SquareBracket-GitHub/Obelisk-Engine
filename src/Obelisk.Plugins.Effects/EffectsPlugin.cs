using System.Text.Json;
using Obelisk.Plugin;

namespace Obelisk.Plugins.Effects;

/// <summary>Registers optional blur setting assets. Games explicitly create and own BlurEffect instances.</summary>
public sealed class EffectsPlugin : IObeliskPlugin
{
    /// <summary>Registers shared and effects-scoped JSON blur-setting loaders without acquiring GPU resources.</summary>
    public void Register(PluginRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RegisterDefaultAssetLoader<BlurSettings>(Load);
        context.RegisterAssetLoader<BlurSettings>(Load);
    }
    private static BlurSettings Load(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Blur settings exceed 64 KiB.");
        var settings = JsonSerializer.Deserialize<BlurSettings>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        })
            ?? throw new InvalidDataException("Expected blur settings object.");
        settings.Validate(); return settings;
    }
}

/// <summary>Configures a bounded separable Gaussian blur. Radius and sigma are measured in input texture pixels.</summary>
public sealed record BlurSettings
{
    /// <summary>Gets the kernel radius, 0–32 pixels; zero bypasses all effect passes.</summary>
    public int Radius { get; init; } = 8;
    /// <summary>Gets positive Gaussian sigma in input pixels; defaults to 3.</summary>
    public float Sigma { get; init; } = 3;
    /// <summary>Gets working-resolution reduction (1, 2 or 4). Defaults to full resolution.</summary>
    public int Downsample { get; init; } = 1;
    internal void Validate()
    {
        if (Radius is < 0 or > 32 || !float.IsFinite(Sigma) || Sigma < 0.1f || Sigma > 64 || Downsample is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(BlurSettings));
    }
}
