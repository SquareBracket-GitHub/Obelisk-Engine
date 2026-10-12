param([switch]$KeepWorkspace)

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

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
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
    } finally { Pop-Location }
} finally {
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
