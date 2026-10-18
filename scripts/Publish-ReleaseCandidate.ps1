param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = [IO.Path]::GetFullPath($Directory)
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-ReleaseCandidate.ps1') -Directory $root
if ($LASTEXITCODE -ne 0) { throw 'Refusing to publish an invalid candidate.' }
$manifest = Get-Content -Raw (Join-Path $root 'release-manifest.json') | ConvertFrom-Json
if (-not $manifest.engineLicenseReady -or $manifest.sourceDirty) {
    throw 'Publication requires approved license metadata and a candidate built from a clean source commit.'
}
Push-Location $repository
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -ne $manifest.sourceCommit -or (& git status --porcelain)) {
        throw 'Publication requires the candidate source revision and a clean checkout.'
    }
} finally { Pop-Location }
$apiKey = $env:NUGET_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    $envPath = Join-Path $repository '.env'
    if (Test-Path -LiteralPath $envPath) {
        $entries = @(Get-Content -LiteralPath $envPath | Where-Object { $_ -match '^\s*NUGET_API_KEY\s*=' })
        if ($entries.Count -gt 1) { throw 'Duplicate NUGET_API_KEY entries in the local .env.' }
        if ($entries.Count -eq 1) { $apiKey = ($entries[0] -replace '^\s*NUGET_API_KEY\s*=\s*', '').Trim().Trim('"', "'") }
    }
}
if ([string]::IsNullOrWhiteSpace($apiKey)) { throw 'Set NUGET_API_KEY in the project .env or process environment. Do not paste keys into chat.' }
$published = [Collections.Generic.List[string]]::new()
try {
    foreach ($id in @('Obelisk.Abstractions', 'Obelisk.Graphics2D', 'Obelisk.Runtime', 'Obelisk.Assets',
        'Obelisk.Plugin', 'Obelisk.Platform.Sdl3', 'Obelisk.Plugins.Images', 'Obelisk.Vanilla', 'Obelisk.Cli')) {
        $package = Join-Path $root "packages/$id.$($manifest.version).nupkg"
        # Never print the argument array containing the key, and never silently skip duplicates.
        & dotnet nuget push $package --api-key $apiKey --source 'https://api.nuget.org/v3/index.json'
        if ($LASTEXITCODE -ne 0) {
            throw "Publication failed for '$id'. Successfully pushed in this invocation: $($published -join ', '). Inspect the public feed before retrying; no automatic deletion or version change was attempted."
        }
        $published.Add($id)
    }
    Write-Output 'Nine packages pushed. Indexing is asynchronous; run the public-feed CLI/Starter gate before recording publication verification as passed.'
} finally { $apiKey = $null }
