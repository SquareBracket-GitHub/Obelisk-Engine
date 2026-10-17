namespace Obelisk.Abstractions;

/// <summary>Immutable keyboard and mouse state captured for one game-loop frame.</summary>
public sealed class InputSnapshot
{
    /// <summary>Gets an immutable snapshot with no keys/buttons down and mouse position zero.</summary>
    public static InputSnapshot Empty { get; } = new(
        new KeyboardSnapshot([], [], []),
        new MouseSnapshot(0, 0, [], [], []));

    /// <summary>Borrows nonnull immutable keyboard and mouse snapshots.</summary>
    public InputSnapshot(KeyboardSnapshot keyboard, MouseSnapshot mouse)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(mouse);
        Keyboard = keyboard;
        Mouse = mouse;
    }

    /// <summary>Gets this frame's keyboard snapshot.</summary>
    public KeyboardSnapshot Keyboard { get; }
    /// <summary>Gets this frame's mouse snapshot.</summary>
    public MouseSnapshot Mouse { get; }
}

/// <summary>Stores independent copies of held, newly pressed and newly released keys for one frame.</summary>
public sealed class KeyboardSnapshot
{
    private readonly HashSet<Key> _down;
    private readonly HashSet<Key> _pressed;
    private readonly HashSet<Key> _released;

    /// <summary>Copies the three supplied key collections; edge states last one frame.</summary>
    public KeyboardSnapshot(IEnumerable<Key> down, IEnumerable<Key> pressed, IEnumerable<Key> released)
    {
        _down = new HashSet<Key>(down);
        _pressed = new HashSet<Key>(pressed);
        _released = new HashSet<Key>(released);
    }

    /// <summary>Tests whether the key or mouse button is held in this snapshot.</summary>
    public bool IsDown(Key key) => _down.Contains(key);
    /// <summary>Tests whether a down edge was captured in this frame, excluding key repeat.</summary>
    public bool WasPressed(Key key) => _pressed.Contains(key);
    /// <summary>Tests whether an up edge was captured in this frame, including focus-loss release.</summary>
    public bool WasReleased(Key key) => _released.Contains(key);
}

/// <summary>Stores output-space mouse position and copied button states for one frame.</summary>
public sealed class MouseSnapshot
{
    private readonly HashSet<MouseButton> _down;
    private readonly HashSet<MouseButton> _pressed;
    private readonly HashSet<MouseButton> _released;

    /// <summary>Copies button states and stores the supplied output-space mouse position.</summary>
    public MouseSnapshot(float x, float y, IEnumerable<MouseButton> down,
        IEnumerable<MouseButton> pressed, IEnumerable<MouseButton> released)
    {
        X = x;
        Y = y;
        _down = new HashSet<MouseButton>(down);
        _pressed = new HashSet<MouseButton>(pressed);
        _released = new HashSet<MouseButton>(released);
    }

    /// <summary>Gets the mouse's horizontal output-space coordinate.</summary>
    public float X { get; }
    /// <summary>Gets the mouse's vertical output-space coordinate.</summary>
    public float Y { get; }
    /// <summary>Tests whether the key or mouse button is held in this snapshot.</summary>
    public bool IsDown(MouseButton button) => _down.Contains(button);
    /// <summary>Tests whether a down edge was captured in this frame, excluding key repeat.</summary>
    public bool WasPressed(MouseButton button) => _pressed.Contains(button);
    /// <summary>Tests whether an up edge was captured in this frame, including focus-loss release.</summary>
    public bool WasReleased(MouseButton button) => _released.Contains(button);
}

/// <summary>Identifies the keyboard keys mapped by the v0.1 backend, without native key codes.</summary>
public enum Key
{
    /// <summary>The A key.</summary>
    A,
    /// <summary>The B key.</summary>
    B,
    /// <summary>The C key.</summary>
    C,
    /// <summary>The D key.</summary>
    D,
    /// <summary>The E key.</summary>
    E,
    /// <summary>The F key.</summary>
    F,
    /// <summary>The G key.</summary>
    G,
    /// <summary>The H key.</summary>
    H,
    /// <summary>The I key.</summary>
    I,
    /// <summary>The J key.</summary>
    J,
    /// <summary>The K key.</summary>
    K,
    /// <summary>The L key.</summary>
    L,
    /// <summary>The M key.</summary>
    M,
    /// <summary>The N key.</summary>
    N,
    /// <summary>The O key.</summary>
    O,
    /// <summary>The P key.</summary>
    P,
    /// <summary>The Q key.</summary>
    Q,
    /// <summary>The R key.</summary>
    R,
    /// <summary>The S key.</summary>
    S,
    /// <summary>The T key.</summary>
    T,
    /// <summary>The U key.</summary>
    U,
    /// <summary>The V key.</summary>
    V,
    /// <summary>The W key.</summary>
    W,
    /// <summary>The X key.</summary>
    X,
    /// <summary>The Y key.</summary>
    Y,
    /// <summary>The Z key.</summary>
    Z,
    /// <summary>The digit 0 key.</summary>
    Alpha0,
    /// <summary>The digit 1 key.</summary>
    Alpha1,
    /// <summary>The digit 2 key.</summary>
    Alpha2,
    /// <summary>The digit 3 key.</summary>
    Alpha3,
    /// <summary>The digit 4 key.</summary>
    Alpha4,
    /// <summary>The digit 5 key.</summary>
    Alpha5,
    /// <summary>The digit 6 key.</summary>
    Alpha6,
    /// <summary>The digit 7 key.</summary>
    Alpha7,
    /// <summary>The digit 8 key.</summary>
    Alpha8,
    /// <summary>The digit 9 key.</summary>
    Alpha9,
    /// <summary>The Left key.</summary>
    Left,
    /// <summary>The Right key.</summary>
    Right,
    /// <summary>The Up key.</summary>
    Up,
    /// <summary>The Down key.</summary>
    Down,
    /// <summary>The Space key.</summary>
    Space,
    /// <summary>The Enter key.</summary>
    Enter,
    /// <summary>The Escape key.</summary>
    Escape,
    /// <summary>The Tab key.</summary>
    Tab,
    /// <summary>The Backspace key.</summary>
    Backspace,
    /// <summary>The LeftShift key.</summary>
    LeftShift,
    /// <summary>The RightShift key.</summary>
    RightShift,
    /// <summary>The LeftControl key.</summary>
    LeftControl,
    /// <summary>The RightControl key.</summary>
    RightControl,
    /// <summary>The LeftAlt key.</summary>
    LeftAlt,
    /// <summary>The RightAlt key.</summary>
    RightAlt,
}

/// <summary>Identifies mapped mouse buttons without exposing platform values.</summary>
public enum MouseButton
{
    /// <summary>The Left mouse button.</summary>
    Left,
    /// <summary>The Middle mouse button.</summary>
    Middle,
    /// <summary>The Right mouse button.</summary>
    Right,
    /// <summary>The X1 mouse button.</summary>
    X1,
    /// <summary>The X2 mouse button.</summary>
    X2,
}
