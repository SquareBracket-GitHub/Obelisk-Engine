# Canvas and the bundled Images plugin

`Obelisk.Vanilla` includes `Obelisk.Plugins.Images`. It is a separate, statically
referenced feature library today. Runtime plugin discovery/manifest registration
will be connected when the revised Day 8 plugin host is implemented. There is no
automatic network request or implicit image cache at startup.

## Load and draw a local image

Store the device and texture in your game. During `Initialize`:

```csharp
device = context.Textures;
using var images = new ImagePlugin(); // using Obelisk.Plugins.Images;
texture = images.LoadTexture(device, "assets/player.png");
```

During `Render`:

```csharp
context.Canvas.DrawSprite(texture, new RectangleF(40, 60, 128, 128),
    tint: new ColorRgba(1, 0.8f, 0.8f, 0.7f),
    rotation: angle,
    origin: new Vector2(64, 64));
context.Canvas.DrawLine(new Vector2(10, 20), new Vector2(180, 80), 4, ColorRgba.White);
context.Canvas.FillCircle(new Vector2(240, 120), 30, ColorRgba.Orange);
```

Call `texture.Dispose()` in `Shutdown` or when replacing it. Repeated disposal is
safe. Engine shutdown also releases textures that the game still owns. Creating,
drawing and disposing textures must happen on the game's owning thread. Handles
from another device or an already released texture are rejected.

## URL import without blocking the game

Keep the importer and a cancellation token alive while a request is pending:

```csharp
pendingImage = images.LoadUrlAsync(new Uri(url), cancellation.Token);
```

This returns CPU-side `RgbaImage` pixels, not a GPU texture. In `Update`, once
`pendingImage.IsCompletedSuccessfully`, call
`device.CreateTexture(pendingImage.Result)` exactly once. Observe failed/cancelled
tasks and retain a placeholder. Cancel on shutdown. The complete pattern is in
`samples/CanvasShowcase/Program.cs`.

URLs must use HTTP or HTTPS. The importer enforces a 30-second network timeout,
16 MiB encoded-byte limit (also while streaming), 8192-pixel dimension limit and
16-megapixel decoded limit. PNG is covered by file, URL and native GPU tests;
other formats supported by StbImageSharp may be used but are not all fixture-tested.
The sample's tiny authored PNG is stored as Base64 text for a reviewable fixture.
This is a desktop importer for game-selected URLs, not a server-side URL proxy.

## Coordinates, blending and batches

The origin is top-left, +X right, +Y down, in output pixels. Sprite destination
size performs scaling. A source rectangle selects texture pixels (and must lie
inside the texture). Rotation is clockwise radians. Origin is a destination-local
pivot: `position + origin + rotate(local - origin)`. With zero rotation, changing
the origin does not move the destination rectangle.

`Canvas.Blend` is captured at each draw call. Default `Alpha` uses straight-alpha
source-over; `Opaque` replaces destination pixels. Tint multiplies texture RGBA.
Sampling is nearest-neighbor with clamp-to-edge. Line segments have flat ends;
zero-length lines draw nothing. Circles use 12–128 triangles, without antialiasing.

Commands retain submission order. Only adjacent commands with the same texture
and blend mode share a draw batch, so transparent overlap order is preserved.
Shapes sample a private white texture. A minimized or zero-sized output skips
presentation and resumes after restoration; textures remain alive.

## Implementation references

- [StbImageSharp 2.30.16](https://www.nuget.org/packages/StbImageSharp/2.30.16)
- [SDL texture upload](https://wiki.libsdl.org/SDL3/SDL_UploadToGPUTexture)
- [SDL texture transfer layout](https://wiki.libsdl.org/SDL3/SDL_GPUTextureTransferInfo)
