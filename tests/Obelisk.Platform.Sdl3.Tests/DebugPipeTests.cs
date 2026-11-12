using System.IO.Pipes;
using System.Text;
using Obelisk.Platform.Sdl3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed class DebugPipeTests
{
    [Fact]
    public async Task MalformedClientDoesNotKillListenerAndNextCommandIsQueued()
    {
        var name = "obd-" + Guid.NewGuid().ToString("N")[..24];
        using var server = new DebugPipeServer(name);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(stop.Token);
            await client.WriteAsync(Encoding.UTF8.GetBytes("not-json\n"), stop.Token);
            var one = new byte[1];
            Assert.Equal(0, await client.ReadAsync(one, stop.Token));
        }
        await using var next = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await next.ConnectAsync(stop.Token);
        await next.WriteAsync(Encoding.UTF8.GetBytes("[\"status\"]\n"), stop.Token);
        while (server.Requests.IsEmpty) await Task.Delay(5, stop.Token);
        Assert.True(server.Requests.TryDequeue(out var request));
        Assert.Equal(new[] { "status" }, request.Arguments);
        request.Response.SetResult("{\"ok\":true}");
        using var reader = new StreamReader(next);
        Assert.Equal("{\"ok\":true}", await reader.ReadLineAsync(stop.Token));
        await request.Sent.Task.WaitAsync(stop.Token);
    }
}
