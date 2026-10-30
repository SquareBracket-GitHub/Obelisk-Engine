param([Parameter(Mandatory)][string]$Directory, [switch]$PublicFeed)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -Raw (Join-Path $root 'engine-candidate.json') | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.engineVersion -ne '0.2.0') { throw 'Expected engine 0.2.0 candidate.' }
$expected = @('Obelisk.Abstractions','Obelisk.Assets','Obelisk.Cli','Obelisk.Graphics2D','Obelisk.Platform.Sdl3','Obelisk.Plugin','Obelisk.Plugins.Images','Obelisk.Plugins.Text','Obelisk.Plugins.Effects','Obelisk.Runtime','Obelisk.Vanilla')
if (($expected | Sort-Object | ConvertTo-Json -Compress) -ne (@($manifest.packages.id) | Sort-Object | ConvertTo-Json -Compress)) { throw 'Unexpected package set.' }
foreach ($package in $manifest.packages) {
    $expectedVersion = if ($package.id -eq 'Obelisk.Plugins.Images') { '0.1.1' } elseif ($package.id -like 'Obelisk.Plugins.*') { '0.1.0' } else { '0.2.0' }
    $expectedKind = if ($package.id -like 'Obelisk.Plugins.*') { 'plugin' } else { 'engine' }
    if ($package.kind -cne $expectedKind -or $package.version -ne $expectedVersion -or $package.path -cne "packages/$($package.id).$($package.version).nupkg" -or
        (Get-FileHash (Join-Path $root $package.path)).Hash.ToLowerInvariant() -cne $package.sha256) { throw 'Invalid candidate package inventory/hash.' }
    if ($expectedKind -eq 'plugin' -and $package.id -ne 'Obelisk.Plugins.Images') {
        $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $root $package.path))
        try {
            $entry = $archive.GetEntry('obelisk.plugin.json')
            if (-not $entry) { throw 'Plugin package is missing its manifest.' }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $pluginManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            if ($pluginManifest.version -ne $expectedVersion -or $pluginManifest.engineMinimum -ne '0.2.0' -or $pluginManifest.engineMaximumExclusive -ne '0.3.0') { throw 'Invalid plugin package manifest.' }
        } finally { $archive.Dispose() }
    }
}
$images = @($manifest.packages | Where-Object id -eq 'Obelisk.Plugins.Images')
if ($images.Count -ne 1 -or $images[0].version -ne '0.1.1') { throw 'Images must retain an independent 0.1.1 compatibility-patch version.' }
if ($manifest.starter -cne 'Obelisk-Starter-v0.2.0.zip' -or
    (Get-FileHash (Join-Path $root $manifest.starter)).Hash.ToLowerInvariant() -cne $manifest.starterSha256) { throw 'Invalid Starter hash.' }
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $parent ('obelisk-engine-gate-' + [guid]::NewGuid().ToString('N'))
$saved = @{}
function Invoke-Dotnet([string[]]$Arguments) { & dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Candidate SDK/tool operation failed.' } }
try {
    New-Item -ItemType Directory $work | Out-Null
    foreach ($name in @('DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH')) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, (Join-Path $work $name), 'Process')
    }
    $feed = [Security.SecurityElement]::Escape((Join-Path $root 'packages'))
    $config = Join-Path $work 'NuGet.Config'
    $localSource = if ($PublicFeed) { '' } else { "<add key=`"candidate`" value=`"$feed`"/>" }
    "<configuration><packageSources><clear/>$localSource<add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>" | Set-Content $config -Encoding utf8
    $tool = Join-Path $work 'tool'
    Invoke-Dotnet @('tool','install','Obelisk.Cli','--version','0.2.0','--tool-path',$tool,'--configfile',$config)
    $cli = Join-Path $tool 'obelisk.exe'
    Push-Location $work
    try { & $cli new Game; if ($LASTEXITCODE -ne 0) { throw 'Project generation failed.' } } finally { Pop-Location }
    $project = Join-Path $work 'Game'
    [xml]$generated = Get-Content -Raw (Join-Path $project 'Game.csproj')
    if ($generated.Project.ItemGroup.PackageReference.Version -ne '[0.2.0]') { throw 'Generated project targets the wrong engine.' }
    @'
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Text;
using Obelisk.Plugins.Effects;
using Obelisk.Vanilla;
using System.Numerics;
return ObeliskApp.Run(new Probe(), new AppOptions { Width=256, Height=192, SampleCount=4 }, new PluginOptions { IncludeEffects=true });
sealed class Probe : IGame {
    private RenderTarget? target; private Material? material; private Texture? texture; private int frames;
    private FontFace? face; private DynamicFont? font; private BlurEffect? blur;
    public void Initialize(InitializeContext c) {
        target=c.Graphics.CreateRenderTarget(128,96);
        face=FontFace.LoadFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"malgun.ttf"));
        font=new DynamicFont(c.Textures,face); blur=new BlurEffect(c.Graphics);
        material=c.Graphics.CreateMaterial("float4 Shade(float4 color,float2 uv){return SampleImage(uv)*color*Parameters;}");
        texture=c.Textures.CreateTexture(new RgbaImage(2,1,new byte[]{255,0,0,255,0,0,255,255}));
        c.Textures.UpdateTexture(texture,1,0,new RgbaImage(1,1,new byte[]{0,255,0,255}));
        if(c.Graphics.SampleCount<1 || c.Graphics.DisplayScale<=0) throw new Exception("Invalid render capabilities");
    }
    public void Update(UpdateContext c) {
        _=c.Input.Mouse.WheelX+c.Input.Mouse.WheelY;
        if(++frames==9) { using var p=System.Diagnostics.Process.GetCurrentProcess(); if(!p.CloseMainWindow()) throw new Exception("Cannot close probe window"); }
        if(frames>120) throw new Exception("Probe did not stop after close request");
    }
    public void Render(RenderContext c) {
        c.Canvas.RenderTo(target!, layer=> { layer.Filter=TextureFilter.Linear; layer.DrawSprite(texture!,new(0,0,16,16)); font!.Draw(layer,c.Time.FrameNumber,"한글 Aa",new(0,20),24); });
        blur!.Draw(c,target!.Texture,new(0,0,128,96),new BlurSettings { Radius=4, Downsample=2 });
        c.Canvas.Material=material; c.Canvas.Parameters=Vector4.One; c.Canvas.DrawSprite(target!.Texture,new(0,0,32,32));
    }
    public void Shutdown(ShutdownContext c) { blur?.Dispose(); font?.Dispose(); face?.Dispose(); texture?.Dispose(); material?.Dispose(); target?.Dispose(); Console.WriteLine("ENGINE_PROBE_SHUTDOWN"); }
}
'@ | Set-Content (Join-Path $project 'Program.cs') -Encoding utf8
    Invoke-Dotnet @('restore',(Join-Path $project 'Game.csproj'),'--configfile',$config)
    Invoke-Dotnet @('build',(Join-Path $project 'Game.csproj'),'-c','Release','--no-restore')
    Push-Location $project
    try {
        $output = & $cli run 2>&1
        $output | Write-Output
        if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'ENGINE_PROBE_SHUTDOWN') { throw 'Independent native rendering probe or shutdown failed.' }
        & $cli build
        if ($LASTEXITCODE -ne 0) { throw 'Self-contained build failed.' }
    } finally { Pop-Location }
    $distribution = Join-Path $project 'artifacts/build/win-x64'
    foreach ($required in @('Game.exe','coreclr.dll','licenses/obelisk/LICENSE','licenses/dotnet/LICENSE.TXT')) {
        if (-not (Test-Path (Join-Path $distribution $required))) { throw "Missing distribution file '$required'." }
    }
    $relocated = Join-Path $work 'relocated distribution'
    Copy-Item -LiteralPath $distribution -Destination $relocated -Recurse
    $previousPath = $env:PATH; $previousDotnet = $env:DOTNET_ROOT
    try {
        $env:PATH = Join-Path $env:SystemRoot 'System32'; $env:DOTNET_ROOT = Join-Path $work 'unavailable-runtime'
        Push-Location $relocated
        try {
            $output = & (Join-Path $relocated 'Game.exe') 2>&1
            $output | Write-Output
            if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'ENGINE_PROBE_SHUTDOWN') { throw 'Relocated SDK-independent game failed.' }
        } finally { Pop-Location }
    } finally { $env:PATH = $previousPath; $env:DOTNET_ROOT = $previousDotnet }
    $assets = Get-Content (Join-Path $project 'obj/project.assets.json') -Raw | ConvertFrom-Json
    if ($assets.libraries.PSObject.Properties.Name -notcontains 'Obelisk.Plugins.Images/0.1.1') { throw 'Vanilla did not resolve the independently versioned Images package.' }
    foreach ($id in @('Obelisk.Plugins.Text','Obelisk.Plugins.Effects')) {
        if ($assets.libraries.PSObject.Properties.Name -notcontains "$id/0.1.0") { throw "Vanilla did not resolve independently versioned $id." }
    }
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root $manifest.starter))
    try {
        $entry = $zip.Entries | Where-Object FullName -like '*/ObeliskStarter.csproj'
        $reader = [IO.StreamReader]::new($entry.Open())
        try { if ($reader.ReadToEnd() -notmatch '\[0\.2\.0\]') { throw 'Starter version mismatch.' } } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    $sourceKind = if ($PublicFeed) { 'public nuget.org only' } else { 'isolated candidate packages' }
    Write-Output "ENGINE 0.2 GATE PASSED: $sourceKind -> CLI 0.2 -> native rendering + dynamic Korean text + blur -> normal shutdown -> relocated self-contained game without SDK PATH -> Images 0.1.1, Text/Effects 0.1.0 -> Starter 0.2."
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    $resolved = [IO.Path]::GetFullPath($work)
    if (-not $resolved.StartsWith([IO.Path]::TrimEndingDirectorySeparator($parent) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^obelisk-engine-gate-[a-f0-9]{32}$') { throw 'Unsafe cleanup path.' }
    if (Test-Path $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
