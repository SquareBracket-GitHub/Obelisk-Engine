using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed class Sdl3NativeApi : ISdl3Api
{
    public static Sdl3NativeApi Instance { get; } = new();
    public ISdlAudioApi AudioApi { get; } = new SdlAudioApi();

    // Accessed only on the SDL owning thread. Resources are removed before device destruction.
    private readonly Dictionary<nint, CanvasRenderer> _renderers = [];

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

    public bool InitializeShaderCompiler()
    {
        return ShaderCross.Init();
    }

    public nint CreateGpuDevice()
    {
        return SDL.CreateGPUDevice(ShaderCross.GetSPIRVShaderFormats(), debugMode: false, name: null);
    }

    public bool ClaimWindowForGpuDevice(nint device, nint window)
    {
        return SDL.ClaimWindowForGPUDevice(device, window);
    }

    public nint CreateCanvasPipeline(nint device, nint window)
    {
        var renderer = new CanvasRenderer(device, SDL.GetGPUSwapchainTextureFormat(device, window));
        _renderers.Add(renderer.Pipeline, renderer);
        return renderer.Pipeline;
    }

    public Texture CreateTexture(nint device, RgbaImage image) =>
        _renderers.Values.Single(r => r.Device == device).CreateTexture(image);

    public bool RenderGpuFrame(nint device, nint window, nint pipeline, IReadOnlyList<RenderCommand2D> drawing)
    {
        var commands = SDL.AcquireGPUCommandBuffer(device);
        if (commands == 0) return false;
        var submitted = false;
        try
        {
            if (!SDL.WaitAndAcquireGPUSwapchainTexture(commands, window, out var target, out var width, out var height))
                return false;
            if (target != 0 && width > 0 && height > 0 &&
                !_renderers[pipeline].Record(commands, target, width, height, drawing)) return false;
            submitted = true; // SDL consumes the command buffer even if submission fails.
            return SDL.SubmitGPUCommandBuffer(commands);
        }
        finally
        {
            if (!submitted) SDL.CancelGPUCommandBuffer(commands);
        }
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
            SDL.EventType.WindowMinimized => new(Sdl3EventType.WindowMinimized),
            SDL.EventType.WindowRestored or SDL.EventType.WindowMaximized => new(Sdl3EventType.WindowRestored),
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
            SDL.EventType.WindowResized or SDL.EventType.WindowPixelSizeChanged => new(
                Sdl3EventType.WindowResized,
                Width: nativeEvent.Window.Data1,
                Height: nativeEvent.Window.Data2),
            _ => new(Sdl3EventType.Other),
        };

        return true;
    }

    public void DestroyWindow(nint window)
    {
        SDL.DestroyWindow(window);
    }

    public void ReleaseGpuPipeline(nint device, nint pipeline)
    {
        if (_renderers.Remove(pipeline, out var renderer)) renderer.Dispose();
    }

    public void ReleaseWindowFromGpuDevice(nint device, nint window)
    {
        SDL.ReleaseWindowFromGPUDevice(device, window);
    }

    public void DestroyGpuDevice(nint device)
    {
        SDL.WaitForGPUIdle(device);
        SDL.DestroyGPUDevice(device);
    }

    public void QuitShaderCompiler()
    {
        ShaderCross.Quit();
    }

    public void Quit()
    {
        SDL.Quit();
    }

    public string GetError()
    {
        return SDL.GetError();
    }

    internal static nint CompileShader(
        nint device,
        string source,
        ShaderCross.ShaderStage stage)
    {
        var bytecode = ShaderCross.CompileSPIRVFromHLSL(
            source,
            "main",
            stage,
            out var bytecodeSize,
            includeDir: null!,
            props: 0);

        if (bytecode == 0)
        {
            return 0;
        }

        try
        {
            var resources = new ShaderCross.GraphicsShaderResourceInfo
            {
                NumUniformBuffers = 0,
                NumSamplers = stage == ShaderCross.ShaderStage.Fragment ? 1U : 0U,
            };
            return ShaderCross.CompileGraphicsShaderFromSPIRV(
                device,
                bytecode,
                bytecodeSize,
                "main",
                stage,
                in resources,
                infoProps: 0,
                shaderProps: 0);
        }
        finally
        {
            SDL.Free(bytecode);
        }
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
