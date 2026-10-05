using Obelisk.Abstractions;

namespace Obelisk.Platform.Sdl3;

internal interface ISdl3Api
{
    bool InitializeVideo();

    nint CreateWindow(string title, int width, int height, bool resizable);

    bool ClearWindowToBlack(nint window);

    bool TryPollEvent(out Sdl3PlatformEvent platformEvent);

    void DestroyWindow(nint window);

    void Quit();

    string GetError();
}

internal readonly record struct Sdl3PlatformEvent(
    Sdl3EventType Type,
    Key? Key = null,
    MouseButton? MouseButton = null,
    float MouseX = 0,
    float MouseY = 0);

internal enum Sdl3EventType
{
    Other,
    QuitRequested,
    KeyDown,
    KeyUp,
    MouseMoved,
    MouseButtonDown,
    MouseButtonUp,
}
