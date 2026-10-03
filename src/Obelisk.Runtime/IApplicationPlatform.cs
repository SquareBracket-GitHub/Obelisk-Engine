namespace Obelisk.Runtime;

/// <summary>
/// Defines the platform operations required by the application lifecycle.
/// </summary>
public interface IApplicationPlatform
{
    void Initialize();

    /// <summary>
    /// Processes pending platform events.
    /// </summary>
    /// <returns><see langword="true"/> to run a frame; otherwise, <see langword="false"/> to stop.</returns>
    bool PumpEvents();

    void Shutdown();
}
