param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$cli = Get-Content -Raw (Join-Path $repository 'src/Obelisk.Cli/Obelisk.Cli.csproj')
[xml]$engine = Get-Content -Raw (Join-Path $repository 'Directory.Build.props')
$version = [string]$cli.Project.PropertyGroup.Version
$engineVersion = [string]$engine.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected an explicit CLI patch version.' }
$destination = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $repository ('artifacts/cli-patch-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $destination) { throw 'Candidate directory already exists; choose a new path.' }
New-Item -ItemType Directory -Path $destination | Out-Null
Push-Location $repository
try {
    $project = 'src/Obelisk.Cli/Obelisk.Cli.csproj'
    & dotnet pack $project -c Release -o (Join-Path $destination 'packages') -m:1 -p:MSBuildEnableWorkloadResolver=false
    if ($LASTEXITCODE -ne 0) { throw 'CLI tool packing failed.' }
    $standalone = Join-Path $destination 'standalone'
    & dotnet publish $project -c Release -r win-x64 --self-contained true -o $standalone -m:1 -p:MSBuildEnableWorkloadResolver=false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false
    if ($LASTEXITCODE -ne 0) { throw 'Standalone CLI publishing failed.' }
    foreach ($notice in @('licenses/dotnet/LICENSE.TXT','licenses/dotnet/THIRD-PARTY-NOTICES.TXT','licenses/obelisk/LICENSE')) {
        if (-not (Test-Path (Join-Path $standalone $notice))) { throw "Missing runtime/engine notice '$notice'." }
    }
    Copy-Item LICENSE (Join-Path $standalone 'LICENSE')
    Copy-Item "docs/release-notes-v$version.md" $destination
    $zipPath = Join-Path $destination "Obelisk-Cli-v$version-win-x64.zip"
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        [string[]]$files = @(Get-ChildItem $standalone -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($standalone, $_.FullName).Replace('\','/') })
        [Array]::Sort($files, [StringComparer]::Ordinal)
        foreach ($file in $files) {
            $entry = $zip.CreateEntry($file, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-01-01T00:00:00+00:00'); $entry.ExternalAttributes = 0
            $stream = $entry.Open(); $source = [IO.File]::OpenRead((Join-Path $standalone $file))
            try { $source.CopyTo($stream) } finally { $source.Dispose(); $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    $paths = @("packages/Obelisk.Cli.$version.nupkg", "Obelisk-Cli-v$version-win-x64.zip", "release-notes-v$version.md")
    $artifacts = @($paths | ForEach-Object { $file = Get-Item (Join-Path $destination $_); [ordered]@{
        path = $_; bytes = $file.Length; sha256 = (Get-FileHash $file.FullName).Hash.ToLowerInvariant()
    } })
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source revision.' }
    [ordered]@{ schemaVersion = 1; kind = 'cli-patch'; cliVersion = $version; engineVersion = $engineVersion;
        sourceCommit = $commit; sourceDirty = [bool](& git status --porcelain); published = $false; artifacts = $artifacts } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $destination 'cli-patch-manifest.json') -Encoding utf8
    $artifacts | ForEach-Object { "$($_.sha256)  $($_.path)" } | Set-Content (Join-Path $destination 'SHA256SUMS') -Encoding utf8
    Write-Output "CLI patch candidate: $destination (CLI $version / engine $engineVersion). No packages were pushed."
} finally { Pop-Location }
