# SPDX-License-Identifier: GPL-3.0-or-later
param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$ExpectedSha256,[Parameter(Mandatory)][string]$FrameworkPath,[Parameter(Mandatory)][string]$SignToolPath)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $PSVersionTable.PSEdition -ne 'Desktop'){throw 'Only disposable Windows CI under Windows PowerShell 5.1 may install test dependencies.'}
$added=$null
try{
 $framework=Get-AppxPackage -Name Microsoft.VCLibs.140.00.UWPDesktop|Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'14.0.33728.0' }|Select-Object -First 1
 if(-not $framework){
  if((Get-FileHash -LiteralPath $FrameworkPath -Algorithm SHA256).Hash -ine 'b56a9101f706f9d95f815f5b7fa6efbac972e86573d378b96a07cff5540c5961'){throw 'Unreviewed framework input.'}
  $signature=Get-AuthenticodeSignature -LiteralPath $FrameworkPath
  if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Invalid framework signature.'}
  Add-AppxPackage -Path $FrameworkPath
  $added=Get-AppxPackage -Name Microsoft.VCLibs.140.00.UWPDesktop|Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'14.0.33728.0' }|Select-Object -First 1
 }
 & (Join-Path $PSScriptRoot 'test-msix-native-local.ps1') -PackagePath $PackagePath -ExpectedSha256 $ExpectedSha256 -SignToolPath $SignToolPath -AcknowledgeTemporaryTestCertificate -NoAudio
}finally{
 if($added){Remove-AppxPackage -Package $added.PackageFullName -ErrorAction Stop}
}
