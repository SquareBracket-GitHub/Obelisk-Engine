param(
    [ValidateRange(30, 1440)][double]$Minutes = 30,
    [switch]$Quick,
    [switch]$SmokeOnly,
    [switch]$AnalyzeOnly,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'The Day 13 native stability Gate requires Windows.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repository 'artifacts/stability' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$owned = $null

function Invoke-Dotnet([string[]]$Arguments, [string]$Log) {
    & dotnet @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $Log -Tail 30; throw "dotnet $($Arguments -join ' ') failed." }
}

function Start-Owned([string]$Executable, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = Split-Path $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $script:owned = [Diagnostics.Process]::Start($start)
    $script:stdout = $owned.StandardOutput.ReadToEndAsync()
    $script:stderr = $owned.StandardError.ReadToEndAsync()
}

function Finish-Owned([string]$Log) {
    $id = $owned.Id
    if (-not $owned.WaitForExit(15000)) { throw 'Owned game did not exit.' }
    Set-Content -LiteralPath $Log -Value ($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
    if ($owned.ExitCode -ne 0) { throw "Owned game failed ($($owned.ExitCode)); inspect '$Log'." }
    if (Get-CimInstance Win32_Process -Filter "ParentProcessId=$id") { throw "Child process survived owned game $id." }
    $owned.Dispose(); $script:owned = $null
}

try {
    Push-Location $repository
    try {
        if (-not $AnalyzeOnly) {
            foreach ($configuration in @('Debug', 'Release')) {
                if (-not $SmokeOnly) {
                    Invoke-Dotnet @('test', 'Obelisk.sln', '-c', $configuration, '--nologo', '-m:1', '-p:MSBuildEnableWorkloadResolver=false') (Join-Path $output "$configuration-tests.log")
                }
                foreach ($name in @('MovingRectangle', 'CanvasShowcase', 'Pong')) {
                    Invoke-Dotnet @('build', "samples/$name/$name.csproj", '-c', $configuration, '--nologo', '-m:1', '-p:MSBuildEnableWorkloadResolver=false') (Join-Path $output "$name-$configuration-build.log")
                    $executable = Join-Path $repository "samples/$name/bin/$configuration/net10.0/$name.exe"
                    Start-Owned $executable @()
                    $timeout = [DateTime]::UtcNow.AddSeconds(30)
                    do {
                        $owned.Refresh()
                        if ($owned.HasExited) { throw "Sample '$name' exited before opening its window." }
                        if ([DateTime]::UtcNow -gt $timeout) { throw "Sample '$name' did not open a native window." }
                        Start-Sleep -Milliseconds 100
                    } while ($owned.MainWindowHandle -eq 0)
                    Start-Sleep -Seconds 2
                    if (-not $owned.CloseMainWindow()) { throw "Could not close '$name' normally." }
                    Finish-Owned (Join-Path $output "$name-$configuration-run.log")
                    Write-Output "SAMPLE PASSED: $name $configuration native window -> normal exit -> no surviving child process."
                }
            }
            if ($SmokeOnly) { return }
            Invoke-Dotnet @('build', 'tests/Obelisk.Stability/Obelisk.Stability.csproj', '-c', 'Release', '--nologo', '-m:1', '-p:MSBuildEnableWorkloadResolver=false') (Join-Path $output 'runner-build.log')
            $arguments = @('--minutes', $Minutes.ToString([Globalization.CultureInfo]::InvariantCulture), '--output', $output)
            if ($Quick) { $arguments = @('--quick', '--minutes', '0.15', '--cycle-seconds', '2', '--output', $output) }
            Start-Owned (Join-Path $repository 'tests/Obelisk.Stability/bin/Release/net10.0/Obelisk.Stability.exe') $arguments
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $nextGpu = 0
            $gpuLog = Join-Path $output 'gpu-memory.jsonl'
            Set-Content -LiteralPath $gpuLog -Value ''
            while (-not $owned.HasExited) {
                if ($watch.Elapsed.TotalSeconds -ge $nextGpu) {
                    $nextGpu += 30
                    try {
                        $rows = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory | Where-Object { $_.Name -like "pid_$($owned.Id)_*" })
                        [pscustomobject]@{ Utc = [DateTime]::UtcNow.ToString('O'); ProcessId = $owned.Id; Available = $rows.Count -gt 0
                            Dedicated = ($rows | Measure-Object DedicatedUsage -Sum).Sum
                            Shared = ($rows | Measure-Object SharedUsage -Sum).Sum
                            Committed = ($rows | Measure-Object TotalCommitted -Sum).Sum
                        } | ConvertTo-Json -Compress | Add-Content -LiteralPath $gpuLog
                    } catch { Write-Warning "GPU process-memory counters unavailable: $_" }
                }
                if ($watch.Elapsed.TotalMinutes -gt $Minutes + 3) { throw 'Stability runner timed out.' }
                Start-Sleep -Seconds 1
            }
            Finish-Owned (Join-Path $output 'runner.log')
        }
    } finally { Pop-Location }

    $summaryFile = Get-Item -LiteralPath (Join-Path $output 'summary.json')
    $metricsFile = Get-Item -LiteralPath (Join-Path $output 'metrics.jsonl')
    if ($metricsFile.LastWriteTimeUtc -gt $summaryFile.LastWriteTimeUtc) { throw 'Metrics are newer than the summary; this run has not completed successfully.' }
    $summary = Get-Content -Raw -LiteralPath $summaryFile.FullName | ConvertFrom-Json
    if (-not $summary.Passed -or $summary.FinalGpuHandles -ne 0) { throw 'Runner did not pass its ownership checks.' }
    $cycles = @($summary.PostShutdown)
    $cpu = for ($i = 1; $i -lt $cycles.Count; $i++) {
        if ($i -ge 3) { ($cycles[$i].CpuSeconds - $cycles[$i-1].CpuSeconds) / ($cycles[$i].Seconds - $cycles[$i-1].Seconds) }
    }
    if ($cpu.Count -ge 6) {
        $cpuStart = ($cpu | Select-Object -First 3 | Measure-Object -Average).Average
        $cpuEnd = ($cpu | Select-Object -Last 3 | Measure-Object -Average).Average
        if ($cpuEnd - $cpuStart -gt 0.10) { throw 'Sustained CPU usage grew by more than 0.10 of one CPU core after warm-up.' }
    }
    $gpuPath = Join-Path $output 'gpu-memory.jsonl'
    $gpu = if (Test-Path -LiteralPath $gpuPath) { @(Get-Content -LiteralPath $gpuPath | Where-Object { $_.Trim() } | ForEach-Object { ConvertFrom-Json $_ }) } else { @() }
    $gpuSteady = @($gpu | Where-Object Available | Select-Object -Skip 6)
    if ($gpuSteady.Count -ge 12) {
        foreach ($field in @('Dedicated', 'Shared', 'Committed')) {
            $first = ($gpuSteady | Select-Object -First 6 | Measure-Object $field -Average).Average
            $last = ($gpuSteady | Select-Object -Last 6 | Measure-Object $field -Average).Average
            if ($last - $first -gt 32MB) { throw "GPU $field memory grew by more than 32 MiB after warm-up." }
        }
    }
    $full = -not $summary.Quick -and $summary.ElapsedSeconds -ge 1800 -and $summary.TrendChecked
    if ($full -and ($cpu.Count -lt 6 -or $gpuSteady.Count -lt 12)) {
        throw 'The full Gate requires CPU and Windows GPU process-memory measurements; missing counters cannot establish resource stability.'
    }
    [pscustomobject]@{ FullDuration = $full; ElapsedSeconds = $summary.ElapsedSeconds; Cycles = $summary.Cycles
        GpuCounters = $gpu.Count; CpuCoreStart = $cpuStart; CpuCoreEnd = $cpuEnd; FinalGpuHandles = $summary.FinalGpuHandles
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'gate.json')
    if (-not $summary.Quick -and -not $full) { throw 'The full Gate requires at least 30 real minutes and enough post-warm-up cycles.' }
    if ($full) { Write-Output "STABILITY GATE PASSED (at least 30 real minutes). Logs: $output" }
    else { Write-Output "STABILITY SMOKE PASSED; the full 30-minute Gate was not evaluated. Logs: $output" }
} finally {
    if ($null -ne $owned) {
        if (-not $owned.HasExited) { $owned.Kill($true); $owned.WaitForExit() }
        $owned.Dispose()
    }
}
