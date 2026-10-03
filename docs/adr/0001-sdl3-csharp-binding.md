# ADR 0001: SDL3 C# binding and native package

- Status: Accepted
- Date: 2026-10-03

## Context

Obelisk needs an SDL3 binding that supports .NET 10, exposes current SDL3 APIs,
ships reproducible Windows native binaries, and keeps SDL types inside the
platform backend.

The initial candidates included `ppy.SDL3-CS`, the modern `SDL3-CS` package,
and other hand-written or generated bindings. Building SDL from source would
add C/C++ toolchain and packaging work before the engine's vertical slice is
proven.

## Decision

Use the following fixed package versions for the Windows-first v0.1 backend:

- `SDL3-CS` 3.4.16.2 for managed bindings
- `SDL3-CS.Windows` 3.4.16.2 for Windows native runtimes

The binding is wrapped by `Obelisk.Platform.Sdl3`. Game projects and public
engine contracts do not expose or directly reference SDL3 types.

References:

- <https://www.nuget.org/packages/SDL3-CS>
- <https://github.com/edwardgushchin/SDL3-CS>
- <https://wiki.libsdl.org/SDL3/FrontPage>

## Consequences

- Windows x86, x64, and ARM64 native binaries are restored through NuGet.
- Normal game code uses `ObeliskApp` and remains independent of SDL3.
- The backend can be replaced without changing `IGame` or the Runtime loop.
- Linux and macOS require their matching native package families when those
  platforms enter the supported release matrix.
- Native package licenses must be included in the eventual distribution.

