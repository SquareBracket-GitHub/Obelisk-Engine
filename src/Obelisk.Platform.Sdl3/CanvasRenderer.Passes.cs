using System.Numerics;
using System.Runtime.InteropServices;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3;

internal sealed partial class CanvasRenderer
{
    private bool RecordFrame(nint commands, nint target, uint width, uint height, IReadOnlyList<RenderCommand2D> drawing, SDL.GPUTextureFormat? mainFormat = null, TextureHandle mainDestination = default)
    {
        EnsureThread();
        var pending = new List<RenderCommand2D>();
        var clear = true;
        foreach (var command in drawing)
        {
            if (command is not RenderPassCommand offscreen) { pending.Add(command); continue; }
            if (!RecordSegment(commands, target, width, height, pending, mainFormat ?? _format, mainDestination, clear,
                new ColorRgba(0.035f, 0.045f, 0.075f), preserveSamples: true)) return false;
            clear = false; pending.Clear();
            ArgumentNullException.ThrowIfNull(offscreen.Target);
            ObjectDisposedException.ThrowIf(offscreen.Target.IsDisposed, offscreen.Target);
            var texture = offscreen.Target.Texture;
            var entry = Resolve(texture.Handle);
            if (!_targets.Contains(texture.Handle) || !ReferenceEquals(texture, entry.Texture))
                throw new InvalidOperationException("Render pass destination must be an owned render target.");
            if (offscreen.Commands.Any(c => c is RenderPassCommand)) throw new NotSupportedException("Nested render passes are not supported.");
            var color = offscreen.ClearColor;
            if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) || !float.IsFinite(color.A))
                throw new ArgumentOutOfRangeException(nameof(drawing));
            if (!RecordSegment(commands, entry.Native.Handle, (uint)texture.Width, (uint)texture.Height,
                offscreen.Commands, SDL.GPUTextureFormat.R8G8B8A8Unorm, texture.Handle, true, color)) return false;
        }
        return RecordSegment(commands, target, width, height, pending, mainFormat ?? _format, mainDestination, clear,
            new ColorRgba(0.035f, 0.045f, 0.075f));
    }

    internal bool RecordSnapshot(nint commands, RenderTarget target, IReadOnlyList<RenderCommand2D> drawing)
        => RecordFrame(commands, Resolve(target.Texture.Handle).Native.Handle, (uint)target.Texture.Width,
            (uint)target.Texture.Height, drawing, SDL.GPUTextureFormat.R8G8B8A8Unorm, target.Texture.Handle);

    private bool RecordSegment(nint commands, nint target, uint width, uint height, IReadOnlyList<RenderCommand2D> drawing,
        SDL.GPUTextureFormat format, TextureHandle destination, bool clear, ColorRgba clearColor, bool preserveSamples = false)
    {
        var mesh = CanvasMesh.Create(drawing, width, height, h =>
        {
            if (h == destination) throw new InvalidOperationException("A render pass cannot sample its own destination.");
            var texture = Resolve(h).Texture;
            return (texture.Width, texture.Height);
        });
        foreach (var batch in mesh.Batches)
        {
            if (batch.Material is { } material && (!_materials.TryGetValue(material.Id, out var owned) || !ReferenceEquals(owned.Material, material)))
                throw new InvalidOperationException("Material is released or belongs to another device.");
            _ = GetPipeline(batch.Material?.Id ?? 0, format, batch.Blend != BlendMode.Opaque);
        }
        if (!_buffers.Upload(commands, mesh.Mesh)) return false;
        var attachment = target;
        if (SampleCount > 1)
        {
            if (destination.IsValid)
            {
                if (!_targetMsaa.TryGetValue(destination, out var multisample))
                { multisample = CreateTargetTexture(width, height, format, SampleCount); _targetMsaa.Add(destination, multisample); }
                attachment = multisample.Handle;
            }
            else
            {
                if (_backbufferMsaa is null || _backbufferSize != (width, height))
                {
                    var replacement = CreateTargetTexture(width, height, format, SampleCount);
                    _backbufferMsaa?.Dispose(); _backbufferMsaa = replacement; _backbufferSize = (width, height);
                }
                attachment = _backbufferMsaa.Handle;
            }
        }
        var alpha = destination.IsValid ? clearColor.A : 1;
        SDL.GPUColorTargetInfo[] targets =
        [new() {
            Texture = attachment, ResolveTexture = SampleCount > 1 ? target : 0,
            ClearColor = new() { R = clearColor.R * alpha, G = clearColor.G * alpha, B = clearColor.B * alpha, A = clearColor.A },
            LoadOp = clear ? SDL.GPULoadOp.Clear : SDL.GPULoadOp.Load,
                StoreOp = SampleCount == 1 ? SDL.GPUStoreOp.Store : preserveSamples ? SDL.GPUStoreOp.ResolveAndStore : SDL.GPUStoreOp.Resolve,
        }];
        var pass = SDL.BeginGPURenderPass(commands, targets, 1, 0);
        if (pass == 0) return false;
        try
        {
            if (mesh.Mesh.Indices.Length == 0) return true;
            SDL.GPUBufferBinding[] vertices = [new() { Buffer = _buffers.VertexHandle }];
            var indices = new SDL.GPUBufferBinding { Buffer = _buffers.IndexHandle };
            SDL.BindGPUVertexBuffers(pass, 0, vertices, 1);
            SDL.BindGPUIndexBuffer(pass, in indices, SDL.GPUIndexElementSize.IndexElementSize32Bit);
            foreach (var batch in mesh.Batches)
            {
                SDL.BindGPUGraphicsPipeline(pass, GetPipeline(batch.Material?.Id ?? 0, format, batch.Blend != BlendMode.Opaque).Handle);
                var handle = batch.Texture.IsValid ? batch.Texture : _white!.Handle;
                var texture = Resolve(handle);
                SDL.GPUTextureSamplerBinding[] binding = [new() { Texture = texture.Native.Handle,
                    Sampler = batch.Filter == TextureFilter.Linear ? _linearSampler!.Handle : _sampler!.Handle }];
                SDL.BindGPUFragmentSamplers(pass, 0, binding, 1);
                Vector4[] uniforms = [batch.Parameters, new(_targets.Contains(handle) ? 1 : 0,
                    destination.IsValid && batch.Blend == BlendMode.Opaque ? 1 : 0, 0, 0)];
                SDL.PushGPUFragmentUniformData(commands, 0, MemoryMarshal.AsBytes(uniforms.AsSpan()), 32);
                SDL.DrawGPUIndexedPrimitives(pass, batch.Count, 1, batch.FirstIndex, 0, 0);
            }
            return true;
        }
        finally { SDL.EndGPURenderPass(pass); }
    }
}
