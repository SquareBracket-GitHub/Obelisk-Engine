param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$props = Get-Content -Raw (Join-Path $repository 'Directory.Build.props')
$engineVersion = [string]$props.Project.PropertyGroup.Version
$destination = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $repository ('artifacts/engine-candidate-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $destination) { throw 'Candidate already exists; choose a fresh directory.' }
New-Item -ItemType Directory $destination | Out-Null
$packages = Join-Path $destination 'packages'
$inventory = @()
Push-Location $repository
try {
    foreach ($project in Get-ChildItem src/*/*.csproj | Sort-Object FullName) {
        [xml]$xml = Get-Content -Raw $project.FullName
        $version = [string]$xml.Project.PropertyGroup.Version
        $id = [IO.Path]::GetFileNameWithoutExtension($project.Name)
        if ($id -like 'Obelisk.Plugins.*') {
            if (-not $version) { throw "Plugin '$id' requires an explicit independent package Version." }
            $plugin = Get-Content -Raw (Join-Path $project.DirectoryName 'obelisk.plugin.json') | ConvertFrom-Json
            if ($plugin.version -ne $version -or [version]$engineVersion -lt [version]$plugin.engineMinimum -or
                [version]$engineVersion -ge [version]$plugin.engineMaximumExclusive) { throw "Plugin '$id' version/range metadata disagrees with this candidate." }
        }
        if (-not $version) { $version = $engineVersion }
        & dotnet pack $project.FullName -c Release -o $packages -m:1 -p:MSBuildEnableWorkloadResolver=false
        if ($LASTEXITCODE -ne 0) { throw "Packing '$id' failed." }
        $path = "packages/$id.$version.nupkg"
        if (-not (Test-Path (Join-Path $destination $path))) { throw "Missing package '$path'." }
        $inventory += [ordered]@{ id = $id; version = $version; kind = $(if ($id -like 'Obelisk.Plugins.*') { 'plugin' } else { 'engine' });
            path = $path; sha256 = (Get-FileHash (Join-Path $destination $path)).Hash.ToLowerInvariant() }
    }
    & pwsh -NoProfile -File scripts/Build-Starter.ps1 -OutputDirectory $destination
    if ($LASTEXITCODE -ne 0) { throw 'Starter generation failed.' }
    [ordered]@{ schemaVersion = 1; engineVersion = $engineVersion; sourceCommit = (& git rev-parse HEAD).Trim();
        sourceDirty = [bool](& git status --porcelain); published = $false; packages = $inventory;
        starter = "Obelisk-Starter-v$engineVersion.zip";
        starterSha256 = (Get-FileHash (Join-Path $destination "Obelisk-Starter-v$engineVersion.zip")).Hash.ToLowerInvariant() } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $destination 'engine-candidate.json') -Encoding utf8
    Write-Output "Engine candidate: $destination. Plugin versions are recorded independently; no packages were published."
} finally { Pop-Location }
