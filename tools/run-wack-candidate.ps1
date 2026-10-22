# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CandidateReceiptPath,[Parameter(Mandatory=$true)][string]$OutputDirectory,[ValidateSet('Help','Test')][string]$Action='Help')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$CandidateReceiptPath=[IO.Path]::GetFullPath($CandidateReceiptPath)
if(-not $OutputDirectory.StartsWith($root+'\artifacts\',[StringComparison]::OrdinalIgnoreCase) -or -not $CandidateReceiptPath.StartsWith($root+'\packaging\out\',[StringComparison]::OrdinalIgnoreCase)){throw 'Keep WACK inputs/output inside the workspace.'}
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'WACK needs an elevated interactive user process.'}
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$receipt=Get-Content -LiteralPath $CandidateReceiptPath -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $receipt.msi -Algorithm SHA256).Hash -ine $receipt.sha256 -or $receipt.version -ne '0.4.2'){throw 'Unexpected MSI input.'}
$appcert=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
if((Get-AuthenticodeSignature -LiteralPath $appcert).Status -ne 'Valid'){throw 'Installed WACK tool has no valid Authenticode signature.'}
$state=[ordered]@{schemaVersion=1;startedUtc=[DateTime]::UtcNow.ToString('o');action=$Action;candidateSha256=$receipt.sha256;wackPath=$appcert;wackSha256=(Get-FileHash -LiteralPath $appcert -Algorithm SHA256).Hash.ToLowerInvariant();reportGenerated=$false;wackPassed=$false;error=$null}
Start-Transcript -Path (Join-Path $OutputDirectory 'transcript.txt') -Force|Out-Null
$installer=New-Object -ComObject WindowsInstaller.Installer
function RunInstaller([string]$Operation,[string]$Name){
 $target=if($Operation -eq '/x'){$receipt.productCode}else{'"'+$receipt.msi+'"'}
 $log=Join-Path $OutputDirectory ($Name+'.msi.log')
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList @($Operation,$target,'/qn','/norestart','/L*v',('"'+$log+'"')) -WindowStyle Hidden -PassThru
 $null=$p.Handle
 if(-not $p.WaitForExit(180000) -or $p.ExitCode -ne 0){throw ('WACK installer preparation failed: '+$Name+'; exit '+$p.ExitCode)}
}
try{
 & $appcert '/?' 2>&1|Tee-Object -FilePath (Join-Path $OutputDirectory 'help.txt')
 $state.helpExitCode=$LASTEXITCODE
 if($Action -eq 'Test'){
  if(@(Get-Process -Name appcertui,'Rilmo Recorder' -ErrorAction SilentlyContinue).Count){throw 'Close WACK UI and Rilmo before this isolated WACK session.'}
  New-Item -ItemType Directory -Force -Path (Join-Path $OutputDirectory 'session')|Out-Null
  & $appcert reset -temppath (Join-Path $OutputDirectory 'session') 2>&1|Tee-Object -FilePath (Join-Path $OutputDirectory 'reset.stdout.txt')
  $state.resetExitCode=$LASTEXITCODE
  if($LASTEXITCODE -ne 0){throw 'WACK could not prepare a new testing session.'}
  if($installer.ProductState($receipt.productCode) -eq 5){RunInstaller '/x' 'remove-before-wack'}
  $report=Join-Path $OutputDirectory 'report.xml'
  if(Test-Path -LiteralPath $report){throw 'Preserve existing WACK report; select a new output directory.'}
  & $appcert test -apptype desktop -setuppath $receipt.msi -setupcommandline '/qn /norestart' -waittimeout 180 -appusage permachine -reportoutputpath $report -temppath (Join-Path $OutputDirectory 'session') 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'test.stdout.txt')
  $state.testExitCode=$LASTEXITCODE
  $state.reportGenerated=Test-Path -LiteralPath $report
  if($state.reportGenerated){$state.reportPath=$report;$state.reportSha256=(Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()}
  # Desktop mode can request a later finalizereport step. Read the real output;
  # report existence and a zero process exit are not a certification pass.
 }
}catch{$state.error=$_.Exception.ToString();Write-Output $state.error}
finally{
 if($Action -eq 'Test' -and $installer.ProductState($receipt.productCode) -ne 5){try{RunInstaller '/i' 'restore-app-after-wack';$state.appRestored=$true}catch{$state.appRestored=$false;$state.restoreError=$_.Exception.ToString()}}
 $state.completedUtc=[DateTime]::UtcNow.ToString('o')
 $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'invocation.json') -Encoding utf8
 Stop-Transcript|Out-Null
}
if($state.error){exit 1}
