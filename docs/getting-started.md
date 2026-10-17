# Getting Started (v0.1)

This guide creates an independent game, edits it, adds a locally built Plugin and
publishes a Windows x64 distribution. Run the PowerShell commands in order.
The engine packages are **not yet public on NuGet**; public publication and its
clean-cache verification are release work. The local feed below is required today.

## Requirements

- Windows x64 and an SDL_GPU-compatible graphics driver.
- .NET SDK 10.0.401 for the engine checkout, Git and PowerShell 7 (`pwsh`).
- Microsoft Visual C++ v14 Redistributable **x64** for Shadercross dependencies.
  See the [runtime prerequisite](../README.md#prerequisites).
- A C# editor is optional. Package consumers do not need a C++ compiler.

## Prepare the local packages

```powershell
git clone https://github.com/SquareBracket-GitHub/Obelisk-Engine.git
Set-Location Obelisk-Engine
$engineRoot = (Get-Location).Path
foreach ($project in Get-ChildItem src/*/*.csproj) {
    dotnet pack $project.FullName -c Release -o artifacts/packages
    if ($LASTEXITCODE -ne 0) { throw "Package build failed: $project" }
}
$obeliskFeed = (Resolve-Path artifacts/packages).Path
```

For the CLI flow below, install the tool (ZIP generation can skip global tool
installation; install it later before the Plugin/build steps):

```powershell
dotnet tool install --global Obelisk.Cli --version 0.1.0 --add-source $obeliskFeed
obelisk --version
```

Expected version: `0.1.0`. Reopen the terminal if the SDK reports the tool directory
is missing from PATH; preserve `$engineRoot` and `$obeliskFeed` as absolute paths.
For an existing tool use `dotnet tool update --global Obelisk.Cli --version 0.1.0
--add-source $obeliskFeed`; uninstall with `dotnet tool uninstall --global Obelisk.Cli`.
Development packages of the same version can be cached; use a fresh NuGet cache
when validating a rebuilt `0.1.0`. Do not overwrite another application's cache.

## Choose CLI or Starter ZIP

Create a workspace outside the engine checkout:

```powershell
New-Item -ItemType Directory -Force ../Obelisk-Games | Out-Null
Set-Location ../Obelisk-Games
$gamesRoot = (Get-Location).Path
```

For the CLI path:

```powershell
obelisk new MyGame
Set-Location MyGame
```

Alternatively, generate/extract the Starter ZIP without installing the CLI:

```powershell
Set-Location $engineRoot
pwsh -NoProfile -File scripts/Build-Starter.ps1
Expand-Archive artifacts/starter/Obelisk-Starter-v0.1.0.zip -DestinationPath $gamesRoot
Set-Location (Join-Path $gamesRoot 'ObeliskStarter')
```

Choose a new destination; neither generator merges into existing projects.
The ZIP and CLI use the same template and exact `Obelisk.Vanilla [0.1.0]` reference.
Moving the game directory does not require moving the engine source, but restore
still needs the local package feed.

Configure the feed **in the game directory**, so later `obelisk run/build` restores
can find the packages too:

```powershell
dotnet new nugetconfig
dotnet nuget add source $obeliskFeed --name obelisk-local --configfile NuGet.Config
dotnet restore --configfile NuGet.Config
dotnet build --no-restore
dotnet run --no-build
```

Keep nuget.org enabled for third-party dependencies. If a NuGet.Config already
exists, edit its sources rather than replacing it. The generated Game draws a
green rectangle; arrow keys move it. Close the window or press Escape to exit.

## Open an editor and edit the game

- VS Code: open the game directory (`code .`) with C# support/C# Dev Kit installed.
- Visual Studio 2026 (18.0+ for supported .NET 10 targeting): **File > Open >
  Project/Solution**, select the generated `.csproj`.
- Rider: **File > Open**, select the `.csproj` to open a .NET project. Actual Rider
  validation remains release work; the SDK commands are the verified fallback.

In `Game.cs`, replace the movement speed `200` with `300`, and replace the color
`new ColorRgba(0.15f, 0.8f, 0.45f, 1)` with `ColorRgba.Orange`. Save, then run:

```powershell
obelisk run
```

The rectangle is orange and moves faster. The Starter path can instead use
`dotnet run`. Installing the CLI later enables the same `run`, `dev` and `build`
commands. `obelisk dev` restarts after saved changes; stop it before adding Plugins.
For an example of arguments: `obelisk run -- "two words"` forwards one game argument.
See the [CLI Reference](cli-reference.md) for commands, manifests and exit codes.

## Build and add your first Plugin

Follow [Plugin Authoring Guide](plugin-authoring.md#build-a-local-plugin) to create
the independent `TutorialPlugin` next to the game, using the same `$obeliskFeed`.
The guide includes the complete project, manifest and C# entry point; no engine
checkout reference is needed. Back in the game directory with the game stopped:

```powershell
obelisk add ../TutorialPlugin/bin/Release/net10.0
obelisk list
obelisk run
```

`list` reports `tutorial 0.1.0` with engine range `[0.1.0, 0.2.0)`; the game now
draws an additional white badge from the Plugin. Close it before continuing.
`obelisk remove tutorial` removes it for the next startup. Installation copies the
whole package and does not execute it; registration happens on game startup.

## Publish, move and run

```powershell
obelisk build
```

The complete output is `artifacts/build/win-x64`. Copy **the whole directory** to
a new location on a compatible Windows x64 machine, then run `MyGame.exe` (or
`ObeliskStarter.exe`). Keep assets, Plugins, native DLLs, ownership metadata and
`licenses/` together. No .NET SDK or shared .NET runtime is required on the target;
the graphics driver and VC++ x64 prerequisite still apply. Rebuild can replace
only CLI-owned output; keep personal files outside it.

The engine's own release license is not selected yet; see [resource ownership,
support and release limitations](resources-and-support.md). Standalone release
packages, public NuGet-only verification and clean-machine certification remain
Day 15 work. The current local distribution Gate checks a relocated game with
the SDK/shared runtime unavailable through its environment.

## If a step fails

- `obelisk` missing: reopen the terminal or check the SDK's tool-directory PATH.
- `NU1101` / package missing: check the game's NuGet.Config and local feed path.
- Native/graphics startup error: check Windows x64, the VC++ runtime, GPU driver and
  that the full output directory was copied. Preserve the operation/error text.
- Plugin startup failure: use `obelisk list`, check manifest/DLL/type/range and
  retain the Plugin name and operation from the exception. Stop the game before repairs.
- Inspect [CLI exit codes](cli-reference.md#exit-codes) rather than assuming every
  nonzero result is a CLI argument error.
