# Obelisk Engine v0.1.0 candidate

This is the first Windows x64 candidate of the experimental .NET 10 / SDL_GPU
2D engine. Candidate assembly is not release approval. No final release tag is
created until the release gates pass.

## Included

- Eight engine packages and the `Obelisk.Cli` .NET tool, all version `0.1.0`.
- Self-contained Windows x64 CLI ZIP and matching Starter project ZIP.
- MIT engine license, Copyright (c) 2026 Taegyun Eom, included in every package
  and archive. Third-party components retain their own notices and terms.
- Canvas drawing, textures/fonts, WAV audio, input, assets and trusted local Plugins.
- CLI `new`, `run`, `dev`, `add`, `list`, `remove` and self-contained game `build`.
- Public API baseline (56 types / 474 entries) and packaged XML reference docs.
- Patched SDL `3.4.16-obelisk.1` for the Day 13 repeated-window shutdown issue.
- Windows project creation tolerates short file locks during the final atomic
  directory rename. Retry is limited to access/sharing/lock errors for at most
  1.4 seconds; a conflicting target is preserved and persistent errors still fail.

## Requirements and limits

Development needs .NET 10 SDK; the checkout pins `10.0.401`. The self-contained
CLI can report help/version without an installed .NET runtime, but commands that
restore/build/run projects still require the SDK. Distributed self-contained
games need Windows x64, a compatible GPU driver and the Microsoft Visual C++ v14
x64 Redistributable. Copy the full output including native DLLs and `licenses/`.

See [supported environments and limits](https://github.com/SquareBracket-GitHub/Obelisk-Engine/blob/main/docs/resources-and-support.md) for ownership,
threading and native dependency details. Linux/macOS/ARM64 are not certified.
OGG streaming, gamepad, multiple windows, 3D, networking, hot reload of Plugin
assemblies and Plugin sandboxing are outside v0.1. Low-level backend APIs are
experimental; the [compatibility policy](https://github.com/SquareBracket-GitHub/Obelisk-Engine/blob/main/docs/api-compatibility.md) defines the freeze.

## Release gates

Local-feed isolated CLI/Starter, documentation and distribution gates passed in
Day 14, with 258 tests in each configuration. Day 13's 30-minute stability test
passed on the development machine. These do not certify a separate clean machine.
Day 15 adds two Windows file-lock regression cases (260 tests per configuration),
checks candidate package/license payloads and provides reproducibility, standalone
CLI and publication tools. Existing whitespace and line endings were normalized
to satisfy the repository's formatting check without changing their code content.

The owner approved MIT / Taegyun Eom on 2026-10-18. Public NuGet publication and
public-feed-only CLI/Starter restore/run remain pending. Actual Rider project
recognition and a separate Windows machine without an SDK are deferred by the
owner and have not passed. A local-runtime test with SDK removed from PATH is
only local evidence. The final release tag remains pending these decisions/gates.

## Recreate and verify

From the checkout on Windows x64 with the pinned SDK:

```powershell
pwsh -NoProfile -File scripts/Build-ReleaseCandidate.ps1
pwsh -NoProfile -File scripts/Verify-ReleaseCandidate.ps1 -Directory <candidate-directory>
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -ReleaseDirectory <candidate-directory> -Distribution -Starter -Documentation
```

The output preserves packages, archives, `SHA256SUMS` and a manifest recording
the source revision, dirty state, SDK and license readiness. Existing candidate
directories are never overwritten. A failed build directory is diagnostic output,
not a valid candidate. Integrity checks detect corruption; checksums are not a
publisher signature. Compare independently rebuilt archive payloads before release.
After publishing the exact packages, repeat with `-PublicFeed` using a fresh cache.
For rebuild comparison, pass `-CompareDirectory <second-candidate-directory>` to
the integrity verifier. It compares all expanded bytes, excluding only ZIP
metadata and arbitrary NuGet relationship identifiers. Package ZIP checksums can
therefore differ between recreations while their payloads match.
