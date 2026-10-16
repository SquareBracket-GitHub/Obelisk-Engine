namespace Obelisk.Platform.Sdl3;

/// <summary>Owns one native resource. Dispose on the device's owning thread before destroying it.</summary>
internal sealed class GpuResource : IDisposable
{
    private static long _liveCount;
    private readonly Action<nint> _release;

    // Engine-owned handles, not the driver's deferred allocations or residency.
    internal static long LiveCount => Interlocked.Read(ref _liveCount);
    public nint Handle { get; private set; }

    public GpuResource(nint handle, Action<nint> release)
    {
        Handle = handle;
        _release = release;
        if (handle != 0) Interlocked.Increment(ref _liveCount);
    }

    public void Dispose()
    {
        var owned = Handle;
        Handle = 0;
        if (owned != 0)
        {
            try { _release(owned); }
            finally { Interlocked.Decrement(ref _liveCount); }
        }
    }
}
