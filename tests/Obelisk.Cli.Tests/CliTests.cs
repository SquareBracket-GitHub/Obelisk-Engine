using System.Text.Json;
using System.Xml.Linq;
using Obelisk.Cli;

namespace Obelisk.Cli.Tests;

public sealed class CliTests
{
    [Theory]
    [InlineData("MyGame")]
    [InlineData("_Game2")]
    [InlineData("Game")]
    [InlineData("Program")]
    [InlineData("System")]
    public void CreatesStandaloneProjectWithPinnedPublicPackageAndAllRequiredFiles(string name)
    {
        using var files = new Workspace();
        var (context, output, error) = files.Context();
        Assert.Equal(CliExitCodes.Success, CliApplication.Run(["new", name], context));
        var project = Path.Combine(files.Root, name);
        Assert.Equal(8, Directory.GetFiles(project, "*", SearchOption.AllDirectories).Length);
        foreach (var relative in new[] { "Program.cs", "Game.cs", $"{name}.csproj", "obelisk.json", ".gitignore", "README.md", "assets/README.txt", "plugins/README.txt" })
        {
            var content = File.ReadAllText(Path.Combine(project, relative));
            Assert.DoesNotContain("{{", content);
            Assert.DoesNotContain("ProjectReference", relative.EndsWith("csproj", StringComparison.Ordinal) ? content : "");
        }
        var xml = XDocument.Load(Path.Combine(project, $"{name}.csproj"));
        var reference = Assert.Single(xml.Descendants("PackageReference"));
        Assert.Equal("Obelisk.Vanilla", (string?)reference.Attribute("Include"));
        Assert.Equal("[0.1.0]", (string?)reference.Attribute("Version"));
        Assert.Equal("net10.0", Assert.Single(xml.Descendants("TargetFramework")).Value);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "obelisk.json")));
        Assert.Equal(name, manifest.RootElement.GetProperty("name").GetString());
        Assert.Equal($"{name}.csproj", manifest.RootElement.GetProperty("project").GetString());
        Assert.Contains($"namespace {name};", File.ReadAllText(Path.Combine(project, "Program.cs")));
        Assert.Contains("Created", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Single(Directory.GetDirectories(files.Root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../Game")]
    [InlineData("C:\\Game")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("1Game")]
    [InlineData("Game Name")]
    [InlineData("class")]
    [InlineData("new")]
    [InlineData("global")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("Game.")]
    [InlineData("Game\n")]
    [InlineData("Game\0")]
    [InlineData("게임")]
    public void InvalidNamesNeverWriteFiles(string name)
    {
        using var files = new Workspace();
        var (context, _, error) = files.Context();
        Assert.Equal(CliExitCodes.Usage, CliApplication.Run(["new", name], context));
        Assert.Contains("Invalid project name", error.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(files.Root));
    }

    [Fact]
    public void NameLengthIsBounded()
    {
        Assert.True(ProjectGenerator.IsValidName(new string('a', 64)));
        Assert.False(ProjectGenerator.IsValidName(new string('a', 65)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionPreservesExistingFileOrDirectory(bool directory)
    {
        using var files = new Workspace();
        var target = Path.Combine(files.Root, "Existing");
        if (directory) Directory.CreateDirectory(target);
        var sentinel = directory ? Path.Combine(target, "keep.txt") : target;
        File.WriteAllText(sentinel, "keep me");
        var (context, _, error) = files.Context();
        Assert.Equal(CliExitCodes.Failure, CliApplication.Run(["new", "Existing"], context));
        Assert.Contains("already exists", error.ToString());
        Assert.Equal("keep me", File.ReadAllText(sentinel));
        Assert.Single(Directory.GetFileSystemEntries(files.Root));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void PartialWriteFailureRemovesOnlyStagingDirectory(int failAt)
    {
        using var files = new Workspace();
        File.WriteAllText(Path.Combine(files.Root, "keep.txt"), "keep me");
        var count = 0;
        var generator = new ProjectGenerator((path, content) =>
        {
            File.WriteAllText(path, content);
            if (++count == failAt) throw new IOException("simulated disk failure");
        });
        var (context, _, error) = files.Context();
        Assert.Equal(CliExitCodes.Failure, CliApplication.Run(["new", "MyGame"], context, generator));
        Assert.Contains("simulated disk failure", error.ToString());
        Assert.False(Directory.Exists(Path.Combine(files.Root, "MyGame")));
        Assert.Equal("keep me", File.ReadAllText(Assert.Single(Directory.GetFileSystemEntries(files.Root))));
    }

    [Fact]
    public async Task TransientWindowsFileLockDoesNotAbortCompletedProjectGeneration()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new Workspace();
        FileStream? held = null;
        Task release = Task.CompletedTask;
        var writes = 0;
        var generator = new ProjectGenerator((path, content) =>
        {
            File.WriteAllText(path, content);
            if (++writes != 8) return;
            held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            release = Task.Run(async () => { await Task.Delay(200); held.Dispose(); });
        });
        try
        {
            var project = generator.Create(files.Root, "MyGame");
            Assert.Equal(8, Directory.GetFiles(project, "*", SearchOption.AllDirectories).Length);
            Assert.Single(Directory.GetDirectories(files.Root));
        }
        finally { await release; held?.Dispose(); }
    }

    [Fact]
    public void PersistentWindowsFileLockFailsWithoutPublishingOrTouchingExistingFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new Workspace();
        var sentinel = Path.Combine(files.Root, "keep.txt");
        File.WriteAllText(sentinel, "keep me");
        FileStream? held = null;
        var writes = 0;
        var generator = new ProjectGenerator((path, content) =>
        {
            File.WriteAllText(path, content);
            if (++writes == 8) held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        });
        try
        {
            Assert.Throws<IOException>(() => generator.Create(files.Root, "MyGame"));
            Assert.False(Directory.Exists(Path.Combine(files.Root, "MyGame")));
            Assert.Equal("keep me", File.ReadAllText(sentinel));
        }
        finally { held?.Dispose(); }
    }

    [Fact]
    public void CollisionCreatedDuringGenerationIsPreserved()
    {
        using var files = new Workspace();
        var target = Path.Combine(files.Root, "MyGame");
        var generator = new ProjectGenerator((path, content) =>
        {
            File.WriteAllText(path, content);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.txt"), "racing writer");
        });
        var (context, _, _) = files.Context();
        Assert.Equal(CliExitCodes.Failure, CliApplication.Run(["new", "MyGame"], context, generator));
        Assert.Equal("racing writer", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.Single(Directory.GetDirectories(files.Root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("new --help")]
    [InlineData("info -h")]
    public void HelpIsSuccessfulAndDoesNotGenerateFiles(string arguments)
    {
        using var files = new Workspace();
        var (context, output, error) = files.Context();
        Assert.Equal(CliExitCodes.Success, CliApplication.Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), context));
        Assert.Contains("Usage: obelisk", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(files.Root));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("new Game extra")]
    [InlineData("info extra")]
    [InlineData("unknown")]
    [InlineData("--version extra")]
    public void UsageFailuresHaveExitCodeAndActionableStderr(string arguments)
    {
        using var files = new Workspace();
        var (context, output, error) = files.Context();
        Assert.Equal(CliExitCodes.Usage, CliApplication.Run(arguments.Split(' '), context));
        Assert.Equal("", output.ToString());
        Assert.Contains("obelisk --help", error.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(files.Root));
    }

    [Fact]
    public void InfoAndVersionReportPinnedVersionsWithoutAProject()
    {
        using var files = new Workspace();
        var (context, output, error) = files.Context();
        Assert.Equal(0, CliApplication.Run(["info"], context));
        Assert.Contains("Obelisk CLI: 0.1.1", output.ToString());
        Assert.Contains("Obelisk.Vanilla [0.1.0]", output.ToString());
        Assert.Contains("Runtime:", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, CliApplication.Run(["--version"], context));
        Assert.Equal($"0.1.1{Environment.NewLine}", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void MissingWorkingDirectoryDoesNotCreateParents()
    {
        using var files = new Workspace();
        var error = new StringWriter();
        var missing = Path.Combine(files.Root, "absent");
        Assert.Equal(CliExitCodes.Failure, CliApplication.Run(["new", "MyGame"], new CliContext(missing, new StringWriter(), error)));
        Assert.False(Directory.Exists(missing));
        Assert.Contains("does not exist", error.ToString());
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "obelisk-cli-tests", Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public (CliContext Context, StringWriter Output, StringWriter Error) Context()
        {
            var output = new StringWriter(); var error = new StringWriter();
            return (new CliContext(Root, output, error), output, error);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
