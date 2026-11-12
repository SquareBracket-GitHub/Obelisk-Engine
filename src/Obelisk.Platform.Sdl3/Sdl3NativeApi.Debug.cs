using Obelisk.Graphics2D;

namespace Obelisk.Platform.Sdl3;

internal sealed partial class Sdl3NativeApi
{
    private readonly Dictionary<nint, RenderTarget?> _debugSnapshots = [];
    public void EnableDebugCapture(nint pipeline) => _debugSnapshots.Add(pipeline, null);
    public byte[] CaptureScreenshot(nint pipeline)
    {
        if (!_debugSnapshots.TryGetValue(pipeline, out var snapshot) || snapshot is null)
            throw new InvalidOperationException("No drawable frame has been presented yet.");
        return PngWriter.Encode(snapshot.Texture.Width, snapshot.Texture.Height, _renderers[pipeline].DownloadSnapshot(snapshot));
    }
}
