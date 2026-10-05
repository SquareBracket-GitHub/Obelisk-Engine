using Obelisk.Abstractions;

namespace Obelisk.Runtime;

/// <summary>
/// Defines the platform operations required by the application lifecycle.
/// </summary>
public interface IApplicationPlatform
{
    InputSnapshot Input { get; }

    void Initialize();

    /// <summary>
    /// Processes pending platform events.
    /// </summary>
    /// <returns><see langword="true"/> to run a frame; otherwise, <see langword="false"/> to stop.</returns>
    bool PumpEvents();

    void Shutdown();
}
