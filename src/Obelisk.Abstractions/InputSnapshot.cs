namespace Obelisk.Abstractions;

/// <summary>Immutable keyboard and mouse state captured for one game-loop frame.</summary>
public sealed class InputSnapshot
{
    public static InputSnapshot Empty { get; } = new(
        new KeyboardSnapshot([], [], []),
        new MouseSnapshot(0, 0, [], [], []));

    public InputSnapshot(KeyboardSnapshot keyboard, MouseSnapshot mouse)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(mouse);
        Keyboard = keyboard;
        Mouse = mouse;
    }

    public KeyboardSnapshot Keyboard { get; }
    public MouseSnapshot Mouse { get; }
}

public sealed class KeyboardSnapshot
{
    private readonly HashSet<Key> _down;
    private readonly HashSet<Key> _pressed;
    private readonly HashSet<Key> _released;

    public KeyboardSnapshot(IEnumerable<Key> down, IEnumerable<Key> pressed, IEnumerable<Key> released)
    {
        _down = new HashSet<Key>(down);
        _pressed = new HashSet<Key>(pressed);
        _released = new HashSet<Key>(released);
    }

    public bool IsDown(Key key) => _down.Contains(key);
    public bool WasPressed(Key key) => _pressed.Contains(key);
    public bool WasReleased(Key key) => _released.Contains(key);
}

public sealed class MouseSnapshot
{
    private readonly HashSet<MouseButton> _down;
    private readonly HashSet<MouseButton> _pressed;
    private readonly HashSet<MouseButton> _released;

    public MouseSnapshot(float x, float y, IEnumerable<MouseButton> down,
        IEnumerable<MouseButton> pressed, IEnumerable<MouseButton> released)
    {
        X = x;
        Y = y;
        _down = new HashSet<MouseButton>(down);
        _pressed = new HashSet<MouseButton>(pressed);
        _released = new HashSet<MouseButton>(released);
    }

    public float X { get; }
    public float Y { get; }
    public bool IsDown(MouseButton button) => _down.Contains(button);
    public bool WasPressed(MouseButton button) => _pressed.Contains(button);
    public bool WasReleased(MouseButton button) => _released.Contains(button);
}

public enum Key
{
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Alpha0, Alpha1, Alpha2, Alpha3, Alpha4,
    Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
    Left, Right, Up, Down,
    Space, Enter, Escape, Tab, Backspace,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
}

public enum MouseButton
{
    Left,
    Middle,
    Right,
    X1,
    X2,
}
