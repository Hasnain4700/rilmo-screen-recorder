# SPDX-License-Identifier: GPL-3.0-or-later
# Apply the camera-free Store scope to a new staging directory only.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$app=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
if(-not $app.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'MSIX staging must stay inside the workspace.'}
for($ancestor=$app;$ancestor;$ancestor=Split-Path $ancestor -Parent){
 if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked MSIX staging is not permitted.'}
}
$receiptPath=Join-Path $app 'runtime-staging.json'
if(-not(Test-Path -LiteralPath $receiptPath)){throw 'Runtime staging receipt is required.'}
$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
$excluded=@(
 'runtime/obs/data/obs-plugins/win-dshow/obs-virtualcam-module32.dll',
 'runtime/obs/data/obs-plugins/win-dshow/obs-virtualcam-module64.dll',
 'runtime/obs/data/obs-plugins/win-dshow/virtualcam-install.bat',
 'runtime/obs/data/obs-plugins/win-dshow/virtualcam-uninstall.bat'
)
$plan=@()
foreach($relative in $excluded){
 $entry=@($receipt.files|Where-Object { $_.path.Replace('\','/') -ieq $relative })
 $file=[IO.Path]::GetFullPath((Join-Path $app $relative))
 if(-not $file.StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Camera omission escaped staging.'}
 if(-not(Test-Path -LiteralPath $file)){
  if($entry.Count){throw 'Camera staging receipt refers to a missing file.'}
  continue
 }
 if($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry[0].sha256){throw 'Camera omission is not covered by the staging hash inventory.'}
 if((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked camera payload is not permitted.'}
 $plan+=,[ordered]@{path=$relative;sha256=$entry[0].sha256;file=$file;reason='Virtual camera is excluded from the first Store release'}
}
# Validate all hashes before removing files; preserve the acquired-source inventory.
foreach($item in $plan){Remove-Item -LiteralPath $item.file}
$receipt.files=@($receipt.files|Where-Object { $_.path.Replace('\','/') -notin $excluded })
$receipt|Add-Member -NotePropertyName storeExcludedFiles -NotePropertyValue @($plan|Select-Object path,sha256,reason) -Force
$receipt|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $receiptPath -Encoding utf8
& (Join-Path $PSScriptRoot 'configure-msix-framework.ps1') -AppDirectory $app
$forbidden=@(Get-ChildItem -LiteralPath $app -Recurse -File|Where-Object {
 $_.Name -match '^(obs-virtualcam-module(?:32|64)\.dll|virtualcam-(?:un)?install\.bat|rilmo-virtual-camera(?:32|64)\.dll)$'
})
if($forbidden.Count){throw 'A virtual-camera executable or registration script remains in the Store payload.'}
[ordered]@{schemaVersion=1;virtualCameraIncluded=$false;omittedCameraFiles=@($plan|Select-Object path,sha256,reason);originalAcquisitionInventoryPreserved=$true;sourceComplianceApproved=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $app 'msix-scope-receipt.json') -Encoding utf8
Write-Output ('Configured camera-free Store scope; omitted '+$plan.Count+' legacy OBS camera files. Physical webcam capture is retained.')
