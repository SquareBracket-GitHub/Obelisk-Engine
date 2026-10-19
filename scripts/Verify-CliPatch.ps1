param([Parameter(Mandatory)][string]$Directory, [switch]$PublicFeed)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'This encoding reproduction targets Windows.' }
$root = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content -Raw (Join-Path $root 'cli-patch-manifest.json') | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.kind -ne 'cli-patch' -or $manifest.cliVersion -notmatch '^\d+\.\d+\.\d+$' -or $manifest.engineVersion -ne '0.1.0') { throw 'Invalid CLI patch manifest.' }
$version = $manifest.cliVersion
$expectedPaths = @("packages/Obelisk.Cli.$version.nupkg", "Obelisk-Cli-v$version-win-x64.zip", "release-notes-v$version.md")
if (($expectedPaths | Sort-Object | ConvertTo-Json -Compress) -ne (@($manifest.artifacts.path) | Sort-Object | ConvertTo-Json -Compress)) { throw 'Unexpected artifact inventory.' }
$sums = @(Get-Content (Join-Path $root 'SHA256SUMS'))
if ($sums.Count -ne 3) { throw 'Unexpected checksum inventory.' }
foreach ($artifact in $manifest.artifacts) {
    $path = Join-Path $root $artifact.path
    if ((Get-Item $path).Length -ne $artifact.bytes -or (Get-FileHash $path).Hash.ToLowerInvariant() -cne $artifact.sha256 -or
        $sums -cnotcontains "$($artifact.sha256)  $($artifact.path)") { throw "Checksum mismatch: '$($artifact.path)'." }
}
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $tempParent ('obelisk-cli-encoding-' + [guid]::NewGuid().ToString('N'))
$saved = @{}; $process = $null; $previousEncoding = [Console]::OutputEncoding
function Invoke-Cli([string]$Executable, [string]$WorkingDirectory, [string[]]$Arguments, [int]$Expected = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $WorkingDirectory; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'ko-KR'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $script:process = [Diagnostics.Process]::Start($start)
    $stdout = [IO.MemoryStream]::new(); $stderr = [IO.MemoryStream]::new()
    try {
        $a = $process.StandardOutput.BaseStream.CopyToAsync($stdout); $b = $process.StandardError.BaseStream.CopyToAsync($stderr)
        if (-not $process.WaitForExit(90000)) { throw 'CLI encoding check timed out.' }
        $a.GetAwaiter().GetResult(); $b.GetAwaiter().GetResult()
        $utf8 = [Text.UTF8Encoding]::new($false, $true)
        $out = $utf8.GetString($stdout.ToArray()); $err = $utf8.GetString($stderr.ToArray())
        if ($out.StartsWith([string][char]0xfeff, [StringComparison]::Ordinal) -or $err.StartsWith([string][char]0xfeff, [StringComparison]::Ordinal)) {
            throw "Unexpected UTF-8 BOM for '$($Arguments -join ' ')'."
        }
        if ($process.ExitCode -ne $Expected) { throw "CLI exited $($process.ExitCode), expected $Expected. $err" }
        return @{ output = $out; error = $err }
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose(); $script:process = $null; $stdout.Dispose(); $stderr.Dispose()
    }
}
try {
    New-Item -ItemType Directory $work | Out-Null
    foreach ($name in @('DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH')) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, (Join-Path $work $name), 'Process')
    }
    $feed = [Security.SecurityElement]::Escape((Join-Path $root 'packages'))
    $localSource = if ($PublicFeed) { '' } else { "<add key=`"cli-patch`" value=`"$feed`"/>" }
    $config = Join-Path $work 'NuGet.Config'
    "<configuration><packageSources><clear/>$localSource<add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>" | Set-Content $config -Encoding utf8
    $tool = Join-Path $work 'tool'
    & dotnet tool install Obelisk.Cli --version $version --tool-path $tool --configfile $config
    if ($LASTEXITCODE -ne 0) { throw 'Isolated CLI patch installation failed.' }
    $installed = @(Get-ChildItem (Join-Path $tool '.store') -Recurse -Filter Obelisk.Cli.dll)
    if ($installed.Count -ne 1) { throw 'Cannot identify installed CLI payload.' }
    $package = [IO.Compression.ZipFile]::OpenRead((Join-Path $root "packages/Obelisk.Cli.$version.nupkg"))
    try {
        $entry = $package.GetEntry('tools/net10.0/any/Obelisk.Cli.dll')
        if (-not $entry) { throw 'CLI package payload missing.' }
        $stream = $entry.Open()
        try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ((Get-FileHash $installed[0].FullName).Hash -cne $expected) { throw 'Installed tool differs from the patch candidate.' }
    } finally { $package.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $root "Obelisk-Cli-v$version-win-x64.zip"), (Join-Path $work 'standalone'))
    [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
    [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(949)
    foreach ($kind in @('tool','standalone')) {
        $exe = if ($kind -eq 'tool') { Join-Path $tool 'obelisk.exe' } else { Join-Path $work 'standalone/Obelisk.Cli.exe' }
        $workspace = Join-Path $work "$kind/한글 경로 日本語 🎮"
        New-Item -ItemType Directory $workspace -Force | Out-Null
        if ((Invoke-Cli $exe $workspace @('--version')).output.Trim() -ne $version) { throw 'CLI version mismatch.' }
        $created = Invoke-Cli $exe $workspace @('new','Game')
        if (-not $created.output.Contains($workspace)) { throw 'Unicode project path was damaged.' }
        $project = Join-Path $workspace 'Game'
        [xml]$xml = Get-Content (Join-Path $project 'Game.csproj') -Raw
        if ($xml.Project.ItemGroup.PackageReference.Version -ne "[$($manifest.engineVersion)]") { throw 'Engine template version changed.' }
        'Console.WriteLine("게임 표준출력 日本語 🎮"); Console.Error.WriteLine("게임 표준오류 한글"); return 17;' | Set-Content (Join-Path $project 'Program.cs') -Encoding utf8
        $run = Invoke-Cli $exe $project @('run') 17
        if (-not $run.output.Contains('게임 표준출력 日本語 🎮') -or -not $run.error.Contains('게임 표준오류 한글') -or
            $run.output -notmatch '복원|빌드') { throw 'Localized SDK or game output was damaged.' }
        $failure = Invoke-Cli $exe $workspace @('new','잘못된이름🎮') 2
        if (-not $failure.error.Contains('잘못된이름🎮')) { throw 'Unicode CLI diagnostic was damaged.' }
        Write-Output "PASS: $kind CLI $version, CP949 parent, Korean dotnet run, Unicode stdout/stderr/path and exit codes."
    }
} finally {
    [Console]::OutputEncoding = $previousEncoding
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    $resolved = [IO.Path]::GetFullPath($work)
    if (-not $resolved.StartsWith([IO.Path]::TrimEndingDirectorySeparator($tempParent) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^obelisk-cli-encoding-[a-f0-9]{32}$') { throw 'Refusing cleanup outside the encoding workspace.' }
    if (Test-Path $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
