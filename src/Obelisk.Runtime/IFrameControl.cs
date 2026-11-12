using Obelisk.Abstractions;

namespace Obelisk.Runtime;

/// <summary>Optional development-only simulation pacing supplied by a platform.</summary>
public interface IFrameControl
{
    /// <summary>Whether the next simulation/render frame should run after event pumping.</summary>
    bool ShouldRunFrame { get; }
    /// <summary>Optional fixed simulation delta, used when stepping paused frames.</summary>
    TimeSpan? FrameDelta { get; }
    /// <summary>Called after update, render and presentation have completed successfully.</summary>
    void FrameCompleted(GameTime time);
}
