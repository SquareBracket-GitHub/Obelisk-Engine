# v0.1 API review and compatibility boundary

Reviewed 2026-10-18 for the v0.1 release candidate. Existing public names and
signatures are retained; the Day 14 change documents and freezes their current
surface rather than renaming game/Plugin contracts. The version remains `0.1.0`.
The maintenance decision is recorded in [ADR 0009](adr/0009-v01-api-freeze-and-documentation.md).

| Namespace | Intended audience and reviewed boundary |
| --- | --- |
| `Obelisk.Vanilla` | Game bootstrap: `ObeliskApp.Run`, `PluginOptions`. |
| `Obelisk.Abstractions` | Game/Module callbacks, contexts, clamped time, immutable input, output size, audio/asset contracts. |
| `Obelisk.Graphics2D` | Canvas, pixel geometry/colors, Texture/Font and owned/borrowed data. Raw command records/device constructors are advanced/experimental. |
| `Obelisk.Assets` | Confined typed asset cache, WAV and bounded font loading. |
| `Obelisk.Plugin` | Trusted package entry point, registration, manifest/range validation and confined paths. PluginHost is advanced host infrastructure. |
| `Obelisk.Plugins.Images` | Optional bounded file/HTTP CPU decoding and owning-thread upload helper. |
| `Obelisk.Runtime` | Advanced host/platform/clock integration; games normally use Vanilla. |
| `Obelisk.Platform.Sdl3` | Experimental backend bootstrap and operation-specific errors; no exported SDL types or native handles. |

There is no required Scene/Entity/Component base type. Vectors use System.Numerics.
Coordinates/sizes are output pixels, time/delta semantics are explicit, and audio
gain is zero to one. TextureHandle is an engine identity, not an OS/GPU pointer.
Naming differences are intentional: Plugin is a package and Module is execution;
game bootstrap and lower-level host are separate entry points.

## Freeze enforcement and XML reference

`tests/Obelisk.Plugin.Tests/PublicApi.v0.1.txt` records exported library types and
declared public members, including parameter names/defaults, nullable contracts,
property accessors, enum values and generic constraints. Its compatibility test
compares the current assemblies against the reviewed baseline without running a
native platform. A separate test rejects native pointer/SDL types crossing the
exported signature boundary. Do not refresh the baseline merely to silence a
failure: review consumer impact, migration/version policy and documentation first.

All eight library projects generate XML documentation alongside their DLLs and in
NuGet `lib/net10.0/`. Missing public documentation (CS1591) fails the repository's
warnings-as-errors build. IntelliSense consumes the XML beside restored assemblies;
the prose guides explain workflows/ownership rather than duplicating every member.
The CLI is an executable; its supported interface is documented commands, manifest
schema and exit behavior, not its public implementation/test-injection classes.

The current surface is frozen through this release candidate. It is still a 0.x
API: future 0.x releases may change it after explicit review and migration notes.
Do not infer stable backend/GPU extension support from a type being public.
No new low-level GPU extension contract is introduced by the documentation freeze.

## Errors and declared Plugin compatibility

- Null/invalid argument or geometry: standard argument exceptions; released objects
  generally report ObjectDisposedException and wrong lifecycle/thread use reports
  InvalidOperationException.
- Asset URI syntax/root violations, malformed encoded data and manifests carry
  format/data/path diagnostics. File I/O retains the target path where applicable.
- SDL startup/presentation failures: `Sdl3Exception.Operation` and `NativeError`.
- Plugin failures: `PluginException.PluginName`, `Operation` and `InnerException`.
  Multiple shutdown failures can be AggregateException; inspect contained errors.
- Game bootstrap exceptions propagate after cleanup; CLI exit codes describe the
  process/command layer and are not an exception-to-code mapping for every API.

Plugin version strings are stable `major.minor.patch`; schema is 1. For engine
0.1.0 use a justified inclusive minimum and exclusive maximum, e.g.
`[0.1.0, 0.2.0)`. Loader preflight enforces that range and shared contract assembly
identity; it cannot prove every third-party binary is semantically compatible.
Use exact engine package references for the current development release and test
the actual Plugin on every engine version claimed. See [Plugin Authoring](plugin-authoring.md).
