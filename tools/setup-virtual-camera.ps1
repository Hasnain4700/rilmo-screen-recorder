param(
    [ValidateSet('Review','Install','Uninstall')][string]$Action = 'Review',
    [switch]$AcknowledgeSystemDeviceChange,
    [string]$AppDirectory
)
# Windows PowerShell 5.1 compatible. This script never requests elevation itself.
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { throw 'Run camera setup in 64-bit Windows PowerShell. A 32-bit host would resolve the camera registration tools incorrectly.' }
if (-not $AppDirectory) { $AppDirectory = Split-Path $PSScriptRoot -Parent }
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$classId = '{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}'
$deviceName = 'Rilmo Virtual Camera'
$installRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Rilmo\Virtual Camera'
$installRoot = [IO.Path]::GetFullPath($installRoot)
$receiptPath = Join-Path $installRoot 'installation.json'
$source64 = Join-Path $AppDirectory 'rilmo-virtual-camera64.dll'
$source32 = Join-Path $AppDirectory 'virtual-camera\rilmo-virtual-camera32.dll'
$provenancePath = Join-Path $AppDirectory 'virtual-camera\source-provenance.json'
$registrations = @(
    @{ Bits='64'; View=[Microsoft.Win32.RegistryView]::Registry64; Regsvr=(Join-Path $env:WINDIR 'System32\regsvr32.exe'); Source=$source64 },
    @{ Bits='32'; View=[Microsoft.Win32.RegistryView]::Registry32; Regsvr=(Join-Path $env:WINDIR 'SysWOW64\regsvr32.exe'); Source=$source32 }
)
function Assert-NoReparse([string]$Path) {
    $cursor = $Path
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Reparse points are not allowed in the camera setup path: '+$cursor) }
        }
        $parent = Split-Path $cursor -Parent
        if ($parent -eq $cursor) { break }; $cursor = $parent
    }
}
function Get-RegisteredPath($View) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$View)
    try { $key = $base.OpenSubKey('SOFTWARE\Classes\CLSID\'+$classId+'\InprocServer32'); if ($key) { try { return [string]$key.GetValue('') } finally { $key.Dispose() } } }
    finally { $base.Dispose() }
    return $null
}
function Invoke-Registration([string]$Executable,[string]$Dll,[bool]$Remove) {
    $arguments = if ($Remove) { '/s /u "'+$Dll+'"' } else { '/s "'+$Dll+'"' }
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw ('Camera registration failed (exit '+$process.ExitCode+'). Close apps using this camera and retry.') }
}
function Read-ValidatedReceiptFiles {
    if (-not (Test-Path -LiteralPath $receiptPath)) { return @() }
    Assert-NoReparse $receiptPath
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.classId -ine $classId -or -not $receipt.PSObject.Properties['installedFiles']) { throw 'Invalid camera installation receipt identity or file list.' }
    $files = @()
    foreach ($file in @($receipt.installedFiles)) {
        if ([string]::IsNullOrWhiteSpace([string]$file)) { throw 'Invalid empty camera receipt path.' }
        $full = [IO.Path]::GetFullPath([string]$file)
        if (-not $full.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notin @('rilmo-virtual-camera64.dll','rilmo-virtual-camera32.dll')) { throw 'Invalid camera installation receipt path.' }
        Assert-NoReparse $full
        $files += $full
    }
    return @($files | Select-Object -Unique)
}
function Save-Receipt([string[]]$Files) {
    $text=[ordered]@{ deviceName=$deviceName; classId=$classId; installedAtUtc=[DateTime]::UtcNow.ToString('o'); installedFiles=@($Files|Select-Object -Unique); sourceProvenance=$provenancePath } | ConvertTo-Json -Depth 5
    $temporary=Join-Path $installRoot ('installation-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    $backup=Join-Path $installRoot ('installation-'+[Guid]::NewGuid().ToString('N')+'.bak')
    try {
        [IO.File]::WriteAllText($temporary,$text,[Text.UTF8Encoding]::new($false))
        if(Test-Path -LiteralPath $receiptPath){[IO.File]::Replace($temporary,$receiptPath,$backup)}
        else{[IO.File]::Move($temporary,$receiptPath)}
    } finally {
        foreach($transient in @($temporary,$backup)){if(Test-Path -LiteralPath $transient){try{Remove-Item -LiteralPath $transient -Force -ErrorAction Stop}catch{Write-Warning ('Receipt temporary file retained: '+$transient)}}}
    }
}
function Remove-OwnedCameraFiles([string[]]$Files) {
    $leftovers = @()
    foreach ($full in $Files) {
        if (Test-Path -LiteralPath $full) {
            try { Remove-Item -LiteralPath $full -Force -ErrorAction Stop }
            catch { $leftovers += $full; Write-Warning ('Device removed; file is still in use and was kept: '+$full) }
        }
    }
    # Only exact parents of receipt files are eligible. Never recurse or remove arbitrary folders.
    foreach ($directory in @($Files | ForEach-Object { Split-Path $_ -Parent } | Select-Object -Unique)) {
        if ($directory.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $directory -PathType Container)) {
            Assert-NoReparse $directory
            if (@(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory -Force }
        }
    }
    return @($leftovers)
}
Write-Output ($deviceName+' setup review')
Write-Output ('Action: '+$Action)
Write-Output ('Machine COM device identity: '+$classId+' (x64 and x86)')
Write-Output ('Files: '+$installRoot)
Write-Output 'Install adds only the Rilmo video device to DirectShow. It does not install a kernel driver or service, change the default camera, start capture, or change OBS registration.'
Write-Output 'Apps can select Rilmo Virtual Camera after setup. Starting/stopping transmission remains a separate Rilmo control. Administrator approval is required. Uninstall removes this device registration.'
Assert-NoReparse $installRoot
$previousReceiptFiles = @(Read-ValidatedReceiptFiles)
foreach ($registration in $registrations) {
    $registration.Previous = Get-RegisteredPath $registration.View
    if ($registration.Previous) {
        $previousFull = [IO.Path]::GetFullPath($registration.Previous)
        if (-not $previousFull.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'The Rilmo CLSID is already registered outside the Rilmo installation folder. Setup will not overwrite it.' }
        Assert-NoReparse $previousFull
        Write-Output ('Current '+$registration.Bits+'-bit component: '+$previousFull)
    }
}
if ($Action -eq 'Review') { return }
if (-not $AcknowledgeSystemDeviceChange) { throw 'Review the changes first, then explicitly pass -AcknowledgeSystemDeviceChange to Install or Uninstall.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this reviewed setup action as Administrator. No registration was changed.' }
if ($Action -eq 'Uninstall') {
    foreach ($registration in $registrations) { if ($registration.Previous) { Invoke-Registration $registration.Regsvr $registration.Previous $true } }
    # Keep binaries if any camera client still holds them. No recursive delete.
    $cleanupFiles = @($previousReceiptFiles + @($registrations | Where-Object { $_.Previous } | ForEach-Object { $_.Previous }) | Select-Object -Unique)
    $leftovers = @(Remove-OwnedCameraFiles $cleanupFiles)
    if ($leftovers.Count -gt 0) { Save-Receipt $leftovers; Write-Warning 'Cleanup receipt retained. Close camera clients, then run Uninstall again to remove the remaining files.' }
    elseif (Test-Path -LiteralPath $receiptPath) { Remove-Item -LiteralPath $receiptPath -Force }
    if ((Test-Path -LiteralPath $installRoot -PathType Container) -and @(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $installRoot -Force }
    Write-Output 'Rilmo Virtual Camera registration removed. Restart camera apps to refresh their device lists.'
    return
}
if (-not (Test-Path -LiteralPath $provenancePath)) { throw 'Camera source/build provenance is missing.' }
Assert-NoReparse $provenancePath
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
foreach ($registration in $registrations) {
    Assert-NoReparse $registration.Source
    if (-not (Test-Path -LiteralPath $registration.Source)) { throw ('Missing camera component: '+$registration.Source) }
    $expected = @($provenance.binaries | Where-Object { $_.name -eq ('rilmo-virtual-camera'+$registration.Bits+'.dll') })
    if ($expected.Count -ne 1 -or $expected[0].sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Camera provenance has no unique expected component hash.' }
    $actual = (Get-FileHash -LiteralPath $registration.Source -Algorithm SHA256).Hash
    if ($actual -ine $expected[0].sha256) { throw ('Camera component hash mismatch: '+$registration.Source) }
    $registration.Hash = $actual.ToLowerInvariant()
}
$version = $registrations[0].Hash.Substring(0,16)+'-'+$registrations[1].Hash.Substring(0,16)
$destination = Join-Path $installRoot $version
Assert-NoReparse $destination
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$installedFiles = @($previousReceiptFiles)
foreach ($registration in $registrations) {
    $registration.Target = Join-Path $destination ('rilmo-virtual-camera'+$registration.Bits+'.dll')
    Copy-Item -LiteralPath $registration.Source -Destination $registration.Target -Force
    $installedFiles += $registration.Target
}
$changed = @()
try {
    foreach ($registration in $registrations) { $changed += $registration; Invoke-Registration $registration.Regsvr $registration.Target $false }
    foreach ($registration in $registrations) { if ((Get-RegisteredPath $registration.View) -ine $registration.Target) { throw 'Registered camera path verification failed.' } }
    Save-Receipt $installedFiles
} catch {
    foreach ($registration in $changed) {
        try { Invoke-Registration $registration.Regsvr $registration.Target $true; if ($registration.Previous) { Invoke-Registration $registration.Regsvr $registration.Previous $false } } catch { Write-Warning ('Camera rollback needs manual review: '+$_.Exception.Message) }
    }
    throw
}
Write-Output 'Rilmo Virtual Camera is installed. Restart camera apps and select Rilmo Virtual Camera. Capture is still off until Start virtual camera is clicked in Rilmo.'
