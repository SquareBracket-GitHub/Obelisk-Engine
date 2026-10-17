using Obelisk.Graphics2D;

namespace Obelisk.Abstractions;

/// <summary>Provides initialization services. Missing textures/assets throw NotSupportedException; audio falls back to silence.</summary>
/// <param name="textures">Borrowed factory, or null when unsupported.</param>
/// <param name="audio">Borrowed device, or null for silent playback.</param>
/// <param name="assets">Borrowed typed cache, or null when unsupported.</param>
public sealed class InitializeContext(ITextureDevice? textures = null, IAudioDevice? audio = null, IAssetSource? assets = null)
{
    /// <summary>Creates a context sharing devices and borrowing the supplied nonnull asset source.</summary>
    public InitializeContext WithAssets(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new InitializeContext(textures, audio, source);
    }
    /// <summary>Gets borrowed cached assets; the host owns their disposable results.</summary>
    public IAssetSource Assets => assets ?? throw new NotSupportedException("This context does not provide an asset source.");
    /// <summary>Gets the owning-thread texture factory; callers own textures they create directly.</summary>
    public ITextureDevice Textures => textures ?? throw new NotSupportedException("This platform does not provide textures.");
    /// <summary>Gets the borrowed playback device or the silent fallback.</summary>
    public IAudioDevice Audio => audio ?? SilentAudioDevice.Instance;
}

/// <summary>Provides one frame's clamped time, immutable input snapshot and drawable size.</summary>
public sealed class UpdateContext
{
    /// <summary>Creates a frame snapshot; omitted input becomes the shared empty snapshot.</summary>
    public UpdateContext(
        GameTime time,
        InputSnapshot? input = null,
        OutputSize outputSize = default)
    {
        Time = time;
        Input = input ?? InputSnapshot.Empty;
        OutputSize = outputSize;
    }

    /// <summary>Gets the clamped simulation time for this frame.</summary>
    public GameTime Time { get; }

    /// <summary>Gets the immutable input state captured before this frame's update.</summary>
    public InputSnapshot Input { get; }

    /// <summary>Gets the drawable size in pixels; zero is possible when presentation is suspended.</summary>
    public OutputSize OutputSize { get; }
}

/// <summary>Collects fresh Canvas commands for a single frame; do not retain it across frames.</summary>
public sealed class RenderContext
{
    /// <summary>Creates a frame context with an empty Canvas command list.</summary>
    public RenderContext(GameTime time, OutputSize outputSize = default)
    {
        Time = time;
        OutputSize = outputSize;
        Canvas = new Canvas2D();
    }

    /// <summary>Gets the clamped simulation time for this frame.</summary>
    public GameTime Time { get; }

    /// <summary>Gets the drawable size in pixels; zero is possible when presentation is suspended.</summary>
    public OutputSize OutputSize { get; }

    /// <summary>Gets this frame's ordered drawing-command collector.</summary>
    public Canvas2D Canvas { get; }
}

/// <summary>Marks the shutdown callback while the platform is still alive for resource disposal.</summary>
public sealed class ShutdownContext;
