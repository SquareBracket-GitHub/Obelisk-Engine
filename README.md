# Obelisk Engine

Obelisk is an experimental, plugin-first 2D game engine for .NET 10 and SDL3.
The Vanilla MVP includes a moving rectangle and a complete local two-player Pong
game. The v0.1 public API is still experimental while plugin and CLI work continues.

## Prerequisites

- .NET SDK 10.0.401 (pinned by `global.json`)
- Git
- Windows x64 with an SDL_GPU-compatible graphics driver for the native samples
- Windows' Malgun Gothic (`malgun.ttf`) for Pong and CanvasShowcase

## Quick Start (draft)

Clone the source, restore dependencies, and run either sample:

```powershell
git clone https://github.com/SquareBracket-GitHub/Obelisk-Engine.git
cd Obelisk-Engine
dotnet restore Obelisk.sln
dotnet run --project samples/MovingRectangle -c Debug
dotnet run --project samples/Pong -c Release
```

The rectangle uses arrow keys; Pong uses Enter to start/restart, W/S for the left
player and Up/Down for the right player. Escape or the window close button exits.
An unavailable audio device makes Pong continue silently. Dependency restoration
requires access to NuGet; the native libraries are supplied by the referenced
packages.

To start your own game, run these commands from the repository root. Public
NuGet packages and the CLI are scheduled for later stages, so this draft uses
one source reference to the Vanilla meta-project:

```powershell
dotnet new console -n MyGame --framework net10.0
dotnet add MyGame/MyGame.csproj reference src/Obelisk.Vanilla/Obelisk.Vanilla.csproj
```

Replace `MyGame/Program.cs` with:

```csharp
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Vanilla;

internal static class Program
{
    [STAThread]
    private static int Main() => ObeliskApp.Run(new MyGame(),
        new AppOptions { Title = "My Obelisk Game", Width = 960, Height = 540 });
}

internal sealed class MyGame : IGame
{
    private float _x = 40;
    public void Initialize(InitializeContext context) { }
    public void Update(UpdateContext context)
    {
        if (context.Input.Keyboard.IsDown(Key.Right)) _x += 200 * context.Time.Delta;
        if (context.Input.Keyboard.IsDown(Key.Left)) _x -= 200 * context.Time.Delta;
        _x = Math.Clamp(_x, 0, Math.Max(0, context.OutputSize.Width - 100));
    }
    public void Render(RenderContext context) =>
        context.Canvas.FillRectangle(new RectangleF(_x, 40, 100, 60), ColorRgba.Orange);
    public void Shutdown(ShutdownContext context) { }
}
```

```powershell
dotnet run --project MyGame
```

Your game directly references only `Obelisk.Vanilla`; its public types live in
`Obelisk.Abstractions`, `Obelisk.Graphics2D`, `Obelisk.Assets` and bundled plugin
namespaces. No SDL types or platform implementation references are needed.

## Build and test

```powershell
dotnet build Obelisk.sln
dotnet test Obelisk.sln --no-build
dotnet build Obelisk.sln -c Release
dotnet test Obelisk.sln -c Release --no-build
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
result for each URI/type pair. Dispose the store during the game's Shutdown,
before the platform closes, to release cached `IDisposable` assets.
A missing file reports both the URI and resolved
path; loader errors retain the original exception as their inner exception.

```csharp
using var assets = new AssetStore("assets"); // using Obelisk.Assets;
assets.RegisterLoader<string>(File.ReadAllText);
var message = assets.Load<string>("project:///message.txt");
```

For text, create `FontLoader` with `context.Textures`, font size, and any
non-ASCII characters your game will use. `LoadFile` rasterizes the basic Latin
set plus those characters into an immutable GPU atlas. Use `font.Measure(text)`
and `context.Canvas.DrawText(font, text, position, color, scale: 0.5f)`, then dispose the font
on the game's owning thread. Unlisted characters fail with their Unicode code
point; advanced shaping and runtime glyph insertion are not yet supported.
`font.Measure(text, scale)` uses the same positive, finite uniform scale as
DrawText, including line height and glyph advance. Scaling reuses the atlas.
The default scale is 1. Font rasterization uses SkiaSharp 4.148.0 and its Windows
native assets.

## Resource ownership and MVP checks

Initialize, Update, Render and Shutdown run in order on the game's owning
thread. Release game-owned GPU textures, fonts and playbacks during Shutdown.
`ObeliskApp.Run` does not dispose your game object; use `using` if it implements
`IDisposable`, as Pong does. If Initialize throws, Shutdown is not called:
clean up partially created resources in Initialize's catch block.

| Resource | Owner and release | Regression coverage |
| --- | --- | --- |
| GPU pipelines/buffers | Platform or renderer; released before GPU device destruction | Buffer reuse/growth, idempotent release and failed initialization |
| Texture | Game or AssetStore; Dispose on owning thread, platform releases survivors | Repeated create/release, stale/foreign handles, wrong-thread rejection and shutdown |
| Font atlas | Font owns its Texture; atlas release also invalidates Font | Scaled glyph pixels, repeated load/release, duplicate Dispose and device shutdown |
| Audio playback | Game holds a playback; Stop/Dispose or platform shutdown releases stream | Completion, loop refill, volume, partial failure, voice limit and shutdown |
| Sound | Immutable managed PCM owned by the caller or cache | WAV format/length validation and safe headless playback |

The Vanilla boundary tests check that MovingRectangle and Pong directly
reference only the meta-project and do not use SDL or Runtime/backend
implementation types. Native entry-point tests invoke each sample's Main,
inject keyboard and quit events, and assert a normal return and SDL shutdown.
Pong integration tests also cover a complete match, both winners, restart,
silent execution and scaled title pixels. Run these Windows tests in both
Debug and Release; they create short-lived native windows. Automated native
tests verify stream behavior and GPU output, but do not assess speaker sound
quality or replace playtesting by two people.

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

