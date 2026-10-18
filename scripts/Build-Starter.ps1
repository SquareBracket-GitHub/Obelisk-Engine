param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$properties = Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props')
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw "Unexpected engine version '$version'." }
$starter = 'ObeliskStarter'
$destination = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $repository 'artifacts/starter' }
$archivePath = Join-Path $destination "Obelisk-Starter-v$version.zip"
$tempParent = Join-Path $repository 'artifacts/starter-work'
$taskRoot = Join-Path $tempParent ('obelisk-starter-build-' + [guid]::NewGuid().ToString('N'))
$workingArchive = Join-Path $taskRoot 'starter.zip'

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

try {
    New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
    $cliProject = Join-Path $repository 'src/Obelisk.Cli/Obelisk.Cli.csproj'
    Invoke-Dotnet @('build', $cliProject, '-c', 'Release', '-m:1', '-p:MSBuildEnableWorkloadResolver=false')
    $cli = Join-Path $repository 'src/Obelisk.Cli/bin/Release/net10.0/Obelisk.Cli.dll'
    $cliVersion = (& dotnet $cli --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $cliVersion -ne $version) { throw "CLI version '$cliVersion' does not match engine version '$version'." }
    Push-Location $taskRoot
    try { Invoke-Dotnet @($cli, 'new', $starter) }
    finally { Pop-Location }
    $project = Join-Path $taskRoot "$starter/$starter.csproj"
    [xml]$projectXml = Get-Content -Raw -LiteralPath $project
    $references = @($projectXml.Project.ItemGroup.PackageReference | Where-Object { $_ })
    if ($references.Count -ne 1 -or $references[0].Include -ne 'Obelisk.Vanilla' -or $references[0].Version -ne "[$version]" -or
        $projectXml.Project.ItemGroup.ProjectReference) { throw 'Starter project must reference only the exact matching Obelisk.Vanilla package.' }
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $taskRoot "$starter/obelisk.json") | ConvertFrom-Json
    if ($manifest.engineVersion -ne $version -or $manifest.project -ne "$starter.csproj") { throw 'Starter manifest version or project does not match the CLI template.' }

    $source = Join-Path $taskRoot $starter
    [string[]]$files = @(Get-ChildItem -LiteralPath $source -File -Recurse -Force | ForEach-Object {
        [IO.Path]::GetRelativePath($source, $_.FullName).Replace('\', '/')
    })
    [Array]::Sort($files, [StringComparer]::Ordinal)
    if ($files.Count -ne 8) { throw "Starter should contain the eight CLI template files; found $($files.Count)." }
    $archive = [IO.Compression.ZipFile]::Open($workingArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $files) {
            $entry = $archive.CreateEntry("$starter/$relative", [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-01-01T00:00:00+00:00')
            $entry.ExternalAttributes = 0
            $entryStream = $entry.Open()
            try {
                $fileStream = [IO.File]::OpenRead((Join-Path $source $relative))
                try { $fileStream.CopyTo($entryStream) }
                finally { $fileStream.Dispose() }
            } finally { $entryStream.Dispose() }
        }
        $entry = $archive.CreateEntry('LICENSE', [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-01-01T00:00:00+00:00')
        $entry.ExternalAttributes = 0
        $entryStream = $entry.Open()
        try {
            $fileStream = [IO.File]::OpenRead((Join-Path $repository 'LICENSE'))
            try { $fileStream.CopyTo($entryStream) } finally { $fileStream.Dispose() }
        } finally { $entryStream.Dispose() }
    } finally { $archive.Dispose() }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Move-Item -LiteralPath $workingArchive -Destination $archivePath -Force
    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "Starter archive: $archivePath"
    Write-Output "Version: $version"
    Write-Output "SHA256: $checksum"
} finally {
    if (Test-Path -LiteralPath $taskRoot) {
        $resolved = [IO.Path]::GetFullPath($taskRoot)
        $prefix = [IO.Path]::TrimEndingDirectorySeparator($tempParent) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notmatch '^obelisk-starter-build-[a-f0-9]{32}$') {
            throw 'Refusing to clean up outside the Starter build workspace.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
