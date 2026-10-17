# Resource lifetime, support and known limits (v0.1)

## Ownership

Update, Render, GPU operations, audio playback and cleanup belong to the thread
running `ObeliskApp.Run` (the main STA thread on Windows). `Run` is synchronous;
it returns zero on normal window-close/Escape and propagates exceptions after
cleanup. Do not move texture creation/disposal or playback onto worker threads.

| Resource | Owner and release rule |
| --- | --- |
| `InitializeContext` devices, `PluginRegistrationContext.Assets` | Borrowed services; do not dispose host-owned devices/cache. |
| Direct `ITextureDevice.CreateTexture` / `ImagePlugin.LoadTexture` result | Caller owns Texture; dispose on the owning thread before platform shutdown. Surviving backend textures are released during shutdown. |
| `FontLoader.LoadFile` / `new Font(...)` | Caller owns Font, which owns its atlas. Dispose Font, not its atlas independently. |
| `IAssetSource.Load<T>` result | Borrowed cached value by normalized URI and type. The host disposes each distinct disposable cached result once. |
| `Sound`, `RgbaImage`, input snapshots | Managed data; no native disposal. Sound/image constructors copy input buffers. |
| `IAudioDevice.Play` result | Caller owns `ISoundPlayback`; Stop/Dispose releases playback, and device shutdown releases survivors. Keep looping playback until explicitly stopped. |
| Per-frame `RenderContext.Canvas` commands | Valid for the frame. Keep referenced Texture/Font alive through presentation; do not retain contexts or command buffers across frames. |
| `ImagePlugin` | Dispose when directly constructed; an injected HttpClient remains caller-owned. Vanilla/PluginHost own registered Plugin instances. |
| Platform/PluginHost | Advanced owning-thread infrastructure; normally managed by Vanilla. A host runs once. |

Initialize resources in `IGame.Initialize` and dispose directly owned resources in
`Shutdown`, while the platform is still alive. Game Shutdown runs only after game
Initialize succeeded: if your Initialize fails halfway, release your partial
resources before rethrowing. Registered modules are included in reverse cleanup
even if their Initialize fails. Cleanup continues after errors and can aggregate
exceptions. See [Plugin lifecycle](plugin-authoring.md#registration-callbacks-and-ownership).

Input snapshots are immutable. Held state persists; Pressed/Released are one-frame
edges (repeat is not a new press). Losing focus releases held keys/buttons.
Game time is accumulated from clamped delta, default maximum 250 ms; it is not
wall-clock time. Minimized/zero-size output suspends presentation while updates
and event pumping continue. Output pixels and initial window dimensions can differ
under display scaling; use `context.OutputSize` for layout.

## CPU loading and GPU upload

File decoding/HTTP image download produces CPU `RgbaImage` pixels. After an async
download completes, hand the pixels back to your main-thread update/init workflow
and call `CreateTexture` there. v0.1 does not provide a background GPU upload queue.
HTTP/HTTPS image reads have a 30-second limit and a 16 MiB encoded limit; decoded
images allow dimensions up to 8192 with at most 16 * 1024 * 1024 pixels.

Canvas uses output pixels, top-left origin, straight-alpha RGBA and ordered draws.
Sprite rotation is radians with origin in local destination pixels. Fonts contain
an explicit bounded character set: printable Basic Latin plus requested runes,
up to 1024 unique characters at size 8–96. Missing glyphs fail; complex shaping,
fallback fonts, bidirectional layout and a general UI system are outside v0.1.
WAV supports PCM16 mono/stereo at 8–192 kHz. Streaming OGG music is not implemented;
an unavailable audio device falls back to silent playback without stopping gameplay.

## Supported environment

The certified target is Windows x64 with an SDL_GPU-compatible GPU/driver.
The checkout pins .NET SDK 10.0.401; game/Plugin projects target .NET 10. Visual
Studio 2026 supports this target; VS Code/Rider can use SDK commands. Actual Rider
opening and a separate clean-machine distribution check remain release tasks.
Linux, macOS, x86/ARM64, mobile and web are not certified release targets.

The platform package includes patched SDL `3.4.16-obelisk.1` for Windows x64; normal
game builds need no C++ tools. Rebuilding that dependency requires the pinned
MSVC/Windows SDK from [ADR 0001](adr/0001-sdl3-csharp-binding.md). Shadercross still
requires Microsoft Visual C++ v14 Redistributable x64 on the target, including
self-contained .NET deployments. Sample fonts are read from installed Windows
Malgun Gothic; the font itself is not distributed.

## Licenses and release status

Preserve the full `licenses/` directory in game/tool distributions. The
[dependency inventory](../licenses/README.md) records SDL/Shadercross/DXC/SPIRV-Cross,
SkiaSharp, StbImageSharp, native prerequisites and the altered SDL build. A
self-contained game also includes license/notices from its actual .NET runtime
pack. Plugins, user images and fonts require their own applicable permissions.
Third-party licenses do not grant a license for the engine's own code: the engine
release license/copyright metadata remains an owner decision scheduled for Day 15.

Public NuGet publication, release archives/checksums and the public-feed-only Gate
are pending. Local-feed guides are usable now. The Day 13 Windows x64 stability
Gate ran 30 real minutes/30 cycles; it is evidence for that machine and workload,
not a guarantee for every driver or an indefinite soak test.

## Explicit limits

- One window, sequential main-thread update/render; no required scene/entity model.
- Trusted local Plugins only; no sandbox, signing, remote registry, dependency
  solver, live Plugin replacement or Assembly unload.
- No Physics, particles, post processing, 3D, networking or gamepad support.
- Low-level render-command/backend APIs remain experimental; game code uses Canvas.
- No single-file, trimmed or AOT game distribution through the CLI.
- The v0.1 public surface is frozen for the release candidate, not a promise of
  compatibility across all future 0.x releases; consult [API policy](api-compatibility.md).
