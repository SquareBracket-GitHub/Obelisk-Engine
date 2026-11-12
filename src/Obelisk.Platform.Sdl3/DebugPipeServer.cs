using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Obelisk.Platform.Sdl3;

internal sealed class DebugPipeServer : IDisposable
{
    internal sealed record Request(string[] Arguments, TaskCompletionSource<string> Response, TaskCompletionSource Sent, CancellationToken Cancellation);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    internal ConcurrentQueue<Request> Requests { get; } = new();
    internal DebugPipeServer(string name)
    {
        // Bind synchronously: a conflicting or unusable endpoint fails startup explicitly.
        var first = CreatePipe(name);
        _listener = Listen(name, first);
    }
    private static NamedPipeServerStream CreatePipe(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    private async Task Listen(string name, NamedPipeServerStream? first)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await using var pipe = first ?? CreatePipe(name);
                first = null;
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    var text = new StringBuilder();
                    var character = new char[1];
                    while (text.Length <= 4096)
                    {
                        if (await reader.ReadAsync(character.AsMemory(), timeout.Token).ConfigureAwait(false) == 0) throw new IOException("Connection closed.");
                        if (character[0] == '\n') break;
                        text.Append(character[0]);
                    }
                    if (text.Length > 4096) throw new InvalidDataException("Command exceeds 4096 characters.");
                    var args = JsonSerializer.Deserialize<string[]>(text.ToString()) ?? throw new InvalidDataException("Expected a command array.");
                    if (args.Length == 0 || args.Any(a => a is null)) throw new InvalidDataException("Expected non-null command arguments.");
                    var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Requests.Enqueue(new(args, result, sent, timeout.Token));
                    var response = await result.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    try { await writer.WriteLineAsync(response.AsMemory(), timeout.Token).ConfigureAwait(false); }
                    finally { sent.TrySetResult(); }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or OperationCanceledException or ArgumentException)
                { /* A malformed/disconnected local client must not stop the game. */ }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { Console.Error.WriteLine($"obelisk debug listener stopped: {error.Message}"); }
    }
    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
