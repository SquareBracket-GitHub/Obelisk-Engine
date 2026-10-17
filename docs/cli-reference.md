# CLI Reference (v0.1.0)

Run commands from the directory containing `obelisk.json`, except `new`, `info`
and help/version. The CLI orchestrates standard .NET SDK commands. Arguments and
paths containing spaces must be quoted. Native execution and distribution are
validated for Windows x64.

| Command | Result |
| --- | --- |
| `obelisk new <name>` | Atomically creates a new child project; no implicit restore/build. |
| `obelisk run [-- <game arguments>]` | Validates, restores, builds and runs once. |
| `obelisk dev [-- <game arguments>]` | Watches files and stops/rebuilds/restarts the owned game tree. |
| `obelisk build` | Publishes Release self-contained Windows x64 output to `artifacts/build/win-x64`. |
| `obelisk add <local-directory>` | Copies a validated trusted local Plugin package into the configured Plugin root. |
| `obelisk remove <manifest-name>` | Removes an installed package for the next game startup. |
| `obelisk list` | Lists installed Plugin versions and declared engine ranges; read-only. |
| `obelisk info` | Prints CLI/template, runtime and platform versions without requiring a project. |
| `obelisk --version` | Prints `0.1.0`. |
| `obelisk help`, `--help`, `-h` | Prints command usage. No arguments also prints help. |

`<command> --help` / `-h` is accepted for new, run, dev, build, add, remove, list
and info. Unknown commands, extra arguments and unsupported options are rejected.

## Creation and project manifest

Names use 1–64 ASCII letters, digits or underscores, starting with a letter or
underscore. C# keywords/contextual reserved words and Windows device names are
rejected. Existing destinations are preserved. Staging avoids partially created
projects; an unsuccessful staging cleanup reports the leftover owned path.

A generated `MyGame/obelisk.json` contains:

```json
{
  "schemaVersion": 1,
  "name": "MyGame",
  "engineVersion": "0.1.0",
  "project": "MyGame.csproj",
  "assets": "assets",
  "plugins": "plugins"
}
```

All six fields are required; unknown/duplicate fields are errors. Paths are
relative to the manifest directory, confined, and cannot traverse reparse points.
The project must exist; asset/Plugin roots may be absent. `engineVersion` must
match the CLI's supported engine version. The asset/Plugin fields select watched
and copied roots; changing them does not rewrite `.csproj` content rules or
`PluginOptions`. Default runtime paths match the canonical published layout.

## Run and development loop

`run/dev` call `dotnet restore`, `dotnet build --no-restore`, then
`dotnet run --no-build --no-restore`. Configure the game's local NuGet source
beforehand while public engine packages remain unpublished.

```powershell
obelisk run -- "two words" --level 2
obelisk dev
```

Everything after `--` is forwarded as game arguments, without reinterpreting it as
CLI options. There is no configuration/RID option for these commands in v0.1.
`dev` combines save bursts with a 300 ms quiet period, stops the old owned process
tree before building, and launches the latest changes once. It watches source,
project/configuration files and configured assets/plugins, ignoring artifacts,
bin/obj and version-control/editor metadata. A closed game waits for another
change. Subsequent build/manifest failures also wait for a repair; an invalid
initial manifest exits immediately.

Windows Job Objects clean owned SDK/game descendants on restart, cancellation and
forced CLI termination. Forced-termination cleanup is not certified on other OSes.
`run`, `dev`, `build`, `add` and `remove` share an OS file lock. Stop the active
operation first; `list` can still run. Games launched directly through dotnet or
their executable must be stopped by their owner before package changes.

## Plugin operations and build

`add` requires a directory containing a manifest and entry DLL, copies the whole
package, and checks metadata/paths without executing its code. Runtime validates
the entry type on startup. Stop the game first; an existing package is preserved
and rejected until removed. See [Plugin Authoring](plugin-authoring.md).

`build` runs standard `dotnet publish -c Release -r win-x64 --self-contained true`
with apphost enabled and single-file/trimming/AOT disabled for dynamic Plugins.
It stages fresh output, copies configured roots to `assets/` and `plugins/`, and
replaces only CLI/project-owned output. Unowned destinations are rejected; publish
failure/cancellation preserves the old distribution. Rebuild removes stale files.
Copy the complete output including `licenses/`, native libraries and Plugins.
Targets need the VC++ x64 prerequisite and a compatible graphics driver, but no
SDK/shared .NET runtime. [Getting Started](getting-started.md) demonstrates the flow.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success, including help/version/info and normal game exit. |
| `1` | CLI operational error, such as collision, invalid manifest, incompatible Plugin, lock or file access failure. |
| `2` | Invalid command syntax, arguments or project name. |
| `130` | CLI cancellation (Ctrl+C) after owned processes are stopped. |
| SDK/game code | `run` propagates unsuccessful restore/build/game codes; `build` propagates unsuccessful publish codes. These can overlap CLI codes. |

CLI diagnostics go to stderr with `obelisk:` and operation/path context; normal
status goes to stdout. SDK/game output is forwarded, so code alone cannot identify
its source. Exceptions from a game are reported by its process/runtime; the CLI
does not translate every game exception into its own error model. Retain stderr
and the invoked command when diagnosing failures. Cancellation and failed builds
do not authorize deleting arbitrary user output.
