namespace Obelisk.Abstractions;

/// <summary>
/// Describes the simulation time for one frame.
/// </summary>
public readonly record struct GameTime
{
    public GameTime(TimeSpan elapsed, TimeSpan deltaTime, ulong frameNumber)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        if (deltaTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }

        Elapsed = elapsed;
        DeltaTime = deltaTime;
        FrameNumber = frameNumber;
    }

    /// <summary>
    /// Gets the accumulated, clamped simulation time.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets the clamped duration of the current frame.
    /// </summary>
    public TimeSpan DeltaTime { get; }

    /// <summary>
    /// Gets the current frame duration in seconds for movement calculations.
    /// </summary>
    public float Delta => (float)DeltaTime.TotalSeconds;

    /// <summary>
    /// Gets the zero-based frame number.
    /// </summary>
    public ulong FrameNumber { get; }
}
