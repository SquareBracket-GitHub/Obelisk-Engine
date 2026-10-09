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

## Repository layout

- `src/`: engine libraries and the CLI
- `samples/`: example games and plugin
- `tests/`: automated tests
- `docs/`: architecture decisions and user documentation (added as work progresses)

