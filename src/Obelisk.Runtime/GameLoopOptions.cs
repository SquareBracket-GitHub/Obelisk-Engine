namespace Obelisk.Runtime;

public sealed class GameLoopOptions
{
    /// <summary>
    /// Gets the largest simulation step accepted for a single frame.
    /// </summary>
    public TimeSpan MaximumDeltaTime { get; init; } = TimeSpan.FromMilliseconds(250);
}
