using System.Diagnostics;

namespace Obelisk.Runtime;

/// <summary>Starts a Stopwatch-backed monotonic clock when constructed.</summary>
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    /// <summary>Gets real time since clock construction.</summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;
}
