namespace Obelisk.Abstractions;

/// <summary>A main-thread execution unit registered by a plugin, independent of its packaging.</summary>
public interface IObeliskModule
{
    /// <summary>Initializes before the game, in Plugin registration order; partial initialization is included in cleanup.</summary>
    void Initialize(InitializeContext context) { }
    /// <summary>Updates before the game on the main thread.</summary>
    void Update(UpdateContext context) { }
    /// <summary>Appends public Canvas commands after the game's Render; no backend access is provided.</summary>
    void Render(RenderContext context) { }
    /// <summary>Releases module resources after the game, in reverse initialization order.</summary>
    void Shutdown(ShutdownContext context) { }
}

/// <summary>Read-only access to the shared game/plugin asset cache.</summary>
public interface IAssetSource
{
    /// <summary>Loads or borrows a cached value for the typed asset URI; do not dispose borrowed results.</summary>
    T Load<T>(string uri) where T : class;
    /// <summary>Resolves project:///path or plugin://name/path under its registered confined root.</summary>
    string ResolvePath(string uri);
}
