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
- Patched SDL 3.4.16 (`3.4.16-obelisk.1`) for the Windows x64 native runtime

On 2026-10-16 the project owner approved building and packaging a native fix.
The Day 13 repeated-initialization probe found a leaked Windows hotplug event in
SDL 3.4.16: the notification thread was joined and callbacks unregistered, but
`s_HotplugEvent` was never closed. The source patch closes/resets it after those
operations. Engine code does not inspect or close SDL's private handles.

`scripts/Build-SdlNative.ps1` verifies the upstream release archive's SHA-256,
applies the checked-in patch to fresh source, and builds upstream's VisualC
Release/x64 project with MSVC 14.44.35207/v143 and Windows SDK 10.0.26100.0.
`native/sdl3/build.json` records source commit, archive/patch/DLL hashes and toolchain.
The patched DLL is checked in as a required runtime dependency and packed under
`runtimes/win-x64/native/SDL3.dll` in `Obelisk.Platform.Sdl3`. Source builds copy
the same file. This replaces `SDL3-CS.Windows`; the managed binding and pinned
Shadercross package are unchanged. Developers consuming engine packages do not
need a C++ compiler. Inputs and build procedure are fixed; byte-identical DLLs
across machines are not claimed.
The pinned compiler trims source-root prefixes from embedded `__FILE__` strings,
and the linker uses the portable PDB name `SDL3.pdb`. The build rejects a DLL that
still contains its absolute source/output paths before replacing the runtime.

The patch, build manifest and original SDL license travel with distribution
notices. Packaged integration checks the DLL hash in restored and published games.
When upstream provides a fixed native release, reassess this patch and rerun the
native probe, all regressions/distribution tests and the full stability Gate.

The binding is wrapped by `Obelisk.Platform.Sdl3`. Game projects and public
engine contracts do not expose or directly reference SDL3 types.

References:

- <https://www.nuget.org/packages/SDL3-CS>
- <https://github.com/edwardgushchin/SDL3-CS>
- <https://wiki.libsdl.org/SDL3/FrontPage>

## Consequences

- The patched native package supports Windows x64, the v0.1 release target.
  x86/ARM64 need separately built and validated native assets before support.
- Normal game code uses `ObeliskApp` and remains independent of SDL3.
- The backend can be replaced without changing `IGame` or the Runtime loop.
- Linux and macOS require their matching native package families when those
  platforms enter the supported release matrix.
- Native package licenses must be included in the eventual distribution.

