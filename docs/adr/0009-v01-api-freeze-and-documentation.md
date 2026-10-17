# ADR 0009: v0.1 API freeze and packaged documentation

- Status: Accepted
- Date: 2026-10-18

## Context

Day 14 prepares a release candidate whose game/Plugin workflow can be followed
using documentation alone. The existing eight library assemblies expose both
game-facing contracts and advanced host/backend types. Renaming them during
documentation work would invalidate the already verified package consumers.

## Decision

Retain the existing public names/signatures and freeze the reviewed v0.1 surface.
Record exported types and declared public members in a versioned compatibility
fixture, including constructor/method parameters and defaults, nullable/property
contracts, enum values and generic constraints. Compare it in the automated suite
and separately reject SDL/native pointer types in exported signatures.

Generate compiler XML reference files for all eight library projects and include
them through standard NuGet packing. Keep CS1591 subject to warnings-as-errors.
Positional-record parameter documentation supplies the generated property docs.
CLI implementation classes remain public to avoid unrelated breaks, but its
supported consumer interface is commands/manifests/exit behavior rather than a
library contract.

Put first-user, Plugin, CLI, lifetime/support and compatibility guides under docs/.
Verify the exact published guide examples from packages in an isolated external
workspace, including edited game execution, Plugin registration and relocated
self-contained output. Public-NuGet publication, clean-machine certification and
actual Rider validation stay in their planned release stage.

## Consequences

- Baseline changes require explicit consumer-impact and version/migration review;
  tests write diagnostic output but never silently refresh the baseline.
- The pinned SDK's generated record surface is part of the fixture; toolchain
  upgrades require review as well as application code changes.
- Consumers receive IntelliSense XML without inheriting repository doc-warning rules.
- Raw command/device/backend APIs remain experimental. This freeze introduces no
  stable low-level GPU extension API and makes no all-future-0.x compatibility promise.
- The engine stays at 0.1.0. Plugin compatibility ranges remain declared startup
  checks, not proof of every third-party binary's semantic compatibility.
