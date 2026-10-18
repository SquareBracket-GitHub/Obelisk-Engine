param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Directory)
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-ReleaseCandidate.ps1') -Directory $root
if ($LASTEXITCODE -ne 0) { throw 'Invalid candidate.' }
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $tempParent ('obelisk-standalone-' + [guid]::NewGuid().ToString('N'))
$process = $null
function Invoke-Standalone([string[]]$Arguments, [int]$Expected = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $work 'cli/Obelisk.Cli.exe'))
    $start.WorkingDirectory = $work
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
    $start.Environment['DOTNET_ROOT'] = Join-Path $work 'no-dotnet'
    $start.Environment['DOTNET_ROOT_X64'] = Join-Path $work 'no-dotnet'
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $script:process = [Diagnostics.Process]::Start($start)
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { throw 'Standalone CLI timed out.' }
    $text = $output.GetAwaiter().GetResult()
    $errorText = $errors.GetAwaiter().GetResult()
    if ($process.ExitCode -ne $Expected -or ($Expected -eq 0 -and $errorText)) {
        throw "Standalone command failed: $($Arguments -join ' '), exit $($process.ExitCode), $errorText"
    }
    $process.Dispose(); $script:process = $null
    return $text
}
try {
    New-Item -ItemType Directory -Path $work | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $root 'Obelisk-Cli-v0.1.0-win-x64.zip'), (Join-Path $work 'cli'))
    if ((Invoke-Standalone @('--version')).Trim() -ne '0.1.0') { throw 'Standalone version differs.' }
    if ((Invoke-Standalone @('--help')) -notmatch 'new') { throw 'Standalone help is incomplete.' }
    Invoke-Standalone @('info') | Write-Output
    Invoke-Standalone @('new', 'StandaloneGame') | Write-Output
    [xml]$project = Get-Content -Raw (Join-Path $work 'StandaloneGame/StandaloneGame.csproj')
    if ($project.Project.ItemGroup.PackageReference.Version -ne '[0.1.0]') { throw 'Standalone template version differs.' }
    Write-Output 'STANDALONE CLI PASSED: extracted archive help/info/version/new with SDK absent from PATH and invalid DOTNET_ROOT. Separate SDK-free machine certification is still required.'
} finally {
    if ($process) {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    $resolved = [IO.Path]::GetFullPath($work)
    $prefix = [IO.Path]::TrimEndingDirectorySeparator($tempParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^obelisk-standalone-[a-f0-9]{32}$') { throw 'Refusing cleanup outside owned workspace.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
