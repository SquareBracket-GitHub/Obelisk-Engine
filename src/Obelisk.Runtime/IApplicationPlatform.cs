using Obelisk.Abstractions;

namespace Obelisk.Runtime;

/// <summary>
/// Defines the platform operations required by the application lifecycle.
/// </summary>
public interface IApplicationPlatform
{
    /// <summary>Gets a borrowed texture factory, or null when unsupported.</summary>
    Obelisk.Graphics2D.ITextureDevice? Textures => null;
    /// <summary>Gets a borrowed audio device or the silent fallback.</summary>
    IAudioDevice Audio => SilentAudioDevice.Instance;

    /// <summary>Gets the latest immutable input snapshot after pumping events.</summary>
    InputSnapshot Input { get; }

    /// <summary>Gets current drawable output dimensions in pixels.</summary>
    OutputSize OutputSize { get; }

    /// <summary>Acquires platform resources; implementations must clean up their own partial initialization failures.</summary>
    void Initialize();

    /// <summary>
    /// Processes pending platform events.
    /// </summary>
    /// <returns><see langword="true"/> to run a frame; otherwise, <see langword="false"/> to stop.</returns>
    bool PumpEvents();

    /// <summary>Submits the frame prepared by the game to the presentation backend.</summary>
    void Present(RenderContext context);

    /// <summary>Releases platform resources after game cleanup on the owning thread.</summary>
    void Shutdown();
}
