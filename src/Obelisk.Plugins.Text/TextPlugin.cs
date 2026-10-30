using Obelisk.Plugin;

namespace Obelisk.Plugins.Text;

/// <summary>Registers CPU font faces and standard MSDF atlas assets; games own their GPU font renderers.</summary>
public sealed class TextPlugin : IObeliskPlugin
{
    /// <summary>Registers shared and text-scoped font and MSDF loaders. Native GPU resources are not created during registration.</summary>
    public void Register(PluginRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RegisterDefaultAssetLoader<FontFace>(FontFace.LoadFile);
        context.RegisterAssetLoader<FontFace>(FontFace.LoadFile);
        context.RegisterDefaultAssetLoader<MsdfAtlas>(MsdfAtlas.LoadFile);
        context.RegisterAssetLoader<MsdfAtlas>(MsdfAtlas.LoadFile);
    }
}
