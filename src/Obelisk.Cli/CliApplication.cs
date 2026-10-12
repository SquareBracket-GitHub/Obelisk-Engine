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
    public const string Version = "0.1.0";
    public const string EngineVersion = "0.1.0";
    private const string Help = """
        Obelisk CLI 0.1.0
        Usage: obelisk <command>

          new <name>    Create a .NET 10 game in a new child directory.
          run [-- args] Restore, build and run the current project.
          dev [-- args] Rebuild/restart after debounced file changes.
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
        DevelopmentCommands? development = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            if (args.Length == 0 || args is ["help" or "--help" or "-h"] || args is ["new" or "info" or "run" or "dev", "--help" or "-h"])
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
                context.Output.WriteLine($"Template: .NET 10 / Obelisk.Vanilla [{EngineVersion}]");
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
            if (args[0] is "run" or "dev")
            {
                if (args.Length > 1 && args[1] != "--") return Usage(context, "Use 'obelisk run/dev -- <game arguments>'. Run 'obelisk --help'.");
                return await (development ?? new DevelopmentCommands()).RunAsync(context,
                    args.Length > 1 ? args[2..] : [], args[0] == "dev", cancellation).ConfigureAwait(false);
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
