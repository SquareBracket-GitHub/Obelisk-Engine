using System.Runtime.InteropServices;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed class Sdl3NativeApi : ISdl3Api
{
    private const string VertexShaderSource = """
        static const float2 Positions[6] = {
            float2(-1.0, -1.0),
            float2( 1.0, -1.0),
            float2(-1.0,  1.0),
            float2(-1.0,  1.0),
            float2( 1.0, -1.0),
            float2( 1.0,  1.0)
        };

        float4 main(uint vertexId : SV_VertexID) : SV_Position
        {
            return float4(Positions[vertexId], 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        cbuffer RectangleColor : register(b0, space3)
        {
            float4 FillColor;
        };

        float4 main() : SV_Target0
        {
            return FillColor;
        }
        """;

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

    public nint CreateSolidRectanglePipeline(nint device, nint window)
    {
        var vertexShader = CompileShader(device, VertexShaderSource, ShaderCross.ShaderStage.Vertex);

        if (vertexShader == 0)
        {
            return 0;
        }

        var fragmentShader = CompileShader(device, FragmentShaderSource, ShaderCross.ShaderStage.Fragment);

        if (fragmentShader == 0)
        {
            SDL.ReleaseGPUShader(device, vertexShader);
            return 0;
        }

        try
        {
            var pipelineInfo = new SDL.GPUGraphicsPipelineCreateInfo
            {
                VertexShader = vertexShader,
                FragmentShader = fragmentShader,
                PrimitiveType = SDL.GPUPrimitiveType.TriangleList,
                RasterizerState = new SDL.GPURasterizerState
                {
                    FillMode = SDL.GPUFillMode.Fill,
                    CullMode = SDL.GPUCullMode.None,
                    FrontFace = SDL.GPUFrontFace.CounterClockwise,
                },
                MultisampleState = new SDL.GPUMultisampleState
                {
                    SampleCount = SDL.GPUSampleCount.SampleCount1,
                },
            };
            SDL.GPUColorTargetDescription[] colorTargets =
            [
                new() { Format = SDL.GetGPUSwapchainTextureFormat(device, window) },
            ];

            return SDL.CreateGPUGraphicsPipeline(
                device,
                in pipelineInfo,
                [],
                [],
                colorTargets);
        }
        finally
        {
            SDL.ReleaseGPUShader(device, fragmentShader);
            SDL.ReleaseGPUShader(device, vertexShader);
        }
    }

    public bool RenderGpuFrame(
        nint device,
        nint window,
        nint pipeline,
        IReadOnlyList<RectangleCommand> rectangles)
    {
        var commandBuffer = SDL.AcquireGPUCommandBuffer(device);

        if (commandBuffer == 0)
        {
            return false;
        }

        if (!SDL.WaitAndAcquireGPUSwapchainTexture(
                commandBuffer,
                window,
                out var swapchainTexture,
                out var swapchainWidth,
                out var swapchainHeight))
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            return false;
        }

        if (swapchainTexture == 0)
        {
            return SDL.SubmitGPUCommandBuffer(commandBuffer);
        }

        SDL.GPUColorTargetInfo[] colorTargets =
        [
            new()
            {
                Texture = swapchainTexture,
                ClearColor = new SDL.FColor { R = 0.035f, G = 0.045f, B = 0.075f, A = 1 },
                LoadOp = SDL.GPULoadOp.Clear,
                StoreOp = SDL.GPUStoreOp.Store,
            },
        ];
        var renderPass = SDL.BeginGPURenderPass(commandBuffer, colorTargets, 1, 0);

        if (renderPass == 0)
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            return false;
        }

        SDL.BindGPUGraphicsPipeline(renderPass, pipeline);

        foreach (var rectangle in rectangles)
        {
            var left = Math.Clamp(rectangle.Bounds.X, 0, swapchainWidth);
            var top = Math.Clamp(rectangle.Bounds.Y, 0, swapchainHeight);
            var right = Math.Clamp(rectangle.Bounds.X + rectangle.Bounds.Width, 0, swapchainWidth);
            var bottom = Math.Clamp(rectangle.Bounds.Y + rectangle.Bounds.Height, 0, swapchainHeight);

            if (right <= left || bottom <= top)
            {
                continue;
            }

            var viewport = new SDL.GPUViewport
            {
                X = left,
                Y = top,
                W = right - left,
                H = bottom - top,
                MinDepth = 0,
                MaxDepth = 1,
            };
            Span<SDL.FColor> color =
            [
                new()
                {
                    R = rectangle.Color.R,
                    G = rectangle.Color.G,
                    B = rectangle.Color.B,
                    A = rectangle.Color.A,
                },
            ];
            var colorBytes = MemoryMarshal.AsBytes(color);
            SDL.PushGPUFragmentUniformData(commandBuffer, 0, colorBytes, (uint)colorBytes.Length);
            SDL.SetGPUViewport(renderPass, in viewport);
            SDL.DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
        }

        SDL.EndGPURenderPass(renderPass);
        return SDL.SubmitGPUCommandBuffer(commandBuffer);
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
        SDL.ReleaseGPUGraphicsPipeline(device, pipeline);
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

    private static nint CompileShader(
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
                NumUniformBuffers = stage is ShaderCross.ShaderStage.Fragment ? 1U : 0U,
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
