namespace Obelisk.Abstractions;

/// <summary>
/// Defines the lifecycle callbacks implemented by an Obelisk game.
/// </summary>
public interface IGame
{
    void Initialize(InitializeContext context);

    void Update(UpdateContext context);

    void Render(RenderContext context);

    void Shutdown(ShutdownContext context);
}
