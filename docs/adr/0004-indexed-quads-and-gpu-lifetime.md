# ADR 0004: Indexed quads and explicit GPU resource lifetime

## Status

Accepted. Supersedes the vertex-ID rectangle drawing path in ADR 0002.

The revised Day 4 implementation extends this design in ADR 0005 (textured
32-byte vertices, multiple primitive types, alpha/opaque pipelines).

## Decision

Rectangle commands become four vertices and six 32-bit indices per quad. Each
vertex contains a clip-space position and linear RGBA color (24 bytes total).
The vertex shader forwards position and color; the fragment shader outputs color.
The pipeline is created once for the window's swapchain format and reused.

Pixel coordinates are converted with `x = 2 * pixelX / width - 1` and
`y = 1 - 2 * pixelY / height`, using the acquired swapchain's actual dimensions.
Geometry outside the output is clipped by the GPU. A full-output viewport replaces
the bootstrap's per-rectangle viewport changes. Rectangle order is preserved in
the index buffer. Alpha blending and texture sampling remain later milestones.

`QuadBuffers` owns vertex, index, and upload transfer buffers. It allocates lazily,
reuses sufficient capacity, and grows to at least the requested quad count. New
buffers must all be created successfully before replacing the old set. Partial
allocation failure releases the new resources and preserves existing buffers.
An empty frame clears/presents without allocating or uploading geometry.

CPU mesh bytes are mapped into the transfer buffer, then a copy pass uploads both
vertex and index data before the render pass on the same command buffer. Mapping
the transfer buffer and uploading destination buffers use SDL's `cycle: true`, so
SDL can supply unused backing storage when previous frames still reference it.
Public buffer handles remain reusable; backing storage may change internally.
The frame path does not wait for GPU idle.

`GpuResource` owns one native handle and a release callback. `Dispose` clears the
handle before releasing it, making repeated disposal harmless. Native operations
and disposal run on the platform's owning thread. Pipeline teardown disposes the
associated buffers and pipeline before the device is destroyed. SDL defers native
resource destruction while submitted work still uses it. Device teardown waits
for GPU idle. There is no finalizer: a GC thread cannot enforce device lifetime or
SDL thread ownership, so the platform's explicit shutdown remains mandatory.

## Validation

- CPU tests verify pixel-to-clip coordinates, colors, index offsets, and empty meshes.
- A release-callback test checks null handles and repeated disposal.
- Windows native tests render 24 frames with changing counts and empty frames.
- A native GPU readback test compares uploaded vertex/index bytes, verifies stable
  handles within capacity, growth, shrink reuse, and use-after-disposal rejection.
- These tests verify submission and buffer contents; they are not screenshot tests.

## Limitations

CPU arrays are rebuilt each frame. Pooling and more advanced batching are deferred.
Only Rectangle commands are executed by the current backend; other Canvas commands
retain their Day 7 collection-only behavior. Texture loading starts in Day 9.
