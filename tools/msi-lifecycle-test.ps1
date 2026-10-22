# SPDX-License-Identifier: GPL-3.0-or-later
# Explicitly invoked elevated local test. Only Rilmo-owned products and CLSIDs.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
function PlainPath([string]$Path) {
 $cursor=[IO.Path]::GetFullPath($Path)
 while($cursor){if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Linked test path: $cursor"};$parent=Split-Path $cursor -Parent;if($parent -eq $cursor){break};$cursor=$parent}
}
$ConfigPath=[IO.Path]::GetFullPath($ConfigPath);PlainPath $ConfigPath
if(-not $ConfigPath.StartsWith($root+'\artifacts\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test configuration must be inside workspace/artifacts.'}
$cfg=Get-Content -LiteralPath $ConfigPath -Raw|ConvertFrom-Json
$work=Split-Path $ConfigPath -Parent
Start-Transcript -Path (Join-Path $work 'elevated-transcript.txt') -Force|Out-Null
$result=[ordered]@{schemaVersion=1;startedUtc=[DateTime]::UtcNow.ToString('o');passed=$false;cleanMachine=$false;os=[Environment]::OSVersion.VersionString;steps=@();error=$null;restoredApp=$null}
$classId='{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}'
$category='{860BB310-5D01-11D0-BD3B-00A0C911CE86}'
$install=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Rilmo Screen Recorder'
$legacy=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Rilmo Recorder'
$installer=New-Object -ComObject WindowsInstaller.Installer
function ProductInstalled([string]$Code){return $installer.ProductState($Code) -eq 5}
function Phase([string]$Value){[IO.File]::WriteAllText((Join-Path $work 'phase.txt'),$Value)}
function Step([string]$Name){$result.steps+=@([ordered]@{name=$Name;passed=$true;utc=[DateTime]::UtcNow.ToString('o')});Write-Output ('PASS '+$Name)}
function RunMsi([string]$Name,[string[]]$Arguments,[int]$Expected=0){
 $log=Join-Path $work ($Name+'.msi.log')
 $argumentList=@($Arguments)+@('/qn','/norestart','/L*v',('"'+$log+'"'))
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $argumentList -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(180000)){throw "MSI timed out at $Name; owned PID $($p.Id). No process was killed."}
 if($p.ExitCode -ne $Expected){throw "$Name exit $($p.ExitCode), expected $Expected. Inspect $log"}
 return $log
}
function ReadCamera([Microsoft.Win32.RegistryView]$View,[string]$Suffix=''){
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$View)
 try{$key=$base.OpenSubKey('Software\Classes\CLSID\'+$classId+'\InprocServer32');if($key){try{return [string]$key.GetValue('')}finally{$key.Dispose()}}}finally{$base.Dispose()}
 return $null
}
function VerifyInstalled($Receipt){
 if(-not(ProductInstalled $Receipt.productCode)){throw 'Expected MSI product is absent.'}
 $metadata=Get-Content -LiteralPath (Join-Path $install 'windows-installer.json') -Raw|ConvertFrom-Json
 if($metadata.productCode -ine $Receipt.productCode){throw 'Installed repair metadata points at the wrong product.'}
 foreach($bits in @('64','32')){
  $relative=if($bits -eq '64'){'rilmo-virtual-camera64.dll'}else{'virtual-camera\rilmo-virtual-camera32.dll'}
  $path=Join-Path $install $relative
  $entry=@($Receipt.files|Where-Object {$_.path -ieq $relative})
  if($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $entry[0].sha256){throw 'Installed camera hash mismatch.'}
  $view=if($bits -eq '64'){[Microsoft.Win32.RegistryView]::Registry64}else{[Microsoft.Win32.RegistryView]::Registry32}
  if((ReadCamera $view) -ine $path){throw "Incorrect $bits-bit COM registration."}
  $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
  try{$key=$base.OpenSubKey('Software\Classes\CLSID\'+$category+'\Instance\'+$classId);if(-not $key){throw 'Camera category registration is missing.'};try{if($key.GetValue('CLSID') -ine $classId -or $key.GetValue('FriendlyName') -cne 'Rilmo Virtual Camera' -or $key.GetValue('FilterData') -isnot [byte[]]){throw 'Camera category data mismatch.'}}finally{$key.Dispose()}}finally{$base.Dispose()}
 }
}
function Consumers([string]$Name){
 foreach($bits in @('64','32')){
  $dll=if($bits -eq '64'){Join-Path $install 'rilmo-virtual-camera64.dll'}else{Join-Path $install 'virtual-camera\rilmo-virtual-camera32.dll'}
  $exe=Join-Path $root ('native/virtual-camera/bin/camera-smoke'+$bits+'.exe')
  $text=& $exe $dll --registered 2>&1
  $text|Set-Content -LiteralPath (Join-Path $work ($Name+'-'+$bits+'.txt')) -Encoding utf8
  if($LASTEXITCODE -ne 0 -or ($text -join ' ') -notmatch 'MONIKER_BIND=true'){throw "Registered $bits-bit consumer failed."}
 }
}
$receipts=@();$legacyRemoved=$false
try{
 if(-not [Environment]::Is64BitProcess -or -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this test as an elevated 64-bit process.'}
 if(@(Get-Process -Name 'Rilmo Recorder' -ErrorAction SilentlyContinue).Count){throw 'Exit Rilmo from the tray before testing.'}
 PlainPath $install
 if((Test-Path -LiteralPath $install) -and @(Get-ChildItem -LiteralPath $install -Force).Count){throw 'The test installation folder is already occupied. Preserve it and inspect before retrying.'}
 foreach($entry in @($cfg.packages)){
  $receiptPath=[IO.Path]::GetFullPath($entry.receipt);PlainPath $receiptPath
  if(-not $receiptPath.StartsWith($root+'\packaging\out\',[StringComparison]::OrdinalIgnoreCase)){throw 'Package receipt escaped workspace packaging/out.'}
  if((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -ine $entry.sha256){throw 'Package receipt changed.'}
  $r=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json;PlainPath $r.msi
  if(-not $r.msi.StartsWith((Split-Path $receiptPath -Parent)+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $r.msi -Algorithm SHA256).Hash -ine $r.sha256){throw 'MSI hash/path mismatch.'}
  $receipts+=@($r)
 }
 if($receipts.Count -ne 3 -or $receipts[0].version -ne '0.4.2' -or $receipts[1].version -ne '0.4.3' -or $receipts[2].version -ne '0.4.4' -or -not $receipts[2].intentionalRollbackFailure){throw 'Expected baseline, upgrade and intentional rollback fixtures.'}
 if(@($receipts|Where-Object {ProductInstalled $_.productCode}).Count){throw 'A test product is already installed. Preserve it and inspect before retrying.'}
 foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){if(ReadCamera $view){throw 'An existing Rilmo camera must be removed with its original installer before testing.'}}
 $restore=[IO.Path]::GetFullPath($cfg.legacyInstaller);PlainPath $restore
 if(-not $restore.StartsWith($root+'\packaging\out\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $restore -Algorithm SHA256).Hash -ine $cfg.legacyInstallerSha256){throw 'Rollback-to-preview installer is not the reviewed artifact.'}
 $uninstaller=Join-Path $legacy 'Uninstall Rilmo.exe';PlainPath $uninstaller
 if((Get-FileHash -LiteralPath $uninstaller -Algorithm SHA256).Hash -ine $cfg.legacyUninstallerSha256){throw 'Legacy uninstaller changed.'}
 Phase 'removing-legacy-preview'
 $legacyRemoved=$true
 # NSIS must copy its uninstaller to TEMP so its installed EXE can be removed.
 # _?= disables that copy and would leave the self-deletion step locked.
 $p=Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -PassThru
 $null=$p.Handle
 if(-not $p.WaitForExit(120000) -or $p.ExitCode -ne 0){throw ('Earlier preview uninstaller exit: '+$p.ExitCode)}
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(Test-Path -LiteralPath $uninstaller){if($timer.Elapsed.TotalSeconds -gt 90){throw 'Earlier preview cleanup did not finish.'};Start-Sleep -Milliseconds 500}
 if(Test-Path -LiteralPath (Join-Path $legacy 'Rilmo Recorder.exe')){throw 'The earlier app executable survived uninstall.'}
 Step 'earlier-preview-uninstalled'
 Phase 'installing-baseline'
 RunMsi 'baseline-install' @('/i',('"'+$receipts[0].msi+'"'))|Out-Null
 VerifyInstalled $receipts[0];Consumers 'registered-baseline';Step 'baseline-install-and-registered-x64-x86-camera'
 Phase 'awaiting-standard-user-camera-test'
 $done=Join-Path $work 'standard-user-result.json';$timer=[Diagnostics.Stopwatch]::StartNew()
 while(-not(Test-Path -LiteralPath $done)){if($timer.Elapsed.TotalSeconds -gt 300){throw 'Standard-user camera test was not supplied within five minutes.'};Start-Sleep -Seconds 1}
 $standard=Get-Content -LiteralPath $done -Raw|ConvertFrom-Json
 if(-not $standard.passed){throw 'The non-elevated camera test failed.'}
 Step 'standard-user-libobs-to-registered-camera'
 Phase 'repairing-registration'
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
 try{$key=$base.OpenSubKey('Software\Classes\CLSID\'+$classId+'\InprocServer32',$true);try{$key.DeleteValue('',$true)}finally{$key.Dispose()}}finally{$base.Dispose()}
 RunMsi 'repair' @('/fomus',$receipts[0].productCode)|Out-Null
 VerifyInstalled $receipts[0];Consumers 'registered-after-repair';Step 'repair-restores-missing-camera-registration'
 Phase 'upgrading'
 RunMsi 'upgrade' @('/i',('"'+$receipts[1].msi+'"'))|Out-Null
 if(ProductInstalled $receipts[0].productCode){throw 'Old product survived major upgrade.'}
 VerifyInstalled $receipts[1];Consumers 'registered-after-upgrade';Step 'major-upgrade'
 Phase 'testing-rollback'
 $log=RunMsi 'intentional-rollback' @('/i',('"'+$receipts[2].msi+'"')) 1603
 if((Get-Content -LiteralPath $log -Raw) -notmatch 'Intentional test failure after file installation'){throw 'Upgrade failed for an unexpected reason; rollback was not exercised.'}
 if(ProductInstalled $receipts[2].productCode){throw 'Failed product remained installed.'}
 VerifyInstalled $receipts[1];Consumers 'registered-after-rollback';Step 'failed-upgrade-rolls-back-to-working-previous-version'
 Phase 'uninstalling'
 RunMsi 'uninstall' @('/x',$receipts[1].productCode)|Out-Null
 foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
  if(ReadCamera $view){throw 'COM registration survived uninstall.'}
  $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
  try{$key=$base.OpenSubKey('Software\Classes\CLSID\'+$category+'\Instance\'+$classId);if($key){$key.Dispose();throw 'Camera category survived uninstall.'}}finally{$base.Dispose()}
 }
 foreach($entry in $receipts[1].files){if(Test-Path -LiteralPath (Join-Path $install $entry.path)){throw ('Owned payload survived uninstall: '+$entry.path)}}
 Step 'uninstall-removes-owned-files-and-both-camera-registrations'
 Phase 'restoring-baseline'
 RunMsi 'final-baseline-install' @('/i',('"'+$receipts[0].msi+'"'))|Out-Null
 VerifyInstalled $receipts[0];$result.restoredApp='0.4.2 MSI development candidate';Step 'baseline-left-installed'
 $result.passed=$true
}catch{
 $result.error=$_.Exception.ToString();Write-Output $result.error
 # Restore a usable known app if test failure left no complete MSI installation.
 if($legacyRemoved){
  $present=@($receipts|Where-Object {ProductInstalled $_.productCode})
  if(-not $present.Count){
   try{RunMsi 'failure-restore-baseline' @('/i',('"'+$receipts[0].msi+'"'))|Out-Null;VerifyInstalled $receipts[0];$result.restoredApp='0.4.2 MSI development candidate'}
   catch{
    try{$p=Start-Process -FilePath $cfg.legacyInstaller -ArgumentList '/S' -WindowStyle Hidden -PassThru;if(-not $p.WaitForExit(180000) -or $p.ExitCode -ne 0){throw 'Earlier preview restore failed.'};$result.restoredApp='0.4.1 earlier preview'}catch{$result.restoredApp='Restore failed; inspect logs before further changes.'}
   }
  }else{$result.restoredApp='An MSI product remains installed; inspect the failed step before retrying.'}
 }
}finally{
 $result.completedUtc=[DateTime]::UtcNow.ToString('o')
 $result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $work 'result.json') -Encoding utf8
 Phase $(if($result.passed){'complete'}else{'failed'})
 Stop-Transcript|Out-Null
}
if(-not $result.passed){exit 1}
