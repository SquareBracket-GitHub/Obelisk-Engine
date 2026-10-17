# Plugin Authoring Guide (v0.1)

A Plugin is a trusted local package with `obelisk.plugin.json`; a Module is an
execution unit registered by its entry point. Game and Plugin code use public
engine contracts without SDL types, native handles or engine source references.

## Build a local Plugin

Use the absolute `$obeliskFeed` from [Getting Started](getting-started.md).
In the workspace containing your game, create a sibling `TutorialPlugin` directory
and put these three files in it.

`TutorialPlugin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Obelisk.Plugin" Version="[0.1.0]" />
    <Content Include="obelisk.plugin.json;assets/**;shaders/**;native/**"
             CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`obelisk.plugin.json`:

```json
{
  "schemaVersion": 1,
  "name": "tutorial",
  "version": "0.1.0",
  "engineMinimum": "0.1.0",
  "engineMaximumExclusive": "0.2.0",
  "assembly": "TutorialPlugin.dll",
  "entryPoint": "TutorialPlugin.EntryPoint",
  "assets": "assets",
  "shaders": "shaders",
  "native": "native"
}
```

`EntryPoint.cs`:

```csharp
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugin;

namespace TutorialPlugin;

public sealed class EntryPoint : IObeliskPlugin
{
    public void Register(PluginRegistrationContext context)
    {
        Console.WriteLine("tutorial Plugin registered.");
        context.RegisterModule(new BadgeModule());
    }

    private sealed class BadgeModule : IObeliskModule
    {
        public void Render(RenderContext context) => context.Canvas.FillRectangle(
            new RectangleF(12, 12, 24, 24), ColorRgba.White);
    }
}
```

Build from that directory:

```powershell
dotnet restore --source $obeliskFeed --source https://api.nuget.org/v3/index.json
dotnet build -c Release --no-restore
```

Copy/install the **whole** `bin/Release/net10.0` directory; it contains the manifest,
DLL, deps metadata and private dependencies. From the stopped game:

```powershell
obelisk add ../TutorialPlugin/bin/Release/net10.0
obelisk list
obelisk run
```

You should see a white badge near the top-left corner. After closing the game,
`obelisk remove tutorial` removes it for the next startup. Replace a version by
removing it first; `add` never overwrites an installed package.

## Manifest and package rules

The file is a JSON object, at most 64 KiB. Unknown, duplicate, missing required or
incompatible fields fail. `schemaVersion` must be 1. Names are 1–64 lowercase ASCII
letters, digits and hyphens, starting with a letter. Versions are stable three-part
numbers without leading zeroes, prerelease or build suffixes. Compatibility uses
an inclusive minimum and exclusive maximum. The engine version is `0.1.0`.

`assembly` is a relative `.dll` path; `entryPoint` names a public concrete
`IObeliskPlugin` implementation with a public parameterless constructor. Optional
assets/shaders/native directories default to those names. Paths must remain inside
their package; rooted paths, empty/dot/traversal segments and reparse points fail.
Private dependencies resolve within the package/deps layout while engine contract
assemblies retain host type identity. Native resolution can fall back to the OS.
The compatibility range is a declaration checked at startup, not a general binary
compatibility solver. See [API compatibility](api-compatibility.md).

## Registration, callbacks and ownership

The host preflights the discovered manifest set for duplicates and compatibility
before executing external entry points. `Register` runs before platform creation;
register factories/modules there and acquire textures/audio in `Initialize`.
The context closes after `Register`; subsequent registrations fail. It offers:

- `Manifest` and confined `Paths.ResolveAsset/ResolveShader/ResolveNative`.
- `RegisterModule`: unique module instances, in registration order.
- `RegisterAssetLoader<T>`: a loader scoped to `plugin://this-name/...`.
- `RegisterDefaultAssetLoader<T>`: one shared default per type, including project URIs.
- `Assets`: a borrowed typed cache, also supplied to game/module initialization.

All callbacks execute sequentially on the main/owning thread. Modules initialize
and update before the game; they render after it. The host restores Canvas blend
state after each module render. Shutdown runs game first, then modules in reverse
initialization order (including partially initialized modules), then cached assets
and Plugin instances. Platform cleanup follows. Errors identify Plugin/operation;
cleanup continues and can report multiple failures in `AggregateException`.

For a scoped text asset, register `context.RegisterAssetLoader<string>(File.ReadAllText)`
and load `context.Assets.Load<string>("plugin://tutorial/message.txt")` during module
initialization. Put the file under the package's `assets/` directory. Cached
disposable results belong to the host; directly created Texture/Font/playback
objects belong to their creator. See [resource lifetime](resources-and-support.md).

## Limits and trust

Plugin code runs with game permissions. Path validation is not a sandbox; install
only trusted packages. There is no signing, remote registry, dependency graph
solver, running-Plugin hot reload or Assembly unload. Assemblies/native libraries
can stay mapped until process exit. Stop `run/dev` before package changes.
Shader/native directories are confined file locations; they do not grant a stable
low-level GPU extension API. The bundled `images` Plugin already owns that name;
set `PluginOptions.IncludeImages = false` before dynamically installing another
Plugin named `images`. Preserve private dependency and asset license notices.
