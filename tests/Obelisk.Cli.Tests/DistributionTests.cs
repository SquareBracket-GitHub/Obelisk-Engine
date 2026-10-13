using System.Text.Json;

namespace Obelisk.Cli.Tests;

public sealed class DistributionTests
{
    [Fact]
    public async Task InstallListRemoveUsesManifestNameAndCopiesWholePackage()
    {
        using var files = new Workspace();
        files.MakePackage("sample");
        Assert.Equal(0, await CliApplication.RunAsync(["add", files.Package], files.Context));
        var installed = Path.Combine(files.Root, "plugins", "sample");
        Assert.Equal("asset", File.ReadAllText(Path.Combine(installed, "assets", "data.txt")));
        Assert.Equal("shader", File.ReadAllText(Path.Combine(installed, "shaders", "a.spv")));
        Assert.Equal("native", File.ReadAllText(Path.Combine(installed, "native", "helper.dll")));
        Assert.True(File.Exists(Path.Combine(installed, "private", "dependency.dll")));
        Assert.Equal(0, await CliApplication.RunAsync(["list"], files.Context));
        Assert.Contains("sample 0.1.0 [0.1.0, 0.2.0)", files.Output.ToString());
        Assert.Equal(0, await CliApplication.RunAsync(["remove", "sample"], files.Context));
        Assert.False(Directory.Exists(installed)); Assert.True(Directory.Exists(files.Package));
        files.Output.GetStringBuilder().Clear();
        Assert.Equal(0, await CliApplication.RunAsync(["list"], files.Context));
        Assert.Contains("No local Plugins", files.Output.ToString());
        Assert.Empty(Directory.GetDirectories(Path.Combine(files.Root, "artifacts"), ".obelisk-*-*"));
    }

    [Fact]
    public async Task ExistingInstallationAndDirectoryCollisionArePreserved()
    {
        using var files = new Workspace(); files.MakePackage("sample");
        Assert.Equal(0, await CliApplication.RunAsync(["add", files.Package], files.Context));
        var installed = Path.Combine(files.Root, "plugins", "sample", "assets", "data.txt");
        File.WriteAllText(installed, "keep");
        Assert.Equal(1, await CliApplication.RunAsync(["add", files.Package], files.Context));
        Assert.Equal("keep", File.ReadAllText(installed));
        Assert.Equal(0, await CliApplication.RunAsync(["remove", "sample"], files.Context));
        File.WriteAllText(Path.Combine(files.Root, "plugins", "sample"), "existing file");
        Assert.Equal(1, await CliApplication.RunAsync(["add", files.Package], files.Context));
        Assert.Contains("preserved", files.Error.ToString());
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("engineMinimum", "0.2.0")]
    [InlineData("assembly", "../outside.dll")]
    [InlineData("assets", "../outside")]
    [InlineData("native", ".. /outside")]
    [InlineData("name", "aux")]
    public async Task InvalidPackagesNeverInstall(string field, string value)
    {
        using var files = new Workspace(); files.MakePackage("sample", field, field == "schemaVersion" ? 2 : value);
        Assert.Equal(1, await CliApplication.RunAsync(["add", files.Package], files.Context));
        Assert.False(Directory.Exists(Path.Combine(files.Root, "plugins", "sample")));
    }

    [Theory]
    [InlineData("missing-directory")]
    [InlineData("missing-manifest")]
    [InlineData("missing-assembly")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    public async Task IncompleteOrAmbiguousPackageHasPathDiagnostic(string failure)
    {
        using var files = new Workspace(); files.MakePackage("sample");
        var manifest = Path.Combine(files.Package, "obelisk.plugin.json");
        switch (failure)
        {
            case "missing-directory": Directory.Delete(files.Package, true); break;
            case "missing-manifest": File.Delete(manifest); break;
            case "missing-assembly": File.Delete(Path.Combine(files.Package, "Sample.dll")); break;
            case "unknown-field": files.MakePackage("sample", "unknown", true); break;
            case "duplicate-field": File.WriteAllText(manifest, File.ReadAllText(manifest).Insert(1, "\"name\":\"other\",")); break;
        }
        Assert.Equal(1, await CliApplication.RunAsync(["add", files.Package], files.Context));
        Assert.Contains(files.Package, files.Error.ToString());
    }

    [Fact]
    public async Task FailedCopyCleansStagingAndPreservesSource()
    {
        using var files = new Workspace(); files.MakePackage("sample");
        using (var locked = new FileStream(Path.Combine(files.Package, "assets", "data.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(1, await CliApplication.RunAsync(["add", files.Package], files.Context));
        Assert.False(Directory.Exists(Path.Combine(files.Root, "plugins", "sample")));
        Assert.True(File.Exists(Path.Combine(files.Package, "assets", "data.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(files.Root, "artifacts"), ".obelisk-add-*"));
    }

    [Theory]
    [InlineData("../sample")]
    [InlineData("C:\\outside")]
    [InlineData("sample/other")]
    [InlineData("Missing")]
    public async Task RemoveRejectsPathsAndInvalidNames(string name)
    {
        using var files = new Workspace(); files.MakePackage("sample");
        Assert.Equal(0, await CliApplication.RunAsync(["add", files.Package], files.Context));
        Assert.Equal(2, await CliApplication.RunAsync(["remove", name], files.Context));
        Assert.True(Directory.Exists(Path.Combine(files.Root, "plugins", "sample")));
    }

    [Fact]
    public async Task DuplicateManuallyInstalledNamesPreventListAndMutation()
    {
        using var files = new Workspace(); files.MakePackage("sample");
        Assert.Equal(0, await CliApplication.RunAsync(["add", files.Package], files.Context));
        CopyPackage(files.Package, Path.Combine(files.Root, "plugins", "different-folder"));
        Assert.Equal(1, await CliApplication.RunAsync(["list"], files.Context));
        Assert.Contains("Duplicate Plugin 'sample'", files.Error.ToString());
        Assert.Equal(1, await CliApplication.RunAsync(["remove", "sample"], files.Context));
        Assert.True(Directory.Exists(Path.Combine(files.Root, "plugins", "sample")));
    }

    [Fact]
    public async Task BuildPublishesReleaseWindowsX64AndIncludesCustomContentRoots()
    {
        using var files = new Workspace(); files.WriteManifest("media", "extensions"); files.MakePackage("sample");
        Directory.CreateDirectory(Path.Combine(files.Root, "media"));
        File.WriteAllText(Path.Combine(files.Root, "media", "message.txt"), "custom asset");
        Assert.Equal(0, await CliApplication.RunAsync(["add", files.Package], files.Context));
        var publisher = new Publisher();
        Assert.Equal(0, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(publisher)));
        var args = publisher.Command!.Arguments;
        Assert.Equal("publish", args[0]); Assert.Contains("Release", args); Assert.Contains("win-x64", args);
        Assert.Contains("--self-contained", args); Assert.Contains("true", args); Assert.Contains("-p:PublishTrimmed=false", args);
        Assert.Equal("custom asset", File.ReadAllText(Path.Combine(files.OutputDirectory, "assets", "message.txt")));
        Assert.True(File.Exists(Path.Combine(files.OutputDirectory, "plugins", "sample", "native", "helper.dll")));
        Assert.True(publisher.Disposed);
        Assert.Empty(Directory.GetDirectories(Path.Combine(files.Root, "artifacts"), ".obelisk-publish-*"));
    }

    [Fact]
    public async Task RebuildReplacesOwnedOutputWithoutStaleFiles()
    {
        using var files = new Workspace();
        Assert.Equal(0, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(new Publisher())));
        File.WriteAllText(Path.Combine(files.OutputDirectory, "stale.dll"), "stale");
        Assert.Equal(0, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(new Publisher())));
        Assert.False(File.Exists(Path.Combine(files.OutputDirectory, "stale.dll")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(files.Root, "artifacts"), ".obelisk-backup-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPublishPreservesPreviousOutput(bool incomplete)
    {
        using var files = new Workspace();
        Assert.Equal(0, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(new Publisher())));
        File.WriteAllText(Path.Combine(files.OutputDirectory, "keep.txt"), "previous");
        var publisher = new Publisher { Exit = incomplete ? 0 : 7, Incomplete = incomplete };
        Assert.Equal(incomplete ? 1 : 7, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(publisher)));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(files.OutputDirectory, "keep.txt")));
        Assert.True(publisher.Disposed);
    }

    [Fact]
    public async Task CancellationStopsPublishAndPreservesOutput()
    {
        using var files = new Workspace();
        Assert.Equal(0, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(new Publisher())));
        var publisher = new Publisher { Block = true };
        using var cancellation = new CancellationTokenSource();
        var operation = CliApplication.RunAsync(["build"], files.Context, cancellation: cancellation.Token, build: new BuildCommand(publisher));
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        Assert.Equal(130, await operation); Assert.True(publisher.Disposed);
        Assert.True(File.Exists(Path.Combine(files.OutputDirectory, "Game.exe")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(files.Root, "artifacts"), ".obelisk-publish-*"));
    }

    [Fact]
    public async Task UnownedOutputIsNeverOverwrittenAndSdkIsNotInvoked()
    {
        using var files = new Workspace(); Directory.CreateDirectory(files.OutputDirectory);
        var sentinel = Path.Combine(files.OutputDirectory, "keep.txt"); File.WriteAllText(sentinel, "user");
        var publisher = new Publisher();
        Assert.Equal(1, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(publisher)));
        Assert.Null(publisher.Command); Assert.Equal("user", File.ReadAllText(sentinel));
        Assert.Contains("preserved", files.Error.ToString());
    }

    [Fact]
    public async Task ProjectLockRejectsConcurrentBuildInstallRemoveAndRun()
    {
        using var files = new Workspace(); files.MakePackage("sample");
        var publisher = new Publisher { Block = true }; using var cancellation = new CancellationTokenSource();
        var operation = CliApplication.RunAsync(["build"], files.Context, cancellation: cancellation.Token, build: new BuildCommand(publisher));
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var args in new[] { new[] { "build" }, new[] { "add", files.Package }, new[] { "remove", "sample" }, new[] { "run" } })
            Assert.Equal(1, await CliApplication.RunAsync(args, files.Context, build: new BuildCommand(new Publisher())));
        Assert.Contains("busy", files.Error.ToString());
        cancellation.Cancel(); Assert.Equal(130, await operation);
        Assert.Equal(0, await CliApplication.RunAsync(["add", files.Package], files.Context));
    }

    [Theory]
    [InlineData("build", "extra")]
    [InlineData("add", "")]
    [InlineData("remove", "")]
    [InlineData("list", "extra")]
    public async Task MalformedCommandsReturnUsage(string command, string extra)
    {
        using var files = new Workspace();
        Assert.Equal(2, await CliApplication.RunAsync(extra.Length == 0 ? [command] : [command, extra], files.Context));
        Assert.Equal(0, await CliApplication.RunAsync([command, "--help"], files.Context));
    }

    [Theory]
    [InlineData("artifacts/data")]
    [InlineData("bin/data")]
    [InlineData("obj/data")]
    public async Task ContentCannotAliasBuildOutput(string path)
    {
        using var files = new Workspace(); files.WriteManifest(path, "plugins");
        var publisher = new Publisher();
        Assert.Equal(1, await CliApplication.RunAsync(["build"], files.Context, build: new BuildCommand(publisher)));
        Assert.Null(publisher.Command);
    }

    private static void CopyPackage(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), "obelisk-distribution-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public string Package { get; }
        public string OutputDirectory => Path.Combine(Root, BuildCommand.OutputDirectory);
        public StringWriter Output { get; } = new(); public StringWriter Error { get; } = new();
        public CliContext Context => new(Root, Output, Error);
        public Workspace()
        {
            Root = Path.Combine(_parent, "game space"); Package = Path.Combine(_parent, "local package");
            Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, "Game.csproj"), "<Project />"); WriteManifest();
        }
        public void WriteManifest(string assets = "assets", string plugins = "plugins") => File.WriteAllText(Path.Combine(Root, "obelisk.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, name = "Game", engineVersion = "0.1.0", project = "Game.csproj", assets, plugins }));
        public void MakePackage(string name, string? field = null, object? value = null)
        {
            Directory.CreateDirectory(Package);
            var fields = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["name"] = name, ["version"] = "0.1.0",
                ["engineMinimum"] = "0.1.0", ["engineMaximumExclusive"] = "0.2.0", ["assembly"] = "Sample.dll", ["entryPoint"] = "Sample.Plugin" };
            if (field is not null) fields[field] = value;
            File.WriteAllText(Path.Combine(Package, "obelisk.plugin.json"), JsonSerializer.Serialize(fields));
            File.WriteAllText(Path.Combine(Package, "Sample.dll"), "assembly");
            foreach (var (path, text) in new[] { ("assets/data.txt", "asset"), ("shaders/a.spv", "shader"), ("native/helper.dll", "native"), ("private/dependency.dll", "dependency") })
            {
                var file = Path.Combine(Package, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text);
            }
        }
        public void Dispose() => Directory.Delete(_parent, recursive: true);
    }

    private sealed class Publisher : IProcessRunner, IChildProcess
    {
        public ProcessCommand? Command { get; private set; }
        public int Exit { get; init; }
        public bool Block { get; init; }
        public bool Incomplete { get; init; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Id => 1; public Task<int> Completion => _done.Task;
        public IChildProcess Start(ProcessCommand command, TextWriter output, TextWriter error)
        {
            Command = command;
            var args = command.Arguments.ToList(); var destination = args[args.IndexOf("--output") + 1];
            foreach (var file in Incomplete ? new[] { "Game.exe" } : new[] { "Game.exe", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll" })
                File.WriteAllText(Path.Combine(destination, file), "published");
            Started.TrySetResult(); if (!Block) _done.TrySetResult(Exit); return this;
        }
        public Task StopAsync() { _done.TrySetResult(-1); return Task.CompletedTask; }
        public async ValueTask DisposeAsync() { await StopAsync(); Disposed = true; }
    }
}
