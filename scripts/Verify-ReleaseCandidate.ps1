param([Parameter(Mandatory)][string]$Directory, [string]$CompareDirectory)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'release-manifest.json') | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.version -ne '0.1.0' -or $manifest.target -ne 'win-x64') {
    throw 'Unsupported candidate schema/version/target.'
}
$expected = @('Obelisk.Abstractions', 'Obelisk.Runtime', 'Obelisk.Platform.Sdl3', 'Obelisk.Graphics2D',
    'Obelisk.Assets', 'Obelisk.Plugin', 'Obelisk.Plugins.Images', 'Obelisk.Vanilla', 'Obelisk.Cli')
$expectedPaths = @($expected | ForEach-Object { "packages/$_.$($manifest.version).nupkg" }) +
    @("Obelisk-Cli-v$($manifest.version)-win-x64.zip", "Obelisk-Starter-v$($manifest.version).zip", 'release-notes-v0.1.0.md')
if ($manifest.engineLicenseReady) { $expectedPaths += 'LICENSE' }
if (($expectedPaths | Sort-Object | ConvertTo-Json -Compress) -ne
    (@($manifest.artifacts.path) | Sort-Object | ConvertTo-Json -Compress)) { throw 'Candidate artifact inventory differs.' }
$checksumLines = @(Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS'))
if ($checksumLines.Count -ne $expectedPaths.Count) { throw 'Checksum inventory differs.' }
foreach ($artifact in $manifest.artifacts) {
    $path = Join-Path $root $artifact.path
    if ((Get-Item -LiteralPath $path).Length -ne $artifact.bytes -or
        (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -cne $artifact.sha256 -or
        $checksumLines -cnotcontains "$($artifact.sha256)  $($artifact.path)") { throw "Checksum mismatch: '$($artifact.path)'." }
}
foreach ($id in $expected) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root "packages/$id.$($manifest.version).nupkg"))
    try {
        $nuspec = @($zip.Entries | Where-Object FullName -like '*.nuspec')
        if ($nuspec.Count -ne 1) { throw "Invalid nuspec: '$id'." }
        $reader = [IO.StreamReader]::new($nuspec[0].Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $xml.package.metadata
        if ($metadata.id -ne $id -or $metadata.version -ne $manifest.version) { throw "Package identity mismatch: '$id'." }
        foreach ($dependency in $xml.SelectNodes("//*[local-name()='dependency']")) {
            if ($dependency.id.StartsWith('Obelisk.') -and $dependency.version.Trim('[', ']') -ne $manifest.version) {
                throw "Engine dependency version mismatch in '$id'."
            }
        }
        if ($id -ne 'Obelisk.Cli' -and -not $zip.GetEntry("lib/net10.0/$id.xml")) { throw "XML reference missing: '$id'." }
        if ($manifest.engineLicenseReady -and (-not $metadata.license -or -not $metadata.copyright)) {
            throw "Engine license metadata missing: '$id'."
        }
        if ($manifest.engineLicenseReady) {
            if ([string]$metadata.license.'#text' -ne 'MIT' -or $metadata.license.type -ne 'expression' -or
                $metadata.copyright -ne 'Copyright (c) 2026 Taegyun Eom' -or $metadata.authors -ne 'Taegyun Eom') {
                throw "Owner-approved license metadata differs: '$id'."
            }
            $license = $zip.GetEntry('LICENSE')
            if (-not $license) { throw "Engine license text missing: '$id'." }
            $stream = $license.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'LICENSE')).Hash) { throw "Engine license text differs: '$id'." }
        }
    } finally { $zip.Dispose() }
}
$starter = [IO.Compression.ZipFile]::OpenRead((Join-Path $root "Obelisk-Starter-v$($manifest.version).zip"))
try {
    if ($manifest.engineLicenseReady) {
        $entry = $starter.GetEntry('LICENSE')
        if (-not $entry) { throw 'Starter archive engine license missing.' }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'LICENSE')).Hash) { throw 'Starter engine license differs.' }
    }
    foreach ($name in @('ObeliskStarter/ObeliskStarter.csproj', 'ObeliskStarter/obelisk.json')) {
        $entry = $starter.GetEntry($name)
        if (-not $entry) { throw "Missing Starter entry '$name'." }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $value = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($name.EndsWith('.csproj')) {
            [xml]$project = $value
            if ($project.Project.ItemGroup.PackageReference.Version -ne "[$($manifest.version)]") { throw 'Starter package version mismatch.' }
        } elseif (($value | ConvertFrom-Json).engineVersion -ne $manifest.version) { throw 'Starter manifest version mismatch.' }
    }
} finally { $starter.Dispose() }
$cli = [IO.Compression.ZipFile]::OpenRead((Join-Path $root "Obelisk-Cli-v$($manifest.version)-win-x64.zip"))
try {
    foreach ($name in @('Obelisk.Cli.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
        'licenses/dotnet/LICENSE.TXT', 'licenses/dotnet/THIRD-PARTY-NOTICES.TXT')) {
        if (-not $cli.GetEntry($name)) { throw "Standalone CLI lacks '$name'." }
    }
    if ($manifest.engineLicenseReady -and -not $cli.GetEntry('LICENSE')) { throw 'Standalone engine license missing.' }
} finally { $cli.Dispose() }
Write-Output 'CANDIDATE INTEGRITY PASSED: nine packages, XML references, dependency/template versions, standalone runtime/notices and SHA256 inventory.'
if ($CompareDirectory) {
    $other = [IO.Path]::GetFullPath($CompareDirectory)
    & pwsh -NoProfile -File $PSCommandPath -Directory $other
    if ($LASTEXITCODE -ne 0) { throw 'Comparison candidate is invalid.' }
    $otherManifest = Get-Content -Raw (Join-Path $other 'release-manifest.json') | ConvertFrom-Json
    if ($manifest.sourceCommit -ne $otherManifest.sourceCommit -or $manifest.sdk -ne $otherManifest.sdk -or
        -not $manifest.sourceTreeSha256 -or $manifest.sourceTreeSha256 -ne $otherManifest.sourceTreeSha256) {
        throw 'Comparison must use the same source revision, source tree content and SDK.'
    }
    foreach ($artifact in $manifest.artifacts) {
        $left = Join-Path $root $artifact.path
        $right = Join-Path $other $artifact.path
        if ($artifact.path -notmatch '\.(zip|nupkg)$') {
            if ((Get-FileHash -LiteralPath $left).Hash -ne (Get-FileHash -LiteralPath $right).Hash) { throw "Recreated file differs: '$($artifact.path)'." }
            continue
        }
        $a = [IO.Compression.ZipFile]::OpenRead($left)
        $b = [IO.Compression.ZipFile]::OpenRead($right)
        try {
            if ((@($a.Entries.FullName) | Sort-Object | ConvertTo-Json -Compress) -ne
                (@($b.Entries.FullName) | Sort-Object | ConvertTo-Json -Compress)) { throw "Recreated archive inventory differs: '$($artifact.path)'." }
            foreach ($entry in $a.Entries) {
                $sa = $entry.Open(); $sb = $b.GetEntry($entry.FullName).Open()
                try {
                    if ($artifact.path.EndsWith('.nupkg') -and $entry.FullName -eq '_rels/.rels') {
                        # NuGet generates arbitrary relationship identifiers on every pack.
                        $ra = [IO.StreamReader]::new($sa); $rb = [IO.StreamReader]::new($sb)
                        try {
                            $equal = ($ra.ReadToEnd() -replace ' Id="[^"]+"', '') -ceq
                                ($rb.ReadToEnd() -replace ' Id="[^"]+"', '')
                        } finally { $ra.Dispose(); $rb.Dispose() }
                    } else {
                        $ha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($sa))
                        $hb = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($sb))
                        $equal = $ha -ceq $hb
                    }
                    if (-not $equal) { throw "Recreated archive content differs: '$($artifact.path)/$($entry.FullName)'." }
                } finally { $sa.Dispose(); $sb.Dispose() }
            }
        } finally { $a.Dispose(); $b.Dispose() }
    }
    Write-Output 'RECREATION PASSED: every archive payload byte matches; only ZIP metadata and NuGet relationship IDs are excluded. NuGet archive SHA256 values need not match across packs.'
}
