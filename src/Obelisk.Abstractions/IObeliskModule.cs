namespace Obelisk.Abstractions;

/// <summary>A main-thread execution unit registered by a plugin, independent of its packaging.</summary>
public interface IObeliskModule
{
    void Initialize(InitializeContext context) { }
    void Update(UpdateContext context) { }
    /// <summary>Appends public Canvas commands after the game's Render; no backend access is provided.</summary>
    void Render(RenderContext context) { }
    void Shutdown(ShutdownContext context) { }
}

/// <summary>Read-only access to the shared game/plugin asset cache.</summary>
public interface IAssetSource
{
    T Load<T>(string uri) where T : class;
    string ResolvePath(string uri);
}
