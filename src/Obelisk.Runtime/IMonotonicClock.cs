namespace Obelisk.Runtime;

/// <summary>
/// Provides elapsed time that must never move backwards.
/// </summary>
public interface IMonotonicClock
{
    TimeSpan Elapsed { get; }
}
