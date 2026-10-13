namespace Obelisk.Cli;

/// <summary>An OS file lock serializes CLI processes; the inert lock file is kept to avoid inode races.</summary>
internal static class ProjectOperation
{
    public static FileStream Acquire(string project)
    {
        var path = ProjectManifest.Resolve(project, "artifacts/.obelisk.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error)
        { throw new IOException($"Project '{project}' is busy or its CLI lock cannot be opened. Stop other Obelisk run/dev/build/add/remove commands and retry. {error.Message}", error); }
    }
}
