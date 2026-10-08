using System.Runtime.InteropServices;
using SDL3;

namespace Obelisk.Platform.Sdl3;

/// <summary>Reusable GPU buffers and staging storage; SDL cycling protects in-flight frames.</summary>
internal sealed class QuadBuffers(nint device) : IDisposable
{
    private GpuResource? _vertices;
    private GpuResource? _indices;
    private GpuResource? _transfer;
    private int _capacity;
    private bool _disposed;

    internal nint VertexHandle => _vertices?.Handle ?? 0;
    internal nint IndexHandle => _indices?.Handle ?? 0;

    public bool Upload(nint commandBuffer, QuadMesh mesh)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mesh.Indices.Length == 0) return true;
        if (!EnsureCapacity(Math.Max((mesh.Vertices.Length + 3) / 4, (mesh.Indices.Length + 5) / 6))) return false;
        var vertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan()).ToArray();
        var indexBytes = MemoryMarshal.AsBytes(mesh.Indices.AsSpan()).ToArray();
        var mapped = SDL.MapGPUTransferBuffer(device, _transfer!.Handle, true);
        if (mapped == 0) return false;
        Marshal.Copy(vertexBytes, 0, mapped, vertexBytes.Length);
        Marshal.Copy(indexBytes, 0, mapped + vertexBytes.Length, indexBytes.Length);
        SDL.UnmapGPUTransferBuffer(device, _transfer.Handle);
        var copy = SDL.BeginGPUCopyPass(commandBuffer);
        if (copy == 0) return false;
        var source = new SDL.GPUTransferBufferLocation { TransferBuffer = _transfer.Handle };
        var target = new SDL.GPUBufferRegion { Buffer = VertexHandle, Size = (uint)vertexBytes.Length };
        SDL.UploadToGPUBuffer(copy, in source, in target, true);
        source.Offset = (uint)vertexBytes.Length;
        target.Buffer = IndexHandle;
        target.Size = (uint)indexBytes.Length;
        SDL.UploadToGPUBuffer(copy, in source, in target, true);
        SDL.EndGPUCopyPass(copy);
        return true;
    }

    private bool EnsureCapacity(int count)
    {
        if (count <= _capacity) return true;
        var capacity = Math.Max(count, checked(Math.Max(16, _capacity) * 2));
        var vertexSize = checked((uint)capacity * 4 * (uint)Marshal.SizeOf<QuadVertex>());
        var indexSize = checked((uint)capacity * 6 * sizeof(uint));
        var vertexInfo = new SDL.GPUBufferCreateInfo { Usage = SDL.GPUBufferUsageFlags.Vertex, Size = vertexSize };
        var indexInfo = new SDL.GPUBufferCreateInfo { Usage = SDL.GPUBufferUsageFlags.Index, Size = indexSize };
        var transferInfo = new SDL.GPUTransferBufferCreateInfo
        {
            Usage = SDL.GPUTransferBufferUsage.Upload,
            Size = checked(vertexSize + indexSize),
        };
        var vertices = new GpuResource(SDL.CreateGPUBuffer(device, in vertexInfo), h => SDL.ReleaseGPUBuffer(device, h));
        var indices = new GpuResource(SDL.CreateGPUBuffer(device, in indexInfo), h => SDL.ReleaseGPUBuffer(device, h));
        var transfer = new GpuResource(SDL.CreateGPUTransferBuffer(device, in transferInfo), h => SDL.ReleaseGPUTransferBuffer(device, h));
        if (vertices.Handle == 0 || indices.Handle == 0 || transfer.Handle == 0)
        {
            transfer.Dispose();
            indices.Dispose();
            vertices.Dispose();
            return false;
        }
        ReleaseBuffers();
        _vertices = vertices;
        _indices = indices;
        _transfer = transfer;
        _capacity = capacity;
        return true;
    }

    private void ReleaseBuffers()
    {
        _transfer?.Dispose();
        _indices?.Dispose();
        _vertices?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBuffers();
    }
}
