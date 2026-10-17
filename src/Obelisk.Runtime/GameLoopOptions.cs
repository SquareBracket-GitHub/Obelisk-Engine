namespace Obelisk.Runtime;

/// <summary>Configures the lower-level synchronous application loop.</summary>
public sealed class GameLoopOptions
{
    /// <summary>
    /// Gets the largest simulation step accepted for a single frame.
    /// </summary>
    public TimeSpan MaximumDeltaTime { get; init; } = TimeSpan.FromMilliseconds(250);
}
