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

internal enum Sdl3PlatformEvent
{
    Other,
    QuitRequested,
    EscapePressed,
}
