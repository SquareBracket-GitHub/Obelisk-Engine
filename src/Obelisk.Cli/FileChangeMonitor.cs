using System.Diagnostics;
using System.Threading.Channels;

namespace Obelisk.Cli;

public interface IChangeMonitor : IDisposable
{
    long Revision { get; }
    void Configure(ProjectManifest manifest);
    Task WaitForChangeAsync(CancellationToken cancellation);
}

/// <summary>Coalesces watcher events until files are quiet; SDK output and editor metadata are ignored.</summary>
public sealed class FileChangeMonitor : IChangeMonitor
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _root;
    private readonly TimeSpan _debounce;
    private readonly Channel<bool> _events = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private long _revision;
    private long _lastChange;
    private string[] _content = ["assets", "plugins"];
    public long Revision => Interlocked.Read(ref _revision);

    public FileChangeMonitor(string directory, TextWriter error, TimeSpan? debounce = null)
    {
        _root = Path.GetFullPath(directory);
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += (_, e) => Changed(e.FullPath);
        _watcher.Created += (_, e) => Changed(e.FullPath);
        _watcher.Deleted += (_, e) => Changed(e.FullPath);
        _watcher.Renamed += (_, e) => { if (Relevant(e.OldFullPath) || Relevant(e.FullPath)) Signal(); };
        _watcher.Error += (_, e) => { error.WriteLine($"obelisk: File watcher error: {e.GetException().Message}. Rechecking the project."); Signal(); };
        _watcher.EnableRaisingEvents = true;
    }

    public void Configure(ProjectManifest manifest) => Volatile.Write(ref _content,
        [manifest.Assets.Replace('\\', '/'), manifest.Plugins.Replace('\\', '/')]);

    private void Changed(string path) { if (Relevant(path)) Signal(); }
    private void Signal()
    {
        Interlocked.Exchange(ref _lastChange, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _revision);
        _events.Writer.TryWrite(true);
    }
    private bool Relevant(string path)
    {
        var relative = Path.GetRelativePath(_root, path).Replace('\\', '/');
        if (relative.Split('/').Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase) || s is ".git" or ".vs" or ".idea" or ".vscode" or "artifacts")) return false;
        if (Volatile.Read(ref _content).Any(root => relative.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))) return true;
        return Path.GetExtension(relative).ToLowerInvariant() is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".config" or ".sln" or ".slnx";
    }
    public async Task WaitForChangeAsync(CancellationToken cancellation)
    {
        _ = await _events.Reader.ReadAsync(cancellation).ConfigureAwait(false);
        while (true)
        {
            var revision = Revision;
            var remaining = _debounce - Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastChange));
            if (remaining > TimeSpan.Zero) { await Task.Delay(remaining, cancellation).ConfigureAwait(false); continue; }
            while (_events.Reader.TryRead(out _)) { }
            if (Revision != revision || Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastChange)) < _debounce) continue;
            return;
        }
    }
    public void Dispose() { _watcher.Dispose(); _events.Writer.TryComplete(); }
}
