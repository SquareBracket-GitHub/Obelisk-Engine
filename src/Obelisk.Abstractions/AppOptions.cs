namespace Obelisk.Abstractions;

/// <summary>
/// Configures the application window and game loop.
/// </summary>
public sealed class AppOptions
{
    /// <summary>Gets the nonempty window title.</summary>
    public string Title { get; init; } = "Obelisk Game";

    /// <summary>Gets the positive initial window width; defaults to 1280.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>Gets the positive initial window height; defaults to 720.</summary>
    public int Height { get; init; } = 720;

    /// <summary>Gets whether the user can resize the window.</summary>
    public bool Resizable { get; init; } = true;

    /// <summary>Gets the positive simulation delta limit; defaults to 250 milliseconds.</summary>
    public TimeSpan MaximumDeltaTime { get; init; } = TimeSpan.FromMilliseconds(250);
}
