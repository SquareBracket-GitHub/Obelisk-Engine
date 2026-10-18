# Obelisk Engine

Obelisk is an experimental, plugin-first 2D game engine for .NET 10 and SDL3.
The Vanilla MVP includes a moving rectangle and a complete local two-player Pong
game, with local Plugin discovery, scoped Asset loaders and Module lifecycle hooks.
An installable CLI generates independent package-based projects. The v0.1 release
candidate API surface is reviewed and frozen; advanced backend/GPU contracts remain
experimental. Publication verification is tracked in the candidate release notes.

## Documentation

| Guide | Use it for |
| --- | --- |
| [Getting Started](docs/getting-started.md) | Public/local packages, CLI/Starter, editor setup, code edits, Plugin and first distribution. |
| [Plugin Authoring](docs/plugin-authoring.md) | Complete package-only Plugin project, manifest, lifecycle and ownership. |
| [CLI Reference](docs/cli-reference.md) | Commands, project schema, process behavior and exit codes. |
| [Resource lifetime and support](docs/resources-and-support.md) | Owned/borrowed resources, thread rules, platform prerequisites and known limits. |
| [API compatibility](docs/api-compatibility.md) | v0.1 freeze boundary, error model, XML reference and compatibility tests. |

For a first game, follow Getting Started in order. IDE IntelliSense documentation
ships with all eight library packages; the CLI's supported interface is its commands.

## Prerequisites

- .NET SDK 10.0.401 (pinned by `global.json`)
- Git
- Windows x64 with an SDL_GPU-compatible graphics driver for the native samples
- [Microsoft Visual C++ v14 Redistributable (x64)](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist):
  Shadercross dependencies import the VC++ runtime, including in self-contained games
- Windows' Malgun Gothic (`malgun.ttf`) for Pong and CanvasShowcase

## Install the CLI and create a game

For a version available on nuget.org, use the public-package flow without a source
checkout or local feed:

```powershell
dotnet tool install --global Obelisk.Cli --version 0.1.0
obelisk new MyGame
Set-Location MyGame
obelisk run
```

For an unpublished development build, create a local feed from the repository root
and install the .NET global tool using standard .NET packaging:

```powershell
foreach ($project in Get-ChildItem src/*/*.csproj) {
    dotnet pack $project.FullName -c Release -o artifacts/packages
    if ($LASTEXITCODE -ne 0) { throw "Package build failed: $project" }
}
$obeliskFeed = (Resolve-Path artifacts/packages).Path
dotnet tool install --global Obelisk.Cli --version 0.1.0 --add-source $obeliskFeed
obelisk --help
obelisk info
```

If .NET asks you to reopen the terminal, do so to make the tool directory available
on PATH. For an existing CLI installation use `dotnet tool update --global
Obelisk.Cli --version 0.1.0 --add-source $obeliskFeed`; remove it with `dotnet tool
uninstall --global Obelisk.Cli`. Installation follows the
[.NET tool options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install).

Create an independent game beside the engine checkout, then restore from the local
feed and NuGet for third-party dependencies:

```powershell
New-Item -ItemType Directory -Force ../Obelisk-Games
Set-Location ../Obelisk-Games
obelisk new MyGame
Set-Location MyGame
dotnet restore --source $obeliskFeed --source https://api.nuget.org/v3/index.json
dotnet build --no-restore
dotnet run --no-build
```

The generated project references only `Obelisk.Vanilla` at exact version `[0.1.0]`,
with no engine source path. Arrow keys move the rectangle; close the window to exit.
It includes Program.cs, Game.cs, the standard .csproj, obelisk.json, assets/,
plugins/, .gitignore and README.md. Assets and Plugin files are copied to build and
publish output. Open the .csproj in Visual Studio or Rider, or the folder in VS Code
with C# support. Later restore operations must still have access to the local feed
until public packages are published; configure a NuGet source or keep passing
`--source` as above.

## Starter ZIP and editor setup

The Starter archive is generated from the same CLI template and version as
`obelisk new ObeliskStarter`:

```powershell
pwsh -NoProfile -File scripts/Build-Starter.ps1
Expand-Archive artifacts/starter/Obelisk-Starter-v0.1.0.zip -DestinationPath ../Obelisk-Games
Set-Location ../Obelisk-Games/ObeliskStarter
dotnet restore --source $obeliskFeed --source https://api.nuget.org/v3/index.json
dotnet build --no-restore
dotnet run --no-build
```

`$obeliskFeed` is the local feed from the installation steps above. Copy or move
the extracted `ObeliskStarter` directory wherever you work; the project contains
no link to the engine checkout. Until the packages are published, each machine
must have the local feed available for restore.

After extraction or `obelisk new`, open the project in an editor:

- VS Code: install the [C# Dev Kit](https://code.visualstudio.com/docs/csharp/get-started),
  open the project folder with `code .`, and use its terminal for the commands above.
- Visual Studio 2026 (18.0+) is required for supported `net10.0` targeting.
  Choose **File > Open > Project/Solution** and select `ObeliskStarter.csproj`
  (or the generated project's `.csproj`). See the
  [.NET/Visual Studio version matrix](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs).
- Rider: choose **File > Open** and select the `.csproj`; selecting only the
  directory opens a folder rather than a .NET solution.

The CLI and SDK commands work independently of these editors. The Starter archive
is a generated artifact under `artifacts/starter/`; regenerate it after changing
the CLI template or engine version.

`new` accepts 1–64 ASCII identifier characters, starting with a letter or underscore;
C# keywords and Windows device names are reserved. It creates a new child directory,
rejects existing paths, and stages the complete template before moving it into
place. Failed writes or a concurrent destination collision remove only its temporary
directory, preserving existing files. If cleanup itself fails, the error identifies
the leftover temporary path. No restore or build runs implicitly during generation.

Commands are `new <name>`, `run [-- <game arguments>]`, `dev [-- <game arguments>]`,
`build`, `add <local-path>`, `remove <plugin>`, `list`, `info`, `help`, `--help`, `-h`
and `--version`. `info`
reports CLI/template versions, runtime and platform without requiring a project.
Exit codes are 0 for success, 1 for operational failures and 2 for invalid arguments;
diagnostics go to stderr. `run` also propagates SDK/game exit codes; Ctrl+C returns
130 after stopping owned processes. `build` propagates publish exit codes too.

Inside a generated game directory, use `obelisk run` for one execution or
`obelisk dev` for automatic restarts. Both validate obelisk.json, then run standard
`dotnet restore`, `dotnet build --no-restore`, and `dotnet run --no-build --no-restore`.
Configure a NuGet source for the local feed before using these commands. Arguments
after `--` are passed unchanged to the game, for example `obelisk run -- "two words"`.

The manifest requires exactly schemaVersion (1), name, engineVersion (0.1.0),
project, assets and plugins. Unknown, duplicate or missing fields are errors.
Paths must be relative, without traversal or reparse points; project must name an
existing .csproj. Asset/Plugin directories may be absent. Those fields determine
watched content roots; custom output paths still require matching .csproj content
rules and game startup options.

With the game stopped, install a local Plugin package directory with
`obelisk add "C:\packages\sample"`, inspect installed versions and engine ranges
with `obelisk list`, and remove by manifest name with `obelisk remove sample`.
The package needs obelisk.plugin.json and its entry DLL; assets, shaders, native
files and private dependencies are copied with the complete folder. Installation
uses the existing Plugin schema/version checks and validates every path without
executing Plugin code. The game's loader validates the DLL and entry type on startup.
Unknown, duplicate or incompatible manifests, missing DLLs, links and path traversal
are rejected. Packages are staged before moving into the manifest's plugins root.
An existing installation is preserved; remove it before installing a new version.
Removal takes effect on the next process startup. If cleanup is blocked by file
permissions or locks, the error identifies the owned leftover staging directory.

`run`, `dev`, `build`, `add` and `remove` share an OS file lock under artifacts;
concurrent CLI operations fail with guidance to stop the other command. Stop a
game started directly with dotnet or its .exe yourself before changing Plugins.
`list` is read-only and remains available while development runs. Plugin code is
trusted and runs with game permissions; installation is not a sandbox or dependency
solver.

`obelisk build` runs standard dotnet publish in Release for win-x64 with
`--self-contained true`, apphost enabled, and single-file, trimming and AOT disabled
to preserve dynamic Plugin loading. It builds in a fresh staging directory, checks
the runtime/apphost, and copies the configured assets/plugins roots to canonical
`assets`/`plugins` directories beside the executable. Use Vanilla's default runtime
paths for this layout. Source roots cannot use artifacts, bin/obj or editor metadata.
Native engine dependencies come from SDK publish; Plugin-private native files retain
their package layout.

Output is `artifacts/build/win-x64`. Copy this whole folder to a Windows x64 machine
with a compatible graphics driver and the VC++ x64 runtime above, then run the game's .exe; no .NET SDK or shared
runtime installation is needed. Generated .gitignore and .csproj exclude artifacts
from source control and default build items. Rebuild replaces only an output with
this CLI/project's ownership marker, eliminating stale files and removed Plugins.
Unowned output is preserved and rejected. Publish failures or cancellation preserve
the previous build. Treat the output as generated: save custom files elsewhere.
Older templates should add `artifacts/` to .gitignore and `artifacts/**` to
DefaultItemExcludes before repeated publishing.

`dev` watches source/project/configuration files and all files under the configured
assets/plugins roots. It ignores bin/obj, version-control/editor metadata and
artifacts. A 300 ms quiet period combines save bursts. It stops the previous process
tree before rebuilding and starts one game for the latest changes. Changes during
preparation discard the stale launch. Build or subsequent manifest errors wait for
another save; a normally closed game also waits for a change. An invalid initial
manifest fails immediately with its path and repair guidance.

On Windows, SDK processes and their descendants are assigned to private Job Objects.
Restart waits for the owned tree to terminate, and closing or forcibly terminating
the CLI releases the job handles and stops assigned descendants. Other platforms
use normal cancellation and process-tree termination; forced CLI termination cleanup
is currently guaranteed only by the Windows implementation.

To reproduce the Day 9 packaging Gate on Windows:

```powershell
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1
# Include the Day 10 run/dev Gate:
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -DevelopmentLoop
# Include Days 9–11 and the relocated distribution Gate:
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -Distribution
# Include the Day 12 Starter equivalence, relocation and native-run Gate:
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -Starter
```

The script creates a fresh directory outside the repository, packs the engine and
CLI, installs the global CLI in an isolated `DOTNET_CLI_HOME`, and uses an isolated
NuGet cache. It checks commands/exit codes, generates MyGame and legal name edge
cases, restores/builds from packages only, opens the generated game's native window
and closes it normally. `-DevelopmentLoop` additionally checks exact game argument
forwarding, a burst of code saves causing one restart, previous-game termination,
and descendant cleanup after killing only the CLI. `-Distribution` includes those
checks, then installs/lists/removes SamplePlugin, builds self-contained output,
and runs a relocated native game with dotnet removed from PATH and DOTNET_ROOT
pointing to an empty directory. It verifies that the game's loaded coreclr comes
from its deployed folder and that Plugin/project assets work; a repeat build must
remove stale output. This simulates an unavailable SDK/shared runtime on the test
machine, rather than uninstalling its SDK. Default cleanup removes its own temporary workspace;
`-KeepWorkspace` retains it for inspection. Existing user global tools remain
unchanged. Third-party notices, the engine MIT license and XML API documentation
are included. All nine `0.1.0` packages are available on nuget.org.

`-Starter` generates the ZIP, compares its project files byte-for-byte with the
installed CLI output, copies and moves the extracted project, then restores,
builds and runs both projects with a native SDL window. The default uses isolated
local packages. The `-PublicFeed -ReleaseDirectory` flow below uses only nuget.org;
the complete public-package flow passed for `0.1.0` on 2026-10-18.

## Release candidate preparation

Build all nine NuGet packages (eight libraries plus the CLI tool), a self-contained
Windows x64 CLI ZIP and the matching Starter ZIP in a new, preserved directory:

```powershell
pwsh -NoProfile -File scripts/Build-ReleaseCandidate.ps1 -OutputDirectory artifacts/rc-first
pwsh -NoProfile -File scripts/Verify-ReleaseCandidate.ps1 -Directory artifacts/rc-first
pwsh -NoProfile -File scripts/Verify-StandaloneCli.ps1 -Directory artifacts/rc-first
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -ReleaseDirectory artifacts/rc-first -Distribution -Starter -Documentation
# Rebuild to a fresh directory, then compare every expanded archive entry:
pwsh -NoProfile -File scripts/Build-ReleaseCandidate.ps1 -OutputDirectory artifacts/rc-second
pwsh -NoProfile -File scripts/Verify-ReleaseCandidate.ps1 -Directory artifacts/rc-first -CompareDirectory artifacts/rc-second
# After publishing these versions, test with only nuget.org and fresh caches:
pwsh -NoProfile -File scripts/Publish-ReleaseCandidate.ps1 -Directory artifacts/rc-first
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -ReleaseDirectory artifacts/rc-first -PublicFeed -Distribution -Starter -Documentation
```

`-ReleaseDirectory` uses the candidate's exact package/Starter bytes and checks
their manifest and SHA256 inventory before the external user flow. `-PublicFeed`
omits the local package source entirely and isolates both package and HTTP caches.
Archive payload comparison ignores ZIP timestamps and NuGet's generated relationship
IDs; NuGet ZIP hashes may differ on a rebuild. `SHA256SUMS` always describes the
actual files in that candidate. Source revision/dirty state and SDK are recorded;
these unsigned checksums detect corruption but do not authenticate the publisher.

The standalone CLI includes a private .NET runtime and its notices. Help, info,
version and project creation work without a discoverable SDK; development commands
still need the .NET SDK. The local PATH-isolated check does not certify a separate
SDK-free Windows machine. The engine uses MIT, Copyright (c) 2026 Taegyun Eom.
Public NuGet publication and the public-feed-only CLI/Starter flow passed on
2026-10-18. Actual Rider recognition and separate
clean-machine certification are deferred by the owner and have not passed.
A build does not create a final Git tag or approve publication. See the
[candidate release notes](docs/release-notes-v0.1.0.md).
Publication is an explicit command: it requires a candidate from a clean source
commit, approved MIT metadata and `NUGET_API_KEY` in the process environment or
ignored project `.env`. Never commit that key. Packages are pushed to nuget.org
in dependency order; duplicate versions and failures stop the operation with the
completed package IDs reported, without deleting packages or changing versions.
Version `0.1.0` has already been published; do not republish it. The published
packages and archived candidate were built from `5a5d5b1`; use that revision in a
separate checkout to recreate them. Documentation updates after publication do
not change the immutable package files. The proposed `v0.1.0` tag targets that
build revision; no final tag has been created.

## Run the repository samples

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
requires access to NuGet; the platform supplies its patched SDL DLL and the
referenced native dependency packages.

To start your own game directly from source, run these commands from the repository
root. This alternative uses one source reference to the Vanilla meta-project;
the CLI above generates a package-only project instead:

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

## Stability verification and distribution notices

On Windows, run the complete Day 13 scenario with a compatible GPU, the pinned
.NET SDK and the sample prerequisites above:

```powershell
pwsh -NoProfile -File scripts/Verify-Stability.ps1
# Only open/close the three original Sample executables in Debug and Release:
pwsh -NoProfile -File scripts/Verify-Stability.ps1 -SmokeOnly
# Short diagnostic run; this does not satisfy the 30-minute Gate:
pwsh -NoProfile -File scripts/Verify-Stability.ps1 -Quick
```

The full command runs the regression suites and all six Sample/configuration
combinations, then repeats the actual Sample game implementations for at least
30 real minutes in one process. The stability executable links their source files
to avoid adding test switches to the games. Each cycle initializes a fresh SDL/GPU
platform, renders, minimizes/restores twice, resizes the two resizable samples,
churns textures and exits through the normal SDL quit path. Pong automatically
starts/restarts matches to exercise audio and gameplay. Window requests use SDL
on the owning thread and affect only the test's own windows.

`artifacts/stability/` contains JSONL measurements and `summary.json`/`gate.json`.
Running samples are recorded every five seconds; stopped measurements follow an
explicit full GC. The first three cycles allow JIT, shader and driver caches to
warm up. Owned GPU handles must remain stable during each cycle and return to zero
after every shutdown. This is an ownership count, not driver residency. Windows
GPU process-memory counters are recorded separately. A full Gate requires enough
valid CPU/GPU measurements; missing GPU counters cannot establish a pass.

The average of the final three stopped measurements may exceed the first three
post-warm-up measurements by at most 64 MiB private memory, 16 MiB managed memory,
16 OS handles and eight threads. CPU usage may grow by at most 0.10 of one CPU
core. GPU dedicated/shared/committed memory averages use six-sample windows after
warm-up and a 32 MiB growth tolerance. These tolerances detect sustained growth
while allowing allocator/driver cache noise; a pass covers the measured workload,
duration and machine. Keep the raw measurements for review. Use
`-AnalyzeOnly -OutputDirectory <existing-results>` to repeat analysis without rerunning games.

The initial 2026-10-15 Day 13 run **failed**: 30 cycles over
1,813.92 seconds left zero owned GPU resources after every shutdown, but stopped
OS handle averages grew from 436.67 to 456.67, exceeding the predeclared +16 limit.
Private memory grew by 13.53 MiB; managed memory grew by about 2.4 KiB. CPU and GPU
memory did not exceed their thresholds, and the runner/children exited. Debug and
Release each passed 256 tests; all three original Sample executables opened and
closed normally in both configurations. Packaged CLI/distribution/Starter local
Gates passed, including preservation of native and .NET runtime notices.

The native isolation probe reproduces approximately one extra OS handle per
SDL video initialization/shutdown without creating a window or GPU device:

```powershell
dotnet run --project tests/Obelisk.Stability -c Release -- --probe init --cycles 40
# Controls: event-only initialization and audio-only initialization remain flat
# after warm-up; use --probe events / --probe audio / --probe empty.
```

With the pinned packages, warm cycles 3–5 averaged 305.67 handles and final three
cycles averaged 341.00. The corresponding event-only control was 287.67 to 289.00,
and audio-only was 330.67 to 332.00. The binding directly calls SDL's native init
and quit exports. In [SDL 3.4.16's Windows device-notification implementation](https://github.com/libsdl-org/SDL/blob/release-3.4.16/src/core/windows/SDL_hid.c),
`WIN_InitDeviceNotification` creates `s_HotplugEvent`, while
`WIN_QuitDeviceNotification` joins the hotplug thread and unregisters callbacks
without closing that event. This matches the observed growth. The same omission
was present in upstream `main` when inspected on 2026-10-15.

On 2026-10-16 the project owner approved the native correction. The patched
`3.4.16-obelisk.1` runtime closes/resets that event after the thread has joined and
notification callbacks have been unregistered. The engine's platform package now
ships this Windows x64 DLL instead of depending on `SDL3-CS.Windows`. Engine code
does not close opaque SDL handles. The 40-cycle video-only probe now averages
310.67 to 312.00 handles; full platform initialization/shutdown averages 414.67 to
414.00, with zero owned GPU resources.

The final 2026-10-16 Gate **passed** with this patched DLL: 30 cycles over
1,814.68 seconds, Debug/Release each passing 256 tests, and all six original Sample
runs passing. Stopped handle averages decreased from 435.00 to 431.67; owned GPU
resources returned to zero after every shutdown. Private/managed memory grew by
10.08 MiB/14.27 KiB, within the original tolerances. CPU usage decreased from
0.0163 to 0.0145 of one core. GPU dedicated/shared/committed averages grew by
0.13/0.17/0.31 MiB across 61 GPU measurements, within the original limits.
The runner exited with no surviving child process. Final logs are under
`artifacts/stability-final-30m/`; `gate.json`, `comparison.json` and
`native-module.json` preserve the result, measurements and loaded DLL identity.
The independent packaged CLI, relocated self-contained game and Starter local
Gates also passed with exact patched DLL and notice hashes. This pass covers the
tested Windows x64 machine and workload.

To rebuild the checked-in runtime, install Visual Studio 2022 C++ tools with
MSVC 14.44.35207/v143 and Windows SDK 10.0.26100.0, then run:

```powershell
pwsh -NoProfile -File scripts/Build-SdlNative.ps1
```

The script verifies the official 3.4.16 archive's SHA-256, applies the patch to a
fresh source tree and builds upstream's Release/x64 VisualC project. It records
source/patch/DLL hashes and toolchain details in `native/sdl3/build.json` and copies
the alteration notice, patch and manifest into distribution licenses. Engine
package consumers use the supplied DLL without C++ tools. Build inputs are pinned;
bit-for-bit identity across toolchain installations is not guaranteed. See
[ADR 0001](docs/adr/0001-sdl3-csharp-binding.md) for ownership and maintenance.
Retain the original failed run's JSONL logs for comparison.

The platform, assets and image Plugin packages carry their dependency texts under
`licenses/` into build/publish output. Preserve that directory when distributing a
game or tool; the packaged CLI integration Gate checks the complete notice set
against the repository originals. See [the dependency inventory](licenses/README.md)
for the fixed versions, native DLLs, upstream sources and font/runtime constraints.
The engine's own code uses [MIT](LICENSE), Copyright (c) 2026 Taegyun Eom.
NuGet metadata and release archives include the license; Vanilla also copies it
into game output at `licenses/obelisk/LICENSE`.

## Documentation and API Gate

```powershell
dotnet test Obelisk.sln -c Debug
dotnet test Obelisk.sln -c Release
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -Distribution -Starter -Documentation
```

The API test compares the eight exported library surfaces to the reviewed v0.1
snapshot and rejects SDL/native pointer types in public signatures. XML generation
and warnings-as-errors reject undocumented public declarations. The documentation
Gate uses an isolated local feed/cache outside the checkout: it compiles the exact
Plugin guide code blocks, applies Getting Started's game edits, runs the installed
CLI, verifies Plugin registration, then relocates/runs the self-contained game.
It also verifies XML reference files restored from all eight library packages.
This exercises the documented local-package path; public-feed certification and
Rider's actual UI validation remain release tasks.

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

## Local Plugins

A Plugin is a trusted distribution package with one `IObeliskPlugin` entry point.
A Module is a registered `IObeliskModule` that participates in the game loop.
`ObeliskApp.Run` discovers immediate subdirectories of `plugins` beside the game
executable. Each package needs `obelisk.plugin.json`, its DLL, private managed
dependencies and optional `assets`, `shaders` and `native` directories. An absent
`plugins` directory means no external plugins. A present package without a manifest
fails startup rather than being silently ignored.

Install the sample from the repository root, with the game stopped:

```powershell
dotnet build samples/SamplePlugin -c Debug
dotnet build samples/MovingRectangle -c Debug
New-Item -ItemType Directory -Force samples/MovingRectangle/bin/Debug/net10.0/plugins/sample
Copy-Item samples/SamplePlugin/bin/Debug/net10.0/* samples/MovingRectangle/bin/Debug/net10.0/plugins/sample -Recurse -Force
dotnet run --project samples/MovingRectangle -c Debug --no-build
```

The green pulsing badge comes from a dynamically loaded Module; its RGB accent is
loaded through `plugin://sample/accent.txt`. The game references only Vanilla.
Close the game, then remove the package and run it again:

```powershell
Remove-Item samples/MovingRectangle/bin/Debug/net10.0/plugins/sample -Recurse
dotnet run --project samples/MovingRectangle -c Debug --no-build
```

Removal takes effect on the next startup. Assemblies are not unloaded; native
libraries can remain mapped until process exit. Hot reload and removal while
running are outside v0.1. Plugin code executes with the game's permissions;
path checks do not provide a security sandbox.

The schema uses stable three-part versions and an inclusive/exclusive engine
range. The current engine version is `0.1.0`:

```json
{
  "schemaVersion": 1,
  "name": "sample",
  "version": "0.1.0",
  "engineMinimum": "0.1.0",
  "engineMaximumExclusive": "0.2.0",
  "assembly": "SamplePlugin.dll",
  "entryPoint": "SamplePlugin.PulsePlugin",
  "assets": "assets",
  "shaders": "shaders",
  "native": "native"
}
```

Names use lowercase ASCII letters, digits and hyphens, starting with a letter.
Path fields are relative to the package; absolute paths, traversal and reparse
points are rejected. Unknown or duplicate JSON fields are rejected. The entire
discovered set is checked for compatibility and duplicate names before external
entry points run. Set the sample's range to `[0.2.0, 0.3.0)` to reproduce an
incompatible startup: the error identifies `sample`, engine `0.1.0` and the range.
Restore the manifest before running again.

Entry points must be public concrete types with a public parameterless constructor:

```csharp
public sealed class MyPlugin : IObeliskPlugin
{
    public void Register(PluginRegistrationContext context)
    {
        context.RegisterAssetLoader<string>(File.ReadAllText);
        context.RegisterModule(new MyModule());
    }
}
```

Registration closes after `Register` returns. A scoped loader handles only this
Plugin's `plugin://name/...` URIs; a shared default loader can serve project assets
or plugins without their own override. Vanilla registers the bundled `images`
Plugin and its `RgbaImage` decoder by default. Games and Modules can use
`InitializeContext.Assets.Load<RgbaImage>("project:///image.png")`; the host owns
cached disposable assets, so callers borrow them rather than disposing them.
Module callbacks run on the host's owning thread: initialize before the game,
update before the game, render after the game, then reverse-order shutdown after
the game. Render appends public Canvas commands and restores Canvas blend state;
it exposes no GPU device or native handle. `PluginRegistrationContext.Paths`
resolves package-owned assets, shader files and private native dependencies.

Use `EnableDynamicLoading=true` in Plugin projects and copy/publish their complete
output. Engine contracts retain host type identity; private managed dependencies
load only from the package and cannot borrow arbitrary application dependencies.
Native dependencies resolve from the package/deps metadata or its native directory,
with normal OS library resolution as a fallback. Custom paths and optional images:

```csharp
ObeliskApp.Run(new MyGame(), plugins: new PluginOptions
{
    Directory = Path.Combine(AppContext.BaseDirectory, "extensions"),
    ProjectAssetRoot = Path.Combine(AppContext.BaseDirectory, "assets"),
    IncludeImages = false,
});
```

If loading the images Plugin dynamically, disable its bundled registration to avoid
a duplicate `images` name. Errors name the Plugin and operation; failed registration
closes the host and disposes earlier registrations. Module initialization failure
also shuts down partially initialized Modules. Shutdown continues through cleanup
errors, releases cached assets and disposes Plugins before platform shutdown.
See [ADR 0006](docs/adr/0006-local-plugin-lifecycle.md) for the design and limits.

## Repository layout

- `src/`: engine libraries and the CLI
- `samples/`: example games and plugin
- `tests/`: automated tests
- `docs/`: architecture decisions and user documentation (added as work progresses)

