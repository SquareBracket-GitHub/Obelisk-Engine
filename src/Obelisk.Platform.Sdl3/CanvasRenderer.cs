using System.Runtime.InteropServices;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed partial class CanvasRenderer : IDisposable, IRenderDevice
{
    private const string VertexSource = """
        struct Output { float4 Position : SV_Position; float4 Color : TEXCOORD0; float2 UV : TEXCOORD1; };
        Output main(float2 position : TEXCOORD0, float4 color : TEXCOORD1, float2 uv : TEXCOORD2) {
            Output output; output.Position = float4(position, 0, 1); output.Color = color; output.UV = uv; return output;
        }
        """;
    private const string FragmentPrefix = """
        Texture2D<float4> Image : register(t0, space2);
        SamplerState ImageSampler : register(s0, space2);
        cbuffer DrawConstants : register(b0, space3) { float4 Parameters; float4 TextureInfo; };
        float4 SampleImage(float2 uv) {
            float4 value = Image.Sample(ImageSampler, uv);
            if (TextureInfo.x > 0.5) value.rgb = value.a > 0.000001 ? value.rgb / value.a : float3(0,0,0);
            return value;
        }
        """;
    private const string DefaultFragment = """
        float4 Shade(float4 color, float2 uv) { return SampleImage(uv) * color; }
        """;
    private const string FragmentSuffix = """
        float4 main(float4 color : TEXCOORD0, float2 uv : TEXCOORD1) : SV_Target0 {
            float4 value = Shade(color, uv);
            if (TextureInfo.y > 0.5) value.rgb *= value.a;
            return value;
        }
        """;
    private static long _nextTexture;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly QuadBuffers _buffers;
    private readonly Dictionary<TextureHandle, (Texture Texture, GpuResource Native)> _textures = [];
    private GpuResource? _opaque;
    private GpuResource? _alpha;
    private GpuResource? _sampler;
    private GpuResource? _linearSampler;
    private Texture? _white;
    private bool _disposed;

    public CanvasRenderer(nint device, SDL.GPUTextureFormat format, int samples = 1)
    {
        Device = device;
        _format = format;
        if (samples is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(samples));
        SampleCount = new[] { 8, 4, 2, 1 }.First(n => n <= samples &&
            SDL.GPUTextureSupportsSampleCount(device, format, NativeSamples(n)) &&
            SDL.GPUTextureSupportsSampleCount(device, SDL.GPUTextureFormat.R8G8B8A8Unorm, NativeSamples(n)));
        _buffers = new QuadBuffers(device);
        try
        {
            _opaque = GetPipeline(0, format, false);
            _alpha = GetPipeline(0, format, true);
            var samplerInfo = new SDL.GPUSamplerCreateInfo
            {
                MinFilter = SDL.GPUFilter.Nearest,
                MagFilter = SDL.GPUFilter.Nearest,
                AddressModeU = SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeV = SDL.GPUSamplerAddressMode.ClampToEdge,
                AddressModeW = SDL.GPUSamplerAddressMode.ClampToEdge,
            };
            _sampler = Own(SDL.CreateGPUSampler(device, in samplerInfo), h => SDL.ReleaseGPUSampler(device, h));
            samplerInfo.MinFilter = samplerInfo.MagFilter = SDL.GPUFilter.Linear;
            _linearSampler = Own(SDL.CreateGPUSampler(device, in samplerInfo), h => SDL.ReleaseGPUSampler(device, h));
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

    private GpuResource CreatePipeline(SDL.GPUTextureFormat format, bool blend, string function)
    {
        using var vertex = Own(Sdl3NativeApi.CompileShader(Device, VertexSource, ShaderCross.ShaderStage.Vertex),
            h => SDL.ReleaseGPUShader(Device, h));
        using var fragment = Own(Sdl3NativeApi.CompileShader(Device, FragmentPrefix + function + FragmentSuffix, ShaderCross.ShaderStage.Fragment, 1),
            h => SDL.ReleaseGPUShader(Device, h));
        var info = new SDL.GPUGraphicsPipelineCreateInfo
        {
            VertexShader = vertex.Handle,
            FragmentShader = fragment.Handle,
            PrimitiveType = SDL.GPUPrimitiveType.TriangleList,
            RasterizerState = new() { FillMode = SDL.GPUFillMode.Fill, CullMode = SDL.GPUCullMode.None },
            MultisampleState = new() { SampleCount = NativeSamples(SampleCount) },
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
            Upload(native.Handle, 0, 0, image);
            return RegisterTexture(native, image.Width, image.Height, false);
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
        return RecordFrame(commands, target, width, height, drawing);
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
        _linearSampler?.Dispose();
        foreach (var entry in _materials.Values.ToArray()) entry.Material.Dispose();
        foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
        _pipelines.Clear();
        _backbufferMsaa?.Dispose();
        _disposed = true;
    }
}
