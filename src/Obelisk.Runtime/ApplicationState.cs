namespace Obelisk.Runtime;

/// <summary>Describes the one-shot application host lifecycle.</summary>
public enum ApplicationState
{
    /// <summary>The host has not run.</summary>
    Created,
    /// <summary>Platform or game initialization is in progress.</summary>
    Initializing,
    /// <summary>Update, Render and presentation are running sequentially.</summary>
    Running,
    /// <summary>Game and platform cleanup is in progress.</summary>
    ShuttingDown,
    /// <summary>Cleanup has finished, including after a failure; the host cannot run again.</summary>
    Stopped,
}
