using System.Diagnostics;

namespace Obelisk.Runtime;

public sealed class StopwatchClock : IMonotonicClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
}
