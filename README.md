# Obelisk Engine

Obelisk is an experimental, plugin-first 2D game engine for .NET 10 and SDL3.
The repository is currently building the first playable slice of the v0.1 engine.

## Prerequisites

- .NET SDK 10.0.401 (pinned by `global.json`)
- Git

## Build and test

```powershell
dotnet build Obelisk.sln
dotnet test Obelisk.sln --no-build
```

## Run the current sample

```powershell
dotnet run --project samples/MovingRectangle/MovingRectangle.csproj
```

The sample opens a resizable SDL3 GPU window and draws an orange rectangle.
Move it with the arrow keys. Movement uses frame delta time, and the rectangle
remains inside the drawable area after a resize. Press Escape or use the window
close button to exit.

## Texture, Sprite and shapes sample

```powershell
dotnet run --project samples/CanvasShowcase
dotnet run --project samples/CanvasShowcase -- --image "C:\images\sprite.png"
dotnet run --project samples/CanvasShowcase -- --url "https://your-host.example/sprite.png"
```

The scene combines rotating/tinted sprites, translucent rectangles, lines,
circles, and Latin/Korean text. The text sample uses Windows' Malgun Gothic
(`malgun.ttf`); that font must be installed. Resize, minimize and restore the
window; Escape exits. URL imports run
asynchronously while the default image remains visible. A failure is reported to
stderr and keeps the default image.

Image importing is provided by the bundled `Obelisk.Plugins.Images` library,
included by `Obelisk.Vanilla`. The renderer only receives RGBA pixels. See
[the image and Canvas guide](docs/canvas-and-images.md) for resource lifetime,
URL loading and the current limits.

## Asset URIs and text

`AssetStore` confines `project:///file` to a project asset root and
`plugin://name/file` to a registered plugin root. Traversal and symlink paths
are rejected. Register a loader once per type, then `Load<T>` caches the first
result for each URI/type pair. Dispose the store after game shutdown to release
cached `IDisposable` assets. A missing file reports both the URI and resolved
path; loader errors retain the original exception as their inner exception.

```csharp
using var assets = new AssetStore("assets"); // using Obelisk.Assets;
assets.RegisterLoader<string>(File.ReadAllText);
var message = assets.Load<string>("project:///message.txt");
```

For text, create `FontLoader` with `context.Textures`, font size, and any
non-ASCII characters your game will use. `LoadFile` rasterizes the basic Latin
set plus those characters into an immutable GPU atlas. Use `font.Measure(text)`
and `context.Canvas.DrawText(font, text, position, color)`, then dispose the font
on the game's owning thread. Unlisted characters fail with their Unicode code
point; advanced shaping and runtime glyph insertion are not yet supported.
Font rasterization uses SkiaSharp 4.148.0 and its Windows native assets.

## Play Pong

```powershell
dotnet run --project samples/Pong
```

Enter starts a local two-player match. The left player uses W/S and the right
player uses Up/Down. First to five points wins; Enter on the result screen
restarts with fresh scores. M toggles sound, and Escape or the window close
button exits. The court and text scale together with the window.
The sample requires Windows' Malgun Gothic font, like CanvasShowcase.
It includes textured paddles, text, three effect sounds and looping WAV music.
The PCM tones and melody were generated for this sample, with no external audio
assets; its checker image is shared with CanvasShowcase.

## Audio

`InitializeContext.Audio` supplies an `IAudioDevice`, without SDL types. Load a
sound with `WavLoader.LoadFile(path)` or register `WavLoader.LoadFile` as an
`AssetStore` loader for `Sound`. The WAV loader works without an audio device,
owns a copy of the decoded PCM, and supports RIFF/WAVE PCM16 mono/stereo at
8–192 kHz. Encoded files are limited to 17 MiB and PCM data to 16 MiB. Compressed
WAV, floating point WAV, OGG and streaming file decoding are not supported yet.

```csharp
var sound = WavLoader.LoadFile("assets/hit.wav");
using var playback = context.Audio.Play(sound, loop: false, volume: 0.5f);
playback.Volume = 0.25f; // Finite range: 0 through 1.
playback.Stop();        // Also released by Dispose or platform shutdown.
```

Use the audio API on the thread that initialized the platform. Each playback
owns one SDL stream and supports independent volume; SDL performs conversion
and mixing. At most 128 simultaneous voices are allowed. Completed one-shots
are released automatically when the platform next presents a frame. Sound is
managed immutable data and does not require disposal. Device shutdown stops
all remaining voices before releasing SDL's audio subsystem; stop/dispose is
idempotent, including after shutdown.

The backend queues about 100 ms of source PCM and replenishes it on each frame,
including while minimized. A long blocked game frame can interrupt looping
audio. `IsPlaying` tracks source and stream data, so the final hardware-buffered
tail may remain briefly after it becomes false. This implementation follows
[SDL's audio stream API](https://wiki.libsdl.org/SDL3/SDL_OpenAudioDeviceStream).
If the audio subsystem or default playback device cannot open, `IsAvailable`
is false and `UnavailableReason` explains why. `Play` then returns an inactive,
safe playback and the game continues silently. Stream failures after a device
has opened are reported as audio-specific exceptions.

## Repository layout

- `src/`: engine libraries and the CLI
- `samples/`: example games and plugin
- `tests/`: automated tests
- `docs/`: architecture decisions and user documentation (added as work progresses)

