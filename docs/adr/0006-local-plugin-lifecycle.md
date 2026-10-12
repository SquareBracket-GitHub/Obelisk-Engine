# ADR 0006: Local Plugin packages and Module lifecycle

## Status

Accepted for revised roadmap Day 8, implemented 2026-10-12. Extends ADR 0005.

## Context

Day 8 needs an installable local package that can extend game execution and asset
importing without changing Runtime or exposing SDL/GPU handles. Existing samples
must keep their single Vanilla reference. Distribution and execution have different
lifetimes, and startup failures must release owned registrations and resources.

## Decision

`IObeliskPlugin` belongs to the Plugin package: it registers loaders and Modules,
then is disposed by the host. `IObeliskModule` and read-only `IAssetSource` belong
to Abstractions. Modules have optional Initialize/Update/Render/Shutdown hooks.
`PluginHost.Wrap(IGame)` adapts registrations to the existing Runtime lifecycle.
Plugin references Assets and Abstractions, rather than Runtime; the Runtime loop
and platform remain independent of package discovery and assembly loading.

Vanilla creates the host before opening the platform, registers bundled `images`,
discovers local packages, and runs the wrapped game. Default discovery and project
asset roots are `plugins` and `assets` beside the executable, with explicit
`PluginOptions` overrides. Images retains file/HTTP helpers and implements the same
Plugin registration contract; it supplies the default RgbaImage loader. An optional
local images manifest also supports dynamic loading when bundled images is disabled.

Schema 1 requires name, Plugin version, minimum and exclusive maximum engine
versions, DLL and entry type. Versions are stable three-part numbers; current
engine compatibility is 0.1.0. Assets/shaders/native paths default to directories
with those names. Unknown/duplicate properties, missing required values, invalid
ranges and manifests above 64 KiB fail explicitly. Names are 1–64 lowercase ASCII
letters/digits/hyphens, starting with a letter. Each immediate child directory is
a package; a missing discovery root is empty, a missing package manifest is an
error. Discovery order is ordinal folder order. All manifests, names, confined
paths and entry DLL presence are checked before any external plugin construction.

Each package uses a noncollectible AssemblyLoadContext and AssemblyDependencyResolver.
Abstractions, Graphics2D, Assets and Plugin contract assemblies are shared with the
host and require matching assembly identities. Framework assemblies use the default
context. Other managed dependencies must reside in the package. Managed DLLs are
loaded from streams to avoid Windows file locks, so their `Assembly.Location` is
empty; Plugin authors use `PluginRegistrationContext.Paths` for package files.
Private native dependencies resolve from deps metadata or the package's native
directory, followed by normal OS resolution. Resolved package paths reject absolute
fields, traversal and reparse points including ancestors. This follows .NET's
[plugin loading model](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support)
with explicit host ownership and confined package dependency paths.

Registration is synchronous and closes on return. Duplicate names, loaders for
the same type/scope and reused Module instances fail. Plugin-scoped loaders take
precedence for their URI authority, then fall back to a shared default loader.
Cache keys remain URI plus type. InitializeContext supplies the host asset source
while preserving the existing texture/audio services. Cached disposable assets
are borrowed by games/Modules and owned by the host.

Modules initialize and update before the game, then append Render commands after
the game. They receive only the public Canvas RenderContext, and blend state is
restored around each Module. All lifecycle operations run on the creating thread.
Shutdown runs the game, Modules in reverse initialization order, cached assets,
Plugins in reverse registration order, and finally the platform. Partial Module
initialization is included in cleanup. Loading/registration failures dispose the
whole host; no partial startup succeeds. Cleanup continues through individual
failures and aggregates errors. Errors identify the Plugin, operation and cause.

## Evidence and limits

Tests build/copy the actual SamplePlugin package, dynamically instantiate its entry,
load its scoped RGB asset, execute Update and Render through ApplicationHost, remove
the package, and start a new host with no Plugin commands. Additional tests exercise
the Images Plugin's private StbImageSharp dependency, shared RgbaImage identity,
missing dependencies and native-library failure. Windows Vanilla integration runs
the discovered sample and bundled image loader through a real SDL/GPU window and
asserts normal shutdown; incompatible plugins are refused before platform creation.
Manifest failures, duplication, path escapes, custom roots, scope/cache ownership,
thread/order enforcement, partial initialization and cleanup failures are covered.

Assembly unloading, hot reload, registration changes while running, signatures,
remote registries and a security sandbox remain outside v0.1. Remove/install packages
with the process stopped; native libraries may remain mapped until exit. Shader
paths are available, but custom GPU shader execution is outside this limited Render
extension. The host does not isolate malicious code or prove third-party native ABI
compatibility. CLI add/remove/list remains scheduled for Day 11.
