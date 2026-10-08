using Obelisk.Abstractions;

namespace Obelisk.Runtime;

/// <summary>
/// Defines the platform operations required by the application lifecycle.
/// </summary>
public interface IApplicationPlatform
{
    Obelisk.Graphics2D.ITextureDevice? Textures => null;

    InputSnapshot Input { get; }

    OutputSize OutputSize { get; }

    void Initialize();

    /// <summary>
    /// Processes pending platform events.
    /// </summary>
    /// <returns><see langword="true"/> to run a frame; otherwise, <see langword="false"/> to stop.</returns>
    bool PumpEvents();

    /// <summary>Submits the frame prepared by the game to the presentation backend.</summary>
    void Present(RenderContext context);

    void Shutdown();
}
