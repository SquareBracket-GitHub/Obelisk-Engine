# 0011: Independently versioned official Text and Effects plugins

Date: 2026-10-30

Status: Accepted within the owner's official plugin implementation request.

## Context

Engine 0.2 provides texture updates, materials, render targets, filtering and
MSAA. Font quality and blur are optional algorithms above those contracts.
The owner requested independent plugin versions: new plugins begin at 0.1.0;
Images advances to 1.0.0 when its functionality changes.

## Decision

- Add Obelisk.Plugins.Text 0.1.0 and Obelisk.Plugins.Effects 0.1.0, each declaring
  engine compatibility [0.2.0, 0.3.0) in package manifests. Images remains the
  unchanged 0.1.1 engine compatibility patch. No NuGet publication is implied.
- Keep existing bitmap Font APIs. Text supplies a separate DynamicFont with
  explicit actual-size/DPI rasterization, fallback faces, partial uploads and
  bounded frame-safe page eviction. It reuses the engine's existing SkiaSharp
  version. FontFace contains no public native types and is borrowed by caches.
- Support offline msdf-atlas-gen Unicode JSON/PNG imports and derivative-based
  median RGB rendering. Do not add a new native MSDF generator, implicit network
  downloads, runtime MSDF atlas generation or complex-script shaping.
- Effects supplies explicit two-pass Gaussian blur, downsampling, reusable targets
  and alpha-correct accumulation. No renderer/backend internals are exposed.
- Vanilla references the SDKs, defaults Text registration on and Effects off;
  constructors and draw calls control resource allocation and execution.
- Allow explicit PluginHost.ShareContract before registration, requiring exact
  full assembly identity. A shared SDK assembly supplies both its public types
  and implementation; a package cannot replace it privately. Vanilla opts in for
  Text/Effects. Unshared dependencies retain private load contexts. This solves
  AssetStore loader key identity for statically referenced plugin SDK types
  without automatic special privileges for official plugin names.
- Preserve independent reviewed public API snapshots and forbid native/SkiaSharp
  public type exposure. Verify actual GPU output, resource lifetime, dynamic
  package type identity, isolated NuGet restoration and relocated deployment.

## Consequences

Games choose font licensing, fallback coverage, cache budgets, scale and effect
quality explicitly. Raster text is sharp at its requested size; subsequent layer
magnification still scales pixels. Dynamic caches cannot evict glyph pages already
used in a physical frame, so exhausted live budgets fail explicitly. MSDF coverage
is limited to the imported atlas. Blur is RGBA8, clamps at source bounds and needs
padding for halos. Resources remain alive until presentation and are released on
their owning thread. SDK/package version changes require rebuilding and restarting.
Actual mixed-DPI monitor movement and a separate SDK-free Windows machine remain
outside the automated checks available on this workstation.
