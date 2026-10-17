namespace Obelisk.Runtime;

/// <summary>
/// Provides elapsed time that must never move backwards.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Gets elapsed real time that never decreases.</summary>
    TimeSpan Elapsed { get; }
}
