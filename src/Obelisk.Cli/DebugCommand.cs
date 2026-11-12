using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Obelisk.Cli;

internal static class DebugCommand
{
    internal static string PipeName(string directory, string? session)
    {
        if (session is not null && !Regex.IsMatch(session, "\\A[a-zA-Z0-9_-]{1,48}\\z"))
            throw new ArgumentException("Session must contain 1-48 ASCII letters, digits, underscores or hyphens.");
        var identity = session is null ? "project:" + Path.GetFullPath(directory) : "session:" + session;
        return "obd-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\n" + identity)))[..24];
    }

    internal static async Task<int> RunAsync(string[] args, CliContext context, CancellationToken cancellation)
    {
        string? session = null;
        if (args is ["--session", var name, ..]) { session = name; args = args[2..]; }
        if (args.Length == 0 || args is ["help" or "--help" or "-h"])
        {
            context.Output.WriteLine("obelisk debug [--session name] status | pause | resume | step [1-600] | click x y [Left|Right|Middle] | mouse move x y | mouse down/up button | key down/up/press key | screenshot file.png | quit");
            context.Output.WriteLine("Coordinates are drawable output pixels from the top-left. Input commands advance paused frames (one for down/up/move, two for click/press). Step uses 1/60 s per frame. Responses acknowledge completed frames. Screenshot captures the last presented frame without advancing simulation.");
            return 0;
        }
        if (args[0] == "screenshot" && args.Length != 2) { context.Error.WriteLine("Use screenshot <file.png>."); return 2; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName(context.WorkingDirectory, session), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            var wireArgs = args[0] == "screenshot" ? new[] { "screenshot" } : args;
            var payload = JsonSerializer.Serialize(wireArgs);
            if (payload.Length > 4096) throw new ArgumentException("Debug command exceeds 4096 characters.");
            await writer.WriteLineAsync(payload.AsMemory(), timeout.Token).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) ?? throw new IOException("Debug session closed without a response.");
            if (line.Length > 96 * 1024 * 1024) throw new InvalidDataException("Debug response is too large.");
            using var response = JsonDocument.Parse(line);
            var root = response.RootElement;
            if (!root.GetProperty("ok").GetBoolean()) { context.Error.WriteLine(root.GetProperty("error").GetString()); return 1; }
            if (args[0] == "screenshot")
            {
                var path = Path.GetFullPath(args[1], context.WorkingDirectory);
                // Avoid replacing an existing artifact accidentally.
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                var pixels = Convert.FromBase64String(root.GetProperty("png").GetString()!);
                await output.WriteAsync(pixels, timeout.Token).ConfigureAwait(false);
                context.Output.WriteLine($"Saved screenshot: {path}");
            }
            else context.Output.WriteLine(line);
            return 0;
        }
        catch (TimeoutException) { context.Error.WriteLine("No debug session connected. Start obelisk run --debug-control and use the same project/session."); return 1; }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { context.Error.WriteLine("Debug command timed out; inspect status before retrying a command that may already have been applied."); return 1; }
        catch (JsonException error) { context.Error.WriteLine($"Invalid debug response: {error.Message}"); return 1; }
    }
}
