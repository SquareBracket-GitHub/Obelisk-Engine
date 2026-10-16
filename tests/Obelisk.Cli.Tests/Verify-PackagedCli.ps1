param([switch]$KeepWorkspace, [switch]$DevelopmentLoop, [switch]$Distribution, [switch]$Starter)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'The native generated-game verification requires Windows.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskRoot = Join-Path $tempParent ('obelisk-day9-' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $taskRoot 'feed'
$workspace = Join-Path $taskRoot 'empty workspace'
$cliHome = Join-Path $taskRoot 'cli-home'
$savedEnvironment = @{}
$gameProcess = $null
$cliProcess = $null

function Wait-Until([scriptblock]$Condition, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw $Message }
        Start-Sleep -Milliseconds 100
    }
}

function Start-Cli([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($cli)
    $start.WorkingDirectory = $project
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $script:cliProcess = [Diagnostics.Process]::Start($start)
    $script:cliOutput = $cliProcess.StandardOutput.ReadToEndAsync()
    $script:cliErrors = $cliProcess.StandardError.ReadToEndAsync()
}

function Read-Starts {
    if (Test-Path -LiteralPath $probe) {
        foreach ($line in Get-Content -LiteralPath $probe) {
            if ($line) { ConvertFrom-Json $line }
        }
    }
}

function Test-Alive([int]$ProcessId) {
    $candidate = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($candidate) { $candidate.Dispose(); return $true }
    return $false
}

function Verify-Template([string]$Directory, [string]$RepositoryPath) {
    [xml]$projectXml = Get-Content -Raw -LiteralPath (Join-Path $Directory 'ObeliskStarter.csproj')
    $references = @($projectXml.Project.ItemGroup.PackageReference | Where-Object { $_ })
    if ($references.Count -ne 1 -or $references[0].Include -ne 'Obelisk.Vanilla' -or
        $references[0].Version -ne '[0.1.0]' -or $projectXml.Project.ItemGroup.ProjectReference) {
        throw "'$Directory' must use only Obelisk.Vanilla [0.1.0], without source references."
    }
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $Directory 'obelisk.json') | ConvertFrom-Json
    if ($manifest.engineVersion -ne '0.1.0' -or $manifest.project -ne 'ObeliskStarter.csproj') {
        throw "'$Directory' has mismatched manifest metadata."
    }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force) {
        $contents = Get-Content -Raw -LiteralPath $file.FullName
        if ($contents.Contains($RepositoryPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "'$($file.FullName)' contains an engine checkout path."
        }
    }
}

function Verify-TemplateRun([string]$Directory, [string]$Probe, [switch]$UseCli) {
    $source = Join-Path $Directory 'Game.cs'
    $gameCode = Get-Content -Raw -LiteralPath $source
    $old = 'public void Initialize(InitializeContext context) { }'
    if (-not $gameCode.Contains($old)) { throw "Unexpected game template in '$Directory'." }
    $literal = $Probe.Replace('"', '""')
    $replacement = "public void Initialize(InitializeContext context) => File.WriteAllText(@`"$literal`", Environment.ProcessId.ToString());"
    Set-Content -LiteralPath $source -Encoding utf8 -Value ($gameCode.Replace($old, $replacement))
    if ($UseCli) {
        $script:project = $Directory
        Start-Cli @('run')
    } else {
        $projectFile = Join-Path $Directory 'ObeliskStarter.csproj'
        Invoke-Dotnet @('restore', $projectFile, '--configfile', $configuration)
        Invoke-Dotnet @('build', $projectFile, '-c', 'Release', '--no-restore')
        $run = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $run.WorkingDirectory = $Directory
        $run.UseShellExecute = $false
        $run.CreateNoWindow = $true
        $run.RedirectStandardOutput = $true
        $run.RedirectStandardError = $true
        foreach ($part in @('run', '--project', $projectFile, '-c', 'Release', '--no-build', '--no-restore')) {
            $run.ArgumentList.Add($part)
        }
        $script:cliProcess = [Diagnostics.Process]::Start($run)
        $script:cliOutput = $cliProcess.StandardOutput.ReadToEndAsync()
        $script:cliErrors = $cliProcess.StandardError.ReadToEndAsync()
    }
    Wait-Until { (Test-Path -LiteralPath $Probe) -or $cliProcess.HasExited } "Game from '$Directory' did not initialize."
    if ($cliProcess.HasExited) { throw "Game from '$Directory' exited: $($cliErrors.GetAwaiter().GetResult())" }
    $id = [int](Get-Content -Raw -LiteralPath $Probe)
    $running = Get-Process -Id $id
    try {
        Wait-Until { $running.Refresh(); $running.HasExited -or $running.MainWindowHandle -ne 0 } "Game from '$Directory' did not open an SDL window."
        if ($running.HasExited -or -not $running.CloseMainWindow()) { throw "Game from '$Directory' could not close normally." }
        if (-not $running.WaitForExit(10000)) {
            $errorText = if ($cliErrors) { $cliErrors.GetAwaiter().GetResult() } else { '' }
            $outputText = if ($cliOutput) { $cliOutput.GetAwaiter().GetResult() } else { '' }
            throw "Game from '$Directory' did not exit after closing its window. stderr: $errorText stdout: $outputText"
        }
    } finally { $running.Dispose() }
    if (-not $cliProcess.WaitForExit(10000) -or $cliProcess.ExitCode -ne 0) {
        throw "Run command failed for '$Directory' (exit $($cliProcess.ExitCode)): $($cliErrors.GetAwaiter().GetResult())"
    }
    if ($cliErrors.GetAwaiter().GetResult()) { throw "Run command reported errors for '$Directory'." }
    $cliProcess.Dispose(); $script:cliProcess = $null
}

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Verify-Notices([string]$Directory) {
    $native = @(Get-ChildItem -LiteralPath $Directory -Filter SDL3.dll -Recurse)
    $expectedNative = (Get-FileHash -LiteralPath (Join-Path $repository 'native/sdl3/win-x64/SDL3.dll')).Hash
    if ($native.Count -eq 0 -or @($native | Where-Object { (Get-FileHash -LiteralPath $_.FullName).Hash -ne $expectedNative }).Count -ne 0) {
        throw 'The generated/published game did not receive the pinned patched SDL native runtime.'
    }
    foreach ($component in @('sdl3', 'skiasharp', 'stbimagesharp')) {
        $source = Join-Path $repository "licenses/$component"
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
            $target = Join-Path $Directory "licenses/$component/$relative"
            if (-not (Test-Path -LiteralPath $target) -or
                (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
                throw "Missing or altered third-party notice '$target'."
            }
        }
    }
}

try {
    New-Item -ItemType Directory -Path $feed, $workspace, $cliHome | Out-Null
    Push-Location $repository
    try {
        foreach ($package in @('Obelisk.Graphics2D', 'Obelisk.Abstractions', 'Obelisk.Assets', 'Obelisk.Runtime',
            'Obelisk.Plugin', 'Obelisk.Plugins.Images', 'Obelisk.Platform.Sdl3', 'Obelisk.Vanilla', 'Obelisk.Cli')) {
            Invoke-Dotnet @('pack', "src/$package/$package.csproj", '-c', 'Release', '-o', $feed,
                '-m:1', '-p:MSBuildEnableWorkloadResolver=false')
        }
        if ($Distribution) {
            Invoke-Dotnet @('build', 'samples/SamplePlugin/SamplePlugin.csproj', '-c', 'Release', '-m:1', '-p:MSBuildEnableWorkloadResolver=false')
        }
        if ($Starter) {
            & pwsh -NoProfile -File (Join-Path $repository 'scripts/Build-Starter.ps1')
            if ($LASTEXITCODE -ne 0) { throw 'Starter ZIP generation failed.' }
        }
    } finally { Pop-Location }

    foreach ($key in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'MSBuildEnableWorkloadResolver')) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }
    $env:DOTNET_CLI_HOME = $cliHome
    $env:NUGET_PACKAGES = Join-Path $taskRoot 'packages'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:MSBuildEnableWorkloadResolver = 'false'
    $configuration = Join-Path $taskRoot 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    Set-Content -LiteralPath $configuration -Encoding utf8 -Value @"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
</configuration>
"@
    Push-Location $workspace
    try {
        # A real global installation in an isolated CLI home leaves the user's tools untouched.
        Invoke-Dotnet @('tool', 'install', '--global', 'Obelisk.Cli', '--version', '0.1.0', '--configfile', $configuration)
        $cli = Join-Path $cliHome '.dotnet/tools/obelisk.exe'
        if (-not (Test-Path -LiteralPath $cli)) { throw "Global command was not installed at '$cli'." }
        & $cli --help
        if ($LASTEXITCODE -ne 0) { throw 'Installed CLI help failed.' }
        & $cli info
        if ($LASTEXITCODE -ne 0) { throw 'Installed CLI info failed.' }
        & $cli new MyGame
        if ($LASTEXITCODE -ne 0) { throw 'Installed CLI project creation failed.' }
        $project = Join-Path $workspace 'MyGame'
        [xml]$projectXml = Get-Content -Raw -LiteralPath (Join-Path $project 'MyGame.csproj')
        $references = @($projectXml.Project.ItemGroup.PackageReference | Where-Object { $_ })
        if ($references.Count -ne 1 -or $references[0].Include -ne 'Obelisk.Vanilla' -or $references[0].Version -ne '[0.1.0]') {
            throw 'Generated project must reference exactly Obelisk.Vanilla [0.1.0].'
        }
        if ($projectXml.Project.ItemGroup.ProjectReference) { throw 'Generated project references engine source.' }
        & $cli new MyGame
        if ($LASTEXITCODE -ne 1) { throw 'A collision did not return exit code 1.' }
        & $cli new '../escape'
        if ($LASTEXITCODE -ne 2) { throw 'An invalid name did not return exit code 2.' }
        Invoke-Dotnet @('restore', (Join-Path $project 'MyGame.csproj'), '--configfile', $configuration)
        Invoke-Dotnet @('build', (Join-Path $project 'MyGame.csproj'), '-c', 'Release', '--no-restore')
        Verify-Notices (Join-Path $project 'bin/Release/net10.0')
        # Legal names that also name a generated type/framework namespace must compile.
        foreach ($edgeName in @('Program', 'Game', 'System')) {
            & $cli new $edgeName
            if ($LASTEXITCODE -ne 0) { throw "Could not generate '$edgeName'." }
            $edgeProject = Join-Path $workspace "$edgeName/$edgeName.csproj"
            Invoke-Dotnet @('restore', $edgeProject, '--configfile', $configuration)
            Invoke-Dotnet @('build', $edgeProject, '-c', 'Release', '--no-restore')
        }
        if (-not (Test-Path -LiteralPath (Join-Path $project 'bin/Release/net10.0/assets/README.txt'))) { throw 'Asset content was not copied.' }
        if (-not (Test-Path -LiteralPath (Join-Path $project 'bin/Release/net10.0/plugins/README.txt'))) { throw 'Plugin content was not copied.' }

        $gameProcess = Start-Process -FilePath (Join-Path $project 'bin/Release/net10.0/MyGame.exe') -WorkingDirectory $project `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRoot 'game.stdout.txt') `
            -RedirectStandardError (Join-Path $taskRoot 'game.stderr.txt')
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (-not $gameProcess.HasExited -and $gameProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
            $gameProcess.Refresh()
        }
        if ($gameProcess.HasExited -or $gameProcess.MainWindowHandle -eq 0) { throw 'Generated game did not open its SDL window.' }
        Start-Sleep -Milliseconds 500
        if (-not $gameProcess.CloseMainWindow()) { throw 'Could not send normal window-close request to generated game.' }
        if (-not $gameProcess.WaitForExit(10000) -or $gameProcess.ExitCode -ne 0) { throw 'Generated game did not exit normally.' }
        if ((Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'game.stderr.txt'))) { throw 'Generated game reported errors.' }
        Write-Output 'DAY 9 GATE PASSED: isolated global CLI -> new -> package-only restore -> build -> native window -> normal exit.'
        if ($DevelopmentLoop -or $Distribution) {
            $probe = Join-Path $taskRoot 'starts.jsonl'
            $probeLiteral = $probe.Replace('"', '""')
            # Instrument only the external generated game; exercise the installed CLI unchanged.
            Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Encoding utf8 -Value @"
using Obelisk.Abstractions;
using Obelisk.Vanilla;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        File.AppendAllText(@"$probeLiteral", System.Text.Json.JsonSerializer.Serialize(new { pid = Environment.ProcessId, args }) + "\n");
        return ObeliskApp.Run(new MyGame.Game(), new AppOptions { Title = "Day 10 Gate", Width = 960, Height = 540 });
    }
}
"@
            Start-Cli @('run', '--', 'two words', 'quote"value', '$literal')
            Wait-Until { @(Read-Starts).Count -eq 1 -or $cliProcess.HasExited } 'CLI run did not start a game.'
            if ($cliProcess.HasExited) { throw "CLI run failed: $($cliErrors.GetAwaiter().GetResult())" }
            $run = @(Read-Starts)[0]
            if (($run.args | ConvertTo-Json -Compress) -ne ('two words', 'quote"value', '$literal' | ConvertTo-Json -Compress)) { throw 'Game arguments were changed.' }
            $runningGame = Get-Process -Id $run.pid
            try {
                Wait-Until { $runningGame.Refresh(); $runningGame.MainWindowHandle -ne 0 } 'CLI run game has no native window.'
                if (-not $runningGame.CloseMainWindow()) { throw 'Could not close CLI run game.' }
            } finally { $runningGame.Dispose() }
            if (-not $cliProcess.WaitForExit(10000) -or $cliProcess.ExitCode -ne 0) { throw 'CLI run did not return the game exit code.' }
            $cliProcess.Dispose(); $cliProcess = $null
            Remove-Item -LiteralPath $probe

            Start-Cli @('dev')
            Wait-Until { @(Read-Starts).Count -eq 1 -or $cliProcess.HasExited } 'CLI dev did not start a game.'
            if ($cliProcess.HasExited) { throw "CLI dev failed: $($cliErrors.GetAwaiter().GetResult())" }
            $firstId = @(Read-Starts)[0].pid
            $gameSource = Join-Path $project 'Game.cs'
            $original = Get-Content -Raw -LiteralPath $gameSource
            foreach ($change in 1..5) {
                Set-Content -LiteralPath $gameSource -Encoding utf8 -Value ($original + "`n// debounce probe $change")
                Start-Sleep -Milliseconds 20
            }
            Wait-Until { @(Read-Starts).Count -ge 2 -or $cliProcess.HasExited } 'File changes did not restart the game.'
            if ($cliProcess.HasExited) { throw "CLI dev exited: $($cliErrors.GetAwaiter().GetResult())" }
            Start-Sleep -Milliseconds 1200
            $starts = @(Read-Starts)
            if ($starts.Count -ne 2 -or (Test-Alive $firstId)) { throw 'Debounced restart duplicated or retained the previous game.' }
            $currentId = $starts[1].pid
            if (-not (Test-Alive $currentId)) { throw 'Restarted game did not remain running.' }
            # Killing only the CLI proves OS job ownership also cleans descendants on forced exit.
            $cliProcess.Kill(); $cliProcess.WaitForExit()
            Wait-Until { -not (Test-Alive $currentId) } 'CLI exit left an orphan game.'
            $output = $cliOutput.GetAwaiter().GetResult()
            foreach ($match in [regex]::Matches($output, 'Game started \(process (\d+)\)')) {
                if (Test-Alive ([int]$match.Groups[1].Value)) { throw 'CLI exit left an orphan SDK process.' }
            }
            $cliProcess.Dispose(); $cliProcess = $null
            Write-Output 'DAY 10 GATE PASSED: installed run + exact arguments + dev burst -> one restart + previous game stopped + forced CLI exit cleans SDK/game descendants.'
        }
        if ($Distribution) {
            Push-Location $project
            try {
                $pluginPackage = Join-Path $repository 'samples/SamplePlugin/bin/Release/net10.0'
                $linkedPackage = Join-Path $taskRoot 'linked package'
                $linkTarget = Join-Path $taskRoot 'preserved link target'
                Copy-Item -LiteralPath $pluginPackage -Destination $linkedPackage -Recurse
                New-Item -ItemType Directory -Path $linkTarget | Out-Null
                $sentinel = Join-Path $linkTarget 'keep.txt'
                Set-Content -LiteralPath $sentinel -Value 'preserve outside package'
                $junction = Join-Path $linkedPackage 'escape'
                New-Item -ItemType Junction -Path $junction -Target $linkTarget | Out-Null
                try {
                    & $cli add $linkedPackage
                    if ($LASTEXITCODE -ne 1 -or -not (Test-Path -LiteralPath $sentinel)) { throw 'Linked Plugin package was accepted or its target was modified.' }
                } finally { Remove-Item -LiteralPath $junction -Force }
                & $cli add $pluginPackage
                if ($LASTEXITCODE -ne 0) { throw 'CLI Plugin installation failed.' }
                $listing = & $cli list
                if ($LASTEXITCODE -ne 0 -or "$listing" -notmatch 'sample 0\.1\.0') { throw 'Installed Plugin was not listed.' }
                & $cli add $pluginPackage
                if ($LASTEXITCODE -ne 1) { throw 'Duplicate Plugin installation was accepted.' }
                $asset = Join-Path $project 'assets/distribution.txt'
                Set-Content -LiteralPath $asset -Encoding utf8 -Value 'day11-project-asset'
                $pluginProbe = Join-Path $taskRoot 'plugin-loaded.txt'
                $probeLiteral = $pluginProbe.Replace('"', '""')
                $gameSource = Join-Path $project 'Game.cs'
                $code = Get-Content -Raw -LiteralPath $gameSource
                $initialize = @"
public void Initialize(InitializeContext context)
    {
        var accent = context.Assets.Load<string>("plugin://sample/accent.txt");
        var asset = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "distribution.txt"));
        File.WriteAllText(@"$probeLiteral", accent + "|" + asset);
    }
"@
                if (-not $code.Contains('public void Initialize(InitializeContext context) { }')) { throw 'Unexpected generated Game source.' }
                Set-Content -LiteralPath $gameSource -Encoding utf8 -Value ($code.Replace('public void Initialize(InitializeContext context) { }', $initialize))
                & $cli build
                if ($LASTEXITCODE -ne 0) { throw 'CLI self-contained build failed.' }
                $published = Join-Path $project 'artifacts/build/win-x64'
                $deployed = Join-Path $taskRoot 'relocated deployed game'
                Copy-Item -LiteralPath $published -Destination $deployed -Recurse
                foreach ($file in @('MyGame.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
                    'assets/distribution.txt', 'plugins/sample/obelisk.plugin.json', 'plugins/sample/SamplePlugin.dll', 'plugins/sample/assets/accent.txt')) {
                    if (-not (Test-Path -LiteralPath (Join-Path $deployed $file))) { throw "Distribution lacks '$file'." }
                }
                foreach ($folder in @('assets', 'shaders', 'native')) {
                    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $pluginPackage $folder) -File -Recurse) {
                        $relative = [IO.Path]::GetRelativePath($pluginPackage, $file.FullName)
                        $copy = Join-Path $deployed "plugins/sample/$relative"
                        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) { throw "Plugin file changed: '$relative'." }
                    }
                }
                if (-not (Get-ChildItem -LiteralPath $deployed -Filter SDL3.dll -Recurse)) { throw 'SDL3 native dependency was not published.' }
                $runtime = Get-Content -Raw -LiteralPath (Join-Path $deployed 'MyGame.runtimeconfig.json') | ConvertFrom-Json
                Verify-Notices $deployed
                $runtimeVersion = ($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
                foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
                    $original = Join-Path $env:NUGET_PACKAGES "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/$notice"
                    $copied = Join-Path $deployed "licenses/dotnet/$notice"
                    if (-not (Test-Path -LiteralPath $copied) -or
                        (Get-FileHash -LiteralPath $original).Hash -ne (Get-FileHash -LiteralPath $copied).Hash) {
                        throw "Missing or altered .NET runtime notice '$copied'."
                    }
                }
                if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or -not $runtime.runtimeOptions.includedFrameworks) { throw 'Distribution requires a shared .NET runtime.' }
                $noDotnet = Join-Path $taskRoot 'no-dotnet'
                New-Item -ItemType Directory -Path $noDotnet | Out-Null
                $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $deployed 'MyGame.exe'))
                $start.WorkingDirectory = $deployed
                $start.UseShellExecute = $false
                $start.CreateNoWindow = $true
                $start.RedirectStandardOutput = $true
                $start.RedirectStandardError = $true
                $start.Environment['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
                $start.Environment['DOTNET_ROOT'] = $noDotnet
                $start.Environment['DOTNET_ROOT_X64'] = $noDotnet
                $start.Environment['DOTNET_ROOT(x86)'] = $noDotnet
                $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
                $gameProcess.Dispose()
                $gameProcess = [Diagnostics.Process]::Start($start)
                $deploymentOutput = $gameProcess.StandardOutput.ReadToEndAsync()
                $deploymentErrors = $gameProcess.StandardError.ReadToEndAsync()
                Wait-Until { $gameProcess.Refresh(); $gameProcess.HasExited -or ($gameProcess.MainWindowHandle -ne 0 -and (Test-Path -LiteralPath $pluginProbe)) } 'Relocated distribution did not open its native window and load Plugin assets.'
                if ($gameProcess.HasExited) { throw "Deployed game failed: $($deploymentErrors.GetAwaiter().GetResult())" }
                if ((Get-Content -Raw -LiteralPath $pluginProbe) -notmatch 'day11-project-asset') { throw 'Deployed game did not use copied project assets.' }
                $coreclr = @($gameProcess.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' })
                if ($coreclr.Count -ne 1 -or [IO.Path]::GetDirectoryName($coreclr[0].FileName) -ne $deployed) { throw 'Game did not load its private deployed runtime.' }
                if (-not $gameProcess.CloseMainWindow() -or -not $gameProcess.WaitForExit(10000) -or $gameProcess.ExitCode -ne 0) { throw 'Deployed game did not exit normally.' }
                if ($deploymentErrors.GetAwaiter().GetResult()) { throw 'Deployed game reported errors.' }

                & $cli remove sample
                if ($LASTEXITCODE -ne 0) { throw 'CLI Plugin removal failed.' }
                $listing = & $cli list
                if ($LASTEXITCODE -ne 0 -or "$listing" -notmatch 'No local Plugins') { throw 'Removed Plugin is still listed.' }
                Set-Content -LiteralPath (Join-Path $published 'stale-file.txt') -Value 'old output'
                & $cli build
                if ($LASTEXITCODE -ne 0) { throw 'Repeat CLI build failed.' }
                if ((Test-Path -LiteralPath (Join-Path $published 'stale-file.txt')) -or
                    (Test-Path -LiteralPath (Join-Path $published 'plugins/sample'))) { throw 'Rebuild retained removed Plugin or stale output.' }
                Write-Output 'DAY 11 GATE PASSED: independent new -> run -> add/list -> Windows x64 self-contained build -> relocate -> native game using local runtime + Plugin/project assets without SDK PATH -> remove/list -> clean rebuild.'
            } finally { Pop-Location }
        }
        if ($Starter) {
            $zip = Join-Path $repository 'artifacts/starter/Obelisk-Starter-v0.1.0.zip'
            if (-not (Test-Path -LiteralPath $zip)) { throw 'Starter ZIP was not created.' }
            & $cli new ObeliskStarter
            if ($LASTEXITCODE -ne 0) { throw 'Packaged CLI could not generate the matching Starter project.' }
            $cliTemplate = Join-Path $workspace 'ObeliskStarter'
            $extracted = Join-Path $taskRoot 'extracted'
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $extracted)
            $zipTemplate = Join-Path $extracted 'ObeliskStarter'
            Verify-Template $cliTemplate $repository
            Verify-Template $zipTemplate $repository
            $cliFiles = @(Get-ChildItem -LiteralPath $cliTemplate -File -Recurse -Force | ForEach-Object {
                [IO.Path]::GetRelativePath($cliTemplate, $_.FullName).Replace('\', '/')
            } | Sort-Object)
            $zipFiles = @(Get-ChildItem -LiteralPath $zipTemplate -File -Recurse -Force | ForEach-Object {
                [IO.Path]::GetRelativePath($zipTemplate, $_.FullName).Replace('\', '/')
            } | Sort-Object)
            if (($cliFiles | ConvertTo-Json -Compress) -ne ($zipFiles | ConvertTo-Json -Compress)) { throw 'CLI and ZIP template file sets differ.' }
            foreach ($relative in $cliFiles) {
                $left = Join-Path $cliTemplate $relative
                $right = Join-Path $zipTemplate $relative
                if ((Get-FileHash -LiteralPath $left).Hash -ne (Get-FileHash -LiteralPath $right).Hash) {
                    throw "CLI and ZIP template differ: '$relative'."
                }
            }
            $copyRoot = Join-Path $taskRoot 'copied workspace'
            $moveRoot = Join-Path $taskRoot 'moved workspace'
            New-Item -ItemType Directory -Path $copyRoot, $moveRoot | Out-Null
            Copy-Item -LiteralPath $zipTemplate -Destination $copyRoot -Recurse
            $copiedTemplate = Join-Path $copyRoot 'ObeliskStarter'
            $movedTemplate = Join-Path $moveRoot 'ObeliskStarter'
            $allowed = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($taskRoot)) + [IO.Path]::DirectorySeparatorChar
            foreach ($path in @($copiedTemplate, $movedTemplate)) {
                if (-not [IO.Path]::GetFullPath($path).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Refusing to move Starter outside the verification workspace.'
                }
            }
            Move-Item -LiteralPath $copiedTemplate -Destination $movedTemplate
            Verify-Template $movedTemplate $repository
            Verify-TemplateRun $movedTemplate (Join-Path $taskRoot 'zip-game.pid')
            Verify-TemplateRun $cliTemplate (Join-Path $taskRoot 'cli-game.pid') -UseCli
            Write-Output 'DAY 12 LOCAL GATE PASSED: packaged CLI and Starter ZIP render byte-identical projects, copied/moved ZIP project and CLI project restore/build/run native SDL windows from isolated local packages; no engine checkout references.'
        }
    } finally { Pop-Location }
} finally {
    if ($null -ne $cliProcess) {
        if (-not $cliProcess.HasExited) { $cliProcess.Kill($true); $cliProcess.WaitForExit() }
        $cliProcess.Dispose()
    }
    if ($null -ne $gameProcess) {
        if (-not $gameProcess.HasExited) { $gameProcess.Kill($true); $gameProcess.WaitForExit() }
        $gameProcess.Dispose()
    }
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
    if ($KeepWorkspace) { Write-Output "Verification workspace: $taskRoot" }
    elseif (Test-Path -LiteralPath $taskRoot) {
        $resolvedRoot = [IO.Path]::GetFullPath($taskRoot)
        $expectedPrefix = [IO.Path]::TrimEndingDirectorySeparator($tempParent) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedRoot) -notmatch '^obelisk-day9-[a-f0-9]{32}$') { throw 'Refusing cleanup outside the verification temp directory.' }
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}
