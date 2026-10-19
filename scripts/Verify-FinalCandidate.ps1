param([Parameter(Mandatory)][string]$Directory, [string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'Final v0.1 verification requires Windows x64.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$candidate = [IO.Path]::GetFullPath($Directory)
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $repository ('artifacts/final-verification-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $output) { throw 'Output already exists; choose a new directory to preserve previous evidence.' }
New-Item -ItemType Directory -Path $output | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    schemaVersion = 1; verifiedAtUtc = $null; verificationHead = $null;
    verificationScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant();
    candidateManifestSha256 = $null; candidateSourceCommit = $null; version = $null;
    status = 'failed'; checks = $checks; error = $null;
    confirmedTagCandidate = $null; finalTagCreated = $false; certificationComplete = $false;
    deferredByOwner = @('Actual Rider project recognition', 'Separate Windows machine without SDK')
}

function Invoke-Check([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $entry = [ordered]@{ name = $Name; passed = $false; exitCode = $null; log = "$Name.log" }
    $checks.Add($entry)
    Write-Output "Checking $Name..."
    & $Executable @Arguments *> (Join-Path $output $entry.log)
    $entry.exitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Check '$Name' failed ($LASTEXITCODE). See '$($entry.log)'." }
    $entry.passed = $true
}

Push-Location $repository
try {
    Invoke-Check 'integrity' 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Verify-ReleaseCandidate.ps1'), '-Directory', $candidate)
    $manifestPath = Join-Path $candidate 'release-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.sourceDirty -or -not $manifest.engineLicenseReady -or $manifest.sourceCommit -notmatch '^[a-f0-9]{40}$') {
        throw 'Final verification requires a clean, licensed candidate with a full source revision.'
    }
    $report.candidateManifestSha256 = (Get-FileHash -LiteralPath $manifestPath).Hash.ToLowerInvariant()
    $report.candidateSourceCommit = $manifest.sourceCommit
    $report.version = $manifest.version
    $report.verificationHead = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify verification revision.' }
    & git diff --quiet $manifest.sourceCommit -- src Directory.Build.props Directory.Build.targets global.json native licenses LICENSE
    if ($LASTEXITCODE -ne 0) { throw 'Engine/package inputs differ from the published candidate. Resolve the difference before confirming a tag candidate.' }
    $checks.Add([ordered]@{ name = 'frozen-source'; passed = $true; exitCode = 0; log = $null })

    foreach ($configuration in @('Debug', 'Release')) {
        Invoke-Check "tests-$configuration" 'dotnet' @('test', 'Obelisk.sln', '-c', $configuration, '-m:1', '-p:MSBuildEnableWorkloadResolver=false')
    }
    Invoke-Check 'format' 'dotnet' @('format', 'Obelisk.sln', '--no-restore', '--verify-no-changes')
    Invoke-Check 'standalone' 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Verify-StandaloneCli.ps1'), '-Directory', $candidate)
    Invoke-Check 'public-flow' 'pwsh' @('-NoProfile', '-File', (Join-Path $repository 'tests/Obelisk.Cli.Tests/Verify-PackagedCli.ps1'),
        '-ReleaseDirectory', $candidate, '-PublicFeed', '-Distribution', '-Starter', '-Documentation')
    # Ensure no stage changed the archived package/ZIP bytes that were just exercised.
    Invoke-Check 'integrity-after' 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Verify-ReleaseCandidate.ps1'), '-Directory', $candidate)
    $report.confirmedTagCandidate = [ordered]@{ name = "v$($manifest.version)"; commit = $manifest.sourceCommit }
    $report.status = 'passed-with-deferred-certification'
    Write-Output "FINAL CANDIDATE PASSED: v$($manifest.version) -> $($manifest.sourceCommit)."
    Write-Output 'Rider and separate SDK-free Windows certification remain owner-deferred. No tag, release or package was published by this verifier.'
} catch {
    $report.error = $_.Exception.Message
    throw
} finally {
    $report.verifiedAtUtc = [DateTime]::UtcNow.ToString('O')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'final-verification.json') -Encoding utf8
    Pop-Location
}
