using System.Runtime.InteropServices;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed partial class CanvasRenderer
{
    internal byte[] DownloadSnapshot(RenderTarget target)
    {
        EnsureThread();
        var width = target.Texture.Width; var height = target.Texture.Height;
        var rowBytes = checked((width * 4 + 255) / 256 * 256);
        var transferInfo = new SDL.GPUTransferBufferCreateInfo { Usage = SDL.GPUTransferBufferUsage.Download, Size = checked((uint)(rowBytes * height)) };
        using var transfer = Own(SDL.CreateGPUTransferBuffer(Device, in transferInfo), h => SDL.ReleaseGPUTransferBuffer(Device, h));
        var commands = SDL.AcquireGPUCommandBuffer(Device);
        if (commands == 0) throw new Sdl3Exception("Screenshot command buffer", SDL.GetError());
        var submitted = false;
        try
        {
            var copy = SDL.BeginGPUCopyPass(commands);
            if (copy == 0) throw new Sdl3Exception("Screenshot copy pass", SDL.GetError());
            var source = new SDL.GPUTextureRegion { Texture = Resolve(target.Texture.Handle).Native.Handle, W = (uint)width, H = (uint)height, D = 1 };
            var destination = new SDL.GPUTextureTransferInfo { TransferBuffer = transfer.Handle, PixelsPerRow = (uint)rowBytes / 4, RowsPerLayer = (uint)height };
            SDL.DownloadFromGPUTexture(copy, in source, in destination);
            SDL.EndGPUCopyPass(copy);
            submitted = true;
            if (!SDL.SubmitGPUCommandBuffer(commands) || !SDL.WaitForGPUIdle(Device)) throw new Sdl3Exception("Screenshot download", SDL.GetError());
        }
        finally { if (!submitted) SDL.CancelGPUCommandBuffer(commands); }
        var mapped = SDL.MapGPUTransferBuffer(Device, transfer.Handle, false);
        if (mapped == 0) throw new Sdl3Exception("Screenshot map", SDL.GetError());
        try
        {
            var pixels = new byte[checked(width * height * 4)];
            for (var row = 0; row < height; row++) Marshal.Copy(mapped + row * rowBytes, pixels, row * width * 4, width * 4);
            return pixels;
        }
        finally { SDL.UnmapGPUTransferBuffer(Device, transfer.Handle); }
    }
}
