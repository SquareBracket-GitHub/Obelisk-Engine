namespace Obelisk.Cli;

public sealed class DevelopmentCommands(IProcessRunner? runner = null, Func<CliContext, IChangeMonitor>? monitorFactory = null)
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task<int> RunAsync(CliContext context, string[] arguments, bool watch, CancellationToken cancellation)
    {
        context = context with { Output = TextWriter.Synchronized(context.Output), Error = TextWriter.Synchronized(context.Error) };
        var manifest = ProjectManifest.Read(context.WorkingDirectory);
        if (!watch)
        {
            var code = await Prepare(context, manifest, cancellation).ConfigureAwait(false);
            if (code != 0) return code;
            await using var game = StartGame(context, manifest, arguments);
            return await game.Completion.WaitAsync(cancellation).ConfigureAwait(false);
        }

        using var changes = monitorFactory?.Invoke(context) ?? new FileChangeMonitor(context.WorkingDirectory, context.Error);
        changes.Configure(manifest);
        context.Output.WriteLine("Watching project files. Press Ctrl+C to stop.");
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var revision = changes.Revision;
            var changed = changes.WaitForChangeAsync(cancellation);
            IChildProcess? game = null;
            try
            {
                try
                {
                    manifest = ProjectManifest.Read(context.WorkingDirectory);
                    changes.Configure(manifest);
                    var code = await Prepare(context, manifest, cancellation).ConfigureAwait(false);
                    if (code == 0 && changes.Revision == revision)
                    {
                        game = StartGame(context, manifest, arguments);
                        var completed = await Task.WhenAny(changed, game.Completion).ConfigureAwait(false);
                        if (completed == game.Completion)
                        {
                            var exit = await game.Completion.ConfigureAwait(false);
                            context.Output.WriteLine($"Game exited with code {exit}. Waiting for changes.");
                            await game.DisposeAsync().ConfigureAwait(false);
                            game = null;
                        }
                    }
                    else if (code != 0) context.Error.WriteLine($"obelisk: Preparation failed with code {code}. Fix the project and save to retry.");
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { context.Error.WriteLine($"obelisk: {error.Message} Waiting for changes."); }
                await changed.ConfigureAwait(false);
            }
            finally { if (game is not null) await game.DisposeAsync().ConfigureAwait(false); }
            context.Output.WriteLine("Changes detected. Previous process stopped; rebuilding.");
        }
    }

    private async Task<int> Prepare(CliContext context, ProjectManifest manifest, CancellationToken cancellation)
    {
        var project = ProjectManifest.Resolve(context.WorkingDirectory, manifest.Project);
        foreach (var args in new[] { new[] { "restore", project, "--nologo" }, new[] { "build", project, "--no-restore", "--nologo" } })
        {
            cancellation.ThrowIfCancellationRequested();
            await using var child = _runner.Start(new ProcessCommand("dotnet", args, context.WorkingDirectory), context.Output, context.Error);
            var exit = await child.Completion.WaitAsync(cancellation).ConfigureAwait(false);
            if (exit != 0) return exit;
        }
        return 0;
    }

    private IChildProcess StartGame(CliContext context, ProjectManifest manifest, string[] arguments)
    {
        var args = new List<string> { "run", "--project", ProjectManifest.Resolve(context.WorkingDirectory, manifest.Project), "--no-build", "--no-restore", "--" };
        args.AddRange(arguments);
        var game = _runner.Start(new ProcessCommand("dotnet", args, context.WorkingDirectory), context.Output, context.Error);
        context.Output.WriteLine($"Game started (process {game.Id}).");
        return game;
    }
}
