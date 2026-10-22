[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$InstallerSha256,
    [Parameter(Mandatory=$true)][string]$AppSha256,
    [string]$PreviousUninstallerSha256,
    [Parameter(Mandatory=$true)][string]$ResultPath
)
$ErrorActionPreference='Stop'
$report=[ordered]@{passed=$false;uninstallExitCode=$null;installExitCode=$null;installedPath='C:\Program Files\Rilmo Recorder';error=$null}
try {
    if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Local installation requires Windows elevation.'}
    if((Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash -ine $InstallerSha256){throw 'Installer hash mismatch.'}
    $target=Join-Path $env:ProgramW6432 'Rilmo Recorder'
    if([IO.Path]::GetFullPath($target) -ine $report.installedPath){throw 'Unexpected Program Files location.'}
    if(Get-Process -Name 'Rilmo Recorder' -ErrorAction SilentlyContinue){throw 'Exit Rilmo before installing. No running process was stopped.'}
    $key='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Rilmo.Recorder.Desktop'
    if(Test-Path -LiteralPath $key){
        if((Get-ItemProperty -LiteralPath $key).InstallLocation -ine $target){throw 'Existing installation location differs.'}
        $uninstaller=Join-Path $target 'Uninstall Rilmo.exe'
        if(-not $PreviousUninstallerSha256 -or (Get-FileHash -LiteralPath $uninstaller -Algorithm SHA256).Hash -ine $PreviousUninstallerSha256){throw 'Previous uninstaller identity differs.'}
        $process=Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -PassThru
        if(-not $process.WaitForExit(120000)){throw 'Uninstaller has not finished; no replacement was started.'}
        $report.uninstallExitCode=$process.ExitCode
        if($process.ExitCode -ne 0){throw "Uninstaller exit code $($process.ExitCode)."}
        $deadline=[DateTime]::UtcNow.AddSeconds(90)
        while((Test-Path -LiteralPath $key) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 250}
        if(Test-Path -LiteralPath $key){throw 'Uninstall did not remove its registration.'}
    }
    $process=Start-Process -FilePath $Installer -ArgumentList '/S' -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(180000)){throw 'Installer has not finished; inspect setup before retrying.'}
    $report.installExitCode=$process.ExitCode
    if($process.ExitCode -ne 0){throw "Installer exit code $($process.ExitCode)."}
    $exe=Join-Path $target 'Rilmo Recorder.exe'
    if((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ine $AppSha256){throw 'Installed executable hash mismatch.'}
    $report.version=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    if((Get-ItemProperty -LiteralPath $key).InstallLocation -ine $target){throw 'Installation registration missing.'}
    $report.passed=$true
} catch {$report.error=$_.Exception.Message}
$report | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
if(-not $report.passed){exit 1}
