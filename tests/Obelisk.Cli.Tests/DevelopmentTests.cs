using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace Obelisk.Cli.Tests;

public sealed class DevelopmentTests
{
    [Theory]
    [InlineData("{}", "required")]
    [InlineData("[]", "object")]
    [InlineData("{", "JSON")]
    public void ManifestErrorsExplainPathAndRepair(string json, string reason)
    {
        using var files = new Workspace();
        File.WriteAllText(files.ManifestPath, json);
        var error = Assert.Throws<InvalidDataException>(() => ProjectManifest.Read(files.Root));
        Assert.Contains(files.ManifestPath, error.Message);
        Assert.Contains(reason, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fix obelisk.json", error.Message);
    }

    [Theory]
    [InlineData("schemaVersion", "2", "schemaVersion")]
    [InlineData("engineVersion", "0.2.0", "incompatible")]
    [InlineData("name", "../bad", "name")]
    [InlineData("project", "../Game.csproj", "confined")]
    [InlineData("project", "Game.txt", ".csproj")]
    [InlineData("project", "Missing.csproj", "missing")]
    [InlineData("assets", "/outside", "confined")]
    [InlineData("plugins", "C:\\outside", "confined")]
    [InlineData("plugins", "a/../plugins", "confined")]
    [InlineData("assets", "a//b", "confined")]
    [InlineData("assets", ".. /outside", "confined")]
    [InlineData("plugins", "plugins./data", "confined")]
    public void ManifestValidatesSchemaCompatibilityAndPaths(string field, string value, string reason)
    {
        using var files = new Workspace();
        files.WriteManifest(field, field == "schemaVersion" ? int.Parse(value) : value);
        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => ProjectManifest.Read(files.Root)).Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("large")]
    [InlineData("file-root")]
    public void ManifestRejectsMissingAmbiguousOrInvalidInput(string kind)
    {
        using var files = new Workspace();
        var json = File.ReadAllText(files.ManifestPath);
        switch (kind)
        {
            case "missing": File.Delete(files.ManifestPath); break;
            case "null": files.WriteManifest("assets", null); break;
            case "unknown": files.WriteManifest("unknown", true); break;
            case "duplicate": File.WriteAllText(files.ManifestPath, json.Insert(1, "\"name\":\"Other\",")); break;
            case "large": File.WriteAllText(files.ManifestPath, new string(' ', 65537)); break;
            case "file-root": File.WriteAllText(Path.Combine(files.Root, "assets"), "file"); break;
        }
        Assert.Throws<InvalidDataException>(() => ProjectManifest.Read(files.Root));
    }

    [Fact]
    public async Task RunOrdersSdkStagesAndPreservesGameArgumentsAndExitCode()
    {
        using var files = new Workspace();
        var runner = new FakeRunner { GameExit = 42 };
        var arguments = new[] { "two words", "--custom", "a\"b", "$(literal);&" };
        var code = await CliApplication.RunAsync(["run", "--", .. arguments], files.Context,
            development: new DevelopmentCommands(runner));
        Assert.Equal(42, code);
        Assert.Equal(["restore", "build", "run"], runner.Commands.Select(c => c.Arguments[0]).ToArray());
        var run = runner.Commands.Last();
        Assert.Equal(arguments, run.Arguments.Skip(6));
        Assert.All(runner.Commands, c => Assert.Equal(files.Root, c.WorkingDirectory));
        Assert.All(runner.Children, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailedSdkStagePreventsGameStartAndPropagatesExit(int stage)
    {
        using var files = new Workspace();
        var runner = new FakeRunner { FailStage = stage };
        Assert.Equal(7, await CliApplication.RunAsync(["run"], files.Context, development: new DevelopmentCommands(runner)));
        Assert.Equal(stage, runner.Commands.Count);
        Assert.DoesNotContain(runner.Commands, c => c.Arguments[0] == "run");
    }

    [Fact]
    public async Task InvalidDirectoryAndRunArgumentsHaveStableExitCodes()
    {
        using var files = new Workspace();
        File.Delete(files.ManifestPath);
        Assert.Equal(1, await CliApplication.RunAsync(["run"], files.Context));
        Assert.Contains("Run inside a generated project", files.Error.ToString());
        Assert.Equal(2, await CliApplication.RunAsync(["run", "--unknown"], files.Context));
        Assert.Equal(2, await CliApplication.RunAsync(["dev", "extra"], files.Context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDisposesRestoreOrGame(bool game)
    {
        using var files = new Workspace();
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner { BlockStage = game ? 0 : 1 };
        var task = CliApplication.RunAsync(["run"], files.Context, development: new DevelopmentCommands(runner), cancellation: cancel.Token);
        await Until(() => runner.Commands.Count == (game ? 3 : 1));
        cancel.Cancel();
        Assert.Equal(130, await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.All(runner.Children, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task DevStopsOldGameBeforeRebuildAndDoesNotAutoRespawnAnExitedGame()
    {
        using var files = new Workspace();
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner(); var changes = new ManualChanges();
        var task = CliApplication.RunAsync(["dev"], files.Context, development: new DevelopmentCommands(runner, _ => changes), cancellation: cancel.Token);
        await Until(() => runner.Games.Count == 1);
        var first = runner.Games.First();
        changes.Signal();
        await Until(() => runner.Games.Count == 2);
        Assert.True(first.Disposed);
        Assert.True(runner.PreviousGameStoppedBeforeRestore);
        runner.Games.Last().Exit(0);
        await Until(() => runner.Games.Last().Disposed);
        await Task.Delay(100);
        Assert.Equal(2, runner.Games.Count);
        cancel.Cancel();
        Assert.Equal(130, await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(changes.Disposed);
        Assert.All(runner.Children, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task DevRecoversFromBadManifestAndBuildFailureAfterNextChange()
    {
        using var files = new Workspace();
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner(); var changes = new ManualChanges();
        var task = CliApplication.RunAsync(["dev"], files.Context, development: new DevelopmentCommands(runner, _ => changes), cancellation: cancel.Token);
        await Until(() => runner.Games.Count == 1);
        File.WriteAllText(files.ManifestPath, "{"); changes.Signal();
        await Until(() => runner.Games.First().Disposed);
        await Until(() => files.Error.ToString().Contains("Waiting for changes", StringComparison.Ordinal));
        files.WriteManifest(); runner.FailStage = runner.Commands.Count + 2; changes.Signal();
        await Until(() => files.Error.ToString().Contains("Preparation failed", StringComparison.Ordinal));
        Assert.Single(runner.Games);
        runner.FailStage = 0; changes.Signal();
        await Until(() => runner.Games.Count == 2);
        cancel.Cancel(); Assert.Equal(130, await task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ChangesDuringBuildDiscardStaleLaunchAndStartOnlyLatestBuild()
    {
        using var files = new Workspace();
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner { BlockStage = 2 }; var changes = new ManualChanges();
        var task = CliApplication.RunAsync(["dev"], files.Context, development: new DevelopmentCommands(runner, _ => changes), cancellation: cancel.Token);
        await Until(() => runner.Commands.Count == 2);
        changes.Signal(); runner.Children.Last().Exit(0);
        await Until(() => runner.Games.Count == 1);
        Assert.Equal(5, runner.Commands.Count);
        cancel.Cancel(); Assert.Equal(130, await task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RealWatcherCoalescesBurstAndIgnoresSdkOutputAndLogs()
    {
        using var files = new Workspace();
        using var changes = new FileChangeMonitor(files.Root, files.Error, TimeSpan.FromMilliseconds(100));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Directory.CreateDirectory(Path.Combine(files.Root, "bin", "nested"));
        File.WriteAllText(Path.Combine(files.Root, "bin", "nested", "noise.cs"), "ignored");
        File.WriteAllText(Path.Combine(files.Root, "runtime.log"), "ignored");
        await Task.Delay(150); Assert.Equal(0, changes.Revision);
        var wait = changes.WaitForChangeAsync(cancel.Token);
        for (var i = 0; i < 5; i++) { File.WriteAllText(Path.Combine(files.Root, "Game.cs"), i.ToString()); await Task.Delay(20); }
        var lastWrite = Stopwatch.StartNew();
        await wait;
        Assert.True(lastWrite.Elapsed >= TimeSpan.FromMilliseconds(60));
        Assert.True(changes.Revision > 0);
        var revision = changes.Revision;
        await Task.Delay(150); Assert.Equal(revision, changes.Revision);
        var second = changes.WaitForChangeAsync(cancel.Token);
        await Task.Delay(150); Assert.False(second.IsCompleted);
        File.Move(Path.Combine(files.Root, "Game.cs"), Path.Combine(files.Root, "Renamed.cs"));
        await second;
    }

    [Fact]
    public async Task RealWatcherIncludesConfiguredContentRoots()
    {
        using var files = new Workspace();
        files.WriteManifest("assets", "data/art");
        Directory.CreateDirectory(Path.Combine(files.Root, "data", "art"));
        using var changes = new FileChangeMonitor(files.Root, files.Error, TimeSpan.FromMilliseconds(30));
        changes.Configure(ProjectManifest.Read(files.Root));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var wait = changes.WaitForChangeAsync(cancel.Token);
        File.WriteAllText(Path.Combine(files.Root, "data", "art", "image.bin"), "asset");
        await wait;
    }

    [Fact]
    public async Task RealProcessRunnerPreservesArgumentsStreamsAndExitCode()
    {
        using var files = new Workspace();
        var output = new StringWriter(); var error = new StringWriter();
        var args = new[] { "echo", "two words", "a\"b", "$(literal);&", "" };
        await using var child = new ProcessRunner().Start(ChildCommand(files.Root, args), output, error);
        Assert.Equal(17, await child.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(args[1..], JsonSerializer.Deserialize<string[]>(output.ToString().Trim()));
        Assert.Contains("child-stderr", error.ToString());
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("parent-exit")]
    public async Task WindowsJobReleasesDescendantEvenWhenItsParentAlreadyExits(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new Workspace();
        var parent = Path.Combine(files.Root, "parent.pid"); var descendant = Path.Combine(files.Root, "child.pid");
        await using var child = new ProcessRunner().Start(ChildCommand(files.Root, [mode, parent, descendant]), new StringWriter(), new StringWriter());
        await Until(() => File.Exists(descendant) && new FileInfo(descendant).Length > 0);
        var pid = int.Parse(File.ReadAllText(descendant));
        if (mode == "tree") await child.StopAsync();
        else Assert.Equal(23, await child.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(IsAlive(pid)); Assert.False(IsAlive(child.Id));
        await child.StopAsync();
    }

    [Fact]
    public void MissingSdkExecutableHasActionableDiagnostic()
    {
        using var files = new Workspace();
        var error = Assert.Throws<InvalidOperationException>(() => new ProcessRunner().Start(
            new ProcessCommand("obelisk-nonexistent-executable", [], files.Root), new StringWriter(), new StringWriter()));
        Assert.Contains("Install the .NET 10 SDK", error.Message);
    }

    private static bool IsAlive(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static ProcessCommand ChildCommand(string root, string[] args)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Obelisk.sln"))) repository = repository.Parent;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(repository!.FullName, "tests", "Obelisk.Cli.TestChild", "bin", configuration, "net10.0", "Obelisk.Cli.TestChild.dll");
        return new ProcessCommand("dotnet", [dll, .. args], root);
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "obelisk-dev-tests", Guid.NewGuid().ToString("N"));
        public string ManifestPath => Path.Combine(Root, "obelisk.json");
        public StringWriter Output { get; } = new();
        public StringWriter Error { get; } = new();
        public CliContext Context => new(Root, Output, Error);
        public Workspace() { Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, "Game.csproj"), "<Project />"); WriteManifest(); }
        public void WriteManifest(string? field = null, object? value = null)
        {
            var fields = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["name"] = "Game",
                ["engineVersion"] = "0.1.0",
                ["project"] = "Game.csproj",
                ["assets"] = "assets",
                ["plugins"] = "plugins"
            };
            if (field is not null) fields[field] = value;
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(fields));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private sealed class ManualChanges : IChangeMonitor
    {
        private readonly Channel<bool> _changes = Channel.CreateUnbounded<bool>();
        private long _revision;
        public long Revision => Interlocked.Read(ref _revision);
        public bool Disposed { get; private set; }
        public void Configure(ProjectManifest manifest) { }
        public void Signal() { Interlocked.Increment(ref _revision); _changes.Writer.TryWrite(true); }
        public async Task WaitForChangeAsync(CancellationToken cancellation) => _ = await _changes.Reader.ReadAsync(cancellation);
        public void Dispose() { Disposed = true; _changes.Writer.TryComplete(); }
    }
    private sealed class FakeRunner : IProcessRunner
    {
        public ConcurrentQueue<ProcessCommand> Commands { get; } = new();
        public ConcurrentQueue<FakeChild> Children { get; } = new();
        public ConcurrentQueue<FakeChild> Games { get; } = new();
        public int? GameExit { get; init; }
        public int FailStage { get; set; }
        public int BlockStage { get; init; }
        public bool PreviousGameStoppedBeforeRestore { get; private set; } = true;
        public IChildProcess Start(ProcessCommand command, TextWriter output, TextWriter error)
        {
            if (command.Arguments[0] == "restore" && Games.TryPeek(out _))
                PreviousGameStoppedBeforeRestore &= Games.All(c => c.Disposed);
            Commands.Enqueue(command);
            var game = command.Arguments[0] == "run";
            var child = new FakeChild(Commands.Count);
            Children.Enqueue(child);
            if (game) { Games.Enqueue(child); if (GameExit is { } exit) child.Exit(exit); }
            else if (Commands.Count != BlockStage) child.Exit(Commands.Count == FailStage ? 7 : 0);
            return child;
        }
    }
    private sealed class FakeChild(int id) : IChildProcess
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Id => id;
        public Task<int> Completion => _exit.Task;
        public bool Disposed { get; private set; }
        public void Exit(int code) => _exit.TrySetResult(code);
        public Task StopAsync() { _exit.TrySetResult(-1); return Task.CompletedTask; }
        public async ValueTask DisposeAsync() { await StopAsync(); Disposed = true; }
    }
}
