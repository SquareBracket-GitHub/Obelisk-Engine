using System.Runtime.InteropServices;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed partial class CanvasRenderer
{
    private static long _nextMaterial;
    private readonly SDL.GPUTextureFormat _format;
    private readonly HashSet<TextureHandle> _targets = [];
    private readonly Dictionary<ulong, (Material Material, string Source)> _materials = [];
    private readonly Dictionary<(ulong Material, SDL.GPUTextureFormat Format, bool Blend), GpuResource> _pipelines = [];
    private readonly Dictionary<TextureHandle, GpuResource> _targetMsaa = [];
    private GpuResource? _backbufferMsaa;
    private (uint Width, uint Height) _backbufferSize;
    public int SampleCount { get; }
    internal Func<float> DisplayScaleProvider { get; init; } = () => 1;
    public float DisplayScale { get { EnsureThread(); return DisplayScaleProvider(); } }

    private static SDL.GPUSampleCount NativeSamples(int count) => count switch
    {
        1 => SDL.GPUSampleCount.SampleCount1,
        2 => SDL.GPUSampleCount.SampleCount2,
        4 => SDL.GPUSampleCount.SampleCount4,
        8 => SDL.GPUSampleCount.SampleCount8,
        _ => throw new ArgumentOutOfRangeException(nameof(count)),
    };

    private GpuResource GetPipeline(ulong material, SDL.GPUTextureFormat format, bool blend)
    {
        var key = (material, format, blend);
        if (!_pipelines.TryGetValue(key, out var pipeline))
        {
            pipeline = CreatePipeline(format, blend, material == 0 ? DefaultFragment : _materials[material].Source);
            _pipelines.Add(key, pipeline);
        }
        return pipeline;
    }

    public Material CreateMaterial(string fragmentFunction)
    {
        EnsureThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentFunction);
        if (fragmentFunction.Length > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(fragmentFunction));
        var id = (ulong)Interlocked.Increment(ref _nextMaterial);
        var material = new Material(id, () =>
        {
            EnsureThread();
            foreach (var key in _pipelines.Keys.Where(k => k.Material == id).ToArray())
            { _pipelines[key].Dispose(); _pipelines.Remove(key); }
            _materials.Remove(id);
        });
        _materials.Add(id, (material, fragmentFunction));
        try { _ = GetPipeline(id, _format, true); return material; }
        catch { material.Dispose(); throw; }
    }

    public RenderTarget CreateRenderTarget(int width, int height)
    {
        EnsureThread(); RgbaImage.ValidateSize(width, height);
        var native = CreateTargetTexture((uint)width, (uint)height, SDL.GPUTextureFormat.R8G8B8A8Unorm, 1);
        try
        {
            var commands = SDL.AcquireGPUCommandBuffer(Device);
            if (commands == 0) throw new Sdl3Exception("Target initialization command buffer", SDL.GetError());
            var submitted = false;
            try
            {
                SDL.GPUColorTargetInfo[] targets = [new() { Texture = native.Handle, LoadOp = SDL.GPULoadOp.Clear, StoreOp = SDL.GPUStoreOp.Store }];
                var pass = SDL.BeginGPURenderPass(commands, targets, 1, 0);
                if (pass == 0) throw new Sdl3Exception("Target initialization", SDL.GetError());
                SDL.EndGPURenderPass(pass); submitted = true;
                if (!SDL.SubmitGPUCommandBuffer(commands)) throw new Sdl3Exception("Target initialization submit", SDL.GetError());
            }
            finally { if (!submitted) SDL.CancelGPUCommandBuffer(commands); }
            return new RenderTarget(RegisterTexture(native, width, height, true));
        }
        catch { native.Dispose(); throw; }
    }

    private GpuResource CreateTargetTexture(uint width, uint height, SDL.GPUTextureFormat format, int samples)
    {
        var info = new SDL.GPUTextureCreateInfo
        {
            Type = SDL.GPUTextureType.TextureType2D,
            Format = format,
            Usage = SDL.GPUTextureUsageFlags.ColorTarget | (samples == 1 ? SDL.GPUTextureUsageFlags.Sampler : 0),
            Width = width,
            Height = height,
            LayerCountOrDepth = 1,
            NumLevels = 1,
            SampleCount = NativeSamples(samples),
        };
        return Own(SDL.CreateGPUTexture(Device, in info), h => SDL.ReleaseGPUTexture(Device, h));
    }

    private Texture RegisterTexture(GpuResource native, int width, int height, bool target)
    {
        var handle = new TextureHandle((ulong)Interlocked.Increment(ref _nextTexture));
        var texture = new Texture(handle, width, height, () =>
        {
            EnsureThread();
            if (_targetMsaa.Remove(handle, out var msaa)) msaa.Dispose();
            _targets.Remove(handle);
            if (_textures.Remove(handle, out var entry)) entry.Native.Dispose();
        });
        _textures.Add(handle, (texture, native));
        if (target) _targets.Add(handle);
        return texture;
    }

    public void UpdateTexture(Texture texture, int x, int y, RgbaImage image)
    {
        EnsureThread(); ArgumentNullException.ThrowIfNull(texture); ArgumentNullException.ThrowIfNull(image);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        var entry = Resolve(texture.Handle);
        if (!ReferenceEquals(entry.Texture, texture)) throw new InvalidOperationException("Foreign texture identity.");
        if (_targets.Contains(texture.Handle)) throw new InvalidOperationException("Render targets cannot be updated from CPU pixels.");
        if (x < 0 || y < 0 || (long)x + image.Width > texture.Width || (long)y + image.Height > texture.Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Update region must be inside the texture.");
        Upload(entry.Native.Handle, x, y, image);
    }

    private void Upload(nint texture, int x, int y, RgbaImage image)
    {
        var rowBytes = checked((image.Width * 4 + 255) / 256 * 256);
        var transferInfo = new SDL.GPUTransferBufferCreateInfo
        { Usage = SDL.GPUTransferBufferUsage.Upload, Size = checked((uint)(rowBytes * image.Height)) };
        using var transfer = Own(SDL.CreateGPUTransferBuffer(Device, in transferInfo), h => SDL.ReleaseGPUTransferBuffer(Device, h));
        var mapped = SDL.MapGPUTransferBuffer(Device, transfer.Handle, false);
        if (mapped == 0) throw new Sdl3Exception("Texture upload mapping", SDL.GetError());
        try
        {
            var bytes = image.Pixels.ToArray();
            for (var row = 0; row < image.Height; row++) Marshal.Copy(bytes, row * image.Width * 4, mapped + row * rowBytes, image.Width * 4);
        }
        finally { SDL.UnmapGPUTransferBuffer(Device, transfer.Handle); }
        var commands = SDL.AcquireGPUCommandBuffer(Device);
        if (commands == 0) throw new Sdl3Exception("Texture upload command buffer", SDL.GetError());
        var submitted = false;
        try
        {
            var copy = SDL.BeginGPUCopyPass(commands);
            if (copy == 0) throw new Sdl3Exception("Texture upload copy pass", SDL.GetError());
            var source = new SDL.GPUTextureTransferInfo { TransferBuffer = transfer.Handle, PixelsPerRow = (uint)rowBytes / 4, RowsPerLayer = (uint)image.Height };
            var target = new SDL.GPUTextureRegion { Texture = texture, X = (uint)x, Y = (uint)y, W = (uint)image.Width, H = (uint)image.Height, D = 1 };
            // No cycling: a partial update must preserve every untouched texel, including when the texture is in flight.
            SDL.UploadToGPUTexture(copy, in source, in target, false);
            SDL.EndGPUCopyPass(copy);
            submitted = true;
            if (!SDL.SubmitGPUCommandBuffer(commands)) throw new Sdl3Exception("Texture upload submit", SDL.GetError());
        }
        finally { if (!submitted) SDL.CancelGPUCommandBuffer(commands); }
    }
}
