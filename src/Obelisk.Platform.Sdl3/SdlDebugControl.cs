using System.Globalization;
using System.Text.Json;
using Obelisk.Abstractions;

namespace Obelisk.Platform.Sdl3;

internal sealed class SdlDebugControl(string pipeName) : IDisposable
{
    private readonly DebugPipeServer _server = new(pipeName);
    private DebugPipeServer.Request? _pending;
    private DebugPipeServer.Request? _quit;
    private int _remaining;
    private Sdl3PlatformEvent? _initial;
    private Sdl3PlatformEvent? _release;
    internal bool Paused { get; private set; }
    internal bool ShouldRunFrame => _quit is null && (!Paused || _remaining > 0);
    internal bool QuitRequested => _quit is not null && (_quit.Sent.Task.IsCompleted || _quit.Cancellation.IsCancellationRequested);
    internal ulong CompletedFrames { get; private set; }
    private TimeSpan _elapsed;

    internal void Pump(Sdl3ApplicationPlatform platform, Func<byte[]> screenshot)
    {
        if (_pending is not null || _quit is not null) return;
        while (_server.Requests.TryDequeue(out var request))
        {
            if (request.Cancellation.IsCancellationRequested) continue;
            try
            {
                var args = request.Arguments;
                switch (args)
                {
                    case ["status"]: Reply(request, platform); break;
                    case ["pause"]: Paused = true; Reply(request, platform); break;
                    case ["resume"]: Paused = false; Reply(request, platform); break;
                    case ["quit"]: _quit = request; Reply(request, platform); return;
                    case ["screenshot"]:
                        request.Response.TrySetResult(JsonSerializer.Serialize(new { ok = true, completedFrames = CompletedFrames, png = Convert.ToBase64String(screenshot()) }));
                        break;
                    case ["step"]: Step(request, 1); return;
                    case ["step", var count]:
                        if (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var frames) || frames is < 1 or > 600)
                            throw new ArgumentException("Step count must be 1-600.");
                        Step(request, frames); return;
                    case ["click", var x, var y]: Click(request, platform, x, y, MouseButton.Left); return;
                    case ["click", var x, var y, var button]: Click(request, platform, x, y, Parse<MouseButton>(button)); return;
                    case ["mouse", "move", var x, var y]:
                        var (mx, my) = Position(platform, x, y);
                        Input(request, new(Sdl3EventType.MouseMoved, MouseX: mx, MouseY: my)); return;
                    case ["mouse", var action, var button] when action is "down" or "up":
                        Input(request, new(action == "down" ? Sdl3EventType.MouseButtonDown : Sdl3EventType.MouseButtonUp,
                            MouseButton: Parse<MouseButton>(button), MouseX: platform.Input.Mouse.X, MouseY: platform.Input.Mouse.Y)); return;
                    case ["key", var action, var keyText] when action is "down" or "up" or "press":
                        var key = Parse<Key>(keyText);
                        if (key == Key.Escape) throw new ArgumentException("Use debug quit to stop the game; Escape injection is not supported.");
                        var down = new Sdl3PlatformEvent(Sdl3EventType.KeyDown, Key: key);
                        var up = new Sdl3PlatformEvent(Sdl3EventType.KeyUp, Key: key);
                        if (action == "press" && platform.Input.Keyboard.IsDown(key)) throw new ArgumentException("Release the held key before pressing it.");
                        Input(request, action == "up" ? up : down, action == "press" ? up : null); return;
                    default: throw new ArgumentException("Unknown debug command. Run obelisk debug help.");
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or NotSupportedException or Sdl3Exception)
            { request.Response.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = error.Message })); }
        }
    }
    private static T Parse<T>(string text) where T : struct, Enum =>
        Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(value) && !int.TryParse(text, out _)
            ? value : throw new ArgumentException($"Unknown {typeof(T).Name}: {text}.");
    private static (float, float) Position(Sdl3ApplicationPlatform platform, string x, string y)
    {
        if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var mx) ||
            !float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var my) || !float.IsFinite(mx) || !float.IsFinite(my) ||
            mx < 0 || my < 0 || mx >= platform.OutputSize.Width || my >= platform.OutputSize.Height)
            throw new ArgumentException("Coordinates must be finite drawable output pixels inside the window.");
        return (mx, my);
    }
    private void Click(DebugPipeServer.Request request, Sdl3ApplicationPlatform platform, string x, string y, MouseButton button)
    {
        if (platform.Input.Mouse.IsDown(button)) throw new ArgumentException("Release the held mouse button before clicking it.");
        var (mx, my) = Position(platform, x, y);
        Input(request, new(Sdl3EventType.MouseButtonDown, MouseButton: button, MouseX: mx, MouseY: my),
            new(Sdl3EventType.MouseButtonUp, MouseButton: button, MouseX: mx, MouseY: my));
    }
    private void Step(DebugPipeServer.Request request, int frames)
    {
        if (!Paused) throw new ArgumentException("Pause before stepping frames.");
        _pending = request; _remaining = frames;
    }
    private void Input(DebugPipeServer.Request request, Sdl3PlatformEvent initial, Sdl3PlatformEvent? release = null)
    {
        _pending = request; _remaining = release.HasValue ? 2 : 1; _initial = initial; _release = release;
    }
    internal void ApplyInput(InputStateTracker tracker)
    {
        if (!ShouldRunFrame) return;
        if (_initial is { } initial) { tracker.Apply(initial); _initial = null; }
        else if (_release is { } release) { tracker.Apply(release); _release = null; }
    }
    internal void FrameCompleted(GameTime time, Sdl3ApplicationPlatform platform)
    {
        CompletedFrames++; _elapsed = time.Elapsed;
        if (_pending is not null && --_remaining == 0)
        {
            Reply(_pending, platform); _pending = null;
        }
    }
    private void Reply(DebugPipeServer.Request request, Sdl3ApplicationPlatform platform) => request.Response.TrySetResult(JsonSerializer.Serialize(new
    {
        ok = true,
        completedFrames = CompletedFrames,
        elapsedSeconds = _elapsed.TotalSeconds,
        paused = Paused,
        processId = Environment.ProcessId,
        width = platform.OutputSize.Width,
        height = platform.OutputSize.Height,
        displayScale = platform.DisplayScale,
        sampleCount = platform.SampleCount,
        coordinates = "drawable-pixels-top-left",
        keysDown = Enum.GetValues<Key>().Where(platform.Input.Keyboard.IsDown).Select(k => k.ToString()),
        mouse = new
        {
            x = platform.Input.Mouse.X,
            y = platform.Input.Mouse.Y,
            buttonsDown = Enum.GetValues<MouseButton>().Where(platform.Input.Mouse.IsDown).Select(b => b.ToString())
        }
    }));
    public void Dispose() => _server.Dispose();
}
