namespace Obelisk.Cli;

public sealed class BuildCommand(IProcessRunner? runner = null)
{
    public const string OutputDirectory = "artifacts/build/win-x64";
    private const string Marker = ".obelisk-build.txt";
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task<int> RunAsync(CliContext context, CancellationToken cancellation)
    {
        var manifest = ProjectManifest.Read(context.WorkingDirectory);
        using var operation = ProjectOperation.Acquire(context.WorkingDirectory);
        var output = ProjectManifest.Resolve(context.WorkingDirectory, OutputDirectory);
        var owner = $"Obelisk.Cli {CliApplication.Version}\n{manifest.Project}\n";
        CheckOutput(output, owner);
        var assets = ProjectFiles.ContentRoot(context.WorkingDirectory, manifest.Assets);
        var plugins = ProjectFiles.ContentRoot(context.WorkingDirectory, manifest.Plugins);
        if (Directory.Exists(assets)) ProjectFiles.ValidateTree(assets);
        _ = PluginCommands.Discover(plugins);
        var staging = ProjectManifest.Resolve(context.WorkingDirectory, $"artifacts/.obelisk-publish-{Guid.NewGuid():N}");
        var backup = ProjectManifest.Resolve(context.WorkingDirectory, $"artifacts/.obelisk-backup-{Guid.NewGuid():N}");
        var published = false;
        try
        {
            Directory.CreateDirectory(staging);
            string[] args = ["publish", ProjectManifest.Resolve(context.WorkingDirectory, manifest.Project),
                "-c", "Release", "-r", "win-x64", "--self-contained", "true", "--nologo", "--output", staging,
                "-p:PublishSingleFile=false", "-p:PublishTrimmed=false", "-p:PublishAot=false", "-p:UseAppHost=true"];
            int exit;
            await using (var child = _runner.Start(new ProcessCommand("dotnet", args, context.WorkingDirectory), context.Output, context.Error))
                exit = await child.Completion.WaitAsync(cancellation).ConfigureAwait(false);
            if (exit != 0) return exit;
            cancellation.ThrowIfCancellationRequested();
            ProjectFiles.ValidateTree(staging);
            foreach (var file in new[] { "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll" })
                if (!File.Exists(Path.Combine(staging, file))) throw new InvalidDataException($"Publish output lacks '{file}'; expected a Windows x64 self-contained application.");
            if (!Directory.EnumerateFiles(staging, "*.exe").Any()) throw new InvalidDataException("Publish output has no executable apphost.");
            foreach (var (source, name) in new[] { (assets, "assets"), (plugins, "plugins") })
            {
                var target = ProjectManifest.Resolve(staging, name);
                ProjectFiles.DeleteTree(target);
                if (Directory.Exists(source)) ProjectFiles.CopyTree(source, target);
            }
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staging, Marker), owner);
            CheckOutput(output, owner);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (Directory.Exists(output)) Directory.Move(output, backup);
            try { Directory.Move(staging, output); }
            catch { if (Directory.Exists(backup) && !Path.Exists(output)) Directory.Move(backup, output); throw; }
            published = true;
            context.Output.WriteLine($"Built Windows x64 self-contained game at '{output}'. Copy this whole folder; run its .exe without installing .NET.");
            return 0;
        }
        finally
        {
            // A failed rollback must never delete the user's previous distribution.
            if (!published && Directory.Exists(backup)) context.Error.WriteLine($"obelisk: Previous build preserved at '{backup}'; recover it before retrying.");
            foreach (var directory in published ? new[] { staging, backup } : new[] { staging })
            {
                try { ProjectFiles.DeleteTree(directory); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                { context.Error.WriteLine($"obelisk: Could not clean owned staging directory '{directory}': {error.Message}. Stop the game and remove this leftover directory."); }
            }
        }
    }

    private static void CheckOutput(string output, string owner)
    {
        if (!Path.Exists(output)) return;
        ProjectFiles.ValidateTree(output);
        var marker = Path.Combine(output, Marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 2048 || File.ReadAllText(marker) != owner)
            throw new IOException($"Output '{output}' is not owned by this CLI/project. Move the existing directory before building; its files were preserved.");
    }
}
