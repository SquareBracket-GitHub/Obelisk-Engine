param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -Raw (Join-Path $root 'engine-candidate.json') | ConvertFrom-Json
$verification = Get-Content -Raw (Join-Path $root 'verification.json') | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.engineVersion -ne '0.2.0' -or $manifest.sourceDirty -or
    $verification.sourceCommit -cne $manifest.sourceCommit -or -not $verification.isolatedPackageGate -or
    -not $verification.relocatedSelfContainedWithoutSdkPath -or -not $verification.formatVerified) {
    throw 'Expected a clean engine 0.2.0 candidate with matching successful verification.'
}
$order = @('Obelisk.Graphics2D','Obelisk.Abstractions','Obelisk.Assets','Obelisk.Runtime','Obelisk.Plugin',
    'Obelisk.Platform.Sdl3','Obelisk.Plugins.Images','Obelisk.Plugins.Text','Obelisk.Plugins.Effects','Obelisk.Vanilla','Obelisk.Cli')
if (($order | Sort-Object | ConvertTo-Json -Compress) -ne (@($manifest.packages.id) | Sort-Object | ConvertTo-Json -Compress)) { throw 'Unexpected package inventory.' }
Push-Location $repository
try {
    # Documentation/publishing tools may advance; never substitute different package inputs.
    & git diff --exit-code $manifest.sourceCommit -- src Directory.Build.props Directory.Build.targets global.json LICENSE licenses
    if ($LASTEXITCODE -ne 0) { throw 'Package source/license inputs differ from the verified candidate.' }
} finally { Pop-Location }
foreach ($package in $manifest.packages) {
    $version = if ($package.id -eq 'Obelisk.Plugins.Images') { '0.1.1' } elseif ($package.id -like 'Obelisk.Plugins.*') { '0.1.0' } else { '0.2.0' }
    if ($package.version -cne $version -or $package.path -cne "packages/$($package.id).$version.nupkg" -or
        (Get-FileHash (Join-Path $root $package.path)).Hash.ToLowerInvariant() -cne $package.sha256) { throw 'Candidate package version/path/hash mismatch.' }
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root $package.path))
    try {
        $entry = @($zip.Entries | Where-Object FullName -like '*.nuspec')
        if ($entry.Count -ne 1) { throw 'Expected one package specification.' }
        $reader = [IO.StreamReader]::new($entry[0].Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.id -cne $package.id -or $spec.package.metadata.version -cne $version -or
            $spec.package.metadata.license.InnerText -cne 'MIT' -or $spec.package.metadata.authors -cne 'Taegyun Eom') { throw 'Package identity/license/author mismatch.' }
    } finally { $zip.Dispose() }
}
$apiKey = $env:NUGET_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    $envPath = Join-Path $repository '.env'
    if (Test-Path -LiteralPath $envPath) {
        $entries = @(Get-Content -LiteralPath $envPath | Where-Object { $_ -match '^\s*NUGET_API_KEY\s*=' })
        if ($entries.Count -gt 1) { throw 'Duplicate NUGET_API_KEY entries.' }
        if ($entries.Count -eq 1) { $apiKey = ($entries[0] -replace '^\s*NUGET_API_KEY\s*=\s*', '').Trim().Trim('"', "'") }
    }
}
if ([string]::IsNullOrWhiteSpace($apiKey)) { throw 'Set NUGET_API_KEY in the local project .env. Never paste keys into chat.' }
$published = [Collections.Generic.List[string]]::new()
try {
    foreach ($id in $order) {
        $package = $manifest.packages | Where-Object id -CEQ $id
        # Do not echo arguments containing credentials; stop on any duplicate/failed push.
        & dotnet nuget push (Join-Path $root $package.path) --api-key $apiKey --source 'https://api.nuget.org/v3/index.json'
        if ($LASTEXITCODE -ne 0) { throw "Publication failed for '$id'. Already pushed this invocation: $($published -join ', '). Inspect the public feed before retrying." }
        $published.Add("$id/$($package.version)")
    }
    Write-Output 'Eleven packages pushed. Run Verify-EngineCandidate.ps1 -PublicFeed after indexing before declaring public verification complete.'
} finally { $apiKey = $null; $entries = $null }
