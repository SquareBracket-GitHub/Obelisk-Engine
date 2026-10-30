# ADR 0010: v0.2 engine rendering mechanisms and independent plugin versions

Date: 2026-10-30

Status: Implemented for the local v0.2.0 candidate; publication is not approved.

## Context

User experience exposed missing wheel input, bitmap text scaling limitations,
static glyph atlases, geometry aliasing and demand for blur effects. The owner
requested engine work first, with official plugins versioned independently.
The v0.1 plugin lifecycle offers public Canvas access but no native GPU handles.

## Decision

Keep input capture, texture sampling/update, DPI/output coordinates, MSAA,
resource ownership, ordered offscreen passes and an experimental fragment material
contract in the engine. Dynamic glyph packing, font fallback, raster/MSDF selection,
blur algorithms and UI behavior remain optional official/plugin implementations.

Extend the existing owning-thread factory with `IRenderDevice`; acquire it through
`InitializeContext.Graphics`, including inside plugin module initialization.
Existing `ITextureDevice` implementers retain source compatibility via a default
unsupported `UpdateTexture` method. Public interfaces contain no SDL/native types.

Capture filter, borrowed material and four finite constants with each draw.
Batch only adjacent commands with matching state, preserving overlap order. Plugin
module rendering restores all four Canvas states in a finally block, including
on failure. Materials use the fixed sprite vertex contract and a compiled HLSL
`Shade` function, one sampler and one float4 parameter; this contract is experimental.

An ordered `RenderTo` pass snapshots a child Canvas. Destinations are owned RGBA8
textures, cleared before use and readable by later passes. Reject nested passes,
feedback, foreign handles and disposed resources. Cache pipelines by material,
color format and blend mode; sample count is immutable for a renderer. Release
target MSAA attachments with their target and replace the backbuffer attachment
on resize, without an unbounded cache of old dimensions.

Offscreen alpha-blended output is internally premultiplied. Clear colors and opaque
offscreen writes follow that representation; the engine's `SampleImage` helper
returns straight RGBA for the public material contract. This avoids double alpha
multiplication when a transparent target is later composited. CPU image uploads
remain straight RGBA. Partial texture updates do not cycle away untouched texels.

Request four MSAA samples by default and fall back to a supported count, at most
the requested 1/2/4/8. Query both swapchain and offscreen texture formats. Large
circles use adaptive subdivision to bound chord error for ordinary output sizes.
Use high-density windows, drawable pixel dimensions and matching mouse coordinates;
expose display scale without implementing font DPI/cache policy in the backend.

Engine libraries and CLI become `0.2.0`. Images' implementation stays unchanged,
but rebuilding its engine dependencies and compatibility metadata is its own
`0.1.1` patch, targeting engine `[0.2.0,0.3.0)`. Package and manifest versions agree.
Vanilla reads the plugin assembly version. No existing published package is
overwritten. Candidate inventories use per-package versions instead of assuming
all plugins share the engine version. Retain the original v0.1 API baseline and
record the additive v0.2 surface separately.

## Validation and limits

CPU tests cover captured state, pass snapshots, fractional wheel reset, validation,
resource disposal retry and plugin state isolation. Actual SDL/GPU tests cover
normalized wheel events, region preservation, nearest/linear sampling, custom
materials, ordered passes, transparent compositing, MSAA coverage, resize and
resource rejection/cleanup. A fresh-cache package-only game exercises the public
contracts and normal shutdown outside the repository.

This does not implement a render graph, compute shader API, multi-input materials,
dynamic text cache, blur or MSDF plugin. MSAA cannot restore bitmap detail. Different
monitor DPI transitions and non-Windows backends are not certified by these tests.
Rider and separate SDK-free Windows certification remain owner-deferred.
