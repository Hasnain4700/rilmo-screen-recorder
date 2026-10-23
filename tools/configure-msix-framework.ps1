# SPDX-License-Identifier: GPL-3.0-or-later
# Transform a newly staged MSIX payload; never install or modify a framework.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$app=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
if(-not $app.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'MSIX staging must stay inside the workspace.'}
if(-not(Test-Path -LiteralPath (Join-Path $app 'Rilmo Recorder.exe'))){throw 'A complete staged app is required.'}
# Reject linked parents as well as linked files before removing staged copies.
for($ancestor=$app;$ancestor;$ancestor=Split-Path $ancestor -Parent){
 if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked MSIX staging is not permitted.'}
}
$framework=[ordered]@{name='Microsoft.VCLibs.140.00.UWPDesktop';publisher='CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US';minVersion='14.0.33728.0';architecture='x64';delivery='Microsoft Store framework dependency'}
$omissions=@()
$receipts=@(
 @{path=(Join-Path $app 'runtime-staging.json');base=$app},
 @{path=(Join-Path $app 'runtime/browser/browser-runtime-inventory.json');base=(Join-Path $app 'runtime/browser')}
)
$plans=@()
foreach($record in $receipts){
 if(-not(Test-Path -LiteralPath $record.path)){throw 'Runtime staging receipt is missing.'}
 $inventory=Get-Content -LiteralPath $record.path -Raw|ConvertFrom-Json
 $kept=@()
 foreach($entry in $inventory.files){
  $name=[IO.Path]::GetFileName($entry.path)
  # .NET's vcruntime140_cor3.dll is a distinct runtime and is not removed.
  if($name -notmatch '^(msvcp140(?:_[a-z0-9_]+)?|vcruntime140(?:_1)?|concrt140)\.dll$'){$kept+=,$entry;continue}
  $file=[IO.Path]::GetFullPath((Join-Path $record.base $entry.path))
  if(-not $file.StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Framework omission escaped staging.'}
  if(-not(Test-Path -LiteralPath $file)-or(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256){throw 'Framework omission hash mismatch.'}
  if((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked framework DLL is not permitted.'}
  $signature=Get-AuthenticodeSignature -LiteralPath $file
  if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Only verified Microsoft C++ runtime copies may be omitted.'}
  $omissions+=,[ordered]@{path=[IO.Path]::GetRelativePath($app,$file).Replace('\','/');sha256=$entry.sha256;reason='Resolved through the declared Microsoft desktop framework'}
  $plans+=,$file
 }
 $inventory.files=$kept
 $inventory|Add-Member -NotePropertyName externalFrameworks -NotePropertyValue @($framework) -Force
 $record.inventory=$inventory
}
# Validate every omission before modifying any staged file.
foreach($file in $plans){Remove-Item -LiteralPath $file}
foreach($record in $receipts){$record.inventory|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $record.path -Encoding utf8}
[ordered]@{schemaVersion=1;framework=$framework;omittedAppLocalCopies=$omissions;sourceComplianceApproved=$false;packageGraphTested=$false}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $app 'msix-framework-receipt.json') -Encoding utf8
Write-Output ('Configured Store C++ framework; omitted '+$omissions.Count+' app-local copies. Package-graph execution still requires testing.')
