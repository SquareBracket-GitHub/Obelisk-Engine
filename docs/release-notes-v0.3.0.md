# Obelisk Engine 0.3.0

Release date: 2026-11-12. .NET 10 / SDL3 / SDL_GPU.

## Changes

- Add macOS SDL3, Shadercross and SkiaSharp native assets. Apple Silicon games
  and distributions are exercised on a real Mac; Intel Mac runtime assets are
  included but native Intel execution is not certified.
- Add `obelisk build --runtime win-x64|osx-arm64|osx-x64`. The existing bare build
  command retains its Windows x64 default. Native dependencies and runtime notices
  remain in the output; copy the entire distribution directory.
- Add opt-in terminal debug control for `run` and `dev`: mouse/key input, state
  queries, pause/resume, fixed-delta frame stepping, PNG capture and normal quit.
  Read the separate [terminal debugging guide](terminal-debugging.md).
- Correct stale empty output plugin directories after plugin removal, without
  ignoring malformed nonempty runtime packages or deleting nonempty user files.
- Adapt sample font paths for macOS. No operating-system font is redistributed.
- Retain native dependency license notices, source archives and provenance.

## Package versions and compatibility

The seven engine libraries and CLI use **0.3.0**. Official plugin packages use
**Images 0.1.2**, **Text 0.1.1** and **Effects 0.1.1**.

All v0.2 public member signatures and contract assembly identities are retained.
The optional Runtime `IFrameControl` interface is additive; SDL implements it
explicitly. Existing schema-1 project files declaring engineVersion 0.2.0 remain
accepted by the new CLI; new projects declare 0.3.0.

Plugin compatibility now checks the actual engine version **0.3.0**, rather than
the alpha's temporary 0.2.0 value. A plugin with exclusive maximum 0.3.0 is
intentionally rejected until its supported range has been validated and updated.
Official packages declare `[0.2.0, 0.4.0)`. Native plugins need binaries for the
chosen target OS and architecture. Binary signature retention does not certify
untested third-party plugins.

## Installation

```sh
dotnet tool install --global Obelisk.Cli --version 0.3.0
# Existing installation:
dotnet tool update --global Obelisk.Cli --version 0.3.0
# Existing game:
dotnet add package Obelisk.Vanilla --version 0.3.0
```

## Verification and limits

Verification records are stored with the release candidate, including package
hashes, source commit, tests and isolated package-consumption gates. Apple Silicon
window transitions, CLI/debug control, PNG pixels, offscreen rendering/MSAA and
plugin removal/restart are covered. Native Windows CI uses SDL's clocked dummy
audio driver because hosted runners have no physical playback device; this does
not certify audible hardware quality.

The macOS minimize/restore failure observed in the alpha was a test-harness timing
assumption. The harness now waits for native state completion and verifies logical
window sizes separately from Retina drawable pixels; the quick stability gate
passes without suppressing the state assertions.

`.app` bundling, signing, notarization, Gatekeeper certification, Intel Mac native
execution and separate SDK-free-machine certification remain outside this release's
verified scope. Use the official .NET SDK for development/publishing; runtime packs
without required license notice files are rejected by the distribution gate.
