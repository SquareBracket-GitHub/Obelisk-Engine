# ADR 0003: Canvas command model and coordinate system

## Status

Accepted

## Context

Game code needs one stable 2D drawing API while the SDL_GPU implementation evolves. Exposing SDL resources or issuing native calls directly from a game would bind game logic to one backend and make rendering difficult to test without a window and GPU.

## Decision

`Obelisk.Graphics2D` is a backend-independent contract assembly. `Canvas2D` records immutable commands in submission order. `RenderContext` owns one new Canvas for each frame. A platform backend consumes those commands after the game has completed `Render`.

The public command set begins with:

- `RectangleCommand`: a filled axis-aligned rectangle.
- `LineCommand`: a line segment with pixel thickness.
- `CircleCommand`: a filled circle.
- `SpriteCommand`: a texture region drawn into a destination rectangle, with optional tint, rotation, and origin.

`TextureHandle` is an opaque engine identity. It must never contain an SDL pointer or another backend-native resource.

The Canvas coordinate system is defined as follows:

- The origin `(0, 0)` is the top-left corner of the drawable output.
- Positive X points right and positive Y points down.
- Coordinates, sizes, radii, line thicknesses, and sprite origins use output pixels.
- Rectangle X and Y identify its top-left corner; width and height must be positive.
- A circle position identifies its center.
- Sprite source rectangles use texture pixels. A missing source rectangle means the entire texture.
- Sprite rotation is measured clockwise in radians around the supplied destination-local origin.
- Commands are rendered in submission order; later commands appear over earlier commands when they overlap.

Invalid geometry, non-finite values, and invalid texture handles are rejected while collecting commands so a backend receives a well-formed command stream.

## Consequences

Game and test projects can construct a complete frame without SDL, a window, or a GPU. Backends remain responsible for translating the command stream into native rendering work. The current SDL backend renders rectangle commands while the remaining pipelines are added in subsequent milestones.
