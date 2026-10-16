param([string]$WorkDirectory)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This build produces the Windows x64 SDL runtime.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = if ($WorkDirectory) { [IO.Path]::GetFullPath($WorkDirectory) } else { Join-Path $repository 'artifacts/native-sdl' }
$archiveHash = '7322236CD12090C3EB40B9728BE4D49C76F66AD17D04369584D4ECAD5CF77C68'
$sourceCommit = 'fa2c02bb6e21974a89ea9824bc53c9932abe5f9c'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath -latest
if (-not $installation) { throw 'Install Visual Studio 2022 C++ tools (v143), MSVC 14.44.35207 and Windows SDK 10.0.26100.0.' }
$msbuild = Join-Path $installation 'MSBuild/Current/Bin/MSBuild.exe'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive = Join-Path $work 'SDL3-3.4.16.tar.gz'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://github.com/libsdl-org/SDL/releases/download/release-3.4.16/SDL3-3.4.16.tar.gz' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) { throw 'SDL source archive checksum mismatch.' }
# Fresh source and object directories prevent reuse of an already patched source or stale objects.
$build = Join-Path $work ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build | Out-Null
& tar -xzf $archive -C $build
if ($LASTEXITCODE) { throw 'SDL source extraction failed.' }
$source = Join-Path $build 'SDL3-3.4.16'
$patch = Join-Path $repository 'native/sdl3/patches/0001-close-hotplug-event.patch'
# Isolate git apply from the engine checkout's ignored artifacts/ prefix.
& git init --quiet $source
if ($LASTEXITCODE) { throw 'Could not isolate the SDL source work directory.' }
Push-Location $source
try {
    & git apply --no-index --check $patch
    if ($LASTEXITCODE) { throw 'SDL patch does not match the pinned source.' }
    & git apply --no-index $patch
    if ($LASTEXITCODE) { throw 'SDL patch failed.' }
} finally { Pop-Location }
if ((Get-Content -Raw -LiteralPath (Join-Path $source 'src/core/windows/SDL_hid.c')) -notmatch 'CloseHandle\(s_HotplugEvent\)') {
    throw 'The expected hotplug event fix is absent from the extracted source.'
}
$output = Join-Path $build 'output'
$objects = Join-Path $build 'objects'
$log = Join-Path $build 'build.log'
$savedLinkOptions = [Environment]::GetEnvironmentVariable('LINK', 'Process')
$savedCompilerOptions = [Environment]::GetEnvironmentVariable('CL', 'Process')
try {
    # Preserve a portable PDB name rather than publishing a developer's absolute path.
    $env:LINK = '/PDBALTPATH:SDL3.pdb'
    # Supported by this pinned compiler; make embedded __FILE__ strings source-relative.
    $env:CL = '/d1trimfile:"' + $source + '"'
    & $msbuild (Join-Path $source 'VisualC/SDL/SDL.vcxproj') /m /v:minimal /nologo /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:VCToolsVersion=14.44.35207 /p:WindowsTargetPlatformVersion=10.0.26100.0 "/p:OutDir=$output\" "/p:IntDir=$objects\" *> $log
} finally {
    [Environment]::SetEnvironmentVariable('LINK', $savedLinkOptions, 'Process')
    [Environment]::SetEnvironmentVariable('CL', $savedCompilerOptions, 'Process')
}
if ($LASTEXITCODE) { Get-Content -LiteralPath $log -Tail 40; throw "SDL build failed; inspect '$log'." }
$binaryText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $output 'SDL3.dll')))
if ($binaryText.Contains($source) -or $binaryText.Contains($output)) { throw 'The DLL embeds an absolute build path.' }
$destination = Join-Path $repository 'native/sdl3/win-x64/SDL3.dll'
Copy-Item -LiteralPath (Join-Path $output 'SDL3.dll') -Destination $destination
[ordered]@{
    Version = '3.4.16-obelisk.1'; SourceCommit = $sourceCommit
    SourceUrl = 'https://github.com/libsdl-org/SDL/releases/download/release-3.4.16/SDL3-3.4.16.tar.gz'
    SourceSha256 = $archiveHash; PatchSha256 = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash
    BinarySha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    Platform = 'win-x64'; Configuration = 'Release'; Toolset = 'v143'
    MsvcVersion = '14.44.35207'; WindowsSdk = '10.0.26100.0'
    CrtLinkage = 'Static (upstream VisualC Release configuration)'
    LinkerOptions = '/PDBALTPATH:SDL3.pdb'
    CompilerOptions = '/d1trimfile:<source-root> (source-relative __FILE__)'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repository 'native/sdl3/build.json') -Encoding utf8
$notices = Join-Path $repository 'licenses/sdl3/obelisk'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'native/sdl3/build.json') -Destination $notices
Copy-Item -LiteralPath $patch -Destination $notices
Write-Output "SDL native build packaged at '$destination'. Log: $log"
