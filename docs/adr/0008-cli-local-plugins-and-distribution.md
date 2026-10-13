# ADR 0008: Local Plugin commands and Windows distribution

## Status

Accepted for revised roadmap Day 11, implemented 2026-10-13. Extends ADRs 0006 and 0007.

## Context

Generated independent projects need local Plugin installation/removal and a complete
game folder for Windows recipients without a .NET SDK or shared runtime. Repeated
publish must avoid stale files, including removed Plugins, while preserving an
existing distribution if preparation fails.

## Decision

The CLI references Obelisk.Plugin for the existing manifest and engine compatibility
rules. Installation reads metadata and checks entry DLL presence without loading or
executing Plugin assemblies. Validate complete package trees, relative paths and
portable directory names; reject reparse points and Windows path aliases. Copy all
package files to a unique staging directory, reread the manifest, then move to the
project's configured plugins root under its manifest name. Duplicate installations
and existing destinations are preserved. Listing reports name/version/engine range.
Removal resolves an installed manifest name, moves its owned package out of discovery,
then deletes it; failed cleanup reports its staging path. The game's existing loader
continues to validate entry types, dependency resolution and runtime lifecycle.

Use an OS file lock at artifacts/.obelisk.lock for run/dev/build/add/remove, held for
the full operation. Keep the inert file after releasing the handle so simultaneous
processes do not race over lock-file identity. Read-only listing remains available.
This serializes cooperating CLI processes; direct dotnet or executable users must
stop their games themselves. Plugin hot reload remains outside v0.1.

Build is standard dotnet publish: Release, win-x64, self-contained, apphost enabled,
single-file/trimming/AOT disabled. Native engine files are SDK publish outputs. The
CLI copies configured content roots to canonical assets/plugins beside the game;
Plugin assets/shaders/native/private dependencies retain their package paths. Build
output and metadata cannot be used as source content roots. Generated templates
exclude artifacts from Git and default SDK item globs.

Publish into a unique artifacts staging directory. Check runtime DLLs and apphost;
only then promote to artifacts/build/win-x64. Replace an existing directory only
when its ownership marker matches this CLI version and project. Move the previous
output to a unique backup and restore it if promotion fails. Never delete a backup
after failed rollback. Publish failure/cancellation leaves prior output intact; a
successful replacement removes stale files. Cleanup failure reports the owned path.
Markers and path checks protect normal workflow mistakes, not adversarial code
executing inside the same project or races caused by arbitrary filesystem mutation.

## Consequences and validation

The target is Windows x64 folder deployment; recipients copy the whole output and
need a compatible graphics driver. SDK users still configure NuGet sources. Package
publication, upgrades, dependency solving, signing and in-process Plugin unloading
remain outside this day's scope.

CLI tests cover install/list/remove, custom roots, duplicate/incompatible/missing
metadata, partial copy, locks, failed/cancelled publish and output preservation.
The external Gate packs/installs the CLI, creates an independent game, runs it,
installs SamplePlugin, publishes and relocates the distribution. It starts the .exe
with no dotnet on PATH and empty DOTNET_ROOT, checks private deployed coreclr loading,
native window and Plugin/project assets, then removes the Plugin and rebuilds without
stale files. This simulates unavailable SDK/runtime paths rather than uninstalling
the host SDK.

Reference: [.NET publish options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish).
