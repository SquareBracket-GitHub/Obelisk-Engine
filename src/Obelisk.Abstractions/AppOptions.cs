namespace Obelisk.Abstractions;

/// <summary>
/// Configures the application window and game loop.
/// </summary>
public sealed class AppOptions
{
    public string Title { get; init; } = "Obelisk Game";

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public bool Resizable { get; init; } = true;

    public TimeSpan MaximumDeltaTime { get; init; } = TimeSpan.FromMilliseconds(250);
}
