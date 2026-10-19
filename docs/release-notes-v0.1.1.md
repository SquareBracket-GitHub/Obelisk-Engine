# Obelisk CLI 0.1.1

This patch fixes garbled non-ASCII output during `obelisk run`, `dev` and `build`
on Windows consoles using legacy code pages. The CLI version is `0.1.1`; engine
packages, generated game manifests and the Vanilla reference remain `0.1.0`.
Existing games do not need a package or manifest migration.

The same project's existing CLI `0.1.0` build output can be replaced by `0.1.1`.
A successful build updates its ownership marker; a failed publish preserves the
previous output. Markers for other projects or unsupported versions remain protected.

## Fix

The process runner previously decoded redirected SDK/game UTF-8 output using the
parent console encoding. With CP949, `복원 완료` became `蹂듭썝 ...`; CP437 also
damaged the text. Both output streams now explicitly decode UTF-8, and child
SDK processes receive `DOTNET_CLI_FORCE_UTF8_ENCODING=1`. The CLI emits BOM-free
UTF-8 and restores its original console output encoding on normal exit. Language
selection is preserved; the fix does not force English output.

Regression cases cover CP949/CP437, Korean, Japanese and emoji in both streams,
and propagation of child exit codes. The patch verifier exercises the actual
tool package and extracted standalone ZIP through `obelisk run` with Korean SDK
output and a Unicode workspace path. Redirected consumers must decode CLI output
as UTF-8; arbitrary child programs that explicitly emit a different encoding are
outside this UTF-8 stream contract. Missing terminal glyphs require a suitable
font and are distinct from encoding corruption.

Debug and Release each passed 268 tests, including both legacy-code-page
reproductions and build ownership upgrade/failure cases. The CI formatting check
also passed. Package and standalone encoding checks passed in an isolated cache
against public engine `0.1.0` packages.
The complete isolated local-feed workflow also passed: native game startup,
development-loop restart and descendant cleanup, self-contained game relocation,
Starter/CLI template byte equality, and documented Plugin creation and removal.

## Prepare and verify

The CLI tool and game development require the .NET 10 SDK. SDK `9.0.101` was
reported on a computer that failed installation with `DotnetToolSettings.xml was
not found`. The public `0.1.0` package was inspected and contains that file under
`tools/net10.0/any/`; this is consistent with the SDK's
[misleading newer-framework tool error](https://github.com/dotnet/sdk/issues/38172).
Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
reopen the terminal, and verify `dotnet --version` selects `10.0.xxx` from the
installation/game directory. A parent `global.json` or PATH can select an older SDK.
Use `dotnet tool install --global Obelisk.Cli --version 0.1.1` for first installation;
use `update` for an existing installation. The remote
computer's retry has not been verified.

```powershell
pwsh -NoProfile -File scripts/Build-CliPatch.ps1 -OutputDirectory artifacts/cli-0.1.1
pwsh -NoProfile -File scripts/Verify-CliPatch.ps1 -Directory artifacts/cli-0.1.1
pwsh -NoProfile -File tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1 -Distribution -Starter -Documentation
```

The candidate contains only `Obelisk.Cli.0.1.1.nupkg`, the standalone Windows x64
CLI ZIP, these notes, a manifest and SHA256 inventory. The engine `0.1.0` packages
are not republished. The tool verifier uses an isolated install/cache and public
engine packages. The original engine-set builder stops when CLI/engine versions
differ; use the CLI patch builder for this release. No NuGet publication or Git
tag is performed by these commands.

Update with
`dotnet tool update --global Obelisk.Cli --version 0.1.1`, or replace the complete
standalone CLI folder with the extracted ZIP. Candidate verification does not
certify Rider or a separate SDK-free Windows machine; prior owner deferrals remain.

## Publication

`Obelisk.Cli` `0.1.1` was pushed to nuget.org on 2026-10-19 from clean source
`0379b5b90c7e28503df5bc0c5a1fbda55aa69fc1`. The submitted package SHA256 is
`c6e29167c5b2d25990804030b59ffb9fcb799b9aaa7a5c04115d6e0bfc246b8a`.
The original candidate archive and its notes remain unchanged. Engine packages
remain `0.1.0`; no Git tag or GitHub Release was created.
After NuGet finished indexing, the public-feed-only fresh-cache installation
passed on 2026-10-19. The installed CLI assembly matched the archived candidate;
Korean SDK output, Unicode paths, both output streams, invalid-name diagnostics
and game exit-code propagation passed for the public tool and archived standalone
ZIP. The earlier indexing wait is resolved; no duplicate upload was attempted.

Repeat the public-feed check against that archived candidate with:

```powershell
pwsh -NoProfile -File scripts/Verify-CliPatch.ps1 -Directory artifacts/cli-0.1.1-0379b5b -PublicFeed
```

`-PublicFeed` removes the local source, isolates both package and HTTP caches,
and compares the installed CLI assembly with the archived candidate before
running the same Unicode/output/exit-code checks. NuGet repository signing may
change the downloaded package file hash; the submitted hash above identifies the
original upload, while the installed payload comparison checks the actual CLI.
