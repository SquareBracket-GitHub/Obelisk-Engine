namespace Obelisk.Platform.Sdl3;

/// <summary>Owns one native resource. Dispose on the device's owning thread before destroying it.</summary>
internal sealed class GpuResource(nint handle, Action<nint> release) : IDisposable
{
    public nint Handle { get; private set; } = handle;

    public void Dispose()
    {
        var owned = Handle;
        Handle = 0;
        if (owned != 0) release(owned);
    }
}
