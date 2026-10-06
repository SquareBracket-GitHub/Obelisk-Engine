using Obelisk.Abstractions;

namespace Obelisk.Platform.Sdl3;

internal interface ISdl3Api
{
    bool InitializeVideo();

    nint CreateWindow(string title, int width, int height, bool resizable);

    bool InitializeShaderCompiler();

    nint CreateGpuDevice();

    bool ClaimWindowForGpuDevice(nint device, nint window);

    nint CreateSolidRectanglePipeline(nint device, nint window);

    bool RenderGpuFrame(
        nint device,
        nint window,
        nint pipeline,
        IReadOnlyList<SolidRectangle> solidRectangles);

    bool TryPollEvent(out Sdl3PlatformEvent platformEvent);

    void DestroyWindow(nint window);

    void ReleaseGpuPipeline(nint device, nint pipeline);

    void ReleaseWindowFromGpuDevice(nint device, nint window);

    void DestroyGpuDevice(nint device);

    void QuitShaderCompiler();

    void Quit();

    string GetError();
}

internal readonly record struct Sdl3PlatformEvent(
    Sdl3EventType Type,
    Key? Key = null,
    MouseButton? MouseButton = null,
    float MouseX = 0,
    float MouseY = 0,
    int Width = 0,
    int Height = 0);

internal enum Sdl3EventType
{
    Other,
    QuitRequested,
    KeyDown,
    KeyUp,
    MouseMoved,
    MouseButtonDown,
    MouseButtonUp,
    WindowResized,
}
