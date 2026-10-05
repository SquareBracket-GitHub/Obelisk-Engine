using System.Runtime.InteropServices;
using Obelisk.Abstractions;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed class Sdl3NativeApi : ISdl3Api
{
    public static Sdl3NativeApi Instance { get; } = new();

    private Sdl3NativeApi()
    {
    }

    public bool InitializeVideo()
    {
        return SDL.Init(SDL.InitFlags.Video);
    }

    public nint CreateWindow(string title, int width, int height, bool resizable)
    {
        var flags = resizable ? SDL.WindowFlags.Resizable : 0;
        return SDL.CreateWindow(title, width, height, flags);
    }

    public bool ClearWindowToBlack(nint window)
    {
        var surfacePointer = SDL.GetWindowSurface(window);

        if (surfacePointer == 0)
        {
            return false;
        }

        var surface = Marshal.PtrToStructure<SDL.Surface>(surfacePointer);
        var formatDetails = SDL.GetPixelFormatDetails(surface.Format);

        if (formatDetails == 0)
        {
            return false;
        }

        var black = SDL.MapRGB(formatDetails, 0, 0, 0, 0);
        return SDL.FillSurfaceRect(surfacePointer, 0, black)
            && SDL.UpdateWindowSurface(window);
    }

    public bool TryPollEvent(out Sdl3PlatformEvent platformEvent)
    {
        if (!SDL.PollEvent(out var nativeEvent))
        {
            platformEvent = new Sdl3PlatformEvent(Sdl3EventType.Other);
            return false;
        }

        var eventType = (SDL.EventType)nativeEvent.Type;
        platformEvent = eventType switch
        {
            SDL.EventType.Quit => new(Sdl3EventType.QuitRequested),
            SDL.EventType.KeyDown => new(Sdl3EventType.KeyDown, MapKey(nativeEvent.Key.Key)),
            SDL.EventType.KeyUp => new(Sdl3EventType.KeyUp, MapKey(nativeEvent.Key.Key)),
            SDL.EventType.MouseMotion => new(
                Sdl3EventType.MouseMoved,
                MouseX: nativeEvent.Motion.X,
                MouseY: nativeEvent.Motion.Y),
            SDL.EventType.MouseButtonDown => new(
                Sdl3EventType.MouseButtonDown,
                MouseButton: MapMouseButton(nativeEvent.Button.Button),
                MouseX: nativeEvent.Button.X,
                MouseY: nativeEvent.Button.Y),
            SDL.EventType.MouseButtonUp => new(
                Sdl3EventType.MouseButtonUp,
                MouseButton: MapMouseButton(nativeEvent.Button.Button),
                MouseX: nativeEvent.Button.X,
                MouseY: nativeEvent.Button.Y),
            _ => new(Sdl3EventType.Other),
        };

        return true;
    }

    public void DestroyWindow(nint window)
    {
        SDL.DestroyWindow(window);
    }

    public void Quit()
    {
        SDL.Quit();
    }

    public string GetError()
    {
        return SDL.GetError();
    }

    private static Key? MapKey(SDL.Keycode keycode)
    {
        if (keycode is >= SDL.Keycode.A and <= SDL.Keycode.Z)
        {
            return Key.A + (int)(keycode - SDL.Keycode.A);
        }

        return keycode switch
        {
            SDL.Keycode.Alpha0 => Key.Alpha0,
            SDL.Keycode.Alpha1 => Key.Alpha1,
            SDL.Keycode.Alpha2 => Key.Alpha2,
            SDL.Keycode.Alpha3 => Key.Alpha3,
            SDL.Keycode.Alpha4 => Key.Alpha4,
            SDL.Keycode.Alpha5 => Key.Alpha5,
            SDL.Keycode.Alpha6 => Key.Alpha6,
            SDL.Keycode.Alpha7 => Key.Alpha7,
            SDL.Keycode.Alpha8 => Key.Alpha8,
            SDL.Keycode.Alpha9 => Key.Alpha9,
            SDL.Keycode.Left => Key.Left,
            SDL.Keycode.Right => Key.Right,
            SDL.Keycode.Up => Key.Up,
            SDL.Keycode.Down => Key.Down,
            SDL.Keycode.Space => Key.Space,
            SDL.Keycode.Return => Key.Enter,
            SDL.Keycode.Escape => Key.Escape,
            SDL.Keycode.Tab => Key.Tab,
            SDL.Keycode.Backspace => Key.Backspace,
            SDL.Keycode.LShift => Key.LeftShift,
            SDL.Keycode.RShift => Key.RightShift,
            SDL.Keycode.LCtrl => Key.LeftControl,
            SDL.Keycode.RCtrl => Key.RightControl,
            SDL.Keycode.LAlt => Key.LeftAlt,
            SDL.Keycode.RAlt => Key.RightAlt,
            _ => null,
        };
    }

    private static MouseButton? MapMouseButton(byte button) => button switch
    {
        1 => MouseButton.Left,
        2 => MouseButton.Middle,
        3 => MouseButton.Right,
        4 => MouseButton.X1,
        5 => MouseButton.X2,
        _ => null,
    };
}
