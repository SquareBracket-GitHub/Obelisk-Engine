using System.Diagnostics;
using System.Text.Json;
using Obelisk.Abstractions;
using Obelisk.Platform.Sdl3;
using SDL3;

namespace Obelisk.Stability;

// Optional diagnostic isolation; this is not a substitute for the Sample stability Gate.
internal static class NativeProbe
{
    internal static int Run(string mode, int cycles)
    {
        if (mode is not ("empty" or "init" or "events" or "video" or "audio" or "gpu" or "platform") || cycles < 1)
            throw new ArgumentException("Probe modes: empty, init, events, video, audio, gpu, platform; cycles must be positive.");
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            if (mode == "platform")
            {
                var platform = new Sdl3ApplicationPlatform(new AppOptions { Title = "Obelisk native lifetime probe", Width = 320, Height = 180 });
                try { platform.Initialize(); }
                finally { platform.Shutdown(); }
            }
            else if (mode != "empty") RunNative(mode);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Thread.Sleep(200); // Observe native worker teardown, not transient shutdown work.
            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new { mode, cycle, process.HandleCount,
                Threads = process.Threads.Count, process.PrivateMemorySize64, GpuHandles = GpuResource.LiveCount }));
        }
        return 0;
    }

    private static void RunNative(string mode)
    {
        var api = Sdl3NativeApi.Instance;
        nint window = 0, device = 0, pipeline = 0;
        var claimed = false;
        var shader = false;
        if (mode != "audio" && !SDL.Init(mode == "events" ? SDL.InitFlags.Events : SDL.InitFlags.Video)) throw new InvalidOperationException(api.GetError());
        try
        {
            if (mode == "audio")
            {
                using var audio = new SdlAudioDevice(new SdlAudioApi());
                audio.Initialize();
            }
            else if (mode is "video" or "gpu")
            {
                window = api.CreateWindow("Obelisk native lifetime probe", 320, 180, false);
                if (window == 0) throw new InvalidOperationException(api.GetError());
                if (mode == "gpu")
                {
                    shader = api.InitializeShaderCompiler();
                    if (!shader) throw new InvalidOperationException(api.GetError());
                    device = api.CreateGpuDevice();
                    if (device == 0) throw new InvalidOperationException(api.GetError());
                    claimed = api.ClaimWindowForGpuDevice(device, window);
                    if (!claimed) throw new InvalidOperationException(api.GetError());
                    pipeline = api.CreateCanvasPipeline(device, window);
                    if (pipeline == 0) throw new InvalidOperationException(api.GetError());
                }
            }
        }
        finally
        {
            if (pipeline != 0) api.ReleaseGpuPipeline(device, pipeline);
            if (claimed) api.ReleaseWindowFromGpuDevice(device, window);
            if (device != 0) api.DestroyGpuDevice(device);
            if (window != 0) api.DestroyWindow(window);
            if (shader) api.QuitShaderCompiler();
            api.Quit();
        }
    }
}
