namespace Obelisk.Abstractions;

/// <summary>
/// Configures the application window and game loop.
/// </summary>
public sealed class AppOptions
{
    /// <summary>Gets the nonempty window title.</summary>
    public string Title { get; init; } = "Obelisk Game";

    /// <summary>Gets the positive initial logical window width; defaults to 1280. Drawable pixels may differ on high-DPI displays.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>Gets the positive initial logical window height; defaults to 720. Drawable pixels may differ on high-DPI displays.</summary>
    public int Height { get; init; } = 720;

    /// <summary>Gets whether the user can resize the window.</summary>
    public bool Resizable { get; init; } = true;
    /// <summary>Gets requested MSAA samples (1, 2, 4 or 8). The backend falls back to supported hardware; defaults to 4.</summary>
    public int SampleCount { get; init; } = 4;

    /// <summary>Gets the positive simulation delta limit; defaults to 250 milliseconds.</summary>
    public TimeSpan MaximumDeltaTime { get; init; } = TimeSpan.FromMilliseconds(250);
}
