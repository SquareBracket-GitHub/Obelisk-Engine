namespace Obelisk.Abstractions;

/// <summary>
/// Defines the lifecycle callbacks implemented by an Obelisk game.
/// </summary>
public interface IGame
{
    /// <summary>Acquires game resources on the main thread before the first frame.</summary>
    void Initialize(InitializeContext context);

    /// <summary>Updates simulation on the main thread before rendering the same frame.</summary>
    void Update(UpdateContext context);

    /// <summary>Appends drawing commands to this frame's Canvas on the main thread.</summary>
    void Render(RenderContext context);

    /// <summary>Releases game-owned resources before platform shutdown; runs after successful initialization.</summary>
    void Shutdown(ShutdownContext context);
}
