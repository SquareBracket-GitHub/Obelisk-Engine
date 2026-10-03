using System.Runtime.InteropServices;
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
            platformEvent = Sdl3PlatformEvent.Other;
            return false;
        }

        var eventType = (SDL.EventType)nativeEvent.Type;
        platformEvent = eventType switch
        {
            SDL.EventType.Quit => Sdl3PlatformEvent.QuitRequested,
            SDL.EventType.KeyDown when nativeEvent.Key.Key == SDL.Keycode.Escape =>
                Sdl3PlatformEvent.EscapePressed,
            _ => Sdl3PlatformEvent.Other,
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
}
