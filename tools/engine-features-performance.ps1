param([ValidateRange(10,120)][int]$Seconds=60)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$output=Join-Path $workspace 'artifacts\engine-features'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$stdout=Join-Path $output 'performance-run.txt'
$stderr=Join-Path $output 'performance-errors.txt'
$script=Join-Path $PSScriptRoot 'engine-smoke.ps1'
$arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$script+'" -Mode Window -Seconds '+$Seconds+' -Runs 1 -SkipPause'
$shellPath=(Get-Process -Id $PID).Path
$child=Start-Process -FilePath $shellPath -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$samples=[Collections.Generic.List[object]]::new()
$appPid=0; $previousCpu=$null; $previousTime=$null
try {
    while(-not $child.HasExited) {
        $processes=@(Get-CimInstance Win32_Process -Filter "Name='EngineSmoke.exe' OR Name='obs-ffmpeg-mux.exe'")
        $app=$processes | Where-Object { $_.ParentProcessId -eq $child.Id -and $_.Name -eq 'EngineSmoke.exe' -and $_.CommandLine -notlike '*--window-target*' } | Select-Object -First 1
        if($app) {
            $appPid=$app.ProcessId
            $owned=@($processes | Where-Object { $_.ProcessId -eq $appPid -or ($_.ParentProcessId -eq $appPid -and $_.Name -eq 'obs-ffmpeg-mux.exe') })
            $cpu=0.0; $ram=0L
            foreach($entry in $owned) { try { $p=Get-Process -Id $entry.ProcessId -ErrorAction Stop; $cpu+=$p.TotalProcessorTime.TotalSeconds; $ram+=$p.WorkingSet64 } catch {} }
            $now=[Diagnostics.Stopwatch]::GetTimestamp()/[double][Diagnostics.Stopwatch]::Frequency
            if($null -ne $previousCpu -and $cpu -ge $previousCpu) { $percent=100*($cpu-$previousCpu)/($now-$previousTime)/[Environment]::ProcessorCount; $samples.Add([pscustomobject]@{ElapsedSeconds=$now; CpuPercent=$percent; WorkingSetMiB=$ram/1MB; OwnedProcesses=$owned.Count}) }
            $previousCpu=$cpu; $previousTime=$now
        }
        Start-Sleep -Seconds 2
        $child.Refresh()
    }
    $child.WaitForExit()
    Get-Content -LiteralPath $stdout
    if($child.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw 'Owned-window performance capture failed.' }
    # Exclude startup and teardown; each remaining sample aggregates recorder + its mux child.
    $steady=@($samples | Select-Object -Skip 3 | Select-Object -SkipLast 1)
    if($steady.Count -lt 4) { throw 'Too few steady capture samples.' }
    $summary=[pscustomobject]@{RequestedSeconds=$Seconds; Samples=$steady.Count; RecorderProcessId=$appPid; CpuMeanPercent=($steady.CpuPercent|Measure-Object -Average).Average; CpuMaximumPercent=($steady.CpuPercent|Measure-Object -Maximum).Maximum; WorkingSetMeanMiB=($steady.WorkingSetMiB|Measure-Object -Average).Average; WorkingSetMaximumMiB=($steady.WorkingSetMiB|Measure-Object -Maximum).Maximum; Scope='Console engine test host plus native mux child; excludes WPF front end, animated target, DWM, drivers and unrelated processes'; Caveat='Existing user recording and desktop workloads were left running; this is a smoke measurement, not an OBS comparison.'}
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'performance-summary.json') -Encoding UTF8
    $samples | Export-Csv -LiteralPath (Join-Path $output 'performance-samples.csv') -NoTypeInformation
    $summary | Format-List
} finally { $child.Dispose() }
