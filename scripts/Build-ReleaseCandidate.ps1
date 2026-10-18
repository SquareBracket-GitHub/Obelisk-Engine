param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'The v0.1 candidate targets Windows x64.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$props = Get-Content -Raw (Join-Path $repository 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected version '$version'." }
$destination = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $repository ('artifacts/release-candidate-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $destination) { throw "Output already exists: '$destination'. Choose a new directory; existing candidates are preserved." }
$packages = @('Obelisk.Abstractions', 'Obelisk.Runtime', 'Obelisk.Platform.Sdl3', 'Obelisk.Graphics2D',
    'Obelisk.Assets', 'Obelisk.Plugin', 'Obelisk.Plugins.Images', 'Obelisk.Vanilla', 'Obelisk.Cli')

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

function Write-Archive([string]$Source, [string]$Target) {
    # Stable ordering, timestamps and attributes make ZIP bytes independent of build time.
    [string[]]$files = @(Get-ChildItem -LiteralPath $Source -File -Recurse -Force | ForEach-Object {
        [IO.Path]::GetRelativePath($Source, $_.FullName).Replace('\', '/')
    })
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $zip = [IO.Compression.ZipFile]::Open($Target, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $files) {
            $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-01-01T00:00:00+00:00')
            $entry.ExternalAttributes = 0
            $stream = $entry.Open()
            try {
                $inputStream = [IO.File]::OpenRead((Join-Path $Source $relative))
                try { $inputStream.CopyTo($stream) } finally { $inputStream.Dispose() }
            } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
}

New-Item -ItemType Directory -Path $destination | Out-Null
Push-Location $repository
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
    $dirty = [bool](& git status --porcelain --untracked-files=normal)
    $sourceFiles = @(& git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source files.' }
    $sourceLines = @($sourceFiles | ForEach-Object {
        if (Test-Path -LiteralPath $_ -PathType Leaf) { "$( (Get-FileHash -LiteralPath $_).Hash )  $_" }
        else { "deleted  $_" }
    })
    $sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(($sourceLines -join "`n")))).ToLowerInvariant()
    $sdk = (& dotnet --version).Trim()
    $expectedSdk = (Get-Content -Raw global.json | ConvertFrom-Json).sdk.version
    if ($sdk -ne $expectedSdk) { throw "SDK '$sdk' does not match '$expectedSdk'." }
    $feed = Join-Path $destination 'packages'
    foreach ($package in $packages) {
        Invoke-Dotnet @('pack', "src/$package/$package.csproj", '-c', 'Release', '-o', $feed,
            '-m:1', '-p:MSBuildEnableWorkloadResolver=false', '-p:ContinuousIntegrationBuild=true')
    }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Build-Starter.ps1') -OutputDirectory $destination
    if ($LASTEXITCODE -ne 0) { throw 'Starter build failed.' }
    $standalone = Join-Path $destination 'standalone'
    Invoke-Dotnet @('publish', 'src/Obelisk.Cli/Obelisk.Cli.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-o', $standalone, '-m:1', '-p:MSBuildEnableWorkloadResolver=false',
        '-p:ContinuousIntegrationBuild=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishAot=false')
    # Copy the notices from the exact runtime pack selected by restore.
    $assets = Get-Content -Raw 'src/Obelisk.Cli/obj/project.assets.json' | ConvertFrom-Json -AsHashtable
    $runtimePacks = @($assets.project.frameworks.Values | ForEach-Object { $_.downloadDependencies } |
        Where-Object name -eq 'Microsoft.NETCore.App.Runtime.win-x64')
    if ($runtimePacks.Count -ne 1) { throw 'Cannot identify the resolved standalone .NET runtime pack.' }
    $runtimeRange = [string]$runtimePacks[0].version
    if ($runtimeRange -notmatch '^\[([^,\]]+),\s*\1\]$') { throw "Expected a pinned runtime pack, found '$runtimeRange'." }
    $runtimeVersion = $matches[1]
    $runtimeRoot = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder "microsoft.netcore.app.runtime.win-x64/$runtimeVersion"
        if (Test-Path -LiteralPath $candidate) { $runtimeRoot = $candidate; break }
    }
    if (-not $runtimeRoot) { throw 'Resolved .NET runtime pack directory is missing.' }
    $notices = Join-Path $standalone 'licenses/dotnet'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $notices
    }
    $licenseReady = (Test-Path -LiteralPath 'LICENSE') -and
        -not [string]::IsNullOrWhiteSpace([string]$props.Project.PropertyGroup.PackageLicenseExpression) -and
        -not [string]::IsNullOrWhiteSpace([string]$props.Project.PropertyGroup.Copyright)
    if ($licenseReady) { Copy-Item -LiteralPath 'LICENSE' -Destination $standalone }
    if ($licenseReady) { Copy-Item -LiteralPath 'LICENSE' -Destination $destination }
    Copy-Item -LiteralPath 'docs/release-notes-v0.1.0.md' -Destination $destination
    Write-Archive $standalone (Join-Path $destination "Obelisk-Cli-v$version-win-x64.zip")
    $items = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg') +
        @(Get-Item -LiteralPath (Join-Path $destination "Obelisk-Cli-v$version-win-x64.zip"),
            (Join-Path $destination "Obelisk-Starter-v$version.zip"), (Join-Path $destination 'release-notes-v0.1.0.md'))
    if ($licenseReady) { $items += Get-Item -LiteralPath (Join-Path $destination 'LICENSE') }
    $entries = @($items | Sort-Object Name | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace('\', '/');
            sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(); bytes = $_.Length }
    })
    [ordered]@{ schemaVersion = 1; version = $version; target = 'win-x64'; sdk = $sdk;
        sourceCommit = $commit; sourceDirty = $dirty; sourceTreeSha256 = $sourceHash; engineLicenseReady = [bool]$licenseReady;
        releaseApproved = $false; artifacts = $entries } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $destination 'release-manifest.json') -Encoding utf8
    @($entries | ForEach-Object { "$($_.sha256)  $($_.path)" }) |
        Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS') -Encoding utf8
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-ReleaseCandidate.ps1') -Directory $destination
    if ($LASTEXITCODE -ne 0) { throw 'Candidate verification failed.' }
    Write-Output "Candidate: $destination"
    Write-Output 'This build does not certify public publication, Rider or a separate SDK-free Windows machine.'
} finally { Pop-Location }
