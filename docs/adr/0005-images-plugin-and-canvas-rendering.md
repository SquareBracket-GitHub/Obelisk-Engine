# ADR 0005: Bundled image importing and complete Canvas rendering

## Status

Accepted for revised roadmap Day 4. Extends ADRs 0003 and 0004.

## Decision

File/HTTP transport and image decoding belong to `Obelisk.Plugins.Images`.
`Obelisk.Vanilla` bundles this optional library; it depends only on Graphics2D and
StbImageSharp 2.30.16. Neither Runtime nor the SDL platform references the image
decoder. The revised Day 8 plugin host will supply discovery/registration later;
today the library is statically referenced and constructed explicitly.

The core contract accepts validated RGBA8 pixels through `ITextureDevice` and
returns an explicitly disposable Texture. `InitializeContext.Textures` supplies
the platform device. HTTP download returns CPU data so an async continuation cannot
accidentally touch the GPU from a worker thread. Asset URI resolution and caching
are deferred to revised Day 5.

The renderer uploads textures through SDL transfer buffers with 256-byte row
alignment. It owns alpha/opaque pipelines, a nearest/clamp sampler, a white texture,
and vertex/index buffers. Texture handles are globally increasing engine IDs,
resolved only within their owning renderer. Disposal removes the ID; shutdown
disposes all survivors before destroying the device. Failed construction cleans up
partially allocated native resources. A failed frame recording cancels its command
buffer.

Vertices now contain position, RGBA and UV (32 bytes). Rectangle, Line, Circle and
Sprite commands build a shared triangle stream. Compatible adjacent commands
share a draw batch; no sorting changes their order. Lines use a perpendicular
half-width vector; circles use bounded triangle fans. Sprite source, scale,
clockwise rotation and destination-local pivot are applied before clip conversion.

Minimize/restore events suspend/resume presentation. Zero-size outputs also suspend,
and swapchain acquisition can return no texture without invalidating resources.
The event loop continues, with a short throttle while presentation is suspended.

## Evidence and limits

Native pixel readback tests cover file PNG → decoder → texture → sampled output,
UV quadrants, rotation, tint, blending, lines and circles at multiple output sizes
and after real window minimize/restore operations. Tests also verify resource
reuse, released handles, cross-thread release rejection, deterministic suspension
and the complete CanvasShowcase lifecycle. HTTP behavior uses a deterministic fake
transport so CI never depends on a public image host.

The actual swapchain path is integration-tested; color comparisons use offscreen
GPU targets with the same renderer. No screenshot comparison or OS display capture
is claimed. CPU mesh arrays are still rebuilt per frame. Advanced filtering,
antialiasing, device-loss recovery and runtime plugin discovery are outside Day 4.
