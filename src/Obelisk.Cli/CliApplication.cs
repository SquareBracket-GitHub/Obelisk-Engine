using System.Runtime.InteropServices;

namespace Obelisk.Cli;

public sealed record CliContext(string WorkingDirectory, TextWriter Output, TextWriter Error);

public static class CliExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
    public const int Interrupted = 130;
}

public static class CliApplication
{
    public const string Version = "0.3.0-alpha261112";
    // Schema-1 project compatibility contract; package releases use Version.
    public const string EngineVersion = "0.2.0";
    private const string Help = """
        Obelisk CLI 0.3.0-alpha261112
        Usage: obelisk <command>

          new <name>    Create a .NET 10 game in a new child directory.
          run [-- args] Restore, build and run the current project.
          dev [-- args] Rebuild/restart after debounced file changes.
          build [--runtime <rid>] Publish self-contained win-x64, osx-arm64 or osx-x64.
          debug [--session name] <command> Control an opted-in local game; debug help lists commands.
          run/dev --debug-control [--session name] [-- args] Enable local debug control.
          add <path>    Install a local Plugin package directory (game must be stopped).
          remove <name> Remove an installed Plugin (game must be stopped).
          list          List installed local Plugins and versions.
          info          Show CLI, template and runtime versions.
          help          Show this help (--help or -h also work).
          --version     Show CLI version.

        Names: 1-64 ASCII letters, digits or underscores; start with a letter or underscore.
        C# keywords and Windows device names are reserved. Existing paths are never overwritten.
        Exit codes: 0 success, 1 operation failed, 2 invalid arguments, 130 interrupted; game/build codes propagate.
        """;

    public static int Run(string[] args, CliContext context, ProjectGenerator? generator = null)
        => RunAsync(args, context, generator).GetAwaiter().GetResult();

    public static async Task<int> RunAsync(string[] args, CliContext context, ProjectGenerator? generator = null,
        DevelopmentCommands? development = null, CancellationToken cancellation = default, BuildCommand? build = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            if (args.Length == 0 || args is ["help" or "--help" or "-h"] || args is ["new" or "info" or "run" or "dev" or "build" or "add" or "remove" or "list", "--help" or "-h"])
            {
                context.Output.WriteLine(Help);
                return CliExitCodes.Success;
            }
            if (args is ["--version"])
            {
                context.Output.WriteLine(Version);
                return CliExitCodes.Success;
            }
            if (args is ["info"])
            {
                context.Output.WriteLine($"Obelisk CLI: {Version}");
                context.Output.WriteLine($"Template: .NET 10 / Obelisk.Vanilla [{Version}] (project contract {EngineVersion})");
                context.Output.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
                context.Output.WriteLine($"Platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
                return CliExitCodes.Success;
            }
            if (args is ["new", var name])
            {
                if (!ProjectGenerator.IsValidName(name)) return Usage(context, $"Invalid project name '{name}'. See 'obelisk new --help'.");
                var path = (generator ?? new ProjectGenerator()).Create(context.WorkingDirectory, name);
                context.Output.WriteLine($"Created '{name}' at '{path}'.");
                context.Output.WriteLine($"Next: cd {name}, then dotnet restore, dotnet build, dotnet run.");
                return CliExitCodes.Success;
            }
            if (args[0] == "debug") return await DebugCommand.RunAsync(args[1..], context, cancellation).ConfigureAwait(false);
            if (args[0] is "run" or "dev")
            {
                if (args.Length > 1 && args[1] == "--debug-control")
                {
                    var offset = 2;
                    string? session = null;
                    if (args.Length > 3 && args[2] == "--session") { session = args[3]; offset = 4; }
                    if (args.Length > offset && args[offset] != "--") return Usage(context, "Use run/dev --debug-control [--session name] [-- game arguments].");
                    var pipe = DebugCommand.PipeName(context.WorkingDirectory, session);
                    context.Output.WriteLine($"Debug control enabled. Session: {session ?? "project"}. Use obelisk debug from this project directory.");
                    return await (development ?? new DevelopmentCommands()).RunAsync(context,
                        args.Length > offset ? args[(offset + 1)..] : [], args[0] == "dev", cancellation, pipe).ConfigureAwait(false);
                }
                if (args.Length > 1 && args[1] != "--") return Usage(context, "Use 'obelisk run/dev -- <game arguments>'. Run 'obelisk --help'.");
                return await (development ?? new DevelopmentCommands()).RunAsync(context,
                    args.Length > 1 ? args[2..] : [], args[0] == "dev", cancellation).ConfigureAwait(false);
            }
            if (args is ["build"]) return await (build ?? new BuildCommand()).RunAsync(context, cancellation).ConfigureAwait(false);
            if (args is ["build", "--runtime", var runtime])
            {
                if (!BuildCommand.IsSupportedRuntime(runtime)) return Usage(context, "Supported runtimes: win-x64, osx-arm64, osx-x64.");
                return await (build ?? new BuildCommand()).RunAsync(context, cancellation, runtime).ConfigureAwait(false);
            }
            if (args is ["list"]) return new PluginCommands().Run(context, "list");
            if (args is ["add" or "remove", var value])
            {
                if (string.IsNullOrWhiteSpace(value) || (args[0] == "remove" && !PluginCommands.IsValidName(value)))
                    return Usage(context, "Use 'obelisk add <local-path>' or 'obelisk remove <manifest-name>', without a path for removal.");
                return new PluginCommands().Run(context, args[0], value);
            }
            return Usage(context, "Unknown command or invalid arguments. Run 'obelisk --help'.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { context.Output.WriteLine("Stopped. Child processes released."); return CliExitCodes.Interrupted; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            context.Error.WriteLine($"obelisk: {error.Message}");
            return CliExitCodes.Failure;
        }
    }

    private static int Usage(CliContext context, string message)
    {
        context.Error.WriteLine($"obelisk: {message}");
        return CliExitCodes.Usage;
    }
}
