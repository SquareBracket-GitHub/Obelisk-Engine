# Reuse Verify-PackagedCli.ps1's isolated feed, cache, tool and owned-process cleanup.
function Read-GuideBlock([string]$Text, [string]$Language) {
    $match = [regex]::Match($Text, '(?ms)^```' + $Language + '\r?\n(.*?)^```')
    if (-not $match.Success) { throw "Missing complete $Language tutorial block." }
    return $match.Groups[1].Value
}

function Close-DocumentedGame([string]$Executable, [switch]$ViaCli) {
    if ($ViaCli) {
        Start-Cli @('run')
        Wait-Until {
            $script:documentedGame = @(Get-CimInstance Win32_Process | Where-Object {
                $_.ExecutablePath -and $_.ExecutablePath.Equals($Executable, [StringComparison]::OrdinalIgnoreCase)
            })
            $documentedGame.Count -eq 1 -or $cliProcess.HasExited
        } 'Documented CLI run did not start the generated game.'
        if ($cliProcess.HasExited) { throw "Documented run failed: $($cliErrors.GetAwaiter().GetResult())" }
        $script:gameProcess = Get-Process -Id ([int]$documentedGame[0].ProcessId)
    } else {
        $start = [Diagnostics.ProcessStartInfo]::new($Executable)
        $start.WorkingDirectory = Split-Path $Executable
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $script:gameProcess = [Diagnostics.Process]::Start($start)
        $output = $gameProcess.StandardOutput.ReadToEndAsync()
        $errors = $gameProcess.StandardError.ReadToEndAsync()
    }
    $id = $gameProcess.Id
    Wait-Until { $gameProcess.Refresh(); $gameProcess.HasExited -or $gameProcess.MainWindowHandle -ne 0 } 'Documented game did not open its SDL window.'
    Start-Sleep -Milliseconds 500
    $gameProcess.Refresh()
    if ($gameProcess.HasExited -or -not $gameProcess.CloseMainWindow() -or -not $gameProcess.WaitForExit(10000)) { throw 'Documented game did not close normally.' }
    if ($ViaCli) {
        if (-not $cliProcess.WaitForExit(10000) -or $cliProcess.ExitCode -ne 0) { throw 'Documented run command failed.' }
        $text = $cliOutput.GetAwaiter().GetResult(); $errorText = $cliErrors.GetAwaiter().GetResult()
        $cliProcess.Dispose(); $script:cliProcess = $null
    } else {
        if ($gameProcess.ExitCode -ne 0) { throw 'Documented distribution failed.' }
        $text = $output.GetAwaiter().GetResult(); $errorText = $errors.GetAwaiter().GetResult()
    }
    if ($errorText -or -not $text.Contains('tutorial Plugin registered.')) { throw "Exact documented Plugin did not register: $errorText" }
    if (Get-CimInstance Win32_Process -Filter "ParentProcessId=$id") { throw 'Documented game left a child process.' }
    $gameProcess.Dispose(); $script:gameProcess = $null
}

function Verify-DocumentationWorkflow {
    foreach ($file in @('README.md','docs/getting-started.md','docs/plugin-authoring.md','docs/cli-reference.md','docs/resources-and-support.md','docs/api-compatibility.md')) {
        $document = Join-Path $repository $file
        foreach ($link in [regex]::Matches((Get-Content -Raw -LiteralPath $document), '\[[^\]]+\]\(([^)]+)\)')) {
            $target = $link.Groups[1].Value
            if ($target -match '^[a-z]+:') { continue }
            $parts = $target.Split('#', 2)
            $path = if ($parts[0]) { [IO.Path]::GetFullPath((Join-Path (Split-Path $document) $parts[0])) } else { $document }
            if (-not (Test-Path -LiteralPath $path)) { throw "Broken documentation link: $file -> $target" }
            if ($parts.Count -eq 2) {
                $anchors = @(Get-Content -LiteralPath $path | Where-Object { $_ -match '^#+ ' } | ForEach-Object {
                    (($_ -replace '^#+ ', '').ToLowerInvariant() -replace '[^a-z0-9 _-]', '') -replace ' ', '-'
                })
                if ($parts[1] -notin $anchors) { throw "Broken documentation anchor: $file -> $target" }
            }
        }
    }
    $guide = Get-Content -Raw -LiteralPath (Join-Path $repository 'docs/plugin-authoring.md')
    $tutorial = Join-Path $taskRoot 'documentation-only'
    New-Item -ItemType Directory -Path $tutorial | Out-Null
    Push-Location $tutorial
    try {
        & $cli new MyGame
        if ($LASTEXITCODE) { throw 'Documented new command failed.' }
        $script:project = Join-Path $tutorial 'MyGame'
        $plugin = Join-Path $tutorial 'TutorialPlugin'
        New-Item -ItemType Directory -Path $plugin | Out-Null
        # Compile the guide examples unchanged, without repository SamplePlugin fixtures.
        Set-Content -LiteralPath (Join-Path $plugin 'TutorialPlugin.csproj') -Value (Read-GuideBlock $guide 'xml') -Encoding utf8
        Set-Content -LiteralPath (Join-Path $plugin 'obelisk.plugin.json') -Value (Read-GuideBlock $guide 'json') -Encoding utf8
        Set-Content -LiteralPath (Join-Path $plugin 'EntryPoint.cs') -Value (Read-GuideBlock $guide 'csharp') -Encoding utf8
        Copy-Item -LiteralPath $configuration -Destination (Join-Path $tutorial 'NuGet.Config')
        Invoke-Dotnet @('restore', (Join-Path $plugin 'TutorialPlugin.csproj'))
        Invoke-Dotnet @('build', (Join-Path $plugin 'TutorialPlugin.csproj'), '-c', 'Release', '--no-restore')
        Push-Location $project
        try {
            # Execute the documented per-game source setup, preserving nuget.org.
            if ($PublicFeed) {
                Copy-Item -LiteralPath $configuration -Destination 'NuGet.Config'
            } else {
                Invoke-Dotnet @('new', 'nugetconfig')
                Invoke-Dotnet @('nuget', 'add', 'source', $feed, '--name', 'obelisk-local', '--configfile', 'NuGet.Config')
            }
            Invoke-Dotnet @('restore', '--configfile', 'NuGet.Config')
            Invoke-Dotnet @('build', '--no-restore')
            $source = Join-Path $project 'Game.cs'
            $code = Get-Content -Raw -LiteralPath $source
            if (-not $code.Contains('direction * 200') -or -not $code.Contains('new ColorRgba(0.15f, 0.8f, 0.45f, 1)')) { throw 'Getting Started edits no longer match the generated template.' }
            Set-Content -LiteralPath $source -Value ($code.Replace('direction * 200', 'direction * 300').Replace('new ColorRgba(0.15f, 0.8f, 0.45f, 1)', 'ColorRgba.Orange')) -Encoding utf8
            & $cli add ../TutorialPlugin/bin/Release/net10.0
            if ($LASTEXITCODE) { throw 'Documented add command failed.' }
            $listed = & $cli list
            if ($LASTEXITCODE -or -not ($listed -match 'tutorial.*0\.1\.0')) { throw 'Documented list command failed.' }
            Close-DocumentedGame (Join-Path $project 'bin/Debug/net10.0/MyGame.exe') -ViaCli
            & $cli build
            if ($LASTEXITCODE) { throw 'Documented build command failed.' }
            $relocated = Join-Path $tutorial 'relocated-game'
            Copy-Item -LiteralPath (Join-Path $project 'artifacts/build/win-x64') -Destination $relocated -Recurse
            Verify-Notices $relocated
            Close-DocumentedGame (Join-Path $relocated 'MyGame.exe')
            & $cli remove tutorial
            if ($LASTEXITCODE) { throw 'Documented remove command failed.' }
        } finally { Pop-Location }
        [xml]$xml = Get-Content -Raw -LiteralPath (Join-Path $project 'MyGame.csproj')
        [xml]$pluginXml = Get-Content -Raw -LiteralPath (Join-Path $plugin 'TutorialPlugin.csproj')
        if ($xml.Project.ItemGroup.ProjectReference -or $pluginXml.Project.ItemGroup.ProjectReference) { throw 'Guide examples reference engine source.' }
        foreach ($name in @('Obelisk.Abstractions','Obelisk.Graphics2D','Obelisk.Assets','Obelisk.Plugin','Obelisk.Plugins.Images','Obelisk.Runtime','Obelisk.Platform.Sdl3','Obelisk.Vanilla')) {
            $doc = Join-Path $env:NUGET_PACKAGES "$($name.ToLowerInvariant())/0.1.0/lib/net10.0/$name.xml"
            [xml]$api = Get-Content -Raw -LiteralPath $doc
            if ($api.doc.assembly.name -ne $name -or @($api.doc.members.member).Count -eq 0) { throw "Missing packaged XML documentation for $name." }
        }
        Write-Output 'DAY 14 GATE PASSED: documented game edits and exact Plugin guide snippets -> package-only restore/build -> CLI run -> Plugin registration -> relocated self-contained native game -> removal; all eight packaged XML references present.'
    } finally { Pop-Location }
}
