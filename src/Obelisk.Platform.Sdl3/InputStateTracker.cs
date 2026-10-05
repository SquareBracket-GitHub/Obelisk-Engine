using Obelisk.Abstractions;

namespace Obelisk.Platform.Sdl3;

internal sealed class InputStateTracker
{
    private readonly HashSet<Key> _keysDown = [];
    private readonly HashSet<Key> _keysPressed = [];
    private readonly HashSet<Key> _keysReleased = [];
    private readonly HashSet<MouseButton> _mouseButtonsDown = [];
    private readonly HashSet<MouseButton> _mouseButtonsPressed = [];
    private readonly HashSet<MouseButton> _mouseButtonsReleased = [];
    private float _mouseX;
    private float _mouseY;

    public void BeginFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mouseButtonsPressed.Clear();
        _mouseButtonsReleased.Clear();
    }

    public void Apply(Sdl3PlatformEvent platformEvent)
    {
        switch (platformEvent.Type)
        {
            case Sdl3EventType.KeyDown when platformEvent.Key is { } key:
                if (_keysDown.Add(key)) _keysPressed.Add(key);
                break;
            case Sdl3EventType.KeyUp when platformEvent.Key is { } key:
                if (_keysDown.Remove(key)) _keysReleased.Add(key);
                break;
            case Sdl3EventType.MouseMoved:
                SetMousePosition(platformEvent);
                break;
            case Sdl3EventType.MouseButtonDown when platformEvent.MouseButton is { } button:
                SetMousePosition(platformEvent);
                if (_mouseButtonsDown.Add(button)) _mouseButtonsPressed.Add(button);
                break;
            case Sdl3EventType.MouseButtonUp when platformEvent.MouseButton is { } button:
                SetMousePosition(platformEvent);
                if (_mouseButtonsDown.Remove(button)) _mouseButtonsReleased.Add(button);
                break;
        }
    }

    public InputSnapshot Capture() => new(
        new KeyboardSnapshot(_keysDown, _keysPressed, _keysReleased),
        new MouseSnapshot(_mouseX, _mouseY, _mouseButtonsDown,
            _mouseButtonsPressed, _mouseButtonsReleased));

    private void SetMousePosition(Sdl3PlatformEvent platformEvent)
    {
        _mouseX = platformEvent.MouseX;
        _mouseY = platformEvent.MouseY;
    }
}
