using System.Runtime.InteropServices;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed class CanvasRenderer : IDisposable, ITextureDevice
{
    private const string VertexSource = """
        struct Output { float4 Position : SV_Position; float4 Color : TEXCOORD0; float2 UV : TEXCOORD1; };
        Output main(float2 position : TEXCOORD0, float4 color : TEXCOORD1, float2 uv : TEXCOORD2) {
            Output output; output.Position = float4(position, 0, 1); output.Color = color; output.UV = uv; return output;
        }
        """;
    private const string FragmentSource = """
        Texture2D<float4> Image : register(t0, space2);
        SamplerState ImageSampler : register(s0, space2);
        float4 main(float4 color : TEXCOORD0, float2 uv : TEXCOORD1) : SV_Target0 {
            return Image.Sample(ImageSampler, uv) * color;
        }
        """;
    private static long _nextTexture;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly QuadBuffers _buffers;
    private readonly Dictionary<TextureHandle, (Texture Texture, GpuResource Native)> _textures = [];
    private GpuResource? _opaque;
    private GpuResource? _alpha;
    private GpuResource? _sampler;
    private Texture? _white;
    private bool _disposed;

    public CanvasRenderer(nint device, SDL.GPUTextureFormat format)
    {
        Device = device;
        _buffers = new QuadBuffers(device);
        try
        {
            _opaque = CreatePipeline(format, false);
            _alpha = CreatePipeline(format, true);
            var samplerInfo = new SDL.GPUSamplerCreateInfo
            {
                MinFilter = SDL.GPUFilter.Nearest,
                MagFilter = SDL.GPUFilter.Nearest,
                AddressModeU = SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeV = SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeW = SDL.GPUSamplerAddressMode.ClampToEdge,
            };
            _sampler = Own(SDL.CreateGPUSampler(device, in samplerInfo), h => SDL.ReleaseGPUSampler(device, h));
            _white = CreateTexture(new RgbaImage(1, 1, [255, 255, 255, 255]));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public nint Device { get; }
    public nint Pipeline => _opaque!.Handle;
    internal int TextureCount => _textures.Count;

    private static GpuResource Own(nint handle, Action<nint> release)
    {
        if (handle == 0) throw new Sdl3Exception("GPU resource creation", SDL.GetError());
        return new(handle, release);
    }

    private GpuResource CreatePipeline(SDL.GPUTextureFormat format, bool blend)
    {
        using var vertex = Own(Sdl3NativeApi.CompileShader(Device, VertexSource, ShaderCross.ShaderStage.Vertex),
            h => SDL.ReleaseGPUShader(Device, h));
        using var fragment = Own(Sdl3NativeApi.CompileShader(Device, FragmentSource, ShaderCross.ShaderStage.Fragment),
            h => SDL.ReleaseGPUShader(Device, h));
        var info = new SDL.GPUGraphicsPipelineCreateInfo
        {
            VertexShader = vertex.Handle,
            FragmentShader = fragment.Handle,
            PrimitiveType = SDL.GPUPrimitiveType.TriangleList,
            RasterizerState = new() { FillMode = SDL.GPUFillMode.Fill, CullMode = SDL.GPUCullMode.None },
            MultisampleState = new() { SampleCount = SDL.GPUSampleCount.SampleCount1 },
        };
        SDL.GPUVertexBufferDescription[] buffers = [new() { Slot = 0, Pitch = (uint)Marshal.SizeOf<QuadVertex>(), InputRate = SDL.GPUVertexInputRate.Vertex }];
        SDL.GPUVertexAttribute[] attributes =
        [
            new() { Location = 0, Format = SDL.GPUVertexElementFormat.Float2, Offset = 0 },
            new() { Location = 1, Format = SDL.GPUVertexElementFormat.Float4, Offset = 8 },
            new() { Location = 2, Format = SDL.GPUVertexElementFormat.Float2, Offset = 24 },
        ];
        SDL.GPUColorTargetDescription[] targets =
        [
            new()
            {
                Format = format,
                BlendState = new()
                {
                    EnableBlend = blend,
                    SrcColorBlendFactor = SDL.GPUBlendFactor.SrcAlpha,
                    DstColorBlendFactor = SDL.GPUBlendFactor.OneMinusSrcAlpha,
                    ColorBlendOp = SDL.GPUBlendOp.Add,
                    SrcAlphaBlendFactor = SDL.GPUBlendFactor.One,
                    DstAlphaBlendFactor = SDL.GPUBlendFactor.OneMinusSrcAlpha,
                    AlphaBlendOp = SDL.GPUBlendOp.Add,
                },
            },
        ];
        return Own(SDL.CreateGPUGraphicsPipeline(Device, in info, buffers, attributes, targets),
            h => SDL.ReleaseGPUGraphicsPipeline(Device, h));
    }

    public Texture CreateTexture(RgbaImage image)
    {
        EnsureThread();
        ArgumentNullException.ThrowIfNull(image);
        var info = new SDL.GPUTextureCreateInfo
        {
            Type = SDL.GPUTextureType.TextureType2D,
            Format = SDL.GPUTextureFormat.R8G8B8A8Unorm,
            Usage = SDL.GPUTextureUsageFlags.Sampler,
            Width = (uint)image.Width,
            Height = (uint)image.Height,
            LayerCountOrDepth = 1,
            NumLevels = 1,
        };
        var native = Own(SDL.CreateGPUTexture(Device, in info), h => SDL.ReleaseGPUTexture(Device, h));
        try
        {
            // Explicit row alignment avoids an extra SDL/D3D12 staging copy.
            var rowBytes = checked((image.Width * 4 + 255) / 256 * 256);
            var transferInfo = new SDL.GPUTransferBufferCreateInfo
            {
                Usage = SDL.GPUTransferBufferUsage.Upload,
                Size = checked((uint)(rowBytes * image.Height)),
            };
            using var transfer = Own(SDL.CreateGPUTransferBuffer(Device, in transferInfo), h => SDL.ReleaseGPUTransferBuffer(Device, h));
            var mapped = SDL.MapGPUTransferBuffer(Device, transfer.Handle, false);
            if (mapped == 0) throw new Sdl3Exception("Texture upload mapping", SDL.GetError());
            try
            {
                var bytes = image.Pixels.ToArray();
                for (var row = 0; row < image.Height; row++)
                    Marshal.Copy(bytes, row * image.Width * 4, mapped + row * rowBytes, image.Width * 4);
            }
            finally { SDL.UnmapGPUTransferBuffer(Device, transfer.Handle); }
            var commands = SDL.AcquireGPUCommandBuffer(Device);
            if (commands == 0) throw new Sdl3Exception("Texture upload command buffer", SDL.GetError());
            var copy = SDL.BeginGPUCopyPass(commands);
            if (copy == 0)
            {
                SDL.CancelGPUCommandBuffer(commands);
                throw new Sdl3Exception("Texture upload copy pass", SDL.GetError());
            }
            var source = new SDL.GPUTextureTransferInfo { TransferBuffer = transfer.Handle, PixelsPerRow = (uint)rowBytes / 4, RowsPerLayer = (uint)image.Height };
            var target = new SDL.GPUTextureRegion { Texture = native.Handle, W = (uint)image.Width, H = (uint)image.Height, D = 1 };
            SDL.UploadToGPUTexture(copy, in source, in target, false);
            SDL.EndGPUCopyPass(copy);
            if (!SDL.SubmitGPUCommandBuffer(commands)) throw new Sdl3Exception("Texture upload submit", SDL.GetError());
            var handle = new TextureHandle((ulong)Interlocked.Increment(ref _nextTexture));
            var texture = new Texture(handle, image.Width, image.Height, () =>
            {
                EnsureThread();
                if (_textures.Remove(handle, out var entry)) entry.Native.Dispose();
            });
            _textures.Add(handle, (texture, native));
            return texture;
        }
        catch { native.Dispose(); throw; }
    }

    private (Texture Texture, GpuResource Native) Resolve(TextureHandle handle)
    {
        if (!_textures.TryGetValue(handle, out var entry))
            throw new InvalidOperationException($"Texture {handle.Value} is released or belongs to another device.");
        return entry;
    }

    /// <summary>Records into a caller-owned command buffer and color target (also used by pixel tests).</summary>
    internal bool Record(nint commands, nint target, uint width, uint height, IReadOnlyList<RenderCommand2D> drawing)
    {
        EnsureThread();
        var mesh = CanvasMesh.Create(drawing, width, height, h =>
        {
            var texture = Resolve(h).Texture;
            return (texture.Width, texture.Height);
        });
        if (!_buffers.Upload(commands, mesh.Mesh)) return false;
        SDL.GPUColorTargetInfo[] targets =
        [
            new() { Texture = target, ClearColor = new() { R = 0.035f, G = 0.045f, B = 0.075f, A = 1 }, LoadOp = SDL.GPULoadOp.Clear, StoreOp = SDL.GPUStoreOp.Store },
        ];
        var pass = SDL.BeginGPURenderPass(commands, targets, 1, 0);
        if (pass == 0) return false;
        if (mesh.Mesh.Indices.Length != 0)
        {
            SDL.GPUBufferBinding[] vertices = [new() { Buffer = _buffers.VertexHandle }];
            var indices = new SDL.GPUBufferBinding { Buffer = _buffers.IndexHandle };
            SDL.BindGPUVertexBuffers(pass, 0, vertices, 1);
            SDL.BindGPUIndexBuffer(pass, in indices, SDL.GPUIndexElementSize.IndexElementSize32Bit);
            foreach (var batch in mesh.Batches)
            {
                SDL.BindGPUGraphicsPipeline(pass, batch.Blend == BlendMode.Opaque ? _opaque!.Handle : _alpha!.Handle);
                var texture = Resolve(batch.Texture.IsValid ? batch.Texture : _white!.Handle);
                SDL.GPUTextureSamplerBinding[] binding = [new() { Texture = texture.Native.Handle, Sampler = _sampler!.Handle }];
                SDL.BindGPUFragmentSamplers(pass, 0, binding, 1);
                SDL.DrawGPUIndexedPrimitives(pass, batch.Count, 1, batch.FirstIndex, 0, 0);
            }
        }
        SDL.EndGPURenderPass(pass);
        return true;
    }

    private void EnsureThread()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("GPU resources must be used on their owning thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureThread();
        foreach (var entry in _textures.Values.ToArray()) entry.Texture.Dispose();
        _buffers.Dispose();
        _sampler?.Dispose();
        _alpha?.Dispose();
        _opaque?.Dispose();
        _disposed = true;
    }
}
